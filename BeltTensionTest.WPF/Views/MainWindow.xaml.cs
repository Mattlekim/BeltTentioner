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

            MainViewModel.Device.OnConnencted += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    bnt_Connect.Content = "Disconnect";
                    bnt_Connect.Background = System.Windows.Media.Brushes.DarkRed;
                    bnt_Connect.IsEnabled = true;
                });
            };

            MainViewModel.Device.OnDisconnection += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    bnt_Connect.Content = "Connect";
                    bnt_Connect.Background = System.Windows.Media.Brushes.DarkGreen;
                    bnt_Connect.IsEnabled = true;
                });
            };
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

        private void bnt_Connect_Click(object sender, RoutedEventArgs e)
        {
            if (MainViewModel.Device.IsConnected)
            {
                Task.Run(() =>
                {
                    Thread.Sleep(100);  
                    MainViewModel.Device.ManualDisconnect();
                });
                
                return;
            }

            bnt_Connect.IsEnabled = false;
        }
    }
}
