using BeltAPI;
using BeltTentionerLib;
using belttentiontest.Controls;
using belttentiontest.Properties;
using SharedResources;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Ports;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Core;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
namespace belttentiontest
{
    [SupportedOSPlatform("windows")]
    public partial class Form1 : Form
    {
        public bool ClosingForm { get; private set; } = false;

        public const float MAXPOSIBLEMOTORVALUE = 180;

        public BeltSerialDevice BeltTentionerDevice;
        private bool handshakeComplete = false;
        private CancellationTokenSource? autoConnectCts;

        // new: iRacing communicator
        private IracingCommunicator? irCommunicator;
        private bool? pendingIracingState = null;


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
            public List<string> CollapsedGroups { get; set; } = new();
        }

        public static AppSettings ApplicatoinSettings { get; set; } = new AppSettings();

        private const string AutoConnectSettingsFile = "autoconnect.json";

        private static void LoadAutoConnectSetting()
        {
            try
            {
                if (!File.Exists(AutoConnectSettingsFile))
                {
                    ApplicatoinSettings = new AppSettings();
                    return;
                }

                var json = File.ReadAllText(AutoConnectSettingsFile);
                if (string.IsNullOrWhiteSpace(json))
                {
                    ApplicatoinSettings = new AppSettings();
                    return;
                }

                // support legacy single-bool file (true/false)
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith('{'))
                {
                    ApplicatoinSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    // legacy format
                    if (bool.TryParse(json.Trim(), out var legacy))
                    {
                        ApplicatoinSettings = new AppSettings { AutoConnectOnStartup = legacy };
                    }
                    else
                    {
                        ApplicatoinSettings = new AppSettings();
                    }
                }
            }
            catch
            {
                ApplicatoinSettings = new AppSettings();
            }
        }

        private static void SaveAutoConnectSetting()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(ApplicatoinSettings, opts);
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
            MyLogger.Log("Application started");
            _isLoading = true;
            LoadAutoConnectSetting();
            _instance = this;
            InitializeComponent();


            // Silent update check on startup (fire-and-forget)
            MyLogger.Log("Checking for updates...");
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

            MyLogger.Log("Binding Controls");
            this.Text = $"Belt Tensioner V{AboutBox.Version}";

            cb_AutoConnect.Checked = ApplicatoinSettings.AutoConnectOnStartup;

            // custom paint for braking groupbox border
            _gb_Braking.Paint += _gb_Braking_Paint;
            // custom paint for cornering groupbox border (green)
            _gb_cornering.Paint += _gb_cornering_Paint;

            _gb_vertical.Paint += _gb_vertical_Paint;

            _mmfWriter = new MemoryMapFileWriter();

            // MMF update timer: call WriteSettingsToMemoryMappedFile 30 times/sec
            mmfUpdateTimer = new System.Windows.Forms.Timer();
            mmfUpdateTimer.Interval = 32; // ~30 times per second
            mmfUpdateTimer.Tick += (s, e) => WriteSettingsToMemoryMappedFile(""); // Pass actual JSON if needed
            mmfUpdateTimer.Start();

            simhub_Tel_Timer = new System.Windows.Forms.Timer();
            simhub_Tel_Timer.Interval = 16; // 16 times a second (~60Hz)
            simhub_Tel_Timer.Tick += (s, e) => GetSimHubData();
            simhub_Tel_Timer.Start();

            belttentionerUpdate = new System.Windows.Forms.Timer();
            belttentionerUpdate.Interval = 16; // ~30 times per second
            belttentionerUpdate.Tick += (s, e) => UpdateBeltTentionFeedback();
            belttentionerUpdate.Start();




   //         buttonConnect.Enabled = false;

  //          _of_seatbeltDevice.Text = "Scanning...";
            SetControlsEnabled(false);
            //buttonConnect.Enabled = true;

            BeltTentionerDevice = new BeltSerialDevice();
            BeltTentionerDevice.MessageReceived += OnMessageReceivedFromSerial;
            BeltTentionerDevice.HandshakeComplete += OnHandshakeCompleteFromSerial;
            BeltTentionerDevice.OnMotorSettingsRecived += OnMotorSettingsRecived;

            
            WindowsMessageBridge.IsEnabled = false;
            MyLogger.Log($"Initializing Windows Message Bridge {(WindowsMessageBridge.IsEnabled? "Yes" : "No")}");
            WindowsMessageBridge.BeltMessageReceived += (msg) =>
            {
                switch (msg.Type)
                {
                    case BeltMessageType.GForce:
                        _ttb_brakingStr.Value = msg.Value;
                        break;

                    case BeltMessageType.GCurve:
                        _ttb_brakingCurve.Value = msg.Value;
                        break;

                    case BeltMessageType.VForce:
                        _ttb_verStr.Value = msg.Value;
                        break;

                    case BeltMessageType.CForce:
                        _ttb_corneringStr.Value = msg.Value;
                        break;

                    case BeltMessageType.CCurve:
                        _ttb_corneringCurve.Value = msg.Value;
                        break;

                    case BeltMessageType.MaxOutput:
                        _ttb_maxOutput.Value = msg.Value;
                        break;

                    case BeltMessageType.InvertConeringForces:
                        cb_invert_sway.Checked = msg.Value != 0;
                        break;
                    case BeltMessageType.ABSEnabled:
                        cb_ABS_Enabled.Checked = msg.Value != 0;
                        break;
                    case BeltMessageType.ABSStrength:
                        _ttb_ABS.Value = msg.Value;
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

                    BeltTentionerDevice.Dispose();

                    // stop iracing monitoring
                    try { irCommunicator?.Dispose(); } catch { }

                    // Stop and dispose timer

                    mmfUpdateTimer?.Stop(); // Stop MMF update timer
                    mmfUpdateTimer?.Dispose(); // Dispose MMF update timer
                }
                catch { }
            };

            MyLogger.Log("Initialization complete, setting up iRacing monitoring");

           
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
               
                CarName = carName;

                
               
                LoadCarSettings(carName);
               

            };
          
             

            //   throw new InvalidOperationException("This is a test exception to verify crash logging.");
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





         //   LoadCarSettings(CarName);

            // Auto-connect on startup if enabled
            if (ApplicatoinSettings.AutoConnectOnStartup)
            {


                _of_seatbeltDevice.Text = $"Connecting...";

                // Fire and forget, UI will update via events
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource();



                    bool ok = await BeltTentionerDevice.ConnectAsync(cts.Token).ConfigureAwait(false);
                    if (ok)
                    {
                        handshakeComplete = true;
                        UpdateConnectionStatusConnected();
                    }
                });
            }

            // Add Help menu with About...
            var darkTable = new DarkMenuColorTable();
            var menuStrip = new MenuStrip();
            menuStrip.BackColor = Color.FromArgb(18, 18, 30);
            menuStrip.ForeColor = Color.FromArgb(160, 160, 190);
            menuStrip.Renderer = new ToolStripProfessionalRenderer(darkTable);
            var menuSystem = new ToolStripMenuItem("Settings");
            menuSystem.ForeColor = Color.FromArgb(160, 160, 190);
            IracingCommunicator.Instance.Enabled = ApplicatoinSettings.UseIracing;
            var menuUseIracing = new ToolStripMenuItem("Use IRacing Telemetry");
            menuUseIracing.CheckOnClick = true;
            menuUseIracing.Checked = ApplicatoinSettings.UseIracing;
            var menuUseSimHub = new ToolStripMenuItem("Use SimHub Telemetry");
            menuUseIracing.Click += (s, e) =>
            {
                ApplicatoinSettings.UseIracing = menuUseIracing.Checked;
                _of_Control.Enabled = ApplicatoinSettings.UseIracing;
                IracingCommunicator.Instance.Enabled = ApplicatoinSettings.UseIracing;
                if (ApplicatoinSettings.UseIracing)
                {
                    ApplicatoinSettings.UseSimHub = false;
                    menuUseSimHub.Checked = ApplicatoinSettings.UseSimHub;
                    _of_simHub.Enabled = ApplicatoinSettings.UseSimHub;
                    _gb_simhub.Enabled = false;
                }

                SaveAutoConnectSetting();
            };


            menuUseSimHub.CheckOnClick = true;
            menuUseSimHub.Checked = ApplicatoinSettings.UseSimHub;
            menuUseSimHub.Click += (s, e) =>
            {
                ApplicatoinSettings.UseSimHub = menuUseSimHub.Checked;
                _of_simHub.Enabled = ApplicatoinSettings.UseSimHub;
                _gb_simhub.Enabled = true;
                if (ApplicatoinSettings.UseSimHub)
                {
                    ApplicatoinSettings.UseIracing = false;
                    menuUseIracing.Checked = ApplicatoinSettings.UseIracing;
                    _of_Control.Enabled = ApplicatoinSettings.UseIracing;
                    IracingCommunicator.Instance.Enabled = ApplicatoinSettings.UseIracing;
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
            var debugLogMenuItem = new ToolStripMenuItem("Show Debug Log...");
            debugLogMenuItem.Click += (s, e) => ShowDebugLog();
            var testingMenuItem = new ToolStripMenuItem("Open Testing Form...");
            testingMenuItem.Click += (s, e) => ShowTestingForm();
            var dropdownColor = Color.FromArgb(160, 160, 190);
            menuUseIracing.ForeColor = dropdownColor;
            menuUseSimHub.ForeColor = dropdownColor;
            debugLogMenuItem.ForeColor = dropdownColor;
            testingMenuItem.ForeColor = dropdownColor;
            updateMenuItem.ForeColor = dropdownColor;
            aboutMenuItem.ForeColor = dropdownColor;
            menuSystem.DropDownItems.Add(menuUseIracing);
            menuSystem.DropDownItems.Add(menuUseSimHub);
            menuSystem.DropDownItems.Add(new ToolStripSeparator());
            menuSystem.DropDownItems.Add(debugLogMenuItem);
            menuSystem.DropDownItems.Add(testingMenuItem);
            menuSystem.DropDownItems.Add(new ToolStripSeparator());
            menuSystem.DropDownItems.Add(updateMenuItem);
            menuSystem.DropDownItems.Add(aboutMenuItem);
            menuStrip.Items.Add(menuSystem);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);


            if (!ApplicatoinSettings.UseSimHub)
                _of_simHub.Enabled = false;

            if (!ApplicatoinSettings.UseIracing)
                _of_Control.Enabled = false;
            _isLoading = false;


            cb_AutoConnect.Enabled = true;
            if (!ApplicatoinSettings.AutoConnectOnStartup)
                buttonConnect.Enabled = true;
            else
                buttonConnect.Enabled = false;

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
            if (!ApplicatoinSettings.UseSimHub)
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
                UpdateCarDriveState(false);
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
                    {
                        _lb_menu.Text = "In Menu";
                        UpdateCarDriveState(false);
                    }
                    else
                    {
                        _lb_menu.Text = "In Game";
                        UpdateCarDriveState(true);
                    }
                }));
            }
            if (_simhub_Telemetry.GameRunning)
            {
                _simHubConnected = true;

                if (!ApplicatoinSettings.UseIracing)
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
                                Debugger.Log(0, "hi", "THIS SHOUILD NOT RUN");
                                CarName = _simhub_Telemetry.CarName;
                                LoadCarSettings($"{_simhub_Telemetry.GameName}-{CarName}");
                                UpdateCarDriveState(true);
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

                        Rotation carRotation = new Rotation(_simhub_Telemetry.RotationPitch, _simhub_Telemetry.RotationPitch, _simhub_Telemetry.RotationYaw);
                        UpdateCarDriveState(true);
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





                        //OnScaledValueUpdated(brake, rcorn, ver, false);
                        UpdateBeltTensionerForces(brake, corn, ver, carRotation);
                    }
                    else
                    {
                        //   OnScaledValueUpdated(0, 0, 0, false);
                        UpdateBeltTensionerForces(0, 0, 0, Rotation.Zero);
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

        private void OnIracingConnected()
        {
            if (!ApplicatoinSettings.UseIracing)
                return;

            maxGForceRecorded = 0f; //reset max G-Force on new connection
          

            UpdateCarDriveState(true);

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

        private bool _wasInCar;
        private void UpdateCarDriveState(bool isInCar)
        {
            if (isInCar && !_wasInCar)
            {
                BeltTentionerDevice.SendSlowMode();
            }
            else
                   if (_wasInCar && !isInCar)
            {
                BeltTentionerDevice.SendSlowMode();
            }

            _wasInCar = isInCar;
        }


        private void OnIracingDisconnected()
        {
            if (!ApplicatoinSettings.UseIracing)
                return;

            SaveSoon();
            CarName = "NA";
            Debugger.Log(0, "hi", $"============IRACING DISCONCTED================");
            LoadCarSettings(CarName);
          


            UpdateCarDriveState(false);

            if (!IsHandleCreated)
            {
                pendingIracingState = false;
                return;
            }

            BeginInvoke(new Action(() =>
            {
                lb_carName.Text = CarName;
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
                    _ttb_motorStart.Value = R_MIN;
                    _ttb_motorEnd.Value   = R_MAX;
                    ck_Inverted.Checked   = R_INVERT;
                }
                else
                {
                    _ttb_motorStart.Value = L_MIN;
                    _ttb_motorEnd.Value   = L_MAX;
                    ck_Inverted.Checked   = L_INVERT;
                }
                cb_duelMotors.Checked = DuelMotors;
                lblChangesNotSaved.Visible = false;
            }));
        }


        private void OnMotorSettingsRecived()
        {
            R_MIN = (int)BeltTentionerDevice.DeviceMotorSettings.RightMinimumAngle;
            R_MAX = (int)BeltTentionerDevice.DeviceMotorSettings.RightMaximumAngle;

            L_MIN = (int)BeltTentionerDevice.DeviceMotorSettings.LeftMinimumAngle;
            L_MAX = (int)BeltTentionerDevice.DeviceMotorSettings.LeftMaximumAngle;

            L_INVERT = BeltTentionerDevice.DeviceMotorSettings.LeftInverted;
            R_INVERT = BeltTentionerDevice.DeviceMotorSettings.RightInverted;
            DuelMotors = BeltTentionerDevice.DuelMotors;


            _motorSettingsLoaded = true;
            UpdateWindows();
        }

        private bool _motorSettingsLoaded = false;
        private void OnMessageReceivedFromSerial(string message)
        {
            if (message != null && message.Length > 0)
            {
                switch (message[0])
                {


                    case 'N':
                        BeltTentionerDevice.Disconnect();
                        BeginInvoke(new Action(() =>
                        {
                            ShowDisconnectedUI("Seatbelt disconnected");
                        }));
                        return;
                }
            }



            // Try to parse the message as a tab-separated serial line
            var parsed = BeltTentionerDevice.ParseSerialLine(message);
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
                Log($"Handshake complete on {BeltTentionerDevice.PortName}");
                SetControlsEnabled(true);
                buttonConnect.Enabled = false;
                // start periodic sending using numericUpDownTarget's value getter

            }));
        }

        private void OnIracingConnectionChanged(bool connected)
        {
            if (!ApplicatoinSettings.UseIracing)
                return;

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
                
                if (gForce > maxGForceRecorded)
                {
                    maxGForceRecorded = gForce;
                    
                }
                // Removed trackBarGForce update
            }));
        }

        private void DrawCurveGraph()
        {
            _testingForm?.DrawCurveGraph();
        }

        private void numericUpDownMaxPower_ValueChanged(object sender, EventArgs e)
        {
            _maxPower = (int)_ttb_maxOutput.Value;
            BeltSettingsChanged();
        }

        private void numericUpDownCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            _curveAmount = _ttb_brakingCurve.Value;
            BeltSettingsChanged();
        }

        private bool _testABS = false;
        private void OnABSValueUpdated()
        {
            if (cb_ABS_Enabled.Checked)
                BeltTentionerDevice.SendABS((int)_ttb_ABS.Value);
        }

        private float _displaySurgeForce = 0, _displaySwayForce = 0, _displayHeaveForce = 0;

        private BeltMotorData _lastMotorOutputValues;


        private float simSurge = 0, simSway = 0, simHeave = 0;
        public Rotation simRotation;

        private void UpdateTelemetoryData(float surge, float sway, float heave, float pitch, float roll, float yaw)
        {
            simSurge = surge;
            simSway = sway;
            simHeave = heave;
            simRotation = new Rotation(pitch, roll, yaw);
        }




        private void BeltSettingsChanged(bool save = true)
        {

            UpdateCarsSettings();
            DrawCurveGraph();

            if (save)
                SaveSoon();
        }

        private bool haveData = false;
        private bool _haveTestingData = false;
        public void UpdateBeltTensionerForces(float surge, float sway, float heave, Rotation carRotation)
        {
            simSurge = surge;
            simSway = sway;
            simHeave = heave;
            simRotation = carRotation;
            _haveTestingData = true;
            //  simHeave -= 1;

            //haveData = true;
        }

        public void StopBeltTensionerForces()
        {
            _haveTestingData = false;
             simSurge = 0;
             simSway = 0;
             simHeave = 0;
             simRotation = Rotation.Zero;   
        }

        private void UpdateBeltTentionFeedback()
        {

            if (CarSettingsDatabase.Instance.CurrentSettings == null)
                return;

            if (!_haveTestingData)
            if (!irCommunicator.Isconnected && !_simHubConnected)
            {
                simSway = 0;
                simHeave = 0;
                simSurge = 0;
            }
           

            if (!_motorSettingsLoaded)
                return; //if we have not loaded in the correct motor settings return false

            if (!BeltTentionerDevice.IsConnected)
                return;

            //   if (!haveData)
            //    return;

            if (_testABS)
            {
                BeltTentionerDevice.SendABS((int)_ttb_ABS.Value);
                return;
            }

          


            BeltMotorData value;
            if (_wasInCar || _haveTestingData)
            {

                value = BeltTentionerDevice.DeviceMotorSettings.Setup(simSurge, simSway, simHeave, CarSettingsDatabase.Instance.CurrentSettings, simRotation);
            }
            else
            {
                CarSettings settings = CarSettingsDatabase.Instance.CurrentSettings;
                int rp = settings.RestingPoint;
                settings.RestingPoint = 0;
                value = BeltTentionerDevice.DeviceMotorSettings.Setup(0, 0, 1, settings, simRotation);
                settings.RestingPoint = rp;
                
            }
            
            bool removeGravity = irCommunicator?.Isconnected ?? false; //we need to remove gravity for iracing

            float yValue = value.SendDataToSerial(BeltTentionerDevice, CarSettingsDatabase.Instance.CurrentSettings, removeGravity, simRotation);
            

            float tmp = _lastMotorOutputValues.LeftSurgeOutput;
            _lastMotorOutputValues = value;


            _displaySurgeForce = value.LeftSurgeOutput;
            _displaySwayForce = value.LeftSwayOutput;
            _displayHeaveForce = value.LeftHeaveOutput;


            //   haveData = false;
            //   simHeave = 0;
            //  simSurge = 0;
            //  simSway = 0;


            _testingForm?.UpdateLivePreview(simSurge, simSway, simHeave);
            var (leftOut, rightOut) = value.GetLastMotorDataSent();
            _testingForm?.UpdateMotorOutput(leftOut, rightOut, simRotation);
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


            }));
            Log($"Manual connect: Connected to {BeltTentionerDevice.PortName}");
        }

        private async void buttonConnect_Click(object sender, EventArgs e)
        {
            try
            {
                autoConnectCts?.Cancel();


                buttonConnect.Enabled = false;

                Invoke(new Action(() => _of_seatbeltDevice.Text = "Scanning..."));

                using var manualCts = new CancellationTokenSource();
                bool ok = await BeltTentionerDevice.ConnectAsync(manualCts.Token).ConfigureAwait(false);
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
            SetGForceMult(_ttb_brakingStr.Value);
            BeltSettingsChanged();
        }

        private void numericUpDownGForceToBelt_ValueChanged_1(object sender, EventArgs e)
        {
            if (_isLoading)
                return;
            //   _gForceMult = (float)numericUpDownGForceToBelt.Value;
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



            LoadCarSettings("NA");

            RestoreCollapsedGroupState();

            DrawCurveGraph();

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {

            SaveCarSettings();
            SaveCollapsedGroupState();
            Settings.Default.Save();

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
          
            

            foreach (Control ctl in _scrollPanel.Controls)
            {
                if (ctl != buttonConnect && ctl != cb_AutoConnect)
                {
                    if (ctl is not Label && ctl is not OnOffStatusControl && ctl.Tag != "de")
                        ctl.Enabled = enabled;
                   
                }
                else
                {

                }
            }

           
        }

        private void LoadCarSettings(string carName)
        {


            _isLoading = true;
            // carName = null;

    
            CarSettingsDatabase.Instance.LoadCarSettingsFromFile(carName);

            var settings = CarSettingsDatabase.Instance.CurrentSettings;

   
         
                _gForceMult = settings.SurgeStrenght;
                _maxPower = settings.MaxPower;
                _curveAmount = settings.SurgeCurveAmount;
                
                
        
            BeginInvoke(new Action(() =>
            {

          
                lb_carName.Text = carName;

                _ttb_brakingStr.Value    = settings.SurgeStrenght;
                _ttb_maxOutput.Value     = settings.MaxPower;
                _ttb_brakingCurve.Value  = settings.SurgeCurveAmount;
                _ttb_corneringStr.Value  = settings.SwayStrength;
                _ttb_verStr.Value        = settings.HeaveStrength;
                _ttb_ABS.Value           = Math.Max(3f, settings.AbsStrength);
                cb_ABS_Enabled.Checked   = settings.AbsEnabled;
                cb_invert_sway.Checked   = settings.InvertSway;
                _coneringCurveAmount     = settings.SwayCurveAmount;
                _ttb_corneringCurve.Value = settings.SwayCurveAmount;
                _ttb_restingPoint.Value  = settings.RestingPoint;
                cb_invertHeave.Checked   = settings.InvertHeave;
                cb_invertSurge.Checked   = settings.InvertSurge;
                _ttb_negativeSway.Value  = settings.NegativeSway;
                _ttb_pitch.Value         = settings.PitchStrength;
                _cb_tilt_invertPitch.Checked = settings.InvertPitch;
                _ttb_roll.Value          = settings.RollStrength;
                _cb_tilt_invertRoll.Checked  = settings.InvertRoll;
      
                _ttb_masterTilt.Value    = settings.MasterTiltStrength;

                _isLoading = false;
            }));

         

            DrawCurveGraph();
           
        }


        System.Timers.Timer _timer;



        public void SaveSoon()
        {
            if (_isLoading)
                return;

            if (_timer == null)
            {
                _timer = new System.Timers.Timer(2000);
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

        private void UpdateCarsSettings()
        {
            if (_isLoading)
                return;
            var settings = CarSettingsDatabase.Instance.CurrentSettings;
            settings.SurgeStrenght    = _ttb_brakingStr.Value;
            settings.MaxPower         = (int)_ttb_maxOutput.Value;
            settings.SurgeCurveAmount = _ttb_brakingCurve.Value;
            settings.SwayStrength     = _ttb_corneringStr.Value;
            settings.HeaveStrength    = _ttb_verStr.Value;
            settings.AbsStrength      = _ttb_ABS.Value;
            settings.AbsEnabled       = cb_ABS_Enabled.Checked;
            settings.InvertSway       = cb_invert_sway.Checked;
            settings.SwayCurveAmount  = _ttb_corneringCurve.Value;
            settings.RestingPoint     = (int)_ttb_restingPoint.Value;
            settings.NegativeSway     = _ttb_negativeSway.Value;
            settings.PitchStrength    = _ttb_pitch.Value;
            settings.InvertPitch      = _cb_tilt_invertPitch.Checked;
            settings.RollStrength     = _ttb_roll.Value;
            settings.InvertRoll       = _cb_tilt_invertRoll.Checked;
         
            settings.MasterTiltStrength = _ttb_masterTilt.Value;
        }

        private void SaveCarSettings()
        {
            if (_isLoading) return; // Don't save while we're still loading settings    





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
                CarName            = this.CarName,
                LongStrengh        = _ttb_brakingStr.Value,
                MaxPower           = _maxPower,
                CurveAmount        = _curveAmount,
                CorneringStrength  = _ttb_corneringStr.Value,
                VerticalStrength   = _ttb_verStr.Value,
                AbsStrength        = _ttb_ABS.Value,
                AbsEnabled         = (byte)(cb_ABS_Enabled.Checked ? 1 : 0),
                InvertCornering    = (byte)(cb_invert_sway.Checked ? 1 : 0),
                ConeringCurveAmount = _ttb_corneringCurve.Value,
                GForce             = _displaySurgeForce,
                LateralG           = _displaySwayForce,
                VerticalG          = _displayHeaveForce,
                ConnectedToSim     = irCommunicator != null ? irCommunicator.IsConnected : false,
                ConnectedToBelt    = BeltTentionerDevice.IsConnected,
                MotorRange         = Math.Abs(L_MAX - L_MIN),
                MotorSwayValue     = _lastMotorOutputValues.LeftSwayOutput,
                MotorSurgeValue    = _lastMotorOutputValues.LeftSurgeOutput,
                MotorHeaveValue    = _lastMotorOutputValues.LeftHeaveOutput
            };
            _mmfWriter?.WriteSettings(structSettings);
        }

        private void nud_Motor_Start_ValueChanged(object sender, EventArgs e)
        {
            if (lb_SelectedMotor.SelectedIndex == 1)
                R_MIN = (int)_ttb_motorStart.Value;
            else
                L_MIN = (int)_ttb_motorStart.Value;
            ShowChangesNotSaved();
        }

        private void nud_Motor_End_ValueChanged(object sender, EventArgs e)
        {
            if (lb_SelectedMotor.SelectedIndex == 1)
                R_MAX = (int)_ttb_motorEnd.Value;
            else
                L_MAX = (int)_ttb_motorEnd.Value;
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
            BeltTentionerDevice.SendUpdatedSettings(L_MIN, L_MAX, R_MIN, R_MAX, L_INVERT, R_INVERT, DuelMotors);



            
            lblChangesNotSaved.Visible = false;
            //   await Task.Delay(1500);
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
            if (settings == null)
                throw new Exception();
            settings.HeaveStrength = _ttb_verStr.Value;
            BeltSettingsChanged();
        }

        private void nud_ABS_ValueChanged(object sender, EventArgs e)
        {
            if (!CarSettingsDatabase.Instance.Settings.TryGetValue(CarName, out var settings))
                return;
            settings.AbsStrength = _ttb_ABS.Value;
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
            ApplicatoinSettings.AutoConnectOnStartup = cb_AutoConnect.Checked;
            SaveAutoConnectSetting();
        }

        private void nud_ConeringCurveAmount_ValueChanged(object sender, EventArgs e)
        {
            _coneringCurveAmount = _ttb_corneringCurve.Value;
            BeltSettingsChanged();
        }

        private void ShowAboutBox()
        {
            using (var about = new AboutBox())
            {
                about.ShowDialog(this);
            }
        }

        /// <summary>
        /// Collects every CollapsibleGroupBox in the given container (and nested containers)
        /// that has Collapsible=true and a non-empty Name.
        /// </summary>
        private static IEnumerable<CollapsibleGroupBox> FindCollapsibleBoxes(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is CollapsibleGroupBox cgb && cgb.Collapsible && !string.IsNullOrEmpty(cgb.Name))
                    yield return cgb;
                foreach (var child in FindCollapsibleBoxes(c))
                    yield return child;
            }
        }

        private void SaveCollapsedGroupState()
        {
            ApplicatoinSettings.CollapsedGroups = new List<string>();
            foreach (var cgb in FindCollapsibleBoxes(_scrollPanel))
            {
                if (cgb.Collapsed)
                    ApplicatoinSettings.CollapsedGroups.Add(cgb.Name);
            }
            SaveAutoConnectSetting();
        }

        private void RestoreCollapsedGroupState()
        {
            if (ApplicatoinSettings.CollapsedGroups == null || ApplicatoinSettings.CollapsedGroups.Count == 0)
                return;

            var collapsed = new HashSet<string>(ApplicatoinSettings.CollapsedGroups);
            foreach (var cgb in FindCollapsibleBoxes(_scrollPanel))
            {
                if (collapsed.Contains(cgb.Name))
                    cgb.Collapsed = true;
            }
        }

        private TestingForm? _testingForm;
        private void ShowTestingForm()
        {
            if (_testingForm == null || _testingForm.IsDisposed)
            {
                _testingForm = new TestingForm();
                _testingForm.Show(this);
            }
            else
            {
                _testingForm.BringToFront();
            }
        }

        private DebugLogForm? _debugLogForm;
        private void ShowDebugLog()
        {
            if (_debugLogForm == null || _debugLogForm.IsDisposed)
            {
                _debugLogForm = new DebugLogForm(BeltTentionerDevice);
                _debugLogForm.Show(this);
            }
            else
            {
                _debugLogForm.BringToFront();
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            DwmSetWindowAttribute(this.Handle, 20, ref dark, sizeof(int));
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
                // Also handle clicks on the balloon tip itself
                updateNotifyIcon.BalloonTipClicked -= UpdateNotifyIcon_Click;
                updateNotifyIcon.BalloonTipClicked += UpdateNotifyIcon_Click;
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
            SaveSoon();
        }

        private void cb_invertSurge_CheckedChanged(object sender, EventArgs e)
        {
            CarSettingsDatabase.Instance.CurrentSettings.InvertSurge = cb_invertSurge.Checked;
            SaveSoon();
        }

        private void _ttb_corneringStr_Click(object sender, EventArgs e)
        {

        }

        private void nud_negativeSway_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading)
                return;
            CarSettingsDatabase.Instance.CurrentSettings.NegativeSway = _ttb_negativeSway.Value;
            SaveSoon();
        }

        private void nud_pitch_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            CarSettingsDatabase.Instance.CurrentSettings.PitchStrength = _ttb_pitch.Value;
            SaveSoon();
        }

        private void nud_roll_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            CarSettingsDatabase.Instance.CurrentSettings.RollStrength = _ttb_roll.Value;
            SaveSoon();
        }

 

        private void nud_masterTilt_ValueChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            CarSettingsDatabase.Instance.CurrentSettings.MasterTiltStrength = _ttb_masterTilt.Value;
            SaveSoon();
        }

        private void _cb_tilt_invertPitch_CheckedChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            CarSettingsDatabase.Instance.CurrentSettings.InvertPitch = _cb_tilt_invertPitch.Checked;
            SaveSoon();
        }

        private void _cb_tilt_invertRoll_CheckedChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            CarSettingsDatabase.Instance.CurrentSettings.InvertRoll = _cb_tilt_invertRoll.Checked;
            SaveSoon();
        }

   

        private void _scrollPanel_MouseWheel(object? sender, MouseEventArgs e)
        {
            int delta = -(e.Delta / 120) * SystemInformation.MouseWheelScrollLines * 20;
            int newVal = Math.Clamp(_scrollPanel.VerticalScroll.Value + delta,
                                    _scrollPanel.VerticalScroll.Minimum,
                                    _scrollPanel.VerticalScroll.Maximum);
            _scrollPanel.VerticalScroll.Value = newVal;
            _scrollPanel.PerformLayout();
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            private static readonly Color _bg = Color.FromArgb(18, 18, 30);
            private static readonly Color _highlight = Color.FromArgb(45, 45, 65);
            private static readonly Color _border = Color.FromArgb(60, 60, 85);
            private static readonly Color _text = Color.FromArgb(160, 160, 190);

            public override Color MenuStripGradientBegin => _bg;
            public override Color MenuStripGradientEnd => _bg;
            public override Color MenuItemSelected => _highlight;
            public override Color MenuItemSelectedGradientBegin => _highlight;
            public override Color MenuItemSelectedGradientEnd => _highlight;
            public override Color MenuItemBorder => _border;
            public override Color MenuItemPressedGradientBegin => _highlight;
            public override Color MenuItemPressedGradientEnd => _highlight;
            public override Color MenuItemPressedGradientMiddle => _highlight;
            public override Color ToolStripDropDownBackground => _bg;
            public override Color ImageMarginGradientBegin => _bg;
            public override Color ImageMarginGradientMiddle => _bg;
            public override Color ImageMarginGradientEnd => _bg;
            public override Color SeparatorDark => _border;
            public override Color SeparatorLight => _border;
            public override Color CheckBackground => _highlight;
            public override Color CheckSelectedBackground => _highlight;
            public override Color CheckPressedBackground => _highlight;
        }
    }
}
