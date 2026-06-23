using System.Windows;

using System.Windows;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow() => InitializeComponent();
        private void OK_Click(object s, RoutedEventArgs e) => Close();

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}

