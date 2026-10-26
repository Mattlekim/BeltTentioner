using BeltTensionTest.WPF.ViewModels;
using System.Windows;
using System.Windows.Interop;
using BeltTensionTest.WPF.Services;
using System.ComponentModel;
using System.Threading;
using System;
using System.Diagnostics;
using System.IO;
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
                        RegisterNavHotkey("NavUp", vm.AppSettings.NavUpKey, vm.AppSettings.NavUpGlobal, MonoXR.Client.OverlayNavAction.Up);
                        RegisterNavHotkey("NavDown", vm.AppSettings.NavDownKey, vm.AppSettings.NavDownGlobal, MonoXR.Client.OverlayNavAction.Down);
                        RegisterNavHotkey("NavIncrease", vm.AppSettings.NavIncreaseKey, vm.AppSettings.NavIncreaseGlobal, MonoXR.Client.OverlayNavAction.Increase);
                        RegisterNavHotkey("NavDecrease", vm.AppSettings.NavDecreaseKey, vm.AppSettings.NavDecreaseGlobal, MonoXR.Client.OverlayNavAction.Decrease);
                        RegisterNavHotkey("NavNextControl", vm.AppSettings.NavNextControlKey, vm.AppSettings.NavNextControlGlobal, MonoXR.Client.OverlayNavAction.NextControl);
                        RegisterNavHotkey("NavPrevControl", vm.AppSettings.NavPrevControlKey, vm.AppSettings.NavPrevControlGlobal, MonoXR.Client.OverlayNavAction.PreviousControl);

                        // Always global: recentering happens with the headset on and the sim focused.
                        if (!string.IsNullOrWhiteSpace(vm.AppSettings.RecenterOverlayKey))
                        {
                            GlobalHotKeyManager.Register("RecenterOverlay", vm.AppSettings.RecenterOverlayKey, () =>
                            {
                                try { vm.MenuStateText = RecenterOverlay("Hotkey", vm.AppSettings.RecenterOverlayKey); } catch { }
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }

            Loaded  += (_, _) => Shared.WpfMessageBridge.Attach(this);
            Loaded += MainWindow_Loaded;

            // Feature tabs follow Preferences → Features: keep a visible tab selected.
            VM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.BeltFeatureEnabled) or nameof(MainViewModel.WindFeatureEnabled))
                    // After the tab Visibility bindings have updated.
                    Dispatcher.BeginInvoke(new Action(SelectVisibleFeatureTab), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            Loaded += (_, _) => SelectVisibleFeatureTab();
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

            // Preferences → OpenXR → Enable OpenXR overlay.
            try { ApplyOverlayEnabledSetting(); } catch { }
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
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Up);
                vm.MenuStateText = $"Hotkey triggered: NavUp ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavDownKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Down);
                vm.MenuStateText = $"Hotkey triggered: NavDown ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavIncreaseKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Increase);
                vm.MenuStateText = $"Hotkey triggered: NavIncrease ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavDecreaseKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Decrease);
                vm.MenuStateText = $"Hotkey triggered: NavDecrease ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavNextControlKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.NextControl);
                vm.MenuStateText = $"Hotkey triggered: NavNextControl ({currentGesture})";
                e.Handled = true;
                return;
            }
            if (Match(vm.AppSettings.NavPrevControlKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.PreviousControl);
                vm.MenuStateText = $"Hotkey triggered: NavPrevControl ({currentGesture})";
                e.Handled = true;
                return;
            }

            // Recenter the VR overlay on the headset
            if (Match(vm.AppSettings.RecenterOverlayKey))
            {
                vm.MenuStateText = RecenterOverlay("Hotkey", currentGesture);
                e.Handled = true;
                return;
            }
        }

        /// <summary>
        /// Recenter the open OpenXR overlay (same as its Recenter button) and
        /// return the status text for the trigger that fired it.
        /// </summary>
        internal static string RecenterOverlay(string source, string gesture)
        {
            return OverlayWindow.RequestRecenter()
                ? $"{source} triggered: RecenterOverlay ({gesture})"
                : $"{source} triggered: RecenterOverlay ({gesture}) — OpenXR overlay is not running";
        }

        private void RegisterNavHotkey(string id, string gesture, bool isGlobal, MonoXR.Client.OverlayNavAction action)
        {
            if (!isGlobal || string.IsNullOrWhiteSpace(gesture)) return;
            GlobalHotKeyManager.Register(id, gesture, () =>
            {
                try { MonoXR.Client.OverlayNavigation.Raise(action); VM!.MenuStateText = $"Hotkey triggered: {id} ({gesture})"; } catch { }
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
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Up);
                vm.MenuStateText = $"Gamepad triggered: NavUp ({gesture})";
            }
            else if (Match(vm.AppSettings.NavDownKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Down);
                vm.MenuStateText = $"Gamepad triggered: NavDown ({gesture})";
            }
            else if (Match(vm.AppSettings.NavIncreaseKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Increase);
                vm.MenuStateText = $"Gamepad triggered: NavIncrease ({gesture})";
            }
            else if (Match(vm.AppSettings.NavDecreaseKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.Decrease);
                vm.MenuStateText = $"Gamepad triggered: NavDecrease ({gesture})";
            }
            else if (Match(vm.AppSettings.NavNextControlKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.NextControl);
                vm.MenuStateText = $"Gamepad triggered: NavNextControl ({gesture})";
            }
            else if (Match(vm.AppSettings.NavPrevControlKey))
            {
                MonoXR.Client.OverlayNavigation.Raise(MonoXR.Client.OverlayNavAction.PreviousControl);
                vm.MenuStateText = $"Gamepad triggered: NavPrevControl ({gesture})";
            }
            else if (Match(vm.AppSettings.RecenterOverlayKey))
            {
                vm.MenuStateText = RecenterOverlay("Gamepad", gesture);
            }
        }

        private void OpenPreferences_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(VM);
            dlg.Owner = this;
            dlg.ShowDialog();
            try { ApplyOverlayEnabledSetting(); } catch { }
        }

        private void OpenCarProfiles_Click(object sender, RoutedEventArgs e)
        {
            new CarProfilesWindow(VM) { Owner = this }.ShowDialog();
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
            OpenOverlayWindow();
        }

        private void OpenOverlayWindow()
        {
            var window = EnsureOverlayWindow();
            if (!window.IsVisible) window.Show();
            window.Activate();
        }

        /// <summary>The overlay window (possibly hidden or never shown), created on demand.</summary>
        private OverlayWindow EnsureOverlayWindow()
        {
            if (_overlayWindow == null)
            {
                _overlayWindow = new OverlayWindow(VM);
                _overlayWindow.Owner = this;
            }
            return _overlayWindow;
        }

        /// <summary>
        /// Preferences → OpenXR → Enable OpenXR overlay starts/stops the overlay.
        /// Its window is only for editing: showing or closing it (closing just
        /// hides it) leaves the overlay's running state alone.
        /// </summary>
        private void ApplyOverlayEnabledSetting()
        {
            bool enabled = VM?.AppSettings?.EnableOpenXrOverlay == true;
            if (enabled) EnsureOverlayWindow().SetRunning(true);
            else _overlayWindow?.SetRunning(false);
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

        private void InstallSimHubPlugin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? sourceDll = FindSimHubPluginDll();
                if (sourceDll == null)
                {
                    MessageBox.Show(this,
                        "Could not find BeltTentioner.dll.\nMake sure the SimHub plugin has been built (Sim_Hub_Plugin project) or is shipped alongside this application.",
                        "SimHub Plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string? simHubDir = FindSimHubDirectory();
                if (simHubDir == null)
                {
                    if (MessageBox.Show(this,
                        "SimHub doesn't appear to be installed on this PC.\n\nInstall SimHub first (simhubdash.com), then run this again.\n\nIf SimHub is installed in a custom location, choose Yes to browse for its folder.",
                        "SimHub Plugin", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                    var dlg = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "Select your SimHub installation folder"
                    };
                    if (dlg.ShowDialog(this) != true) return;
                    if (!IsSimHubDirectory(dlg.FolderName))
                    {
                        MessageBox.Show(this, $"{SimHubExeName} was not found in that folder, so it doesn't look like a SimHub installation.",
                            "SimHub Plugin", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    simHubDir = dlg.FolderName;
                }

                if (Process.GetProcessesByName("SimHubWPF").Length > 0)
                {
                    var result = MessageBox.Show(this,
                        "SimHub appears to be running. Close it before installing so the plugin file isn't locked.\n\nContinue anyway?",
                        "SimHub Plugin", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes) return;
                }

                string targetPath = Path.Combine(simHubDir, Path.GetFileName(sourceDll));
                try
                {
                    File.Copy(sourceDll, targetPath, overwrite: true);
                }
                catch (UnauthorizedAccessException)
                {
                    // SimHub usually lives in Program Files – retry the copy elevated
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c copy /y \"{sourceDll}\" \"{targetPath}\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                    if (proc == null || proc.ExitCode != 0 || !File.Exists(targetPath))
                        throw new IOException("Elevated copy failed.");
                }

                string simHubExe = Path.Combine(simHubDir, SimHubExeName);
                bool canLaunch = File.Exists(simHubExe) && Process.GetProcessesByName("SimHubWPF").Length == 0;
                string installed = $"Plugin installed to:\n{targetPath}\n\nEnable the \"BeltTentioner\" plugin when SimHub prompts (or via Settings > Plugins).";

                if (!canLaunch)
                {
                    MessageBox.Show(this, installed + "\n\nStart SimHub to use it.",
                        "SimHub Plugin", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (MessageBox.Show(this, installed + "\n\nLaunch SimHub now?",
                    "SimHub Plugin", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = simHubExe,
                        WorkingDirectory = simHubDir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to install the SimHub plugin:\n{ex.Message}",
                    "SimHub Plugin", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string? FindSimHubPluginDll()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "simhub", "BeltTentioner.dll"),
                Path.Combine(AppContext.BaseDirectory, "BeltTentioner.dll"),
            };

            // Dev environment: walk up from the build output folder to the repo root
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                candidates.Add(Path.Combine(dir.FullName, "BeltTensionTest.WPF", "simhub", "BeltTentioner.dll"));
                candidates.Add(Path.Combine(dir.FullName, "Sim_Hub_Plugin", "bin", "Release", "BeltTentioner.dll"));
                candidates.Add(Path.Combine(dir.FullName, "Sim_Hub_Plugin", "bin", "Debug", "BeltTentioner.dll"));
                dir = dir.Parent;
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private const string SimHubExeName = "SimHubWPF.exe";

        private static bool IsSimHubDirectory(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, SimHubExeName));

        private static string? FindSimHubDirectory()
        {
            // 1. SimHub is running right now - its process tells us exactly where it lives
            foreach (var proc in Process.GetProcessesByName("SimHubWPF"))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    if (IsSimHubDirectory(dir)) return dir;
                }
                catch { /* access denied - fall through to the other checks */ }
                finally { proc.Dispose(); }
            }

            // 2. Uninstall registry entries (covers custom install locations)
            foreach (var (hive, view) in new[]
            {
                (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
                (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32),
                (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Default),
            })
            {
                try
                {
                    using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null) continue;
                    foreach (string name in uninstall.GetSubKeyNames())
                    {
                        using var key = uninstall.OpenSubKey(name);
                        string? display = key?.GetValue("DisplayName") as string;
                        if (display == null || !display.Contains("SimHub", StringComparison.OrdinalIgnoreCase)) continue;

                        string? loc = (key!.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (IsSimHubDirectory(loc)) return loc;

                        string? icon = (key.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"');
                        string? iconDir = string.IsNullOrEmpty(icon) ? null : Path.GetDirectoryName(icon);
                        if (IsSimHubDirectory(iconDir)) return iconDir;
                    }
                }
                catch { /* unreadable hive - ignore */ }
            }

            // 3. Default install folders
            var candidates = new[]
            {
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\SimHub"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\SimHub"),
                Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Programs\SimHub"),
            };
            return candidates.FirstOrDefault(IsSimHubDirectory);
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// Exit (bypassing minimize-to-tray) and start a fresh instance once
        /// this one has shut down, so the belt's serial port is free again.
        /// </summary>
        internal void RestartApp()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Application.Current.Exit += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); } catch { }
            };
            _isExitRequested = true;
            Close();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// Exit (bypassing minimize-to-tray) after an update installer has been
        /// started; the installer waits for this process to end, then relaunches.
        /// </summary>
        internal void ExitForUpdate()
        {
            _isExitRequested = true;
            Close();
            Application.Current.Shutdown();
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

            // Really exiting: stop a background (hidden) overlay too — it would
            // otherwise just hide again and keep the process alive.
            try { _overlayWindow?.Shutdown(); } catch { }

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

        // A collapsed tab keeps showing its content while selected, so move the
        // selection onto an enabled feature's tab whenever the features change.
        private void SelectVisibleFeatureTab()
        {
            if (tabs_Main.SelectedItem is TabItem current && current.Visibility == Visibility.Visible) return;
            foreach (var item in tabs_Main.Items)
            {
                if (item is TabItem ti && ti.Visibility == Visibility.Visible)
                {
                    tabs_Main.SelectedItem = ti;
                    return;
                }
            }
            tabs_Main.SelectedIndex = -1;
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
