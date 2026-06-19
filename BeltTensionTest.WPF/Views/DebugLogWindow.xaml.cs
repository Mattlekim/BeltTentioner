using BeltAPI;
using System;
using System.Windows;
using System.Windows.Threading;

namespace BeltTensionTest.WPF.Views
{
    public partial class DebugLogWindow : Window
    {
        private readonly BeltSerialDevice _device;
        private readonly DispatcherTimer  _timer;
        private int _lastCount;

        public DebugLogWindow(BeltSerialDevice device)
        {
            _device = device;
            InitializeComponent();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTick;
            _timer.Start();

            Closed += (_, _) => _timer.Stop();
        }

        private void OnTick(object? s, EventArgs e)
        {
            var log = _device.GetLog;
            if (log.Count == _lastCount) return;
            for (int i = _lastCount; i < log.Count; i++)
                LogListBox.Items.Add(log[i]);
            _lastCount = log.Count;
            _device._log.Clear();
            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            StatusLabel.Text = $"{_lastCount} entries";
        }

        private void Clear_Click(object s, RoutedEventArgs e)
        {
            LogListBox.Items.Clear();
            _lastCount = 0;
        }

        private void CopyAll_Click(object s, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in LogListBox.Items)
                sb.AppendLine(item?.ToString());
            Clipboard.SetText(sb.ToString());
        }
    }
}
