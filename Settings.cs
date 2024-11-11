using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FastImageGallery;

namespace FastImageGallery
{
    public class Settings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastImageGallery",
            "settings.json"
        );

        public List<string> WatchedFolders { get; set; } = new List<string>();

        public static Settings Current { get; private set; } = new Settings();

        public static void Load()
        {
            try
            {
                Logger.Log($"Attempting to load settings from: {SettingsPath}");
                
                string? directory = Path.GetDirectoryName(SettingsPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Logger.Log($"Creating settings directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(SettingsPath))
                {
                    Logger.Log("Settings file exists, loading...");
                    string json = File.ReadAllText(SettingsPath);
                    var loadedSettings = JsonSerializer.Deserialize<Settings>(json);
                    if (loadedSettings != null)
                    {
                        Current = loadedSettings;
                        Logger.Log($"Settings loaded successfully. Watched folders count: {Current.WatchedFolders.Count}");
                    }
                }
                else
                {
                    Logger.Log("No settings file found, using defaults");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error loading settings", ex);
                Current = new Settings();
            }
        }

        public static void Save()
        {
            try
            {
                Logger.Log($"Attempting to save settings to: {SettingsPath}");
                Logger.Log($"Current watched folders count: {Current.WatchedFolders.Count}");

                string? directory = Path.GetDirectoryName(SettingsPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Logger.Log($"Creating settings directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                };
                
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(SettingsPath, json);
                
                Logger.Log("Settings saved successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error saving settings", ex);
                throw; // Rethrow to make failures visible
            }
        }
    }
}