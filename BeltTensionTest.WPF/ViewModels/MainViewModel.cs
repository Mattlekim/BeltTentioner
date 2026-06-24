using BeltAPI;
using BeltTensionTest.WPF.Helpers;
using BeltTensionTest.WPF.Models;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.Shared;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;
using BeltTensionTest.WPF.Views;
using BeltTensionTest;

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

        public static bool OverideMotorAnglesForTesting { get; set; } = false;
        

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

        // Wind graph display: scatter of (speed, power%)
        private PointCollection _windScatterPoints = new PointCollection();
        public PointCollection WindScatterPoints { get => _windScatterPoints; set => SetField(ref _windScatterPoints, value); }

     

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

        private double _windMinSpeed;
        public double WindMinSpeed
        {
            get => _windMinSpeed;
            set
            {
                if (SetField(ref _windMinSpeed, value))
                {
                    OnCarSettingChanged();
                    GenerateWindGraphImage();
                }
            }
        }

        private double _windMinPower;
        public double WindMinPower
        {
            get => _windMinPower;
            set
            {
                if (SetField(ref _windMinPower, value))
                {
                    OnCarSettingChanged();
                    GenerateWindGraphImage();
                }
            }
        }

        private double _windRestingPower;
        public double WindRestingPower
        {
            get => _windRestingPower;
            set
            {
                if (SetField(ref _windRestingPower, value))
                {
                    // Resting power is an application-level setting
                    if (AppSettings != null)
                    {
                        AppSettings.WindRestingPower = (int)_windRestingPower;
                        _settingsSvc.Save(AppSettings);
                    }
                    GenerateWindGraphImage();
                }
            }
        }

        private double _windPowerPercentage;
        public double WindPowerPercentage
        {
            get => _windPowerPercentage;
            set
            {
                if (SetField(ref _windPowerPercentage, value))
                {
                    OnCarSettingChanged();
                    GenerateWindGraphImage();
                }
            }
        }

        private float _windCurve = 1f;
        public float WindCurve
        {
            get => _windCurve;
            set
            {
                if (SetField(ref _windCurve, value))
                {
                    OnCarSettingChanged();
                    GenerateWindGraphImage();
                }
            }
        }

        private BitmapSource? _windGraphImageSource;
        public BitmapSource? WindGraphImageSource { get => _windGraphImageSource; set => SetField(ref _windGraphImageSource, value); }

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
            ConnectCommand          = new AsyncRelayCommand(DoConnectAsync);
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
            _iracing.OnDriverInCarChange += UpdateCarDriveState;
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

            // Placeholder for keeping the timer
            // _windDisplayTimer.Start();

            // Initial car settings
            LoadCarSettings("NA");
            // initial wind graph
            GenerateWindGraphImage();

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
                    else
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            DeviceStatusText = "Connection Failed!";
                            ConnectButtonEnabled = true;
                        });
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

            // Load the disk dictionary so we can check if the requested car exists
            _carSettingsSvc.LoadFromDisk();

            if (!_carSettingsSvc.Settings.ContainsKey(carName))
            {
                // Show a dialog allowing the user to pick an existing save to assign to this car.
                var available = _carSettingsSvc.GetAvailableCarNames()
                    .Where(n => !string.Equals(n, carName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                string? chosenSource = null;

                var app = Application.Current;
                if (app != null)
                {
                    var d = app.Dispatcher;
                    if (d != null && !d.HasShutdownStarted && !d.HasShutdownFinished)
                    {
                        d.Invoke(() =>
                        {
                            var dlg = new SelectCarSaveWindow(available, carName);
                            var res = dlg.ShowDialog();
                            if (res == true)
                                chosenSource = dlg.SelectedCarName;
                        });
                    }
                }

                if (!string.IsNullOrEmpty(chosenSource))
                {
                    // Copy chosen save into new car profile and persist
                    _carSettingsSvc.CopySettings(chosenSource, carName);
                }
                else
                {
                    // If user cancelled or no selection made, fall back to previous behaviour
                    if (_carSettingsSvc.Settings.TryGetValue("NA", out var naSettings))
                        _carSettingsSvc.Settings[carName] = naSettings?.DeepCopy() ?? new CarSettings();
                    else
                        _carSettingsSvc.Settings[carName] = new CarSettings();
                    _carSettingsSvc.SaveCurrentCarSettings(null);
                }
            }

            // Finally load the car settings into CurrentSettings (this will set CurrentSettings)
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
            _windMinSpeed = (double)s.WindMinSpeed;
            _windMinPower = (double)s.WindMinPower;
            // Resting power is stored in app settings (global)
            _windRestingPower = AppSettings?.WindRestingPower ?? 0;
            _windPowerPercentage = (double)s.WindPowerPercentage;
            _windCurve = s.WindCurve;

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
            OnPropertyChanged(nameof(WindMinPower));
            OnPropertyChanged(nameof(WindRestingPower));
            OnPropertyChanged(nameof(WindPowerPercentage));
            OnPropertyChanged(nameof(WindCurve));
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
            s.WindMinSpeed = (int)_windMinSpeed;
            s.WindMinPower = (int)_windMinPower;
            s.WindPowerPercentage = (int)_windPowerPercentage;
            s.WindCurve = _windCurve;
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

            int lastestFirmwareHash = BeltSerialDevice.GetVersionHash(UpdateService.FIRMWARE_VERSION);
            if (lastestFirmwareHash > Device.VersionHash)
            {
                ThemedMessageBox.Show("To update firmware go to settings, flash nano.", "New Firmware Available!");
            }
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
            UpdateCarDriveState(_iracing.isInCar);
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
                _simHubConnected = false;
                SimHubGroupEnabled = false;
                SimHubText       = "Not Connected to SimHub";
                return;
            }

            SimHubIsOn = true;
            _simHubConnected = true;

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
            if (OverideMotorAnglesForTesting)
            {
                if (SelectedMotorIndex == 0) //left motor test
                {
                    
                        Device.SendLeftAngle( MotorSettingsWindow.TestingAngle);
                }
                else if (SelectedMotorIndex == 1) // right motor test
                   
                        Device.SendRightAngle(MotorSettingsWindow.TestingAngle);

                return;
            }


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

            value.SendDataToSerial(Device, _carSettingsSvc.CurrentSettings, _wasInCar, removeGravity, _simRotation);
            _lastMotorOutput = value;

            // Publish motor outputs for testing window (left, right, rotation)
            var last = value.GetLastMotorDataSent();
            MotorOutputUpdated?.Invoke(last.Item1, last.Item2, _simRotation);
        }

        // Generate a simple wind power vs speed graph based on current sliders.
        // Formula: for speed < WindMinSpeed -> 0, otherwise linear ramp up to WindPowerPercentage
        // across displayed max speed (defaults to 150 matching slider max).
        private void GenerateWindGraphImage(int width = 320, int height = 160)
        {
            try
            {
                int w = Math.Max(100, width);
                int h = Math.Max(80, height);
                using var bmp = new System.Drawing.Bitmap(w, h);
                using var g = System.Drawing.Graphics.FromImage(bmp);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // background
                g.Clear(System.Drawing.Color.FromArgb(18, 18, 30));

                int lp = 36, rp = 12, tp = 18, bp = 32;
                int gw = w - lp - rp;
                int gh = h - tp - bp;
                if (gw <= 0 || gh <= 0) return;

                // axes
                using var axisPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(70, 70, 100), 1);
                g.DrawLine(axisPen, lp, tp, lp, tp + gh);
                g.DrawLine(axisPen, lp, tp + gh, lp + gw, tp + gh);

                // grid horizontal
                using var gridPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(38, 38, 58), 1);
                for (int i = 0; i <= 4; i++)
                    g.DrawLine(gridPen, lp, tp + i * gh / 4, lp + gw, tp + i * gh / 4);



                // labels (min/max)
                using var lblBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(160, 160, 190));
                using var lblFont = new System.Drawing.Font("Segoe UI", 8f);
                
                
                g.DrawString("Power", lblFont, lblBrush, lp - 18, tp - 20   );
                // Y labels: 0 .. 255 (PWM)
                double maxYVal = 255.0;
                for (int i = 0; i <= 4; i++)
                {
                    double val = maxYVal - i * (maxYVal / 4.0);
                    var sz = g.MeasureString(((int)val).ToString(), lblFont);
                    g.DrawString(((int)val).ToString(), lblFont, lblBrush, lp - sz.Width - 6, tp + i * gh / 4 - sz.Height / 2);
                }

                // X labels: 0 .. maxSpeed (use 300)
                int maxSpeed = 300;
                for (int i = 0; i <= 4; i++)
                {
                    int sp = i * maxSpeed / 4;
                    var sx = lp + i * gw / 4;
                    var sz = g.MeasureString(sp.ToString(), lblFont);
                    g.DrawString(sp.ToString(), lblFont, lblBrush, sx - sz.Width/2, tp + gh + 4);
                }

                g.DrawString("Speed", lblFont, lblBrush, lp + gw / 2 - 40, tp + gh + 16);
                
                // compute points
                var pts = new List<System.Drawing.PointF>();
                // Use same formula as StartWindLoop: apply WindMinSpeed and WindCurve, produce PWM 0..255
                for (int x = 0; x <= gw; x++)
                {
                    double speed = (double)x / gw * maxSpeed;
                    double pwm = 0.0;
                    if (MaxSpeed > WindMinSpeed)
                    {
                        // speed below min -> use resting power
                        if (speed < WindMinSpeed)
                        {
                            double restPct = WindRestingPower / 255;
                            pwm = Math.Round(restPct * 255.0);
                            pwm = Math.Clamp(pwm, 0.0, 255.0);
                        }
                        else
                        {
                            double maxPct = WindPowerPercentage / 255;
                            double minPct = WindMinPower / 255;
                            double norm = (speed - WindMinSpeed) / (MaxSpeed - WindMinSpeed);
                            norm = Math.Clamp(norm, 0.0, 1.0);
                            double curved = Math.Pow(norm, WindCurve <= 0 ? 1.0 : WindCurve);
                            double pct = minPct + (maxPct - minPct) * curved;
                            pwm = Math.Round(pct * 255.0);
                            pwm = Math.Clamp(pwm, 0.0, 255.0);
                        }
                    }
                    float px = lp + x;
                    float py = tp + (float)((1.0 - (pwm / 255.0)) * (gh));
                    pts.Add(new System.Drawing.PointF(px, py));
                }

                // fill under curve
                if (pts.Count >= 2)
                {
                    var poly = new List<System.Drawing.PointF> { new System.Drawing.PointF(pts[0].X, tp + gh) };
                    poly.AddRange(pts);
                    poly.Add(new System.Drawing.PointF(pts[^1].X, tp + gh));
                    using var fillBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(35, 80, 160, 255));
                    g.FillPolygon(fillBrush, poly.ToArray());
                    using var glow = new System.Drawing.Pen(System.Drawing.Color.FromArgb(50, 80, 160, 255), 4);
                    g.DrawLines(glow, pts.ToArray());
                    using var linePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(100, 160, 255), 2);
                    g.DrawLines(linePen, pts.ToArray());
                }

                // Convert to BitmapSource
                var handle = bmp.GetHbitmap();
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    WindGraphImageSource = src;
                }
                finally { DeleteObject(handle); }
            }
            catch { }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

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
            if (MotorStart >= MotorEnd) { ThemedMessageBox.Show("Motor start angle must be less than end angle.", "Invalid Settings", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

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
                    ThemedMessageBox.Show("You are on the latest version.", "Update Check",
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
                            var minPctVal = WindMinPower; // 0..100
                            int val = 0;

                       
                            if (MaxSpeed > WindMinSpeed)
                            {
                                if (!_wasInCar)
                                {
                                    double restPct = WindRestingPower;
                                    val = (int)System.Math.Round(restPct);
                                    val = (int)Math.Clamp(val, 0, 255);
                                }
                                else
                                {
                                    double maxPct = pct / 100.0;
                                    double minPct = minPctVal / 100.0;
                                    // normalized progress from min speed to max speed
                                    double norm = (speed - WindMinSpeed) / (MaxSpeed - WindMinSpeed);
                                    norm = Math.Clamp(norm, 0.0, 1.0);
                                    // apply curve: WindCurve==1 => linear, >1 => more gradual start
                                    double curved = Math.Pow(norm, WindCurve);
                                    double interp = minPct + (maxPct - minPct) * curved;
                                    val = (int)System.Math.Round(interp * 255.0);
                                    val = (int)Math.Clamp(val, 0, 255);
                                   // val = 100;
                                }
                            }
                            try { Device.SendWindPower((ushort)val); } catch { }
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
