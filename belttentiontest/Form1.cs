using belttentiontest.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Core;

namespace belttentiontest
{
    public partial class Form1 : Form
    {
        public bool ClosingForm { get; private set; } = false;

        public const float MAXPOSIBLEMOTORVALUE = 180;

        private SerialCommunicator communicator;
        private bool handshakeComplete = false;
        private CancellationTokenSource? autoConnectCts;

        // new: iRacing communicator
        private IracingCommunicator? irCommunicator;
        private bool? pendingIracingState = null;

        // Timer for periodic updates
        private System.Windows.Forms.Timer? updateTimer;

        // Timer for MMF updates
        private System.Windows.Forms.Timer? mmfUpdateTimer;

        private int lastCurvedValue = 0;

        private string CarName = "NA";
        private int _maxPower = 100;
        private float _gForceMult = 1f;
        private double _curveAmount = 1f;

        private float maxGForceRecorded = 0f; // Max G-Force recorded

        private string carSettingsFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");

        // Singleton instance for Form1
        private static Form1? _instance;
        public static Form1 Instance
        {
            get
            {
                if (_instance == null || _instance.IsDisposed)
                {
                    _instance = new Form1();
                }
                return _instance;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            ClosingForm = true;
            base.OnClosing(e);
        }

        // Standalone setting for auto-connect on startup
        private const string AutoConnectSettingsFile = "autoconnect.json";
        public static bool AutoConnectOnStartup { get; set; } = false;

        private static void LoadAutoConnectSetting()
        {
            try
            {
                if (File.Exists(AutoConnectSettingsFile))
                {
                    var json = File.ReadAllText(AutoConnectSettingsFile);
                    AutoConnectOnStartup = JsonSerializer.Deserialize<bool>(json);
                }
            }
            catch { AutoConnectOnStartup = false; }
        }

        private static void SaveAutoConnectSetting()
        {
            try
            {
                var json = JsonSerializer.Serialize(AutoConnectOnStartup);
                File.WriteAllText(AutoConnectSettingsFile, json);
            }
            catch { }
        }

        private double _coneringCurveAmount = 1.0; // backing field for new setting

        private MemoryMapFileWriter? _mmfWriter;

        public Form1()
        {
            LoadAutoConnectSetting();
            _instance = this;
            InitializeComponent();
            cb_AutoConnect.Checked = AutoConnectOnStartup;

            _mmfWriter = new MemoryMapFileWriter();

            // MMF update timer: call WriteSettingsToMemoryMappedFile 30 times/sec
            mmfUpdateTimer = new System.Windows.Forms.Timer();
            mmfUpdateTimer.Interval = 33; // ~30 times per second
            mmfUpdateTimer.Tick += (s, e) => WriteSettingsToMemoryMappedFile(""); // Pass actual JSON if needed
            mmfUpdateTimer.Start();

            buttonConnect.Text = "Connecting...";
            buttonConnect.Enabled = false;

            labelStatus.Text = "Scanning ports...";
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
                    mmfUpdateTimer?.Stop(); // Stop MMF update timer
                    mmfUpdateTimer?.Dispose(); // Dispose MMF update timer
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
            irCommunicator.ABSValueUpdated += OnABSValueUpdated;

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

            // Initialize and start update timer
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 100; // 100ms
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();

            LoadCarSettings(CarName);

            // Auto-connect on startup if enabled
            if (AutoConnectOnStartup)
            {
                // Fire and forget, UI will update via events
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource();
                    bool ok = await communicator.ConnectAsync(cts.Token).ConfigureAwait(false);
                    if (ok)
                    {
                        handshakeComplete = true;
                        UpdateConnectionStatusConnected();
                    }
                });
            }
        }

        // Timer tick event handler
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            bool lmotor = lb_SelectedMotor.SelectedIndex == 0;



            if (checkBoxTest.Checked)
                OnScaledValueUpdated((int)numericUpDownTarget.Value, 0, 0, lmotor);
            else
            if (IracingCommunicator.Instance != null)
                if (!IracingCommunicator.Instance.IsConnected)
                {
                    OnScaledValueUpdated(0.1f, 0, 0, false); //keep communications alive with small value when not connected to iRacing
                    OnScaledValueUpdated(0.1f, 0, 0, true); //keep communications alive     with small value when not connected to iRacing
                }

