using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GsPlugin.Infrastructure;
using GsPlugin.Services;

namespace GsPlugin.Models {
    /// <summary>
    /// Compact per-item fingerprint index for library / achievement diff baselines.
    /// Stores hashes only — not full game/achievement rows — so large libraries (~10k+)
    /// never re-serialize multi-megabyte snapshots (the OOM class that broke sync).
    /// </summary>
    public class GsSyncHashIndexFile {
        public int IdentityGeneration { get; set; }
        public DateTime? FullSyncAt { get; set; }
        /// <summary>playnite_id → item fingerprint</summary>
        public Dictionary<string, string> Entries { get; set; }
            = new Dictionary<string, string>();
    }

    /// <summary>
    /// Static manager for <c>gs_library_hashes.json</c> / <c>gs_achievement_hashes.json</c>.
    /// Thread-safe. Migrates once from legacy fat <see cref="GsSnapshot"/> files.
    /// </summary>
    public static class GsSyncHashIndex {
        private static GsSyncHashIndexFile _library;
        private static GsSyncHashIndexFile _achievements;
        private static string _folderPath;
        private static string _libraryFilePath;
        private static string _achievementsFilePath;
        private static string _legacyCombinedPath;
        private static readonly object _lock = new object();

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            WriteIndented = false
        };

        public static void Initialize(string folderPath) {
            lock (_lock) {
                _folderPath = folderPath;
                _libraryFilePath = Path.Combine(folderPath, "gs_library_hashes.json");
                _achievementsFilePath = Path.Combine(folderPath, "gs_achievement_hashes.json");
                _legacyCombinedPath = Path.Combine(folderPath, "gs_snapshot.json");

                // Recover any crash-interrupted writes (both the compact indexes and the legacy
                // fat snapshot) before reading, so an upgrade after a crash between temp-write
                // and rename does not silently lose a baseline and force a full re-upload.
                GsAtomicFile.RecoverTemp(_libraryFilePath);
                GsAtomicFile.RecoverTemp(_achievementsFilePath);
                GsAtomicFile.RecoverTemp(_legacyCombinedPath);

                var currentGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;

                // Read/derive both halves first (no writes, no deletes) so the generation check
                // below runs against the *loaded* generation — the compact file save re-stamps
                // it to the current generation, so discarding a stale-identity baseline has to
                // happen before that save, not after.
                var (libIndex, libMigrated) = LoadOrMigrateLibrary();
                var (achIndex, achMigrated) = LoadOrMigrateAchievements();

                _library = libIndex;
                _achievements = achIndex;

                var libNeedsSave = libMigrated;
                if (_library.IdentityGeneration != currentGeneration) {
                    GsLogger.Warn($"[GsSyncHashIndex] Library index generation {_library.IdentityGeneration} != data generation {currentGeneration}; discarding");
                    _library = new GsSyncHashIndexFile { IdentityGeneration = currentGeneration };
                    libNeedsSave = true;
                }
                var achNeedsSave = achMigrated;
                if (_achievements.IdentityGeneration != currentGeneration) {
                    GsLogger.Warn($"[GsSyncHashIndex] Achievement index generation {_achievements.IdentityGeneration} != data generation {currentGeneration}; discarding");
                    _achievements = new GsSyncHashIndexFile { IdentityGeneration = currentGeneration };
                    achNeedsSave = true;
                }

                if (libNeedsSave) {
                    SaveLibraryInternal();
                }
                if (achNeedsSave) {
                    SaveAchievementsInternal();
                }

                // Delete the legacy fat snapshot only after BOTH halves have been read and the
                // compact indexes persisted — otherwise migrating the library half could destroy
                // the combined file the achievements half still needs to read.
                if (libMigrated || achMigrated) {
                    GsAtomicFile.TryDelete(_legacyCombinedPath);
                }
            }
        }

