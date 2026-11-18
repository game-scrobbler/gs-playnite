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

        /// <summary>
        /// Same settings the store used when it read the legacy file itself. <see cref="GsSnapshot"/>
        /// has no naming policy or converters, and WriteIndented has no effect on reads.
        /// </summary>
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
            var data = GsDataManager.DataOrNull;
            var libPending = data?.LibraryBaselineInvalidationPending == true;
            var achPending = data?.AchievementBaselineInvalidationPending == true;
            var libInvalidationDone = false;
            var achInvalidationDone = false;

            InitializeLocked(folderPath, libPending, achPending, ref libInvalidationDone, ref achInvalidationDone);

            // Outside the index lock: GsDataManager takes its own lock, and sync commits take the
            // data lock before this one, so taking it from inside would invert the order.
            if (libInvalidationDone || achInvalidationDone) {
                GsDataManager.MutateAndSave(d => {
                    if (libInvalidationDone) {
                        d.LibraryBaselineInvalidationPending = false;
                    }

                    if (achInvalidationDone) {
                        d.AchievementBaselineInvalidationPending = false;
                    }
                });
            }
        }

        private static void InitializeLocked(
            string folderPath,
            bool libPending,
            bool achPending,
            ref bool libInvalidationDone,
            ref bool achInvalidationDone) {
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

                // Finish an invalidation a previous session could not complete (see InvalidateHalf).
                // The half starts empty whether or not the delete succeeds now, so a baseline the
                // server has moved past is never served; the flag is cleared below once the files
                // are gone.
                if (libPending) {
                    libInvalidationDone = FinishPendingInvalidation(_library);
                }

                if (achPending) {
                    achInvalidationDone = FinishPendingInvalidation(_achievements);
                }

                // Read/derive both halves first (no writes, no deletes) so the generation check
                // below runs against the *loaded* generation: the compact file save re-stamps
                // it to the current generation, so discarding a stale-identity baseline has to
                // happen before that save, not after.
                var libLoaded = !libPending && _library.TryLoadCompact();
                var achLoaded = !achPending && _achievements.TryLoadCompact();

                // Parse the legacy snapshot at most once, and only when a half still needs it. The
                // fat file is the multi-megabyte OOM class the compact index exists to avoid, so
                // once both compact files are valid it is never parsed again. May stay null when
                // the file is unreadable (corrupt or transiently locked).
                var legacyExists = File.Exists(_legacyCombinedPath);
                GsSnapshot legacy = null;
                if (legacyExists && ((!libLoaded && !libPending) || (!achLoaded && !achPending))) {
                    legacy = GsAtomicFile.LoadJson<GsSnapshot>(_legacyCombinedPath, _legacyJsonOptions);
                }

                // A half that loaded from a valid compact file must not be re-derived. A half that
                // did not load always goes through MigrateFromLegacy (even with a null snapshot) so
                // its index ends up non-null. A compact file that exists but is unreadable is
                // replaced by the legacy-derived baseline: it could be a transient lock rather than
                // corruption, and the worst case is the same older-baseline redundancy noted below.
                // A half with a pending invalidation counts as having nothing in the legacy file:
                // that data is the outdated baseline being dropped.
                var libLegacy = libPending
                    ? GsLegacyHalfResult.Absent
                    : libLoaded ? (GsLegacyHalfResult?)null : _library.MigrateFromLegacy(legacy);
                var achLegacy = achPending
                    ? GsLegacyHalfResult.Absent
                    : achLoaded ? (GsLegacyHalfResult?)null : _achievements.MigrateFromLegacy(legacy);

                var libNeedsSave = PrepareHalfForSave(_library, libLoaded, libLegacy, currentGeneration);
                var achNeedsSave = PrepareHalfForSave(_achievements, achLoaded, achLegacy, currentGeneration);

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
                // fine and simply carries nothing for it. When the half did not load and could not
                // be derived (snapshot unreadable, or the fingerprint recipe threw on a malformed
                // entry), the legacy data may still be the only copy.
                var librarySafe = librarySaved && (libLoaded || libLegacy != GsLegacyHalfResult.Unavailable);
                var achievementsSafe = achievementsSaved && (achLoaded || achLegacy != GsLegacyHalfResult.Unavailable);

                // Deletion depends on every half being durable or not needing the legacy data, not
                // on whether this call migrated anything. That way a legacy file retained after a
                // failed compact save is cleaned up on the first launch after any later successful
                // write, instead of lingering forever because nothing is left to migrate.
                if (legacyExists) {
                    if (librarySafe && achievementsSafe) {
                        // RecoverTemp only promoted a .tmp when the final file was missing, so a
                        // stale one can sit beside it. Removing the final file alone would let the
                        // next launch promote that .tmp back.
                        GsAtomicFile.TryDeleteWithTemp(_legacyCombinedPath);
                    }
                    else {
                        var pending = new List<string>();
                        if (!librarySafe) {
                            pending.Add(librarySaved ? "library (legacy data unreadable)" : "library (save failed)");
                        }

                        if (!achievementsSafe) {
                            pending.Add(achievementsSaved ? "achievements (legacy data unreadable)" : "achievements (save failed)");
                        }

                        GsLogger.Warn($"[GsSyncHashIndex] Legacy gs_snapshot.json retained; not yet durable: {string.Join(", ", pending)}. The next Initialize retries.");
                    }
                }
            }
        }

        /// <summary>
        /// Applies the generation check to one half and reports whether it must be written.
        /// A half with data (loaded or migrated) is discarded on a generation mismatch, and the
        /// discard is persisted. A half that started empty is only re-stamped in memory: writing
        /// an empty file for it would make the next launch load that file as a valid compact half
        /// and delete a legacy snapshot this launch could not read, losing the baseline.
        /// </summary>
        private static bool PrepareHalfForSave(
            GsHashIndexStore store,
            bool loaded,
            GsLegacyHalfResult? legacyHalf,
            int currentGeneration) {
            var migrated = legacyHalf == GsLegacyHalfResult.Migrated;
            if (!loaded && !migrated) {
                store.ResetToGeneration(currentGeneration);
                return false;
            }

            var discarded = store.DiscardIfGenerationMismatch(currentGeneration);
            return migrated || discarded;
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
        /// full old library and achievement rows. Returns false when either compact save failed or
        /// the legacy file (or its <c>.tmp</c>) could not be removed, so a caller can tell the wipe
        /// is incomplete.
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

                // A failed save here leaves the old file in place, stamped with a generation
                // that still matches (nothing rotated), so the generation check on the next
                // Initialize would not catch it as stale and the pre-reset baseline would
                // resurface. Delete it outright instead of relying on that check.
                if (!libSaved) {
                    _library.DeleteFile();
                }

                if (!achSaved) {
                    _achievements.DeleteFile();
                }

                // A stale .tmp left by an interrupted promotion (or a crash before this final file
                // existed) would otherwise survive a wipe and get promoted back on the next launch,
                // resurrecting the data the user asked to clear.
                var legacyGone = GsAtomicFile.TryDeleteWithTemp(_legacyCombinedPath);

                return libSaved && achSaved && legacyGone;
            }
        }

        public static bool Reset() {
            return ClearAll();
        }

        /// <summary>
        /// Called when the server accepted a library change but the library index could not
        /// record it. See <see cref="InvalidateHalf"/>.
        /// </summary>
        public static bool InvalidateLibraryBaseline() {
            return InvalidateHalf(_library, d => d.LibraryBaselineInvalidationPending = true);
        }

        /// <summary>
        /// Called when the server accepted an achievements change but the achievement index could
        /// not record it. See <see cref="InvalidateHalf"/>.
        /// </summary>
        public static bool InvalidateAchievementBaseline() {
            return InvalidateHalf(_achievements, d => d.AchievementBaselineInvalidationPending = true);
        }

        /// <summary>
        /// Drops one half's baseline in memory and on disk so its next sync is a full upload.
        /// Leaving any baseline loadable here lets the server sit ahead of it: an item added and
        /// synced, then removed locally before a restart, would match the recovered baseline and
        /// the unchanged global hash, so no sync would ever remove it from the server.
        ///
        /// The legacy snapshot goes too, because it would re-derive the same outdated baseline on
        /// the next launch. That also drops the other half's legacy fallback, which costs at most
        /// one extra full upload for that half and only after two failed index writes.
        /// Returns true when nothing that could restore an outdated baseline is left on disk. When
        /// something is, the half is marked pending in <see cref="GsData"/> so the next
        /// <see cref="Initialize"/> drops it instead of loading it.
        /// </summary>
        private static bool InvalidateHalf(GsHashIndexStore store, Action<GsData> markPending) {
            bool gone;
            lock (_lock) {
                gone = DeleteOutdatedBaseline(store, inMemoryToo: true);
            }

            if (!gone && GsDataManager.DataOrNull != null) {
                // A lock that blocked the index save can block these deletes too. Record that the
                // half still has to be dropped, in gs_data.json (a different file), so the next
                // Initialize finishes the job instead of loading the outdated baseline. Done
                // outside the index lock; callers must not hold the data lock (CommitSyncBaselineAsync
                // calls this after TryMutateIfActiveIdentity returns).
                GsLogger.Warn("[GsSyncHashIndex] Could not remove an outdated baseline from disk; the next Initialize will drop it.");
                GsDataManager.MutateAndSave(markPending);
            }

            return gone;
        }

        /// <summary>
        /// Retries a pending invalidation at startup. Returns true once nothing outdated is left
        /// on disk, which lets the caller clear the pending flag.
        /// </summary>
        private static bool FinishPendingInvalidation(GsHashIndexStore store) {
            var gone = DeleteOutdatedBaseline(store, inMemoryToo: false);
            if (!gone) {
                GsLogger.Warn("[GsSyncHashIndex] Pending baseline invalidation still blocked; the half starts empty and the delete is retried next launch.");
            }

            return gone;
        }

        /// <summary>
        /// Deletes one half's file and the legacy snapshot, each with its <c>.tmp</c>. Both deletes
        /// always run. Caller holds <see cref="_lock"/>.
        /// </summary>
        private static bool DeleteOutdatedBaseline(GsHashIndexStore store, bool inMemoryToo) {
            var halfGone = inMemoryToo ? store.Invalidate() : store.DeleteFile();
            var legacyGone = GsAtomicFile.TryDeleteWithTemp(_legacyCombinedPath);
            return halfGone && legacyGone;
        }
    }
}
