using BeltTensionTest.WPF.ViewModels;
using System.Windows;
using System.Windows.Interop;
using BeltTensionTest.WPF.Services;
using System.ComponentModel;
using System.Threading;
using System;
using System.Windows.Controls;

namespace BeltTensionTest.WPF.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel VM => (MainViewModel)DataContext;
        private readonly SettingsService _localSettings = new SettingsService();
        private TestingWindow? _testingWindow;
        private DebugLogWindow? _debugWindow;
        private FlashNanoWindow? _flashWindow;
        private MotorSettingsWindow? _motorSettingsWindow;
        private OverlayWindow? _overlayWindow;
        private BuyMeCoffeeWindow? _buyWindow;
        private TrayIcon? _trayIcon;
        private bool _isExitRequested = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            // Initialize global hotkey manager and register any persisted global bindings
            try
            {
                GlobalHotKeyManager.Initialize(this);
                var vm = VM;
                if (vm?.AppSettings != null)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(vm.AppSettings.ToggleFanKey) && vm.AppSettings.ToggleFanGlobal)
                        {
                            GlobalHotKeyManager.Register("ToggleFan", vm.AppSettings.ToggleFanKey, () =>
                            {
                                try { vm.EnableForCar = !vm.EnableForCar; vm.MenuStateText = $"Hotkey triggered: ToggleFan ({vm.AppSettings.ToggleFanKey})"; } catch { }
                            });
                        }
                        if (!string.IsNullOrWhiteSpace(vm.AppSettings.IncreaseWindRestingKey) && vm.AppSettings.IncreaseWindRestingGlobal)
                        {
                            GlobalHotKeyManager.Register("IncreaseRest", vm.AppSettings.IncreaseWindRestingKey, () =>
                            {
                                try { vm.WindRestingPower = vm.WindRestingPower + 1; vm.MenuStateText = $"Hotkey triggered: IncreaseRest ({vm.AppSettings.IncreaseWindRestingKey})"; } catch { }
                            });
                        }
                        if (!string.IsNullOrWhiteSpace(vm.AppSettings.DecreaseWindRestingKey) && vm.AppSettings.DecreaseWindRestingGlobal)
                        {
                            GlobalHotKeyManager.Register("DecreaseRest", vm.AppSettings.DecreaseWindRestingKey, () =>
                            {
                                try { vm.WindRestingPower = vm.WindRestingPower - 1; vm.MenuStateText = $"Hotkey triggered: DecreaseRest ({vm.AppSettings.DecreaseWindRestingKey})"; } catch { }
                            });
                        }

                        // Overlay navigation bindings
                        RegisterNavHotkey("NavUp", vm.AppSettings.NavUpKey, vm.AppSettings.NavUpGlobal, Services.Overlays.OverlayNavAction.Up);
                        RegisterNavHotkey("NavDown", vm.AppSettings.NavDownKey, vm.AppSettings.NavDownGlobal, Services.Overlays.OverlayNavAction.Down);
                        RegisterNavHotkey("NavIncrease", vm.AppSettings.NavIncreaseKey, vm.AppSettings.NavIncreaseGlobal, Services.Overlays.OverlayNavAction.Increase);
                        RegisterNavHotkey("NavDecrease", vm.AppSettings.NavDecreaseKey, vm.AppSettings.NavDecreaseGlobal, Services.Overlays.OverlayNavAction.Decrease);
                    }
                    catch { }
                }
            }
            catch { }

            Loaded  += (_, _) => Shared.WpfMessageBridge.Attach(this);
            Loaded += MainWindow_Loaded;
            // Capture key presses when window has focus
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            // Gamepad bindings are polled globally (independent of window focus)
            GlobalHotKeyManager_GamepadInit();
            Closing += MainWindow_Closing;

            // Save size/position when closing
            Closing += (_, _) => SaveWindowBounds();

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

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                var s = VM?.AppSettings;
                if (s != null)
                {
                    // Apply saved size
                    if (s.WindowWidth > 0) Width = s.WindowWidth;
                    if (s.WindowHeight > 0) Height = s.WindowHeight;

                    // Apply saved position if present
                    if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
                    {
                        Left = s.WindowLeft;
                        Top = s.WindowTop;
                    }

                    // Apply saved state if valid
                    if (!string.IsNullOrWhiteSpace(s.WindowState) && Enum.TryParse<WindowState>(s.WindowState, out var ws))
                    {
                        WindowState = ws;
                    }
                }
            }
            catch { }

            // Subscribe to changes so we can persist them
            SizeChanged += MainWindow_SizeChanged;
            LocationChanged += MainWindow_LocationChanged;
            StateChanged += MainWindow_StateChanged;

            VM?.LoadCarSettings(VM?.CarNameDisplay);
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Only persist when in normal state (not minimized/maximized)
            if (WindowState == WindowState.Normal)
                SaveWindowBounds();
        }

        private void MainWindow_LocationChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
                SaveWindowBounds();
        }

        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            // Persist restore bounds / state when the window state changes
            SaveWindowBounds();
        }

        private void SaveWindowBounds()
        {
            try
            {
                var s = VM?.AppSettings;
                if (s == null) return;

                if (WindowState == WindowState.Normal)
                {
                    s.WindowWidth = Width;
                    s.WindowHeight = Height;
                    s.WindowLeft = Left;
                    s.WindowTop = Top;
                    s.WindowState = WindowState.ToString();
                }
                else
                {
                    // Use RestoreBounds to capture the normal window size/position
                    var rb = RestoreBounds;
                    s.WindowWidth = rb.Width;
                    s.WindowHeight = rb.Height;
                    s.WindowLeft = rb.Left;
                    s.WindowTop = rb.Top;
                    s.WindowState = WindowState.ToString();
                }

                _localSettings.Save(s);
            }
            catch { }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                try { this.DragMove(); } catch { }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
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

            // Overlay navigation
            if (Match(vm.AppSettings.NavUpKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Up);
                vm.MenuStateText = $"Hotkey triggered: NavUp ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavDownKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Down);
                vm.MenuStateText = $"Hotkey triggered: NavDown ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavIncreaseKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Increase);
                vm.MenuStateText = $"Hotkey triggered: NavIncrease ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavDecreaseKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Decrease);
                vm.MenuStateText = $"Hotkey triggered: NavDecrease ({currentGesture})";
                e.Handled = true;
                return;
            }
        }

        private void RegisterNavHotkey(string id, string gesture, bool isGlobal, Services.Overlays.OverlayNavAction action)
        {
            if (!isGlobal || string.IsNullOrWhiteSpace(gesture)) return;
            GlobalHotKeyManager.Register(id, gesture, () =>
            {
                try { Services.Overlays.OverlayNavigation.Raise(action); VM!.MenuStateText = $"Hotkey triggered: {id} ({gesture})"; } catch { }
            });
        }

        // Start polling gamepads and route pad-button presses to the bound actions.
        private void GlobalHotKeyManager_GamepadInit()
        {
            GamepadService.Instance.ButtonPressed += MainWindow_GamepadButtonPressed;
            GamepadService.Instance.Start();
        }

        private void MainWindow_GamepadButtonPressed(string name)
        {
            // Ignore live presses while a binding is being assigned in the settings window.
            if (KeyBindingControl.CapturingActive) return;

            var vm = VM;
            if (vm?.AppSettings == null) return;

            var gesture = "Pad:" + name;
            bool Match(string g) => !string.IsNullOrWhiteSpace(g)
                && string.Equals(g.Trim(), gesture, StringComparison.OrdinalIgnoreCase);

            if (Match(vm.AppSettings.ToggleFanKey))
            {
                vm.EnableForCar = !vm.EnableForCar;
                vm.MenuStateText = $"Gamepad triggered: ToggleFan ({gesture})";
            }
            else if (Match(vm.AppSettings.IncreaseWindRestingKey))
            {
                vm.WindRestingPower = vm.WindRestingPower + 1;
                vm.MenuStateText = $"Gamepad triggered: IncreaseRest ({gesture})";
            }
            else if (Match(vm.AppSettings.DecreaseWindRestingKey))
            {
                vm.WindRestingPower = vm.WindRestingPower - 1;
                vm.MenuStateText = $"Gamepad triggered: DecreaseRest ({gesture})";
            }
            else if (Match(vm.AppSettings.NavUpKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Up);
                vm.MenuStateText = $"Gamepad triggered: NavUp ({gesture})";
            }
            else if (Match(vm.AppSettings.NavDownKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Down);
                vm.MenuStateText = $"Gamepad triggered: NavDown ({gesture})";
            }
            else if (Match(vm.AppSettings.NavIncreaseKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Increase);
                vm.MenuStateText = $"Gamepad triggered: NavIncrease ({gesture})";
            }
            else if (Match(vm.AppSettings.NavDecreaseKey))
            {
                Services.Overlays.OverlayNavigation.Raise(Services.Overlays.OverlayNavAction.Decrease);
                vm.MenuStateText = $"Gamepad triggered: NavDecrease ({gesture})";
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

        private void OpenOverlayWindow_Click(object sender, RoutedEventArgs e)
        {
            if (_overlayWindow == null || !_overlayWindow.IsLoaded)
            {
                _overlayWindow = new OverlayWindow(VM);
                _overlayWindow.Owner = this;
                _overlayWindow.Show();
            }
            else
            {
                _overlayWindow.Activate();
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

        private void OpenBuyMeACoffee_Click(object sender, RoutedEventArgs e)
        {
            if (_buyWindow == null || !_buyWindow.IsLoaded)
            {
                _buyWindow = new BuyMeCoffeeWindow();
                _buyWindow.Owner = this;
                _buyWindow.ShowDialog();
            }
            else
            {
                _buyWindow.Activate();
            }
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

        // Called when the selected tab changes; if the Wind tab is selected, request the VM to refresh the graph
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (VM == null) return;
                if (sender is TabControl tc)
                {
                    if (tc.SelectedItem is TabItem ti)
                    {
                        var header = ti.Header?.ToString() ?? string.Empty;
                        if (string.Equals(header, "Wind", StringComparison.OrdinalIgnoreCase))
                        {
                            int w = (int)(img_WindGraph?.ActualWidth ?? 300);
                            int h = (int)(img_WindGraph?.ActualHeight ?? 140);
                            if (w <= 0) w = 300;
                            if (h <= 0) h = 140;
                            VM.RefreshWindGraph(w, h);
                        }
                    }
                }
            }
            catch { }
        }

        // Called when the wind Image control is loaded; refresh the graph to match the control size
        private void WindImage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VM == null) return;
                int w = (int)(img_WindGraph?.ActualWidth ?? 300);
                int h = (int)(img_WindGraph?.ActualHeight ?? 140);
                if (w <= 0) w = 300;
                if (h <= 0) h = 140;
                VM.RefreshWindGraph(w, h);
            }
            catch { }
        }
    }
}
