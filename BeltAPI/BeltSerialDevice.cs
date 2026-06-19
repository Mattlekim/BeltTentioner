using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeltAPI
{
    public class BeltSerialDevice : IDisposable
    {
   
        public int MAXPOSIBLEMOTORANGLE = 180;

        private SerialPort? serialPort;
        private readonly HashSet<string> triedPorts = new();

        private CancellationTokenSource? sendLoopCts;
        private Task? sendLoopTask;
        // lock for queued send values
        private readonly object _sendLock = new object();

        public event Action<string>? MessageReceived;
        public event Action? HandshakeComplete;

        public event Action<string>? DeviceVersionReceived;

        public bool IsConnected => serialPort != null && serialPort.IsOpen;
        public string? PortName => serialPort?.PortName;

        public event Action OnConnencted;
        public event Action OnDisconnection;

        private bool _getSettings = false;

        public List<string> _log = new List<string>();

        public List<string> GetLog => _log;

        private void Log(string message) =>
            _log.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        private MotorSettings _motorSettings;

        /// <summary>
        /// motor settings for both motors
        /// </summary>
        public MotorSettings DeviceMotorSettings => _motorSettings;

        private MotorSettings _rightMotorSettings;

        public Action OnMotorSettingsRecived;

        public bool DuelMotors { get; private set; } = false;

        private void SendPacket(byte key, ushort value)
        {
            var sp = serialPort;
            if (sp == null || !sp.IsOpen)
                return;

            byte[] packet = new byte[3];
            packet[0] = key;
            packet[1] = (byte)(value & 0xFF);   // low byte
            packet[2] = (byte)(value >> 8);     // high byte

            sp.Write(packet, 0, 3);
        }

        private void SendPacket(SerialPort sp, byte key, ushort value)
        {
            if (sp == null || !sp.IsOpen)
                return;

            byte[] packet = new byte[3];
            packet[0] = key;
            packet[1] = (byte)(value & 0xFF);   // low byte
            packet[2] = (byte)(value >> 8);     // high byte

            sp.Write(packet, 0, 3);
        }

        public void Dispose()
        {
           
            try { ClosePort(); } catch { }
        }

        private void ClosePort()
        {
            try
            {
                if (serialPort != null)
                {
                    // stop any background send loop when closing the port
                    try { StopSendLoop(); } catch { }
                    try { serialPort.DataReceived -= SerialPort_DataReceived; } catch { }
                    try { serialPort?.Close(); } catch { }
                    try { serialPort?.Dispose(); } catch { }
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
            Log($"Serial error received: {e.EventType}");
            MessageReceived?.Invoke("DEVICE_UNPLUGGED");
            Disconnect();
        }

        private string GetVersionNumber(string message)
        {
            var parts = message.Split(':');
            if (parts.Length == 2)
            {

                DeviceVersionReceived?.Invoke(parts[1]);
                return (parts[1]);
            }


            return string.Empty;
        }

        public void RequestDeviceVersion(Action<string, string> callback)
        {
            SendPacket(0x10, 0);
        }

        public void SendWindPower(ushort power)
        {
            if  (power > 255)
                            {
                power = 255;
            }
            //power = 200;
            if (power < 255)
            {

            }
            SendPacket(0x03, power);
        }




        private void DecodeSettings(string message)
        {
            Log($"DecodeSettings: {message}");
            var parsedSettings = ParseSerialLineSettings(message.Substring(1));

            if (parsedSettings.HasValue)
            {
                Log($"Settings decoded: lmin={parsedSettings.Value.lmin} lmax={parsedSettings.Value.lmax} rmin={parsedSettings.Value.rmin} rmax={parsedSettings.Value.rmax} linvert={parsedSettings.Value.linvert} rinvert={parsedSettings.Value.rinvert} both={parsedSettings.Value.both}");

                _motorSettings.LeftMinimumAngle = Math.Clamp(parsedSettings.Value.lmin, 0, MAXPOSIBLEMOTORANGLE);
                _motorSettings.LeftMaximumAngle = Math.Clamp(parsedSettings.Value.lmax, 0, MAXPOSIBLEMOTORANGLE);
                _motorSettings.LeftInverted = parsedSettings.Value.linvert;

                _motorSettings.RightMinimumAngle = Math.Clamp(parsedSettings.Value.rmin, 0, MAXPOSIBLEMOTORANGLE);
                _motorSettings.RightMaximumAngle = Math.Clamp(parsedSettings.Value.rmax, 0, MAXPOSIBLEMOTORANGLE);
                _motorSettings.RightInverted = parsedSettings.Value.rinvert;

                DuelMotors = parsedSettings.Value.both;

                if (OnMotorSettingsRecived != null)
                {
                    OnMotorSettingsRecived?.Invoke();
                }
            }
            else
            {
                Log($"DecodeSettings: failed to parse '{message}'");
            }
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
                    Log($"ReadExisting failed: {ex.Message}");
                   
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
                    Log($"RX: {line}");
                    if (line[0] == 'S') //if recive settings
                    {
                        DecodeSettings(line);
                    }

                    if (line.StartsWith("VERSION:", StringComparison.OrdinalIgnoreCase))
                    {
                        string version = GetVersionNumber(line.Substring(8));
                        Log($"Device version: {version}");
                    }

                    MessageReceived?.Invoke(line);
                    if (string.Equals(line, "READY", StringComparison.OrdinalIgnoreCase))
                    {
                        HandshakeComplete?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ReadAllSerialData exception: {ex.Message}");
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

      private async Task<bool> SendHandshakeAsync(SerialPort sp, CancellationToken ct, int preWriteDelayMs = 2000, int waitMs = 3000)
{
    try
    {
        try { sp.DiscardInBuffer(); } catch { }

        if (preWriteDelayMs > 0)
        {
            try { await Task.Delay(preWriteDelayMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        // --- NEW BINARY HANDSHAKE ---
        // key = 0x00
        // lo  = 0x48 ('H')
        // hi  = 0x01 (protocol version)
        try
        {
            SendPacket(sp, 0x00, 0x0148);   // sends [00][48][01]
            Log($"Sent binary HELLO on {sp.PortName}");
        }
        catch { }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!ct.IsCancellationRequested && sw.ElapsedMilliseconds < waitMs)
        {
            try
            {
                var available = sp.BytesToRead;
                if (available > 0)
                {
                    var s = sp.ReadExisting();
                    if (!string.IsNullOrEmpty(s) &&
                        s.IndexOf("READY", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch { }

            try { await Task.Delay(100, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
    catch { }

    return false;
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


        bool _stopScan = false;
        private async Task AutoConnectLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && serialPort == null)
                {
                    if (_stopScan) return;
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
                            isConnected = true;

                            OnConnencted?.Invoke();
                            StartSendLoop();
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
                Log($"Trying port {portName}");
                trial.Open();
                trial.ErrorReceived += SerialPort_ErrorReceived;
                Log($"Opened port {portName}");
            }
            catch (Exception ex)
            {
                Log($"Failed to open port {portName}: {ex.Message}");
                try { trial.Dispose(); } catch { }
                return null;
            }
          //  await Task.Delay(preWriteDelayMs, ct);

            try
            {
                var ok = await SendHandshakeAsync(trial, ct, preWriteDelayMs, waitMs).ConfigureAwait(false);
                if (ok)
                {
                    Log($"Handshake succeeded on {portName}");
                    isConnected = true;
                    OnConnencted?.Invoke();
                    return trial;
                }

                Log($"Handshake timed out on {portName}");
                try { trial.Close(); trial.Dispose(); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                Log($"Exception during handshake on {portName}: {ex.Message}");
                try { trial.Close(); trial.Dispose(); } catch { }
                return null;
            }
        }



        public void SendRequestSettings()
        {
            if (!isConnected)
                return;

            Log("Sending SETTINGS request");
            _getSettings = true;

            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    // SETTINGS request → key 0x11, value ignored
                    SendPacket(0x11, 0);
                }
                else
                {
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                }
            }
            catch
            {
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }


        public void SendUpdatedSettings(int lmin, int lmax, int rmin, int rmax, bool linvert, bool rinvert, bool both)
        {
            if (!isConnected)
                return;

            _motorSettings = new MotorSettings
            {
                LeftMinimumAngle = Math.Clamp(lmin, 0, MAXPOSIBLEMOTORANGLE),
                LeftMaximumAngle = Math.Clamp(lmax, 0, MAXPOSIBLEMOTORANGLE),
                LeftInverted = linvert,
                RightMinimumAngle = Math.Clamp(rmin, 0, MAXPOSIBLEMOTORANGLE),
                RightMaximumAngle = Math.Clamp(rmax, 0, MAXPOSIBLEMOTORANGLE),
                RightInverted = rinvert
            };

            DuelMotors = both;

            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    Log("TX settings (binary)");

                    // Send 7 packets in order
                    SendPacket(0x12, (ushort)lmin);
                    SendPacket(0x12, (ushort)lmax);
                    SendPacket(0x12, (ushort)rmin);
                    SendPacket(0x12, (ushort)rmax);
                    SendPacket(0x12, (ushort)(linvert ? 1 : 0));
                    SendPacket(0x12, (ushort)(rinvert ? 1 : 0));
                    SendPacket(0x12, (ushort)(both ? 1 : 0));

                    LogToFile($"Sent settings (binary): {lmin}-{lmax}-{rmin}-{rmax}-{(linvert ? 1 : 0)}-{(rinvert ? 1 : 0)}-{(both ? 1 : 0)}");
                }
                else
                {
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                }
            }
            catch
            {
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }


        public void SendABS(float value)
        {
            if (!isConnected)
                return;

            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    value = Math.Clamp(value, 0, 255);
                    ushort v = (ushort)value;

                    Log($"TX: ABS({value})");
                    SendPacket(0x04, v);   // 0x04 = ABS key
                }
                else
                {
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                }
            }
            catch
            {
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }


        public void SendSlowMode()
        {
            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    var line = $"S:0{sp.NewLine}";
                    Log("TX: S:0 (slow mode)");
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

        public void SendCustomData(string data)
        {

            if (!isConnected)
                return;
            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    sp.Write(data + sp.NewLine);
                }
                else
                {
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                }
            }
            catch (Exception ex)
            {
                // Device may have been unplugged or port closed
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

        private float lValueToSend, rValueToSend;
        private bool _haveValueToSend = false;

        /// <summary>
        /// Immediate send API (keeps last queued values and attempts to write now).
        /// </summary>
        public void SendValue(float lvalue, float rvalue)
        {
            // record last values
          //  lock (_sendLock)
            {
                lValueToSend = lvalue;
                rValueToSend = rvalue;
                _haveValueToSend = true;
            }

      //      // perform immediate write
        //    WriteValues(lvalue, rvalue);
        }

        // Internal write routine that performs the actual serial writes (no flag changes)
        private void WriteValues(float lvalue, float rvalue)
        {
            if (!isConnected)
                return;
            

            try
            {
                var sp = serialPort;
                if (sp != null && sp.IsOpen)
                {
                    lvalue = Math.Clamp(lvalue, 0, MAXPOSIBLEMOTORANGLE);
                    rvalue = Math.Clamp(rvalue, 0, MAXPOSIBLEMOTORANGLE);

                    ushort lv = (ushort)lvalue;
                    ushort rv = (ushort)rvalue;

                    // LEFT packet
                    byte[] leftPacket = new byte[3];
                    leftPacket[0] = 0x01;               // key
                    leftPacket[1] = (byte)(lv & 0xFF);  // low byte
                    leftPacket[2] = (byte)(lv >> 8);    // high byte
                    sp.Write(leftPacket, 0, 3);

                    // RIGHT packet
                    byte[] rightPacket = new byte[3];
                    rightPacket[0] = 0x02;               // key
                    rightPacket[1] = (byte)(rv & 0xFF);  // low byte
                    rightPacket[2] = (byte)(rv >> 8);    // high byte
                    sp.Write(rightPacket, 0, 3);
                }
                else
                {
                    MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                    Disconnect();
                }
            }
            catch
            {
                MessageReceived?.Invoke("DEVICE_UNPLUGGED");
                Disconnect();
            }
        }

  
        private void StartSendLoop()
        {
                if (sendLoopCts != null) return;
                sendLoopCts = new CancellationTokenSource();
                var ct = sendLoopCts.Token;
                sendLoopTask = Task.Run(async () =>
                {
                    const int intervalMs = 1000 / 60; // ~60 Hz
                    
                while (!ct.IsCancellationRequested)
                        {
                            // If have a queued value, grab and clear it under lock
                            bool have = false;
                            float lv = 0, rv = 0;
                            lock (_sendLock)
                            {
                                if (_haveValueToSend)
                                {
                                    have = true;
                                    lv = lValueToSend;
                                    rv = rValueToSend;
                                    _haveValueToSend = false;
                                }
                            }
                            if (have)
                            {
                                try { WriteValues(lv, rv); } catch { }
                            }

                            try { await Task.Delay(intervalMs, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { break; }
                        }
                    
                 
                }, ct);
            }
         
        

        private void StopSendLoop()
        {
            try
            {
                if (sendLoopCts != null)
                {
                    try { sendLoopCts.Cancel(); } catch { }
                    try { sendLoopTask?.Wait(200); } catch { }
                    try { sendLoopTask = null; } catch { }
                    try { sendLoopCts.Dispose(); } catch { }
                    sendLoopCts = null;
                }
            }
            catch { }
        }


        bool isConnected = false;

        private void AsyncDisconnect()
        {
            Log($"AsyncDisconnect called (port: {PortName ?? "none"})");
            try { ClosePort(); } catch { }
            isConnected = false;
            OnDisconnection?.Invoke();
        }

        public void ManualDisconnect()
        {
            Log($"ManualDisconnect called (port: {PortName ?? "none"})");
            try { ClosePort(); } catch { }
            isConnected = false;
            OnDisconnection?.Invoke();
            _stopScan = true;
        }

        public void Disconnect()
        {
            Log($"Disconnect called (port: {PortName ?? "none"})");
            try { ClosePort(); } catch { }
            isConnected = false;
            OnDisconnection?.Invoke();
            Task.Run(async () =>
            {
             //   Form1.Instance.LabelStatus = "Disconnected. Reconnecting";
                int tries = 0;
                for (int i = 0; i < 5; i++)
                {
                    using var manualCts = new CancellationTokenSource();
                    bool ok = await ConnectAsync(manualCts.Token).ConfigureAwait(false);

                    if (isConnected)
                    {
                    //    Form1.Instance.UpdateConnectionStatusConnected();
                        return;
                    }
                    tries++;
                    await Task.Delay(1500).ConfigureAwait(false);
                }
            });
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

       


        public async Task<bool> ConnectAsync(CancellationToken ct)
        {
            Log($"Atempting To Connect");
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                return false;
            }

            bool ok = await ManualScanAsync(ct).ConfigureAwait(false);
            if (ok)
            {
                Log($"Connected to {PortName}");
                isConnected = true;
                OnConnencted?.Invoke();
                SendRequestSettings();
                StartSendLoop();
            }
            else
            {
                Log("ConnectAsync: no device responded");
            }
            return ok;
        }

        public BeltMotorData SetupMotorsForData(float surge, float sway, float heave, CarSettings settings, Rotation carRotation = default)
        {
            
            return DeviceMotorSettings.Setup(surge, sway, heave, settings, carRotation);
        }
    }
}
