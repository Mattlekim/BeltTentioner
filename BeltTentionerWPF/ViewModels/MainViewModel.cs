using BeltAPI;
using BeltTentionerWPF.Services;
using IRSDKSharper;
using System.IO.Ports;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BeltTentionerWPF.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        // ?? Device ??????????????????????????????????????????????????????????
        public BeltSerialDevice BeltDevice { get; } = new BeltSerialDevice();
        private bool _motorSettingsLoaded = false;
        private bool _wasInCar = false;
        private bool _haveTestingData = false;

        // ?? iRacing ?????????????????????????????????????????????????????????
        private IRacingSdk? _irSdk;
        private IRacingSdkDatum? _dLong, _dLat, _dVert, _dPitch, _dRoll, _dYaw, _dABS, _dReplay;
        private bool _datumInit = false;
        private bool _irConnected = false;
        private bool _irEnabled = true;

        // ?? Timers ???????????????????????????????????????????????????????????
        private readonly DispatcherTimer _updateTimer;
        private System.Timers.Timer? _saveTimer;

        // ?? Sim forces (raw) ?????????????????????????????????????????????????
        private float _simSurge, _simSway, _simHeave;
        private Rotation _simRotation = Rotation.Zero;

        // ?? Car info ?????????????????????????????????????????????????????????
        private string _carName = "NA";
        private string _irStatusText = "Not connected";
        private string _deviceStatusText = "Not connected";
        private bool _deviceConnected = false;
        private bool _irActive = false;

        // ?? Motor range (from device) ????????????????????????????????????????
        private int _lMin = 0, _lMax = 180, _rMin = 0, _rMax = 180;
        private bool _lInvert = false, _rInvert = false;
        private bool _dualMotors = false;
        private int _selectedMotorIndex = 0;

        // ?? Live feedback ????????????????????????????????????????????????????
        private float _displaySurge, _displaySway, _displayHeave;
        private BeltMotorData _lastMotorData;
        private bool _isLoading = false;

        // ?? Testing ??????????????????????????????????????????????????????????
        private TestingViewModel? _testingViewModel;
        public TestingViewModel TestingVM => _testingViewModel ??= new TestingViewModel(this);

        // ?? Properties exposed to UI ?????????????????????????????????????????
        public string CarName
        {
            get => _carName;
            set { if (SetField(ref _carName, value)) App.Current.Dispatcher.InvokeAsync(() => OnPropertyChanged()); }
        }

        public string IrStatusText { get => _irStatusText; set => SetField(ref _irStatusText, value); }
        public string DeviceStatusText { get => _deviceStatusText; set => SetField(ref _deviceStatusText, value); }
        public bool DeviceConnected { get => _deviceConnected; set => SetField(ref _deviceConnected, value); }
        public bool IrActive { get => _irActive; set => SetField(ref _irActive, value); }

        public bool UseIracing
        {
            get => AppSettingsService.Current.UseIracing;
            set
            {
                AppSettingsService.Current.UseIracing = value;
                _irEnabled = value;
                if (_irSdk != null) { /* enabled flag checked in telemetry handler */ }
                OnPropertyChanged();
                AppSettingsService.Save();
            }
        }

        public bool AutoConnectOnStartup
        {
            get => AppSettingsService.Current.AutoConnectOnStartup;
            set { AppSettingsService.Current.AutoConnectOnStartup = value; OnPropertyChanged(); AppSettingsService.Save(); }
        }

        public bool HasPendingMotorChanges { get => _hasPendingMotorChanges; set => SetField(ref _hasPendingMotorChanges, value); }
        private bool _hasPendingMotorChanges;

        // Motor UI
        public int SelectedMotorIndex
        {
            get => _selectedMotorIndex;
            set { SetField(ref _selectedMotorIndex, value); RefreshMotorUI(); }
        }

        public int MotorStart { get => _motorStart; set { SetField(ref _motorStart, value); MarkMotorChanged(); } }
        private int _motorStart;
        public int MotorEnd { get => _motorEnd; set { SetField(ref _motorEnd, value); MarkMotorChanged(); } }
        private int _motorEnd;
        public bool MotorInverted { get => _motorInverted; set { SetField(ref _motorInverted, value); MarkMotorChanged(); } }
        private bool _motorInverted;
        public bool DualMotors { get => _dualMotors; set { SetField(ref _dualMotors, value); MarkMotorChanged(); } }

        // ?? Car Settings ??????????????????????????????????????????????????????
        public float SurgeStrength { get => _surgeStrength; set { SetField(ref _surgeStrength, value); ApplyCarSettings(); } }
        private float _surgeStrength = 1f;

        public float SurgeCurve { get => _surgeCurve; set { SetField(ref _surgeCurve, value); ApplyCarSettings(); } }
        private float _surgeCurve = 1f;

        public bool InvertSurge { get => _invertSurge; set { SetField(ref _invertSurge, value); ApplyCarSettings(); } }
        private bool _invertSurge;

        public float SwayStrength { get => _swayStrength; set { SetField(ref _swayStrength, value); ApplyCarSettings(); } }
        private float _swayStrength = 1f;

        public float SwayCurve { get => _swayCurve; set { SetField(ref _swayCurve, value); ApplyCarSettings(); } }
        private float _swayCurve = 1f;

        public bool InvertSway { get => _invertSway; set { SetField(ref _invertSway, value); ApplyCarSettings(); } }
        private bool _invertSway;

        public float NegativeSway { get => _negativeSway; set { SetField(ref _negativeSway, value); ApplyCarSettings(); } }
        private float _negativeSway;

        public float HeaveStrength { get => _heaveStrength; set { SetField(ref _heaveStrength, value); ApplyCarSettings(); } }
        private float _heaveStrength = 1f;

        public bool InvertHeave { get => _invertHeave; set { SetField(ref _invertHeave, value); ApplyCarSettings(); } }
        private bool _invertHeave;

        public int MaxPower { get => _maxPower; set { SetField(ref _maxPower, value); ApplyCarSettings(); } }
        private int _maxPower = 100;

        public int RestingPoint { get => _restingPoint; set { SetField(ref _restingPoint, value); ApplyCarSettings(); } }
        private int _restingPoint;

        public float AbsStrength { get => _absStrength; set { SetField(ref _absStrength, value); ApplyCarSettings(); } }
        private float _absStrength = 1f;

        public bool AbsEnabled { get => _absEnabled; set { SetField(ref _absEnabled, value); ApplyCarSettings(); } }
        private bool _absEnabled;

        // Rumble / rumble strip settings
        public float RumbleStrength { get => _rumbleStrength; set { SetField(ref _rumbleStrength, value); ApplyCarSettings(); } }
        private float _rumbleStrength = 1f;

        public bool RumbleStripEnabled { get => _rumbleStripEnabled; set { SetField(ref _rumbleStripEnabled, value); ApplyCarSettings(); } }
        private bool _rumbleStripEnabled;

        public float PitchStrength { get => _pitchStrength; set { SetField(ref _pitchStrength, value); ApplyCarSettings(); } }
        private float _pitchStrength = 10f;

        public bool InvertPitch { get => _invertPitch; set { SetField(ref _invertPitch, value); ApplyCarSettings(); } }
        private bool _invertPitch;

        public float RollStrength { get => _rollStrength; set { SetField(ref _rollStrength, value); ApplyCarSettings(); } }
        private float _rollStrength = 10f;

        public bool InvertRoll { get => _invertRoll; set { SetField(ref _invertRoll, value); ApplyCarSettings(); } }
        private bool _invertRoll;

        public float MasterTiltStrength { get => _masterTiltStrength; set { SetField(ref _masterTiltStrength, value); ApplyCarSettings(); } }
        private float _masterTiltStrength = 10f;

        // ?? Commands ??????????????????????????????????????????????????????????
        public ICommand ConnectCommand { get; }
        public ICommand ApplyMotorSettingsCommand { get; }
        public ICommand OpenTestingCommand { get; }

        // ?? Constructor ???????????????????????????????????????????????????????
        public MainViewModel()
        {
            AppSettingsService.Load();
            CarSettingsService.Instance.LoadFromFile("NA");
            LoadSettingsToUI(CarSettingsService.Instance.CurrentSettings);

            BeltDevice.MessageReceived += OnSerialMessage;
            BeltDevice.HandshakeComplete += OnHandshakeComplete;
            BeltDevice.OnMotorSettingsRecived += OnMotorSettingsReceived;

            ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !DeviceConnected);
            ApplyMotorSettingsCommand = new RelayCommand(ApplyMotorSettings, () => DeviceConnected && HasPendingMotorChanges);
            OpenTestingCommand = new RelayCommand(() => OpenTesting());

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _updateTimer.Tick += OnUpdateTick;
            _updateTimer.Start();

            _irEnabled = AppSettingsService.Current.UseIracing;
            StartIRacing();

            if (AppSettingsService.Current.AutoConnectOnStartup)
                _ = ConnectAsync();
        }

        // ?? iRacing SDK ???????????????????????????????????????????????????????
        private void StartIRacing()
        {
            try
            {
                _irSdk = new IRacingSdk();
                _irSdk.OnConnected += () =>
                {
                    _irConnected = true;
                    App.Current.Dispatcher.InvokeAsync(() => { IrStatusText = "Connected"; IrActive = true; });
                };
                _irSdk.OnDisconnected += () =>
                {
                    _irConnected = false;
                    _datumInit = false;
                    App.Current.Dispatcher.InvokeAsync(() => { IrStatusText = "Not connected"; IrActive = false; });
                };
                _irSdk.OnTelemetryData += OnIrTelemetry;
                _irSdk.OnSessionInfo += OnIrSessionInfo;
                _irSdk.Start();
            }
            catch { }
        }

        private void OnIrSessionInfo()
        {
            if (!_irEnabled || _irSdk == null) return;
            try
            {
                var si = _irSdk.Data.SessionInfo;
                if (si?.DriverInfo?.Drivers == null) return;
                int idx = si.DriverInfo.DriverCarIdx;
                if (idx < si.DriverInfo.Drivers.Count)
                {
                    string carName = si.DriverInfo.Drivers[idx].CarScreenName ?? "NA";
                    if (carName != _carName)
                        App.Current.Dispatcher.InvokeAsync(() => LoadCar(carName));
                }
            }
            catch { }
        }

        private void OnIrTelemetry()
        {
            if (!_irEnabled || _irSdk == null) return;
            try
            {
                if (!_datumInit) InitDatums();

                bool isReplay = _irSdk.Data.GetBool(_dReplay);
                if (isReplay) { UpdateForces(0, 0, 0, Rotation.Zero); return; }

                float surge = -(_irSdk.Data.GetFloat(_dLong) / 9.81f);
                float sway = _irSdk.Data.GetFloat(_dLat) / 9.81f;
                float heave = _irSdk.Data.GetFloat(_dVert) / 9.81f;
                float pitch = _irSdk.Data.GetFloat(_dPitch);
                float roll = _irSdk.Data.GetFloat(_dRoll);
                float yaw = _irSdk.Data.GetFloat(_dYaw);

                UpdateForces(surge, sway, heave, new Rotation(pitch, roll, yaw));

                bool abs = _irSdk.Data.GetBool(_dABS);
                if (abs && AbsEnabled)
                    BeltDevice.SendABS((int)AbsStrength);
            }
            catch { }
        }

        private void InitDatums()
        {
            if (_irSdk == null) return;
            _dLong = _irSdk.Data.TelemetryDataProperties["LongAccel"];
            _dLat = _irSdk.Data.TelemetryDataProperties["LatAccel"];
            _dVert = _irSdk.Data.TelemetryDataProperties["VertAccel"];
            _dPitch = _irSdk.Data.TelemetryDataProperties["Pitch"];
            _dRoll = _irSdk.Data.TelemetryDataProperties["Roll"];
            _dYaw = _irSdk.Data.TelemetryDataProperties["Yaw"];
            _dABS = _irSdk.Data.TelemetryDataProperties["BrakeABSactive"];
            _dReplay = _irSdk.Data.TelemetryDataProperties["IsReplayPlaying"];
            _datumInit = true;
        }

        // ?? Device events ?????????????????????????????????????????????????????
        private void OnSerialMessage(string msg)
        {
            if (msg == "DEVICE_UNPLUGGED" || (msg.Length > 0 && msg[0] == 'N'))
            {
                App.Current.Dispatcher.InvokeAsync(() =>
                {
                    DeviceConnected = false;
                    DeviceStatusText = "Disconnected";
                    _motorSettingsLoaded = false;
                });
            }
        }

        private void OnHandshakeComplete()
        {
            App.Current.Dispatcher.InvokeAsync(() =>
            {
                DeviceConnected = true;
                DeviceStatusText = $"Connected ({BeltDevice.PortName})";
                BeltDevice.SendRequestSettings();
            });
        }

        private void OnMotorSettingsReceived()
        {
            _lMin = (int)BeltDevice.DeviceMotorSettings.LeftMinimumAngle;
            _lMax = (int)BeltDevice.DeviceMotorSettings.LeftMaximumAngle;
            _rMin = (int)BeltDevice.DeviceMotorSettings.RightMinimumAngle;
            _rMax = (int)BeltDevice.DeviceMotorSettings.RightMaximumAngle;
            _lInvert = BeltDevice.DeviceMotorSettings.LeftInverted;
            _rInvert = BeltDevice.DeviceMotorSettings.RightInverted;
            _dualMotors = BeltDevice.DuelMotors;
            _motorSettingsLoaded = true;
            App.Current.Dispatcher.InvokeAsync(RefreshMotorUI);
        }

        private void RefreshMotorUI()
        {
            _isLoading = true;
            if (_selectedMotorIndex == 1)
            {
                MotorStart = _rMin; MotorEnd = _rMax; MotorInverted = _rInvert;
            }
            else
            {
                MotorStart = _lMin; MotorEnd = _lMax; MotorInverted = _lInvert;
            }
            OnPropertyChanged(nameof(DualMotors));
            HasPendingMotorChanges = false;
            _isLoading = false;
        }

        private void MarkMotorChanged()
        {
            if (_isLoading) return;
            if (_selectedMotorIndex == 1) { _rMin = MotorStart; _rMax = MotorEnd; _rInvert = MotorInverted; }
            else { _lMin = MotorStart; _lMax = MotorEnd; _lInvert = MotorInverted; }
            HasPendingMotorChanges = true;
        }

        private void ApplyMotorSettings()
        {
            BeltDevice.SendUpdatedSettings(_lMin, _lMax, _rMin, _rMax, _lInvert, _rInvert, _dualMotors);
            HasPendingMotorChanges = false;
            _motorSettingsLoaded = true;
        }

        // ?? Connect ???????????????????????????????????????????????????????????
        private async Task ConnectAsync()
        {
            DeviceStatusText = "Scanning...";
            DeviceConnected = false;
            using var cts = new CancellationTokenSource();
            bool ok = await BeltDevice.ConnectAsync(cts.Token).ConfigureAwait(false);
            if (!ok)
                App.Current.Dispatcher.InvokeAsync(() => { DeviceStatusText = "No device found"; });
        }

        // ?? Forces ????????????????????????????????????????????????????????????
        public void UpdateForces(float surge, float sway, float heave, Rotation rotation)
        {
            _simSurge = surge; _simSway = sway; _simHeave = heave; _simRotation = rotation;
            _haveTestingData = true;
        }

        public void StopForces()
        {
            _simSurge = 0; _simSway = 0; _simHeave = 0; _simRotation = Rotation.Zero;
            _haveTestingData = false;
        }

        // ?? Main update loop ??????????????????????????????????????????????????
        private void OnUpdateTick(object? sender, EventArgs e)
        {
            if (!_motorSettingsLoaded || !DeviceConnected) return;
            var settings = CarSettingsService.Instance.CurrentSettings;
            if (settings == null) return;

            bool inCar = _wasInCar || _haveTestingData;
            BeltMotorData value;

            if (inCar)
            {
                value = BeltDevice.DeviceMotorSettings.Setup(_simSurge, _simSway, _simHeave, settings, _simRotation);
            }
            else
            {
                var s2 = settings;
                int rp = s2.RestingPoint; s2.RestingPoint = 0;
                value = BeltDevice.DeviceMotorSettings.Setup(0, 0, 1, s2, _simRotation);
                s2.RestingPoint = rp;
            }

            bool removeGravity = _irConnected;
            value.SendDataToSerial(BeltDevice, settings, removeGravity, _simRotation);

            _lastMotorData = value;
            _displaySurge = value.LeftSurgeOutput;
            _displaySway = value.LeftSwayOutput;
            _displayHeave = value.LeftHeaveOutput;

            var (lOut, rOut) = value.GetLastMotorDataSent();
            _testingViewModel?.UpdateMotorOutput(lOut, rOut, _simRotation);
            _testingViewModel?.UpdateLivePreview(_simSurge, _simSway, _simHeave);
        }

        // ?? Car / Settings ????????????????????????????????????????????????????
        public void LoadCar(string name)
        {
            CarSettingsService.Instance.SaveCurrent(_carName);
            _carName = name;
            CarName = name;
            CarSettingsService.Instance.LoadFromFile(name);
            LoadSettingsToUI(CarSettingsService.Instance.CurrentSettings);
        }

        private void LoadSettingsToUI(CarSettings s)
        {
            _isLoading = true;
            SurgeStrength = s.SurgeStrenght;
            SurgeCurve = s.SurgeCurveAmount;
            InvertSurge = s.InvertSurge;
            SwayStrength = s.SwayStrength;
            SwayCurve = s.SwayCurveAmount;
            InvertSway = s.InvertSway;
            NegativeSway = s.NegativeSway;
            HeaveStrength = s.HeaveStrength;
            InvertHeave = s.InvertHeave;
            MaxPower = s.MaxPower;
            RestingPoint = s.RestingPoint;
            AbsStrength = s.AbsStrength;
            AbsEnabled = s.AbsEnabled;
            RumbleStrength = s.RumbleStrength;
            RumbleStripEnabled = s.RumbleStripEnabled;
            PitchStrength = s.PitchStrength;
            InvertPitch = s.InvertPitch;
            RollStrength = s.RollStrength;
            InvertRoll = s.InvertRoll;
            MasterTiltStrength = s.MasterTiltStrength;
            _isLoading = false;
        }

        private void ApplyCarSettings()
        {
            if (_isLoading) return;
            var s = CarSettingsService.Instance.CurrentSettings;
            s.SurgeStrenght = SurgeStrength;
            s.SurgeCurveAmount = SurgeCurve;
            s.InvertSurge = InvertSurge;
            s.SwayStrength = SwayStrength;
            s.SwayCurveAmount = SwayCurve;
            s.InvertSway = InvertSway;
            s.NegativeSway = NegativeSway;
            s.HeaveStrength = HeaveStrength;
            s.InvertHeave = InvertHeave;
            s.MaxPower = MaxPower;
            s.RestingPoint = RestingPoint;
            s.AbsStrength = AbsStrength;
            s.AbsEnabled = AbsEnabled;
            s.RumbleStrength = RumbleStrength;
            s.RumbleStripEnabled = RumbleStripEnabled;
            s.PitchStrength = PitchStrength;
            s.InvertPitch = InvertPitch;
            s.RollStrength = RollStrength;
            s.InvertRoll = InvertRoll;
            s.MasterTiltStrength = MasterTiltStrength;
            SaveSoon();
        }

        private void SaveSoon()
        {
            _saveTimer?.Stop(); _saveTimer?.Dispose();
            _saveTimer = new System.Timers.Timer(2000) { AutoReset = false };
            _saveTimer.Elapsed += (s, e) => CarSettingsService.Instance.SaveCurrent(_carName);
            _saveTimer.Start();
        }

        private void OpenTesting()
        {
            var win = new Views.TestingWindow { DataContext = TestingVM };
            win.Show();
        }

        // ?? Dispose ???????????????????????????????????????????????????????????
        public void Dispose()
        {
            _updateTimer.Stop();
            _saveTimer?.Stop();
            CarSettingsService.Instance.SaveCurrent(_carName);
            try { BeltDevice.Dispose(); } catch { }
            try { _irSdk?.Stop(); } catch { }
        }
    }
}
