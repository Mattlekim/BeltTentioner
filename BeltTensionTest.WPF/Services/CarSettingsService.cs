using BeltAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

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

        /// <summary>
        /// Loads the settings dictionary from disk without creating or modifying entries.
        /// Useful for UI checks before creating new entries.
        /// </summary>
        public void LoadFromDisk()
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
        }

        /// <summary>
        /// Returns available car keys (names) currently loaded in memory.
        /// Call LoadFromDisk() first to ensure the latest list from disk.
        /// </summary>
        public IEnumerable<string> GetAvailableCarNames()
        {
            return Settings?.Keys?.OrderBy(k => k) ?? Enumerable.Empty<string>();
        }

        /// <summary>
        /// Copy settings from an existing key into a target key (deep copy) and persist.
        /// If source key is not found, creates a default settings entry for the target.
        /// </summary>
        public void CopySettings(string sourceName, string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return;

            if (Settings.TryGetValue(sourceName, out var source))
                Settings[targetName] = source?.DeepCopy() ?? new CarSettings();
            else if (Settings.TryGetValue("NA", out var naSettings))
                Settings[targetName] = naSettings?.DeepCopy() ?? new CarSettings();
            else
                Settings[targetName] = new CarSettings();

            SaveCurrentCarSettings(null);
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
