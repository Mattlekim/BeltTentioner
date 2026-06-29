using BeltAPI;
using IRSDKSharper;
using System;
using System.Threading.Tasks;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Wraps the iRacing SDK, exposing events in a WPF-friendly way.
    /// Mirrors IracingCommunicator from WinForms but is injectable.
    /// </summary>
    public class IracingService : IDisposable
    {
        public enum RumbleSide
        {
            None,
            Left,
            Right,
            Both
        }

        private static readonly Lazy<IracingService> _instance = new(() => new IracingService());
        public static IracingService Instance => _instance.Value;

        private IRacingSdk? _sdk;
        private bool _isConnected;
        private bool _dataInitialized;
        private string _oldCarName = string.Empty;

        private IRacingSdkDatum? _datumSpeed;
        private IRacingSdkDatum? _datumGear;
        private IRacingSdkDatum? _datumAbs;
        private IRacingSdkDatum? _datumReplay;
        private IRacingSdkDatum? _datumLong;
        private IRacingSdkDatum? _datumLat;
        private IRacingSdkDatum? _datumVert;
        private IRacingSdkDatum? _datumPitch;
        private IRacingSdkDatum? _datumRoll;
        private IRacingSdkDatum? _datumYaw;

        // Rumble pitch datums (front axle)
        private IRacingSdkDatum? _datumRumbleFL;
        private IRacingSdkDatum? _datumRumbleFR;

        public bool IsConnected => _isConnected;
        public bool Enabled { get; set; } = true;

        // Events — same contract as WinForms IracingCommunicator
        public event Action<bool>? ConnectionChanged;
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<float>? GForceUpdated;
        public event Action<float, float, float, Rotation>? TelemetryUpdated;
        public event Action? AbsTriggered;
        public event Action<string>? CarNameChanged;
        public event Action<int,int>? GearChanged;
        public event Action<RumbleSide>? RumbleStripDetected;

        public event Action<bool> OnDriverInCarChange;

        private IracingService()
        {
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    _sdk = new IRacingSdk();
                    _sdk.OnConnected += OnConnected;
                    _sdk.OnDisconnected += OnDisconnected;
                    _sdk.OnTelemetryData += OnTelemetryData;
                    _sdk.Start();
                }
                catch
                {
                }
            });
        }

        private void OnConnected()
        {
            if (_isConnected) return;
            _isConnected = true;
            ConnectionChanged?.Invoke(true);
            Connected?.Invoke();
        }

        private void OnDisconnected()
        {
            if (!_isConnected) return;
            _isConnected = false;
            _dataInitialized = false;
            ConnectionChanged?.Invoke(false);
            Disconnected?.Invoke();
        }

        private bool SetupDatums()
        {
            if (_dataInitialized) return true;
            if (_sdk == null) return false;

            try
            {
                _datumAbs = _sdk.Data.TelemetryDataProperties["BrakeABSactive"];
                _datumReplay = _sdk.Data.TelemetryDataProperties["IsReplayPlaying"];
                _datumLong = _sdk.Data.TelemetryDataProperties["LongAccel"];
                _datumLat = _sdk.Data.TelemetryDataProperties["LatAccel"];
                _datumVert = _sdk.Data.TelemetryDataProperties["VertAccel"];
                _datumPitch = _sdk.Data.TelemetryDataProperties["Pitch"];
                _datumRoll = _sdk.Data.TelemetryDataProperties["Roll"];
                _datumYaw = _sdk.Data.TelemetryDataProperties["Yaw"];
                _datumSpeed = _sdk.Data.TelemetryDataProperties["Speed"];
                _datumGear = _sdk.Data.TelemetryDataProperties["Gear"];
                // Rumble pitch (front axle)
                try { _datumRumbleFL = _sdk.Data.TelemetryDataProperties["TireLF_RumblePitch"]; } catch { _datumRumbleFL = null; }
                try { _datumRumbleFR = _sdk.Data.TelemetryDataProperties["TireRF_RumblePitch"]; } catch { _datumRumbleFR = null; }

                _dataInitialized = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        bool isReplay = false;
        bool _wasReplay = false;

        private bool _rumbleLeftPrev = false;
        private bool _rumbleRightPrev = false;

        private int _lastGear = 0;
        private bool _haveGear = false;

        private readonly RmsFilter _rmsLeft = new RmsFilter(12);
        private readonly RmsFilter _rmsRight = new RmsFilter(12);

        private const float RUMBLE_MIN_SPEED = 5.0f;
        private const float RUMBLE_RMS_THRESHOLD = 2.0f;

        private float speed;
        public float Speed => speed;
        public bool isInCar => !isReplay;

        private void OnTelemetryData()
        {
            if (!Enabled) return;
            if (!SetupDatums()) return;

            try
            {
                if (_sdk?.Data.SessionInfo?.DriverInfo != null)
                {
                    var drivers = _sdk.Data.SessionInfo.DriverInfo.Drivers;
                    int idx = _sdk.Data.SessionInfo.DriverInfo.DriverCarIdx;
                    if (idx < drivers.Count)
                    {
                        var name = drivers[idx].CarScreenName;
                        if (name != _oldCarName)
                        {
                            _oldCarName = name;
                            CarNameChanged?.Invoke(name);
                        }
                    }
                }
            }
            catch { }

            _wasReplay = isReplay;
            isReplay = _sdk!.Data.GetBool(_datumReplay);

            if (isReplay != _wasReplay)
                OnDriverInCarChange?.Invoke(isInCar);

            if (isReplay)
            {
                TelemetryUpdated?.Invoke(0, 0, 0, Rotation.Zero);
                GForceUpdated?.Invoke(0);
                // reset gear state when entering replay
                _haveGear = false;
                return;
            }

            bool abs = _sdk.Data.GetBool(_datumAbs);
            float surge = -(_sdk.Data.GetFloat(_datumLong) / 9.81f);
            float sway = _sdk.Data.GetFloat(_datumLat) / 9.81f;
            float heave = _sdk.Data.GetFloat(_datumVert) / 9.81f;
            float pitch = _sdk.Data.GetFloat(_datumPitch);
            float roll = _sdk.Data.GetFloat(_datumRoll);
            float yaw = _sdk.Data.GetFloat(_datumYaw);
            speed = _sdk.Data.GetFloat(_datumSpeed);

            TelemetryUpdated?.Invoke(surge, sway, heave, new Rotation(pitch, roll, yaw));
            GForceUpdated?.Invoke(-Math.Clamp(surge, -1000, 0));
            if (abs) AbsTriggered?.Invoke();

            // --- Gear change detection ---
            try
            {
                if (_datumGear != null)
                {
                    // read as float and convert to int (SDK exposes gear as float)
                    float gf = _sdk.Data.GetFloat(_datumGear);
                    int gear = (int)Math.Round(gf);
                    if (!_haveGear)
                    {
                        _lastGear = gear;
                        _haveGear = true;
                    }
                    else if (gear != _lastGear)
                    {
                        try { GearChanged?.Invoke(_lastGear, gear); } catch { }
                        _lastGear = gear;
                    }
                }
            }
            catch { }

            // --- Rumble strip detection using Tire RumblePitch ---
            try
            {
                bool rumbleLeft = false;
                bool rumbleRight = false;

                if (_datumRumbleFL != null && _datumRumbleFR != null && speed >= RUMBLE_MIN_SPEED)
                {
                    float fl = _sdk.Data.GetFloat(_datumRumbleFL);
                    float fr = _sdk.Data.GetFloat(_datumRumbleFR);

                    float rmsLeft = _rmsLeft.Add(fl);
                    float rmsRight = _rmsRight.Add(fr);

                    rumbleLeft = rmsLeft >= RUMBLE_RMS_THRESHOLD;
                    rumbleRight = rmsRight >= RUMBLE_RMS_THRESHOLD;
                }

                if (rumbleLeft != _rumbleLeftPrev || rumbleRight != _rumbleRightPrev)
                {
                    _rumbleLeftPrev = rumbleLeft;
                    _rumbleRightPrev = rumbleRight;

                    var side = RumbleSide.None;
                    if (rumbleLeft && rumbleRight) side = RumbleSide.Both;
                    else if (rumbleLeft) side = RumbleSide.Left;
                    else if (rumbleRight) side = RumbleSide.Right;

                    try { RumbleStripDetected?.Invoke(side); } catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_sdk != null)
                {
                    _sdk.OnConnected -= OnConnected;
                    _sdk.OnDisconnected -= OnDisconnected;
                    _sdk.OnTelemetryData -= OnTelemetryData;
                    try { _sdk.Stop(); } catch { }
                    try { (_sdk as IDisposable)?.Dispose(); } catch { }
                    _sdk = null;
                }
            }
            catch { }
        }
    }

    public class RmsFilter
    {
        private readonly float[] _buffer;
        private int _index;
        private int _count;

        public RmsFilter(int windowSize)
        {
            if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
            _buffer = new float[windowSize];
            _index = 0;
            _count = 0;
        }

        public float Add(float sample)
        {
            _buffer[_index] = sample;
            _index = (_index + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;

            double sumSq = 0.0;
            for (int i = 0; i < _count; i++)
                sumSq += _buffer[i] * _buffer[i];

            return (float)Math.Sqrt(sumSq / _count);
        }
    }
}
