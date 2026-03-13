using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;

namespace GsPlugin.Models {
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
        // SHA-256 hex hash of the last achievement payload sent to the server.
        public string LastAchievementHash { get; set; } = null;
        // UTC time until which the server has asked us not to send library diffs.
        public DateTime? LibraryDiffSyncCooldownExpiresAt { get; set; } = null;
        // Hash of last-synced integration accounts (e.g. Steam UserId).
        // Forces a sync when a user links/switches accounts even if the library is unchanged.
        public string LastIntegrationAccountsHash { get; set; } = null;
        /// <summary>
        /// Global kill switch set when the user requests data deletion.
        /// Separate from Flags so that UpdateFlags() cannot accidentally clear it.
        /// </summary>
        public bool OptedOut { get; set; } = false;

        /// <summary>
        /// Monotonically increasing counter incremented each time the install identity is rotated.
        /// Written into gs_snapshot.json at save time; on load, GsSnapshotManager discards any
        /// snapshot whose generation does not match this value, making stale snapshots
        /// self-healing after a crash between the two-file rotation write sequence.
        /// </summary>
        public int IdentityGeneration { get; set; } = 0;

        /// <summary>
        /// Per-install authentication token issued by the server at registration.
        /// Stored as the raw 64-char hex token (never the hash).
        /// Sent in every write request as the x-playnite-token header.
        /// Null until /v2/register has been called successfully.
        /// </summary>
        public string InstallToken { get; set; } = null;

        /// <summary>
        /// Whether to show version update notifications in Playnite's notification tray.
        /// </summary>
        public bool ShowUpdateNotifications { get; set; } = true;

        /// <summary>
        /// Whether to show important server notifications in Playnite's notification tray.
        /// </summary>
        public bool ShowImportantNotifications { get; set; } = true;

        /// <summary>
        /// IDs of server notifications already shown in Playnite's tray.
        /// Prevents re-showing on restart. Capped at 100 entries.
        /// </summary>
        public List<string> ShownNotificationIds { get; set; } = new List<string>();

        public void UpdateFlags(bool disableSentry, bool disableScrobbling, bool disablePostHog = false) {
            Flags.Clear();
            if (disableSentry) Flags.Add("no-sentry");
            if (disableScrobbling) Flags.Add("no-scrobble");
            if (disablePostHog) Flags.Add("no-posthog");
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
                        // Generate new InstallID if not present (fresh install or corrupt/missing data file).
                        // Bumping IdentityGeneration ensures any surviving gs_snapshot.json is treated
                        // as stale and discarded by GsSnapshotManager.Initialize().
                        _data.InstallID = Guid.NewGuid().ToString();
                        _data.IdentityGeneration++;
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
                    // Fallback to new GUID if initialization fails; bump generation for same reason.
                    _data.InstallID = Guid.NewGuid().ToString();
                    _data.IdentityGeneration++;
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
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }
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
        /// Gets the current custom data, or null if not yet initialized.
        /// Use this in code paths that may run during initialization (e.g. Sentry).
        /// </summary>
        public static GsData DataOrNull => _data;

        /// <summary>
        /// Returns true if the user has opted out (requested data deletion).
        /// Safe to call before initialization (returns false).
        /// </summary>
        public static bool IsOptedOut => _data?.OptedOut == true;

        /// <summary>
        /// Transitions the plugin to the opted-out state: sets OptedOut flag,
        /// clears all session/sync/linking state, and persists to disk. Thread-safe.
        /// </summary>
        public static void PerformOptOut() {
            lock (_lock) {
                _data.OptedOut = true;
                _data.ActiveSessionId = null;
                _data.PendingStartGameId = null;
                _data.PendingScrobbles.Clear();
                _data.LinkedUserId = null;
                _data.LastLibraryHash = null;
                _data.LastAchievementHash = null;
                _data.LastSyncAt = null;
                _data.LastSyncGameCount = null;
                _data.SyncCooldownExpiresAt = null;
                _data.LibraryDiffSyncCooldownExpiresAt = null;
                _data.LastIntegrationAccountsHash = null;
                _data.InstallToken = null; // Token is invalidated server-side on opt-out
                SaveInternal();
            }
        }

