using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Xml;
using System.Text.Json;

namespace GsPlugin {
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
}
