using BeltAPI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using YamlDotNet.Core;
namespace belttentiontest
{ 
    public class CarSettingsDatabase
    {
        private static CarSettingsDatabase? _instance;
        public static CarSettingsDatabase Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new CarSettingsDatabase();
                return _instance;
            }
            set { _instance = value; }
        }


        public CarSettings CurrentSettings { get; set; } = new CarSettings();

        public Dictionary<string, CarSettings> Settings { get; set; } = new();

        private string carSettingsFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "car_settings.json");

        public void SaveCurrentCarSettings(string? name)
        {
            if (_isLoading) return; // Don't save while loading to avoid overwriting loaded settings

            if (name != null && name != string.Empty)
                Settings[name] = CurrentSettings;

            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(carSettingsFile, json);

            } catch { }
        }

        public CarSettings LoadCarSettingsInToCurrent(string carName)
        {
            // If no settings for carName, try to copy from NA
            if (!Settings.TryGetValue(carName, out var settings)) //if car name not found in settings, try to copy from NA
            {
                if (Settings.TryGetValue("NA", out var naSettings))
                {
                    // Deep copy NA settings to new car
                    Settings.Add(carName, naSettings);
                }
                else
                {
                    settings = new CarSettings();
                }
                Settings[carName] = settings;
                // Save immediately so the new car gets its own settings file entry
                SaveCurrentCarSettings(null);
                
            }
            if (settings == null)
                settings = new CarSettings();
            return settings;
        }

        private bool _isLoading = false;
        public CarSettings LoadCarSettingsFromFile(string carName)
        {
            _isLoading = true;
            if (File.Exists(carSettingsFile))
            {
                try
                {
                    var json = File.ReadAllText(carSettingsFile);
                    CarSettingsDatabase.Instance = JsonSerializer.Deserialize<CarSettingsDatabase>(json) ?? new CarSettingsDatabase();
                    return LoadCarSettingsInToCurrent(carName);
                    
                }
                catch {  }
            }
            _isLoading = false;
            return new CarSettings();
        }
    }

    
}
