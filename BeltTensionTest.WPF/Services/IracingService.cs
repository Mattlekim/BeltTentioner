using BeltAPI;
using IRSDKSharper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>Spotter car-alongside states, mirroring the SDK's irsdk_CarLeftRight values.</summary>
    public enum CarLeftRight
    {
        Off = 0,
        Clear = 1,        // no cars around us
        CarLeft = 2,
        CarRight = 3,
        CarLeftRight = 4, // cars on both sides
        TwoCarsLeft = 5,
        TwoCarsRight = 6,
    }

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

        private IRacingSdkDatum? _datumSessionNum;
        private IRacingSdkDatum? _datumSessionTime;
        private IRacingSdkDatum? _datumCarLeftRight;
        private IRacingSdkDatum? _datumSessionState;
        private IRacingSdkDatum? _datumFuelLevel;
        private IRacingSdkDatum? _datumBrakeBias;   // dcBrakeBias: only cars with an adjustable bias
        private IRacingSdkDatum? _datumTrackTemp;
        private IRacingSdkDatum? _datumAirTemp;
        private IRacingSdkDatum? _datumClutch;
        private IRacingSdkDatum? _datumReplayFrame;
        private IRacingSdkDatum? _datumReplayFrameEnd;

        /// <summary>
        /// Type of the session currently running ("Practice", "Lone Qualify",
        /// "Open Qualify", "Race", ...) from the session info YAML. Empty until
        /// connected / session info arrives.
        /// </summary>
        public string SessionType { get; private set; } = string.Empty;

        /// <summary>Raised when the current session changes type (practice → qualy → race, ...).</summary>
        public event Action<string>? SessionTypeChanged;

        /// <summary>
        /// Track length in meters, parsed from WeekendInfo.TrackLength
        /// ("3.85 km"). 0 until connected / session info arrives.
        /// </summary>
        public float TrackLengthMeters { get; private set; }
        private string _trackLengthRaw = string.Empty;

        /// <summary>Session clock in seconds (SessionTime), refreshed every telemetry tick.</summary>
        public double SessionTime { get; private set; }

        /// <summary>
        /// Spotter "car alongside" state (CarLeftRight telemetry var),
        /// refreshed every telemetry tick. Off until connected.
        /// </summary>
        public CarLeftRight CarsAlongside { get; private set; } = CarLeftRight.Off;

        /// <summary>
        /// Sector start positions (lap-distance pct) from the session's
        /// SplitTimeInfo YAML, or null until connected. Used for sector timing.
        /// </summary>
        public IReadOnlyList<float>? SectorStartPcts { get; private set; }

        /// <summary>
        /// iRacing session state (SessionState telemetry var, irsdk_SessionState):
        /// see the SessionState* constants. 0 until connected.
        /// </summary>
        public int SessionState { get; private set; }

        public const int SessionStateGetInCar = 1;
        public const int SessionStateWarmup = 2;
        public const int SessionStateParadeLaps = 3;
        public const int SessionStateRacing = 4;
        public const int SessionStateCheckered = 5;
        public const int SessionStateCoolDown = 6;

        /// <summary>True when the event uses a standing start (WeekendOptions.StandingStart).</summary>
        public bool StandingStart { get; private set; }

        /// <summary>
        /// Starting grid of the current session, keyed by CarIdx: overall and
        /// class grid position, both 1-based. Taken from the session's
        /// QualifyPositions, falling back to QualifyResultsInfo. Empty until
        /// a grid is published.
        /// </summary>
        public IReadOnlyDictionary<int, (int Pos, int ClassPos)> GridPositions { get; private set; }
            = new Dictionary<int, (int, int)>();
        private object? _gridSource;

        /// <summary>Player fuel in liters (FuelLevel).</summary>
        public float FuelLevel { get; private set; }

        /// <summary>Player brake bias in % front (dcBrakeBias); NaN when the car has no adjustable bias.</summary>
        public float BrakeBias { get; private set; } = float.NaN;

        /// <summary>Track surface temperature in degrees C (TrackTempCrew); NaN until connected.</summary>
        public float TrackTempC { get; private set; } = float.NaN;

        /// <summary>Air temperature in degrees C (AirTemp); NaN until connected.</summary>
        public float AirTempC { get; private set; } = float.NaN;

        /// <summary>Clutch pedal pressed, 0..1 (1 - Clutch; the SDK reports engagement).</summary>
        public float ClutchPressed { get; private set; }

        /// <summary>Replay position, in frames from the start of the buffer (ReplayFrameNum).</summary>
        public int ReplayFrameNum { get; private set; }

        /// <summary>Last frame in the replay buffer — "now" (ReplayFrameNumEnd).</summary>
        public int ReplayFrameNumEnd { get; private set; }

        /// <summary>True only while a replay is actually rolling (IsReplayPlaying).</summary>
        public bool IsReplayPlaying => isReplay;

        // How close to the end of the buffer still counts as live: scrubbing
        // back further than this is what tells a paused replay apart from
        // sitting in the car (IsReplayPlaying is false for both).
        private const int LiveEdgeFrames = 30;

        /// <summary>
        /// True while the sim is showing the replay rather than the live edge
        /// — playing, or paused somewhere behind the end of the buffer.
        /// </summary>
        public bool InReplay =>
            isReplay || (ReplayFrameNumEnd > 0 && ReplayFrameNum < ReplayFrameNumEnd - LiveEdgeFrames);

        /// <summary>
        /// Jump the replay to an absolute frame (counted from the start of the
        /// buffer). False when there is no connection to act on.
        /// </summary>
        public bool SeekReplayToFrame(int frame)
        {
            if (_sdk == null || !_isConnected) return false;
            try
            {
                _sdk.ReplaySetPlayPosition(IRacingSdkEnum.RpyPosMode.Begin, Math.Max(0, frame));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Point the replay camera at a car by its car number, keeping the
        /// camera group and camera the user is on (0 = "leave as is").
        /// </summary>
        public bool FocusCameraOnCar(string carNumber)
        {
            if (_sdk == null || !_isConnected) return false;
            if (!int.TryParse(carNumber, out int number)) return false;
            try
            {
                _sdk.CamSwitchNum(IRacingSdkEnum.CamSwitchMode.FocusAtDriver, number, 0, 0);
                return true;
            }
            catch { return false; }
        }

        public bool IsConnected => _isConnected;
        public bool Enabled { get; set; } = true;

        /// <summary>Live data for the player's car, refreshed every telemetry tick.</summary>
        public Data.Car PlayerCar { get; } = new Data.Car();

        /// <summary>Raised after <see cref="PlayerCar"/> has been refreshed with a new telemetry frame.</summary>
        public event Action<Data.Car>? PlayerCarUpdated;

        private readonly Dictionary<int, Data.Car> _carsByIdx = new();
        private List<Data.Car> _cars = new();

        /// <summary>
        /// Every car in the current session (pace car and spectators excluded),
        /// refreshed each telemetry tick. The player's car is in here too, as
        /// its own instance separate from <see cref="PlayerCar"/>. The list
        /// object is replaced (not mutated) when cars join or leave, so a
        /// grabbed reference is safe to enumerate.
        /// </summary>
        public IReadOnlyList<Data.Car> Cars => _cars;

        /// <summary>Raised after all cars in <see cref="Cars"/> have been refreshed.</summary>
        public event Action<IReadOnlyList<Data.Car>>? CarsUpdated;

        // Events � same contract as WinForms IracingCommunicator
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
            // Forget the last car so reconnecting in the same car re-fires CarNameChanged
            // (the view model drops back to the "NA" profile on disconnect).
            _oldCarName = string.Empty;
            SessionType = string.Empty;
            TrackLengthMeters = 0f;
            _trackLengthRaw = string.Empty;
            SessionTime = 0;
            SectorStartPcts = null;
            SessionState = 0;
            StandingStart = false;
            GridPositions = new Dictionary<int, (int, int)>();
            _gridSource = null;
            FuelLevel = 0f;
            BrakeBias = float.NaN;
            TrackTempC = float.NaN;
            AirTempC = float.NaN;
            ClutchPressed = 0f;
            ReplayFrameNum = 0;
            ReplayFrameNumEnd = 0;
            PlayerCar.Reset();
            _carsByIdx.Clear();
            _cars = new List<Data.Car>();
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
                try { _datumSessionNum = _sdk.Data.TelemetryDataProperties["SessionNum"]; } catch { _datumSessionNum = null; }
                try { _datumSessionTime = _sdk.Data.TelemetryDataProperties["SessionTime"]; } catch { _datumSessionTime = null; }
                try { _datumCarLeftRight = _sdk.Data.TelemetryDataProperties["CarLeftRight"]; } catch { _datumCarLeftRight = null; }
                try { _datumSessionState = _sdk.Data.TelemetryDataProperties["SessionState"]; } catch { _datumSessionState = null; }
                try { _datumFuelLevel = _sdk.Data.TelemetryDataProperties["FuelLevel"]; } catch { _datumFuelLevel = null; }
                try { _datumBrakeBias = _sdk.Data.TelemetryDataProperties["dcBrakeBias"]; } catch { _datumBrakeBias = null; }
                try { _datumTrackTemp = _sdk.Data.TelemetryDataProperties["TrackTempCrew"]; } catch { _datumTrackTemp = null; }
                try { _datumAirTemp = _sdk.Data.TelemetryDataProperties["AirTemp"]; } catch { _datumAirTemp = null; }
                try { _datumClutch = _sdk.Data.TelemetryDataProperties["Clutch"]; } catch { _datumClutch = null; }
                try { _datumReplayFrame = _sdk.Data.TelemetryDataProperties["ReplayFrameNum"]; } catch { _datumReplayFrame = null; }
                try { _datumReplayFrameEnd = _sdk.Data.TelemetryDataProperties["ReplayFrameNumEnd"]; } catch { _datumReplayFrameEnd = null; }

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

            // Session clock and sector boundaries first — sector timing hooked
            // on PlayerCarUpdated/CarsUpdated must see the fresh values.
            try
            {
                if (_datumSessionTime != null)
                    SessionTime = _sdk!.Data.GetDouble(_datumSessionTime);

                CarsAlongside = _datumCarLeftRight != null
                    ? (CarLeftRight)_sdk!.Data.GetInt(_datumCarLeftRight)
                    : CarLeftRight.Off;

                if (_datumSessionState != null) SessionState = _sdk!.Data.GetInt(_datumSessionState);
                if (_datumReplayFrame != null) ReplayFrameNum = _sdk!.Data.GetInt(_datumReplayFrame);
                if (_datumReplayFrameEnd != null) ReplayFrameNumEnd = _sdk!.Data.GetInt(_datumReplayFrameEnd);
                if (_datumFuelLevel != null) FuelLevel = _sdk!.Data.GetFloat(_datumFuelLevel);
                BrakeBias = _datumBrakeBias != null ? _sdk!.Data.GetFloat(_datumBrakeBias) : float.NaN;
                if (_datumTrackTemp != null) TrackTempC = _sdk!.Data.GetFloat(_datumTrackTemp);
                if (_datumAirTemp != null) AirTempC = _sdk!.Data.GetFloat(_datumAirTemp);
                if (_datumClutch != null) ClutchPressed = Math.Clamp(1f - _sdk!.Data.GetFloat(_datumClutch), 0f, 1f);

                var split = _sdk!.Data.SessionInfo?.SplitTimeInfo?.Sectors;
                if (split != null && split.Count > 0 &&
                    (SectorStartPcts == null || SectorStartPcts.Count != split.Count))
                {
                    var starts = new List<float>(split.Count);
                    foreach (var sec in split)
                        starts.Add(sec.SectorStartPct);
                    SectorStartPcts = starts;
                }
            }
            catch { }

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

            // Refresh the player's car snapshot (runs in replay too — the
            // CarIdx arrays and session info stay valid there).
            try
            {
                int playerIdx = _sdk!.Data.SessionInfo?.DriverInfo?.DriverCarIdx ?? -1;
                if (playerIdx >= 0)
                {
                    PlayerCar.Update(_sdk, playerIdx);
                    PlayerCarUpdated?.Invoke(PlayerCar);
                }
            }
            catch { }

            UpdateSessionType();
            UpdateCars();

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
                    
                    int gear = _sdk.Data.GetInt(_datumGear);
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

        /// <summary>
        /// Resolve the running session's type ("Practice", "Race", ...) by
        /// matching the SessionNum telemetry var against the session info YAML.
        /// </summary>
        private void UpdateSessionType()
        {
            try
            {
                var sessions = _sdk?.Data.SessionInfo?.SessionInfo?.Sessions;
                if (sessions == null || _datumSessionNum == null) return;

                // Track length, reparsed only when the YAML string changes.
                var lenStr = _sdk?.Data.SessionInfo?.WeekendInfo?.TrackLength ?? string.Empty;
                if (lenStr != _trackLengthRaw)
                {
                    _trackLengthRaw = lenStr;
                    var numPart = lenStr.Split(' ')[0];
                    if (float.TryParse(numPart, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float km))
                        TrackLengthMeters = km * 1000f;
                }

                StandingStart = (_sdk?.Data.SessionInfo?.WeekendInfo?.WeekendOptions?.StandingStart ?? 0) != 0;

                int num = _sdk!.Data.GetInt(_datumSessionNum);
                foreach (var s in sessions)
                {
                    if (s.SessionNum != num) continue;
                    var type = s.SessionType ?? string.Empty;
                    if (type != SessionType)
                    {
                        SessionType = type;
                        SessionTypeChanged?.Invoke(type);
                    }
                    UpdateGridPositions(s);
                    break;
                }
            }
            catch { }
        }

        /// <summary>
        /// Rebuild <see cref="GridPositions"/> when the session info's grid
        /// list object changes (the YAML is re-parsed into new objects on
        /// every session info update, so a reference check is enough).
        /// Positions in the YAML are 0-based; they are stored 1-based.
        /// </summary>
        private void UpdateGridPositions(IRacingSdkSessionInfo.SessionInfoModel.SessionModel session)
        {
            var grid = new Dictionary<int, (int, int)>();
            if (session.QualifyPositions is { Count: > 0 } qp)
            {
                if (ReferenceEquals(qp, _gridSource)) return;
                _gridSource = qp;
                foreach (var q in qp)
                    grid[q.CarIdx] = (q.Position + 1, q.ClassPosition + 1);
            }
            else if (_sdk?.Data.SessionInfo?.QualifyResultsInfo?.Results is { Count: > 0 } qr)
            {
                if (ReferenceEquals(qr, _gridSource)) return;
                _gridSource = qr;
                foreach (var q in qr)
                    grid[q.CarIdx] = (q.Position + 1, q.ClassPosition + 1);
            }
            else
            {
                if (_gridSource == null) return;
                _gridSource = null;
            }
            GridPositions = grid;
        }

        /// <summary>
        /// Keep one <see cref="Data.Car"/> per driver in the session in sync
        /// with the SDK: create cars as drivers appear, drop them when they
        /// leave, and refresh every remaining one each tick.
        /// </summary>
        private void UpdateCars()
        {
            try
            {
                var driverInfo = _sdk?.Data.SessionInfo?.DriverInfo;
                if (driverInfo == null) return;

                bool membershipChanged = false;
                var present = new HashSet<int>();

                foreach (var d in driverInfo.Drivers)
                {
                    if (d.CarIsPaceCar != 0 || d.IsSpectator != 0) continue;
                    present.Add(d.CarIdx);
                    if (!_carsByIdx.TryGetValue(d.CarIdx, out var car))
                    {
                        car = new Data.Car();
                        _carsByIdx[d.CarIdx] = car;
                        membershipChanged = true;
                    }
                    car.Update(_sdk!, d.CarIdx);
                }

                foreach (var idx in _carsByIdx.Keys)
                {
                    if (!present.Contains(idx)) { membershipChanged = true; break; }
                }

                if (membershipChanged)
                {
                    var fresh = new List<Data.Car>();
                    foreach (var idx in present)
                        fresh.Add(_carsByIdx[idx]);
                    foreach (var idx in _carsByIdx.Keys.Where(k => !present.Contains(k)).ToList())
                        _carsByIdx.Remove(idx);
                    _cars = fresh;
                }

                CarsUpdated?.Invoke(_cars);
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
