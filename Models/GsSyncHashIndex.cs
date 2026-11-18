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

        private static readonly JsonSerializerOptions _legacyJsonOptions = new JsonSerializerOptions {
            WriteIndented = false
        };

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
                // below runs against the *loaded* generation: the compact file save re-stamps
                // it to the current generation, so discarding a stale-identity baseline has to
                // happen before that save, not after.
                var libLoaded = _library.TryLoadCompact();
                var achLoaded = _achievements.TryLoadCompact();

                // Parse the legacy snapshot at most once, and only when a half still needs it. The
                // fat file is the multi-megabyte OOM class the compact index exists to avoid, so
                // once both compact files are valid it is never parsed again. May stay null when
                // the file is unreadable (corrupt or transiently locked).
                var legacyExists = File.Exists(_legacyCombinedPath);
                GsSnapshot legacy = null;
                if (legacyExists && (!libLoaded || !achLoaded)) {
                    legacy = GsAtomicFile.LoadJson<GsSnapshot>(_legacyCombinedPath, _legacyJsonOptions);
                }

                // A half that loaded from a valid compact file must not be re-derived. A half that
                // did not load always goes through MigrateFromLegacy (even with a null snapshot) so
                // its index ends up non-null. A compact file that exists but is unreadable is
                // replaced by the legacy-derived baseline: it could be a transient lock rather than
                // corruption, and the worst case is the same older-baseline redundancy noted below.
                var libMigrated = !libLoaded && _library.MigrateFromLegacy(legacy);
                var achMigrated = !achLoaded && _achievements.MigrateFromLegacy(legacy);

                var libNeedsSave = libMigrated;
                if (_library.DiscardIfGenerationMismatch(currentGeneration)) {
                    libNeedsSave = true;
                }

                var achNeedsSave = achMigrated;
                if (_achievements.DiscardIfGenerationMismatch(currentGeneration)) {
                    achNeedsSave = true;
                }

                // Both saves are always attempted: the halves are independent, so one failing must
                // not stop the other from becoming durable.
                //
                // A migrated half keeps serving its in-memory baseline even when its save failed.
                // The legacy file is retained as the durable source, a baseline older than the
                // server only produces redundant re-sends (upserts the server already has, removals
                // it already applied), and dropping it instead would force a full re-upload of a
                // large library over what is often a transient antivirus lock.
                var librarySaved = !libNeedsSave || _library.Save();
                var achievementsSaved = !achNeedsSave || _achievements.Save();

                // A half no longer needs the legacy file once its write (if any) succeeded and it
                // either loaded from a valid compact file, migrated, or the legacy snapshot parsed
                // fine and simply carries nothing for it. When the legacy file could not be parsed
                // and the half did not load, the legacy data may still be the only copy.
                var librarySafe = librarySaved && (libLoaded || libMigrated || legacy != null);
                var achievementsSafe = achievementsSaved && (achLoaded || achMigrated || legacy != null);

                // Deletion depends on every half being durable or not needing the legacy data, not
                // on whether this call migrated anything. That way a legacy file retained after a
                // failed compact save is cleaned up on the first launch after any later successful
                // write, instead of lingering forever because nothing is left to migrate.
                if (legacyExists) {
                    if (librarySafe && achievementsSafe) {
                        GsAtomicFile.TryDelete(_legacyCombinedPath);
                    }
                    else {
                        var pending = new List<string>();
                        if (!librarySafe) {
                            pending.Add(librarySaved ? "library (legacy snapshot unreadable)" : "library (save failed)");
                        }

                        if (!achievementsSafe) {
                            pending.Add(achievementsSaved ? "achievements (legacy snapshot unreadable)" : "achievements (save failed)");
                        }

                        GsLogger.Warn($"[GsSyncHashIndex] Legacy gs_snapshot.json retained; not yet durable: {string.Join(", ", pending)}. The next Initialize retries.");
                    }
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
        /// <see cref="Initialize"/>: identity rotation (<see cref="GsDataManager.RotateInstallId"/>)
        /// can reach it before the index is initialized, and the legacy manager it replaced never
        /// threw here. When uninitialized, the in-memory reset is sufficient: the next
        /// <see cref="Initialize"/> writes clean files, and its generation check discards any
        /// stale-identity data found in the legacy snapshot and then deletes that file.
        ///
        /// When initialized, a retained legacy <c>gs_snapshot.json</c> is always deleted, even if a
        /// compact save fails: the user asked for the data to be wiped, and that file holds the
        /// full old library and achievement rows. The return value reflects the compact saves only.
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

                // Both saves are attempted regardless of the other's outcome.
                var libSaved = _library.Save();
                var achSaved = _achievements.Save();

                if (!string.IsNullOrEmpty(_legacyCombinedPath)) {
                    GsAtomicFile.TryDelete(_legacyCombinedPath);
                }

                return libSaved && achSaved;
            }
        }

        public static bool Reset() {
            return ClearAll();
        }
    }
}
