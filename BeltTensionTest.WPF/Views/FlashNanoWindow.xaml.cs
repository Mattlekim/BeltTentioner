using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;

using BeltTensionTest.WPF.ViewModels;

namespace BeltTensionTest.WPF.Views
{
    public partial class FlashNanoWindow : Window
    {
        private string? _avrdudePath;
        private readonly int _fTimeOut = 10000; // milliseconds (10 seconds)
        private System.Windows.Threading.DispatcherTimer? _animTimer;
        private int _animTick = 0;
        private Stopwatch? _flashStopwatch;


        public FlashNanoWindow()
        {
            InitializeComponent();

            MainViewModel.Device.ManualDisconnect();

            //MainViewModel.Device.OnConnencted
            Loaded += (_, _) => RefreshPorts();
            btnRefresh.Click += (_, _) => RefreshPorts();
            btnFlash.Click += async (_, _) => await FlashSelectedPortAsync();
            btnLocate.Click += (_, _) => LocateAvrdude();
            comboPorts.SelectionChanged += ComboPorts_SelectionChanged;
            // initialize animation timer
            _animTimer = new System.Windows.Threading.DispatcherTimer();
            _animTimer.Interval = TimeSpan.FromMilliseconds(300);
            _animTimer.Tick += (_, _) => UpdateAnimation();


          
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
        }

        private void StartBusyAnimation()
        {
            Dispatcher.Invoke(() =>
            {
                progressBusy.Visibility = Visibility.Visible;
                try
                {
                    progressBusy.IsIndeterminate = false;
                    progressBusy.Minimum = 0;
                    progressBusy.Maximum = _fTimeOut;
                    progressBusy.Value = 0;
                }
                catch { }
                _animTick = 0;
                txtStatusAnim.Text = "Working";
                _flashStopwatch = Stopwatch.StartNew();
                _animTimer?.Start();
            });
        }

        private void StopBusyAnimation()
        {
            Dispatcher.Invoke(() =>
            {
                _animTimer?.Stop();
                try { _flashStopwatch?.Stop(); } catch { }
                _flashStopwatch = null;
                progressBusy.Visibility = Visibility.Collapsed;
                txtStatusAnim.Text = string.Empty;
            });
        }

        private void UpdateAnimation()
        {
            _animTick = (_animTick + 1) % 4;
            txtStatusAnim.Text = "Working" + new string('.', _animTick);
            try
            {
                if (_flashStopwatch != null)
                {
                    var elapsed = (double)Math.Min(_flashStopwatch.ElapsedMilliseconds, _fTimeOut);
                    progressBusy.Value = elapsed;
                }
                else
                {
                    // fallback small motion
                    progressBusy.Value = (progressBusy.Value + 15) % progressBusy.Maximum;
                }
            }
            catch { }
        }

        private void KillAvrdudeProcesses()
        {
            try
            {
                // Kill any running avrdude processes left behind
                var procs = Process.GetProcessesByName("avrdude");
                foreach (var p in procs)
                {
                    try
                    {
                        AppendOutput($"Killing leftover avrdude process PID={p.Id}");
                        p.Kill();
                    }
                    catch (Exception ex)
                    {
                        AppendOutput($"Failed to kill avrdude PID={p.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendOutput("Error enumerating avrdude processes: " + ex.Message);
            }
        }

     

        private void RefreshPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
                comboPorts.ItemsSource = ports;
                if (ports.Length > 0)
                {
                    comboPorts.SelectedIndex = 0;
                    Thread.Sleep(100); // give time for selection changed to fire
                    MainViewModel.Device.RequestDeviceVersion();
                }
                else
                    lblPortStatus.Content = "No device detected";
            }
            catch (Exception ex)
            {
                AppendOutput("Error refreshing ports: " + ex.Message);
            }
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

        private async void ComboPorts_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (comboPorts.SelectedItem == null)
            {
                lblPortStatus.Content = "No device detected";
                lblPortStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
                return;
            }

            var port = comboPorts.SelectedItem.ToString();
            lblPortStatus.Content = "Querying...";
            lblPortStatus.Foreground = System.Windows.Media.Brushes.Gold;

            await Task.Run(() =>
            {
                try
                {
                    
                    
                    using (var sp = new SerialPort(port, 9600))
                    {
                        sp.ReadTimeout = 800;
                        sp.WriteTimeout = 300;
                        sp.NewLine = "\n";
                        sp.Open();
                        // give some time for device to reset after opening (if needed)
                        Task.Delay(120).Wait();
                        // clear
                        try { sp.DiscardInBuffer(); } catch { }
                        try { sp.DiscardOutBuffer(); } catch { }
                        // send version request
                        sp.WriteLine("VER");

                        // read lines until timeout
                        var sw = Stopwatch.StartNew();
                        string? versionLine = null;
                        bool alive = true;
                        while (sw.ElapsedMilliseconds < 1500 && alive)
                        {
                            try
                            {
                                while (sp.BytesToRead > 0)
                                {
                                    var line = sp.ReadLine();

                                    if (string.IsNullOrWhiteSpace(line)) continue;

                                    if (line.StartsWith("Wai"))
                                        SendPacket(sp, 0x10, 0x0001); // send wakeup packet
                                    line = line.Trim();
                                    if (line.StartsWith("VER:")) { versionLine = line.Substring(4).Trim();
                                        alive = false;
                                        break; }
                                    // also accept plain version lines
                                    if (line.IndexOf("1.") >= 0 || line.IndexOf("0.") >= 0) { versionLine = line; alive = false; break; }
                                }
                            }
                            catch (TimeoutException) { }
                            catch { break; }
                        }

                        if (!string.IsNullOrEmpty(versionLine))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                lblPortStatus.Content = versionLine;
                                lblPortStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                lblPortStatus.Content = "No response";
                                lblPortStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
                            });
                        }

                        try { sp.Close(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        lblPortStatus.Content = "Error: " + ex.Message;
                        lblPortStatus.Foreground = System.Windows.Media.Brushes.IndianRed;
                    });
                }
            });
        }

