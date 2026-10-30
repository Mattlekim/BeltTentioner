using System;
using BeltTensionTest.WPF.Views;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// A controller / wheel button that recenters the VR overlay, assigned from
    /// the in-VR HUD Settings panel: <see cref="BeginCapture"/> waits for one
    /// button press and records its <see cref="GamepadService"/> name
    /// ("Button5"). The owner persists <see cref="Binding"/> when
    /// <see cref="Changed"/> fires. Works alongside the keyboard recenter key
    /// in Preferences.
    /// </summary>
    public sealed class RecenterBinding : IDisposable
    {
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(15);

        private readonly Action _recenter;
        private string? _pressed;
        private DateTime _captureStarted;

        public RecenterBinding(string binding, Action recenter)
        {
            Binding = binding ?? string.Empty;
            _recenter = recenter;
            GamepadService.Instance.ButtonPressed += OnButtonPressed;
            GamepadService.Instance.Polled += OnPolled;
            GamepadService.Instance.Start();
        }

        /// <summary>The bound control name, or empty when none is.</summary>
        public string Binding { get; private set; }

        /// <summary>True while waiting for the user to press the button to bind.</summary>
        public bool Capturing { get; private set; }

        /// <summary>Raised after the binding is set or cleared, and when a capture starts or gives up.</summary>
        public event Action? Changed;

        public void BeginCapture()
        {
            Capturing = true;
            _pressed = null;
            _captureStarted = DateTime.UtcNow;
            // Keep the main window's own button actions from firing while
            // the user is pressing buttons to assign.
            KeyBindingControl.CapturingActive = true;
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
            _pressed = null;
            KeyBindingControl.CapturingActive = false;
        }

        private void OnButtonPressed(string name)
        {
            if (Capturing)
            {
                _pressed ??= name;
                return;
            }
            if (KeyBindingControl.CapturingActive) return; // settings window is assigning a key
            if (Binding.Length > 0 && string.Equals(Binding, name, StringComparison.OrdinalIgnoreCase))
                _recenter();
        }

        // Finish once the button is let go (or give up after a while).
        private void OnPolled()
        {
            if (!Capturing) return;
            if (DateTime.UtcNow - _captureStarted > CaptureTimeout)
            {
                EndCapture();
                Changed?.Invoke();
                return;
            }
            if (_pressed == null || GamepadService.Instance.HeldButtons.Count > 0) return;
            Binding = _pressed;
            EndCapture();
            Changed?.Invoke();
        }

        /// <summary>Compact form for display: "Button5" becomes "B5".</summary>
        public static string Short(string binding) =>
            binding.Replace("Button", "B", StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            EndCapture();
            GamepadService.Instance.ButtonPressed -= OnButtonPressed;
            GamepadService.Instance.Polled -= OnPolled;
        }
    }
}
