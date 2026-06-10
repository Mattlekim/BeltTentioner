using System.Windows;

namespace BeltTensionTest.WPF.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow() => InitializeComponent();
        private void OK_Click(object s, RoutedEventArgs e) => Close();
    }
}
