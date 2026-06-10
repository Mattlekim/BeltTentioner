using BeltAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Manages per-car settings, mirroring the WinForms CarSettingsDatabase singleton.
    /// This is a service so it can be injected and tested independently.
    /// </summary>
    public class CarSettingsService
    {
        private static readonly Lazy<CarSettingsService> _instance = new(() => new CarSettingsService());
        public static CarSettingsService Instance => _instance.Value;

        private readonly string _filePath;
        private bool _isLoading;

        public CarSettings CurrentSettings { get; set; } = new CarSettings();
        public Dictionary<string, CarSettings> Settings { get; set; } = new();

        public CarSettingsService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");
        }

        public void SaveCurrentCarSettings(string? name)
        {
            if (_isLoading) return;
            if (!string.IsNullOrEmpty(name))
                Settings[name] = CurrentSettings;
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }

        public void LoadCarSettingsFromFile(string carName)
        {
            _isLoading = true;
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<CarSettingsService>(json);
                    if (data != null)
                        Settings = data.Settings ?? new Dictionary<string, CarSettings>();
                }
            }
            catch { }
            finally { _isLoading = false; }

            LoadCarSettingsIntoCurrent(carName);
        }

        private void LoadCarSettingsIntoCurrent(string carName)
        {
            if (!Settings.TryGetValue(carName, out var settings))
            {
                if (Settings.TryGetValue("NA", out var naSettings))
                    Settings[carName] = naSettings?.DeepCopy() ?? new CarSettings();
                else
                    Settings[carName] = new CarSettings();
                SaveCurrentCarSettings(null);
            }
            CurrentSettings = Settings[carName];
        }
    }
}
