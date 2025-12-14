using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IRSDKSharper;

namespace belttentiontest
{
    // Simple monitor that reports whether iRacing is running.
    // Uses IRacingSdk events when available.
    public class IracingCommunicator : IDisposable
    {
        private static readonly Lazy<IracingCommunicator> _instance = new(() => new IracingCommunicator());
        public static IracingCommunicator Instance => _instance.Value;

        private bool _isConnected;
        public bool Isconnected => _isConnected;    

        // General change event (bool = connected)
        public event Action<bool>? ConnectionChanged;

        // Explicit events for connect / disconnect
        public event Action? Connected;
        public event Action? Disconnected;

        // Event to notify when g_Force is updated
        public event Action<float>? GForceUpdated;

        // Event to notify when scaledValue is updated
        public event Action<float, float, float, bool>? ScaledValueUpdated;

        public event Action? ABSValueUpdated;

        public bool IsConnected => _isConnected;

        IRacingSdk? _iracingClient;

        public Action<string>? CarNameChanged;
        // Singleton: make constructor private
        private IracingCommunicator()
        {
            _isConnected = false;
            try
            {
                _iracingClient = new IRacingSdk();
                SubscribeToSdkEvents();
                // start the client; run-start in try/catch so failures don't block construction
                try { _iracingClient.Start(); } catch { }
            }
            catch
            {
                // ignore - SDK may not be available or may throw
            }
        }

        private void SubscribeToSdkEvents()
        {
            try
            {
                if (_iracingClient == null) return;
                // subscribe using the SDK's event names
                _iracingClient.OnConnected += OnClientConnected;
                _iracingClient.OnDisconnected += OnClientDisconnected;
                _iracingClient.OnTelemetryData += _iracingClient_OnTelemetryData;
            }
            catch { }
        }

        private void _iracingClient_OnTelemetryData()
        {
            // keep this handler minimal and non-blocking
            OnClientTelemetryData();
        }

        string _oldCarName = string.Empty;
     
        private bool _absActive = false;

        public void OnClientTelemetryData()
        {
            if (_iracingClient != null)
            {

                if (_iracingClient.Data.SessionInfo != null)
                {
                    string carName = _iracingClient.Data.SessionInfo.DriverInfo.Drivers[_iracingClient.Data.SessionInfo.DriverInfo.DriverCarIdx].CarScreenName;

                    if (carName != _oldCarName)
                    {
                        CarNameChanged?.Invoke(carName);
                    }
                    _oldCarName = carName;
                }

                _absActive = false;
                _absActive = _iracingClient.Data.GetBool("BrakeABSactive");

                bool isReplay = _iracingClient.Data.GetBool("IsReplayPlaying");
                if (isReplay)
                {
                    ScaledValueUpdated?.Invoke(0, 0, 0, false);
                    ScaledValueUpdated?.Invoke(0, 0, 0, true);
                    GForceUpdated?.Invoke(0);
                    return;
                }
                float longitude = _iracingClient.Data.GetFloat("LongAccel");
                float g_Force = longitude / 9.81f;

                float lmotor = g_Force, rmotor = g_Force;

                if (lmotor > 0)
                    lmotor = 0;

                if (rmotor > 0)
                    rmotor = 0;

                float lat = _iracingClient.Data.GetFloat("LatAccel");
                float lat_g_Force = lat / 9.81f;

                float ver = _iracingClient.Data.GetFloat("VertAccel");
                float ver_g_Force = ver / 9.81f;
                // Notify subscribers with the new g_Force value

                float lat_lMotor = 0, lat_rMotor = 0;


                if (lat_g_Force > 0) //turning left
                {
                    lat_lMotor = Math.Abs(lat_g_Force);
                    lat_rMotor = 0;
                }
                else //turning right
                {
                    lat_rMotor = Math.Abs(lat_g_Force);
                    lat_lMotor = 0;
                }

            

                ScaledValueUpdated?.Invoke(-lmotor, lat_lMotor, ver_g_Force < 0 ? 0: ver_g_Force, false);
                ScaledValueUpdated?.Invoke(-rmotor, lat_rMotor, ver_g_Force < 0 ? 0 : ver_g_Force, true);

                if (!_absActive)
                    ABSValueUpdated?.Invoke();
                GForceUpdated?.Invoke(-Math.Clamp(g_Force,-1000,0));
                return;
            }

         
            // Telemetry data received - placeholder for future processing
        }
        bool flip;
        public void CalculateABSEffect(ref float lmotor, ref float rmotor)
        {
            if (flip)
            {
                lmotor += ABSStrength;
                rmotor += ABSStrength;
                flip = !flip;
                return;
            }


            // ABS effect: alternate between 0 and ABSStrength at the desired frequency (using ABSFrequency)
            float t = (float)DateTime.Now.TimeOfDay.TotalSeconds;
            float freq = ABSFrequency > 0 ? ABSFrequency : 40.0f; // Default to 40Hz if not set
            float squarel = Math.Sin(t * freq * 2 * Math.PI) > 0 ? 1.0f : 0.0f;
            float squarer = Math.Sin((t + ABSFrequency) * freq * 2 * Math.PI) > 0 ? 1.0f : 0.0f;
            float absPulsel = squarel * ABSStrength;
            float absPulser = squarer * ABSStrength;
            lmotor += absPulsel;
            rmotor += absPulser;
        }

        public float ABSFrequency { get; set; } = 4.0f; // Frequency in Hz, default 40Hz

        private void UnsubscribeFromSdkEvents()
        {
            try
            {
                if (_iracingClient == null) return;

                try { _iracingClient.OnConnected -= OnClientConnected; } catch { }
                try { _iracingClient.OnDisconnected -= OnClientDisconnected; } catch { }
                try { _iracingClient.OnTelemetryData -= _iracingClient_OnTelemetryData; } catch { }
            }
            catch { }
        }

        private void OnClientConnected()
        {
            if (!_isConnected)
            {
                _isConnected = true;
                try { ConnectionChanged?.Invoke(true); } catch { }
                try { Connected?.Invoke(); } catch { }
            }
        }

        private void OnClientDisconnected()
        {
            if (_isConnected)
            {
                _isConnected = false;
                try { ConnectionChanged?.Invoke(false); } catch { }
                try { Disconnected?.Invoke(); } catch { }
            }
        }


        public float ABSStrength = 1f;
        

     
        /// <summary>
        /// Stop monitoring and shutdown the IRacing SDK client without blocking indefinitely.
        /// </summary>
        public void StopMonitoring()
        {
            try
            {
                // Unsubscribe first so we don't get callbacks while shutting down
                UnsubscribeFromSdkEvents();

                if (_iracingClient != null)
                {
                    try
                    {
                        // Attempt to stop the SDK cleanly, but do it asynchronously and with a timeout
                        var task = Task.Run(() =>
                        {
                            try { _iracingClient.Stop(); } catch { }
                        });

                        // Wait briefly for stop to complete to avoid hangs
                        if (!task.Wait(TimeSpan.FromMilliseconds(500)))
                        {
                            // if Stop() is blocked, don't wait longer — let process exit
                        }
                    }
                    catch { }
                    finally
                    {
                        try { (_iracingClient as IDisposable)?.Dispose(); } catch { }
                        _iracingClient = null;
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            // Ensure StopMonitoring is quick and non-blocking
            try { StopMonitoring(); } catch { }
        }
    }
}
