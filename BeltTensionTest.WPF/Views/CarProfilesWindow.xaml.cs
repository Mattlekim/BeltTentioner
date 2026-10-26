using BeltAPI;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.Shared;
using BeltTensionTest.WPF.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>Settings → Car Profiles: list, delete, export and import car profiles.</summary>
    public partial class CarProfilesWindow : Window
    {
        private const string FileFilter = "Car profiles (*.json)|*.json|All files (*.*)|*.*";

        private readonly MainViewModel _vm;
        private readonly CarSettingsService _svc = CarSettingsService.Instance;

        private sealed record ProfileItem(string Name, string Display);

        public CarProfilesWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            // Make sure the active car's latest slider values are in the list before it is shown.
            _svc.SaveCurrentCarSettings(_vm.ActiveCarName);
            Refresh();
        }

        private IReadOnlyList<string> SelectedNames =>
            lst_Profiles.SelectedItems.OfType<ProfileItem>().Select(p => p.Name).ToList();

        private void Refresh()
        {
            var active = _vm.ActiveCarName;
            lst_Profiles.ItemsSource = _svc.GetAvailableCarNames()
                .Select(n => new ProfileItem(n, string.Equals(n, active, StringComparison.OrdinalIgnoreCase) ? n + "   (active)" : n))
                .ToList();

            int count = _svc.Settings.Count;
            txt_Summary.Text = count == 0 ? "No car profiles saved yet." : $"{count} car profile{(count == 1 ? "" : "s")}";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool any = _svc.Settings.Count > 0;
            bool selected = lst_Profiles.SelectedItems.Count > 0;
            btn_ExportAll.IsEnabled = any;
            btn_ExportSelected.IsEnabled = selected;
            btn_Delete.IsEnabled = selected;
        }

        private void Profiles_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateButtons();

        private bool IsActiveProfile(string name) => string.Equals(name, _vm.ActiveCarName, StringComparison.OrdinalIgnoreCase);

        // ----- Delete ---------------------------------------------------------

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var names = SelectedNames;
            if (names.Count == 0) return;

            // The loaded profile keeps being saved back, so deleting it would just bring it back.
            var active = names.Where(IsActiveProfile).ToList();
            if (active.Count > 0)
            {
                ThemedMessageBox.Show(this,
                    $"\"{active[0]}\" is the profile currently in use and can't be deleted. Switch to another car first.",
                    "Car Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
                names = names.Except(active).ToList();
                if (names.Count == 0) return;
            }

            string what = names.Count == 1 ? $"the profile \"{names[0]}\"" : $"{names.Count} profiles";
            if (ThemedMessageBox.Show(this, $"Delete {what}? This can't be undone.", "Delete Profiles",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            foreach (var n in names) _svc.DeleteProfile(n);
            Refresh();
            txt_Status.Text = $"Deleted {names.Count} profile{(names.Count == 1 ? "" : "s")}.";
        }

        // ----- Export ---------------------------------------------------------

        private void ExportSelected_Click(object sender, RoutedEventArgs e)
        {
            var names = SelectedNames;
            if (names.Count == 0) return;
            string suggested = names.Count == 1 ? SafeFileName(names[0]) : "BST car profiles";
            Export(names, suggested);
        }

        private void ExportAll_Click(object sender, RoutedEventArgs e) =>
            Export(_svc.GetAvailableCarNames().ToList(), "BST car profiles");

        private void Export(IReadOnlyList<string> names, string suggestedFileName)
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export car profiles",
                Filter = FileFilter,
                FileName = suggestedFileName + ".json",
                DefaultExt = ".json",
                OverwritePrompt = true
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                _svc.SaveCurrentCarSettings(_vm.ActiveCarName);
                _svc.ExportProfiles(names, dlg.FileName);
                txt_Status.Text = $"Exported {names.Count} profile{(names.Count == 1 ? "" : "s")} to {dlg.FileName}";
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this, "Could not export:\n" + ex.Message, "Car Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ----- Import ---------------------------------------------------------

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Import car profiles", Filter = FileFilter, CheckFileExists = true };
            if (dlg.ShowDialog(this) != true) return;

            Dictionary<string, CarSettings> incoming;
            try
            {
                incoming = CarSettingsService.ReadProfilesFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this, "Could not read that file as car profiles:\n" + ex.Message,
                    "Car Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Match an existing profile's exact key so a case difference doesn't create a duplicate.
            string KeyFor(string name) =>
                _svc.Settings.Keys.FirstOrDefault(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) ?? name;

            var conflicts = incoming
                .Where(p => _svc.Settings.ContainsKey(KeyFor(p.Key)))
                .Select(p => (Name: KeyFor(p.Key), Current: _svc.Settings[KeyFor(p.Key)], Imported: p.Value))
                .ToList();

            // Show what differs and let the user pick which existing profiles to replace.
            var replace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (conflicts.Count > 0)
            {
                var conflictDlg = new ImportConflictWindow(conflicts) { Owner = this };
                if (conflictDlg.ShowDialog() != true) return;
                replace.UnionWith(conflictDlg.ReplaceNames);
            }

            int imported = 0, skipped = 0;
            bool activeChanged = false;
            foreach (var (name, settings) in incoming)
            {
                string key = KeyFor(name);
                if (_svc.Settings.ContainsKey(key) && !replace.Contains(key)) { skipped++; continue; }

                bool isActive = IsActiveProfile(key);
                _svc.SetProfile(key, settings, makeCurrent: isActive);
                activeChanged |= isActive;
                imported++;
            }

            if (imported > 0)
            {
                _svc.SaveCurrentCarSettings(null);
                // Reload the active car so the sliders show the imported values.
                if (activeChanged) _vm.LoadCarSettings(_vm.ActiveCarName);
            }

            Refresh();
            txt_Status.Text = $"Imported {imported} profile{(imported == 1 ? "" : "s")}" + (skipped > 0 ? $", skipped {skipped}." : ".");
        }

        // ----- Window chrome --------------------------------------------------

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
