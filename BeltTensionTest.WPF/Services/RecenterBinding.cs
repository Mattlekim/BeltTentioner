using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BeltTensionTest.WPF.Views;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// A controller / wheel binding that triggers an action (recentring the VR overlay, dropping a brake
    /// marker), assigned from the in-VR HUD Settings panel: <see cref="BeginCapture"/> waits for the user
    /// to press what they want to bind and records it. The owner persists <see cref="Binding"/> when
    /// <see cref="Changed"/> fires. Works alongside the keyboard keys in Preferences.
    ///
    /// A binding is one or more parts joined by "+", all of which must be satisfied at once; it fires when
    /// the last of them is. A part is either
    ///   * a button, by its <see cref="GamepadService"/> name ("Button5", "A", "PovUp"), held down, or
    ///   * an analog position: an axis with a direction and a threshold, 0..1 ("AxisZ>0.30" is a pedal
    ///     pressed past 30%, "LeftStickX&lt;0.20" a stick pushed left).
    /// So "Button5", "Button5+Button12", "AxisZ>0.30" and "Button5+AxisZ>0.30" (a button and a pedal) are
    /// all bindings. While capturing, hold every button, and push every axis a good way, that should be
    /// part of it, then let go of all of them.
    /// </summary>
    public sealed class RecenterBinding : IDisposable
    {
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);
        private const int MaxParts = 4;

        // An axis counts as part of a binding once it has moved this far from where it was when the capture
        // started; the threshold is then placed this far from that start (so it is crossed well before the
        // end of the travel, and an idle axis' jitter never gets near it).
        private const double AxisMoveToCapture = 0.4;
        private const double AxisThresholdFromRest = 0.3;

        private readonly Action _recenter;
        private DateTime _captureStarted;
        private readonly List<string> _captured = new();
        private Dictionary<string, double> _baseline = new();
        private bool _wasDown = true; // true so a control already held when the app starts does not fire

        public RecenterBinding(string binding, Action recenter)
        {
            Binding = binding ?? string.Empty;
            _recenter = recenter;
            GamepadService.Instance.Polled += OnPolled;
            GamepadService.Instance.Start();
        }

        /// <summary>The bound controls ("Button5", "Button5+Button12", "Button5+AxisZ>0.30"), or empty when none.</summary>
        public string Binding { get; private set; }

        /// <summary>True while waiting for the user to press what they want to bind.</summary>
        public bool Capturing { get; private set; }

        /// <summary>Raised after the binding is set or cleared, and when a capture starts or gives up.</summary>
        public event Action? Changed;

        public void BeginCapture()
        {
            Capturing = true;
            _captured.Clear();
            _baseline = new Dictionary<string, double>(GamepadService.Instance.Axes, StringComparer.OrdinalIgnoreCase);
            _captureStarted = DateTime.UtcNow;
            // Keep the main window's own button actions from firing while
            // the user is pressing buttons to assign.
            KeyBindingControl.CapturingActive = true;
            Changed?.Invoke();
        }

        /// <summary>Give up a capture in progress, keeping the binding as it was.</summary>
        public void Cancel()
        {
            if (!Capturing) return;
            EndCapture();
            Changed?.Invoke();
        }

        public void Clear()
        {
            EndCapture();
            Binding = string.Empty;
            Changed?.Invoke();
        }

        private void EndCapture()
        {
            if (!Capturing) return;
            Capturing = false;
            _captured.Clear();
            _wasDown = true; // whatever is still held now must not fire when capture ends
            KeyBindingControl.CapturingActive = false;
        }

        private void OnPolled()
        {
            var pad = GamepadService.Instance;
            if (Capturing) { Capture(pad); return; }

            if (KeyBindingControl.CapturingActive) { _wasDown = true; return; } // settings window is assigning a key
            if (Binding.Length == 0) return;

            // A combo of buttons counts when they were all down within a short time of each other, so two quick
            // taps that never quite overlap between two polls still fire it.
            var parts = Binding.Split('+');
            bool down = parts.All(part => Satisfied(pad, part, parts.Length > 1 ? ComboGraceMs : 0));
            if (down && !_wasDown)
            {
                try { _recenter(); } catch { }
            }
            _wasDown = down;
        }

        // Collect what is held or pushed, and finish once all of it has been let go (or give up after a while).
        private void Capture(GamepadService pad)
        {
            if (DateTime.UtcNow - _captureStarted > CaptureTimeout)
            {
                EndCapture();
                Changed?.Invoke();
                return;
            }

            foreach (var name in pad.HeldButtons)
                if (_captured.Count < MaxParts && !_captured.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _captured.Add(name);

            foreach (var (name, value) in pad.Axes)
            {
                if (_captured.Count >= MaxParts) break;
                if (!_baseline.TryGetValue(name, out double rest)) continue; // plugged in during the capture
                if (_captured.Any(p => TryParseAxis(p, out var axis, out _, out _) && string.Equals(axis, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                double moved = value - rest;
                if (Math.Abs(moved) < AxisMoveToCapture) continue;
                bool up = moved > 0;
                double threshold = Math.Clamp(rest + (up ? AxisThresholdFromRest : -AxisThresholdFromRest), 0.05, 0.95);
                _captured.Add(name + (up ? ">" : "<") + threshold.ToString("0.00", CultureInfo.InvariantCulture));
            }

            // Still down: a button held, or an axis still past its threshold.
            if (_captured.Count == 0 || _captured.Any(p => Satisfied(pad, p))) return;

            Binding = string.Join("+", _captured);
            EndCapture();
            Changed?.Invoke();
        }

        private const int ComboGraceMs = 150;

        private static bool Satisfied(GamepadService pad, string part, int graceMs = 0)
        {
            if (!TryParseAxis(part, out var axis, out bool greater, out double threshold))
                return graceMs > 0 ? pad.HeldWithin(part, graceMs) : pad.IsHeld(part);
            return pad.TryGetAxis(axis, out double value) && (greater ? value > threshold : value < threshold);
        }

        /// <summary>Split an analog part such as "AxisZ>0.30" into its axis, direction and threshold.</summary>
        private static bool TryParseAxis(string part, out string axis, out bool greater, out double threshold)
        {
            axis = string.Empty; greater = true; threshold = 0;
            int at = part.IndexOfAny(new[] { '>', '<' });
            if (at <= 0) return false;
            axis = part[..at];
            greater = part[at] == '>';
            return double.TryParse(part[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out threshold);
        }

        /// <summary>Compact form for display: "Button5+AxisZ>0.30" becomes "B5+AxisZ>30%".</summary>
        public static string Short(string binding) =>
            string.Join("+", binding.Split('+').Select(part =>
                TryParseAxis(part, out var axis, out bool greater, out double threshold)
                    ? $"{axis}{(greater ? '>' : '<')}{threshold * 100:0}%"
                    : part.Replace("Button", "B", StringComparison.OrdinalIgnoreCase)));

        public void Dispose()
        {
            EndCapture();
            GamepadService.Instance.Polled -= OnPolled;
        }
    }
}
