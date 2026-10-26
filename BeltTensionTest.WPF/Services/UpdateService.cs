using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Checks the GitHub releases of the repo for a newer build, downloads the
    /// release zip and installs it over the running install (via a helper
    /// script that waits for this process to exit, then relaunches the app).
    /// The app version is stamped into the assembly at build time (see
    /// version.txt and .github/workflows/build-wpf.yml) and is the same number
    /// used for the GitHub release tag.
    /// </summary>
    public class UpdateService
    {
        private const string RepoOwner = "Mattlekim";
        private const string RepoName  = "BeltTentioner";
        public const string FIRMWARE_VERSION = "1.0.5";

        /// <summary>How often a long-running instance re-checks for updates.</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(3);

        public static readonly string AppVersion = ReadAppVersion();

        /// <summary>
        /// True for builds not stamped by CI (e.g. run from Visual Studio): their
        /// build number is 0, so they would always look older than the latest release.
        /// </summary>
        public static readonly bool IsDevBuild =
            Version.TryParse(AppVersion, out var v) && v.Build <= 0;

        public class UpdateInfo
        {
            public bool IsUpdateAvailable { get; set; }
            public string? RemoteTag { get; set; }
            public string? DownloadUrl { get; set; }
            public long DownloadSize { get; set; }
            public string? Notes { get; set; }
            public string? Error { get; set; }
        }

        private static string ReadAppVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
            var v = asm.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            var result = new UpdateInfo();
            try
            {
                using var http = CreateClient();
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var resp = await http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { result.Error = $"HTTP {(int)resp.StatusCode}"; return result; }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return result;
                var remoteTag = tagProp.GetString() ?? string.Empty;
                if (!TryParseVersion(remoteTag, out var remoteVer) || remoteVer == null) return result;
                if (!Version.TryParse(AppVersion, out var localVer)) return result;
                if (remoteVer.CompareTo(localVer) <= 0) return result;

                // The release zip is the update payload; ignore other assets.
                string? dlUrl = null;
                long size = 0;
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (name == null || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!a.TryGetProperty("browser_download_url", out var bd)) continue;
                        dlUrl = bd.GetString();
                        if (a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s)) size = s;
                        break;
                    }
                }
                if (dlUrl == null) { result.Error = "Release has no downloadable zip."; return result; }

                result.IsUpdateAvailable = true;
                result.RemoteTag = remoteTag;
                result.DownloadUrl = dlUrl;
                result.DownloadSize = size;
                if (doc.RootElement.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
                    result.Notes = body.GetString();
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        /// <summary>Downloads the release zip to a temp file and returns its path.</summary>
        public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl)) throw new InvalidOperationException("No download URL.");

            var dir = Path.Combine(Path.GetTempPath(), "BeltTensioner-update");
            Directory.CreateDirectory(dir);
            var zipPath = Path.Combine(dir, $"BeltTensioner-{info.RemoteTag}.zip");

            using var http = CreateClient();
            using var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.DownloadSize;

            using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var dst = File.Create(zipPath))
            {
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            // Fail now (not mid-install) if the download is corrupt.
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (!zip.Entries.Any(e => e.Name.Equals("BST.exe", StringComparison.OrdinalIgnoreCase)
                                       || e.Name.Equals("BeltTensionTest.WPF.exe", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Downloaded update does not contain the application.");
            }
            return zipPath;
        }

        /// <summary>
        /// Starts a hidden helper that waits for this process to exit, extracts
        /// the zip over the install folder, and relaunches the app. The caller
        /// must shut the app down right after this returns true.
        /// </summary>
        public bool StartInstall(string zipPath)
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                var installDir = Path.GetDirectoryName(exe)!;

                var script = Path.Combine(Path.GetDirectoryName(zipPath)!, "install-update.ps1");
                File.WriteAllText(script, InstallScript);

                var psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" " +
                                $"-ProcessId {Environment.ProcessId} -Zip \"{zipPath}\" -InstallDir \"{installDir}\" -Exe \"{exe}\""
                };
                // Program Files installs need admin to be overwritten.
                if (!CanWrite(installDir)) psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        private const string InstallScript = @"
param([int]$ProcessId, [string]$Zip, [string]$InstallDir, [string]$Exe)
$ErrorActionPreference = 'Stop'
try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction SilentlyContinue } catch { }
$tmp = Join-Path ([IO.Path]::GetDirectoryName($Zip)) 'extracted'
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
Expand-Archive -Path $Zip -DestinationPath $tmp -Force
$ok = $false
for ($i = 0; $i -lt 10 -and -not $ok; $i++) {
    try { Copy-Item -Path (Join-Path $tmp '*') -Destination $InstallDir -Recurse -Force; $ok = $true }
    catch { Start-Sleep -Seconds 1 }
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $Zip -Force -ErrorAction SilentlyContinue
# The app used to be BeltTensionTest.WPF.exe; an update that renamed it leaves the old name behind.
if (-not (Test-Path $Exe) -or (Test-Path (Join-Path $InstallDir 'BST.exe'))) { $Exe = Join-Path $InstallDir 'BST.exe' }
Start-Process -FilePath $Exe -WorkingDirectory $InstallDir
";

        private static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BeltTensioner-WPF-Updater");
            return http;
        }

        private static bool TryParseVersion(string raw, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim().TrimStart('v', 'V');
            var idx = s.IndexOfAny(new[] { '-', '+' });
            if (idx >= 0) s = s[..idx];
            var parts = s.Split('.');
            if (parts.Length == 1) s += ".0.0";
            else if (parts.Length == 2) s += ".0";
            return Version.TryParse(s, out version);
        }
    }
}
