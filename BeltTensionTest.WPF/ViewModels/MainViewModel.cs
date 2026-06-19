using BeltAPI;
using BeltTensionTest.WPF.Helpers;
using BeltTensionTest.WPF.Models;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.Shared;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BeltTensionTest.WPF.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        // Events for testing window live updates
        public event Action<float, float, float>? LivePreviewUpdated;
        public event Action<float, float, BeltAPI.Rotation>? MotorOutputUpdated;
        // ?? Services ?????????????????????????????????????????????????????????
        private readonly SettingsService    _settingsSvc   = new();
        private readonly CarSettingsService _carSettingsSvc = CarSettingsService.Instance;
        private readonly IracingService     _iracing        = IracingService.Instance;
        private readonly SimHubService      _simHub         = new();
        private readonly MmfWriterService   _mmfWriter      = new();
        private readonly UpdateService      _updateSvc      = new();

        // ?? Timers ????????????????????????????????????????????????????????????
        private readonly DispatcherTimer _feedbackTimer;
        private readonly DispatcherTimer _mmfTimer;
        private readonly DispatcherTimer _simHubTimer;

        // ?? State ?????????????????????????????????????????????????????????????
        public AppSettings AppSettings { get; private set; }

        

        public static BeltAPI.BeltSerialDevice Device { get; } = new BeltAPI.BeltSerialDevice();

   

        private string _carName = "NA";
        private bool _motorSettingsLoaded;
        private bool _wasInCar;
        private bool _haveTestingData;
        private bool _isLoading;
        private bool _testAbs;
        private bool _simHubConnected;
        private string _lastGameName = string.Empty;

        private float _simSurge, _simSway, _simHeave;
        private BeltAPI.Rotation _simRotation;
        private BeltMotorData _lastMotorOutput;

        // Motor settings
        private int L_MIN, L_MAX = 180, R_MIN, R_MAX = 180;
        private bool L_INVERT, R_INVERT, _dualMotors;

        private System.Timers.Timer? _saveTimer;

        // Wind background loop
        private CancellationTokenSource? _windCts;
        private Task? _windTask;

        private bool _enableForCar;
        public bool EnableForCar
        {
            get => _enableForCar;
            set
            {
                if (SetField(ref _enableForCar, value))
                {
                    OnCarSettingChanged();
                    if (value) StartWindLoop(); else StopWindLoop();
                }
            }
        }

        private int _windMinSpeed;
        public int WindMinSpeed
        {
            get => _windMinSpeed;
            set { if (SetField(ref _windMinSpeed, value)) OnCarSettingChanged(); }
        }

        private int _windPowerPercentage;
        public int WindPowerPercentage
        {
            get => _windPowerPercentage;
            set { if (SetField(ref _windPowerPercentage, value)) OnCarSettingChanged(); }
        }

        // ?? Bindable Properties ????????????????????????????????????????????????
        private string _deviceStatusText = "Not connected";
        public string DeviceStatusText
        {
            get => _deviceStatusText;
            set => SetField(ref _deviceStatusText, value);
        }

        private bool _deviceIsOn;
        public bool DeviceIsOn
        {
            get => _deviceIsOn;
            set => SetField(ref _deviceIsOn, value);
        }

        private bool _iracingIsOn;
        public bool IracingIsOn
        {
            get => _iracingIsOn;
            set => SetField(ref _iracingIsOn, value);
        }

        private bool _simHubIsOn;
        public bool SimHubIsOn
        {
            get => _simHubIsOn;
            set => SetField(ref _simHubIsOn, value);
        }

        private bool _simHubGroupEnabled;
        public bool SimHubGroupEnabled
        {
            get => _simHubGroupEnabled;
            set => SetField(ref _simHubGroupEnabled, value);
        }

        private string _simHubText = "Not Connected to SimHub";
        public string SimHubText
        {
            get => _simHubText;
            set => SetField(ref _simHubText, value);
        }

        private string _menuStateText = "";
        public string MenuStateText
        {
            get => _menuStateText;
            set => SetField(ref _menuStateText, value);
        }

        private bool _supportBrake;
        public bool SupportBrake
        {
            get => _supportBrake;
            set => SetField(ref _supportBrake, value);
        }

        private bool _supportCornering;
        public bool SupportCornering
        {
            get => _supportCornering;
            set => SetField(ref _supportCornering, value);
        }

        private bool _supportVertical;
        public bool SupportVertical
        {
            get => _supportVertical;
            set => SetField(ref _supportVertical, value);
        }

        private string _carNameDisplay = "NA";
        public string CarNameDisplay
        {
            get => _carNameDisplay;
            set => SetField(ref _carNameDisplay, value);
        }

        private bool _controlsEnabled;
        public bool ControlsEnabled
        {
            get => _controlsEnabled;
            set => SetField(ref _controlsEnabled, value);
        }

        private bool _connectButtonEnabled = true;
        public bool ConnectButtonEnabled
        {
            get => _connectButtonEnabled;
            set => SetField(ref _connectButtonEnabled, value);
        }

        private bool _autoConnect;
        public bool AutoConnect
        {
            get => _autoConnect;
            set
            {
                if (SetField(ref _autoConnect, value))
                {
                    AppSettings.AutoConnectOnStartup = value;
                    _settingsSvc.Save(AppSettings);
                }
            }
        }

        private bool _useIracing;
        public bool UseIracing
        {
            get => _useIracing;
            set
            {
                if (SetField(ref _useIracing, value))
                {
                    AppSettings.UseIracing = value;
                    _iracing.Enabled = value;
                    if (value)
                    {
                        UseSimHub = false;
                        // mark SimHub indicator off when switching to iRacing
                        SimHubIsOn = false;
                        SimHubText = "Disabled";
                    }
                    _settingsSvc.Save(AppSettings);
                }
            }
        }

        private bool _useSimHub;
        public bool UseSimHub
        {
            get => _useSimHub;
            set
            {
                if (SetField(ref _useSimHub, value))
                {
                    AppSettings.UseSimHub = value;
                    SimHubGroupEnabled = value;
                    if (value)
                    {
                        UseIracing = false;
                        // mark iRacing indicator off when switching to SimHub
                        IracingIsOn = false;
                        // reset SimHub text until connected
                        SimHubText = "Not Connected to SimHub";
                    }
                    _settingsSvc.Save(AppSettings);
                }
            }
        }

        // ?? Car Settings (bound to sliders) ????????????????????????????????????
        private float _brakingStrength = 1f;
        public float BrakingStrength
        {
            get => _brakingStrength;
            set { if (SetField(ref _brakingStrength, value)) OnCarSettingChanged(); }
        }

        private float _brakingCurve = 1f;
        public float BrakingCurve
        {
            get => _brakingCurve;
            set { if (SetField(ref _brakingCurve, value)) OnCarSettingChanged(); }
        }

        private bool _invertSurge;
        public bool InvertSurge
        {
            get => _invertSurge;
            set { if (SetField(ref _invertSurge, value)) OnCarSettingChanged(); }
        }

        private float _corneringStrength = 1f;
        public float CorneringStrength
        {
            get => _corneringStrength;
            set { if (SetField(ref _corneringStrength, value)) OnCarSettingChanged(); }
        }

        private float _corneringCurve = 1f;
        public float CorneringCurve
        {
            get => _corneringCurve;
            set { if (SetField(ref _corneringCurve, value)) OnCarSettingChanged(); }
        }

        private bool _invertSway;
        public bool InvertSway
        {
            get => _invertSway;
            set { if (SetField(ref _invertSway, value)) OnCarSettingChanged(); }
        }

        private float _verticalStrength = 1f;
        public float VerticalStrength
        {
            get => _verticalStrength;
            set { if (SetField(ref _verticalStrength, value)) OnCarSettingChanged(); }
        }

        private bool _invertHeave;
        public bool InvertHeave
        {
            get => _invertHeave;
            set { if (SetField(ref _invertHeave, value)) OnCarSettingChanged(); }
        }

        private float _maxOutput = 100f;
        public float MaxOutput
        {
            get => _maxOutput;
            set { if (SetField(ref _maxOutput, value)) OnCarSettingChanged(); }
        }

        private float _restingPoint;
        public float RestingPoint
        {
            get => _restingPoint;
            set { if (SetField(ref _restingPoint, value)) OnCarSettingChanged(); }
        }

        private float _negativeSway;
        public float NegativeSway
        {
            get => _negativeSway;
            set { if (SetField(ref _negativeSway, value)) OnCarSettingChanged(); }
        }

        private float _absStrength = 3f;
        public float AbsStrength
        {
            get => _absStrength;
            set { if (SetField(ref _absStrength, value)) OnCarSettingChanged(); }
        }

        private bool _absEnabled;
        public bool AbsEnabled
        {
            get => _absEnabled;
            set { if (SetField(ref _absEnabled, value)) OnCarSettingChanged(); }
        }

        // Tilt
        private float _pitchStrength = 10f;
        public float PitchStrength
        {
            get => _pitchStrength;
            set { if (SetField(ref _pitchStrength, value)) OnCarSettingChanged(); }
        }

        private bool _invertPitch;
        public bool InvertPitch
        {
            get => _invertPitch;
            set { if (SetField(ref _invertPitch, value)) OnCarSettingChanged(); }
        }

        private float _rollStrength = 10f;
        public float RollStrength
        {
            get => _rollStrength;
            set { if (SetField(ref _rollStrength, value)) OnCarSettingChanged(); }
        }

        private bool _invertRoll;
        public bool InvertRoll
        {
            get => _invertRoll;
            set { if (SetField(ref _invertRoll, value)) OnCarSettingChanged(); }
        }

        private float _masterTiltStrength = 10f;
        public float MasterTiltStrength
        {
            get => _masterTiltStrength;
            set { if (SetField(ref _masterTiltStrength, value)) OnCarSettingChanged(); }
        }

        // Motor settings
        private int _motorStart;
        public int MotorStart
        {
            get => _motorStart;
            set { if (SetField(ref _motorStart, value)) ShowChangesNotSaved = true; }
        }

        private int _motorEnd = 180;
        public int MotorEnd
        {
            get => _motorEnd;
            set { if (SetField(ref _motorEnd, value)) ShowChangesNotSaved = true; }
        }

        private bool _motorInverted;
        public bool MotorInverted
        {
            get => _motorInverted;
            set { if (SetField(ref _motorInverted, value)) ShowChangesNotSaved = true; }
        }

        private bool _dualMotorsEnabled;
        public bool DualMotorsEnabled
        {
            get => _dualMotorsEnabled;
            set { if (SetField(ref _dualMotorsEnabled, value)) ShowChangesNotSaved = true; }
        }

        private int _selectedMotorIndex;
        public int SelectedMotorIndex
        {
            get => _selectedMotorIndex;
            set { if (SetField(ref _selectedMotorIndex, value)) RefreshMotorDisplay(); }
        }

        private bool _showChangesNotSaved;
        public bool ShowChangesNotSaved
        {
            get => _showChangesNotSaved;
            set => SetField(ref _showChangesNotSaved, value);
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetField(ref _isUpdateAvailable, value);
        }

        private string _updateTag = string.Empty;
        public string UpdateTag
        {
            get => _updateTag;
            set => SetField(ref _updateTag, value);
        }

        // Live telemetry display
        private float _displayGForce;
        public float DisplayGForce
        {
            get => _displayGForce;
            set => SetField(ref _displayGForce, value);
        }

        private string _absStatusText = "ABS Inactive";
        public string AbsStatusText
        {
            get => _absStatusText;
            set => SetField(ref _absStatusText, value);
        }

        private bool _absActive;
        public bool AbsActive
        {
            get => _absActive;
            set => SetField(ref _absActive, value);
        }

        // ?? Commands ???????????????????????????????????????????????????????????
        public ICommand ConnectCommand { get; }
        public ICommand ApplyMotorSettingsCommand { get; }
        public ICommand TestAbsCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand OpenUpdateCommand { get; }

        // ?? Constructor ????????????????????????????????????????????????????????
        public MainViewModel()
        {
            AppSettings = _settingsSvc.Load();
            _autoConnect = AppSettings.AutoConnectOnStartup;
            _useIracing  = AppSettings.UseIracing;
            _useSimHub   = AppSettings.UseSimHub;

            // Commands
            ConnectCommand          = new AsyncRelayCommand(DoConnectAsync, _ => ConnectButtonEnabled);
            ApplyMotorSettingsCommand = new RelayCommand(DoApplyMotorSettings, _ => ControlsEnabled);
            TestAbsCommand          = new RelayCommand(DoTestAbs);
            CheckUpdatesCommand     = new AsyncRelayCommand(DoCheckUpdatesAsync);
            OpenUpdateCommand       = new RelayCommand(DoOpenUpdate);

            // Device events
            Device.HandshakeComplete    += OnHandshakeComplete;
            Device.MessageReceived      += OnDeviceMessageReceived;
            Device.OnMotorSettingsRecived += OnMotorSettingsReceived;

            // iRacing events
            _iracing.Connected         += OnIracingConnected;
            _iracing.Disconnected      += OnIracingDisconnected;
            _iracing.ConnectionChanged += OnIracingConnectionChanged;
            _iracing.TelemetryUpdated  += UpdateBeltTensionerForces;
            _iracing.GForceUpdated     += g =>
            {
                var app = Application.Current;
                if (app == null) return;
                var d = app.Dispatcher;
                if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
                d.InvokeAsync(() => DisplayGForce = g);
            };
            _iracing.AbsTriggered      += OnAbsTriggered;
            _iracing.CarNameChanged    += OnCarNameChanged;

            _iracing.Enabled = AppSettings.UseIracing;

            // WPF message bridge
            WpfMessageBridge.BeltMessageReceived += OnBridgeMessage;

            // Timers
            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _feedbackTimer.Tick += (_, _) => UpdateBeltFeedback();

            _mmfTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
            _mmfTimer.Tick += (_, _) => WriteMmf();

            _simHubTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _simHubTimer.Tick += (_, _) => PollSimHub();

            _feedbackTimer.Start();
            _mmfTimer.Start();
            _simHubTimer.Start();

            // Initial car settings
            LoadCarSettings("NA");

            // Auto connect
            if (AppSettings.AutoConnectOnStartup)
            {
                DeviceStatusText   = "Connecting...";
                ConnectButtonEnabled = false;
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource();
                    bool ok = await Device.ConnectAsync(cts.Token).ConfigureAwait(false);
                    if (ok) Application.Current.Dispatcher.Invoke(() => OnConnectionSuccess());
                });
            }
            // Silent update check
            _ = Task.Run(async () =>
            {
                var info = await _updateSvc.CheckAsync().ConfigureAwait(false);
                if (info.IsUpdateAvailable)
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsUpdateAvailable = true;
                        UpdateTag = info.RemoteTag ?? string.Empty;
                    });
            });
        }

        private void OnGForceUpdated(float g)
        {
            var app = Application.Current;
            if (app == null) return;
            var d = app.Dispatcher;
            if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
            d.InvokeAsync(() => DisplayGForce = g);
        }


        // ?? Car Settings Load / Save ????????????????????????????????????????????
        public void LoadCarSettings(string carName)
        {
            if (carName != _carName)
                _carSettingsSvc.SaveCurrentCarSettings(_carName);
            _isLoading = true;
            _carSettingsSvc.LoadCarSettingsFromFile(carName);
            _carName = carName;

            var s = _carSettingsSvc.CurrentSettings;
            _brakingStrength   = s.SurgeStrenght;
            _brakingCurve      = s.SurgeCurveAmount;
            _invertSurge       = s.InvertSurge;
            _corneringStrength = s.SwayStrength;
            _corneringCurve    = s.SwayCurveAmount;
            _invertSway        = s.InvertSway;
            _verticalStrength  = s.HeaveStrength;
            _invertHeave       = s.InvertHeave;
            _maxOutput         = s.MaxPower;
            _restingPoint      = s.RestingPoint;
            _negativeSway      = s.NegativeSway;
            _absStrength       = Math.Max(3f, s.AbsStrength);
            _absEnabled        = s.AbsEnabled;
            _pitchStrength     = s.PitchStrength;
            _invertPitch       = s.InvertPitch;
            _rollStrength      = s.RollStrength;
            _invertRoll        = s.InvertRoll;
            _masterTiltStrength = s.MasterTiltStrength;
            // Wind settings
            _enableForCar = s.EnableForCar;
            _windMinSpeed = s.WindMinSpeed;
            _windPowerPercentage = s.WindPowerPercentage;

            // Notify all bound properties
            OnPropertyChanged(nameof(BrakingStrength));
            OnPropertyChanged(nameof(BrakingCurve));
            OnPropertyChanged(nameof(InvertSurge));
            OnPropertyChanged(nameof(CorneringStrength));
            OnPropertyChanged(nameof(CorneringCurve));
            OnPropertyChanged(nameof(InvertSway));
            OnPropertyChanged(nameof(VerticalStrength));
            OnPropertyChanged(nameof(InvertHeave));
            OnPropertyChanged(nameof(MaxOutput));
            OnPropertyChanged(nameof(RestingPoint));
            OnPropertyChanged(nameof(NegativeSway));
            OnPropertyChanged(nameof(AbsStrength));
            OnPropertyChanged(nameof(AbsEnabled));
            OnPropertyChanged(nameof(PitchStrength));
            OnPropertyChanged(nameof(InvertPitch));
            OnPropertyChanged(nameof(RollStrength));
            OnPropertyChanged(nameof(InvertRoll));
            OnPropertyChanged(nameof(MasterTiltStrength));
            OnPropertyChanged(nameof(EnableForCar));
            OnPropertyChanged(nameof(WindMinSpeed));
            OnPropertyChanged(nameof(WindPowerPercentage));
            CarNameDisplay = carName;

            // Start wind loop if enabled for this car
            if (_enableForCar) StartWindLoop();

            _isLoading = false;
        }

        private void OnCarSettingChanged()
        {
            if (_isLoading) return;
            var s = _carSettingsSvc.CurrentSettings;
            s.SurgeStrenght     = _brakingStrength;
            s.SurgeCurveAmount  = _brakingCurve;
            s.InvertSurge       = _invertSurge;
            s.SwayStrength      = _corneringStrength;
            s.SwayCurveAmount   = _corneringCurve;
            s.InvertSway        = _invertSway;
            s.HeaveStrength     = _verticalStrength;
            s.InvertHeave       = _invertHeave;
            s.MaxPower          = (int)_maxOutput;
            s.RestingPoint      = (int)_restingPoint;
            s.NegativeSway      = _negativeSway;
            s.AbsStrength       = _absStrength;
            s.AbsEnabled        = _absEnabled;
            s.PitchStrength     = _pitchStrength;
            s.InvertPitch       = _invertPitch;
            s.RollStrength      = _rollStrength;
            s.InvertRoll        = _invertRoll;
            s.MasterTiltStrength = _masterTiltStrength;
            // wind settings
            s.EnableForCar = _enableForCar;
            s.WindMinSpeed = _windMinSpeed;
            s.WindPowerPercentage = _windPowerPercentage;
            SaveSoon();
        }

        private void SaveSoon()
        {
            _saveTimer?.Stop();
            _saveTimer?.Close();
            _saveTimer?.Dispose();
            _saveTimer = new System.Timers.Timer(2000) { AutoReset = false };
            _saveTimer.Elapsed += (_, _) => _carSettingsSvc.SaveCurrentCarSettings(_carName);
            _saveTimer.Start();
        }

        // ?? Device events ?????????????????????????????????????????????????????
        private void OnHandshakeComplete()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DeviceStatusText = $"Connected: {Device.PortName}";
                DeviceIsOn       = true;
                ControlsEnabled  = true;
                ConnectButtonEnabled = false;
            });
        }

        private void OnDeviceMessageReceived(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (msg[0] == 'N' || msg == "DEVICE_UNPLUGGED")
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    DeviceStatusText = "Device disconnected";
                    DeviceIsOn       = false;
                    ControlsEnabled  = false;
                    ConnectButtonEnabled = true;
                });
            }
        }

        private void OnMotorSettingsReceived()
        {
            L_MIN   = (int)Device.DeviceMotorSettings.LeftMinimumAngle;
            L_MAX   = (int)Device.DeviceMotorSettings.LeftMaximumAngle;
            R_MIN   = (int)Device.DeviceMotorSettings.RightMinimumAngle;
            R_MAX   = (int)Device.DeviceMotorSettings.RightMaximumAngle;
            L_INVERT = Device.DeviceMotorSettings.LeftInverted;
            R_INVERT = Device.DeviceMotorSettings.RightInverted;
            _dualMotors = Device.DuelMotors;
            _motorSettingsLoaded = true;
            Application.Current.Dispatcher.Invoke(RefreshMotorDisplay);
        }

        private void RefreshMotorDisplay()
        {
            _isLoading = true;
            if (_selectedMotorIndex == 1)
            {
                _motorStart   = R_MIN; OnPropertyChanged(nameof(MotorStart));
                _motorEnd     = R_MAX; OnPropertyChanged(nameof(MotorEnd));
                _motorInverted = R_INVERT; OnPropertyChanged(nameof(MotorInverted));
            }
            else
            {
                _motorStart   = L_MIN; OnPropertyChanged(nameof(MotorStart));
                _motorEnd     = L_MAX; OnPropertyChanged(nameof(MotorEnd));
                _motorInverted = L_INVERT; OnPropertyChanged(nameof(MotorInverted));
            }
            _dualMotorsEnabled = _dualMotors; OnPropertyChanged(nameof(DualMotorsEnabled));
            ShowChangesNotSaved = false;
            _isLoading = false;
        }

        // ?? iRacing events ?????????????????????????????????????????????????????
        private void OnIracingConnected()
        {
            // Always reflect SDK connection state in the UI indicator
            Application.Current.Dispatcher.Invoke(() => IracingIsOn = true);

            // Only apply in-app behaviour when the user enabled iRacing telemetry
            if (!AppSettings.UseIracing) return;
            UpdateCarDriveState(true);
        }

        private void OnIracingDisconnected()
        {
            // Always update UI indicator
            Application.Current.Dispatcher.Invoke(() => IracingIsOn = false);

            // Only perform additional cleanup when the user had iRacing telemetry enabled
            if (!AppSettings.UseIracing) return;
            _carSettingsSvc.SaveCurrentCarSettings(_carName);
            _carName = "NA";
            Application.Current.Dispatcher.Invoke(() => CarNameDisplay = "NA");
            LoadCarSettings("NA");
            UpdateCarDriveState(false);
        }

        private void OnIracingConnectionChanged(bool connected)
        {
            // Always reflect SDK connection state in the UI indicator
            Application.Current.Dispatcher.Invoke(() => IracingIsOn = connected);

            // Only apply in-app behaviour when the user enabled iRacing telemetry
            if (!AppSettings.UseIracing) return;
        }

        private void OnCarNameChanged(string name)
        {
            _carName = name;
            Application.Current.Dispatcher.Invoke(() => CarNameDisplay = name);
            LoadCarSettings(name);
        }

        private void OnAbsTriggered()
        {
            if (_absEnabled)
                Device.SendABS((int)_absStrength);
        }

        // ?? Telemetry ?????????????????????????????????????????????????????????
        public void UpdateBeltTensionerForces(float surge, float sway, float heave, BeltAPI.Rotation rot)
        {
            _simSurge    = surge;
            _simSway     = sway;
            _simHeave    = heave;
            _simRotation = rot;
            _haveTestingData = true;
        }

        public void StopBeltTensionerForces()
        {
            _haveTestingData = false;
            _simSurge = _simSway = _simHeave = 0;
            _simRotation = BeltAPI.Rotation.Zero;
        }

        private void UpdateCarDriveState(bool inCar)
        {
            if (inCar && !_wasInCar) Device.SendSlowMode();
            else if (_wasInCar && !inCar) Device.SendSlowMode();
            _wasInCar = inCar;
        }

        // ?? SimHub polling ?????????????????????????????????????????????????????
        private bool _simhubPaused;
        private bool _shSupportBrake, _shSupportCornering, _shSupportVertical;

        private void PollSimHub()
        {
            if (!AppSettings.UseSimHub) return;

            var data = _simHub.Poll();

            if (!_simHub.Connected)
            {
                SimHubIsOn       = false;
                SimHubGroupEnabled = false;
                SimHubText       = "Not Connected to SimHub";
                return;
            }

            SimHubIsOn = true;

            if (data.Paused != _simhubPaused)
            {
                _simhubPaused = data.Paused;
                MenuStateText = _simhubPaused ? "In Menu" : "In Game";
                UpdateCarDriveState(!_simhubPaused);
            }

            if (data.GameRunning)
            {
                SimHubGroupEnabled = true;
                if (data.GameName != _lastGameName)
                {
                    _lastGameName = data.GameName;
                    SimHubText = string.IsNullOrWhiteSpace(data.GameName)
                        ? "No Game Detected"
                        : $"Game: {data.GameName}";
                }

                if (_shSupportBrake != data.SupportBraking)    { _shSupportBrake    = data.SupportBraking;    SupportBrake     = _shSupportBrake; }
                if (_shSupportCornering != data.SupportCornering) { _shSupportCornering = data.SupportCornering; SupportCornering = _shSupportCornering; }
                if (_shSupportVertical != data.SupportVertical) { _shSupportVertical = data.SupportVertical;   SupportVertical  = _shSupportVertical; }

                if (!AppSettings.UseIracing && !data.Paused)
                {
                    UpdateBeltTensionerForces(
                        data.Braking / 9.81f,
                        data.Cornering / 9.81f,
                        data.Vertical / 9.81f,
                        new BeltAPI.Rotation(data.RotationPitch, data.RotationRoll, data.RotationYaw));
                    UpdateCarDriveState(true);
                }
            }
            else
            {
                SimHubText = "No Game Detected";
            }
        }

        // ?? Belt feedback loop ?????????????????????????????????????????????????
        private void UpdateBeltFeedback()
        {
            if (_carSettingsSvc.CurrentSettings == null) return;
            if (!_haveTestingData && !_iracing.IsConnected && !_simHubConnected)
            {
                _simSurge = _simSway = _simHeave = 0;
            }
            if (!_motorSettingsLoaded || !Device.IsConnected) return;
            if (_testAbs) { Device.SendABS((int)_absStrength); return; }

            BeltMotorData value;
            if (_wasInCar || _haveTestingData)
            {
                value = Device.DeviceMotorSettings.Setup(
                    _simSurge, _simSway, _simHeave,
                    _carSettingsSvc.CurrentSettings, _simRotation);
            }
            else
            {
                var s  = _carSettingsSvc.CurrentSettings;
                int rp = s.RestingPoint;
                s.RestingPoint = 0;

                int gravity = 1;
                if (!IracingIsOn || SimHubIsOn)
                    gravity = 0;
                value = Device.DeviceMotorSettings.Setup(0, 0, gravity, s, _simRotation);
                s.RestingPoint = rp;
            }

            bool removeGravity = _iracing.IsConnected;

            // Publish live preview inputs for testing window
            LivePreviewUpdated?.Invoke(_simSurge, _simSway, _simHeave);

            value.SendDataToSerial(Device, _carSettingsSvc.CurrentSettings, removeGravity, _simRotation);
            _lastMotorOutput = value;

            // Publish motor outputs for testing window (left, right, rotation)
            var last = value.GetLastMotorDataSent();
            MotorOutputUpdated?.Invoke(last.Item1, last.Item2, _simRotation);
        }

        // ?? MMF write ??????????????????????????????????????????????????????????
        private void WriteMmf()
        {
            if (string.IsNullOrEmpty(_carName)) return;
            _mmfWriter.Write(new MmfPayload
            {
                CarName           = _carName,
                LongStrengh       = _brakingStrength,
                MaxPower          = (int)_maxOutput,
                CurveAmount       = _brakingCurve,
                CorneringStrength = _corneringStrength,
                VerticalStrength  = _verticalStrength,
                AbsStrength       = _absStrength,
                AbsEnabled        = (byte)(_absEnabled ? 1 : 0),
                InvertCornering   = (byte)(_invertSway ? 1 : 0),
                ConeringCurveAmount = _corneringCurve,
                GForce            = _lastMotorOutput.LeftSurgeOutput,
                LateralG          = _lastMotorOutput.LeftSwayOutput,
                VerticalG         = _lastMotorOutput.LeftHeaveOutput,
                ConnectedToSim    = _iracing.IsConnected,
                ConnectedToBelt   = Device.IsConnected,
                MotorRange        = Math.Abs(L_MAX - L_MIN),
                MotorSurgeValue   = _lastMotorOutput.LeftSurgeOutput,
                MotorSwayValue    = _lastMotorOutput.LeftSwayOutput,
                MotorHeaveValue   = _lastMotorOutput.LeftHeaveOutput
            });
        }

        // ?? Commands impl ??????????????????????????????????????????????????????
        private async Task DoConnectAsync(object? _)
        {
            ConnectButtonEnabled = false;
            DeviceStatusText     = "Scanning...";
            DeviceIsOn           = false;

            using var cts = new CancellationTokenSource();
            bool ok = await Device.ConnectAsync(cts.Token).ConfigureAwait(false);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ok) OnConnectionSuccess();
                else
                {
                    DeviceStatusText     = "No device responded";
                    DeviceIsOn           = false;
                    ControlsEnabled      = false;
                    ConnectButtonEnabled = true;
                }
            });
        }

        private void OnConnectionSuccess()
        {
            DeviceStatusText     = $"Connected: {Device.PortName}";
            DeviceIsOn           = true;
            ControlsEnabled      = true;
            ConnectButtonEnabled = false;
        }

        private void DoApplyMotorSettings(object? _)
        {
            if (_selectedMotorIndex == 1) { R_MIN = MotorStart; R_MAX = MotorEnd; R_INVERT = MotorInverted; }
            else                          { L_MIN = MotorStart; L_MAX = MotorEnd; L_INVERT = MotorInverted; }
            _dualMotors = DualMotorsEnabled;
            Device.SendUpdatedSettings(L_MIN, L_MAX, R_MIN, R_MAX, L_INVERT, R_INVERT, _dualMotors);
            ShowChangesNotSaved = false;
            _motorSettingsLoaded = true;
        }

        private void DoTestAbs(object? _)
        {
            if (_testAbs) return;
            _testAbs = true;
            Task.Delay(2000).ContinueWith(_ => _testAbs = false);
        }

        private async Task DoCheckUpdatesAsync(object? _)
        {
            var info = await _updateSvc.CheckAsync().ConfigureAwait(false);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (info.IsUpdateAvailable)
                {
                    IsUpdateAvailable = true;
                    UpdateTag = info.RemoteTag ?? string.Empty;
                }
                else
                {
                    MessageBox.Show("You are on the latest version.", "Update Check",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        private void DoOpenUpdate(object? _) => _updateSvc.OpenReleasePage();

        private float MaxSpeed = 300;
        private void StartWindLoop()
        {
            try
            {
                if (_windTask != null && _windCts != null && !_windCts.IsCancellationRequested)
                    return; // already running

                _windCts = new CancellationTokenSource();
                var ct = _windCts.Token;
                _windTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            float speed = 0;
                            if (IracingIsOn)
                            {
                                speed = IracingService.Instance.Speed;
                            }
                            var pct = WindPowerPercentage; // 0..100
                            var val = (int)System.Math.Round((pct / 100.0) * (speed / MaxSpeed) * 255.0);
                            try { Device.SendWindPower(val); } catch { }
                            await Task.Delay(33, ct).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                }, ct);
            }
            catch { }
        }

        private void StopWindLoop()
        {
            try
            {
                // ensure we send zero once when disabling
                try { Device.SendWindPower(0); } catch { }

                try
                {
                    _windCts?.Cancel();
                }
                catch { }

                try { _windTask?.Wait(500); } catch { }
                _windTask = null;
                try { _windCts?.Dispose(); } catch { }
                _windCts = null;
            }
            catch { }
        }

        // ?? Windows Message Bridge ?????????????????????????????????????????????
        private void OnBridgeMessage(BeltMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (msg.Type)
                {
                    case BeltMessageType.GForce:              BrakingStrength    = msg.Value; break;
                    case BeltMessageType.GCurve:              BrakingCurve       = msg.Value; break;
                    case BeltMessageType.VForce:              VerticalStrength   = msg.Value; break;
                    case BeltMessageType.CForce:              CorneringStrength  = msg.Value; break;
                    case BeltMessageType.CCurve:              CorneringCurve     = msg.Value; break;
                    case BeltMessageType.MaxOutput:           MaxOutput          = msg.Value; break;
                    case BeltMessageType.InvertConeringForces: InvertSway        = msg.Value != 0; break;
                    case BeltMessageType.ABSEnabled:          AbsEnabled         = msg.Value != 0; break;
                    case BeltMessageType.ABSStrength:         AbsStrength        = msg.Value; break;
                }
            });
        }

        // ?? Cleanup ????????????????????????????????????????????????????????????
        public void Dispose()
        {
            _feedbackTimer.Stop();
            _mmfTimer.Stop();
            _simHubTimer.Stop();
            _saveTimer?.Stop();
            _saveTimer?.Dispose();
            _carSettingsSvc.SaveCurrentCarSettings(_carName);
            // Unsubscribe events to allow clean shutdown
            Device.HandshakeComplete    -= OnHandshakeComplete;
            Device.MessageReceived      -= OnDeviceMessageReceived;
            Device.OnMotorSettingsRecived -= OnMotorSettingsReceived;

            _iracing.Connected         -= OnIracingConnected;
            _iracing.Disconnected      -= OnIracingDisconnected;
            _iracing.ConnectionChanged -= OnIracingConnectionChanged;
            _iracing.TelemetryUpdated  -= UpdateBeltTensionerForces;
            _iracing.GForceUpdated     -= OnGForceUpdated;
            _iracing.AbsTriggered      -= OnAbsTriggered;
            _iracing.CarNameChanged    -= OnCarNameChanged;

            WpfMessageBridge.BeltMessageReceived -= OnBridgeMessage;
            WpfMessageBridge.Detach();

            Device.Dispose();
            _simHub.Dispose();
            _mmfWriter.Dispose();

            // Dispose the singleton iracing service to stop its SDK background work
            try { _iracing.Dispose(); } catch { }
        }
    }
}
