using System;
using System.Windows;
using System.Windows.Controls;

namespace BeltTensionTest.WPF.Views
{
    public partial class KeyBindingControl : UserControl
    {
        public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
            nameof(Gesture), typeof(string), typeof(KeyBindingControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IsGlobalProperty = DependencyProperty.Register(
            nameof(IsGlobal), typeof(bool), typeof(KeyBindingControl), new PropertyMetadata(false));

        public string Gesture
        {
            get => (string)GetValue(GestureProperty);
            set => SetValue(GestureProperty, value);
        }

        public bool IsGlobal
        {
            get => (bool)GetValue(IsGlobalProperty);
            set => SetValue(IsGlobalProperty, value);
        }

        public event EventHandler? GestureChanged;
        public event EventHandler? GlobalChanged;

        // True while the capture dialog is open. Used by the main window to ignore live gamepad
        // presses while the user is assigning a binding.
        public static bool CapturingActive { get; set; }

        public KeyBindingControl()
        {
            InitializeComponent();

            // hookup UI to dependency properties
            UpdateDisplay();
            PART_Global.IsChecked = IsGlobal;

            this.Loaded += (_, _) =>
            {
                UpdateDisplay();
                PART_Global.IsChecked = IsGlobal;
            };

            PART_Global.Checked += (s, e) => { IsGlobal = true; GlobalChanged?.Invoke(this, EventArgs.Empty); };
            PART_Global.Unchecked += (s, e) => { IsGlobal = false; GlobalChanged?.Invoke(this, EventArgs.Empty); };

            // Clicking the box opens the capture dialog.
            PART_Box.PreviewMouseLeftButtonDown += (s, e) => { OpenCaptureDialog(); e.Handled = true; };
        }

        // Show the current binding prominently, or a muted "Not set" placeholder when empty.
        private void UpdateDisplay()
        {
            if (string.IsNullOrWhiteSpace(Gesture))
            {
                PART_Text.Text = "Not set";
                PART_Text.FontStyle = FontStyles.Italic;
                PART_Text.Foreground = (System.Windows.Media.Brush)TryFindResource("BorderBrush") ?? System.Windows.Media.Brushes.Gray;
            }
            else
            {
                PART_Text.Text = Gesture;
                PART_Text.FontStyle = FontStyles.Normal;
                PART_Text.Foreground = (System.Windows.Media.Brush)TryFindResource("AccentBlueBrush") ?? System.Windows.Media.Brushes.DodgerBlue;
            }
        }

        private void OpenCaptureDialog()
        {
            var dlg = new CaptureBindingWindow(Gesture)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true && dlg.CapturedGesture != null)
            {
                Gesture = dlg.CapturedGesture;
                UpdateDisplay();
                GestureChanged?.Invoke(this, EventArgs.Empty);

                // F13–F24 come from a Stream Deck (or macro device), which is typically pressed
                // while the sim has focus — a non-global binding would never fire. Turning the
                // checkbox on raises GlobalChanged, which persists and registers the hotkey.
                if (IsStreamDeckKey(Gesture) && !IsGlobal)
                    PART_Global.IsChecked = true;
            }
        }

        // True for bare F13–F24 gestures — keys only sendable by devices like a Stream Deck.
        private static bool IsStreamDeckKey(string gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture) || gesture.Contains('+')) return false;
            var g = gesture.Trim();
            return g.Length >= 2 && (g[0] == 'F' || g[0] == 'f')
                && int.TryParse(g.Substring(1), out int n) && n >= 13 && n <= 24;
        }

        private void PART_Clear_Click(object sender, RoutedEventArgs e)
        {
            Gesture = string.Empty;
            UpdateDisplay();
            GestureChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
