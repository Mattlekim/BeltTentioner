using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Runs <see cref="OverlayBenchmark"/> (2 minutes as one canvas, 2 minutes as one
    /// overlay per window) and shows the comparison. The overlay itself keeps being
    /// driven by OverlayWindow's timer; this window only starts it, shows how far it
    /// is and displays the result.
    /// </summary>
    public partial class BenchmarkWindow : Window
    {
        private readonly OverlayBenchmark _bench;
        private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(500) };

        public BenchmarkWindow(OverlayBenchmark bench)
        {
            _bench = bench;
            InitializeComponent();
            IntroText.Text =
                "Runs the VR overlay for 2 minutes as one shared canvas, then 2 minutes with one overlay per window, " +
                "and compares GPU use and GPU memory.\n\n" +
                "For the VR game's own GPU numbers, start the game first with the overlay showing. " +
                "Leave edit mode and the desktop preview alone while it runs. " +
                "The overlay stays usable; the numbers are best if you leave the PC otherwise idle.";
            _poll.Tick += (_, _) => Refresh();
            Closing += (_, _) => { _poll.Stop(); _bench.Cancel(); };
            Refresh();
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (!_bench.TryStart(out string error))
            {
                StatusText.Text = error;
                return;
            }
            ReportBox.Clear();
            _poll.Start();
            Refresh();
        }

        private void Stop_Click(object sender, RoutedEventArgs e) => _bench.Cancel();

        private void Refresh()
        {
            bool running = _bench.IsRunning;
            Bar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            Bar.Value = _bench.Progress;
            StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            if (running)
            {
                StatusText.Text = _bench.Status + (_bench.GameAttached ? "" : "  (VR game not attached: only this app is measured)");
                return;
            }

            if (_bench.State == BenchmarkState.Idle)
            {
                string? why = _bench.CannotStartReason;
                StatusText.Text = why ?? "Ready.";
                StartButton.IsEnabled = why == null;
                return;
            }

            _poll.Stop();
            StatusText.Text = _bench.State switch
            {
                BenchmarkState.Finished => "Done.",
                BenchmarkState.Cancelled => "Cancelled. The overlay is back the way it was.",
                _ => _bench.Error ?? _bench.Status,
            };
            StartButton.Content = "Run again";
            StartButton.IsEnabled = _bench.CannotStartReason == null;
            if (_bench.Report != null)
            {
                ReportBox.Text = _bench.Report;
                CopyButton.Visibility = Visibility.Visible;
                OpenFolderButton.Visibility = string.IsNullOrEmpty(_bench.ReportPath) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(_bench.Report ?? ""); } catch (System.Runtime.InteropServices.COMException) { }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_bench.ReportPath)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_bench.ReportPath}\""); } catch { }
        }

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
