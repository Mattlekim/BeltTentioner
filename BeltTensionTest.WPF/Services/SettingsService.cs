using BeltTensionTest.WPF.Models;
using System;
using System.IO;
using System.Text.Json;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Loads / saves AppSettings from autoconnect.json next to the executable.
    /// </summary>
    public class SettingsService
    {
        private const string FileName = "autoconnect.json";
        private readonly string _filePath;

        public SettingsService()
        {
            // Persist settings in the user's AppData folder to avoid permission issues when writing
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appdata, "BeltTensioner");
            _filePath = Path.Combine(dir, FileName);
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new AppSettings();
                var json = File.ReadAllText(_filePath).Trim();
                if (string.IsNullOrWhiteSpace(json)) return new AppSettings();

                // support legacy single-bool file
                if (json.StartsWith('{'))
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                if (bool.TryParse(json, out var legacy))
                    return new AppSettings { AutoConnectOnStartup = legacy };
            }
            catch { }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            // Ensure directory exists
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, opts));
            }
            catch (Exception)
            {
                // Let caller decide how to handle failures; keep behavior silent here.
            }
        }

        // Expose file path for diagnostics
        public string FilePath => _filePath;
    }
}
