using BeltTentionerLib;
using belttentiontest.Controls;
using belttentiontest.Properties;
using SharedResources;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Ports;
using System.Runtime.ConstrainedExecution;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Core;

using BeltAPI;
namespace belttentiontest
{
    public partial class Form1 : Form
    {
        public bool ClosingForm { get; private set; } = false;

        public const float MAXPOSIBLEMOTORVALUE = 180;

        private BeltSerialDevice communicator;
        private bool handshakeComplete = false;
        private CancellationTokenSource? autoConnectCts;

        // new: iRacing communicator
        private IracingCommunicator? irCommunicator;
        private bool? pendingIracingState = null;

        // Timer for periodic updates
        private System.Windows.Forms.Timer? updateTimer;

        private System.Windows.Forms.Timer? simhub_Tel_Timer;

        // Timer for MMF updates
        private System.Windows.Forms.Timer? mmfUpdateTimer;

        private int lastCurvedValue = 0;

        private string CarName = "NA";
        private int _maxPower = 100;
        private float _gForceMult = 1f;
        private double _curveAmount = 1f;

        private float maxGForceRecorded = 0f; // Max G-Force recorded

        private string carSettingsFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");

        private TelemetryMmfReader? telemetryReader;

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

        // Auto-connect settings persisted to autoconnect.json
        public class AppSettings
        {
            public bool AutoConnectOnStartup { get; set; } = false;
            public bool UseSimHub { get; set; } = false;
            public bool UseIracing { get; set; } = true;
        }

        public static AppSettings applicatoinSettings { get; set; } = new AppSettings();

        private const string AutoConnectSettingsFile = "autoconnect.json";

        private static void LoadAutoConnectSetting()
        {
            try
            {
                if (!File.Exists(AutoConnectSettingsFile))
                {
                    applicatoinSettings = new AppSettings();
                    return;
                }

                var json = File.ReadAllText(AutoConnectSettingsFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    applicatoinSettings = new AppSettings();
                    return;
                }

                // support legacy single-bool file (true/false)
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith('{'))
                {
                    applicatoinSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    // legacy format
                    if (bool.TryParse(json.Trim(), out var legacy))
                    {
                        applicatoinSettings = new AppSettings { AutoConnectOnStartup = legacy };
                    }
                    else
                    {
                        applicatoinSettings = new AppSettings();
                    }
                }
            }
            catch
            {
                applicatoinSettings = new AppSettings();
            }
        }

