using System.Windows;

namespace BeltTensionTest.WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global unhandled exception handler
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    $"Unhandled error:\n{ex.Exception.Message}",
                    "Belt Tensioner Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
