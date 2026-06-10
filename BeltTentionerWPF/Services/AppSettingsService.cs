using System.IO;
using System.Text.Json;

namespace BeltTentionerWPF.Services
{
    public class AppSettings
    {
        public bool AutoConnectOnStartup { get; set; } = false;
        public bool UseSimHub { get; set; } = false;
        public bool UseIracing { get; set; } = true;
    }

    public static class AppSettingsService
    {
        private const string SettingsFile = "autoconnect.json";
        public static AppSettings Current { get; private set; } = new AppSettings();

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var json = File.ReadAllText(SettingsFile);
                if (string.IsNullOrWhiteSpace(json)) return;
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith('{'))
                    Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                else if (bool.TryParse(json.Trim(), out var legacy))
                    Current = new AppSettings { AutoConnectOnStartup = legacy };
            }
            catch { Current = new AppSettings(); }
        }

        public static void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(Current, opts));
            }
            catch { }
        }
    }
}
