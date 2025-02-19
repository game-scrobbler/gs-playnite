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
    public class GsData {
        public string InstallID { get; set; } = null;
        public string ActiveSessionId { get; set; } = null;
        public string Theme { get; set; } = "Dark";
        public string[] Flags { get; set; } = Array.Empty<string>();

        public void UpdateFlags(bool disableSentry, bool disableScrobbling) {
            var flagsList = new List<string>();
            if (disableSentry) flagsList.Add("no-sentry");
            if (disableScrobbling) flagsList.Add("no-scrobble");
            Flags = flagsList.ToArray();
        }
    }

    /// <summary>
    /// Static manager class for handling persistent data operations.
    /// </summary>
    public static class GsDataManager {
        /// <summary>
        /// The current data instance.
        /// </summary>
        private static GsData _data;

        /// <summary>
        /// Path to the data storage file.
        /// </summary>
        private static string _filePath;

        private static readonly ILogger _logger = LogManager.GetLogger();
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
            _filePath = Path.Combine(folderPath, "gs_data.json");
            _data = Load();

            try {
                if (string.IsNullOrEmpty(_data.InstallID)) {
                    if (!string.IsNullOrEmpty(oldID)) {
                        // Migrate InstallID from settings to GsData
                        _data.InstallID = oldID;
                        _logger.Info("Migrated InstallID from settings to GsData");
                        SentrySdk.AddBreadcrumb(
                            message: "Migrated InstallID from settings",
                            category: "migration",
                            data: new Dictionary<string, string> { { "InstallID", oldID } }
                        );
                    }
                    else {
                        // Generate new InstallID only if both GsData and settings are empty
                        _data.InstallID = Guid.NewGuid().ToString();
                        _logger.Info("Generated new InstallID");
                        SentrySdk.AddBreadcrumb(
                            message: "Generated new InstallID",
                            category: "initialization",
                            data: new Dictionary<string, string> { { "InstallID", _data.InstallID } }
                        );
                    }
                    Save();
                }
                else if (!string.IsNullOrEmpty(oldID) && _data.InstallID != oldID) {
                    // Log potential InstallID mismatch
                    _logger.Warn($"InstallID mismatch - GsData: {_data.InstallID}, Settings: {oldID}");
                    SentrySdk.CaptureMessage(
                        "InstallID mismatch detected",
                        scope => {
                            scope.Level = SentryLevel.Warning;
                            scope.SetExtra("GsDataInstallID", _data.InstallID);
                            scope.SetExtra("SettingsInstallID", oldID);
                        }
                    );
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Failed to initialize GsData");
                SentrySdk.CaptureException(ex);
                // Fallback to oldID or new GUID if initialization fails
                _data.InstallID = oldID ?? Guid.NewGuid().ToString();
                Save();
            }
        }

        /// <summary>
        /// Loads the custom data from disk.
        /// Returns a new instance if the file does not exist.
        /// </summary>
        private static GsData Load() {
            if (!File.Exists(_filePath)) {
                return new GsData();
            }

            try {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<GsData>(json, jsonOptions) ?? new GsData();
            }
            catch (Exception ex) {
                _logger.Error(ex, "Failed to load custom GsData");
                SentrySdk.CaptureException(ex, scope => {
                    scope.SetExtra("FilePath", _filePath);
                    if (File.Exists(_filePath)) {
                        scope.SetExtra("FileContent", File.ReadAllText(_filePath));
                    }
                });
                return new GsData();
            }
        }

        /// <summary>
        /// Saves the custom data to disk.
        /// </summary>
        public static void Save() {
            try {
                var json = JsonSerializer.Serialize(_data, jsonOptions);
#if DEBUG
                MessageBox.Show($"Saving the plugin data: {json}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex) {
                _logger.Error(ex, "Failed to save custom GsData");
#if DEBUG
                MessageBox.Show($"Failed to save the plugin data", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                SentrySdk.CaptureException(ex, scope => {
                    scope.SetExtra("FilePath", _filePath);
                    scope.SetExtra("AttemptedData", JsonSerializer.Serialize(_data));
                });
            }
        }

        /// <summary>
        /// Gets the current custom data.
        /// </summary>
        public static GsData Data => _data;
    }
}
