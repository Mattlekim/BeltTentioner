using BeltTensionTest.WPF.ViewModels;
using System.Windows;
using System.Windows.Interop;
using System.ComponentModel;
using System.Threading;
using System;

namespace BeltTensionTest.WPF.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;
        private TestingWindow? _testingWindow;
        private DebugLogWindow? _debugWindow;
        private FlashNanoWindow? _flashWindow;
        private MotorSettingsWindow? _motorSettingsWindow;
        private TrayIcon? _trayIcon;
        private bool _isExitRequested = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            Loaded  += (_, _) => Shared.WpfMessageBridge.Attach(this);
            // Capture key presses when window has focus
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;

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

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var app = Application.Current;
            if (app == null) return;
            var vm = VM;
            if (vm == null || vm.AppSettings == null) return;

            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var mods = System.Windows.Input.Keyboard.Modifiers;

            // Build canonical gesture string for the current key event (e.g. "Ctrl+G" or "G")
            string BuildCurrentGesture()
            {
                string cur = string.Empty;
                if ((mods & System.Windows.Input.ModifierKeys.Control) != 0) cur += "Ctrl+";
                if ((mods & System.Windows.Input.ModifierKeys.Alt) != 0) cur += "Alt+";
                if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0) cur += "Shift+";
                if ((mods & System.Windows.Input.ModifierKeys.Windows) != 0) cur += "Win+";
                cur += key.ToString();
                return cur;
            }

            bool Match(string gestureStr)
            {
                if (string.IsNullOrWhiteSpace(gestureStr)) return false;
                try
                {
                    var stored = gestureStr.Trim();
                    var current = BuildCurrentGesture();
                    return string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            // Build current gesture once
            var currentGesture = BuildCurrentGesture();

            // Debug output and brief UI feedback to help diagnose mapping issues
            try { System.Diagnostics.Debug.WriteLine($"Hotkey pressed: {currentGesture}"); } catch { }
            vm.MenuStateText = $"Key: {currentGesture}";

            // Toggle fan
            if (Match(vm.AppSettings.ToggleFanKey))
            {
                vm.EnableForCar = !vm.EnableForCar;
                vm.MenuStateText = $"Hotkey triggered: ToggleFan ({currentGesture})";
                e.Handled = true;
                return;
            }

            // Increase resting power
            if (Match(vm.AppSettings.IncreaseWindRestingKey))
            {
                vm.WindRestingPower = vm.WindRestingPower + 1; // increment by 1
                vm.MenuStateText = $"Hotkey triggered: IncreaseRest ({currentGesture})";
                e.Handled = true;
                return;
            }

            // Decrease resting power
            if (Match(vm.AppSettings.DecreaseWindRestingKey))
            {
                vm.WindRestingPower = vm.WindRestingPower - 1; // decrement by 1
                vm.MenuStateText = $"Hotkey triggered: DecreaseRest ({currentGesture})";
                e.Handled = true;
                return;
            }
        }

        private void OpenPreferences_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(VM);
            dlg.Owner = this;
            dlg.ShowDialog();
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

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // If minimize-to-taskbar is enabled, cancel close and hide window to tray
            if (VM?.AppSettings?.MinimizeToTaskbarOnClose == true && !_isExitRequested)
            {
                e.Cancel = true;
                Hide();
                ShowTrayIcon();
                return;
            }

            // Dispose viewmodel when actually exiting
            try { VM?.Dispose(); } catch { }
            RemoveTrayIcon();
        }

        private void ShowTrayIcon()
        {
            if (_trayIcon != null) return;
            _trayIcon = new TrayIcon("Belt Tensioner",
                onOpen: () => Application.Current.Dispatcher.Invoke(() =>
                {
                    Show(); WindowState = WindowState.Normal; Activate();
                }),
                onExit: () => Application.Current.Dispatcher.Invoke(() =>
                {
                    _isExitRequested = true; Close();
                }));
        }

        private void RemoveTrayIcon()
        {
            if (_trayIcon == null) return;
            try { _trayIcon.Dispose(); } catch { }
            _trayIcon = null;
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
