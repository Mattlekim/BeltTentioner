using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        public KeyBindingControl()
        {
            InitializeComponent();

            // hookup UI to dependency properties
            PART_Text.Text = Gesture;
            PART_Global.IsChecked = IsGlobal;

            // Keep DP and UI in sync
            this.Loaded += (_, _) =>
            {
                PART_Text.Text = Gesture;
                PART_Global.IsChecked = IsGlobal;
            };

            PART_Global.Checked += (s, e) => { IsGlobal = true; GlobalChanged?.Invoke(this, EventArgs.Empty); };
            PART_Global.Unchecked += (s, e) => { IsGlobal = false; GlobalChanged?.Invoke(this, EventArgs.Empty); };

            // Capture logic: allow user to click text box to arm and then press keys
            PART_Text.PreviewMouseLeftButtonDown += (s, e) =>
            {
                try
                {
                    PART_Text.Focusable = true;
                    PART_Text.IsTabStop = true;
                    PART_Text.Focus();
                    PART_Text.SelectAll();
                    PART_Text.Tag = true; // ready
                    e.Handled = true;
                }
                catch { }
            };

            PART_Text.GotFocus += (s, e) =>
            {
                try
                {
                    PART_Text.Tag = true;
                    PART_Text.BorderThickness = new Thickness(2);
                    PART_Text.BorderBrush = (System.Windows.Media.Brush)Application.Current.TryFindResource("AccentBlueBrush") ?? System.Windows.Media.Brushes.Gold;
                }
                catch { }
            };

            PART_Text.PreviewKeyDown += PART_Text_PreviewKeyDown;
        }

        private void PART_Text_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
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

            var ready = PART_Text.Tag as bool? ?? false;
            if (!ready)
            {
                e.Handled = true; return;
            }

            PART_Text.Text = s;
            Gesture = s;
            PART_Text.Tag = false;

            // restore visuals
            try
            {
                PART_Text.BorderThickness = new Thickness(1);
                PART_Text.BorderBrush = (System.Windows.Media.Brush)Application.Current.TryFindResource("BorderBrush") ?? System.Windows.Media.Brushes.Gray;
                PART_Text.Background = (System.Windows.Media.Brush)Application.Current.TryFindResource("BgLightBrush") ?? System.Windows.Media.Brushes.Transparent;
            }
            catch { }

            GestureChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
