using System;
using System.IO;
using System.Text.Json;
using Playnite.SDK;

namespace GsPlugin {
    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GSData {
        public string InstallID { get; set; } = null;
        public string SessionId { get; set; } = null;
        public bool IsDark { get; set; } = true;
    }

    /// <summary>
    /// Handles loading and saving CustomData to a JSON file.
    /// </summary>
    public class GSDataStorage {
        private readonly string filePath;
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

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
                return JsonSerializer.Deserialize<GSData>(json, jsonOptions) ?? new GSData();
            }
            catch (Exception ex) {
                logger.Error(ex, "Failed to load custom GS data");
                return new GSData();
            }
        }

        /// <summary>
        /// Saves the custom data to disk.
        /// </summary>
        public void Save(GSData data) {
            try {
                var json = JsonSerializer.Serialize(data, jsonOptions);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex) {
                logger.Error(ex, "Failed to save custom GS data");
            }
        }
    }

    public static class GSDataManager {
        private static GSDataStorage storage;
        private static GSData data;
        private static readonly ILogger logger = LogManager.GetLogger();

        /// <summary>
        /// Initializes the custom data manager.
        /// You must call this method (typically on plugin initialization)
        /// and pass in your plugin's user data folder.
        /// </summary>
        /// <param name="folderPath">The folder path where the custom data file will be stored.</param>
        public static void Initialize(string folderPath, string oldID) {
            storage = new GSDataStorage(folderPath);
            data = storage.Load();

            if (string.IsNullOrEmpty(data.InstallID)) {
                if (string.IsNullOrEmpty(oldID)) {
                    data.InstallID = Guid.NewGuid().ToString();
                    logger.Info("Generated new GS InstallID.");
                } else {
                    data.InstallID = oldID;
                    logger.Info("Converted oldID to new GS InstallID.");
                }
                Save();
            }
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
