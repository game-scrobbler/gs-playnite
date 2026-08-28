using System;
using System.Collections.Generic;
using System.IO;
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
    ///
    /// Both halves are the same store (<see cref="GsHashIndexStore"/>); this type is the facade
    /// that owns the two instances, the single lock, and the cross-half ordering rules of
    /// <see cref="Initialize"/> and <see cref="ClearAll"/>.
    /// </summary>
    public static class GsSyncHashIndex {
        private const string LegacyCombinedFileName = "gs_snapshot.json";

        /// <summary>
        /// The one lock guarding both stores. <see cref="GsHashIndexStore"/> takes no locks of its
        /// own, so every entry point here acquires this before touching a store. That is what
        /// keeps the two-half operations (<see cref="Initialize"/>, <see cref="ClearAll"/>) from
        /// ever being observed half-applied.
        /// </summary>
        private static readonly object _lock = new object();

        private static readonly GsHashIndexStore _library = new GsHashIndexStore(
            fileName: "gs_library_hashes.json",
            label: "Library",
            legacyHalfLabel: "library",
            sentryOperation: "GsSyncHashIndex.SaveLibrary",
            fromLegacySnapshot: LibraryFromLegacySnapshot);

        private static readonly GsHashIndexStore _achievements = new GsHashIndexStore(
            fileName: "gs_achievement_hashes.json",
            label: "Achievement",
            legacyHalfLabel: "achievements",
            sentryOperation: "GsSyncHashIndex.SaveAchievements",
            fromLegacySnapshot: AchievementsFromLegacySnapshot);

        private static string _legacyCombinedPath;

        /// <summary>
        /// Library half of a legacy combined snapshot, or null when it carries none. This plus the
        /// achievements twin below are the only real differences between the two stores.
        /// </summary>
        private static GsSyncHashIndexFile LibraryFromLegacySnapshot(GsSnapshot legacy) {
            if (legacy.Library == null) {
                return null;
            }
            return GsHashIndexStore.FromLegacyDict(
                legacy.Library,
                legacy.IdentityGeneration,
                legacy.LibraryFullSyncAt,
                GsHashUtils.LibraryFingerprintFromSnapshot);
        }

        /// <summary>Achievements half of a legacy combined snapshot, or null when it carries none.</summary>
        private static GsSyncHashIndexFile AchievementsFromLegacySnapshot(GsSnapshot legacy) {
            if (legacy.Achievements == null) {
                return null;
            }
            return GsHashIndexStore.FromLegacyDict(
                legacy.Achievements,
                legacy.IdentityGeneration,
                legacy.AchievementsFullSyncAt,
                GsHashUtils.AchievementFingerprintFromSnapshot);
        }

        public static void Initialize(string folderPath) {
            lock (_lock) {
                _legacyCombinedPath = Path.Combine(folderPath, LegacyCombinedFileName);
                _library.SetLocation(folderPath);
                _achievements.SetLocation(folderPath);

                // Recover any crash-interrupted writes (both the compact indexes and the legacy
                // fat snapshot) before reading, so an upgrade after a crash between temp-write
                // and rename does not silently lose a baseline and force a full re-upload.
                _library.RecoverTemp();
                _achievements.RecoverTemp();
                GsAtomicFile.RecoverTemp(_legacyCombinedPath);

                var currentGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;

                // Read/derive both halves first (no writes, no deletes) so the generation check
                // below runs against the *loaded* generation — the compact file save re-stamps
                // it to the current generation, so discarding a stale-identity baseline has to
                // happen before that save, not after.
                var libMigrated = _library.LoadOrMigrate(_legacyCombinedPath);
                var achMigrated = _achievements.LoadOrMigrate(_legacyCombinedPath);

                var libNeedsSave = libMigrated;
                if (_library.DiscardIfGenerationMismatch(currentGeneration)) {
                    libNeedsSave = true;
                }
                var achNeedsSave = achMigrated;
                if (_achievements.DiscardIfGenerationMismatch(currentGeneration)) {
                    achNeedsSave = true;
                }

                if (libNeedsSave) {
                    _library.Save();
                }
                if (achNeedsSave) {
                    _achievements.Save();
                }

                // Delete the legacy fat snapshot only after BOTH halves have been read and the
                // compact indexes persisted — otherwise migrating the library half could destroy
                // the combined file the achievements half still needs to read.
                if (libMigrated || achMigrated) {
                    GsAtomicFile.TryDelete(_legacyCombinedPath);
                }
            }
        }

        public static bool HasLibraryBaseline {
            get { lock (_lock) { return _library.HasBaseline; } }
        }

        public static bool HasAchievementsBaseline {
            get { lock (_lock) { return _achievements.HasBaseline; } }
        }

        public static int LibraryEntryCount {
            get { lock (_lock) { return _library.EntryCount; } }
        }

        public static int AchievementEntryCount {
            get { lock (_lock) { return _achievements.EntryCount; } }
        }

        /// <summary>Shallow copy of library fingerprints for diff computation.</summary>
        public static Dictionary<string, string> GetLibraryFingerprints() {
            lock (_lock) {
                return _library.GetFingerprints();
            }
        }

        /// <summary>Shallow copy of achievement fingerprints for diff computation.</summary>
        public static Dictionary<string, string> GetAchievementFingerprints() {
            lock (_lock) {
                return _achievements.GetFingerprints();
            }
        }

        public static bool ReplaceLibraryIndex(Dictionary<string, string> entries) {
            lock (_lock) {
                return _library.ReplaceIndex(entries);
            }
        }

        public static bool ApplyLibraryDiff(
            Dictionary<string, string> upserted,
            List<string> removed) {
            lock (_lock) {
                return _library.ApplyDiff(upserted, removed);
            }
        }

        public static bool ReplaceAchievementIndex(Dictionary<string, string> entries) {
            lock (_lock) {
                return _achievements.ReplaceIndex(entries);
            }
        }

        public static bool ApplyAchievementDiff(
            Dictionary<string, string> upserted,
            List<string> cleared) {
            lock (_lock) {
                return _achievements.ApplyDiff(upserted, cleared);
            }
        }

        public static bool ClearLibraryIndex() {
            lock (_lock) {
                return _library.Clear();
            }
        }

        public static bool ClearAchievementIndex() {
            lock (_lock) {
                return _achievements.Clear();
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
                _library.ResetToGeneration(gen);
                _achievements.ResetToGeneration(gen);
                if (!_library.HasFilePath || !_achievements.HasFilePath) {
                    // Not initialized (rotation before startup Initialize, or in tests).
                    return true;
                }
                return _library.Save() && _achievements.Save();
            }
        }

        public static bool Reset() {
            return ClearAll();
        }
    }
}
