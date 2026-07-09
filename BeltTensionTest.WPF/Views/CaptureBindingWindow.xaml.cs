using System;
using System.Windows;
using System.Windows.Input;
using BeltTensionTest.WPF.Services;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Modal dialog that captures a single keyboard gesture or gamepad button and returns it
    /// via <see cref="CapturedGesture"/>. Shares the "Pad:" naming used by the rest of the
    /// binding system so keyboard and gamepad bindings are interchangeable.
    /// </summary>
    public partial class CaptureBindingWindow : Window
    {
        /// <summary>The gesture confirmed by the user (null if cancelled or nothing captured).</summary>
        public string? CapturedGesture { get; private set; }

        private string? _pending;

        public CaptureBindingWindow(string? currentGesture)
        {
            InitializeComponent();

            _pending = string.IsNullOrWhiteSpace(currentGesture) ? null : currentGesture;

            // F13–F24 don't exist on physical keyboards, so a Stream Deck "Hotkey" action
            // sending one can never conflict with the sim. Offer them as a direct pick since
            // they can't be typed into the capture area.
            for (int f = 13; f <= 24; f++)
                cmb_StreamDeckKey.Items.Add("F" + f);

            UpdateDisplay();

            // Suppress live action-triggering (main window) while assigning.
            KeyBindingControl.CapturingActive = true;

            PreviewKeyDown += CaptureBindingWindow_PreviewKeyDown;

            GamepadService.Instance.Start();
            GamepadService.Instance.ButtonPressed += OnGamepadButton;

            Closed += (_, _) =>
            {
                GamepadService.Instance.ButtonPressed -= OnGamepadButton;
                KeyBindingControl.CapturingActive = false;
            };
        }

        private void UpdateDisplay()
        {
            txt_Pending.Text = string.IsNullOrWhiteSpace(_pending) ? "None" : _pending;
        }

        private void CaptureBindingWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
            {
                e.Handled = true; return;
            }

            var mods = Keyboard.Modifiers;
            string s = string.Empty;
            if ((mods & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((mods & ModifierKeys.Alt) != 0) s += "Alt+";
            if ((mods & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((mods & ModifierKeys.Windows) != 0) s += "Win+";
            s += key.ToString();

            _pending = s;
            UpdateDisplay();
            e.Handled = true;
        }

        private void StreamDeckKey_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmb_StreamDeckKey.SelectedItem is string key)
            {
                _pending = key;
                UpdateDisplay();
            }
        }

        private void OnGamepadButton(string name)
        {
            // Raised on the UI thread by the polling timer.
            _pending = "Pad:" + name;
            UpdateDisplay();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            CapturedGesture = _pending;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
