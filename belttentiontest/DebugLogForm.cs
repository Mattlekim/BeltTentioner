using BeltAPI;
using System;
using System.Windows.Forms;

namespace belttentiontest
{
    /// <summary>
    /// A lightweight floating window that tails the BeltSerialDevice log in real time.
    /// </summary>
    internal class DebugLogForm : Form
    {
        private readonly BeltSerialDevice _device;
        private readonly TextBox _textBox;
        private readonly Button _btnClear;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private int _lastLogCount = 0;

        public DebugLogForm(BeltSerialDevice device)
        {
            _device = device;

            Text = "Serial Device Debug Log";
            Size = new System.Drawing.Size(600, 400);
            MinimumSize = new System.Drawing.Size(300, 200);
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(
                System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea.Right - 620 ?? 100,
                100);

            // Clear button
            _btnClear = new Button
            {
                Text = "Clear",
                Dock = DockStyle.Top,
                Height = 28
            };
            _btnClear.Click += (_, __) =>
            {
                _device.GetLog.Clear();
                _lastLogCount = 0;
                _textBox.Clear();
            };

            // Log text box
            _textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 9f),
                WordWrap = false,
                BackColor = System.Drawing.Color.Black,
                ForeColor = System.Drawing.Color.LimeGreen
            };

            Controls.Add(_textBox);
            Controls.Add(_btnClear);

            // Poll for new log entries ~10 times per second
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _refreshTimer.Tick += RefreshLog;
            _refreshTimer.Start();

            FormClosed += (_, __) => _refreshTimer.Stop();
        }

        protected override void OnLoad(EventArgs e)
        {
           
            base.OnLoad(e);
        }

        private void RefreshLog(object? sender, EventArgs e)
        {
            var log = _device.GetLog;
            if (log.Count == _lastLogCount)
                return;

            // Append only the new lines to avoid rewriting the whole box
            _textBox.SuspendLayout();
            for (int i = _lastLogCount; i < log.Count; i++)
            {
                _textBox.AppendText(log[i] + Environment.NewLine);
            }
            _lastLogCount = log.Count;
            _textBox.ResumeLayout();

            // Auto-scroll to bottom
            _textBox.SelectionStart = _textBox.TextLength;
            _textBox.ScrollToCaret();
        }
    }
}
