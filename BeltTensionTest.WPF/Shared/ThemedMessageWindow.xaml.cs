using System.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Shared
{
    public partial class ThemedMessageWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public ThemedMessageWindow(string text, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            InitializeComponent();
            Title = caption ?? string.Empty;
            TextMessage.Text = text ?? string.Empty;

            // Create buttons based on requested button set
            ButtonsPanel.Children.Clear();

            void AddButton(string content, MessageBoxResult res, bool isDefault = false, bool isCancel = false)
            {
                var b = new Button { Content = content, Margin = new Thickness(6, 0, 0, 0), MinWidth = 80 };
                b.Click += (_, __) => { Result = res; DialogResult = true; Close(); };
                if (isDefault) b.IsDefault = true;
                if (isCancel) b.IsCancel = true;
                // style to match dark theme
                b.Background = System.Windows.Media.Brushes.DimGray;
                b.Foreground = System.Windows.Media.Brushes.White;
                ButtonsPanel.Children.Add(b);
            }

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                    AddButton("No", MessageBoxResult.No, isCancel: true);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("Yes", MessageBoxResult.Yes);
                    AddButton("No", MessageBoxResult.No);
                    AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                    break;
                default:
                    AddButton("OK", MessageBoxResult.OK, isDefault: true);
                    break;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Treat as cancel/close
            Result = MessageBoxResult.None;
            DialogResult = false;
            Close();
        }
    }
}
