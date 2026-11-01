using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BeltTensionTest.WPF.Services;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Help > Select version: lists every published release and lets the user download and install
    /// any of them, older ones included. The download / install itself is the update window.
    /// </summary>
    public partial class VersionSelectorWindow : Window
    {
        private readonly UpdateService _svc;

        /// <summary>True when the user chose to install and the installer helper is running; the app must exit.</summary>
        public bool InstallStarted { get; private set; }

        private sealed class Row
        {
            public UpdateService.ReleaseEntry Release { get; init; } = null!;
            public string Title => Release.Tag.TrimStart('v', 'V') + (Release.IsCurrent ? "  (installed)" : "");
            public string Subtitle =>
                (Release.Published?.ToLocalTime().ToString("d MMM yyyy") ?? "") + (Release.IsPrerelease ? "  pre-release" : "");
        }

        public VersionSelectorWindow(UpdateService svc)
        {
            InitializeComponent();
            _svc = svc;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            StatusText.Text = "Getting the list of versions from GitHub...";
            var (releases, error) = await _svc.ListReleasesAsync();
            if (releases.Count == 0)
            {
                HeaderText.Text = "No versions found";
                StatusText.Text = error != null ? "Could not get the list of versions: " + error : "There are no downloadable releases.";
                return;
            }
            VersionList.ItemsSource = releases.Select(r => new Row { Release = r }).ToList();
            var current = VersionList.Items.Cast<Row>().FirstOrDefault(r => r.Release.IsCurrent);
            VersionList.SelectedItem = current ?? VersionList.Items[0];
            StatusText.Text = $"You are on version {UpdateService.AppVersion}. {releases.Count} versions available.";
            if (error != null) StatusText.Text += " (The list may be incomplete: " + error + ")";
        }

        private UpdateService.ReleaseEntry? Selected => (VersionList.SelectedItem as Row)?.Release;

        private void VersionList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var r = Selected;
            InstallButton.IsEnabled = r != null;
            if (r == null) return;
            string v = r.Tag.TrimStart('v', 'V');
            HeaderText.Text = "Version " + v;
            string size = r.DownloadSize > 0 ? $"  -  {r.DownloadSize / 1048576.0:0.#} MB" : "";
            SubText.Text = (r.IsCurrent ? "This is the version you are running." : $"You are on version {UpdateService.AppVersion}.") + size;
            InstallButton.Content = r.IsCurrent ? "Reinstall" : "Download & install";
            NotesText.Text = string.IsNullOrWhiteSpace(r.Notes) ? "No release notes were provided." : r.Notes.Trim();
        }

        private void VersionList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Selected != null && e.OriginalSource is FrameworkElement) Install_Click(sender, e);
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            var r = Selected;
            if (r == null) return;
            var win = new UpdateWindow(_svc, r.ToUpdateInfo(), null) { Owner = this };
            win.ShowDialog();
            if (win.InstallStarted)
            {
                InstallStarted = true;
                DialogResult = true;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
