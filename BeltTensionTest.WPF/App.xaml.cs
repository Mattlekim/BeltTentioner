using System.Windows;

namespace BeltTensionTest.WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Register the bundled MonoXR OpenXR layer (HKCU, best-effort) so
            // the VR overlay works from an unzipped copy with no install step.
            Services.MonoXRLayerInstaller.EnsureRegistered();

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
