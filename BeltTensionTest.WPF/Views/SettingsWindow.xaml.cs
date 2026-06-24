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
            chk_StartWithWindows.IsChecked = _vm.AppSettings?.StartWithWindows ?? false;
            chk_MinimizeToTaskbar.IsChecked = _vm.AppSettings?.MinimizeToTaskbarOnClose ?? false;

            // Initialize keybinding boxes
            if (_vm.AppSettings != null)
            {
                txt_ToggleFan.Text = _vm.AppSettings.ToggleFanKey ?? string.Empty;
                txt_IncreaseWindRest.Text = _vm.AppSettings.IncreaseWindRestingKey ?? string.Empty;
                txt_DecreaseWindRest.Text = _vm.AppSettings.DecreaseWindRestingKey ?? string.Empty;
            }

            // Also load on-disk settings to ensure mappings saved from previous runs are shown
            try
            {
                var disk = _settingsSvc.Load();
                if (disk != null)
                {
                    if (!string.IsNullOrWhiteSpace(disk.ToggleFanKey)) txt_ToggleFan.Text = disk.ToggleFanKey;
                    if (!string.IsNullOrWhiteSpace(disk.IncreaseWindRestingKey)) txt_IncreaseWindRest.Text = disk.IncreaseWindRestingKey;
                    if (!string.IsNullOrWhiteSpace(disk.DecreaseWindRestingKey)) txt_DecreaseWindRest.Text = disk.DecreaseWindRestingKey;
                }
            }
            catch { }

            // Ensure no textbox is marked ready for capture by default; require explicit selection
            try
            {
                txt_ToggleFan.Tag = false;
                txt_IncreaseWindRest.Tag = false;
                txt_DecreaseWindRest.Tag = false;

                // restore visuals in case
                txt_ToggleFan.BorderBrush = null;
                txt_ToggleFan.BorderThickness = new System.Windows.Thickness(1);
                txt_ToggleFan.Background = System.Windows.Media.Brushes.Transparent;

                txt_IncreaseWindRest.BorderBrush = null;
                txt_IncreaseWindRest.BorderThickness = new System.Windows.Thickness(1);
                txt_IncreaseWindRest.Background = System.Windows.Media.Brushes.Transparent;

                txt_DecreaseWindRest.BorderBrush = null;
                txt_DecreaseWindRest.BorderThickness = new System.Windows.Thickness(1);
                txt_DecreaseWindRest.Background = System.Windows.Media.Brushes.Transparent;
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

            var startWithWindows = chk_StartWithWindows.IsChecked == true;
            var minimizeToTaskbar = chk_MinimizeToTaskbar.IsChecked == true;

            _vm.AppSettings.StartWithWindows = startWithWindows;
            _vm.AppSettings.MinimizeToTaskbarOnClose = minimizeToTaskbar;
            // Save keybindings
            _vm.AppSettings.ToggleFanKey = txt_ToggleFan.Text ?? string.Empty;
            _vm.AppSettings.IncreaseWindRestingKey = txt_IncreaseWindRest.Text ?? string.Empty;
            _vm.AppSettings.DecreaseWindRestingKey = txt_DecreaseWindRest.Text ?? string.Empty;

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

        private void KeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // Mark textbox as ready to capture a single keypress and select text.
                try { tb.SelectAll(); } catch { }
                try
                {
                    tb.Tag = true;
                    // visually indicate waiting-for-key state
                    tb.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAA20"));
                    tb.BorderThickness = new Thickness(2);
                    tb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A22"));
                }
                catch { }
            }
        }

        private void KeyBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only arm textbox for capture when user explicitly clicks it. Make it focusable then focus.
            if (sender is TextBox tb)
            {
                try
                {
                    tb.Focusable = true;
                    tb.IsTabStop = true;
                    tb.Tag = true;
                    tb.Focus();
                    tb.SelectAll();
                    e.Handled = true;
                }
                catch { }
            }
        }

        private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Capture combination excluding modifier-only presses
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
            {
                e.Handled = true;
                return;
            }

            var mods = Keyboard.Modifiers;

            // Build a gesture string manually to avoid KeyGesture throwing for unsupported combinations
            string s = string.Empty;
            if ((mods & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((mods & ModifierKeys.Alt) != 0) s += "Alt+";
            if ((mods & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((mods & ModifierKeys.Windows) != 0) s += "Win+";

            // Use the Key enum name for the key portion
            s += key.ToString();

            if (sender is TextBox tb)
            {
                // Only capture if textbox was marked as ready (first keypress after focus).
                var ready = tb.Tag as bool? ?? false;
                if (!ready)
                {
                    // Ignore subsequent keypresses while the box is not ready to capture
                    e.Handled = true;
                    return;
                }

                tb.Text = s;
                // Mark as captured so further keys don't overwrite until user focuses again
                tb.Tag = false;

                // restore visuals
                try
                {
                    tb.BorderBrush = null;
                    tb.BorderThickness = new Thickness(1);
                    tb.Background = Brushes.Transparent;
                }
                catch { }

                // Persist the mapping immediately so a breakpoint on Save will be hit
                try
                {
                    if (_vm?.AppSettings != null)
                    {
                        if (tb == txt_ToggleFan)
                            _vm.AppSettings.ToggleFanKey = s;
                        else if (tb == txt_IncreaseWindRest)
                            _vm.AppSettings.IncreaseWindRestingKey = s;
                        else if (tb == txt_DecreaseWindRest)
                            _vm.AppSettings.DecreaseWindRestingKey = s;

                        _settingsSvc.Save(_vm.AppSettings);

                        // Reload and copy to in-memory AppSettings to ensure consistency
                        try
                        {
                            var loaded = _settingsSvc.Load();
                            if (loaded != null && _vm.AppSettings != null)
                            {
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
                    }
                }
                catch { }
            }

            e.Handled = true;
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
