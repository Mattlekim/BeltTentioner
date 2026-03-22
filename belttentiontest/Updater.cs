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

        public class UpdateInfo
        {
            public bool IsUpdateAvailable { get; set; }
            public string? RemoteTag { get; set; }
            public string? Changelog { get; set; }
            public string? DownloadUrl { get; set; }
            public string? Error { get; set; }
        }

        // Silent check: returns UpdateInfo without prompting. Safe to call on background thread.
        public static async Task<UpdateInfo> GetUpdateInfoAsync()
        {
            var result = new UpdateInfo { IsUpdateAvailable = false };
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BeltTentioner-Updater");
                var releaseApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                HttpResponseMessage resp;
                try
                {
                    resp = await http.GetAsync(releaseApi).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.Error = "Failed to connect: " + ex.Message;
                    return result;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    result.Error = $"Update server returned {(int)resp.StatusCode}";
                    return result;
                }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return result;
                var remoteTag = tagProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(remoteTag)) return result;
                if (!TryParseVersion(remoteTag, out var remoteVersion)) return result;

                string changelog = string.Empty;
                if (doc.RootElement.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String)
                    changelog = CleanChangelog(bodyProp.GetString() ?? string.Empty);

                Version? localVersion = null;
                try { localVersion = new Version(AboutBox.Version); } catch { }
                if (localVersion == null) return result;

                if (remoteVersion.CompareTo(localVersion) <= 0) return result;

                // determine download url (prefer first asset)
                string? downloadUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0)
                {
                    var first = assets[0];
                    if (first.TryGetProperty("browser_download_url", out var bd)) downloadUrl = bd.GetString();
                }
                if (string.IsNullOrEmpty(downloadUrl))
                    downloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/archive/refs/heads/master.zip";

                result.IsUpdateAvailable = true;
                result.RemoteTag = remoteTag;
                result.Changelog = changelog;
                result.DownloadUrl = downloadUrl;
                return result;
            }
            catch (Exception ex)
            {
                return new UpdateInfo { Error = ex.Message };
            }
        }

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

                // extract changelog/body if present
                string changelog = string.Empty;
                if (doc.RootElement.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String)
                {
                    changelog = bodyProp.GetString() ?? string.Empty;
                }

                // normalize changelog for display (decode HTML entities, try base64 fallback)
                changelog = CleanChangelog(changelog);

                // attempt to fetch commits included in this release by comparing to previous release
                try
                {
                    // fetch releases list to find previous release
                    var releasesApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
                    using var relResp = await http.GetAsync(releasesApi).ConfigureAwait(false);
                    if (relResp.IsSuccessStatusCode)
                    {
                        using var relStream = await relResp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var relDoc = await JsonDocument.ParseAsync(relStream).ConfigureAwait(false);
                        if (relDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            // collect tag names ordered by published_at desc
                            var releases = new System.Collections.Generic.List<(string tag, DateTime? published)>();
                            foreach (var el in relDoc.RootElement.EnumerateArray())
                            {
                                try
                                {
                                    var tag = el.GetProperty("tag_name").GetString() ?? string.Empty;
                                    DateTime? pub = null;
                                    if (el.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String)
                                    {
                                        if (DateTime.TryParse(p.GetString(), out var dt)) pub = dt;
                                    }
                                    releases.Add((tag, pub));
                                }
                                catch { }
                            }

                            // sort by published desc
                            releases.Sort((a, b) => Nullable.Compare(b.published, a.published));
                            // find index of current tag
                            int idx = releases.FindIndex(r => string.Equals(r.tag, remoteTag, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0 && idx + 1 < releases.Count)
                            {
                                var prevTag = releases[idx + 1].tag;
                                if (!string.IsNullOrWhiteSpace(prevTag))
                                {
                                    try
                                    {
                                        var compareApi = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/compare/{prevTag}...{remoteTag}";
                                        using var cmpResp = await http.GetAsync(compareApi).ConfigureAwait(false);
                                        if (cmpResp.IsSuccessStatusCode)
                                        {
                                            using var cmpStream = await cmpResp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                            using var cmpDoc = await JsonDocument.ParseAsync(cmpStream).ConfigureAwait(false);
                                            if (cmpDoc.RootElement.TryGetProperty("commits", out var commitsEl) && commitsEl.ValueKind == JsonValueKind.Array)
                                            {
                                                var sbCommits = new StringBuilder();
                                                sbCommits.AppendLine();
                                                sbCommits.AppendLine("--- Commits in this release ---");
                                                foreach (var c in commitsEl.EnumerateArray())
                                                {
                                                    try
                                                    {
                                                        var sha = c.GetProperty("sha").GetString() ?? string.Empty;
                                                        string shortSha = sha.Length >= 7 ? sha.Substring(0, 7) : sha;
                                                        string msg = "";
                                                        string author = "";
                                                        if (c.TryGetProperty("commit", out var commitObj))
                                                        {
                                                            if (commitObj.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                                                                msg = m.GetString() ?? string.Empty;
                                                            if (commitObj.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object)
                                                            {
                                                                if (a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String)
                                                                    author = an.GetString() ?? string.Empty;
                                                            }
                                                        }
                                                        // fallback to top-level author.login
                                                        if (string.IsNullOrEmpty(author) && c.TryGetProperty("author", out var topAuthor) && topAuthor.ValueKind == JsonValueKind.Object)
                                                        {
                                                            if (topAuthor.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String)
                                                                author = login.GetString() ?? string.Empty;
                                                        }

                                                        // take first line of message
                                                        var firstLine = string.IsNullOrEmpty(msg) ? "(no message)" : msg.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0];
                                                        sbCommits.AppendLine($"- {shortSha}: {firstLine} ({author})");
                                                    }
                                                    catch { }
                                                }

                                                // append commits list to changelog
                                                changelog += sbCommits.ToString();
                                            }
                                        }
                                    }
                                    catch { /* ignore compare failures */ }
                                }
                            }
                        }
                    }
                }
                catch { /* ignore additional metadata failures */ }

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

                // Show changelog and ask
                var ask = ShowChangelogAndAsk(owner, $"A new version is available ({remoteTag})", changelog);
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
                    // Download with progress
                    await DownloadFileWithProgressAsync(http, downloadUrl, tmpZip, owner).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    ShowMessage(owner, "Download canceled.", "Update Check");
                    return;
                }
                catch (Exception ex)
                {
                    ShowMessage(owner, "Failed to download update. " + ex.Message, "Update Check");
                    return;
                }

                // Ask user to install after successful download
                var installAsk = AskYesNo(owner, "Download complete. Install update now?", "Install Update");
                if (installAsk != DialogResult.Yes)
                {
                    ShowMessage(owner, "Update downloaded to: " + tmpZip, "Update Downloaded");
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
                
                // If the extracted content doesn't contain the actual executable, try to locate the directory that does
                var exePath = Assembly.GetEntryAssembly()?.Location;
                var exeName = exePath != null ? Path.GetFileName(exePath) : null;
                if (!string.IsNullOrEmpty(exeName))
                {
                    // search for the exe inside the extracted tree (e.g. repo may contain a 'bin/Release' or 'publish' folder)
                    try
                    {
                        var found = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories);
                        if (found != null && found.Length > 0)
                        {
                            // use the directory that contains the exe as the source to copy
                            extractedRoot = Path.GetDirectoryName(found[0]) ?? extractedRoot;
                        }
                    }
                    catch
                    {
                        // ignore and fall back to extractedRoot
                    }
                }

                var appDir = AppDomain.CurrentDomain.BaseDirectory;

                // Create a batch file that will copy files and restart the app. This batch will be launched and the current process will exit.
                // exeName may have been set above
                 var batchPath = Path.Combine(extractDir, "update_and_restart.bat");

                // Use robocopy if available for more robust copying
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("setlocal enabledelayedexpansion");
                sb.AppendLine("REM wait for the app to exit, up to 30 seconds");
                sb.AppendLine($"set EXE=\"{Path.Combine(appDir, exeName ?? string.Empty)}\"");
                sb.AppendLine("set RETRIES=30");
                sb.AppendLine("set COUNT=0");
                sb.AppendLine(":WAITLOOP");
                // check if process with exe name is still running
                if (!string.IsNullOrEmpty(exeName))
                {
                    sb.AppendLine($"tasklist /FI \"IMAGENAME eq {exeName}\" | find /I \"{exeName}\" > nul");
                    sb.AppendLine("if %errorlevel%==0 (");
                    sb.AppendLine("    if %COUNT% GEQ %RETRIES% goto COPY");
                    sb.AppendLine("    timeout /t 1 /nobreak >nul");
                    sb.AppendLine("    set /A COUNT+=1");
                    sb.AppendLine("    goto WAITLOOP");
                    sb.AppendLine(")");
                }
                sb.AppendLine(":COPY");
                sb.AppendLine("REM Attempt robocopy first");
                sb.AppendLine($"robocopy \"{extractedRoot}\" \"{appDir}\" /MIR /NDL /NFL /NJH /NJS /COPY:DAT /R:3 /W:2 > \"%TEMP%\\updater_log.txt\" 2>&1");
                sb.AppendLine("set RC=%ERRORLEVEL%");
                sb.AppendLine("REM Robocopy returns codes < 8 for success or minor issues");
                sb.AppendLine("if %RC% GEQ 8 (");
                sb.AppendLine("    REM fallback to xcopy if robocopy failed");
                sb.AppendLine($"    xcopy \"{extractedRoot}\\*\" \"{appDir}\\\" /E /Y /I > \"%TEMP%\\updater_xcopy_log.txt\" 2>&1");
                sb.AppendLine(")");
                sb.AppendLine("REM Start the application if the exe exists");
                if (!string.IsNullOrEmpty(exeName))
                {
                    sb.AppendLine($"if exist \"{Path.Combine(appDir, exeName)}\" start \"\" \"{Path.Combine(appDir, exeName)}\"");
                }
                sb.AppendLine("endlocal");
                sb.AppendLine("echo Update finished. Press any key to close this window.");
                sb.AppendLine("pause > nul");
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

        // Download helper with a simple progress form
        private static async Task DownloadFileWithProgressAsync(HttpClient http, string url, string destinationPath, IWin32Window? owner)
        {
            using var dresp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            dresp.EnsureSuccessStatusCode();

            var total = dresp.Content.Headers.ContentLength ?? -1L;

            using var source = await dresp.Content.ReadAsStreamAsync().ConfigureAwait(false);

            // Ensure progress form is created and shown on UI thread
            DownloadProgressForm? progressForm = null;
            try
            {
                Control? uiControl = null;
                if (owner is Control ctrl && ctrl.IsHandleCreated)
                {
                    uiControl = ctrl;
                }
                else if (Application.OpenForms.Count > 0)
                {
                    var top = Application.OpenForms[0];
                    if (top.IsHandleCreated) uiControl = top;
                }

                if (uiControl != null)
                {
                    // create and show progress form on the UI thread
                    uiControl.Invoke(new Action(() =>
                    {
                        progressForm = new DownloadProgressForm(uiControl);
                        progressForm.Show();
                    }));
                }
                else
                {
                    // no UI available, create on current thread
                    progressForm = new DownloadProgressForm(null);
                    progressForm.Show();
                }

                const int bufferSize = 81920;
                var buffer = new byte[bufferSize];
                long totalRead = 0;
                int read;

                using var fs = File.Create(destinationPath);
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    totalRead += read;

                    if (progressForm != null)
                    {
                        var percent = total > 0 ? (int)(totalRead * 100 / total) : -1;
                        try
                        {
                            if (progressForm.InvokeRequired)
                            {
                                progressForm.BeginInvoke(new Action(() => progressForm.UpdateProgress(percent, totalRead, total)));
                            }
                            else
                            {
                                progressForm.UpdateProgress(percent, totalRead, total);
                            }
                        }
                        catch
                        {
                            // ignore UI update errors
                        }
                    }
                }
            }
            finally
            {
                if (progressForm != null)
                {
                    try
                    {
                        if (progressForm.InvokeRequired)
                        {
                            progressForm.BeginInvoke(new Action(() => { try { progressForm.Close(); } catch { } }));
                        }
                        else
                        {
                            progressForm.Close();
                        }
                    }
                    catch
                    {
                        try { progressForm.Close(); } catch { }
                    }
                }
            }
        }

        // Small progress dialog used during download
        private class DownloadProgressForm : Form
        {
            private ProgressBar _bar;
            private Label _label;
            public DownloadProgressForm(IWin32Window? owner)
            {
                StartPosition = FormStartPosition.CenterParent;
                Width = 400;
                Height = 100;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                _bar = new ProgressBar { Style = ProgressBarStyle.Continuous, Dock = DockStyle.Bottom, Height = 20, Minimum = 0, Maximum = 100 };
                _label = new Label { Text = "Downloading...", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Padding = new Padding(8) };

                Controls.Add(_bar);
                Controls.Add(_label);

                if (owner is Form f)
                {
                    this.Owner = f;
                }
            }

            public void UpdateProgress(int percent, long downloaded, long total)
            {
                if (percent >= 0)
                {
                    _bar.Value = Math.Clamp(percent, 0, 100);
                    _label.Text = $"Downloading... {percent}% ({FormatBytes(downloaded)}/{(total > 0 ? FormatBytes(total) : "?" )})";
                }
                else
                {
                    _label.Text = $"Downloading... ({FormatBytes(downloaded)}/?)";
                }
            }

            private static string FormatBytes(long bytes)
            {
                if (bytes < 1024) return bytes + " B";
                if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
                if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
                return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " GB";
            }
        }
        
        // Show changelog in a dialog with Yes/No buttons. Returns DialogResult.Yes to proceed.
        private static DialogResult ShowChangelogAndAsk(IWin32Window? owner, string title, string changelog)
        {
            try
            {
                Form? target = null;
                if (owner is Form f) target = f;
                else if (owner is Control c) target = c.FindForm() ?? (c as Form);
                if (target == null) target = Form.ActiveForm ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);

                // Build dialog on UI thread of target if available
                if (target != null && target.IsHandleCreated)
                {
                    if (target.InvokeRequired)
                    {
                        return (DialogResult)target.Invoke(new Func<DialogResult>(() => ShowChangelogDialogInternal(target, title, changelog)));
                    }
                    else
                    {
                        return ShowChangelogDialogInternal(target, title, changelog);
                    }
                }
                else
                {
                    return ShowChangelogDialogInternal(null, title, changelog);
                }
            }
            catch
            {
                return DialogResult.No;
            }
        }

        private static DialogResult ShowChangelogDialogInternal(IWin32Window? owner, string title, string changelog)
        {
            using var dlg = new Form();
            dlg.Text = title;
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.Width = 600;
            dlg.Height = 400;
            dlg.FormBorderStyle = FormBorderStyle.Sizable;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = true;

            var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Consolas", 10) };
            tb.Text = string.IsNullOrEmpty(changelog) ? "No changelog available." : changelog;

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
            var btnYes = new Button { Text = "Yes", DialogResult = DialogResult.Yes, AutoSize = true }; 
            var btnNo = new Button { Text = "No", DialogResult = DialogResult.No, AutoSize = true };
            btnPanel.Controls.Add(btnYes);
            btnPanel.Controls.Add(btnNo);

            dlg.Controls.Add(tb);
            dlg.Controls.Add(btnPanel);

            dlg.AcceptButton = btnYes;
            dlg.CancelButton = btnNo;

            if (owner is Form fOwner)
            {
                dlg.ShowDialog(fOwner);
                return dlg.DialogResult;
            }
            else
            {
                return dlg.ShowDialog();
            }
        }

        // Try to normalize changelog text for display. Handles HTML entities and optional base64-encoded bodies.
        private static string CleanChangelog(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            try
            {
                // Decode HTML entities (GitHub sometimes returns HTML-encoded content)
                string decoded = System.Net.WebUtility.HtmlDecode(raw);

                // If the text contains many non-printable characters, it might be base64 encoded
                int nonPrintable = 0;
                int inspect = Math.Min(200, decoded.Length);
                for (int i = 0; i < inspect; i++)
                {
                    var ch = decoded[i];
                    if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t') nonPrintable++;
                }

                if (inspect > 0 && ((double)nonPrintable / inspect) > 0.3)
                {
                    // try base64 -> utf8
                    try
                    {
                        var bytes = Convert.FromBase64String(decoded);
                        var utf = System.Text.Encoding.UTF8.GetString(bytes);
                        decoded = System.Net.WebUtility.HtmlDecode(utf);
                    }
                    catch { /* not base64 */ }
                }

                // Normalize newlines
                decoded = decoded.Replace("\r\n", "\n").Replace("\r", "\n");
                return decoded;
            }
            catch
            {
                return raw;
            }
        }
    }
}