        /// <summary>
        /// Loads the library index from the compact file, or derives it once from the legacy fat
        /// combined snapshot (<c>gs_snapshot.json</c> — the only format any shipped build wrote).
        /// Pure: never writes or deletes — the caller persists and clears the legacy file after
        /// the generation check. Returns the index plus whether it came from a migration.
        /// </summary>
        private static (GsSyncHashIndexFile index, bool migrated) LoadOrMigrateLibrary() {
            if (File.Exists(_libraryFilePath)) {
                return (GsAtomicFile.LoadJson<GsSyncHashIndexFile>(_libraryFilePath, jsonOptions)
                    ?? new GsSyncHashIndexFile(), false);
            }

            if (File.Exists(_legacyCombinedPath)) {
                try {
                    var legacy = GsAtomicFile.LoadJson<GsSnapshot>(_legacyCombinedPath, jsonOptions);
                    if (legacy?.Library != null) {
                        GsLogger.Info("[GsSyncHashIndex] Migrating library half of gs_snapshot.json");
                        return (FromLibraryDict(legacy.Library, legacy.IdentityGeneration, legacy.LibraryFullSyncAt), true);
                    }
                }
                catch (Exception ex) {
                    GsLogger.Warn($"[GsSyncHashIndex] Combined snapshot library migrate failed: {ex.Message}");
                }
            }

            return (new GsSyncHashIndexFile(), false);
        }

        /// <summary>
        /// Loads the achievement index from the compact file, or derives it once from the legacy
        /// fat combined snapshot. Pure: never writes or deletes — see <see cref="LoadOrMigrateLibrary"/>.
        /// </summary>
        private static (GsSyncHashIndexFile index, bool migrated) LoadOrMigrateAchievements() {
            if (File.Exists(_achievementsFilePath)) {
                return (GsAtomicFile.LoadJson<GsSyncHashIndexFile>(_achievementsFilePath, jsonOptions)
                    ?? new GsSyncHashIndexFile(), false);
            }

            if (File.Exists(_legacyCombinedPath)) {
                try {
                    var legacy = GsAtomicFile.LoadJson<GsSnapshot>(_legacyCombinedPath, jsonOptions);
                    if (legacy?.Achievements != null) {
                        GsLogger.Info("[GsSyncHashIndex] Migrating achievements half of gs_snapshot.json");
                        return (FromAchievementsDict(legacy.Achievements, legacy.IdentityGeneration, legacy.AchievementsFullSyncAt), true);
                    }
                }
                catch (Exception ex) {
                    GsLogger.Warn($"[GsSyncHashIndex] Combined snapshot achievements migrate failed: {ex.Message}");
                }
            }

            return (new GsSyncHashIndexFile(), false);
        }