        private void AppendOutput(string text)
        {
            Dispatcher.Invoke(() =>
            {
                txtOutput.AppendText(text + Environment.NewLine);
                txtOutput.ScrollToEnd();
            });
        }

        private string ResolveHexPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Common locations
                var candidates = new[]
                {
                    Path.Combine(baseDir, "Hex", "belt-tentioner.ino.hex"),
                    Path.Combine(baseDir, "..", "Hex", "belt-tentioner.ino.hex"),
                    Path.Combine(baseDir, "..", "..", "BeltTensionTest.WPF", "Hex", "belt-tentioner.ino.hex"),
                    Path.Combine(baseDir, "BeltTensionTest.WPF", "Hex", "belt-tentioner.ino.hex")
                };
                foreach (var c in candidates)
                {
                    try { var f = Path.GetFullPath(c); if (File.Exists(f)) return f; } catch { }
                }
            }
            catch { }
            return string.Empty;
        }

        private async Task FlashSelectedPortAsync()
        {
            if (comboPorts.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a COM port.", "No Port Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var port = comboPorts.SelectedItem.ToString();
            var hexPath = ResolveHexPath();
            if (string.IsNullOrEmpty(hexPath) || !File.Exists(hexPath))
            {
                MessageBox.Show(this, "Hex file not found. Expected: BeltTensionTest.WPF/Hex/belt-tentioner.ino.hex", "Hex Missing", MessageBoxButton.OK, MessageBoxImage.Error);
                AppendOutput("Hex file not found. Looked for: " + hexPath);
                return;
            }

            btnFlash.IsEnabled = false;
            btnRefresh.IsEnabled = false;
            StartBusyAnimation();

            try
            {
                AppendOutput($"Starting flash to {port} using hex: {hexPath}");

                string avrdude = FindAvrdudeExecutable();
                if (string.IsNullOrEmpty(avrdude))
                {
                    AppendOutput("avrdude.exe not found in application folder, common Arduino install locations, or PATH. Please install avrdude and ensure it's available.");
                    // show which paths we checked
                    AppendOutput("Checked locations:");
                    foreach (var p in GetAvrdudeSearchPaths())
                    {
                        AppendOutput("  " + p);
                    }
                    MessageBox.Show(this, "avrdude.exe not found. Please install avrdude and make sure it's on PATH or in the application folder.", "avrdude missing", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AppendOutput("Using avrdude: " + avrdude);

                // choose baud attempts based on bootloader selection
                string sel = "Auto";
                try { sel = (comboBootloader.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Auto"; } catch { }
                string[] baudAttempts;
                if (sel.IndexOf("Old", StringComparison.OrdinalIgnoreCase) >= 0)
                    baudAttempts = new[] { "57600" };
                else if (sel.IndexOf("New", StringComparison.OrdinalIgnoreCase) >= 0)
                    baudAttempts = new[] { "115200" };
                else
                    baudAttempts = new[] { "115200", "57600" };
                bool success = false;
                string lastResult = string.Empty;
                foreach (var baud in baudAttempts)
                {
                    if ((_flashStopwatch?.ElapsedMilliseconds ?? 0) > _fTimeOut)
                    {
                        AppendOutput($"Flashing timed out after {_fTimeOut}ms (aborting)");
                        try { KillAvrdudeProcesses(); } catch { }
                        break;
                    }
                    AppendOutput($"Attempting with baud {baud}...");
                    var args = $"-v -p atmega328p -c arduino -P {port} -b {baud} -D -Uflash:w:\"{hexPath}\":i";
                    AppendOutput($"Command: \"{avrdude}\" {args}");
                    var result = await RunProcessAsync(avrdude, args, _fTimeOut);
                    if (string.IsNullOrWhiteSpace(result))
                        AppendOutput("(no output)");
                    else
                        AppendOutput(result);
                    lastResult = result ?? string.Empty;
                    if ((lastResult.IndexOf("bytes of flash verified", StringComparison.OrdinalIgnoreCase) >= 0) || (lastResult.IndexOf("avrdude done", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        success = true;
                        break;
                    }
                    if ((_flashStopwatch?.ElapsedMilliseconds ?? 0) > _fTimeOut)
                    {
                        AppendOutput($"Flashing timed out after {_fTimeOut}ms (aborting)");
                        try { KillAvrdudeProcesses(); } catch { }
                        break;
                    }
                }

                    if (success)
                    {
                        AppendOutput("Flash completed successfully.");
                        try { KillAvrdudeProcesses(); } catch { }
                        MessageBox.Show(this, "Flash completed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                else
                {
                    AppendOutput("Flash failed. See output for details.");
                    if (!string.IsNullOrWhiteSpace(lastResult))
                    {
                        AppendOutput("Last avrdude output:");
                        AppendOutput(lastResult);
                    }
                    MessageBox.Show(this, "Flash failed. See output for details.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AppendOutput("Exception: " + ex.ToString());
            }
            finally
            {
                try { StopBusyAnimation(); } catch { }
                btnFlash.IsEnabled = true;
                btnRefresh.IsEnabled = true;
            }
        }

        private string FindAvrdudeExecutable()
        {
            // If user located avrdude manually, use that
            try
            {
                if (!string.IsNullOrEmpty(_avrdudePath) && File.Exists(_avrdudePath))
                    return _avrdudePath;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(baseDir, "avrdude.exe");
                if (File.Exists(local)) return local;

                // Check common Arduino install locations
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                var arduinoCandidates = new[]
                {
                    Path.Combine(programFilesX86, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    Path.Combine(programFiles, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    Path.Combine(programFiles, "Arduino\\resources\\app", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    Path.Combine(localAppData, "Programs", "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    // Arduino IDE 2.x default install location under LocalAppData
                    Path.Combine(localAppData, "Programs", "Arduino IDE", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    Path.Combine(localAppData, "Programs", "Arduino IDE", "resources", "app", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                    // Arduino IDE 1.x legacy location
                    Path.Combine(programFilesX86, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"),
                };

                foreach (var candidate in arduinoCandidates)
                {
                    try { var f = Path.GetFullPath(candidate); if (File.Exists(f)) return f; } catch { }
                }

                // Fall back to PATH
                string? path = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(path))
                {
                    foreach (var part in path.Split(Path.PathSeparator))
                    {
                        try
                        {
                            var candidate = Path.Combine(part, "avrdude.exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private IEnumerable<string> GetAvrdudeSearchPaths()
        {
            var paths = new List<string>();
            if (!string.IsNullOrEmpty(_avrdudePath)) paths.Add(_avrdudePath);
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            paths.Add(Path.Combine(baseDir, "avrdude.exe"));

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            paths.Add(Path.Combine(programFilesX86, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(programFiles, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(programFiles, "Arduino\\resources\\app", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(localAppData, "Programs", "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(localAppData, "Programs", "Arduino IDE", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(localAppData, "Programs", "Arduino IDE", "resources", "app", "hardware", "tools", "avr", "bin", "avrdude.exe"));
            paths.Add(Path.Combine(programFilesX86, "Arduino", "hardware", "tools", "avr", "bin", "avrdude.exe"));

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var part in pathEnv.Split(Path.PathSeparator))
                {
                    try { paths.Add(Path.Combine(part, "avrdude.exe")); } catch { }
                }
            }

            return paths;
        }

        private void LocateAvrdude()
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Executable files (*.exe)|*.exe";
            dlg.DefaultExt = ".exe";
            dlg.Title = "Locate avrdude.exe";
            if (dlg.ShowDialog(this) == true)
            {
                _avrdudePath = dlg.FileName;
                AppendOutput("Using avrdude: " + _avrdudePath);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }

        private async Task<string> RunProcessAsync(string exePath, string arguments, int timeoutMs = 60000)
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = new Process() { StartInfo = psi };
                var output = new StringBuilder();

                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };

                bool started;
                try
                {
                    started = proc.Start();
                }
                catch (Exception ex)
                {
                    return "Exception starting process: " + ex.Message + "\n" + ex.StackTrace;
                }

                if (!started)
                {
                    return "Failed to start process";
                }

                try { AppendOutput($"Process started: PID={proc.Id}"); } catch { }

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Wait for exit with timeout
                var exited = await Task.Run(() => proc.WaitForExit(timeoutMs));
                if (!exited)
                {
                    try
                    {
                        proc.Kill();
                        AppendOutput("Process killed after timeout");
                    }
                    catch { }
                }

                // allow background readers to flush
                await Task.Delay(200);

                string exitInfo;
                try { exitInfo = proc.HasExited ? $"\nProcess exited with code {proc.ExitCode}" : "\nProcess did not exit cleanly"; } catch { exitInfo = "\nProcess exit code unavailable"; }
                output.AppendLine(exitInfo);
                return output.ToString();
            }
            catch (Exception ex)
            {
                return "Exception running process: " + ex.Message + "\n" + ex.StackTrace;
            }
        }
    }
}
