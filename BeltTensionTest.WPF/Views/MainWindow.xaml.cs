using BeltTensionTest.WPF.ViewModels;
using System.Windows;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;
        private TestingWindow? _testingWindow;
        private DebugLogWindow? _debugWindow;
        private FlashNanoWindow? _flashWindow;
        private MotorSettingsWindow? _motorSettingsWindow;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            Loaded  += (_, _) => Shared.WpfMessageBridge.Attach(this);
            Closing += (_, _) => VM.Dispose();
        }

        private void OpenMotorSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_motorSettingsWindow == null || !_motorSettingsWindow.IsLoaded)
            {
                _motorSettingsWindow = new MotorSettingsWindow(DataContext);
                _motorSettingsWindow.Owner = this;
                _motorSettingsWindow.Show();
            }
            else
            {
                _motorSettingsWindow.Activate();
            }
        }

        private void OpenTestingWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_testingWindow == null || !_testingWindow.IsLoaded)
            {
                _testingWindow = new TestingWindow(VM);
                _testingWindow.Owner = this;
                _testingWindow.Show();
            }
            else
            {
                _testingWindow.Activate();
            }
        }

        private void OpenDebugLog_Click(object sender, RoutedEventArgs e)
        {
            if (_debugWindow == null || !_debugWindow.IsLoaded)
            {
                _debugWindow = new DebugLogWindow(MainViewModel.Device);
                _debugWindow.Owner = this;
                _debugWindow.Show();
            }
            else
            {
                _debugWindow.Activate();
            }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        private void OpenFlashNano_Click(object sender, RoutedEventArgs e)
        {
            if (_flashWindow == null || !_flashWindow.IsLoaded)
            {
                _flashWindow = new FlashNanoWindow();
                _flashWindow.Owner = this;
                _flashWindow.Show();
                MainViewModel.Device.ManualDisconnect();
                // Update UI status indicators to reflect manual disconnect
                VM.DeviceIsOn = false;
                VM.DeviceStatusText = "Device disconnected";
                VM.ControlsEnabled = false;
                VM.ConnectButtonEnabled = true;

            }
            else
            {
                _flashWindow.Activate();
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        private void SendWind_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // read from view model (percentage 0-100)
                var pct = VM.WindPowerPercentage;
                var val = (int)System.Math.Round((pct / 100.0) * 255.0);
                MainViewModel.Device.SendWindPower(val);
            }
            catch { }
        }
    }
}
