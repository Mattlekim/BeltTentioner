using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BeltTensionTest.WPF.Services
{
    public class UpdateService
    {
        private const string RepoOwner = "Mattlekim";
        private const string RepoName  = "BeltTentioner";
        public const  string AppVersion = "1.0.47";

        public class UpdateInfo
        {
            public bool IsUpdateAvailable { get; set; }
            public string? RemoteTag { get; set; }
            public string? DownloadUrl { get; set; }
            public string? Error { get; set; }
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            var result = new UpdateInfo();
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BeltTensioner-WPF-Updater");
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var resp = await http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) { result.Error = $"HTTP {(int)resp.StatusCode}"; return result; }

                using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return result;
                var remoteTag = tagProp.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(remoteTag)) return result;

                if (!TryParseVersion(remoteTag, out var remoteVer)) return result;
                if (!Version.TryParse(AppVersion, out var localVer)) return result;
                if (remoteVer.CompareTo(localVer) <= 0) return result;

                string? dlUrl = null;
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    if (assets[0].TryGetProperty("browser_download_url", out var bd)) dlUrl = bd.GetString();
                dlUrl ??= $"https://github.com/{RepoOwner}/{RepoName}/archive/refs/heads/master.zip";

                result.IsUpdateAvailable = true;
                result.RemoteTag = remoteTag;
                result.DownloadUrl = dlUrl;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }

        public void OpenReleasePage()
        {
            try { Process.Start(new ProcessStartInfo($"https://github.com/{RepoOwner}/{RepoName}/releases/latest") { UseShellExecute = true }); }
            catch { }
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
