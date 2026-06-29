using BeltAPI;
using IRSDKSharper;
using System;

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
        private IRacingSdkDatum? _datumAbs;
        private IRacingSdkDatum? _datumReplay;
        private IRacingSdkDatum? _datumLong;
        private IRacingSdkDatum? _datumLat;
        private IRacingSdkDatum? _datumVert;
        private IRacingSdkDatum? _datumPitch;
        private IRacingSdkDatum? _datumRoll;
        private IRacingSdkDatum? _datumYaw;
        private IRacingSdkDatum? _datumRumbleLeft;
        private IRacingSdkDatum? _datumRumbleRight;

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
        // Rumble strip detection event. Fires with side: "Left", "Right", "Both", or "None"
        public event Action<RumbleSide>? RumbleStripDetected;

        private IracingService()
        {
            
                Task.Run(async () =>
                {
                    await Task.Delay(1000); // Small delay to allow app to initialize before connecting to iRacing
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
                        // If we fail to connect, we'll just stay disconnected and try again next time telemetry data is requested.
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
                _datumAbs   = _sdk.Data.TelemetryDataProperties["BrakeABSactive"];
                _datumReplay = _sdk.Data.TelemetryDataProperties["IsReplayPlaying"];
                _datumLong  = _sdk.Data.TelemetryDataProperties["LongAccel"];
                _datumLat   = _sdk.Data.TelemetryDataProperties["LatAccel"];
                _datumVert  = _sdk.Data.TelemetryDataProperties["VertAccel"];
                _datumPitch = _sdk.Data.TelemetryDataProperties["Pitch"];
                _datumRoll  = _sdk.Data.TelemetryDataProperties["Roll"];
                _datumYaw   = _sdk.Data.TelemetryDataProperties["Yaw"];
                // Try to bind rumble strip datums if available in this SDK build
                try { _datumRumbleLeft = _sdk.Data.TelemetryDataProperties["RumbleStripLeft"]; } catch { _datumRumbleLeft = null; }
                try { _datumRumbleRight = _sdk.Data.TelemetryDataProperties["RumbleStripRight"]; } catch { _datumRumbleRight = null; }
                _datumSpeed = _sdk.Data.TelemetryDataProperties["Speed"];
                _dataInitialized = true;
                return true;
            }
            catch { return false; }
        }
        bool isReplay = false;
        bool _wasReplay = false;
        // Rumble detection state
        private bool _rumbleLeftPrev = false;
        private bool _rumbleRightPrev = false;
        // Detection thresholds (tuneable)
        private const float RUMBLE_LAT_THRESHOLD = 0.6f; // g
        private const float RUMBLE_HEAVE_THRESHOLD = 0.8f; // g
        private const float RUMBLE_MIN_SPEED = 5.0f; // m/s

        public event Action<bool> OnDriverInCarChange;
        private void OnTelemetryData()
        {
            if (!Enabled) return;
            if (!SetupDatums()) return;

            // Car name
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
                return;
            }

            bool abs = _sdk.Data.GetBool(_datumAbs);
            float surge  = -(_sdk.Data.GetFloat(_datumLong) / 9.81f);
            float sway   = _sdk.Data.GetFloat(_datumLat)  / 9.81f;
            float heave  = _sdk.Data.GetFloat(_datumVert) / 9.81f;
            float pitch  = _sdk.Data.GetFloat(_datumPitch);
            float roll   = _sdk.Data.GetFloat(_datumRoll);
            float yaw    = _sdk.Data.GetFloat(_datumYaw);
            speed = _sdk.Data.GetFloat(_datumSpeed);
            TelemetryUpdated?.Invoke(surge, sway, heave, new Rotation(pitch, roll, yaw));
            GForceUpdated?.Invoke(-Math.Clamp(surge, -1000, 0));
            if (abs) AbsTriggered?.Invoke();

            // Rumble strip detection
            try
            {
                bool rumbleLeft = false;
                bool rumbleRight = false;

                // If SDK exposes rumble strip flags, prefer those
                if (_datumRumbleLeft != null)
                {
                    try { rumbleLeft = _sdk.Data.GetBool(_datumRumbleLeft); } catch { rumbleLeft = false; }
                }
                if (_datumRumbleRight != null)
                {
                    try { rumbleRight = _sdk.Data.GetBool(_datumRumbleRight); } catch { rumbleRight = false; }
                }

                // If SDK does not provide rumble flags, use a simple heuristic based on lateral+vertical acceleration
                if (_datumRumbleLeft == null && _datumRumbleRight == null)
                {
                    if (speed >= RUMBLE_MIN_SPEED && Math.Abs(sway) >= RUMBLE_LAT_THRESHOLD && Math.Abs(heave) >= RUMBLE_HEAVE_THRESHOLD)
                    {
                        if (sway < 0)
                            rumbleLeft = true;
                        else if (sway > 0)
                            rumbleRight = true;
                    }
                }

                // Fire event when state changes
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

        private float speed;
        public float Speed => speed;
        public bool isInCar => !isReplay;
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
}
