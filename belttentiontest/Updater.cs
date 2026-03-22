using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace belttentiontest
{
    internal static class Updater
    {
        private const string RepoOwner = "Mattlekim";
        private const string RepoName = "BeltTentioner";

        public static async Task CheckForUpdatesAsync(Form owner)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BeltTentioner-Updater");

                // Query latest release API to get version (tag)
                var releaseApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

                HttpResponseMessage resp;
                try
                {
                    resp = await http.GetAsync(releaseApi).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    ShowMessage(owner, "Failed to connect to update server. Check your internet connection.", "Update Check");
                    return;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    ShowMessage(owner, $"Update server returned an error: {(int)resp.StatusCode} {resp.ReasonPhrase}", "Update Check");
                    return;
                }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                // Expect tag_name like "v1.2.3" or "1.2.3"
                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                {
                    ShowMessage(owner, "Could not determine remote version (unexpected API response).", "Update Check");
                    return;
                }

                var remoteTag = tagProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(remoteTag))
                {
                    ShowMessage(owner, "Could not determine remote version (empty tag).", "Update Check");
                    return;
                }

                // Parse remote version
                if (!TryParseVersion(remoteTag, out var remoteVersion))
                {
                    ShowMessage(owner, $"Could not parse remote version '{remoteTag}'.", "Update Check");
                    return;
                }

                // Get local version from assembly (prefer informational version)
                Version? localVersion = new Version(AboutBox.Version);
                

                if (localVersion == null)
                {
                    ShowMessage(owner, "Cannot determine current application version. Update aborted.", "Update Check");
                    return;
                }

                // Compare
                var cmp = remoteVersion.CompareTo(localVersion);
                if (cmp <= 0)
                {
                    ShowMessage(owner, "No updates available.", "Update Check");
                    return;
                }

                var ask = AskYesNo(owner, $"A new version is available ({remoteTag}).\nDownload and install the latest version now?", "Update Available");
                if (ask != DialogResult.Yes) return;


                Form1.StopTimers(); // stop timer to prevent multiple update attempts
                // Download the source zip for the default branch (master/main). Prefer release asset if present.
                string? downloadUrl = null;
                // If release has assets, prefer first asset's browser_download_url
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0)
                {
                    var first = assets[0];
                    if (first.TryGetProperty("browser_download_url", out var bd))
                        downloadUrl = bd.GetString();
                }

                // fallback to archive of default branch
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    // Try to read default_branch from repo metadata
                    downloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/archive/refs/heads/master.zip";
                }

                var tmpZip = Path.Combine(Path.GetTempPath(), $"{RepoName}_update_{Guid.NewGuid():N}.zip");

                try
                {
                    using var dresp = await http.GetAsync(downloadUrl).ConfigureAwait(false);
                    if (!dresp.IsSuccessStatusCode)
                    {
                        ShowMessage(owner, $"Failed to download update archive: {(int)dresp.StatusCode} {dresp.ReasonPhrase}", "Update Check");
                        return;
                    }

                    using var fs = File.Create(tmpZip);
                    await dresp.Content.CopyToAsync(fs).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    ShowMessage(owner, "Failed to download update. Check your internet connection.", "Update Check");
                    return;
                }

                var extractDir = Path.Combine(Path.GetTempPath(), $"{RepoName}_extract_{Guid.NewGuid():N}");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(tmpZip, extractDir);

                // The repo zip typically extracts into a single folder; pick first subdirectory if needed
                var extractedRoot = extractDir;
                var dirs = Directory.GetDirectories(extractDir);
                if (dirs.Length == 1)
                    extractedRoot = dirs[0];

                var appDir = AppDomain.CurrentDomain.BaseDirectory;

                // Create a batch file that will copy files and restart the app. This batch will be launched and the current process will exit.
                var exePath = Assembly.GetEntryAssembly()?.Location;
                var exeName = exePath != null ? Path.GetFileName(exePath) : null;
                var batchPath = Path.Combine(extractDir, "update_and_restart.bat");

                // Use robocopy if available for more robust copying
                string copyCmd = $"robocopy \"{extractedRoot}\" \"{appDir}\" /MIR /NDL /NFL /NJH /NJS";

                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("REM wait a moment to allow the app to exit");
                sb.AppendLine("ping 127.0.0.1 -n 2 > nul");
                sb.AppendLine(copyCmd);
                if (!string.IsNullOrEmpty(exeName))
                    sb.AppendLine($"start \"\" \"{Path.Combine(appDir, exeName)}\"");
                sb.AppendLine("exit");

                File.WriteAllText(batchPath, sb.ToString(), Encoding.UTF8);

                // Start the batch and exit this process
                var psi = new ProcessStartInfo
                {
                    FileName = batchPath,
                    WorkingDirectory = extractDir,
                    UseShellExecute = true,
                    Verb = "runas" // request elevation if needed
                };

                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    // try without elevation
                    Process.Start(new ProcessStartInfo { FileName = batchPath, WorkingDirectory = extractDir, UseShellExecute = true });
                }

                // Exit application so the batch can overwrite files
                Application.Exit();
            }
            catch (Exception ex)
            {
                ShowMessage(owner, "Update failed: " + ex.Message, "Update Error");
            }
        }

        private static bool TryParseVersion(string raw, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            // remove leading 'v' or 'V'
            var s = raw.Trim();
            if ((s.StartsWith("v") || s.StartsWith("V")) && s.Length > 1) s = s.Substring(1);
            // split off prerelease metadata
            var idx = s.IndexOfAny(new char[] { '-', '+' });
            if (idx >= 0) s = s.Substring(0, idx);
            // Keep only digits and dots
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                if (char.IsDigit(ch) || ch == '.') sb.Append(ch);
                else break; // stop at first non-digit/dot
            }
            var candidate = sb.ToString();
            if (string.IsNullOrEmpty(candidate)) return false;
            // Normalize to at most 4 components
            var parts = candidate.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 4)
            {
                candidate = string.Join('.', parts, 0, 4);
            }
            // If only major/minor provided, append .0 for build
            if (parts.Length == 1) candidate += ".0.0";
            else if (parts.Length == 2) candidate += ".0";

            return Version.TryParse(candidate, out version);
        }

        private static void ShowMessage(IWin32Window? owner, string message, string caption)
        {
            try
            {
                // Choose the best owner: provided owner -> ActiveForm -> first open form
                Form? target = null;

                if (owner is Form f)
                {
                    target = f;
                }
                else if (owner is Control c)
                {
                    target = c.FindForm() ?? (c as Form);
                }

                if (target == null)
                {
                    target = Form.ActiveForm ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);
                }

                if (target != null && target.IsHandleCreated)
                {
                    // Ensure we run on the UI thread of the target so MessageBox is parented and centered over it
                    if (target.InvokeRequired)
                    {
                        try
                        {
                            target.Invoke(new Action(() => MessageBox.Show(target, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information)));
                        }
                        catch
                        {
                            // fallback
                            MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show(target, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    // No suitable owner found - show normal message box
                    MessageBox.Show(message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch { }
        }

        // Ask a Yes/No question and return result, owner-safe and marshaled to UI thread
        private static DialogResult AskYesNo(IWin32Window? owner, string message, string caption)
        {
            try
            {
                Form? target = null;
                if (owner is Form f) target = f;
                else if (owner is Control c) target = c.FindForm() ?? (c as Form);
                if (target == null) target = Form.ActiveForm ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);

                if (target != null && target.IsHandleCreated)
                {
                    if (target.InvokeRequired)
                    {
                        try
                        {
                            return (DialogResult)target.Invoke(new Func<DialogResult>(() => MessageBox.Show(target, message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question)));
                        }
                        catch
                        {
                            return MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        }
                    }
                    else
                    {
                        return MessageBox.Show(target, message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    }
                }
                else
                {
                    return MessageBox.Show(message, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                }
            }
            catch
            {
                return DialogResult.No;
            }
        }
    }
}
