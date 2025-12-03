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

        private double curveAmount = 1.0; // Default curve amount, can be set via UI or property
        private int lastCurvedValue = 0;
        private int maxPower
        { 
        get => Settings.Default.MaxPower; // Maximum power value, can be set via UI or property
            set
            {
                Settings.Default.MaxPower = value;
            }
        }

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
            irCommunicator = IracingCommunicator.Instance;
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
                    irCommunicator.MotorStrenth = (float)numericUpDownBeltStrength.Value;
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
            else
            if (IracingCommunicator.Instance != null)
                if (!IracingCommunicator.Instance.IsConnected)
                    OnScaledValueUpdated(20); //keep comuncations allive with small value when not connected to iRacing
            
            


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

        private void DrawCurveGraph()
        {
            if (pictureBoxCurveGraph == null) return;
            int width = pictureBoxCurveGraph.Width;
            int height = pictureBoxCurveGraph.Height;
            int axisSpace = 20; // Space reserved for X axis labels/ticks
            int xPadding = 12; // Padding on left and right sides
            int graphWidth = width - 2 * xPadding;
            var bmp = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.White);
                var pen = new System.Drawing.Pen(System.Drawing.Color.Blue, 2);
                var font = new System.Drawing.Font("Arial", 8);
                var brush = System.Drawing.Brushes.Black;
                // REMOVED: Draw X axis label (now in labelXAxis)
                // Draw Y axis label (rotated)
                string yLabel = "Output To Belt";
                var yLabelSize = g.MeasureString(yLabel, font);
                g.TranslateTransform(0, (height - axisSpace) / 2);
                g.RotateTransform(-90);
                g.DrawString(yLabel, font, brush, -(yLabelSize.Width / 2), 0);
                g.ResetTransform();
                // Draw X axis tick marks and numbers (0 to 7)
                int numTicks = 8;
                for (int i = 0; i <= numTicks; i++)
                {
                    float tickValue = i;
                    int tickX = xPadding + (int)Math.Round(tickValue / 7.0 * (graphWidth - 1));
                    int tickY = height - 1;
                    g.DrawLine(System.Drawing.Pens.Black, tickX, tickY - 4, tickX, tickY);
                    string tickLabel = tickValue.ToString("0.##");
                    var tickLabelSize = g.MeasureString(tickLabel, font);
                    g.DrawString(tickLabel, font, brush, tickX - tickLabelSize.Width / 2, tickY - tickLabelSize.Height - 2);
                }
                // Draw curve (leave axisSpace at bottom, and xPadding on sides)
                int graphHeight = height - axisSpace;
                for (int x = 0; x < graphWidth; x++)
                {
                    float inputValue = (float)(x * 7.0 / (graphWidth - 1)); // X axis: 0..7 (float)
                    double normalized = inputValue / 7.0;
                    double curved = Math.Pow(normalized, curveAmount); // 0..1
                    int yValue = (int)Math.Round(curved * 1023); // full scale
                    if (yValue > maxPower) yValue = maxPower;
                    int y = graphHeight - 1 - (int)(yValue / 1023.0 * (graphHeight - 1)); // Y axis: 0..1023
                    int drawX = xPadding + x;
                    if (x > 0)
                    {
                        float prevInputValue = (float)((x - 1) * 7.0 / (graphWidth - 1));
                        double prevNormalized = prevInputValue / 7.0;
                        double prevCurved = Math.Pow(prevNormalized, curveAmount);
                        int prevYValue = (int)Math.Round(prevCurved * 1023);
                        if (prevYValue > maxPower) prevYValue = maxPower;
                        int prevY = graphHeight - 1 - (int)(prevYValue / 1023.0 * (graphHeight - 1));
                        int prevDrawX = xPadding + x - 1;
                        g.DrawLine(pen, prevDrawX, prevY, drawX, y);
                    }
                }
            }
            pictureBoxCurveGraph.Image = bmp;
        }

        private void numericUpDownMaxPower_ValueChanged(object sender, EventArgs e)
        {
            maxPower = (int)numericUpDownMaxPower.Value;
            DrawCurveGraph();
        }

        private void OnScaledValueUpdated(int value)
        {
            // Apply curve: value in [0,1023], curveAmount >= 0.0
            double normalized = Math.Clamp((double)value / 1023.0, 0.0, 1.0);
            double curved = Math.Pow(normalized, curveAmount);
            int curvedValue = (int)Math.Round(curved * Settings.Default.GForceMult);
            curvedValue = Math.Clamp(curvedValue, 0, maxPower);
            lastCurvedValue = curvedValue;

            
            communicator.SendValue(curvedValue);
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
                irCommunicator.MotorStrenth = (float)numericUpDownBeltStrength.Value;
            }
            DrawCurveGraph(); // Update graph when belt strength changes
        }

        private void numericUpDownCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            curveAmount = (double)numericUpDownCurveAmount.Value;
            DrawCurveGraph();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Load BeltStrength from settings
            if (irCommunicator != null)
            {
                float savedStrength = Settings.Default.BeltStrength;
                irCommunicator.MotorStrenth = savedStrength;
                decimal min = numericUpDownBeltStrength.Minimum;
                decimal max = numericUpDownBeltStrength.Maximum;
                decimal value = Math.Min(max, Math.Max(min, (decimal)savedStrength));
                numericUpDownBeltStrength.Value = value;
            }
            // Load curveAmount from settings
            curveAmount = Settings.Default.CurveAmount;
            decimal curveMin = numericUpDownCurveAmount.Minimum;
            decimal curveMax = numericUpDownCurveAmount.Maximum;
            decimal curveValue = Math.Min(curveMax, Math.Max(curveMin, (decimal)curveAmount));
            numericUpDownCurveAmount.Value = curveValue;
            // Load maxPower from settings
            maxPower = Settings.Default.MaxPower;
            decimal maxPowerMin = numericUpDownMaxPower.Minimum;
            decimal maxPowerMax = numericUpDownMaxPower.Maximum;
            decimal maxPowerValue = Math.Min(maxPowerMax, Math.Max(maxPowerMin, (decimal)maxPower));
            numericUpDownMaxPower.Value = maxPowerValue;
            DrawCurveGraph();
            // Load lastCurvedValue from settings
            lastCurvedValue = Settings.Default.LastCurvedValue;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Save BeltStrength to settings
            Settings.Default.BeltStrength = (float)numericUpDownBeltStrength.Value;
            // Save curveAmount to settings
            Settings.Default.CurveAmount = (double)numericUpDownCurveAmount.Value;
            // Save maxPower to settings
            Settings.Default.MaxPower = (int)numericUpDownMaxPower.Value;
            // Save lastCurvedValue to settings
            Settings.Default.LastCurvedValue = lastCurvedValue;
            Settings.Default.Save();
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
