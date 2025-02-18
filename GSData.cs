using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Playnite.SDK;
using Sentry;
using System.Collections.Generic;

namespace GsPlugin {
    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GSData {
        public string InstallID { get; set; } = null;
        public string SessionId { get; set; } = null;
        public string Theme { get; set; } = "Dark";
    }

    /// <summary>
    /// Static manager class for handling persistent data operations.
    /// </summary>
    public static class GSDataManager {
        /// <summary>
        /// The current data instance.
        /// </summary>
        private static GSData data;

        /// <summary>
        /// Path to the data storage file.
        /// </summary>
        private static string filePath;

        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        /// <summary>
        /// Initializes the custom data manager.
        /// You must call this method (typically on plugin initialization)
        /// and pass in your plugin's user data folder.
        /// </summary>
        /// <param name="folderPath">The folder path where the custom data file will be stored.</param>
        public static void Initialize(string folderPath, string oldID) {
            filePath = Path.Combine(folderPath, "customData.json");
            data = Load();

            try {
                if (string.IsNullOrEmpty(data.InstallID)) {
                    if (!string.IsNullOrEmpty(oldID)) {
                        // Migrate InstallID from settings to GSData
                        data.InstallID = oldID;
                        logger.Info("Migrated InstallID from settings to GSData");
                        SentrySdk.AddBreadcrumb(
                            message: "Migrated InstallID from settings",
                            category: "migration",
                            data: new Dictionary<string, string> { { "InstallID", oldID } }
                        );
                    }
                    else {
                        // Generate new InstallID only if both GSData and settings are empty
                        data.InstallID = Guid.NewGuid().ToString();
                        logger.Info("Generated new InstallID");
                        SentrySdk.AddBreadcrumb(
                            message: "Generated new InstallID",
                            category: "initialization",
                            data: new Dictionary<string, string> { { "InstallID", data.InstallID } }
                        );
                    }
                    Save();
                }
                else if (!string.IsNullOrEmpty(oldID) && data.InstallID != oldID) {
                    // Log potential InstallID mismatch
                    logger.Warning($"InstallID mismatch - GSData: {data.InstallID}, Settings: {oldID}");
                    SentrySdk.CaptureMessage(
                        "InstallID mismatch detected",
                        scope => {
                            scope.Level = SentryLevel.Warning;
                            scope.SetExtra("GSDataInstallID", data.InstallID);
                            scope.SetExtra("SettingsInstallID", oldID);
                        }
                    );
                }
            }
            catch (Exception ex) {
                logger.Error(ex, "Failed to initialize GSData");
                SentrySdk.CaptureException(ex);
                // Fallback to oldID or new GUID if initialization fails
                data.InstallID = oldID ?? Guid.NewGuid().ToString();
                Save();
            }
        }

        /// <summary>
        /// Loads the custom data from disk.
        /// Returns a new instance if the file does not exist.
        /// </summary>
        private static GSData Load() {
            if (!File.Exists(filePath)) {
                return new GSData();
            }

            try {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<GSData>(json, jsonOptions) ?? new GSData();
            }
            catch (Exception ex) {
                logger.Error(ex, "Failed to load custom GS data");
                SentrySdk.CaptureException(ex, scope => {
                    scope.SetExtra("FilePath", filePath);
                    if (File.Exists(filePath)) {
                        scope.SetExtra("FileContent", File.ReadAllText(filePath));
                    }
                });
                return new GSData();
            }
        }

        /// <summary>
        /// Saves the custom data to disk.
        /// </summary>
        public static void Save() {
            try {
                var json = JsonSerializer.Serialize(data, jsonOptions);
#if DEBUG
                MessageBox.Show($"Saving the plugin data: {json}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex) {
                logger.Error(ex, "Failed to save custom GS data");
#if DEBUG
                MessageBox.Show($"Failed to save the plugin data", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                SentrySdk.CaptureException(ex, scope => {
                    scope.SetExtra("FilePath", filePath);
                    scope.SetExtra("AttemptedData", JsonSerializer.Serialize(data));
                });
            }
        }

        /// <summary>
        /// Gets the current custom data.
        /// </summary>
        public static GSData Data => data;
    }
}