        private static GsSyncHashIndexFile FromLibraryDict(
            Dictionary<string, GameSnapshot> library,
            int generation,
            DateTime? fullSyncAt) {
            var entries = new Dictionary<string, string>(library.Count);
            foreach (var kvp in library) {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) {
                    continue;
                }
                entries[kvp.Key] = GsHashUtils.LibraryFingerprintFromSnapshot(kvp.Value);
            }
            return new GsSyncHashIndexFile {
                IdentityGeneration = generation,
                FullSyncAt = fullSyncAt,
                Entries = entries
            };
        }

        private static GsSyncHashIndexFile FromAchievementsDict(
            Dictionary<string, GameAchievementSnapshot> achievements,
            int generation,
            DateTime? fullSyncAt) {
            var entries = new Dictionary<string, string>(achievements.Count);
            foreach (var kvp in achievements) {
                if (string.IsNullOrEmpty(kvp.Key) || kvp.Value == null) {
                    continue;
                }
                entries[kvp.Key] = GsHashUtils.AchievementFingerprintFromSnapshot(kvp.Value);
            }
            return new GsSyncHashIndexFile {
                IdentityGeneration = generation,
                FullSyncAt = fullSyncAt,
                Entries = entries
            };
        }

        private static void EnsureInitialized() {
            if (_library == null || _achievements == null) {
                throw new InvalidOperationException("GsSyncHashIndex not initialized. Call Initialize() first.");
            }
        }

        private static bool SaveLibraryInternal() {
            if (_library == null) {
                return false;
            }
            _library.IdentityGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
            try {
                GsAtomicFile.WriteJson(_libraryFilePath, _library, jsonOptions);
                return true;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSyncHashIndex] Failed to save library index: {ex.Message}");
                GsSentry.CaptureException(ex, "GsSyncHashIndex.SaveLibrary");
                return false;
            }
        }

        private static bool SaveAchievementsInternal() {
            if (_achievements == null) {
                return false;
            }
            _achievements.IdentityGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
            try {
                GsAtomicFile.WriteJson(_achievementsFilePath, _achievements, jsonOptions);
                return true;
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsSyncHashIndex] Failed to save achievement index: {ex.Message}");
                GsSentry.CaptureException(ex, "GsSyncHashIndex.SaveAchievements");
                return false;
            }
        }

        public static bool HasLibraryBaseline {
            get { lock (_lock) { EnsureInitialized(); return _library.FullSyncAt.HasValue; } }
        }

        public static bool HasAchievementsBaseline {
            get { lock (_lock) { EnsureInitialized(); return _achievements.FullSyncAt.HasValue; } }
        }

        public static int LibraryEntryCount {
            get { lock (_lock) { EnsureInitialized(); return _library.Entries?.Count ?? 0; } }
        }

        public static int AchievementEntryCount {
            get { lock (_lock) { EnsureInitialized(); return _achievements.Entries?.Count ?? 0; } }
        }

        /// <summary>Shallow copy of library fingerprints for diff computation.</summary>
        public static Dictionary<string, string> GetLibraryFingerprints() {
            lock (_lock) {
                EnsureInitialized();
                return new Dictionary<string, string>(_library.Entries ?? new Dictionary<string, string>());
            }
        }

        /// <summary>Shallow copy of achievement fingerprints for diff computation.</summary>
        public static Dictionary<string, string> GetAchievementFingerprints() {
            lock (_lock) {
                EnsureInitialized();
                return new Dictionary<string, string>(_achievements.Entries ?? new Dictionary<string, string>());
            }
        }

        public static bool ReplaceLibraryIndex(Dictionary<string, string> entries) {
            lock (_lock) {
                EnsureInitialized();
                _library.Entries = entries ?? new Dictionary<string, string>();
                _library.FullSyncAt = DateTime.UtcNow;
                return SaveLibraryInternal();
            }
        }

        public static bool ApplyLibraryDiff(
            Dictionary<string, string> upserted,
            List<string> removed) {
            lock (_lock) {
                EnsureInitialized();
                if (_library.Entries == null) {
                    _library.Entries = new Dictionary<string, string>();
                }
                if (upserted != null) {
                    foreach (var kvp in upserted) {
                        _library.Entries[kvp.Key] = kvp.Value;
                    }
                }
                if (removed != null) {
                    foreach (var id in removed) {
                        _library.Entries.Remove(id);
                    }
                }
                return SaveLibraryInternal();
            }
        }

        public static bool ReplaceAchievementIndex(Dictionary<string, string> entries) {
            lock (_lock) {
                EnsureInitialized();
                _achievements.Entries = entries ?? new Dictionary<string, string>();
                _achievements.FullSyncAt = DateTime.UtcNow;
                return SaveAchievementsInternal();
            }
        }

        public static bool ApplyAchievementDiff(
            Dictionary<string, string> upserted,
            List<string> cleared) {
            lock (_lock) {
                EnsureInitialized();
                if (_achievements.Entries == null) {
                    _achievements.Entries = new Dictionary<string, string>();
                }
                if (upserted != null) {
                    foreach (var kvp in upserted) {
                        _achievements.Entries[kvp.Key] = kvp.Value;
                    }
                }
                if (cleared != null) {
                    foreach (var id in cleared) {
                        _achievements.Entries.Remove(id);
                    }
                }
                return SaveAchievementsInternal();
            }
        }

        public static bool ClearLibraryIndex() {
            lock (_lock) {
                EnsureInitialized();
                _library.Entries = new Dictionary<string, string>();
                _library.FullSyncAt = null;
                return SaveLibraryInternal();
            }
        }

        public static bool ClearAchievementIndex() {
            lock (_lock) {
                EnsureInitialized();
                _achievements.Entries = new Dictionary<string, string>();
                _achievements.FullSyncAt = null;
                return SaveAchievementsInternal();
            }
        }

        /// <summary>
        /// Resets both indexes to a clean state stamped with the current identity generation and
        /// persists them. Unlike the read/mutate methods, this does NOT require prior
        /// <see cref="Initialize"/> — identity rotation (<see cref="GsDataManager.RotateInstallId"/>)
        /// can reach it before the index is initialized, and the legacy manager it replaced never
        /// threw here. When uninitialized, the in-memory reset is sufficient: the next
        /// <see cref="Initialize"/> writes clean files.
        /// </summary>
        public static bool ClearAll() {
            lock (_lock) {
                var gen = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
                _library = new GsSyncHashIndexFile { IdentityGeneration = gen };
                _achievements = new GsSyncHashIndexFile { IdentityGeneration = gen };
                if (string.IsNullOrEmpty(_libraryFilePath) || string.IsNullOrEmpty(_achievementsFilePath)) {
                    // Not initialized (rotation before startup Initialize, or in tests).
                    return true;
                }
                return SaveLibraryInternal() && SaveAchievementsInternal();
            }
        }

        public static bool Reset() {
            return ClearAll();
        }
    }
}
