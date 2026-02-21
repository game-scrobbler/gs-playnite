using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sentry;

namespace GsPlugin {
    /// <summary>
    /// Represents a scrobble request that failed to send and is waiting to be retried.
    /// </summary>
    public class PendingScrobble {
        public string Type { get; set; }
        public GsApiClient.ScrobbleStartReq StartData { get; set; }
        public GsApiClient.ScrobbleFinishReq FinishData { get; set; }
        public DateTime QueuedAt { get; set; }
        /// <summary>
        /// Number of times this item has been through FlushPendingScrobblesAsync without success.
        /// Items are permanently dropped once this reaches the max flush attempts threshold.
        /// </summary>
        public int FlushAttempts { get; set; }
    }

    /// <summary>
    /// Holds custom persistent data.
    /// </summary>
    public class GsData {
        /// <summary>
        /// Sentinel value returned by the API when an account is not linked.
        /// </summary>
        public const string NotLinkedValue = "not_linked";

        public string InstallID { get; set; } = null;
        public string ActiveSessionId { get; set; } = null;
        /// <summary>
        /// Game ID whose start scrobble was queued (failed to send).
        /// Used by OnGameStoppedAsync to pair a finish with the pending start.
        /// Cleared once the finish is queued or when the start succeeds.
        /// </summary>
        public string PendingStartGameId { get; set; } = null;
        public string Theme { get; set; } = "Dark";
        public List<string> Flags { get; set; } = new List<string>();
        public string LinkedUserId { get; set; } = null;
        public bool NewDashboardExperience { get; set; } = false;
        public bool SyncAchievements { get; set; } = true;
        public List<string> AllowedPlugins { get; set; } = new List<string>();
        public DateTime? AllowedPluginsLastFetched { get; set; }
        public List<PendingScrobble> PendingScrobbles { get; set; } = new List<PendingScrobble>();
        public string LastNotifiedVersion { get; set; } = null;
        public DateTime? LastSyncAt { get; set; } = null;
        public int? LastSyncGameCount { get; set; } = null;
        // UTC time until which the server has asked us not to sync again (24-hour cooldown).
        public DateTime? SyncCooldownExpiresAt { get; set; } = null;
        // SHA-256 hex hash of the last library payload sent to the server.
        // Used to skip syncs when the library hasn't changed between sessions.
        public string LastLibraryHash { get; set; } = null;
        // UTC time until which the server has asked us not to send library diffs.
        public DateTime? LibraryDiffSyncCooldownExpiresAt { get; set; } = null;

        public void UpdateFlags(bool disableSentry, bool disableScrobbling) {
            Flags.Clear();
            if (disableSentry) Flags.Add("no-sentry");
            if (disableScrobbling) Flags.Add("no-scrobble");
        }
    }

    /// <summary>
    /// Utility methods for formatting time spans as human-readable strings.
    /// </summary>
    public static class GsTime {
        /// <summary>
        /// Formats an elapsed <see cref="TimeSpan"/> as a past-tense string, e.g. "just now", "5 minutes ago", "2 hours ago", "3 days ago".
        /// </summary>
        public static string FormatElapsed(TimeSpan elapsed) {
            if (elapsed.TotalMinutes < 1)
                return "just now";
            if (elapsed.TotalHours < 1) {
                int mins = (int)elapsed.TotalMinutes;
                return $"{mins} minute{(mins == 1 ? "" : "s")} ago";
            }
            if (elapsed.TotalDays < 1) {
                int hours = (int)elapsed.TotalHours;
                return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
            }
            int days = (int)elapsed.TotalDays;
            return $"{days} day{(days == 1 ? "" : "s")} ago";
        }

        /// <summary>
        /// Formats a remaining <see cref="TimeSpan"/> as a future-tense string, e.g. "less than a minute", "45 minutes", "2 hours", "1 hour 30 minutes".
        /// </summary>
        public static string FormatRemaining(TimeSpan remaining) {
            if (remaining.TotalMinutes < 1)
                return "less than a minute";
            if (remaining.TotalHours < 1) {
                int mins = (int)remaining.TotalMinutes;
                return $"{mins} minute{(mins == 1 ? "" : "s")}";
            }
            int hours = (int)remaining.TotalHours;
            int remMins = remaining.Minutes;
            return remMins > 0
                ? $"{hours} hour{(hours == 1 ? "" : "s")} {remMins} minute{(remMins == 1 ? "" : "s")}"
                : $"{hours} hour{(hours == 1 ? "" : "s")}";
        }
    }

    /// <summary>
    /// Static manager class for handling persistent data operations.
    /// Thread-safe: all access to _data is synchronized via _lock.
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

        /// <summary>
        /// Lock object for thread-safe access to _data and file operations.
        /// </summary>
        private static readonly object _lock = new object();

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
            lock (_lock) {
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
                        SaveInternal();
                    }
                }
                catch (Exception ex) {
                    GsLogger.Error("Failed to initialize GsData", ex);
                    GsSentry.CaptureException(ex, "Failed to initialize GsData");
                    // Fallback to new GUID if initialization fails
                    _data.InstallID = Guid.NewGuid().ToString();
                    SaveInternal();
                }
            }
        }

        /// <summary>
        /// Loads the custom data from disk.
        /// Returns a new instance if the file does not exist.
        /// Must be called under _lock.
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
        /// Saves the custom data to disk. Thread-safe.
        /// </summary>
        public static void Save() {
            lock (_lock) {
                SaveInternal();
            }
        }

        /// <summary>
        /// Internal save implementation. Must be called under _lock.
        /// </summary>
        private static void SaveInternal() {
            try {
                var json = JsonSerializer.Serialize(_data, jsonOptions);
                GsLogger.Info("Saving plugin data to disk");
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to save custom GsData", ex);
                GsSentry.CaptureException(ex, "Failed to save GsData to disk");
            }
        }

        /// <summary>
        /// Gets the current custom data.
        /// Throws if Initialize() has not been called.
        /// </summary>
        public static GsData Data {
            get {
                if (_data == null) {
                    throw new InvalidOperationException("GsDataManager not initialized. Call Initialize() first.");
                }
                return _data;
            }
        }

        /// <summary>
        /// Returns true if an account is linked (LinkedUserId is set and not the "not_linked" sentinel).
        /// </summary>
        public static bool IsAccountLinked =>
            !string.IsNullOrEmpty(Data?.LinkedUserId) && Data.LinkedUserId != GsData.NotLinkedValue;

        /// <summary>
        /// Adds a pending scrobble to the queue and persists it. Thread-safe.
        /// </summary>
        public static void EnqueuePendingScrobble(PendingScrobble item) {
            lock (_lock) {
                _data.PendingScrobbles.Add(item);
                SaveInternal();
            }
        }

        /// <summary>
        /// Atomically removes and returns all pending scrobbles from the queue. Thread-safe.
        /// </summary>
        public static List<PendingScrobble> DequeuePendingScrobbles() {
            lock (_lock) {
                var snapshot = new List<PendingScrobble>(_data.PendingScrobbles);
                _data.PendingScrobbles.Clear();
                SaveInternal();
                return snapshot;
            }
        }
    }
}
