using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sentry;

namespace GsPlugin {
    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GsData {
        public string InstallID { get; set; } = null;
        public string ActiveSessionId { get; set; } = null;
        public string Theme { get; set; } = "Dark";
        public List<string> Flags { get; set; } = new List<string>();
        public string LinkedUserId { get; set; } = null;
        public bool NewDashboardExperience { get; set; } = false;
        public List<string> AllowedPlugins { get; set; } = new List<string>();
        public DateTime? AllowedPluginsLastFetched { get; set; }

        public void UpdateFlags(bool disableSentry, bool disableScrobbling) {
            Flags.Clear();
            if (disableSentry) Flags.Add("no-sentry");
            if (disableScrobbling) Flags.Add("no-scrobble");
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

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        /// <summary>
        /// Initializes the custom data manager.
        /// You must call this method (typically on plugin initialization)
        /// and pass in your plugin's user data folder.
        /// </summary>
        /// <param name="folderPath">The folder path where the custom data file will be stored.</param>
        /// <param name="oldID">Legacy parameter - no longer used as InstallID is exclusively managed by GsData.</param>
        public static void Initialize(string folderPath, string oldID) {
            _filePath = Path.Combine(folderPath, "gs_data.json");
            _data = Load();

            try {
                if (string.IsNullOrEmpty(_data.InstallID)) {
                    // Generate new InstallID if not present
                    _data.InstallID = Guid.NewGuid().ToString();
                    GsLogger.Info("Generated new InstallID");
                    GsSentry.AddBreadcrumb(
                        message: "Generated new InstallID",
                        category: "initialization",
                        data: new Dictionary<string, string> { { "InstallID", _data.InstallID } }
                    );
                    Save();
                }
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to initialize GsData", ex);
                GsSentry.CaptureException(ex, "Failed to initialize GsData");
                // Fallback to new GUID if initialization fails
                _data.InstallID = Guid.NewGuid().ToString();
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
                GsLogger.Error("Failed to load custom GsData", ex);
                GsSentry.CaptureException(ex, "Failed to load GsData from disk");
                return new GsData();
            }
        }

        /// <summary>
        /// Saves the custom data to disk.
        /// </summary>
        public static void Save() {
            try {
                var json = JsonSerializer.Serialize(_data, jsonOptions);
                GsLogger.Info($"Saving plugin data: {json}");
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to save custom GsData", ex);
                GsSentry.CaptureException(ex, "Failed to save GsData to disk");
            }
        }

        /// <summary>
        /// Gets the current custom data.
        /// </summary>
        public static GsData Data => _data;
    }
}
