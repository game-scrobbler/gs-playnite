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
        public ScrobbleStartReq StartData { get; set; }
        public ScrobbleFinishReq FinishData { get; set; }
        public DateTime QueuedAt { get; set; }
        /// <summary>
        /// Number of times this item has been through FlushPendingScrobblesAsync without success.
        /// Items are permanently dropped once this reaches the max flush attempts threshold.
        /// </summary>
        public int FlushAttempts { get; set; }
    }

    /// <summary>
    /// Selects the optional extras cleared by <see cref="GsData.ClearIdentityBoundState"/>
    /// on top of the base set that every identity reset clears.
    /// Declared at namespace level (not nested) so it stays off the JSON-serialized shape of
    /// <see cref="GsData"/>.
    /// </summary>
    [Flags]
    public enum IdentityClearScope {
        /// <summary>Clear only the base set: link, sessions, queued scrobbles, sync hashes and cooldowns.</summary>
        None = 0,
        /// <summary>
        /// Also clear <see cref="GsData.InstallToken"/>. Used when the install identity itself
        /// goes away (opt-out invalidates the token server-side, rotation abandons it).
        /// Not used by unlink, where the install stays registered under the same token.
        /// </summary>
        InstallToken = 1 << 0,
        /// <summary>
        /// Also clear <see cref="GsData.ShownNotificationIds"/> so the new identity can be
        /// shown notifications it has already seen under the old one.
        /// </summary>
        ShownNotifications = 1 << 1
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
        /// <summary>
        /// Active scrobble session IDs keyed by Playnite game ID. Playnite allows
        /// multiple games to run simultaneously, so a single shared field would let
        /// one game's stop event finish a different game's session.
        /// </summary>
        public Dictionary<string, string> ActiveSessionsByGameId { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// Game IDs whose start scrobble was queued (failed to send).
        /// Used by OnGameStoppedAsync to pair a finish with the pending start.
        /// An entry is removed once the finish is queued or when the start succeeds.
        /// </summary>
        public List<string> PendingStartGameIds { get; set; } = new List<string>();
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
        /// Written into gs_library_hashes.json / gs_achievement_hashes.json at save time; on load,
        /// GsSyncHashIndex discards any index whose generation does not match this value,
        /// making stale baselines self-healing after a crash between the two-file rotation write.
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

        /// <summary>
        /// Number of scrobbles permanently dropped due to repeated flush failures.
        /// Displayed in the settings diagnostics section. Reset on successful flush or manual sync.
        /// </summary>
        public int DroppedScrobbleCount { get; set; } = 0;

        internal GsData CreateRollbackSnapshot() {
            var copy = (GsData)MemberwiseClone();
            copy.ActiveSessionsByGameId = new Dictionary<string, string>(ActiveSessionsByGameId);
            copy.PendingStartGameIds = new List<string>(PendingStartGameIds);
            copy.PendingScrobbles = new List<PendingScrobble>(PendingScrobbles);
            copy.Flags = new List<string>(Flags);
            copy.AllowedPlugins = new List<string>(AllowedPlugins);
            copy.ShownNotificationIds = new List<string>(ShownNotificationIds);
            return copy;
        }

        /// <summary>
        /// Clears the state bound to the current install/account identity so it cannot bleed into
        /// the next one. Single source of truth for opt-out, install-id rotation and account
        /// unlink; add any new identity-bound field here rather than at the call sites.
        /// </summary>
        /// <remarks>
        /// Neither locks nor saves, so it is safe to call from inside GsDataManager's lock
        /// (PerformOptOut / RotateInstallId) and from within a MutateAndSave action (unlink).
        /// Callers are responsible for persisting afterwards.
        /// Collections are cleared in place rather than reassigned because other code holds
        /// references to the same instances.
        /// </remarks>
        /// <param name="scope">Extras to clear beyond the base set.</param>
        public void ClearIdentityBoundState(IdentityClearScope scope) {
            LinkedUserId = null;
            ActiveSessionsByGameId.Clear();
            PendingStartGameIds.Clear();
            PendingScrobbles.Clear();
            LastLibraryHash = null;
            LastAchievementHash = null;
            LastSyncAt = null;
            LastSyncGameCount = null;
            SyncCooldownExpiresAt = null;
            LibraryDiffSyncCooldownExpiresAt = null;
            LastIntegrationAccountsHash = null;

            if ((scope & IdentityClearScope.InstallToken) != 0) {
                InstallToken = null;
            }

            // Asymmetry preserved from the three hand-written copies this method replaced:
            // only install-id rotation cleared ShownNotificationIds, and none of them cleared
            // DroppedScrobbleCount (a lifetime diagnostics counter, not identity-bound state).
            // Both are recorded as observed behavior, not as a deliberate design conclusion;
            // a future reader should decide intentionally rather than assume it was reasoned.
            if ((scope & IdentityClearScope.ShownNotifications) != 0) {
                ShownNotificationIds.Clear();
            }
        }

        public void UpdateFlags(bool disableSentry, bool disableScrobbling, bool disablePostHog = false) {
            // Build a new list and swap atomically to avoid IndexOutOfRangeException
            // when concurrent readers iterate via Flags.Contains() without a lock.
            var newFlags = new List<string>();
            if (disableSentry) newFlags.Add("no-sentry");
            if (disableScrobbling) newFlags.Add("no-scrobble");
            if (disablePostHog) newFlags.Add("no-posthog");
            Flags = newFlags;
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
                return GsLocalization.Get("LOCGsPluginElapsedJustNow", "just now");
            if (elapsed.TotalHours < 1) {
                int mins = (int)elapsed.TotalMinutes;
                return GsLocalization.Format("LOCGsPluginElapsedMinutesFormat",
                    $"{mins} minute{(mins == 1 ? "" : "s")} ago", mins);
            }
            if (elapsed.TotalDays < 1) {
                int hours = (int)elapsed.TotalHours;
                return GsLocalization.Format("LOCGsPluginElapsedHoursFormat",
                    $"{hours} hour{(hours == 1 ? "" : "s")} ago", hours);
            }
            int days = (int)elapsed.TotalDays;
            return GsLocalization.Format("LOCGsPluginElapsedDaysFormat",
                $"{days} day{(days == 1 ? "" : "s")} ago", days);
        }

        /// <summary>
        /// Formats a remaining <see cref="TimeSpan"/> as a future-tense string, e.g. "less than a minute", "45 minutes", "2 hours", "1 hour 30 minutes".
        /// </summary>
        public static string FormatRemaining(TimeSpan remaining) {
            if (remaining.TotalMinutes < 1)
                return GsLocalization.Get("LOCGsPluginRemainingLessThanMinute", "less than a minute");
            if (remaining.TotalHours < 1) {
                int mins = (int)remaining.TotalMinutes;
                return GsLocalization.Format("LOCGsPluginRemainingMinutesFormat",
                    $"{mins} minute{(mins == 1 ? "" : "s")}", mins);
            }
            int hours = (int)remaining.TotalHours;
            int remMins = remaining.Minutes;
            return remMins > 0
                ? GsLocalization.Format("LOCGsPluginRemainingHoursMinutesFormat",
                    $"{hours} hour{(hours == 1 ? "" : "s")} {remMins} minute{(remMins == 1 ? "" : "s")}", hours, remMins)
                : GsLocalization.Format("LOCGsPluginRemainingHoursFormat",
                    $"{hours} hour{(hours == 1 ? "" : "s")}", hours);
        }
    }

    /// <summary>
    /// Static manager class for handling persistent data operations.
    /// Thread-safe: all access to _data is synchronized via _lock.
    /// </summary>
    public static class GsDataManager {
        /// <summary>
        /// Raised when install-token or pending-scrobble state changes.
        /// Settings UI subscribes to keep diagnostics indicators fresh.
        /// Fired outside the lock so handlers must not call back into GsDataManager under lock.
        /// </summary>
        public static event EventHandler DiagnosticsStateChanged;

        /// <summary>
        /// Manually fires DiagnosticsStateChanged for callers that modify
        /// diagnostics-visible state via MutateAndSave (which does not auto-fire).
        /// </summary>
        public static void NotifyDiagnosticsChanged() {
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
        }

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
        // Live event handlers own these durable queue items until their HTTP attempt ends.
        // Claims are process-local: after a crash all persisted items become replayable.
        private static readonly HashSet<PendingScrobble> _claimedScrobbles = new HashSet<PendingScrobble>();

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
                // Never leave an earlier identity usable after a failed initialization.
                // In particular, an unreadable file is not permission to create a new install.
                _data = null;
                _claimedScrobbles.Clear();
                try {
                    var loaded = Load();
                    if (string.IsNullOrEmpty(loaded.InstallID)) {
                        loaded.InstallID = Guid.NewGuid().ToString();
                        loaded.IdentityGeneration++;
                        Directory.CreateDirectory(folderPath);
                        GsAtomicFile.WriteJson(_filePath, loaded, jsonOptions);
                        GsLogger.Info("Generated new InstallID");
                    }
                    _data = loaded;
                }
                catch (Exception ex) {
                    // Consent is in the unreadable file, so report locally only. Throwing stops
                    // plugin construction before registration, telemetry, or sync can run.
                    _filePath = null;
                    GsLogger.Error("Cannot load plugin data; preserving the existing file and stopping initialization", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Loads the custom data from disk.
        /// Returns a new instance if the file does not exist.
        /// Must be called under _lock.
        /// </summary>
        private static GsData Load() {
            GsAtomicFile.RecoverTemp(_filePath);

            for (var attempt = 0; ; attempt++) {
                try {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<GsData>(json, jsonOptions);
                    if (data == null) {
                        throw new JsonException("Plugin data must contain an object, not null.");
                    }
                    MigrateLegacySessionFields(data, json);
                    return data;
                }
                catch (FileNotFoundException) when (!File.Exists(_filePath + ".tmp")) {
                    return new GsData();
                }
                catch (DirectoryNotFoundException) {
                    return new GsData();
                }
                catch (Exception ex) when (attempt < 2 &&
                    (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)) {
                    System.Threading.Thread.Sleep(50 * (attempt + 1));
                }
            }
        }

        /// <summary>
        /// One-time upgrade path from plugin versions that tracked a single scrobble session as
        /// scalar fields (ActiveSessionId, PendingStartGameId) instead of per-game collections.
        /// System.Text.Json silently ignores unmapped JSON members on deserialize, so without this
        /// an in-flight session or queued start present at the moment of upgrade would otherwise be
        /// dropped with no trace.
        /// </summary>
        private static void MigrateLegacySessionFields(GsData data, string json) {
            try {
                using (var doc = JsonDocument.Parse(json)) {
                    var root = doc.RootElement;

                    // PendingStartGameId (singular) was itself a bare game ID, so it migrates
                    // 1:1 into the new per-game list.
                    if (root.TryGetProperty("PendingStartGameId", out var pendingStartEl)
                        && pendingStartEl.ValueKind == JsonValueKind.String) {
                        var legacyGameId = pendingStartEl.GetString();
                        if (!string.IsNullOrEmpty(legacyGameId) && !data.PendingStartGameIds.Contains(legacyGameId)) {
                            data.PendingStartGameIds.Add(legacyGameId);
                            GsLogger.Info("Migrated legacy PendingStartGameId into PendingStartGameIds");
                        }
                    }

                    // ActiveSessionId (singular) had no per-game key, so it cannot be mapped onto
                    // the new per-game dictionary without fabricating a game ID. Rather than
                    // silently dropping it, surface it so we have visibility into how often an
                    // in-flight session gets orphaned by an upgrade.
                    if (root.TryGetProperty("ActiveSessionId", out var activeSessionIdEl)
                        && activeSessionIdEl.ValueKind == JsonValueKind.String
                        && !string.IsNullOrEmpty(activeSessionIdEl.GetString())) {
                        GsLogger.Warn("Found legacy ActiveSessionId with no game association; " +
                            "this in-flight session cannot be migrated and will not be finished.");
                        GsSentry.AddBreadcrumb(
                            message: "Legacy ActiveSessionId orphaned during upgrade",
                            category: "migration"
                        );
                    }
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to inspect legacy session fields: {ex.Message}");
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
        /// Atomically mutates the data and persists it under a single lock acquisition.
        /// Use this instead of directly modifying <see cref="Data"/> properties followed by <see cref="Save()"/>
        /// to prevent concurrent threads from interleaving mutations.
        /// </summary>
        /// <param name="action">Action that modifies the <see cref="GsData"/> instance.</param>
        public static void MutateAndSave(Action<GsData> action) {
            lock (_lock) {
                action(Data);
                SaveInternal();
            }
        }

        /// <summary>Rejects responses that belong to a deleted or replaced installation.</summary>
        public static bool TryMutateIfActiveIdentity(string expectedInstallId, int expectedGeneration, Action<GsData> action) {
            bool saved;
            lock (_lock) {
                if (_data == null || _data.OptedOut || _data.InstallID != expectedInstallId
                    || _data.IdentityGeneration != expectedGeneration) {
                    return false;
                }
                saved = PersistMutation(action);
            }
            if (saved) NotifyDiagnosticsChanged();
            return saved;
        }

        // Preserve queue item identity across rollback: an in-flight sender holds those same
        // objects. Only the session ID of a paired finish is edited by these transactions.
        private static bool PersistMutation(Action<GsData> action) {
            var before = _data;
            var claims = new HashSet<PendingScrobble>(_claimedScrobbles);
            var finishIds = _data.PendingScrobbles.Where(p => p.FinishData != null)
                .Select(p => p.FinishData).Distinct().ToDictionary(f => f, f => f.session_id);
            bool saved = false;
            try {
                // Work on copied collections so a failed write preserves existing references
                // as well as their contents. Pending items stay shared for live/replay pairing.
                _data = before.CreateRollbackSnapshot();
                action(_data);
                saved = SaveInternal();
                return saved;
            }
            finally {
                if (!saved) {
                    _data = before;
                    foreach (var finish in finishIds) finish.Key.session_id = finish.Value;
                    _claimedScrobbles.Clear();
                    _claimedScrobbles.UnionWith(claims);
                }
            }
        }

        private static bool MutatePendingScrobble(PendingScrobble item, Action<GsData> action) {
            bool saved;
            lock (_lock) {
                if (_data == null || _data.OptedOut || !_data.PendingScrobbles.Contains(item)) return false;
                saved = PersistMutation(action);
            }
            if (saved) NotifyDiagnosticsChanged();
            return saved;
        }

        /// <summary>
        /// Persists every shutdown finish and removes only the corresponding active sessions
        /// in one write. On a failed write the original collections remain available for retry.
        /// </summary>
        public static bool QueueSessionFinishesAndClearActive(Dictionary<string, string> sessions, List<PendingScrobble> finishes,
            string expectedInstallId = null, int? expectedGeneration = null) {
            bool saved;
            lock (_lock) {
                if (_data == null || _data.OptedOut) return false;
                if ((expectedInstallId != null && _data.InstallID != expectedInstallId)
                    || (expectedGeneration.HasValue && _data.IdentityGeneration != expectedGeneration.Value)) return false;
                saved = PersistMutation(d => {
                    foreach (var pending in finishes) {
                        var finish = pending.FinishData;
                        if (string.IsNullOrEmpty(finish.session_id)
                            && !d.PendingScrobbles.Any(p => p.Type == "start"
                                && IsSameGame(p, finish.game_id, finish.plugin_id))) {
                            // A start can complete after shutdown takes its active-session
                            // snapshot. Resolve that response within the same durable write.
                            if (d.ActiveSessionsByGameId.TryGetValue(finish.game_id, out var completedSession)) {
                                finish.session_id = completedSession;
                            }
                            else if (!d.PendingStartGameIds.Contains(finish.game_id)) {
                                continue; // The start was already dropped or the game stopped.
                            }
                        }
                        d.PendingScrobbles.Add(pending);
                        d.PendingStartGameIds.Remove(finish.game_id);
                        if (!string.IsNullOrEmpty(finish.session_id)
                            && d.ActiveSessionsByGameId.TryGetValue(finish.game_id, out var active)
                            && active == finish.session_id) {
                            d.ActiveSessionsByGameId.Remove(finish.game_id);
                        }
                    }
                    foreach (var session in sessions) {
                        if (d.ActiveSessionsByGameId.TryGetValue(session.Key, out var current)
                            && current == session.Value) {
                            d.ActiveSessionsByGameId.Remove(session.Key);
                        }
                    }
                });
            }
            if (saved) NotifyDiagnosticsChanged();
            return saved;
        }

        /// <summary>
        /// Returns true if there is a recorded active scrobble session for the given game ID. Thread-safe.
        /// </summary>
        public static bool HasActiveSession(string gameId) {
            lock (_lock) {
                return !string.IsNullOrEmpty(gameId) && _data.ActiveSessionsByGameId.ContainsKey(gameId);
            }
        }

        /// <summary>
        /// Attempts to get the active scrobble session ID for the given game ID. Thread-safe.
        /// </summary>
        public static bool TryGetActiveSession(string gameId, out string sessionId) {
            lock (_lock) {
                if (string.IsNullOrEmpty(gameId)) {
                    sessionId = null;
                    return false;
                }
                return _data.ActiveSessionsByGameId.TryGetValue(gameId, out sessionId);
            }
        }

        /// <summary>
        /// Returns a point-in-time copy of all active sessions keyed by game ID. Thread-safe.
        /// </summary>
        public static Dictionary<string, string> SnapshotActiveSessions() {
            lock (_lock) {
                return new Dictionary<string, string>(_data.ActiveSessionsByGameId);
            }
        }

        /// <summary>
        /// Returns true if the given game ID has a pending (queued) start scrobble. Thread-safe.
        /// </summary>
        public static bool HasPendingStart(string gameId) {
            lock (_lock) {
                return !string.IsNullOrEmpty(gameId) && _data.PendingStartGameIds.Contains(gameId);
            }
        }

        /// <summary>
        /// Internal save implementation. Must be called under _lock.
        /// </summary>
        private static bool SaveInternal() {
            if (_data == null || string.IsNullOrEmpty(_filePath)) return false;
            try {
                var dir = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }
                GsLogger.Info("Saving plugin data to disk");
                GsAtomicFile.WriteJson(_filePath, _data, jsonOptions);
                return true;
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to save custom GsData", ex);
                GsSentry.CaptureException(ex, "Failed to save GsData to disk");
                return false;
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
                _data.IdentityGeneration++;
                // Token is invalidated server-side on opt-out, so clear it too.
                _data.ClearIdentityBoundState(IdentityClearScope.InstallToken);
                SaveInternal();
            }
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
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
            bool stored;
            lock (_lock) {
                if (_data.OptedOut) {
                    return false;
                }
                _data.InstallToken = token;
                SaveInternal();
                stored = true;
            }
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
            return stored;
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
                _data.IdentityGeneration++;
                // Clear all identity-bound sync and linking state so the recovered install
                // cannot inherit stale cooldowns, hashes, baselines, queued work, or an
                // account link that belongs to the abandoned server-side identity.
                _data.ClearIdentityBoundState(
                    IdentityClearScope.InstallToken | IdentityClearScope.ShownNotifications);
                SaveInternal();
                GsLogger.Info("InstallID rotated for lost-token recovery; identity-bound state cleared");
            }
            // Reset hash index outside the data lock (each manager has its own lock).
            GsSyncHashIndex.Reset();
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
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
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Returns a snapshot of the pending scrobble queue without removing items. Thread-safe.
        /// Use with <see cref="RemovePendingScrobble"/> for crash-safe flush: items remain on disk
        /// until each one is confirmed sent, so a mid-flush crash loses nothing.
        /// </summary>
        public static List<PendingScrobble> PeekPendingScrobbles() {
            lock (_lock) {
                // A claimed start can already have a durable matching finish behind it.
                // Replay only the available prefix, so that finish cannot overtake its start.
                return _data.PendingScrobbles.TakeWhile(item => !_claimedScrobbles.Contains(item)).ToList();
            }
        }

        public static void ClaimPendingScrobble(PendingScrobble item) {
            lock (_lock) {
                _claimedScrobbles.Add(item);
            }
        }

        public static void ReleasePendingScrobble(PendingScrobble item) {
            lock (_lock) {
                _claimedScrobbles.Remove(item);
            }
        }

        private static bool IsSameGame(PendingScrobble item, string gameId, string pluginId) =>
            item.Type == "start"
                ? item.StartData?.game_id == gameId && item.StartData?.plugin_id == pluginId
                : item.FinishData?.game_id == gameId && item.FinishData?.plugin_id == pluginId;

        public static bool HasEarlierPendingScrobble(PendingScrobble item) {
            lock (_lock) {
                var index = _data.PendingScrobbles.IndexOf(item);
                var gameId = item.StartData?.game_id ?? item.FinishData?.game_id;
                var pluginId = item.StartData?.plugin_id ?? item.FinishData?.plugin_id;
                return _data.PendingScrobbles.Take(Math.Max(0, index))
                    .Any(p => IsSameGame(p, gameId, pluginId));
            }
        }

        // A second start belongs to another launch: never attach its finish to this start.
        private static PendingScrobble FindPairedFinish(PendingScrobble start) {
            var startIndex = _data.PendingScrobbles.IndexOf(start);
            for (var i = startIndex + 1; i < _data.PendingScrobbles.Count; i++) {
                var candidate = _data.PendingScrobbles[i];
                if (!IsSameGame(candidate, start.StartData.game_id, start.StartData.plugin_id)) continue;
                return candidate.Type == "finish" ? candidate : null;
            }
            return null;
        }

        public static bool CompletePendingStart(PendingScrobble item, string sessionId) {
            if (item?.StartData == null) return false;
            return MutatePendingScrobble(item, d => {
                var gameId = item.StartData.game_id;
                var pluginId = item.StartData.plugin_id;
                var pairedFinish = FindPairedFinish(item);
                var laterStart = _data.PendingScrobbles.Skip(_data.PendingScrobbles.IndexOf(item) + 1)
                    .Any(p => p.Type == "start" && IsSameGame(p, gameId, pluginId));
                if (pairedFinish != null && string.IsNullOrEmpty(pairedFinish.FinishData.session_id)) {
                    pairedFinish.FinishData.session_id = sessionId;
                }
                if (pairedFinish == null && !laterStart && !string.IsNullOrEmpty(gameId)
                    && !string.IsNullOrEmpty(sessionId)) {
                    _data.ActiveSessionsByGameId[gameId] = sessionId;
                }
                if (!laterStart && (pairedFinish != null || !string.IsNullOrEmpty(sessionId))) {
                    _data.PendingStartGameIds.Remove(gameId);
                }
                _data.PendingScrobbles.Remove(item);
                _claimedScrobbles.Remove(item);
            });
        }

        public static bool CompletePendingScrobble(PendingScrobble item) {
            return MutatePendingScrobble(item, d => {
                var finish = item.FinishData;
                if (!string.IsNullOrEmpty(finish?.game_id)
                    && _data.ActiveSessionsByGameId.TryGetValue(finish.game_id, out var active)
                    && active == finish.session_id) {
                    _data.ActiveSessionsByGameId.Remove(finish.game_id);
                }
                _data.PendingScrobbles.Remove(item);
                _claimedScrobbles.Remove(item);
            });
        }

        public static bool DropPendingScrobble(PendingScrobble item) {
            return MutatePendingScrobble(item, d => {
                if (item.Type == "start" && item.StartData != null) {
                    var pairedFinish = FindPairedFinish(item);
                    if (pairedFinish != null) {
                        _data.PendingScrobbles.Remove(pairedFinish);
                        _claimedScrobbles.Remove(pairedFinish);
                        _data.DroppedScrobbleCount++;
                    }
                    if (!_data.PendingScrobbles.Any(p => p != item && p.Type == "start"
                        && IsSameGame(p, item.StartData.game_id, item.StartData.plugin_id))) {
                        _data.PendingStartGameIds.Remove(item.StartData.game_id);
                    }
                }
                _data.PendingScrobbles.Remove(item);
                _claimedScrobbles.Remove(item);
                _data.DroppedScrobbleCount++;
            });
        }

        /// <summary>
        /// Removes a single pending scrobble from the queue and persists immediately. Thread-safe.
        /// Used by the flush path to commit each item individually after a confirmed send.
        /// </summary>
        public static void RemovePendingScrobble(PendingScrobble item) {
            lock (_lock) {
                _data.PendingScrobbles.Remove(item);
                _claimedScrobbles.Remove(item);
                SaveInternal();
            }
            DiagnosticsStateChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Increments a pending scrobble's FlushAttempts counter and persists atomically. Thread-safe.
        /// Used by the flush path after a failed send; keeps the mutate-then-save sequence under
        /// the same lock as every other queue mutation instead of writing to the item directly.
        /// </summary>
        public static void IncrementPendingScrobbleFlushAttempts(PendingScrobble item) {
            lock (_lock) {
                item.FlushAttempts++;
                SaveInternal();
            }
        }
    }
}
