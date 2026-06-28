using BeltTensionTest.WPF.ViewModels;
using BeltTensionTest.WPF.Services;
using System.Windows;
using Microsoft.Win32;
using System;
using System.Windows.Input;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Media;

namespace BeltTensionTest.WPF.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly SettingsService _settingsSvc = new();

        public SettingsWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            // Initialize controls from current app settings
            chk_AutoConnect.IsChecked = _vm.AppSettings?.AutoConnectOnStartup ?? false;
            chk_StartWithWindows.IsChecked = _vm.AppSettings?.StartWithWindows ?? false;
            chk_MinimizeToTaskbar.IsChecked = _vm.AppSettings?.MinimizeToTaskbarOnClose ?? false;

            // Initialize keybinding boxes
            if (_vm.AppSettings != null)
            {
                kb_ToggleFan.Gesture = _vm.AppSettings.ToggleFanKey ?? string.Empty;
                kb_ToggleFan.IsGlobal = _vm.AppSettings.ToggleFanGlobal;
                kb_IncreaseWindRest.Gesture = _vm.AppSettings.IncreaseWindRestingKey ?? string.Empty;
                kb_IncreaseWindRest.IsGlobal = _vm.AppSettings.IncreaseWindRestingGlobal;
                kb_DecreaseWindRest.Gesture = _vm.AppSettings.DecreaseWindRestingKey ?? string.Empty;
                kb_DecreaseWindRest.IsGlobal = _vm.AppSettings.DecreaseWindRestingGlobal;
            }

            // Also load on-disk settings to ensure mappings saved from previous runs are shown
            try
            {
                var disk = _settingsSvc.Load();
                if (disk != null)
                {
                    if (!string.IsNullOrWhiteSpace(disk.ToggleFanKey)) kb_ToggleFan.Gesture = disk.ToggleFanKey;
                    kb_ToggleFan.IsGlobal = disk.ToggleFanGlobal;
                    if (!string.IsNullOrWhiteSpace(disk.IncreaseWindRestingKey)) kb_IncreaseWindRest.Gesture = disk.IncreaseWindRestingKey;
                    kb_IncreaseWindRest.IsGlobal = disk.IncreaseWindRestingGlobal;
                    if (!string.IsNullOrWhiteSpace(disk.DecreaseWindRestingKey)) kb_DecreaseWindRest.Gesture = disk.DecreaseWindRestingKey;
                    kb_DecreaseWindRest.IsGlobal = disk.DecreaseWindRestingGlobal;
                }
            }
            catch { }

            // Ensure no textbox is marked ready for capture by default; require explicit selection
            // Wire up keybinding control change handlers so mappings are saved immediately when changed
            try
            {
                kb_ToggleFan.GestureChanged += Kb_GestureChanged;
                kb_IncreaseWindRest.GestureChanged += Kb_GestureChanged;
                kb_DecreaseWindRest.GestureChanged += Kb_GestureChanged;

                kb_ToggleFan.GlobalChanged += Kb_GlobalChanged;
                kb_IncreaseWindRest.GlobalChanged += Kb_GlobalChanged;
                kb_DecreaseWindRest.GlobalChanged += Kb_GlobalChanged;
            }
            catch { }

            // Ensure initial focus is not on a mapping box; focus OK button so user must click/select a box to map
            Loaded += (_, _) =>
            {
                try
                {
                    // Defer focus setting until layout is complete to override any control-level focus
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            btn_Ok.Focus();
                            System.Windows.Input.Keyboard.Focus(btn_Ok);
                            System.Windows.Input.FocusManager.SetFocusedElement(this, btn_Ok);
                        }
                        catch { }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                catch { }
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.AppSettings == null) return;

            var autoConnect = chk_AutoConnect.IsChecked == true;
            var startWithWindows = chk_StartWithWindows.IsChecked == true;
            var minimizeToTaskbar = chk_MinimizeToTaskbar.IsChecked == true;

            // Apply auto-connect setting through the viewmodel so side-effects (save/connect) occur
            try { _vm.AutoConnect = autoConnect; } catch { _vm.AppSettings.AutoConnectOnStartup = autoConnect; }

            _vm.AppSettings.StartWithWindows = startWithWindows;
            _vm.AppSettings.MinimizeToTaskbarOnClose = minimizeToTaskbar;
            // Save keybindings
            _vm.AppSettings.ToggleFanKey = kb_ToggleFan.Gesture ?? string.Empty;
            _vm.AppSettings.ToggleFanGlobal = kb_ToggleFan.IsGlobal;
            _vm.AppSettings.IncreaseWindRestingKey = kb_IncreaseWindRest.Gesture ?? string.Empty;
            _vm.AppSettings.IncreaseWindRestingGlobal = kb_IncreaseWindRest.IsGlobal;
            _vm.AppSettings.DecreaseWindRestingKey = kb_DecreaseWindRest.Gesture ?? string.Empty;
            _vm.AppSettings.DecreaseWindRestingGlobal = kb_DecreaseWindRest.IsGlobal;

            // Persist
            _settingsSvc.Save(_vm.AppSettings);

            // Diagnostic: verify file was written and contains the expected keys
            try
            {
                var path = _settingsSvc.FilePath;
                if (System.IO.File.Exists(path))
                {
                    var txt = System.IO.File.ReadAllText(path);
                    // quick check: file should contain at least one of the key names
                    if (!txt.Contains("ToggleFanKey") && !txt.Contains("IncreaseWindRestingKey") && !txt.Contains("DecreaseWindRestingKey"))
                    {
                        MessageBox.Show(this, $"Settings file written but does not contain keybindings.\nPath: {path}\nContents:\n{txt}", "Settings Save Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(this, $"Settings file was not written. Path: {path}", "Settings Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch { }

            // Verify saved by reloading file and updating in-memory settings if possible
            try
            {
                var loaded = _settingsSvc.Load();
                if (loaded != null && _vm.AppSettings != null)
                {
                    // copy values into the existing AppSettings instance
                    _vm.AppSettings.AutoConnectOnStartup = loaded.AutoConnectOnStartup;
                    _vm.AppSettings.UseSimHub = loaded.UseSimHub;
                    _vm.AppSettings.UseIracing = loaded.UseIracing;
                    _vm.AppSettings.CollapsedGroups = loaded.CollapsedGroups ?? new System.Collections.Generic.List<string>();
                    _vm.AppSettings.WindRestingPower = loaded.WindRestingPower;
                    _vm.AppSettings.StartWithWindows = loaded.StartWithWindows;
                    _vm.AppSettings.MinimizeToTaskbarOnClose = loaded.MinimizeToTaskbarOnClose;
                    _vm.AppSettings.ToggleFanKey = loaded.ToggleFanKey;
                    _vm.AppSettings.IncreaseWindRestingKey = loaded.IncreaseWindRestingKey;
                    _vm.AppSettings.DecreaseWindRestingKey = loaded.DecreaseWindRestingKey;
                }
            }
            catch { }

            // Update startup registration
            try
            {
                SetStartupRegistration(startWithWindows);
            }
            catch { }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Kb_GestureChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_vm?.AppSettings == null) return;
                if (sender == kb_ToggleFan)
                {
                    _vm.AppSettings.ToggleFanKey = kb_ToggleFan.Gesture ?? string.Empty;
                    // update registration
                    GlobalHotKeyManager.Unregister("ToggleFan");
                    if (kb_ToggleFan.IsGlobal && !string.IsNullOrWhiteSpace(kb_ToggleFan.Gesture))
                    {
                        GlobalHotKeyManager.Register("ToggleFan", kb_ToggleFan.Gesture, () => { try { _vm.EnableForCar = !_vm.EnableForCar; _vm.MenuStateText = $"Hotkey triggered: ToggleFan ({kb_ToggleFan.Gesture})"; } catch { } });
                    }
                }
                else if (sender == kb_IncreaseWindRest)
                {
                    _vm.AppSettings.IncreaseWindRestingKey = kb_IncreaseWindRest.Gesture ?? string.Empty;
                    GlobalHotKeyManager.Unregister("IncreaseRest");
                    if (kb_IncreaseWindRest.IsGlobal && !string.IsNullOrWhiteSpace(kb_IncreaseWindRest.Gesture))
                    {
                        GlobalHotKeyManager.Register("IncreaseRest", kb_IncreaseWindRest.Gesture, () => { try { _vm.WindRestingPower = _vm.WindRestingPower + 1; _vm.MenuStateText = $"Hotkey triggered: IncreaseRest ({kb_IncreaseWindRest.Gesture})"; } catch { } });
                    }
                }
                else if (sender == kb_DecreaseWindRest)
                {
                    _vm.AppSettings.DecreaseWindRestingKey = kb_DecreaseWindRest.Gesture ?? string.Empty;
                    GlobalHotKeyManager.Unregister("DecreaseRest");
                    if (kb_DecreaseWindRest.IsGlobal && !string.IsNullOrWhiteSpace(kb_DecreaseWindRest.Gesture))
                    {
                        GlobalHotKeyManager.Register("DecreaseRest", kb_DecreaseWindRest.Gesture, () => { try { _vm.WindRestingPower = _vm.WindRestingPower - 1; _vm.MenuStateText = $"Hotkey triggered: DecreaseRest ({kb_DecreaseWindRest.Gesture})"; } catch { } });
                    }
                }

                _settingsSvc.Save(_vm.AppSettings);
            }
            catch { }
        }

        private void Kb_GlobalChanged(object? sender, EventArgs e)
        {
            try
            {
                if (_vm?.AppSettings == null) return;
                if (sender == kb_ToggleFan)
                {
                    _vm.AppSettings.ToggleFanGlobal = kb_ToggleFan.IsGlobal;
                    GlobalHotKeyManager.Unregister("ToggleFan");
                    if (kb_ToggleFan.IsGlobal && !string.IsNullOrWhiteSpace(kb_ToggleFan.Gesture))
                        GlobalHotKeyManager.Register("ToggleFan", kb_ToggleFan.Gesture, () => { try { _vm.EnableForCar = !_vm.EnableForCar; _vm.MenuStateText = $"Hotkey triggered: ToggleFan ({kb_ToggleFan.Gesture})"; } catch { } });
                }
                else if (sender == kb_IncreaseWindRest)
                {
                    _vm.AppSettings.IncreaseWindRestingGlobal = kb_IncreaseWindRest.IsGlobal;
                    GlobalHotKeyManager.Unregister("IncreaseRest");
                    if (kb_IncreaseWindRest.IsGlobal && !string.IsNullOrWhiteSpace(kb_IncreaseWindRest.Gesture))
                        GlobalHotKeyManager.Register("IncreaseRest", kb_IncreaseWindRest.Gesture, () => { try { _vm.WindRestingPower = _vm.WindRestingPower + 1; _vm.MenuStateText = $"Hotkey triggered: IncreaseRest ({kb_IncreaseWindRest.Gesture})"; } catch { } });
                }
                else if (sender == kb_DecreaseWindRest)
                {
                    _vm.AppSettings.DecreaseWindRestingGlobal = kb_DecreaseWindRest.IsGlobal;
                    GlobalHotKeyManager.Unregister("DecreaseRest");
                    if (kb_DecreaseWindRest.IsGlobal && !string.IsNullOrWhiteSpace(kb_DecreaseWindRest.Gesture))
                        GlobalHotKeyManager.Register("DecreaseRest", kb_DecreaseWindRest.Gesture, () => { try { _vm.WindRestingPower = _vm.WindRestingPower - 1; _vm.MenuStateText = $"Hotkey triggered: DecreaseRest ({kb_DecreaseWindRest.Gesture})"; } catch { } });
                }

                _settingsSvc.Save(_vm.AppSettings);
            }
            catch { }
        }

        private void SetStartupRegistration(bool enable)
        {
            const string runKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
            const string appName = "BeltTensioner";
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return;

            using var key = Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key == null) return;
            if (enable)
            {
                key.SetValue(appName, '"' + exe + '"');
            }
            else
            {
                try { key.DeleteValue(appName); } catch { }
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
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
    }
}
