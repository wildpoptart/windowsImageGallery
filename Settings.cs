using System;

using System.Collections.Generic;

using System.IO;

using System.Text.Json;

using System.Linq;

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



        private List<string> _watchedFolders = new List<string>();

        

        public List<string> WatchedFolders

        {

            get => _watchedFolders;

            set => _watchedFolders = value.Distinct().ToList(); // Ensure no duplicates when setting

        }



        public ThumbnailSize PreferredThumbnailSize { get; set; } = ThumbnailSize.Medium;



        public static Settings Current { get; private set; } = new Settings();



        public bool AddWatchedFolder(string folderPath)

        {

            // Normalize the path to ensure consistent comparison

            string normalizedPath = Path.GetFullPath(folderPath);

            

            // Check if the folder or any parent folder is already being watched

            if (_watchedFolders.Any(existing => 

                normalizedPath.StartsWith(Path.GetFullPath(existing), StringComparison.OrdinalIgnoreCase)))

            {

                Logger.Log($"Folder or parent folder already being watched: {folderPath}");

                return false;

            }



            // Check if the new folder is a parent of any existing watched folders

            // If so, remove the child folders as they'll be covered by the parent

            var childFolders = _watchedFolders

                .Where(existing => Path.GetFullPath(existing)

                    .StartsWith(normalizedPath, StringComparison.OrdinalIgnoreCase))

                .ToList();



            foreach (var childFolder in childFolders)

            {

                _watchedFolders.Remove(childFolder);

                Logger.Log($"Removed child folder: {childFolder} as parent will be watched");

            }



            _watchedFolders.Add(normalizedPath);

            Logger.Log($"Added new watched folder: {normalizedPath}");

            return true;

        }



        public bool RemoveWatchedFolder(string folderPath)

        {

            string normalizedPath = Path.GetFullPath(folderPath);

            bool removed = _watchedFolders.Remove(normalizedPath);

            if (removed)

            {

                Logger.Log($"Removed watched folder: {normalizedPath}");

            }

            return removed;

        }



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