            if (_testABS)
            {
                OnABSValueUpdated();
            }
        }

        private void OnIracingConnected()
        {
            maxGForceRecorded = 0f; //reset max G-Force on new connection
            labelMaxGForce.Text = $"Max G-Force: {maxGForceRecorded:F2}";



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
            CarName = "NA";

            if (!IsHandleCreated)
            {
                pendingIracingState = false;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                textBoxIracingStatus.Text = "Iracing Not Connect";
                Log("iRacing: Not connected");
            }));
        }

        private void UpdateIracingLabel(bool connected)
        {
            textBoxIracingStatus.Text = connected ? "Iracing Connected" : "Iracing Not Connect";
            Log(connected ? "iRacing: Connected" : "iRacing: Not connected");
        }

        private void Log(string message, bool append = true)
        {
            // Log does nothing but can be used
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

        int L_MIN = 0, L_MAX = 180, R_MIN = 0, R_MAX = 180;
        bool L_INVERT = false, R_INVERT = false;
        bool DuelMotors = false;

        private void UpdateWindows()
        {
            BeginInvoke(new Action(() =>
            {
                if (lb_SelectedMotor.SelectedIndex == 1)
                {
                    nud_Motor_Start.Value = R_MIN;
                    nud_Motor_End.Value = R_MAX;
                    ck_Inverted.Checked = R_INVERT;
                }
                else
                {
                    nud_Motor_Start.Value = L_MIN;
                    nud_Motor_End.Value = L_MAX;
                    ck_Inverted.Checked = L_INVERT;
                }
                cb_duelMotors.Checked = DuelMotors;
                lblChangesNotSaved.Visible = false;
            }));
        }

        private void OnMessageReceivedFromSerial(string message)
        {
            if (message != null && message.Length > 0)
            {
                switch (message[0])
                {
                    case 'S':
                        var parsedSettings = communicator.ParseSerialLineSettings(message.Substring(1));

                        if (parsedSettings.HasValue)
                        {
                            L_MIN = parsedSettings.Value.lmin;
                            L_MAX = parsedSettings.Value.lmax;
                            R_MIN = parsedSettings.Value.rmin;
                            R_MAX = parsedSettings.Value.rmax;
                            L_INVERT = parsedSettings.Value.linvert;
                            R_INVERT = parsedSettings.Value.rinvert;
                            DuelMotors = parsedSettings.Value.both;

                            L_MIN = Math.Clamp(L_MIN, 0, (int)MAXPOSIBLEMOTORVALUE);
                            L_MAX = Math.Clamp(L_MAX, 0, (int)MAXPOSIBLEMOTORVALUE);
                            R_MIN = Math.Clamp(R_MIN, 0, (int)MAXPOSIBLEMOTORVALUE);
                            R_MAX = Math.Clamp(R_MAX, 0, (int)MAXPOSIBLEMOTORVALUE);

                            UpdateWindows();


                        }

                        return; // ignore debug lines

                    case 'N':
                        communicator.Disconnect();
                        BeginInvoke(new Action(() =>
                        {
                            ShowDisconnectedUI("Seatbelt disconnected");
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
                buttonConnect.Enabled = false;
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
                // Removed trackBarGForce update
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
                var penLat = new System.Drawing.Pen(System.Drawing.Color.Red, 2); // Red for lateral
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
                int maxV = (int)(((float)_maxPower / 100f) * MAXPOSIBLEMOTORVALUE);
                // Draw main curve (blue)
                for (int x = 0; x < graphWidth; x++)
                {
                    float inputValue = (float)(x * 7.0 / (graphWidth - 1)); // X axis: 0..7 (float)
                    double normalized = inputValue / 7.0;
                    double curved = Math.Pow(normalized, _curveAmount); // 0..1
                    int yValue = (int)(Math.Round(curved * MAXPOSIBLEMOTORVALUE) * _gForceMult); // full scale
                    if (yValue > maxV) yValue = maxV;
                    int y = graphHeight - 1 - (int)(yValue / MAXPOSIBLEMOTORVALUE * (graphHeight - 1)); // Y axis: 0..1023
                    int drawX = xPadding + x;
                    if (x > 0)
                    {
                        float prevInputValue = (float)((x - 1) * 7.0 / (graphWidth - 1));
                        double prevNormalized = prevInputValue / 7.0;
                        double prevCurved = Math.Pow(prevNormalized, _curveAmount);
                        int prevYValue = (int)(Math.Round(prevCurved * MAXPOSIBLEMOTORVALUE) * _gForceMult);
                        if (prevYValue > maxV) prevYValue = maxV;
                        int prevY = graphHeight - 1 - (int)(prevYValue / MAXPOSIBLEMOTORVALUE * (graphHeight - 1));
                        int prevDrawX = xPadding + x - 1;
                        g.DrawLine(pen, prevDrawX, prevY, drawX, y);
                    }
                }
                // Draw lateral load curve (red)
                float corneringStrength = (float)nud_coneringStrengh.Value;
                for (int x = 0; x < graphWidth; x++)
                {
                    float inputValue = (float)(x * 5.0 / (graphWidth - 1)); // X axis: 0..5 (float, only positive)
                    float lat_normal = inputValue / 5.0f; // normalize to 0..1
                    double lat_curved = Math.Pow(lat_normal, _coneringCurveAmount);
                    float latValue = (float)lat_curved * 5f * corneringStrength; // scale and apply strength
                    // Map to Y axis (full scale)
                    int y = graphHeight - 1 - (int)(latValue / 7.0 * (graphHeight - 1));
                    int drawX = xPadding + (int)(inputValue / 7.0 * (graphWidth - 1));
                    if (x > 0)
                    {
                        float prevInputValue = (float)((x - 1) * 5.0 / (graphWidth - 1));
                        float prevLat_normal = prevInputValue / 5.0f;
                        double prevLat_curved = Math.Pow(prevLat_normal, _coneringCurveAmount);
                        float prevLatValue = (float)prevLat_curved * 5f * corneringStrength;
                        int prevY = graphHeight - 1 - (int)(prevLatValue / 7.0 * (graphHeight - 1));
                        int prevDrawX = xPadding + (int)(prevInputValue / 7.0 * (graphWidth - 1));
                        g.DrawLine(penLat, prevDrawX, prevY, drawX, y);
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
            _curveAmount = (double)numericUpDownCurveAmount.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph();
        }


        private bool _testABS = false;
        private void OnABSValueUpdated()
        {
            if (cb_ABS_Enabled.Checked)
                communicator.SendABS((int)nud_ABS.Value);
        }

        private float _displayGForce = 0, _displayLatForce = 0, _displayVForce = 0;



        private void OnScaledValueUpdated(float simBrakingValue, float SimConeringValue, float SimVeriticalValue, bool lMotor)
        {
            if (checkBoxTest.Checked)
                simBrakingValue = (float)numericUpDownTarget.Value;
               
            
            MotorSettings lmotorSettings = new MotorSettings
            {
                MaxPower = _maxPower,
                GForceMult = _gForceMult,
                CurveAmount = (float)_curveAmount,
                ConeringCurveAmount = (float)_coneringCurveAmount,
                ConeringStrengh = (float)nud_coneringStrengh.Value,
                VerticalStrengh = (float)nudVertical.Value,
                Min = lMotor ? L_MIN : R_MIN,
                Max = lMotor ? L_MAX : R_MAX,
                Invert = lMotor ? L_INVERT : R_INVERT,
            };

            MotorOutputValues value = lmotorSettings.CalculateMotorValue(simBrakingValue, SimConeringValue, SimVeriticalValue);
        
            float yValue = value.CalcluateMotorSignalOutput(lmotorSettings);

            _displayGForce = value.BreakingForce;
            _displayLatForce = lMotor ? -value.ConeringForce : value.ConeringForce;
            _displayVForce = value.VerticalForce;

            communicator.SendValue(yValue, lMotor);
        }

        public string LabelStatus
        {
            get { return labelStatus.Text; }
            set
            {
                Invoke(new Action(() =>
                {
                    labelStatus.Text = value;
                    labelStatus.ForeColor = Color.Red;
                }
                ));
            }
        }

        public void UpdateABSStatus(bool active)
        {
            Invoke(new Action(() =>
            {
                lb_ABS_Status.Text = active ? "ABS Active" : "ABS Inactive";
                lb_ABS_Status.ForeColor = active ? System.Drawing.Color.Red : System.Drawing.Color.Black;
            }));
        }

        public void UpdateConnectionStatusConnected()
        {
            Invoke(new Action(() =>
            {
                labelStatus.Text = $"Connected to Seatbelt";
                labelStatus.ForeColor = System.Drawing.Color.Black;
                SetControlsEnabled(true);
                buttonConnect.Enabled = false;
                buttonConnect.Text = "Connect";
                communicator.StartSendLoop(() => checkBoxTest.Checked ? (int)numericUpDownTarget.Value : 0);
            }));
            Log($"Manual connect: Connected to {communicator.PortName}");
        }

        private async void buttonConnect_Click(object sender, EventArgs e)
        {
            try
            {
                autoConnectCts?.Cancel();

                buttonConnect.Text = "Connecting...";
                buttonConnect.Enabled = false;

                Invoke(new Action(() => labelStatus.Text = "Scanning ports..."));

                using var manualCts = new CancellationTokenSource();
                bool ok = await communicator.ConnectAsync(manualCts.Token).ConfigureAwait(false);
                if (ok)
                {
                    handshakeComplete = true;
                    UpdateConnectionStatusConnected();
                    return;
                }

                Invoke(new Action(() =>
                {
                    labelStatus.Text = "No device responded";
                    labelStatus.ForeColor = System.Drawing.Color.Red;
                    SetControlsEnabled(false);
                    buttonConnect.Text = "Connect";
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
                buttonConnect.Text = "Connect";
                buttonConnect.Enabled = true;
            }
        }

        private void labelStatus_Click(object sender, EventArgs e)
        {

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


            // Select the first item in lb_SelectedMotor by default if available
            if (lb_SelectedMotor != null && lb_SelectedMotor.Items.Count > 0)
            {
                lb_SelectedMotor.SelectedIndex = 0;
            }
            DrawCurveGraph();

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {

            SaveCarSettings(CarName);
            Settings.Default.Save();
            updateTimer?.Stop();
            updateTimer?.Dispose();
            mmfUpdateTimer?.Stop(); // Stop MMF update timer
            mmfUpdateTimer?.Dispose(); // Dispose MMF update timer
            _mmfWriter?.Dispose();
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
                if (ctl != buttonConnect && ctl != cb_AutoConnect)
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
                    CarSettingsStore.Instance = JsonSerializer.Deserialize<CarSettingsStore>(json) ?? new CarSettingsStore();
                }
                catch { CarSettingsStore.Instance = new CarSettingsStore(); }
            }
            else
            {
                CarSettingsStore.Instance = new CarSettingsStore();
            }

            // If no settings for carName, try to copy from NA
            if (!CarSettingsStore.Instance.Settings.TryGetValue(carName, out var settings))
            {
                if (CarSettingsStore.Instance.Settings.TryGetValue("NA", out var naSettings))
                {
                    // Deep copy NA settings to new car
                    settings = JsonSerializer.Deserialize<CarSettings>(JsonSerializer.Serialize(naSettings));
                }
                else
                {
                    settings = new CarSettings();
                }
                CarSettingsStore.Instance.Settings[carName] = settings;
                // Save immediately so the new car gets its own settings file entry
                SaveCarSettings(carName);
            }
            // Apply settings to UI
            _gForceMult = settings.MaxGForceMult;
            _maxPower = settings.MaxPower;
            _curveAmount = settings.CurveAmount;
            numericUpDownGForceToBelt.Value = (decimal)settings.MaxGForceMult;
            numericUpDownMaxPower.Value = settings.MaxPower;
            numericUpDownCurveAmount.Value = (decimal)settings.CurveAmount;
            nud_coneringStrengh.Value = (decimal)settings.CorneringStrength;
            nudVertical.Value = (decimal)settings.VerticalStrength; // NEW
            if (settings.AbsStrength < 3)
                settings.AbsStrength = 3;
            nud_ABS.Value = (int)settings.AbsStrength; // NEW
            cb_ABS_Enabled.Checked = settings.AbsEnabled; // NEW
            irCommunicator.ABSStrength = settings.AbsStrength;
            cb_invert_conering.Checked = settings.InvertCornering; // NEW
            irCommunicator.InvertCornering = settings.InvertCornering;
            // Set new setting to UI
            _coneringCurveAmount = settings.ConeringCurveAmount;
            nud_ConeringCurveAmount.Value = (decimal)settings.ConeringCurveAmount;
        }

        private void SaveCarSettings(string carName)
        {
            var settings = new CarSettings
            {
                MaxGForceMult = (float)numericUpDownGForceToBelt.Value,
                MaxPower = (int)numericUpDownMaxPower.Value,
                CurveAmount = (double)numericUpDownCurveAmount.Value,
                CorneringStrength = (float)nud_coneringStrengh.Value,
                VerticalStrength = (float)nudVertical.Value, // NEW
                AbsStrength = (float)nud_ABS.Value, // NEW
                AbsEnabled = cb_ABS_Enabled.Checked, // NEW
                InvertCornering = cb_invert_conering.Checked, // NEW
                ConeringCurveAmount = (double)nud_ConeringCurveAmount.Value // NEW
            };
            CarSettingsStore.Instance.Settings[carName] = settings;
            try
            {
                var json = JsonSerializer.Serialize(CarSettingsStore.Instance, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(carSettingsFile, json);
                
            }
            catch { }
        }

        private void WriteSettingsToMemoryMappedFile(string json)
        {
            if (this.CarName == string.Empty || this.CarName == null)
                return;

            var structSettings = new MemoryMapFileFormat
            {

                CarName = this.CarName,
                MaxGForceMult = (float)numericUpDownGForceToBelt.Value,
                MaxPower = _maxPower,
                CurveAmount =_curveAmount,
                CorneringStrength = (float)nud_coneringStrengh.Value,
                VerticalStrength = (float)nudVertical.Value,
                AbsStrength = (float)nud_ABS.Value,
                AbsEnabled = (byte)(cb_ABS_Enabled.Checked ? 1 : 0),
                InvertCornering = (byte)(cb_invert_conering.Checked ? 1 : 0),
                ConeringCurveAmount = (float)nud_ConeringCurveAmount.Value,

                GForce = _displayGForce,
                LateralG = _displayLatForce,
                VerticalG = _displayVForce,

                ConnectedToSim = irCommunicator != null ? irCommunicator.IsConnected : false,

            };
            _mmfWriter?.WriteSettings(structSettings);
        }

        private void nud_Motor_Start_ValueChanged(object sender, EventArgs e)
        {
            if (lb_SelectedMotor.SelectedIndex == 1)
                R_MIN = (int)nud_Motor_Start.Value;
            else
                L_MIN = (int)nud_Motor_Start.Value;
            ShowChangesNotSaved();
        }

        private void nud_Motor_End_ValueChanged(object sender, EventArgs e)
        {
            if (lb_SelectedMotor.SelectedIndex == 1)
                R_MAX = (int)nud_Motor_End.Value;
            else
                L_MAX = (int)nud_Motor_End.Value;
            ShowChangesNotSaved();
        }

        private void bnt_Inverted_CheckedChanged(object sender, EventArgs e)
        {
            if (lb_SelectedMotor.SelectedIndex == 1)
                R_INVERT = ck_Inverted.Checked;
            else
                L_INVERT = ck_Inverted.Checked;
            ShowChangesNotSaved();
        }

        private void cb_duelMotors_CheckedChanged(object sender, EventArgs e)
        {
            DuelMotors = cb_duelMotors.Checked;
            ShowChangesNotSaved();
        }

        private void ShowChangesNotSaved()
        {
            lblChangesNotSaved.Visible = true;
        }

        private async void bnt_Apply_Click(object sender, EventArgs e)
        {
            communicator.SendUpdatedSettings(L_MIN, L_MAX, R_MIN, R_MAX, L_INVERT, R_INVERT, DuelMotors);
            lblSettingsSaved.Visible = true;
            lblChangesNotSaved.Visible = false;
            await Task.Delay(1500);
            lblSettingsSaved.Visible = false;
        }

        private void lb_SelectedMotor_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateWindows();
        }

        private void nud_coneringStrengh_ValueChanged(object sender, EventArgs e)
        {

            SaveCarSettings(CarName);
        }

        private void nud_coneringStrengh_ValueChanged_1(object sender, EventArgs e)
        {

            SaveCarSettings(CarName);
            DrawCurveGraph();
        }



        private void cb_testGFroce_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void sl_horGforce_Scroll(object sender, EventArgs e)
        {

        }

        private void nudVertical_ValueChanged(object sender, EventArgs e)
        {
            if (!CarSettingsStore.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.VerticalStrength = (float)nudVertical.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph();
        }

        private void nud_ABS_ValueChanged(object sender, EventArgs e)
        {
            if (!CarSettingsStore.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.AbsStrength = (float)nud_ABS.Value;
            irCommunicator.ABSStrength = settings.AbsStrength;
            SaveCarSettings(CarName);
        }

        private void cb_ABS_Enabled_CheckedChanged(object sender, EventArgs e)
        {
            if (!CarSettingsStore.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.AbsEnabled = cb_ABS_Enabled.Checked;
            SaveCarSettings(CarName);
        }


        private void bnt_testABS_Click(object sender, EventArgs e)
        {
            if (_testABS)
                return;
            _testABS = true;
            Task.Delay(2000).ContinueWith(_ => _testABS = false);
        }

        private void groupBox2_Enter(object sender, EventArgs e)
        {

        }

        private void cb_invert_conering_CheckedChanged(object sender, EventArgs e)
        {
            irCommunicator.InvertCornering = cb_invert_conering.Checked;
        }

        private void cb_AutoConnect_CheckedChanged(object sender, EventArgs e)
        {
            AutoConnectOnStartup = cb_AutoConnect.Checked;
            SaveAutoConnectSetting();
        }

        private void nud_ConeringCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            _coneringCurveAmount = (double)nud_ConeringCurveAmount.Value;
            SaveCarSettings(CarName);
            DrawCurveGraph();
        }
    }
}
