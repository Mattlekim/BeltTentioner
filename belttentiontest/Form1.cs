using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using belttentiontest.Properties;

namespace belttentiontest
{
    public partial class Form1 : Form
    {
        private SerialCommunicator communicator;
        private bool handshakeComplete = false;
        private CancellationTokenSource? autoConnectCts;

        // new: iRacing communicator
        private IracingCommunicator? irCommunicator;
        private bool? pendingIracingState = null;

        // Timer for periodic updates
        private System.Windows.Forms.Timer? updateTimer;

        public Form1()
        {
            InitializeComponent();

            communicator = new SerialCommunicator();
            communicator.MessageReceived += OnMessageReceivedFromSerial;
            communicator.HandshakeComplete += OnHandshakeCompleteFromSerial;

            // Auto-detection on startup disabled. Manual connect via button only.

            // Ensure cancellation when form closes
            this.FormClosed += (_, __) => autoConnectCts?.Cancel();

            // Ensure communicator stopped and disposed when form closes
            this.FormClosing += async (s, e) =>
            {
                try
                {
                    autoConnectCts?.Cancel();
                    await communicator.StopSendLoopAsync().ConfigureAwait(false);
                    communicator.Dispose();

                    // stop iracing monitoring
                    try { irCommunicator?.Dispose(); } catch { }

                    // Stop and dispose timer
                    updateTimer?.Stop();
                    updateTimer?.Dispose();
                }
                catch { }
            };

            // start iRacing monitoring
            irCommunicator = new IracingCommunicator();
            irCommunicator.ConnectionChanged += OnIracingConnectionChanged;
            irCommunicator.Connected += OnIracingConnected;
            irCommunicator.Disconnected += OnIracingDisconnected;
            irCommunicator.GForceUpdated += OnGForceUpdated;
            irCommunicator.ScaledValueUpdated += OnScaledValueUpdated;

            // initial statuses
            labelStatus.Text = "Status"; // keep labelStatus for serial device status
            // textBoxIracingStatus initial text set in designer

            // When the form is shown, ensure we reflect the current state (in case the monitor fired earlier)
            this.Shown += (s, e) =>
            {
                if (pendingIracingState.HasValue)
                {
                    UpdateIracingLabel(pendingIracingState.Value);
                    pendingIracingState = null;
                }
                else
                {
                    UpdateIracingLabel(irCommunicator?.IsConnected ?? false);
                }
            };

            // Set initial BeltStrength value
            if (irCommunicator != null)
            {
                irCommunicator.BeltStrength = (float)numericUpDownBeltStrength.Value;
            }

            // Initialize and start update timer
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 100; // 100ms
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        // Timer tick event handler
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {

            if (checkBoxTest.Checked)
                OnScaledValueUpdated((int)numericUpDownTarget.Value);
            // TODO: Add periodic update logic here
            // Example: Update a label with current time
            // labelStatus.Text = $"Status: {DateTime.Now:HH:mm:ss.fff}";
        }

        private void OnIracingConnected()
        {
            if (!IsHandleCreated)
            {
                pendingIracingState = true;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                textBoxIracingStatus.Text = "connected";
                Log("iRacing: Connected");
            }));
        }

