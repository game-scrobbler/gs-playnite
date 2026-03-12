using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GsPlugin.Infrastructure;

namespace GsPlugin.Models {
    public class GameSnapshot {
        public string playnite_id { get; set; }
        public string game_id { get; set; }
        public string plugin_id { get; set; }
        public long playtime_seconds { get; set; }
        public int play_count { get; set; }
        public string last_activity { get; set; }
        public string metadata_hash { get; set; }
        public int? achievement_count_unlocked { get; set; }
        public int? achievement_count_total { get; set; }
    }

    public class AchievementSnapshot {
        public string name { get; set; }
        public bool is_unlocked { get; set; }
        public string date_unlocked { get; set; }
        public float? rarity_percent { get; set; }
    }

    public class GameAchievementSnapshot {
        public string playnite_id { get; set; }
        public List<AchievementSnapshot> achievements { get; set; }
    }

    public class GsSnapshot {
        public Dictionary<string, GameSnapshot> Library { get; set; } = new Dictionary<string, GameSnapshot>();
        public Dictionary<string, GameAchievementSnapshot> Achievements { get; set; } = new Dictionary<string, GameAchievementSnapshot>();
        public DateTime? LibraryFullSyncAt { get; set; }
        public DateTime? AchievementsFullSyncAt { get; set; }
        /// <summary>
        /// Must match GsData.IdentityGeneration. When they differ the snapshot was written for a
        /// previous identity (e.g. crash between RotateInstallId and GsSnapshotManager.Reset) and
        /// is discarded automatically on next startup.
        /// </summary>
        public int IdentityGeneration { get; set; } = 0;
    }

    /// <summary>
    /// Static manager for the snapshot file used for diff-based sync.
    /// Thread-safe: all access to _snapshot is synchronized via _lock.
    /// Stored in a separate file (gs_snapshot.json) to keep gs_data.json lean.
    /// </summary>
    public static class GsSnapshotManager {
        private static GsSnapshot _snapshot;
        private static string _filePath;
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        /// <summary>
        /// Initializes the snapshot manager.
        /// Call once during plugin startup, after GsDataManager.Initialize(), passing the same folder.
        /// If the persisted snapshot's IdentityGeneration does not match GsData.IdentityGeneration
        /// (e.g. a crash occurred between RotateInstallId and GsSnapshotManager.Reset), the stale
        /// snapshot is discarded and replaced with a clean one so the install runs a fresh full sync.
        /// </summary>
        public static void Initialize(string folderPath) {
            lock (_lock) {
                _filePath = Path.Combine(folderPath, "gs_snapshot.json");
                var loaded = Load();
                var currentGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
                if (loaded.IdentityGeneration != currentGeneration) {
                    GsLogger.Warn($"[GsSnapshotManager] Snapshot generation {loaded.IdentityGeneration} != data generation {currentGeneration}; discarding stale snapshot");
                    _snapshot = new GsSnapshot { IdentityGeneration = currentGeneration };
                    SaveInternal();
                }
                else {
                    _snapshot = loaded;
                }
            }
        }

        private static GsSnapshot Load() {
            if (!File.Exists(_filePath)) {
                return new GsSnapshot();
            }

            try {
                var json = File.ReadAllText(_filePath);
                var snapshot = JsonSerializer.Deserialize<GsSnapshot>(json, jsonOptions) ?? new GsSnapshot();
                // Guard against null dictionaries from persisted JSON (e.g., "Library": null)
                snapshot.Library = snapshot.Library ?? new Dictionary<string, GameSnapshot>();
                snapshot.Achievements = snapshot.Achievements ?? new Dictionary<string, GameAchievementSnapshot>();
                return snapshot;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSnapshotManager] Failed to load snapshot: {ex.Message}");
                return new GsSnapshot();
            }
        }

        public static void Save() {
            lock (_lock) {
                SaveInternal();
            }
        }

        /// <summary>
        /// Resets the snapshot to a clean state, stamps the current identity generation, and persists.
        /// Call when the install identity is rotated so the recovered install is forced to
        /// run a seeding full sync rather than inheriting stale diff baselines.
        /// Thread-safe.
        /// </summary>
        public static void Reset() {
            lock (_lock) {
                var generation = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
                _snapshot = new GsSnapshot { IdentityGeneration = generation };
                SaveInternal();
            }
        }

        private static void SaveInternal() {
            // Stamp the current identity generation before every write so the on-disk snapshot
            // always reflects the identity it was built for. On next Initialize() a mismatch
            // between this value and GsData.IdentityGeneration causes automatic discard.
            if (_snapshot != null) {
                _snapshot.IdentityGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
            }
            try {
                var json = JsonSerializer.Serialize(_snapshot, jsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSnapshotManager] Failed to save snapshot: {ex.Message}");
            }
        }

