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
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
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
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, opts));
            }
            catch { }
        }
    }
}
