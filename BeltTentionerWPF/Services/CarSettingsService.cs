using BeltAPI;
using System.IO;
using System.Text.Json;

namespace BeltTentionerWPF.Services
{
    /// <summary>
    /// Manages per-car settings persistence (ported from belttentiontest CarSettingsDatabase).
    /// </summary>
    public class CarSettingsService
    {
        private static CarSettingsService? _instance;
        public static CarSettingsService Instance => _instance ??= new CarSettingsService();

        public CarSettings CurrentSettings { get; private set; } = new CarSettings();
        public Dictionary<string, CarSettings> Settings { get; set; } = new();

        private string CarSettingsFile =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");

        private bool _isLoading;

        public void LoadFromFile(string carName)
        {
            _isLoading = true;
            if (File.Exists(CarSettingsFile))
            {
                try
                {
                    var json = File.ReadAllText(CarSettingsFile);
                    var data = JsonSerializer.Deserialize<CarSettingsService>(json);
                    if (data != null)
                        Settings = data.Settings;
                }
                catch { }
            }
            LoadIntoCurrent(carName);
            _isLoading = false;
        }

        public void LoadIntoCurrent(string carName)
        {
            if (!Settings.TryGetValue(carName, out var settings))
            {
                var copy = Settings.TryGetValue("NA", out var na) ? na?.DeepCopy() : null;
                Settings[carName] = copy ?? new CarSettings();
            }
            CurrentSettings = Settings[carName];
        }

        public void SaveCurrent(string? carName)
        {
            if (_isLoading) return;
            if (!string.IsNullOrEmpty(carName))
                Settings[carName] = CurrentSettings;

            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CarSettingsFile, json);
            }
            catch { }
        }
    }
}
