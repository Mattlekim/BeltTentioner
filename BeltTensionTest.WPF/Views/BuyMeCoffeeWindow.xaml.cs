using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace BeltTensionTest.WPF.Views
{
    public partial class BuyMeCoffeeWindow : Window
    {
        // Replace with your actual BuyMeACoffee page
        private const string CoffeeUrl = "https://www.buymeacoffee.com/yourname";

        public BuyMeCoffeeWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure close is performed on the UI thread
                if (!Dispatcher.CheckAccess())
                    Dispatcher.Invoke(() => Close());
                else
                    Close();
            }
            catch { }
        }

        private void Buy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = CoffeeUrl,
                    UseShellExecute = true
                };
                try { Process.Start(psi); } catch { }
            }
            catch { }
        }
    }
}
