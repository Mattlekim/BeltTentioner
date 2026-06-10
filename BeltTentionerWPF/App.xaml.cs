using System.Windows;

namespace BeltTentionerWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CrashLogger.Initialize();
        }
    }
}
