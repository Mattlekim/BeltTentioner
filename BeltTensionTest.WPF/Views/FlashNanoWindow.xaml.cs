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

namespace BeltTensionTest.WPF.Views
{
    public partial class FlashNanoWindow : Window
    {
        private string? _avrdudePath;

        public FlashNanoWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshPorts();
            btnRefresh.Click += (_, _) => RefreshPorts();
            btnFlash.Click += async (_, _) => await FlashSelectedPortAsync();
            btnLocate.Click += (_, _) => LocateAvrdude();
        }

        private void RefreshPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
                comboPorts.ItemsSource = ports;
                if (ports.Length > 0)
                    comboPorts.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                AppendOutput("Error refreshing ports: " + ex.Message);
            }
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

                string[] baudAttempts = new[] { "115200", "57600" };
                bool success = false;
                string lastResult = string.Empty;
                foreach (var baud in baudAttempts)
                {
                    AppendOutput($"Attempting with baud {baud}...");
                    var args = $"-v -p atmega328p -c arduino -P {port} -b {baud} -D -Uflash:w:\"{hexPath}\":i";
                    AppendOutput($"Command: \"{avrdude}\" {args}");
                    var result = await RunProcessAsync(avrdude, args);
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
                }

                if (success)
                {
                    AppendOutput("Flash completed successfully.");
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

        private Task<string> RunProcessAsync(string exePath, string arguments)
        {
            var tcs = new TaskCompletionSource<string>();
            try
            {
                var psi = new ProcessStartInfo(exePath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = new Process() { StartInfo = psi, EnableRaisingEvents = true };
                var output = new StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                proc.Exited += (s, e) =>
                {
                    try { output.AppendLine($"\nProcess exited with code {proc.ExitCode}"); } catch { }
                    tcs.TrySetResult(output.ToString());
                    proc.Dispose();
                };

                bool started = proc.Start();
                if (!started)
                {
                    tcs.TrySetResult("Failed to start process");
                }
                else
                {
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult("Exception starting process: " + ex.Message + "\n" + ex.StackTrace);
            }

            return tcs.Task;
        }
    }
}
