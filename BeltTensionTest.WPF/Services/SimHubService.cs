using BeltTensionTest.WPF.Shared;
using System;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Polls the SimHub telemetry memory-mapped file and raises events with fresh data.
    /// Mirrors the GetSimHubData() logic from Form1.
    /// </summary>
    public class SimHubService : IDisposable
    {
        private TelemetryMmfReader? _reader;
        private bool _disposed;
        private DateTime _lastConnectAttempt = DateTime.MinValue;

        public bool Connected { get; private set; }

        public event Action<BeltTensionTest.WPF.Shared.TelemetrySharedData>? TelemetryReceived;
        public event Action? SimHubConnected;
        public event Action? SimHubDisconnected;

        public BeltTensionTest.WPF.Shared.TelemetrySharedData Poll()
        {
            if (_disposed) return default;

            if (_reader == null)
            {
                TryConnect();
                return default;
            }

            if (!_reader.Connected)
            {
                Disconnect();
                return default;
            }

            var data = _reader.Read();
            TelemetryReceived?.Invoke(data);
            return data;
        }

        private void TryConnect()
        {
            try
            {
                // Throttle connection attempts to once per second to avoid busy-looping when MMF is absent
                var now = DateTime.UtcNow;
                if ((now - _lastConnectAttempt) < TimeSpan.FromSeconds(1)) return;
                _lastConnectAttempt = now;

                _reader = new TelemetryMmfReader();
                if (_reader.Connected)
                {
                    Connected = true;
                    SimHubConnected?.Invoke();
                }
                else
                {
                    _reader.Dispose();
                    _reader = null;
                }
            }
            catch { _reader = null; }
        }

        private void Disconnect()
        {
            Connected = false;
            _reader?.Dispose();
            _reader = null;
            SimHubDisconnected?.Invoke();
        }

        public void Dispose()
        {
            _disposed = true;
            _reader?.Dispose();
        }
    }
}
