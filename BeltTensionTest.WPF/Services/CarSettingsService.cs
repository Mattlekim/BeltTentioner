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
            // Store car settings in the user's Documents\BBT folder
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = Path.Combine(docs, "BBT");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }
            _filePath = Path.Combine(dir, "car_settings.json");
        }

        public void SaveCurrentCarSettings(string? name)
        {
            if (_isLoading) return;
            if (!string.IsNullOrEmpty(name))
                Settings[name] = CurrentSettings;
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                var fileDir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                    Directory.CreateDirectory(fileDir);
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

        // ── Profile management (Settings → Car Profiles) ─────────────────────

        // Same shape as car_settings.json so a full export is a valid profile file
        // and either can be imported.
        private class ProfileFile
        {
            public Dictionary<string, CarSettings>? Settings { get; set; }
        }

        /// <summary>Removes a profile and persists. False if it didn't exist.</summary>
        public bool DeleteProfile(string name)
        {
            if (!Settings.Remove(name)) return false;
            SaveCurrentCarSettings(null);
            return true;
        }

        /// <summary>Writes the named profiles (copies) to a JSON file.</summary>
        public void ExportProfiles(IEnumerable<string> names, string path)
        {
            var file = new ProfileFile { Settings = new Dictionary<string, CarSettings>() };
            foreach (var name in names)
                if (Settings.TryGetValue(name, out var s) && s != null)
                    file.Settings[name] = s.DeepCopy();

            File.WriteAllText(path, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Reads the profiles in an exported file. Throws if it isn't a profile file.</summary>
        public static Dictionary<string, CarSettings> ReadProfilesFile(string path)
        {
            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path));
            var profiles = file?.Settings?
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value != null)
                .ToDictionary(p => p.Key, p => p.Value);
            if (profiles == null || profiles.Count == 0)
                throw new InvalidDataException("The file doesn't contain any car profiles.");
            return profiles;
        }

        /// <summary>
        /// Adds or replaces a profile. makeCurrent also points CurrentSettings at it, so a
        /// pending autosave of the active car can't write the old values back over it.
        /// Call SaveCurrentCarSettings(null) after a batch.
        /// </summary>
        public void SetProfile(string name, CarSettings settings, bool makeCurrent)
        {
            Settings[name] = settings;
            if (makeCurrent) CurrentSettings = settings;
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
