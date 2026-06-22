using BeltTensionTest.WPF.ViewModels;
using BeltTensionTest.WPF.Services;
using System.Windows;
using Microsoft.Win32;
using System;

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
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.AppSettings == null) return;

            var startWithWindows = chk_StartWithWindows.IsChecked == true;
            var minimizeToTaskbar = chk_MinimizeToTaskbar.IsChecked == true;

            _vm.AppSettings.StartWithWindows = startWithWindows;
            _vm.AppSettings.MinimizeToTaskbarOnClose = minimizeToTaskbar;

            // Persist
            _settingsSvc.Save(_vm.AppSettings);

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
    }
}