        private static void EnsureInitialized() {
            if (_snapshot == null) {
                throw new InvalidOperationException("GsSnapshotManager not initialized. Call Initialize() first.");
            }
        }

        /// <summary>
        /// Returns true if a library baseline exists (a full sync has been done previously).
        /// Uses the timestamp rather than dictionary count so that empty libraries are valid baselines.
        /// </summary>
        public static bool HasLibraryBaseline {
            get { lock (_lock) { EnsureInitialized(); return _snapshot.LibraryFullSyncAt.HasValue; } }
        }

        /// <summary>
        /// Returns true if an achievements baseline exists.
        /// Uses the timestamp rather than dictionary count so that empty achievement sets are valid baselines.
        /// </summary>
        public static bool HasAchievementsBaseline {
            get { lock (_lock) { EnsureInitialized(); return _snapshot.AchievementsFullSyncAt.HasValue; } }
        }

        /// <summary>
        /// Returns a shallow copy of the library dictionary for safe read-only use.
        /// Callers can iterate without risk of concurrent mutation by writers.
        /// </summary>
        public static Dictionary<string, GameSnapshot> GetLibrarySnapshot() {
            lock (_lock) {
                EnsureInitialized();
                return new Dictionary<string, GameSnapshot>(_snapshot.Library);
            }
        }

        /// <summary>
        /// Returns a shallow copy of the achievements dictionary for safe read-only use.
        /// Callers can iterate without risk of concurrent mutation by writers.
        /// </summary>
        public static Dictionary<string, GameAchievementSnapshot> GetAchievementsSnapshot() {
            lock (_lock) {
                EnsureInitialized();
                return new Dictionary<string, GameAchievementSnapshot>(_snapshot.Achievements);
            }
        }

        /// <summary>
        /// Replaces the library snapshot with the current state and persists it.
        /// </summary>
        public static void UpdateLibrarySnapshot(Dictionary<string, GameSnapshot> library) {
            lock (_lock) {
                _snapshot.Library = library;
                _snapshot.LibraryFullSyncAt = DateTime.UtcNow;
                SaveInternal();
            }
        }

        /// <summary>
        /// Applies a diff result to the existing library snapshot and persists it.
        /// </summary>
        public static void ApplyLibraryDiff(
            Dictionary<string, GameSnapshot> added,
            Dictionary<string, GameSnapshot> updated,
            List<string> removed) {
            lock (_lock) {
                foreach (var kvp in added) {
                    _snapshot.Library[kvp.Key] = kvp.Value;
                }
                foreach (var kvp in updated) {
                    _snapshot.Library[kvp.Key] = kvp.Value;
                }
                foreach (var id in removed) {
                    _snapshot.Library.Remove(id);
                }
                SaveInternal();
            }
        }

        /// <summary>
        /// Replaces the achievements snapshot with the current state and persists it.
        /// </summary>
        public static void UpdateAchievementsSnapshot(Dictionary<string, GameAchievementSnapshot> achievements) {
            lock (_lock) {
                _snapshot.Achievements = achievements;
                _snapshot.AchievementsFullSyncAt = DateTime.UtcNow;
                SaveInternal();
            }
        }

        /// <summary>
        /// Applies a diff result to the existing achievements snapshot and persists it.
        /// Changed entries are upserted; cleared entries are removed.
        /// </summary>
        public static void ApplyAchievementsDiff(
            Dictionary<string, GameAchievementSnapshot> changed,
            List<string> cleared) {
            lock (_lock) {
                foreach (var kvp in changed) {
                    _snapshot.Achievements[kvp.Key] = kvp.Value;
                }
                foreach (var id in cleared) {
                    _snapshot.Achievements.Remove(id);
                }
                SaveInternal();
            }
        }

        /// <summary>
        /// Clears both library and achievements snapshots entirely. Called on data deletion opt-out.
        /// </summary>
        public static void ClearAll() {
            lock (_lock) {
                _snapshot = new GsSnapshot();
                SaveInternal();
            }
        }

        /// <summary>
        /// Clears the library snapshot. Called when the server requests a force-full-sync.
        /// </summary>
        public static void ClearLibrarySnapshot() {
            lock (_lock) {
                _snapshot.Library = new Dictionary<string, GameSnapshot>();
                _snapshot.LibraryFullSyncAt = null;
                SaveInternal();
            }
        }

        /// <summary>
        /// Clears the achievements snapshot. Called when the server requests a force-full-sync.
        /// </summary>
        public static void ClearAchievementsSnapshot() {
            lock (_lock) {
                _snapshot.Achievements = new Dictionary<string, GameAchievementSnapshot>();
                _snapshot.AchievementsFullSyncAt = null;
                SaveInternal();
            }
        }
    }
}
