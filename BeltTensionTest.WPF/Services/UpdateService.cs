using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// App updates, served by the website (api/belttentioner on riddlersoftgames.co.uk): ask
    /// latest.php what the newest version is, download that installer (resumable, sha256
    /// checked), then hand it to <see cref="InstallAndRestart"/>. Versions are published by
    /// publish-release.ps1.
    /// </summary>
    public class UpdateService
    {
        private const string ApiBase = "https://riddlersoftgames.co.uk/api/belttentioner";
        public const string FIRMWARE_VERSION = "1.0.5";

        /// <summary>How often a running app asks again (it always asks once on startup).</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(2);

        /// <summary>The running version, from the assembly - which the csproj sets, so it is the installer's version too.</summary>
        public static string AppVersion { get; } = ReadAppVersion();

        private static string ReadAppVersion()
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }

        public class UpdateInfo
        {
            public bool IsUpdateAvailable { get; set; }
            public string? RemoteTag { get; set; }
            public string? DownloadUrl { get; set; }
            public string? FileName { get; set; }
            public string? Sha256 { get; set; }
            public long Size { get; set; }
            public string? Notes { get; set; }
            public string? Error { get; set; }
        }

        private static HttpClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BeltTensioner-WPF-Updater/" + AppVersion);
            return http;
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            var result = new UpdateInfo();
            try
            {
                using var http = NewClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                using var resp = await http.GetAsync($"{ApiBase}/latest.php?current={AppVersion}").ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { result.Error = $"HTTP {(int)resp.StatusCode}"; return result; }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("latest", out var latest) || latest.ValueKind != JsonValueKind.Object)
                    return result; // nothing published yet

                var remoteTag = latest.GetProperty("version").GetString() ?? string.Empty;
                if (!Version.TryParse(remoteTag, out var remoteVer)) return result;
                if (!Version.TryParse(AppVersion, out var localVer)) return result;
                if (remoteVer.CompareTo(localVer) <= 0) return result;

                result.IsUpdateAvailable = true;
                result.RemoteTag = remoteTag;
                result.DownloadUrl = latest.GetProperty("url").GetString();
                result.FileName = latest.GetProperty("file").GetString();
                result.Sha256 = latest.GetProperty("sha256").GetString();
                result.Size = latest.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                result.Notes = latest.TryGetProperty("notes", out var n) ? n.GetString() : null;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        private static string UpdatesDir
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeltTensioner", "updates");

        /// <summary>Where the installer for <paramref name="info"/> is (or will be) saved.</summary>
        public static string InstallerPath(UpdateInfo info)
            => Path.Combine(UpdatesDir, Path.GetFileName(info.FileName ?? $"BeltTensionerSetup-{info.RemoteTag}.exe"));

        /// <summary>
        /// Downloads the installer, carrying on from a half-finished file, and checks its sha256.
        /// An installer already downloaded and intact is reused. Returns the path.
        /// </summary>
        public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl) || string.IsNullOrEmpty(info.Sha256))
                throw new InvalidOperationException("The update has no download.");

            Directory.CreateDirectory(UpdatesDir);
            var final = InstallerPath(info);
            var part = final + ".part";

            if (File.Exists(final) && HashMatches(final, info.Sha256)) { progress?.Report(1); return final; }
            try { File.Delete(final); } catch { }

            using var http = NewClient();
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (info.Size > 0 && have > info.Size) { File.Delete(part); have = 0; }

            if (info.Size == 0 || have < info.Size)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
                if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                if (have > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0; // server ignored Range

                long total = have + (resp.Content.Headers.ContentLength ?? 0);
                using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                long done = have;
                int read;
                while ((read = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)done / total));
                }
            }

            if (!HashMatches(part, info.Sha256))
            {
                try { File.Delete(part); } catch { }
                throw new InvalidDataException("The downloaded update is damaged (checksum mismatch). Try again.");
            }
            File.Move(part, final, true);
            progress?.Report(1);
            return final;
        }

        private static bool HashMatches(string path, string expected)
        {
            using var fs = File.OpenRead(path);
            var hex = Convert.ToHexString(SHA256.HashData(fs));
            return string.Equals(hex, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when this copy was put here by the setup program (it leaves unins000.exe beside
        /// the app). A portable zip has none, and running the installer over it would make a
        /// second copy somewhere else instead of updating this one.
        /// </summary>
        public static bool IsInstalledCopy
            => Directory.GetFiles(AppContext.BaseDirectory, "unins*.exe").Length > 0;

        /// <summary>
        /// Installs over the running copy and starts the new one. The app cannot replace its own
        /// files while it runs, so a small PowerShell script (detached from this process) waits
        /// for this process to exit, runs the setup silently - Inno's CloseApplications sweeps up
        /// anything still holding a file - and then launches the app again. The caller must
        /// shut the app down right after this returns true.
        /// </summary>
        public bool InstallAndRestart(string installerPath, out string? error)
        {
            error = null;
            try
            {
                var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                // installed for all users (under Program Files) needs the all-users mode again,
                // which asks for admin; otherwise the upgrade would land in a new per-user folder
                bool allUsers = exe.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                             || exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);
                var modeArg = allUsers ? "/ALLUSERS" : "/CURRENTUSER";

                string Q(string s) => "'" + s.Replace("'", "''") + "'";
                var script =
                    $"$ErrorActionPreference='SilentlyContinue'\n" +
                    $"Wait-Process -Id {Environment.ProcessId} -Timeout 60\n" +
                    $"Start-Process -FilePath {Q(installerPath)} -ArgumentList '/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS','/FORCECLOSEAPPLICATIONS','{modeArg}' -Wait\n" +
                    $"Start-Process -FilePath {Q(exe)} -WorkingDirectory {Q(Path.GetDirectoryName(exe)!)}\n";
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

                Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand {encoded}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Shows the downloaded installer in Explorer (for a portable copy, which can't be updated in place).</summary>
        public static void RevealInstaller(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
            catch { }
        }

        /// <summary>The downloads page, for when the in-app download fails.</summary>
        public void OpenReleasePage()
        {
            try { Process.Start(new ProcessStartInfo("https://riddlersoftgames.co.uk/belttentioner.html#downloads") { UseShellExecute = true }); }
            catch { }
        }
    }
}
