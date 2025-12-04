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
        public event Action<float>? ScaledValueUpdated;

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
        public float MotorStrenth = 6;
        public void OnClientTelemetryData()
        {
            if (_iracingClient == null) return;
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

                bool isReplay = _iracingClient.Data.GetBool("IsReplayPlaying");
                if (isReplay)
                {
                    ScaledValueUpdated?.Invoke(0);
                    GForceUpdated?.Invoke(0);
                    return;
                }
                float lat = _iracingClient.Data.GetFloat("LongAccel");
                float g_Force = lat / 9.81f;
                // Notify subscribers with the new g_Force value

                // GForceUpdated?.Invoke(lat * BeltStrength);
                if (lat < 0)
                {
                   
                    // Notify subscribers with the new scaledValue (as int)
                    ScaledValueUpdated?.Invoke(-g_Force);
                    GForceUpdated?.Invoke(-g_Force);
                }
                else
                {
                    ScaledValueUpdated?.Invoke(0.0f);
                    GForceUpdated?.Invoke(0.0f);
                }
            }
            // Telemetry data received - placeholder for future processing
        }

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
