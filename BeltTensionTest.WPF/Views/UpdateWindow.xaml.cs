using System;
using System.Threading;
using System.Windows;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.Shared;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Shows the release notes for a pending update and walks the user through
    /// download, then an explicit "install now?" yes/no.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private readonly UpdateService _svc;
        private readonly UpdateService.UpdateInfo _info;
        private CancellationTokenSource? _cts;
        private bool _busy;

        /// <summary>Path of the downloaded zip once the download has finished.</summary>
        public string? DownloadedZip { get; private set; }

        /// <summary>True when the user chose to install and the installer helper is running.</summary>
        public bool InstallStarted { get; private set; }

        public UpdateWindow(UpdateService svc, UpdateService.UpdateInfo info, string? alreadyDownloaded)
        {
            InitializeComponent();
            _svc = svc;
            _info = info;
            DownloadedZip = alreadyDownloaded;

            HeaderText.Text = $"Version {info.RemoteTag?.TrimStart('v', 'V')} is available";
            SubText.Text = $"You are on version {UpdateService.AppVersion}.";
            NotesText.Text = string.IsNullOrWhiteSpace(info.Notes) ? "No release notes were provided." : info.Notes.Trim();

            if (DownloadedZip != null) ActionButton.Content = "Install";
        }

        private async void Action_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            if (DownloadedZip == null)
            {
                if (!await DownloadAsync()) return;
            }
            AskInstall();
        }

        private async System.Threading.Tasks.Task<bool> DownloadAsync()
        {
            _busy = true;
            ActionButton.IsEnabled = false;
            LaterButton.Content = "Cancel";
            Progress.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "Downloading...";
            _cts = new CancellationTokenSource();
            try
            {
                var progress = new Progress<double>(p =>
                {
                    Progress.Value = p;
                    StatusText.Text = $"Downloading... {p:P0}";
                });
                DownloadedZip = await _svc.DownloadAsync(_info, progress, _cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Download cancelled.";
                return false;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Download failed: " + ex.Message;
                return false;
            }
            finally
            {
                _busy = false;
                _cts?.Dispose();
                _cts = null;
                ActionButton.IsEnabled = true;
                LaterButton.Content = "Later";
                Progress.Visibility = Visibility.Collapsed;
                if (DownloadedZip != null) ActionButton.Content = "Install";
            }
        }

        private void AskInstall()
        {
            var answer = ThemedMessageBox.Show(this,
                "The update has been downloaded.\n\nInstall it now? The app will close and then restart automatically.",
                "Install update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) { Close(); return; }

            if (_svc.StartInstall(DownloadedZip!))
            {
                InstallStarted = true;
                DialogResult = true;
            }
            else
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = "Could not start the installer. Please try again.";
            }
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) { _cts?.Cancel(); return; }
            Close();
        }
    }
}
