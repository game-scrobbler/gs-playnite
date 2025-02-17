using System;
using System.IO;
using System.Text.Json;

namespace GsPlugin {
    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GSData {
        public string InstallID { get; set; } = null;
        public string SessionId { get; set; } = null;
        public Boolean IsDark { get; set; } = false;
    }

    /// <summary>
    /// Handles loading and saving CustomData to a JSON file.
    /// </summary>
    public class GSDataStorage {
        private readonly string filePath;

        public GSDataStorage(string folderPath) {
            // Build the path for the custom data file.
            filePath = Path.Combine(folderPath, "customData.json");
        }

        /// <summary>
        /// Loads the custom data from disk.
        /// Returns a new instance if the file does not exist.
        /// </summary>
        public GSData Load() {
            if (!File.Exists(filePath)) {
                return new GSData();
            }

            try {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<GSData>(json) ?? new GSData();
            }
            catch (Exception) {
                // Optionally log the error using Playnite's logging
                // For example: Playnite.SDK.Logger.Error(ex, "Failed to load custom data");

                return new GSData();
            }
        }

        /// <summary>
        /// Saves the custom data to disk.
        /// </summary>
        public void Save(GSData data) {
            try {
                var json = JsonSerializer.Serialize(data);
                File.WriteAllText(filePath, json);
            }
            catch (Exception) {
                // Optionally log the error
                // For example: Playnite.SDK.Logger.Error(ex, "Failed to save custom data");
            }
        }
    }


    public static class GSDataManager {
        private static GSDataStorage storage;
        private static GSData data;

        /// <summary>
        /// Initializes the custom data manager.
        /// You must call this method (typically on plugin initialization)
        /// and pass in your plugin's user data folder.
        /// </summary>
        /// <param name="folderPath">The folder path where the custom data file will be stored.</param>
        public static void Initialize(string folderPath, string oldID) {
            storage = new GSDataStorage(folderPath);
            data = storage.Load();

            if (string.IsNullOrEmpty(oldID)) {
                if (string.IsNullOrEmpty(data.InstallID)) {
                    data.InstallID = Guid.NewGuid().ToString();
                    Save();
                }
            }
            else {
                data.InstallID = oldID;
                Save();
            }
            // If InstallID is missing or empty, create a new one and save.

        }

        /// <summary>
        /// Gets the current custom data.
        /// </summary>
        public static GSData Data => data;

        /// <summary>
        /// Saves the current custom data to disk.
        /// </summary>
        public static void Save() {
            storage?.Save(data);
        }
    }
}
