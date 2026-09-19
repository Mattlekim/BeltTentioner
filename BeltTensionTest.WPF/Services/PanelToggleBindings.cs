using System;
using System.Collections.Generic;
using System.Linq;
using BeltTensionTest.WPF.Views;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Controller / wheel button bindings that toggle an overlay panel's
    /// visibility. A binding is either a single control ("Button5") or a
    /// combo ("Button5+Button12": hold the first, press the second), using
    /// <see cref="GamepadService"/> control names. Bindings are keyed by
    /// panel name and live in the given dictionary (the app settings), so
    /// the owner just saves settings when <see cref="Changed"/> fires.
    ///
    /// Assignment happens in the VR overlay's edit mode: a panel's Bind
    /// button starts <see cref="BeginCapture"/>; the user presses one button,
    /// or holds one and presses another, and the binding is recorded once
    /// everything is released. Clicking Bind again while it waits clears the
    /// panel's binding instead.
    /// </summary>
    public sealed class PanelToggleBindings : IDisposable
    {
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

        private readonly Dictionary<string, string> _bindings;
        private readonly Func<IEnumerable<OverlayRenderTarget>> _panels;
        private readonly Action _invalidateCanvas;

        // Capture in progress: the panel being bound and the distinct
        // controls pressed so far, in press order.
        private OverlayRenderTarget? _capturing;
        private readonly List<string> _capturePresses = new();
        private DateTime _captureStarted;

        // State a panel had before a binding hid it, restored when shown again
        // (so a collapsed panel comes back collapsed).
        private readonly Dictionary<OverlayRenderTarget, OverlayTargetState> _stateBeforeHide = new();

        /// <summary>Raised after a binding is added, changed or cleared.</summary>
        public event Action? Changed;

        /// <summary>Raised with a short status line when a binding is captured or fires.</summary>
        public event Action<string>? Status;

        public PanelToggleBindings(Dictionary<string, string> bindings,
                                   Func<IEnumerable<OverlayRenderTarget>> panels, Action invalidateCanvas)
        {
            _bindings = bindings;
            _panels = panels;
            _invalidateCanvas = invalidateCanvas;
            GamepadService.Instance.ButtonPressed += OnButtonPressed;
            GamepadService.Instance.Polled += OnPolled;
            GamepadService.Instance.Start();
        }

        /// <summary>Edit-mode Bind button text and highlight for a panel.</summary>
        public (string Label, bool Active) LabelFor(OverlayRenderTarget rt)
        {
            if (rt == _capturing) return ("Press button(s)", true);
            return _bindings.TryGetValue(rt.Name, out var b) && !string.IsNullOrEmpty(b)
                ? ("Bind: " + Short(b), true)
                : ("Bind button", false);
        }

        /// <summary>Bind button clicked: start capturing for the panel, or clear it if it was already waiting.</summary>
        public void BeginCapture(OverlayRenderTarget rt)
        {
            if (_capturing == rt)
            {
                EndCapture();
                if (_bindings.Remove(rt.Name))
                {
                    Status?.Invoke($"{rt.Name}: toggle binding cleared");
                    Changed?.Invoke();
                }
                return;
            }

            EndCapture();
            _capturing = rt;
            _capturePresses.Clear();
            _captureStarted = DateTime.UtcNow;
            // Keep the main window's own button actions from firing while
            // the user is pressing buttons to assign.
            KeyBindingControl.CapturingActive = true;
            rt.Invalidate();
        }

        private void EndCapture()
        {
            if (_capturing == null) return;
            var rt = _capturing;
            _capturing = null;
            _capturePresses.Clear();
            KeyBindingControl.CapturingActive = false;
            rt.Invalidate();
        }

        private void OnButtonPressed(string name)
        {
            if (_capturing != null)
            {
                if (!_capturePresses.Contains(name, StringComparer.OrdinalIgnoreCase))
                    _capturePresses.Add(name);
                return;
            }
            if (KeyBindingControl.CapturingActive) return; // settings window is assigning a key

            var pad = GamepadService.Instance;
            var parsed = _bindings
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .Select(kv => (Panel: kv.Key, Parts: kv.Value.Split('+')))
                .Where(b => string.Equals(b.Parts[^1], name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A combo whose modifier is held wins over a plain binding of the
            // same button, so "hold 5, press 12" doesn't also fire "12".
            var hits = parsed.Where(b => b.Parts.Length == 2 && pad.IsHeld(b.Parts[0])).ToList();
            if (hits.Count == 0)
                hits = parsed.Where(b => b.Parts.Length == 1).ToList();

            foreach (var hit in hits)
            {
                // Edit-mode-only panels (e.g. the Display panel) have no
                // show/hide of their own, so a stale binding can't strand one.
                var rt = _panels().FirstOrDefault(p => p.Name == hit.Panel && !p.EditModeOnly);
                if (rt == null) continue;
                Toggle(rt);
                Status?.Invoke($"{rt.Name} {(rt.Visible ? "shown" : "hidden")} ({Short(string.Join("+", hit.Parts))})");
            }
        }

        // After every poll: finish a capture once the user has pressed
        // something and let go of everything (or give up after a while).
        private void OnPolled()
        {
            if (_capturing == null) return;
            if (DateTime.UtcNow - _captureStarted > CaptureTimeout)
            {
                EndCapture();
                return;
            }
            if (_capturePresses.Count == 0 || GamepadService.Instance.HeldButtons.Count > 0) return;

            // One control = single binding; several = the first held is the
            // modifier and the last pressed is the trigger.
            string binding = _capturePresses.Count == 1
                ? _capturePresses[0]
                : $"{_capturePresses[0]}+{_capturePresses[^1]}";
            var rt = _capturing;
            _bindings[rt.Name] = binding;
            EndCapture();
            Status?.Invoke($"{rt.Name}: toggle bound to {Short(binding)}");
            Changed?.Invoke();
        }

        private void Toggle(OverlayRenderTarget rt)
        {
            if (rt.Visible)
            {
                _stateBeforeHide[rt] = rt.State;
                rt.State = OverlayTargetState.Hidden;
            }
            else
            {
                rt.State = _stateBeforeHide.TryGetValue(rt, out var s) ? s : OverlayTargetState.Visible;
            }
            _invalidateCanvas();
        }

        /// <summary>Compact form for the button label: "Button5+Button12" → "B5+B12".</summary>
        private static string Short(string binding) =>
            binding.Replace("Button", "B", StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            EndCapture();
            GamepadService.Instance.ButtonPressed -= OnButtonPressed;
            GamepadService.Instance.Polled -= OnPolled;
        }
    }
}
