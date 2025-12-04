using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using belttentiontest.Properties;
using System.IO;
using System.Text.Json;

namespace belttentiontest
{
    public partial class Form1 : Form
    {
        public static int MaxAnalogValue = 1023;

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

        private string CarName = "N/A";
        private int _maxPower;
        private float _gForceMult = 1f;
        private double _curveAmount = 1f;

        private float maxGForceRecorded = 0f; // Max G-Force recorded

        private CarSettingsStore carSettingsStore = new CarSettingsStore();
        private string carSettingsFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");

        public Form1()
        {
            InitializeComponent();

            SetControlsEnabled(false);
            buttonConnect.Enabled = true;

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
            irCommunicator.CarNameChanged += (carName) =>
            {
                BeginInvoke(new Action(() =>
                {
                    lb_carName.Text = $"Car: {carName}";
                    CarName = carName;
                    LoadCarSettings(carName);
                }));
            };

            // initial statuses
            // labelStatus.Text = "Status"; // keep labelStatus for serial device status
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
                    OnScaledValueUpdated(0.1f); //keep comuncations allive with small value when not connected to iRacing



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

        private void ShowDisconnectedUI(string reason = "Device disconnected")
        {
            labelStatus.Text = reason;
            labelStatus.ForeColor = System.Drawing.Color.Red;
            labelAnalogValue.Text = $"Analog: NA";
            labelTargetValue.Text = $"Target: NA";
            labelDistanceValue.Text = $"Distance: NA";
            SetControlsEnabled(false);
            buttonConnect.Enabled = true;
        }

        private void OnMessageReceivedFromSerial(string message)
        {
            var parsedDisconected = communicator.ParseSerialLineDisconect(message);
            if (parsedDisconected.HasValue)
            {
                if (communicator.IsConnected)
                {
                    var (analogValue, Connect) = parsedDisconected.Value;
                    communicator.Disconnect();
                    BeginInvoke(new Action(() =>
                    {
                        ShowDisconnectedUI("Seatbelt disconnected");
                        labelAnalogValue.Text = $"Analog: {analogValue}";
                    }));
                    return;
                }
            }

            // Try to parse the message as a tab-separated serial line
            var parsed = communicator.ParseSerialLine(message);
            if (parsed.HasValue)
            {
                var (analogValue, target, distance) = parsed.Value;
                Debug.WriteLine($"Serial Data: Analog={analogValue}, Target={target}, Distance={distance}");
                // Display values on the form
                BeginInvoke(new Action(() =>
                {
                    labelAnalogValue.Text = $"Analog: {analogValue}";
                    labelTargetValue.Text = $"Target: {target}";
                    labelDistanceValue.Text = $"Distance: {distance}";
                }));
                return;
            }

            if (message == "DEVICE_UNPLUGGED")
            {
                communicator.Disconnect();
                BeginInvoke(new Action(() =>
                {
                    ShowDisconnectedUI("Device unplugged");
                }));
                return;
            }



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
                SetControlsEnabled(true);
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
                if (gForce > maxGForceRecorded)
                {
                    maxGForceRecorded = gForce;
                    labelMaxGForce.Text = $"Max G-Force: {maxGForceRecorded:F2}";
                }
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
                // Draw Y axis label (rotated)
                string yLabel = "Output To Belt";
                var yLabelSize = g.MeasureString(yLabel, font);
                g.TranslateTransform(0, (height - axisSpace) / 2);
                g.RotateTransform(-90);
                g.DrawString(yLabel, font, brush, -(yLabelSize.Width / 2), 0);
                g.ResetTransform();

                // Draw X and Y axis lines
                int graphHeight = height - axisSpace;
                // Y axis (vertical)
                g.DrawLine(System.Drawing.Pens.Black, xPadding, 0, xPadding, graphHeight - 1);
                // X axis (horizontal)
                g.DrawLine(System.Drawing.Pens.Black, xPadding, graphHeight - 1, xPadding + graphWidth - 1, graphHeight - 1);

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
                int maxV = (int)((_maxPower / 100f) * MaxAnalogValue);
                // Draw curve (leave axisSpace at bottom, and xPadding on sides)
                for (int x = 0; x < graphWidth; x++)
                {
                    float inputValue = (float)(x * 7.0 / (graphWidth - 1)); // X axis: 0..7 (float)
                    double normalized = inputValue / 7.0;
                    double curved = Math.Pow(normalized, curveAmount); // 0..1
                    int yValue = (int)(Math.Round(curved * 1023) * _gForceMult); // full scale
                    if (yValue > maxV) yValue = maxV;
                    int y = graphHeight - 1 - (int)(yValue / 1023.0 * (graphHeight - 1)); // Y axis: 0..1023
                    int drawX = xPadding + x;
                    if (x > 0)
                    {
                        float prevInputValue = (float)((x - 1) * 7.0 / (graphWidth - 1));
                        double prevNormalized = prevInputValue / 7.0;
                        double prevCurved = Math.Pow(prevNormalized, curveAmount);
                        int prevYValue = (int)(Math.Round(prevCurved * 1023) * _gForceMult);

                        if (prevYValue > maxV) prevYValue = maxV;
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
            _maxPower = (int)numericUpDownMaxPower.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph();
        }

        private void numericUpDownCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            curveAmount = (double)numericUpDownCurveAmount.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph();
        }

        private void OnScaledValueUpdated(float value)
        {

            if (checkBoxTest.Checked)
                value = (float)numericUpDownTarget.Value;
            // Apply curve: value in [0,1023], curveAmount >= 0.0
            float inputValue = Math.Clamp(value, 0, 7);
            double normalized = inputValue / 7.0;
            double curved = Math.Pow(normalized, curveAmount); // 0..1
            int yValue = (int)(Math.Round(curved * 1023) * _gForceMult); // full scale
            int maxV = (int)((_maxPower / 100f) * MaxAnalogValue);
            if (yValue > maxV) yValue = maxV;


            communicator.SendValue(yValue);
        }

        private async void buttonConnect_Click(object sender, EventArgs e)
        {
            try
            {
                autoConnectCts?.Cancel();

                string[] ports = SerialPort.GetPortNames();
                if (ports.Length == 0)
                {
                    labelStatus.Text = "No device found";
                    labelStatus.ForeColor = System.Drawing.Color.Red;
                    Log("No COM ports found");
                    SetControlsEnabled(false);
                    buttonConnect.Enabled = true;
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
                        labelStatus.Text = $"Connected to Seatbelt";
                        labelStatus.ForeColor = System.Drawing.Color.Black;
                        SetControlsEnabled(true);
                        communicator.StartSendLoop(() => checkBoxTest.Checked ? (int)numericUpDownTarget.Value : 0);
                    }));
                    Log($"Manual connect: Connected to {communicator.PortName}");
                    return;
                }

                Invoke(new Action(() =>
                {
                    labelStatus.Text = "No device responded";
                    labelStatus.ForeColor = System.Drawing.Color.Red;
                    SetControlsEnabled(false);
                    buttonConnect.Enabled = true;
                }));
                Log("Manual connect: No device responded");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
                labelStatus.ForeColor = System.Drawing.Color.Red;
                Log($"Manual connect failed: {ex.Message}");
                SetControlsEnabled(false);
                buttonConnect.Enabled = true;
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

        public void SetGForceMult(float value)
        {
            _gForceMult = value;
            DrawCurveGraph();
        }

        private void numericUpDownGForceToBelt_ValueChanged(object sender, EventArgs e)
        {
            SetGForceMult((float)numericUpDownGForceToBelt.Value);
            SaveCarSettings(CarName);
        }

        private void numericUpDownGForceToBelt_ValueChanged_1(object sender, EventArgs e)
        {
            _gForceMult = (float)numericUpDownGForceToBelt.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph(); // Update graph when belt strength changes
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
    
           
            DrawCurveGraph();
          
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Save BeltStrength to settings
            Settings.Default.BeltStrength = (float)numericUpDownBeltStrength.Value;
 
            SaveCarSettings(CarName);
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

        private void SetControlsEnabled(bool enabled)
        {
            foreach (Control ctl in this.Controls)
            {
                if (ctl != buttonConnect)
                {
                    if (ctl is not Label)
                        ctl.Enabled = enabled;
                }
            }
        }

        private void LoadCarSettings(string carName)
        {
            // Load settings from file
            if (File.Exists(carSettingsFile))
            {
                try
                {
                    var json = File.ReadAllText(carSettingsFile);
                    carSettingsStore = JsonSerializer.Deserialize<CarSettingsStore>(json) ?? new CarSettingsStore();
                }
                catch { carSettingsStore = new CarSettingsStore(); }
            }
            else
            {
                carSettingsStore = new CarSettingsStore();
            }
            if (!carSettingsStore.Settings.TryGetValue(carName, out var settings))
            {
                settings = new CarSettings();
                carSettingsStore.Settings[carName] = settings;
            }
            // Apply settings to UI
            _gForceMult = settings.MaxGForceMult;
            _maxPower = settings.MaxPower;
            _curveAmount = settings.CurveAmount;
            numericUpDownGForceToBelt.Value = (decimal)settings.MaxGForceMult;
            numericUpDownMaxPower.Value = settings.MaxPower;
            numericUpDownCurveAmount.Value = (decimal)settings.CurveAmount;
        }

        private void SaveCarSettings(string carName)
        {
            var settings = new CarSettings
            {
                MaxGForceMult = (float)numericUpDownGForceToBelt.Value,
                MaxPower = (int)numericUpDownMaxPower.Value,
                CurveAmount = (double)numericUpDownCurveAmount.Value
            };
            carSettingsStore.Settings[carName] = settings;
            try
            {
                var json = JsonSerializer.Serialize(carSettingsStore, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(carSettingsFile, json);
            }
            catch { }
        }

    }
}