        private void OnIracingDisconnected()
        {
            if (!IsHandleCreated)
            {
                pendingIracingState = false;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                textBoxIracingStatus.Text = "not connect";
                Log("iRacing: Not connected");
            }));
        }

        private void UpdateIracingLabel(bool connected)
        {
            textBoxIracingStatus.Text = connected ? "connected" : "not connect";
            Log(connected ? "iRacing: Connected" : "iRacing: Not connected");
        }

        private void Log(string message, bool append = true)
        {
            // textBoxLog removed, so Log does nothing
        }

        private void OnMessageReceivedFromSerial(string message)
        {
            // Only process messages that are pure analog values (integer)
            if (int.TryParse(message, out int analogValue))
            {
                // Example: update a label with the analog value
                Debug.WriteLine($"Analog value: {analogValue}");
                // If you want to show it in the UI, add a label and set its text here
                // labelAnalogValue.Text = analogValue.ToString();
            }
            // Ignore all other messages
        }

        private void OnHandshakeCompleteFromSerial()
        {
            handshakeComplete = true;
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                labelStatus.Text = $"Handshake complete";
                labelStatus.ForeColor = System.Drawing.Color.Black;
                Log($"Handshake complete on {communicator.PortName}");
                // start periodic sending using numericUpDownTarget's value getter
                communicator.StartSendLoop(() => checkBoxTest.Checked ? (int)numericUpDownTarget.Value : 0);
            }));
        }

        private void OnIracingConnectionChanged(bool connected)
        {
            // If the form handle isn't created yet, store the state so we can update when shown
            if (!IsHandleCreated)
            {
                pendingIracingState = connected;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                UpdateIracingLabel(connected);
            }));
        }

        private void OnGForceUpdated(float gForce)
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                labelGForce.Text = $"G-Force: {gForce:F2}";
            }));
        }

        private void OnScaledValueUpdated(int value)
        {
            //if (!checkBoxTest.Checked)
                communicator.SendValue(value);
        }

        private async void buttonConnect_Click(object sender, EventArgs e)
        {
            try
            {
                autoConnectCts?.Cancel();

                string[] ports = SerialPort.GetPortNames();
                if (ports.Length == 0)
                {
                    labelStatus.Text = "No COM ports found";
                    labelStatus.ForeColor = System.Drawing.Color.Red;
                    Log("No COM ports found");
                    return;
                }

                Invoke(new Action(() => labelStatus.Text = "Scanning ports..."));

                using var manualCts = new CancellationTokenSource();
                bool ok = await communicator.ManualScanAsync(manualCts.Token).ConfigureAwait(false);
                if (ok)
                {
                    handshakeComplete = true;
                    Invoke(new Action(() =>
                    {
                        labelStatus.Text = $"Connected to {communicator.PortName} (handshake complete)";
                        labelStatus.ForeColor = System.Drawing.Color.Black;
                        communicator.StartSendLoop(() => checkBoxTest.Checked ? (int)numericUpDownTarget.Value : 0);
                    }));
                    Log($"Manual connect: Connected to {communicator.PortName}");
                    return;
                }

                Invoke(new Action(() =>
                {
                    labelStatus.Text = "No device responded";
                    labelStatus.ForeColor = System.Drawing.Color.Red;
                }));
                Log("Manual connect: No device responded");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
                labelStatus.ForeColor = System.Drawing.Color.Red;
                Log($"Manual connect failed: {ex.Message}");
            }
        }

        private void labelStatus_Click(object sender, EventArgs e)
        {

        }

        

        private void numericUpDownBeltStrength_ValueChanged(object sender, EventArgs e)
        {
            if (irCommunicator != null)
            {
                irCommunicator.BeltStrength = (float)numericUpDownBeltStrength.Value;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Load BeltStrength from settings
            if (irCommunicator != null)
            {
                float savedStrength = Settings.Default.BeltStrength;
                irCommunicator.BeltStrength = savedStrength;
                // NumericUpDown expects decimal and its min/max are decimal, clamp and cast accordingly
                decimal min = numericUpDownBeltStrength.Minimum;
                decimal max = numericUpDownBeltStrength.Maximum;
                decimal value = Math.Min(max, Math.Max(min, (decimal)savedStrength));
                numericUpDownBeltStrength.Value = value;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Save BeltStrength to settings
            Settings.Default.BeltStrength = (float)numericUpDownBeltStrength.Value;
            Settings.Default.Save();
            // Stop and dispose timer
            updateTimer?.Stop();
            updateTimer?.Dispose();
            base.OnFormClosing(e);
        }

        private void numericUpDownTarget_ValueChanged(object sender, EventArgs e)
        {
    
        }

        private void checkBoxTest_CheckedChanged(object sender, EventArgs e)
        {
            // You can add logic here if needed, or leave empty if not used
        }
    }
}
