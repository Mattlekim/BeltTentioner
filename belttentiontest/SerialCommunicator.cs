using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace belttentiontest
{
    public class SerialCommunicator : IDisposable
    {
        private SerialPort? serialPort;
        private readonly HashSet<string> triedPorts = new();

        private CancellationTokenSource? sendLoopCts;
        private Task? sendLoopTask;

        public event Action<string>? MessageReceived;
        public event Action? HandshakeComplete;

        public bool IsConnected => serialPort != null && serialPort.IsOpen;
        public string? PortName => serialPort?.PortName;

        private bool _getSettings = false;
        public void Dispose()
        {
            try { StopSendLoopAsync().GetAwaiter().GetResult(); } catch { }
            try { ClosePort(); } catch { }
        }

        private void ClosePort()
        {
            try
            {
                if (serialPort != null)
                {
                    try { serialPort.DataReceived -= SerialPort_DataReceived; } catch { }
                    try { serialPort.Close(); } catch { }
                    try { serialPort.Dispose(); } catch { }
                    serialPort = null;
                }
            }
            catch { }
        }

        private void SerialPort_DataReceived(object? sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                var sp = sender as SerialPort ?? serialPort;
                if (sp == null) return;

                // Delegate to the shared reader
                ReadAllSerialData(sp);
            }
            catch { }
        }

        private void SerialPort_ErrorReceived(object? sender, SerialErrorReceivedEventArgs e)
        {
            // Device unplugged or hardware error
            MessageReceived?.Invoke("DEVICE_UNPLUGGED");
            Disconnect();
        }

        // Read any available data and raise MessageReceived and HandshakeComplete when appropriate
        private void ReadAllSerialData(SerialPort sp)
        {
            if (_getSettings)
            {
                Thread.Sleep(50); //wait for setttings to be sent
                _getSettings = false;
            }
            try
            {
                string data;
                try { data = sp.ReadExisting(); } catch (Exception ex)
                {
                    // Device may have been unplugged or port closed
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                    return;
                }
                if (string.IsNullOrEmpty(data)) return;

                var lines = data.Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    MessageReceived?.Invoke(line);
                    if (string.Equals(line, "READY", StringComparison.OrdinalIgnoreCase))
                    {
                        HandshakeComplete?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                // Device may have been unplugged or port closed
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

        // Send the handshake (HELLO) and wait up to waitMs for READY. Returns true on success.
        private async Task<bool> SendHandshakeAsync(SerialPort sp, CancellationToken ct, int preWriteDelayMs = 2000, int waitMs = 3000)
        {
            try
            {
                try { sp.DiscardInBuffer(); } catch { }

                if (preWriteDelayMs > 0)
                {
                    try { await Task.Delay(preWriteDelayMs, ct).ConfigureAwait(false); } catch (OperationCanceledException) { }
                }

                try { sp.Write("HELLO\n"); } catch { }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!ct.IsCancellationRequested && sw.ElapsedMilliseconds < waitMs)
                {
                    try
                    {
                        var available = sp.BytesToRead;
                        if (available > 0)
                        {
                            var s = sp.ReadExisting();
                            if (!string.IsNullOrEmpty(s) && s.IndexOf("READY", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch { }

                    try { await Task.Delay(100, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
            }
            catch { }

            return false;
        }

        // Send a target value over the given port
        private async Task SendTargetAsync(SerialPort sp, int target, CancellationToken ct)
        {
            try
            {
                if (sp == null || !sp.IsOpen) return;
                var line = $"T:{target}{sp.NewLine}";
                var bytes = Encoding.ASCII.GetBytes(line);
                try
                {
                    await sp.BaseStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch
                {
                    // fallback to synchronous write if BaseStream fails
                    try { sp.Write(line); } catch { }
                }
            }
            catch { }
        }

        // Keep a simple auto-connect entry point (optional caller can use it)
        public async Task StartAutoConnectAsync(CancellationToken ct)
        {
            try
            {
                // passive detect first
                if (await InitialDetectAsync(ct).ConfigureAwait(false)) return;

                await AutoConnectLoopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        public async Task<bool> ManualScanAsync(CancellationToken ct)
        {
            triedPorts.Clear();
            string[] ports = SerialPort.GetPortNames();
            foreach (var p in ports)
            {
                if (ct.IsCancellationRequested) break;
                var port = await TryOpenAndHandshakeSimpleAsync(p, ct).ConfigureAwait(false);
                if (port != null)
                {
                    serialPort = port;
                    serialPort.DataReceived += SerialPort_DataReceived;
                    return true;
                }
            }
            return false;
        }

        private async Task<bool> InitialDetectAsync(CancellationToken ct)
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (var p in ports)
            {
                if (ct.IsCancellationRequested) break;
                var port = await TryOpenAndHandshakeSimpleAsync(p, ct, preWriteDelayMs: 0, waitMs: 1500).ConfigureAwait(false);
                if (port != null)
                {
                    serialPort = port;
                    serialPort.DataReceived += SerialPort_DataReceived;
                    HandshakeComplete?.Invoke();
                    return true;
                }
            }
            return false;
        }

        private async Task AutoConnectLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && serialPort == null)
                {
                    string[] ports = SerialPort.GetPortNames();
                    foreach (var p in ports)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (triedPorts.Contains(p)) continue;

                        var port = await TryOpenAndHandshakeSimpleAsync(p, ct).ConfigureAwait(false);
                        if (port != null)
                        {
                            serialPort = port;
                            serialPort.DataReceived += SerialPort_DataReceived;
                            HandshakeComplete?.Invoke();
                            return;
                        }

                        triedPorts.Add(p);
                        await Task.Delay(250, ct).ConfigureAwait(false);
                    }

                    await Task.Delay(2000, ct).ConfigureAwait(false);
                    triedPorts.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        // Simple handshake: open, optional short delay, send HELLO, poll ReadExisting until READY or timeout
        private async Task<SerialPort?> TryOpenAndHandshakeSimpleAsync(string portName, CancellationToken ct, int preWriteDelayMs = 3000, int waitMs = 3000)
        {
            SerialPort trial = new SerialPort(portName, 9600)
            {
                NewLine = "\n",
                ReadTimeout = 200,
                WriteTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            try
            {
                trial.Open();
                trial.ErrorReceived += SerialPort_ErrorReceived;
            }
            catch
            {
                try { trial.Dispose(); } catch { }
                return null;
            }
          //  await Task.Delay(preWriteDelayMs, ct);

            try
            {
                var ok = await SendHandshakeAsync(trial, ct, preWriteDelayMs, waitMs).ConfigureAwait(false);
                if (ok)
                {
                    return trial;
                }

                try { trial.Close(); trial.Dispose(); } catch { }
                return null;
            }
            catch
            {
                try { trial.Close(); trial.Dispose(); } catch { }
                return null;
            }
        }

        public void StartSendLoop(Func<int> getTarget)
        {
            if (sendLoopCts != null && !sendLoopCts.IsCancellationRequested) return;
            sendLoopCts = new CancellationTokenSource();
            var token = sendLoopCts.Token;
            sendLoopTask = Task.Run(async () =>
            {
                var period = TimeSpan.FromSeconds(1.0 / 60.0);
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        
                        int target = 0;
                        try { target = getTarget(); } catch { target = 0; }
                        if (target == 0)
                            Thread.Sleep(period);
                            continue;
                        try
                        {
                            var sp = serialPort;
                            if (sp != null && sp.IsOpen)
                            {
                                await SendTargetAsync(sp, target, token).ConfigureAwait(false);
                            }
                        }
                        catch { }

                        try { await Task.Delay(period, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                    }
                }
                catch { }
            }, token);
        }

        public async Task StopSendLoopAsync()
        {
            try
            {
                if (sendLoopCts != null && !sendLoopCts.IsCancellationRequested)
                {
                    sendLoopCts.Cancel();
                    if (sendLoopTask != null)
                    {
                        await Task.WhenAny(sendLoopTask, Task.Delay(1000)).ConfigureAwait(false);
                    }
                }
            }
            catch { }
            finally
            {
                try { sendLoopCts?.Dispose(); } catch { }
                sendLoopCts = null;
                sendLoopTask = null;
            }
        }

        public void SendRequestSettings()
        {
            _getSettings = true;
            string msg = $"SETTINGS";
            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    sp.WriteLine(msg);
                }
            }
            catch (Exception ex)
            {
                // Device may have been unplugged or port closed
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

        public void SendUpdatedSettings(int lmin, int lmax, int rmin, int rmax, bool linvert, bool rinvert, bool both)
        {
            string msg = $"SN:{lmin}-{lmax}-{rmin}-{rmax}-{(linvert ? 1 : 0)}-{(rinvert ? 1 : 0)}-{(both ? 1 : 0)}";
            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    sp.WriteLine(msg);
                    LogToFile("Sent settings: " + msg);
                }
            }
            catch (Exception ex)
            {
                // Device may have been unplugged or port closed
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

        public void SendValue(float value, bool leftMotor)
        {
            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    value = Math.Clamp(value, 0, (int)Form1.MAXPOSIBLEMOTORVALUE);
                    var line = string.Empty;
                    if (leftMotor)
                        line = $"L:{value}{sp.NewLine}";
                    else
                        line = $"R:{value}{sp.NewLine}";
                    sp.Write(line);
                }
            }
            catch (Exception ex)
            {
                // Device may have been unplugged or port closed
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            try
            {
                StopSendLoopAsync().GetAwaiter().GetResult();
            }
            catch { }
            try { ClosePort(); } catch { }
        }

        /// <summary>
        /// Parses a serial line in the format: analogValue\tTarget\tDistance
        /// Example: "512\t300\t100"
        /// Returns a tuple (analogValue, target, distance) if successful, otherwise null.
        /// </summary>
        public (int analogValue, int target, int distance)? ParseSerialLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var parts = line.Trim().Split('\t');
            if (parts.Length != 3) return null;
            if (int.TryParse(parts[0], out int analogValue) &&
                int.TryParse(parts[1], out int target) &&
                int.TryParse(parts[2], out int distance))
            {
                return (analogValue, target, distance);
            }
            return null;
        }

   

        /// <summary>
        /// Parses a serial line in the format: analogValue\tTarget\tDistance
        /// Example: "512\t300\t100"
        /// Returns a tuple (analogValue, target, distance) if successful, otherwise null.
        /// </summary>
        public (int lmin, int lmax, int rmin, int rmax, bool linvert, bool rinvert, bool both)? ParseSerialLineSettings(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var parts = line.Trim().Split('\t');
            if (parts.Length != 7) return null;

           
            if (int.TryParse(parts[0], out int lmin) &&
                int.TryParse(parts[1], out int lmax) &&
                int.TryParse(parts[2], out int rmin) &&
                int.TryParse(parts[3], out int rmax) &&
                int.TryParse(parts[4], out int li) &&
                int.TryParse(parts[5], out int ri) &&
                int.TryParse(parts[6], out int b))
            {
                return (lmin, lmax, rmin, rmax, li==1, ri == 1, b == 1);
            }
         
            return null;
        }

        // Helper method to log to file
        private static void LogToFile(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serial_log.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }
    }
}
