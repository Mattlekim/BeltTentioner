using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace belttentiontest
{
    public class FlashNanoForm : Form
    {
        private ComboBox comboPorts;
        private Button btnRefresh;
        private Button btnFlash;
        private TextBox txtOutput;

        // Path to hex file (relative to application base)
        private readonly string hexRelativePath = Path.Combine("..", "BeltTensionTest.WPF", "Hex", "belt-tentioner.ino.hex");

        public FlashNanoForm()
        {
            Text = "Flash Nano";
            Size = new Size(560, 360);
            FormBorderStyle = FormBorderStyle.SizableToolWindow;

            var lbl = new Label() { Text = "COM Port:", Location = new Point(12, 14), AutoSize = true };
            comboPorts = new ComboBox() { Location = new Point(80, 10), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefresh = new Button() { Text = "Refresh", Location = new Point(290, 9), Size = new Size(75, 26) };
            btnFlash = new Button() { Text = "Flash", Location = new Point(375, 9), Size = new Size(75, 26) };

            txtOutput = new TextBox() { Location = new Point(12, 48), Size = new Size(520, 260), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true }; 

            Controls.Add(lbl);
            Controls.Add(comboPorts);
            Controls.Add(btnRefresh);
            Controls.Add(btnFlash);
            Controls.Add(txtOutput);

            btnRefresh.Click += (s, e) => RefreshPorts();
            btnFlash.Click += async (s, e) => await FlashSelectedPortAsync();

            Load += (s, e) => RefreshPorts();
        }

        private void RefreshPorts()
        {
            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames().OrderBy(p => p).ToArray();
                comboPorts.Items.Clear();
                comboPorts.Items.AddRange(ports);
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
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AppendOutput(text)));
                return;
            }
            txtOutput.AppendText(text + Environment.NewLine);
        }

        private string ResolveHexPath()
        {
            // Try the exact relative path from running folder
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidate = Path.GetFullPath(Path.Combine(baseDir, hexRelativePath));
                if (File.Exists(candidate))
                    return candidate;

                // also try sibling project path (in repo root)
                candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "BeltTensionTest.WPF", "Hex", "belt-tentioner.ino.hex"));
                if (File.Exists(candidate))
                    return candidate;

                // try path inside solution folder (one up)
                candidate = Path.Combine(baseDir, "BeltTensionTest.WPF", "Hex", "belt-tentioner.ino.hex");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
            return string.Empty;
        }

        private async Task FlashSelectedPortAsync()
        {
            if (comboPorts.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a COM port.", "No Port Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var port = comboPorts.SelectedItem.ToString();
            var hexPath = ResolveHexPath();
            if (string.IsNullOrEmpty(hexPath) || !File.Exists(hexPath))
            {
                MessageBox.Show(this, "Hex file not found. Expected: BeltTensionTest.WPF/Hex/belt-tentioner.ino.hex", "Hex Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                AppendOutput("Hex file not found. Looked for: " + hexPath);
                return;
            }

            btnFlash.Enabled = false;
            btnRefresh.Enabled = false;

            try
            {
                AppendOutput($"Starting flash to {port} using hex: {hexPath}");

                string avrdude = FindAvrdudeExecutable();
                if (string.IsNullOrEmpty(avrdude))
                {
                    AppendOutput("avrdude.exe not found in application folder or PATH. Please install avrdude and ensure it's available.");
                    MessageBox.Show(this, "avrdude.exe not found. Please install avrdude and make sure it's on PATH or in the application folder.", "avrdude missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Try common arguments for Arduino Nano with atmega328p. Try 115200 then 57600.
                string[] baudAttempts = new[] { "115200", "57600" };
                bool success = false;
                foreach (var baud in baudAttempts)
                {
                    AppendOutput($"Attempting with baud {baud}...");
                    var args = $"-v -p atmega328p -c arduino -P {port} -b {baud} -D -Uflash:w:\"{hexPath}\":i";
                    var result = await RunProcessAsync(avrdude, args);
                    AppendOutput(result);
                    if (result.IndexOf("bytes of flash verified", StringComparison.OrdinalIgnoreCase) >= 0 || result.IndexOf("avrdude done", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        success = true;
                        break;
                    }
                }

                if (success)
                {
                    AppendOutput("Flash completed successfully.");
                    MessageBox.Show(this, "Flash completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    AppendOutput("Flash failed. See output for details.");
                    MessageBox.Show(this, "Flash failed. See output for details.", "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AppendOutput("Exception: " + ex.ToString());
            }
            finally
            {
                btnFlash.Enabled = true;
                btnRefresh.Enabled = true;
            }
        }

        private string FindAvrdudeExecutable()
        {
            // check app folder
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var local = Path.Combine(baseDir, "avrdude.exe");
                if (File.Exists(local)) return local;

                // check PATH
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
                string output = string.Empty;
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) output += e.Data + Environment.NewLine; };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output += e.Data + Environment.NewLine; };
                proc.Exited += (s, e) =>
                {
                    try { output += $"\nProcess exited with code {proc.ExitCode}" + Environment.NewLine; } catch { }
                    tcs.TrySetResult(output);
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