        private static void SaveAutoConnectSetting()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(applicatoinSettings, opts);
                File.WriteAllText(AutoConnectSettingsFile, json);
            }
            catch { }
        }

        private double _coneringCurveAmount = 1.0; // backing field for new setting

        private MemoryMapFileWriter? _mmfWriter;

        public static void StopTimers()
        {
            Instance?.mmfUpdateTimer?.Stop();
        }
        bool _isLoading = false;

        System.Windows.Forms.Timer belttentionerUpdate;

        public Form1()
        {
            _isLoading = true;
            LoadAutoConnectSetting();
            _instance = this;
            InitializeComponent();



            // Silent update check on startup (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var info = await Updater.GetUpdateInfoAsync().ConfigureAwait(false);
                    if (info != null && info.IsUpdateAvailable)
                    {
                        // show notify icon on UI thread
                        try
                        {
                            BeginInvoke(new Action(() => ShowUpdateAvailableNotification(info)));
                        }
                        catch
                        {
                            // ignore UI errors
                        }
                    }
                }
                catch { }
            });

            this.Text = $"Belt Tensioner V{AboutBox.Version}";
            ThinTrackBar.Bind(_ttb_maxOutput, numericUpDownMaxPower);
            ThinTrackBar.Bind(_ttb_restingPoint, percentageUpDownRestingPoint);

            ThinTrackBar.Bind(_ttb_brakingCurve, numericUpDownCurveAmount);
            ThinTrackBar.Bind(_ttb_brakingStr, numericUpDownGForceToBelt);

            ThinTrackBar.Bind(_ttb_corneringCurve, nud_ConeringCurveAmount);
            ThinTrackBar.Bind(_ttb_corneringStr, nud_coneringStrengh);

            ThinTrackBar.Bind(_ttb_verStr, nudVertical);

            cb_AutoConnect.Checked = applicatoinSettings.AutoConnectOnStartup;

            // custom paint for braking groupbox border
            _gb_Braking.Paint += _gb_Braking_Paint;
            // custom paint for cornering groupbox border (green)
            _gb_cornering.Paint += _gb_cornering_Paint;

            _gb_vertical.Paint += _gb_vertical_Paint;

            _mmfWriter = new MemoryMapFileWriter();

            // MMF update timer: call WriteSettingsToMemoryMappedFile 30 times/sec
            mmfUpdateTimer = new System.Windows.Forms.Timer();
            mmfUpdateTimer.Interval = 33; // ~30 times per second
            mmfUpdateTimer.Tick += (s, e) => WriteSettingsToMemoryMappedFile(""); // Pass actual JSON if needed
            mmfUpdateTimer.Start();

            simhub_Tel_Timer = new System.Windows.Forms.Timer();
            simhub_Tel_Timer.Interval = 16; // 16 times a second (~60Hz)
            simhub_Tel_Timer.Tick += (s, e) => GetSimHubData();
            simhub_Tel_Timer.Start();

            belttentionerUpdate = new System.Windows.Forms.Timer();
            belttentionerUpdate.Interval = (int)(1000f / 60f); // ~60 times per second
            belttentionerUpdate.Tick += (s, e) => UpdateBeltTentionFeedback();
            belttentionerUpdate.Start();
            
            

            buttonConnect.Text = "Connecting...";
            buttonConnect.Enabled = false;

            _of_seatbeltDevice.Text = "Scanning...";
            SetControlsEnabled(false);
            buttonConnect.Enabled = true;

            communicator = new BeltSerialDevice();
            communicator.MessageReceived += OnMessageReceivedFromSerial;
            communicator.HandshakeComplete += OnHandshakeCompleteFromSerial;

            WindowsMessageBridge.BeltMessageReceived += (msg) =>
            {
                switch (msg.Type)
                {
                    case BeltMessageType.GForce:
                        numericUpDownGForceToBelt.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.GCurve:
                        numericUpDownCurveAmount.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.VForce:
                        nudVertical.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.CForce:
                        nud_coneringStrengh.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.CCurve:
                        nud_ConeringCurveAmount.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.MaxOutput:
                        numericUpDownMaxPower.Value = (decimal)msg.Value;
                        break;

                    case BeltMessageType.InvertConeringForces:
                        cb_invert_sway.Checked = msg.Value != 0;
                        break;
                    case BeltMessageType.ABSEnabled:
                        cb_ABS_Enabled.Checked = msg.Value != 0;
                        break;
                    case BeltMessageType.ABSStrength:
                        nud_ABS.Value = (decimal)msg.Value;
                        break;
                }
            };
            // Auto-detection on startup disabled. Manual connect via button only.

            // Ensure cancellation when form closes
            this.FormClosed += (_, __) => autoConnectCts?.Cancel();

            // Ensure communicator stopped and disposed when form closes
            this.FormClosing += async (s, e) =>
            {
                try
                {
                    autoConnectCts?.Cancel();
                   
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
            irCommunicator.ScaledValueUpdated += UpdateBeltTensionerForces;
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
            if (applicatoinSettings.AutoConnectOnStartup)
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

            // Add Help menu with About...
            var menuStrip = new MenuStrip();
            var menuSystem = new ToolStripMenuItem("Settings");
            IracingCommunicator.Instance.Enabled = applicatoinSettings.UseIracing;
            var menuUseIracing = new ToolStripMenuItem("Use IRacing Telemetry");
            menuUseIracing.CheckOnClick = true;
            menuUseIracing.Checked = applicatoinSettings.UseIracing;
            var menuUseSimHub = new ToolStripMenuItem("Use SimHub Telemetry");
            menuUseIracing.Click += (s, e) =>
            {
                applicatoinSettings.UseIracing = menuUseIracing.Checked;
                _of_Control.Enabled = applicatoinSettings.UseIracing;
                IracingCommunicator.Instance.Enabled = applicatoinSettings.UseIracing;
                if (applicatoinSettings.UseIracing)
                {
                    applicatoinSettings.UseSimHub = false;
                    menuUseSimHub.Checked = applicatoinSettings.UseSimHub;
                    _of_simHub.Enabled = applicatoinSettings.UseSimHub;
                    _gb_simhub.Enabled = false;
                }

                SaveAutoConnectSetting();
            };


            menuUseSimHub.CheckOnClick = true;
            menuUseSimHub.Checked = applicatoinSettings.UseSimHub;
            menuUseSimHub.Click += (s, e) =>
            {
                applicatoinSettings.UseSimHub = menuUseSimHub.Checked;
                _of_simHub.Enabled = applicatoinSettings.UseSimHub;
                _gb_simhub.Enabled = true;
                if (applicatoinSettings.UseSimHub)
                {
                    applicatoinSettings.UseIracing = false;
                    menuUseIracing.Checked = applicatoinSettings.UseIracing;
                    _of_Control.Enabled = applicatoinSettings.UseIracing;
                    IracingCommunicator.Instance.Enabled = applicatoinSettings.UseIracing;
                }

                SaveAutoConnectSetting();
            };

            var installSimHubPlugin = new ToolStripMenuItem("Install SimHub Plugin");
            installSimHubPlugin.Click += (s, e) =>
            {

            };

            var updateMenuItem = new ToolStripMenuItem("Check for Updates...");
            updateMenuItem.Click += async (s, e) => await Updater.CheckForUpdatesAsync(this);
            var aboutMenuItem = new ToolStripMenuItem("About...");
            aboutMenuItem.Click += (s, e) => ShowAboutBox();
            menuSystem.DropDownItems.Add(menuUseIracing);
            menuSystem.DropDownItems.Add(menuUseSimHub);
            menuSystem.DropDownItems.Add(updateMenuItem);
            menuSystem.DropDownItems.Add(aboutMenuItem);
            menuStrip.Items.Add(menuSystem);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);


            if (!applicatoinSettings.UseSimHub)
                _of_simHub.Enabled = false;

            if (!applicatoinSettings.UseIracing)
                _of_Control.Enabled = false;
            _isLoading = false;
        }

        bool _simHubConnected = false;

        TelemetrySharedData _simhub_Telemetry;
        int _nextSimHubFrameCheck = 60;
        string lastGameName = "";

        bool _simHub_SupportBraking = false;
        bool _simHub_SupportCornering = false;
        bool _simHub_SupportVertical = false;
        bool _simhub_Paused = false;
        private void GetSimHubData()
        {
            if (!applicatoinSettings.UseSimHub)
                return;

            if (telemetryReader == null)
            {
                _simHubConnected = false;

                _nextSimHubFrameCheck--;
                if (_nextSimHubFrameCheck <= 0)
                {

                    telemetryReader = new TelemetryMmfReader();
                    _nextSimHubFrameCheck = 60;
                    if (telemetryReader.Connected)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            Log("Connected to SimHub telemetry MMF");

                            _of_simHub.IsOn = true;
                            BeginInvoke(new Action(() =>
                            {
                                if (!_simhub_Telemetry.GameRunning)
                                    lb_simhub.Text = $"No Game Detected";
                                else
                                    lb_simhub.Text = $"Game: {_simhub_Telemetry.GameName}";
                                _gb_simhub.Enabled = true;
                            }));


                        }));
                    }
                }
                return;
            }
            else
            if (!telemetryReader.Connected)
            {
                _of_simHub.IsOn = false;

                telemetryReader.Dispose();
                telemetryReader = null;

                BeginInvoke(new Action(() =>
                {
                    Log("Disconnected from SimHub telemetry MMF");
                    lb_simhub.Text = $"Not Connected to SimHub";
                    _gb_simhub.Enabled = false;
                }));

                return;
            }
            _simhub_Telemetry = telemetryReader.Read();

            if (_simhub_Telemetry.Paused != _simhub_Paused)
            {
                _simhub_Paused = _simhub_Telemetry.Paused;
                BeginInvoke(new Action(() =>
                {
                    if (_simhub_Paused)
                        _lb_menu.Text = "In Menu";
                    else
                        _lb_menu.Text = "In Game";
                }));
            }
            if (_simhub_Telemetry.GameRunning)
            {
                _simHubConnected = true;

                if (!applicatoinSettings.UseIracing)
                {
                    if (_simhub_Telemetry.GameName != lastGameName)
                    {
                        lastGameName = _simhub_Telemetry.GameName;
                        BeginInvoke(new Action(() =>
                        {
                            if (string.IsNullOrWhiteSpace(_simhub_Telemetry.GameName))
                                lb_simhub.Text = $"No Game Detected";
                            else
                                lb_simhub.Text = $"Game: {_simhub_Telemetry.GameName}";
                        }));

                        if (_simhub_Telemetry.GameName != string.Empty)
                            if (CarName != _simhub_Telemetry.CarName)
                            {
                                CarName = _simhub_Telemetry.CarName;
                                LoadCarSettings($"{_simhub_Telemetry.GameName}-{CarName}");
                                BeginInvoke(new Action(() =>
                                {
                                    lb_carName.Text = CarName;
                                }));
                            }
                    }

                    if (!_simhub_Telemetry.Paused)
                    {
                        var brake = _simhub_Telemetry.Braking / 9.81f;
                        var corn = _simhub_Telemetry.Cornering / 9.81f;
                        var ver = _simhub_Telemetry.Vertical / 9.81f;

                        if (_simHub_SupportBraking != _simhub_Telemetry.SupportBraking)
                        {
                            _simHub_SupportBraking = _simhub_Telemetry.SupportBraking;
                            BeginInvoke(new Action(() =>
                            {
                                _on_supportBrake.IsOn = _simHub_SupportBraking;
                            }));
                        }
                        if (_simHub_SupportCornering != _simhub_Telemetry.SupportCornering)
                        {
                            _simHub_SupportCornering = _simhub_Telemetry.SupportCornering;
                            BeginInvoke(new Action(() =>
                            {
                                _onSupportCorn.IsOn = _simHub_SupportCornering;
                            }));
                        }
                        if (_simHub_SupportVertical != _simhub_Telemetry.SupportVertical)
                        {
                            _simHub_SupportVertical = _simhub_Telemetry.SupportVertical;
                            BeginInvoke(new Action(() =>
                            {
                                _on_supoortVer.IsOn = _simHub_SupportVertical;
                            }));
                        }


                        float lcorn = 0, rcorn = 0;
                        if (corn < 0) //turning left
                        {
                            lcorn = Math.Abs(corn);
                            rcorn = 0;
                        }
                        else //turning right
                        {
                            rcorn = Math.Abs(corn);
                            lcorn = 0;
                        }


                        //OnScaledValueUpdated(brake, rcorn, ver, false);
                        UpdateBeltTensionerForces(brake, lcorn, ver);
                    }
                    else
                    {
                        //   OnScaledValueUpdated(0, 0, 0, false);
                        UpdateBeltTensionerForces(0, 0, 0);
                    }
                }
            }
            else
            {
                _simHubConnected = false;

                if (lastGameName != _simhub_Telemetry.GameName)
                {
                    if (string.IsNullOrWhiteSpace(_simhub_Telemetry.GameName))
                        lb_simhub.Text = $"No Game Detected";
                    else
                        lb_simhub.Text = $"Game: {_simhub_Telemetry.GameName}";
                }
            }
        }

        // Timer tick event handler
        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {


            if (!communicator.IsConnected)
                return;

            if (!applicatoinSettings.UseIracing)
                return;

            if (checkBoxTest.Checked)
                OnScaledValueUpdated((int)numericUpDownTarget.Value, 0, 0);
            else
            if (IracingCommunicator.Instance != null)
                if (!IracingCommunicator.Instance.IsConnected)
                {

                    OnScaledValueUpdated(0.1f, 0, 1); //keep communications alive     with small value when not connected to iRacing
                }

            if (_testABS)
            {
                OnABSValueUpdated();
            }
        }

        private void OnIracingConnected()
        {
            if (!applicatoinSettings.UseIracing)
                return;

            maxGForceRecorded = 0f; //reset max G-Force on new connection
            labelMaxGForce.Text = $"Max G-Force: {maxGForceRecorded:F2}";



            if (!IsHandleCreated)
            {
                pendingIracingState = true;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                _of_Control.IsOn = true;
                Log("iRacing: Connected");
            }));
        }

        private void OnIracingDisconnected()
        {
            SaveSoon();
            CarName = "NA";
            LoadCarSettings(CarName);
            lb_carName.Text = CarName;

            if (!IsHandleCreated)
            {
                pendingIracingState = false;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                _of_Control.IsOn = false;
                Log("iRacing: Not connected");
            }));
        }

        private void UpdateIracingLabel(bool connected)
        {
            _of_Control.IsOn = connected;
            Log(connected ? "IRacing: Connected" : "IRacing: Not connected");
        }

        private void Log(string message, bool append = true)
        {
            // Log does nothing but can be used
        }

        private void ShowDisconnectedUI(string reason = "Device disconnected")
        {
            _of_seatbeltDevice.Text = reason;
            _of_seatbeltDevice.IsOn = false;

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

        private bool _motorSettingsLoaded = false;
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
                            _motorSettingsLoaded = true;
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

                return;
            }

            if (message == "DEVICE_UNPLUGGED")
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        ShowDisconnectedUI("Device unplugged");
                    }));
                }
                catch { }
                return;
            }
        }

        private void OnHandshakeCompleteFromSerial()
        {
            handshakeComplete = true;
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {

                _of_seatbeltDevice.Text = $"Handshake complete";
                _of_seatbeltDevice.IsOn = true;
                Log($"Handshake complete on {communicator.PortName}");
                SetControlsEnabled(true);
                buttonConnect.Enabled = false;
                // start periodic sending using numericUpDownTarget's value getter
              
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

        private float _lastLongForceInput = 0f;
        private float _lastLatForceInput = 0f;
        private float _lastVertForceInput = 0f;

        private void DrawCurveGraph()
        {
            if (pictureBoxCurveGraph == null) return;
            int width = pictureBoxCurveGraph.Width;
            int height = pictureBoxCurveGraph.Height;
            int axisSpace = 20;
            int xPadding = 12;
            int graphWidth = width - 2 * xPadding;
            int graphHeight = height - axisSpace;
            var bmp = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.White);
                var penMain = new System.Drawing.Pen(System.Drawing.Color.Blue, 2);
                var penLat = new System.Drawing.Pen(System.Drawing.Color.Green, 2);
                var penVer = new System.Drawing.Pen(System.Drawing.Color.Orange, 2);
                var penMax = new System.Drawing.Pen(System.Drawing.Color.OrangeRed, 2);
                var penResting = new System.Drawing.Pen(System.Drawing.Color.Cyan, 2);
                var penPreview = new System.Drawing.Pen(System.Drawing.Color.Purple, 2);
                var font = new System.Drawing.Font("Arial", 8);
                var brush = System.Drawing.Brushes.Black;

                // Draw Y axis label (rotated)
                string yLabel = "Output To Belt";
                var yLabelSize = g.MeasureString(yLabel, font);
                g.TranslateTransform(0, (height - axisSpace) / 2);
                g.RotateTransform(-90);
                g.DrawString(yLabel, font, brush, -(yLabelSize.Width / 2), 0);
                g.ResetTransform();

                // Draw axes
                g.DrawLine(System.Drawing.Pens.Black, xPadding, 0, xPadding, graphHeight - 1);
                g.DrawLine(System.Drawing.Pens.Black, xPadding, graphHeight - 1, xPadding + graphWidth - 1, graphHeight - 1);

                // Draw X axis ticks and labels
                int numTicks = 9;
                for (int i = -2; i <= numTicks; i++)
                {
                    float tickValue = i;
                    int tickX = xPadding + (int)Math.Round(tickValue / 10.0 * (graphWidth - 1));
                    int tickY = height - 16;
                    g.DrawLine(System.Drawing.Pens.Black, tickX, tickY - 4, tickX, tickY);
                    string tickLabel = (tickValue - 2).ToString("0.##");
                    var tickLabelSize = g.MeasureString(tickLabel, font);
                    g.DrawString(tickLabel, font, brush, tickX - tickLabelSize.Width / 2, tickY - tickLabelSize.Height + 12);
                }

                // Prepare settings
                MotorSettings settings = new MotorSettings
                {
                    MaxPower = _maxPower,
                    SurgeStrength = _gForceMult,
                    CurveAmount = (float)_curveAmount,
                    ConeringCurveAmount = (float)_coneringCurveAmount,
                    SwayStrength = (float)nud_coneringStrengh.Value,
                    HeaveStrength = (float)nudVertical.Value,
                    LeftMinimumAngle = 0,
                    LeftMaximumAngle = 90,
                    LeftInverted = false
                };
                float min = settings.LeftMinimumAngle;
                float max = settings.LeftMaximumAngle;
                if (min > max) (min, max) = (max, min);
                float motorRange = max - min;
                if (motorRange == 0) motorRange = 1;

                // Helper for Y mapping
                int MapY(float yValue) => (int)((1.0f - (Math.Clamp(yValue, min, max) - min) / motorRange) * (graphHeight - 1));

                // Draw max output as a solid yellow line
                float maxOutput = min + (motorRange * (_maxPower / 100f));
                int yMax = MapY(maxOutput);
                g.DrawLine(penMax, xPadding, yMax, xPadding + graphWidth - 1, yMax);

                int yResting = MapY(motorRange * ((float)percentageUpDownRestingPoint.Value / 100f));
                g.DrawLine(penResting, xPadding, yResting, xPadding + graphWidth - 1, yResting);
                // Main curve (longitudinal G)
                int? prevY = null, prevX = null;

                if (_cb_showBraking.Checked)
                    for (int x = -47; x < graphWidth; x++)
                    {


                        float inputValue = (float)(x / (float)graphWidth) * 10;
                        if (inputValue > MotorSettings.SurgeGForceScale)
                            break;
                        BeltMotorData output = settings.Setup(inputValue, 0, 0, (int)percentageUpDownRestingPoint.Value);
                        float yValue = output.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings);
                        int y = MapY(yValue);
                        int drawX = xPadding + x + 47;
                        if (prevY.HasValue)
                            g.DrawLine(penMain, prevX.Value, prevY.Value, drawX, y);
                        prevY = y;
                        prevX = drawX;
                    }

                // Lateral curve (cornering G)
                prevY = null; prevX = null;
                if (_cb_showCorn.Checked)
                    for (int x = 0; x < graphWidth; x++)
                    {
                        float inputValue = (float)(x / (float)graphWidth) * 10;

                        if (inputValue > MotorSettings.SwayGForceScale)
                            break;
                        BeltMotorData output = settings.Setup(0, inputValue, 0, (int)percentageUpDownRestingPoint.Value);
                        float yValue = output.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings);
                        int drawX = 47 + xPadding + x;
                        int y = MapY(yValue);
                        if (prevY.HasValue)
                            g.DrawLine(penLat, prevX.Value, prevY.Value, drawX, y);
                        prevY = y;
                        prevX = drawX;
                    }

                prevY = null; prevX = null;

                if (_cb_showVer.Checked)
                    for (int x = -47; x < graphWidth; x++)
                    {
                        float inputValue = (float)(x / (float)graphWidth) * 10;
                        BeltMotorData output = settings.Setup(0, 0, inputValue, (int)percentageUpDownRestingPoint.Value);

                        float yValue = output.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings);
                        if (inputValue > MotorSettings.HeaveGForceScale)
                            break;
                        int drawX = 47 + xPadding + x;
                        int y = MapY(yValue);
                        if (prevY.HasValue)
                            g.DrawLine(penVer, prevX.Value, prevY.Value, drawX, y);
                        prevY = y;
                        prevX = drawX;
                    }

                // Live preview: show actual positions if enabled
                if (cb_livePrieview != null && cb_livePrieview.Checked)
                {
                    if (_cb_showBraking.Checked)
                    {
                        // Longitudinal force marker
                        int longX = xPadding + (int)(_lastLongForceInput / MotorSettings.SurgeGForceScale * (graphWidth - 1));
                        BeltMotorData longOutput = settings.Setup(_lastLongForceInput, 0, 0, (int)percentageUpDownRestingPoint.Value);
                        int longY = MapY(longOutput.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings));
                        g.FillEllipse(System.Drawing.Brushes.Blue, longX - 5 + 47, longY - 5, 10, 10);
                    }

                    // Lateral force marker
                    if (_cb_showCorn.Checked)
                    {
                        int latX = xPadding + (int)(_lastLatForceInput / MotorSettings.SurgeGForceScale * (graphWidth - 1));
                        BeltMotorData latOutput = settings.Setup(0, _lastLatForceInput, 0, (int)percentageUpDownRestingPoint.Value);
                        int latY = MapY(latOutput.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings));
                        g.FillEllipse(System.Drawing.Brushes.Green, latX - 5 + 47, latY - 5, 10, 10);
                    }

                    // Vertical force marker
                    if (_cb_showVer.Checked)
                    {
                        int verX = xPadding + (int)(_lastVertForceInput / MotorSettings.SurgeGForceScale * (graphWidth - 1));
                        BeltMotorData verOutput = settings.Setup(0, 0, _lastVertForceInput, (int)percentageUpDownRestingPoint.Value);
                        int verY = MapY(verOutput.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings));
                        g.FillEllipse(System.Drawing.Brushes.Orange, verX - 5 + 47, verY - 5, 10, 10);
                    }
                    // Combined bar (long + lat + vertical)
                    float combinedLong = _lastLongForceInput;
                    float combinedLat = _lastLatForceInput;
                    float combinedVert = _lastVertForceInput;
                    BeltMotorData combinedOutput = settings.Setup(combinedLong, combinedLat, combinedVert, (int)percentageUpDownRestingPoint.Value);
                    float combinedValue = combinedOutput.CalculateDataToSerail(settings, CarSettingsDatabase.Instance.CurrentSettings);
                    combinedValue = Math.Abs(combinedValue); //for showing max force dont invert it
                    // Bar max is based on settings.MaxPower (0-100 percent of motor range)
                    float barMaxValue = min + (motorRange * (settings.MaxPower / 100f));
                    float barPercent = (combinedValue - min) / (barMaxValue - min);
                    barPercent = Math.Clamp(barPercent, 0f, 1f);
                    int barMaxHeight = (int)((barMaxValue - min) / motorRange * (graphHeight - 1));
                    int barHeight = (int)(barPercent * barMaxHeight);
                    int barWidth = 15;
                    int barX = xPadding + graphWidth - barWidth - 2 + 12;
                    int barY = graphHeight - barHeight;
                    // Color logic

                    Brush barBrush = BrushUtils.LerpBrush(Brushes.Green, Brushes.Red, barPercent);

                    g.FillRectangle(barBrush, barX, barY, barWidth, barHeight);
                    // Draw the bar's max outline
                    int barMaxY = graphHeight - barMaxHeight;
                    g.DrawRectangle(System.Drawing.Pens.Black, barX, barMaxY, barWidth, barMaxHeight);


                }
            }
            pictureBoxCurveGraph.Image = bmp;
        }

        private void numericUpDownMaxPower_ValueChanged(object sender, EventArgs e)
        {
            _maxPower = (int)numericUpDownMaxPower.Value;
            BeltSettingsChanged();
        }

        private void numericUpDownCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            _curveAmount = (double)numericUpDownCurveAmount.Value;
            BeltSettingsChanged();
        }


        private bool _testABS = false;
        private void OnABSValueUpdated()
        {
            if (cb_ABS_Enabled.Checked)
                communicator.SendABS((int)nud_ABS.Value);
        }

        private float _displaySurgeForce = 0, _displaySwayForce = 0, _displayHeaveForce = 0;

        private BeltMotorData _lastMotorOutputValues;


        private float simSurge = 0, simSway = 0, simHeave = 0;

        private void UpdateTelemetoryData(float surge, float sway, float heave)
        {
            simSurge = surge;
            simSway = sway;
            simHeave = heave;
        }


        private MotorSettings _motorSettings;

        private void BeltSettingsChanged(bool save = true)
        {
            _motorSettings = new MotorSettings
            {
                MaxPower = _maxPower,
                SurgeStrength = _gForceMult,
                CurveAmount = (float)_curveAmount,
                ConeringCurveAmount = (float)_coneringCurveAmount,
                SwayStrength = (float)nud_coneringStrengh.Value,
                HeaveStrength = (float)nudVertical.Value,
                LeftMinimumAngle = L_MIN,
                LeftMaximumAngle = L_MAX,
                LeftInverted = L_INVERT,
                RightMinimumAngle = R_MIN,
                RightMaximumAngle = R_MAX,
                RightInverted = R_INVERT
            };
            DrawCurveGraph();

            if (save)
                SaveSoon();
        }

        private void UpdateBeltTensionerForces(float surge, float sway, float heave)
        {
            simSurge = surge;
            simSway = sway;
            simHeave = heave;
        
        }

        private void UpdateBeltTentionFeedback()
        {
            if (checkBoxTest.Checked)
            {
                simSurge = (float)numericUpDownTarget.Value;
                simHeave = 0;
                simSway = 0;
            }
            else
            {
                simHeave -= 1f; //remove gravity
                if (simHeave < -2) //clamp it to -2G to avoid extreme values from jumps etc throwing off the belt tensioner
                    simHeave = -2;
            }

            BeltMotorData value = _motorSettings.Setup(simSurge, simSway, simHeave, (int)percentageUpDownRestingPoint.Value);

            float yValue = value.SendDataToSerial(_motorSettings, communicator, CarSettingsDatabase.Instance.CurrentSettings);


            float tmp = _lastMotorOutputValues.SurgeOutput;
            _lastMotorOutputValues = value;


            _displaySurgeForce = value.SurgeOutput;
            _displaySwayForce = value.SwayOutput;
            _displayHeaveForce = value.HeaveOutput;

            if (cb_livePrieview != null && cb_livePrieview.Checked)
            {

                // Store latest force inputs for live preview
                _lastLongForceInput = _lastLongForceInput * .9f + simSurge * .1f;
                _lastLatForceInput = _lastLatForceInput * .9f + simSway * .1f;
                _lastVertForceInput = _lastVertForceInput * .9f + simHeave * .1f;
                DrawCurveGraph();
            }
        }

        private void OnScaledValueUpdated(float simSurge, float simSway, float simHeave)
        {


            if (!_motorSettingsLoaded)
                return; //if we have not loaded in the correct motor settings return false

            if (checkBoxTest.Checked)
            {
                simSurge = (float)numericUpDownTarget.Value;
                simHeave = 0;
                simSway = 0;
            }
            else
            {
                simHeave -= 1f; //remove gravity
                if (simHeave < -2) //clamp it to -2G to avoid extreme values from jumps etc throwing off the belt tensioner
                    simHeave = -2;
            }
                      

            BeltMotorData value = _motorSettings.Setup(simSurge, simSway, simHeave, (int)percentageUpDownRestingPoint.Value);

            float yValue = value.SendDataToSerial(_motorSettings, communicator, CarSettingsDatabase.Instance.CurrentSettings);

     
            float tmp = _lastMotorOutputValues.SurgeOutput;
            _lastMotorOutputValues = value;


            _displaySurgeForce = value.SurgeOutput;
            _displaySwayForce = value.SwayOutput;
            _displayHeaveForce = value.HeaveOutput;

            if (cb_livePrieview != null && cb_livePrieview.Checked)
            {

                // Store latest force inputs for live preview
                _lastLongForceInput = _lastLongForceInput * .9f + simSurge * .1f;
                _lastLatForceInput = _lastLatForceInput * .9f + simSway * .1f;
                _lastVertForceInput = _lastVertForceInput * .9f + simHeave * .1f;
                DrawCurveGraph();
            }


        }

        public string LabelStatus
        {
            get { return _of_seatbeltDevice.Text; }
            set
            {
                Invoke(new Action(() =>
                {
                    _of_seatbeltDevice.Text = value;
                    _of_seatbeltDevice.IsOn = false;
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
                _of_seatbeltDevice.Text = $"Connected!";
                _of_seatbeltDevice.IsOn = true;
                SetControlsEnabled(true);
                buttonConnect.Enabled = false;
                buttonConnect.Text = "Connect";
                
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

                Invoke(new Action(() => _of_seatbeltDevice.Text = "Scanning..."));

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
                    _of_seatbeltDevice.Text = "No device responded";
                    _of_seatbeltDevice.IsOn = false;
                    SetControlsEnabled(false);
                    buttonConnect.Text = "Connect";
                    buttonConnect.Enabled = true;
                }));
                Log("Manual connect: No device responded");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
                _of_seatbeltDevice.IsOn = false;
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
            BeltSettingsChanged();
        }

        private void numericUpDownGForceToBelt_ValueChanged(object sender, EventArgs e)
        {
            SetGForceMult((float)numericUpDownGForceToBelt.Value);
            BeltSettingsChanged();
        }

        private void numericUpDownGForceToBelt_ValueChanged_1(object sender, EventArgs e)
        {
            _gForceMult = (float)numericUpDownGForceToBelt.Value;
            BeltSettingsChanged();
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

            SaveCarSettings();
            Settings.Default.Save();
            updateTimer?.Stop();
            updateTimer?.Dispose();
            mmfUpdateTimer?.Stop(); // Stop MMF update timer
            mmfUpdateTimer?.Dispose(); // Dispose MMF update timer
            _mmfWriter?.Dispose();
            base.OnFormClosing(e);
            try { if (updateNotifyIcon != null) { updateNotifyIcon.Visible = false; updateNotifyIcon.Dispose(); updateNotifyIcon = null; } } catch { }
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
                    if (ctl is not Label && ctl is not OnOffStatusControl && ctl.Tag != "de")
                        ctl.Enabled = enabled;
                }
            }
        }

        private void LoadCarSettings(string carName)
        {
            CarSettingsDatabase.Instance.LoadCarSettingsFromFile(carName);
            var settings = CarSettingsDatabase.Instance.CurrentSettings;
            // Apply settings to UI
            _gForceMult = settings.MaxGForceMult;
            _maxPower = settings.MaxPower;
            _curveAmount = settings.CurveAmount;

            try
            {
                numericUpDownGForceToBelt.Value = (decimal)settings.MaxGForceMult;
            }
            catch
            {
                numericUpDownGForceToBelt.Value = numericUpDownGForceToBelt.Minimum;
            }
            numericUpDownMaxPower.Value = settings.MaxPower;
            numericUpDownCurveAmount.Value = (decimal)settings.CurveAmount;
            nud_coneringStrengh.Value = (decimal)settings.CorneringStrength;
            nudVertical.Value = (decimal)settings.VerticalStrength; // NEW
            if (settings.AbsStrength < 3)
                settings.AbsStrength = 3;
            nud_ABS.Value = (int)settings.AbsStrength; // NEW
            cb_ABS_Enabled.Checked = settings.AbsEnabled; // NEW

            cb_invert_sway.Checked = settings.InvertSway; // NEW

            // Set new setting to UI
            _coneringCurveAmount = settings.ConeringCurveAmount;
            nud_ConeringCurveAmount.Value = (decimal)settings.ConeringCurveAmount;
            percentageUpDownRestingPoint.Value = (decimal)settings.RestingPoint;
            lb_carName.Text = carName;

            cb_invertHeave.Checked = settings.InvertHeave; // NEW
            cb_invertSurge.Checked = settings.InvertSurge; // NEW
            DrawCurveGraph();
        }


        System.Timers.Timer _timer;



        public void SaveSoon()
        {
            if (_isLoading)
                return;

            if (_timer == null)
            {
                _timer = new System.Timers.Timer(3000);
                _timer.Elapsed += (s, e) =>
                {

                    SaveCarSettings();

                };
                _timer.Start();
                _timer.AutoReset = false;
            }
            else
            {
                _timer.Stop();
                _timer.Close();
                _timer.Dispose();
                _timer = null;
            }
        }

        private void SaveCarSettings()
        {
            if (_isLoading) return; // Don't save while we're still loading settings    
          

            var settings = CarSettingsDatabase.Instance.CurrentSettings;
            settings.MaxGForceMult = (float)numericUpDownGForceToBelt.Value;
            settings.MaxPower = (int)numericUpDownMaxPower.Value;
            settings.CurveAmount = (double)numericUpDownCurveAmount.Value;
            settings.CorneringStrength = (float)nud_coneringStrengh.Value;
            settings.VerticalStrength = (float)nudVertical.Value; // NEW
            settings.AbsStrength = (float)nud_ABS.Value; // NEW
            settings.AbsEnabled = cb_ABS_Enabled.Checked; // NEW
            settings.InvertSway = cb_invert_sway.Checked; // NEW
            settings.ConeringCurveAmount = (double)nud_ConeringCurveAmount.Value; // NEW
            settings.RestingPoint = (int)percentageUpDownRestingPoint.Value; // NEW

           
            try
            {
                CarSettingsDatabase.Instance.SaveCurrentCarSettings(CarName);

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
                LongStrengh = (float)numericUpDownGForceToBelt.Value,
                MaxPower = _maxPower,
                CurveAmount = _curveAmount,
                CorneringStrength = (float)nud_coneringStrengh.Value,
                VerticalStrength = (float)nudVertical.Value,
                AbsStrength = (float)nud_ABS.Value,
                AbsEnabled = (byte)(cb_ABS_Enabled.Checked ? 1 : 0),
                InvertCornering = (byte)(cb_invert_sway.Checked ? 1 : 0),
                ConeringCurveAmount = (float)nud_ConeringCurveAmount.Value,

                GForce = _displaySurgeForce,
                LateralG = _displaySwayForce,
                VerticalG = _displayHeaveForce,

                ConnectedToSim = irCommunicator != null ? irCommunicator.IsConnected : false,
                ConnectedToBelt = communicator.IsConnected,
                MotorRange = Math.Abs(L_MAX - L_MIN),
                MotorSwayValue = _lastMotorOutputValues.SwayOutput,
                MotorSurgeValue = _lastMotorOutputValues.SurgeOutput,
                MotorHeaveValue = _lastMotorOutputValues.HeaveOutput

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
            BeltSettingsChanged(false);
        }

        private async void bnt_Apply_Click(object sender, EventArgs e)
        {
            communicator.SendUpdatedSettings(L_MIN, L_MAX, R_MIN, R_MAX, L_INVERT, R_INVERT, DuelMotors);
            lblSettingsSaved.Visible = true;
            lblChangesNotSaved.Visible = false;
            await Task.Delay(1500);
            lblSettingsSaved.Visible = false;
            _motorSettingsLoaded = true;
        }

        private void lb_SelectedMotor_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateWindows();
        }



        private void nud_coneringStrengh_ValueChanged(object sender, EventArgs e)
        {

            BeltSettingsChanged();
        }


        private void nudVertical_ValueChanged(object sender, EventArgs e)
        {
            if (!CarSettingsDatabase.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.VerticalStrength = (float)nudVertical.Value;
            BeltSettingsChanged();
        }

        private void nud_ABS_ValueChanged(object sender, EventArgs e)
        {
            if (!CarSettingsDatabase.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.AbsStrength = (float)nud_ABS.Value;
            BeltSettingsChanged();
        }

        private void cb_ABS_Enabled_CheckedChanged(object sender, EventArgs e)
        {
            if (!CarSettingsDatabase.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.AbsEnabled = cb_ABS_Enabled.Checked;
            BeltSettingsChanged();
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

        private void cb_invert_sway_CheckedChanged(object sender, EventArgs e)
        {
            CarSettingsDatabase.Instance.CurrentSettings.InvertSway = cb_invert_sway.Checked;
        }

        private void cb_AutoConnect_CheckedChanged(object sender, EventArgs e)
        {
            applicatoinSettings.AutoConnectOnStartup = cb_AutoConnect.Checked;
            SaveAutoConnectSetting();
        }

        private void nud_ConeringCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            _coneringCurveAmount = (double)nud_ConeringCurveAmount.Value;
            BeltSettingsChanged();
        }

        private void ShowAboutBox()
        {
            using (var about = new AboutBox())
            {
                about.ShowDialog(this);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (!WindowsMessageBridge.DecodeWndProc(ref m))
            {
                base.WndProc(ref m);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void gb_Car_Settings_Enter(object sender, EventArgs e)
        {

        }

        private void labelCurveAmount_Click(object sender, EventArgs e)
        {

        }

        // Custom paint to draw blue border around braking GroupBox
        private void _gb_Braking_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not GroupBox gb) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Measure text
            var text = gb.Text ?? string.Empty;
            var textSize = g.MeasureString(text, gb.Font);

            // Coordinates
            int left = 0;
            int top = (int)(textSize.Height / 2);
            int right = gb.ClientRectangle.Width - 1;
            int bottom = gb.ClientRectangle.Height - 1;
            int textX = 8;
            int textLeft = textX - 4;
            int textRight = textX + (int)textSize.Width + 4;

            // Fill the background behind the text to hide default border under text
            using (var b = new System.Drawing.SolidBrush(gb.BackColor))
            {
                g.FillRectangle(b, textX - 2, 0, textSize.Width + 4, (int)textSize.Height);
            }

            // Draw the blue border as lines but skip the segment under the caption
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Blue, 2))
            {
                // Top left segment
                g.DrawLine(pen, left, top, Math.Max(left, textLeft), top);
                // Top right segment
                g.DrawLine(pen, Math.Min(right, textRight), top, right, top);
                // Left vertical
                g.DrawLine(pen, left, top, left, bottom);
                // Right vertical
                g.DrawLine(pen, right, top, right, bottom);
                // Bottom horizontal
                g.DrawLine(pen, left, bottom, right, bottom);
            }

            // Draw the caption text on top
            using (var fore = new System.Drawing.SolidBrush(gb.ForeColor))
            {
                g.DrawString(text, gb.Font, fore, textX, 0);
            }
        }

        // Custom paint to draw green border around cornering GroupBox
        private void _gb_cornering_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not GroupBox gb) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Measure text
            var text = gb.Text ?? string.Empty;
            var textSize = g.MeasureString(text, gb.Font);

            // Coordinates
            int left = 0;
            int top = (int)(textSize.Height / 2);
            int right = gb.ClientRectangle.Width - 1;
            int bottom = gb.ClientRectangle.Height - 1;
            int textX = 8;
            int textLeft = textX - 4;
            int textRight = textX + (int)textSize.Width + 4;

            // Fill the background behind the text to hide default border under text
            using (var b = new System.Drawing.SolidBrush(gb.BackColor))
            {
                g.FillRectangle(b, textX - 2, 0, textSize.Width + 4, (int)textSize.Height);
            }

            // Draw the green border as lines but skip the segment under the caption
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Green, 2))
            {
                // Top left segment
                g.DrawLine(pen, left, top, Math.Max(left, textLeft), top);
                // Top right segment
                g.DrawLine(pen, Math.Min(right, textRight), top, right, top);
                // Left vertical
                g.DrawLine(pen, left, top, left, bottom);
                // Right vertical
                g.DrawLine(pen, right, top, right, bottom);
                // Bottom horizontal
                g.DrawLine(pen, left, bottom, right, bottom);
            }

            // Draw the caption text on top
            using (var fore = new System.Drawing.SolidBrush(gb.ForeColor))
            {
                g.DrawString(text, gb.Font, fore, textX, 0);
            }
        }

        // Custom paint to draw green border around cornering GroupBox
        private void _gb_vertical_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not GroupBox gb) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Measure text
            var text = gb.Text ?? string.Empty;
            var textSize = g.MeasureString(text, gb.Font);

            // Coordinates
            int left = 0;
            int top = (int)(textSize.Height / 2);
            int right = gb.ClientRectangle.Width - 1;
            int bottom = gb.ClientRectangle.Height - 1;
            int textX = 8;
            int textLeft = textX - 4;
            int textRight = textX + (int)textSize.Width + 4;

            // Fill the background behind the text to hide default border under text
            using (var b = new System.Drawing.SolidBrush(gb.BackColor))
            {
                g.FillRectangle(b, textX - 2, 0, textSize.Width + 4, (int)textSize.Height);
            }

            // Draw the green border as lines but skip the segment under the caption
            using (var pen = new System.Drawing.Pen(System.Drawing.Color.Orange, 2))
            {
                // Top left segment
                g.DrawLine(pen, left, top, Math.Max(left, textLeft), top);
                // Top right segment
                g.DrawLine(pen, Math.Min(right, textRight), top, right, top);
                // Left vertical
                g.DrawLine(pen, left, top, left, bottom);
                // Right vertical
                g.DrawLine(pen, right, top, right, bottom);
                // Bottom horizontal
                g.DrawLine(pen, left, bottom, right, bottom);
            }

            // Draw the caption text on top
            using (var fore = new System.Drawing.SolidBrush(gb.ForeColor))
            {
                g.DrawString(text, gb.Font, fore, textX, 0);
            }
        }
        private void percentageUpDownRestingPoint_ValueChanged(object sender, EventArgs e)
        {
            BeltSettingsChanged();
        }



        private void _ttb_maxOutput_Scroll(object sender, EventArgs e)
        {

        }

        private void _cb_showBraking_CheckedChanged(object sender, EventArgs e)
        {
            DrawCurveGraph();
        }

        private void _cb_showCorn_CheckedChanged(object sender, EventArgs e)
        {
            DrawCurveGraph();
        }

        private void _cb_showVer_CheckedChanged(object sender, EventArgs e)
        {
            DrawCurveGraph();
        }

        private NotifyIcon? updateNotifyIcon;

        private void ShowUpdateAvailableNotification(Updater.UpdateInfo info)
        {
            try
            {
                if (updateNotifyIcon == null)
                {
                    updateNotifyIcon = new NotifyIcon();
                    updateNotifyIcon.Icon = this.Icon ?? System.Drawing.SystemIcons.Application;
                    updateNotifyIcon.Visible = true;
                    updateNotifyIcon.Text = "Update available";
                    updateNotifyIcon.BalloonTipTitle = "Update available";
                }

                updateNotifyIcon.BalloonTipText = info.RemoteTag != null ? $"Version {info.RemoteTag} is available. Click to download." : "An update is available. Click to download.";
                updateNotifyIcon.Tag = info; // store info for click handler
                updateNotifyIcon.Click -= UpdateNotifyIcon_Click;
                updateNotifyIcon.Click += UpdateNotifyIcon_Click;
                updateNotifyIcon.ShowBalloonTip(5000);

                // also set a small visible indicator on the taskbar by changing the form's title suffix
                this.Text = this.Text + " - Update available";
            }
            catch { }
        }

        private async void UpdateNotifyIcon_Click(object? sender, EventArgs e)
        {
            try
            {
                if (sender is NotifyIcon ni && ni.Tag is Updater.UpdateInfo)
                {
                    // Hide the balloon and start the interactive updater
                    ni.Visible = false;
                }

                // Launch the full updater flow (this will re-query and show changelog/download UI)
                await Updater.CheckForUpdatesAsync(this).ConfigureAwait(false);
            }
            catch { }
        }

        private void cb_invertHeave_CheckedChanged(object sender, EventArgs e)
        {
            CarSettingsDatabase.Instance.CurrentSettings.InvertHeave = cb_invertHeave.Checked;
            CarSettingsDatabase.Instance.SaveCurrentCarSettings(CarName);
        }

        private void cb_invertSurge_CheckedChanged(object sender, EventArgs e)
        {
            CarSettingsDatabase.Instance.CurrentSettings.InvertSurge = cb_invertSurge.Checked;
            CarSettingsDatabase.Instance.SaveCurrentCarSettings(CarName);
        }
    }
}