        /// <summary>
        /// Clears the opted-out state so the plugin resumes normal operation.
        /// The user will need to re-link their account and sync their library again.
        /// </summary>
        public static void PerformOptIn() {
            lock (_lock) {
                _data.OptedOut = false;
                SaveInternal();
            }
        }

        /// <summary>
        /// Atomically appends notification IDs to ShownNotificationIds and persists.
        /// Trims to <paramref name="maxIds"/> to prevent unbounded growth. Thread-safe.
        /// </summary>
        public static void RecordShownNotifications(List<string> newIds, int maxIds) {
            if (newIds == null || newIds.Count == 0) return;
            lock (_lock) {
                foreach (var id in newIds) {
                    _data.ShownNotificationIds.Add(id);
                }
                if (_data.ShownNotificationIds.Count > maxIds) {
                    _data.ShownNotificationIds = _data.ShownNotificationIds
                        .Skip(_data.ShownNotificationIds.Count - maxIds)
                        .ToList();
                }
                SaveInternal();
            }
        }

        /// <summary>
        /// Returns a snapshot of ShownNotificationIds for filtering. Thread-safe.
        /// </summary>
        public static HashSet<string> GetShownNotificationIds() {
            lock (_lock) {
                return new HashSet<string>(_data.ShownNotificationIds);
            }
        }

        /// <summary>
        /// Atomically writes the install token only when the install is still active (not opted out).
        /// Returns true if the token was stored, false if the write was suppressed due to opt-out.
        /// Thread-safe: the opt-out check and the write happen under the same lock, eliminating the
        /// window between a lockless IsOptedOut check and a subsequent direct field assignment.
        /// </summary>
        public static bool SetInstallTokenIfActive(string token) {
            lock (_lock) {
                if (_data.OptedOut) {
                    return false;
                }
                _data.InstallToken = token;
                SaveInternal();
                return true;
            }
        }

        /// <summary>
        /// Rotates to a fresh InstallID and clears the stale InstallToken, then persists.
        /// Used for lost-token recovery: when the server reports PLAYNITE_TOKEN_ALREADY_REGISTERED
        /// and we have no local token, generating a new identity allows immediate re-registration
        /// without depending on the missing old token. Thread-safe.
        /// </summary>
        public static string RotateInstallId() {
            string newId;
            lock (_lock) {
                newId = Guid.NewGuid().ToString();
                _data.InstallID = newId;
                _data.InstallToken = null;
                _data.IdentityGeneration++;
                // Clear all identity-bound sync and linking state so the recovered install
                // cannot inherit stale cooldowns, hashes, baselines, queued work, or an
                // account link that belongs to the abandoned server-side identity.
                _data.LinkedUserId = null;
                _data.ActiveSessionId = null;
                _data.PendingStartGameId = null;
                _data.PendingScrobbles.Clear();
                _data.LastLibraryHash = null;
                _data.LastAchievementHash = null;
                _data.LastSyncAt = null;
                _data.LastSyncGameCount = null;
                _data.SyncCooldownExpiresAt = null;
                _data.LibraryDiffSyncCooldownExpiresAt = null;
                _data.LastIntegrationAccountsHash = null;
                _data.ShownNotificationIds.Clear();
                SaveInternal();
                GsLogger.Info("InstallID rotated for lost-token recovery; identity-bound state cleared");
            }
            // Reset snapshot outside the data lock (each manager has its own lock).
            GsSnapshotManager.Reset();
            return newId;
        }

        /// <summary>
        /// Returns the install ID to include in new outbound request bodies, or null when a token
        /// is present. When x-playnite-token is sent the server resolves identity from the token,
        /// so including the UUID in the body is redundant and re-exposes it in request payloads.
        /// Note: pending scrobble DTOs already have user_id baked in at queue time, so omitting it
        /// here does not affect replay of previously serialized work.
        /// </summary>
        public static string InstallIdForBody =>
            string.IsNullOrEmpty(_data?.InstallToken) ? _data?.InstallID : null;

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
