using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsSyncHashIndexTests {
        [Fact]
        public void Initialize_DiscardsIndexWhenIdentityGenerationMismatches() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "g1", "fp" }
                }));
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);

                // Bump identity generation as RotateInstallId would
                GsDataManager.MutateAndSave(d => d.IdentityGeneration = d.IdentityGeneration + 1);
                GsSyncHashIndex.Initialize(temp.Path);

                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.Equal(0, GsSyncHashIndex.LibraryEntryCount);
            }
        }

        [Fact]
        public void HasLibraryBaseline_EmptyIndexWithTimestamp_ReturnsTrue() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string>()));
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.Equal(0, GsSyncHashIndex.LibraryEntryCount);
            }
        }

        [Fact]
        public void HasLibraryBaseline_NoSync_ReturnsFalse() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
            }
        }

        [Fact]
        public void GetLibraryFingerprints_ReturnsShallowCopy() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "fp1" }
                });

                var copy1 = GsSyncHashIndex.GetLibraryFingerprints();
                var copy2 = GsSyncHashIndex.GetLibraryFingerprints();
                Assert.NotSame(copy1, copy2);

                copy1.Remove("game1");
                copy1["game99"] = "x";
                var fresh = GsSyncHashIndex.GetLibraryFingerprints();
                Assert.True(fresh.ContainsKey("game1"));
                Assert.False(fresh.ContainsKey("game99"));
            }
        }

        [Fact]
        public void ApplyLibraryDiff_UpsertsAndRemoves() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "keep", "a" },
                    { "update", "b" },
                    { "remove", "c" }
                });

                Assert.True(GsSyncHashIndex.ApplyLibraryDiff(
                    new Dictionary<string, string> {
                        { "new", "d" },
                        { "update", "e" }
                    },
                    new List<string> { "remove" }));

                var fps = GsSyncHashIndex.GetLibraryFingerprints();
                Assert.Equal(3, fps.Count);
                Assert.Equal("a", fps["keep"]);
                Assert.Equal("e", fps["update"]);
                Assert.Equal("d", fps["new"]);
                Assert.False(fps.ContainsKey("remove"));
            }
        }

        [Fact]
        public void ClearLibraryIndex_ResetsBaseline() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "fp" }
                });
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.ClearLibraryIndex());
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
            }
        }

        [Fact]
        public void Persistence_SurvivesReinitialize() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "42|1||meta" }
                }));
                Assert.True(GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "game1", "game1:1:0:abc" }
                }));

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Equal("42|1||meta", GsSyncHashIndex.GetLibraryFingerprints()["game1"]);
                Assert.Equal("game1:1:0:abc", GsSyncHashIndex.GetAchievementFingerprints()["game1"]);
                Assert.True(File.Exists(Path.Combine(temp.Path, "gs_library_hashes.json")));
                Assert.True(File.Exists(Path.Combine(temp.Path, "gs_achievement_hashes.json")));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("gs_library_hashes.json")]
        [InlineData("gs_achievement_hashes.json")]
        public void Persistence_MigratesLegacyCombinedSnapshot(string blockedCompactFile) {
            // Pins: a failed compact write retains the legacy file, the OTHER half is still
            // written and both baselines are served in memory, and a later Initialize completes
            // the migration and deletes the legacy file.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var blockedTempPath = blockedCompactFile == null
                    ? null
                    : BlockTempPath(temp.Path, blockedCompactFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);

                    if (blockedCompactFile != null) {
                        Assert.True(File.Exists(legacyPath),
                            "A failed compact write must retain the durable migration source.");
                        var otherCompactFile = blockedCompactFile == LibraryFile ? AchievementFile : LibraryFile;
                        Assert.True(File.Exists(Path.Combine(temp.Path, otherCompactFile)),
                            "The half whose write succeeded must be persisted even when the other fails.");
                        Assert.False(File.Exists(Path.Combine(temp.Path, blockedCompactFile)));
                        AssertLegacyBaselinesServed(legacy);

                        Unblock(blockedTempPath);
                        GsSyncHashIndex.Initialize(temp.Path);
                    }
                }
                finally {
                    Unblock(blockedTempPath);
                }

                AssertLegacyBaselinesServed(legacy);
                Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                Assert.True(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                Assert.False(File.Exists(legacyPath));
            }
        }

        [Theory]
        [InlineData("gs_library_hashes.json")]
        [InlineData("gs_achievement_hashes.json")]
        public void Persistence_MigrationRetainsLegacyOnSharingViolationThenCompletes(string heldCompactFile) {
            // Pins the IOException path (a held file handle goes through GsAtomicFile.WithRetry,
            // unlike a directory blocker, which is an UnauthorizedAccessException that is never
            // retried). Asserts outcomes only: timing a real handle is non-discriminating.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var heldTempPath = Path.Combine(temp.Path, heldCompactFile) + ".tmp";
                var otherCompactFile = heldCompactFile == LibraryFile ? AchievementFile : LibraryFile;

                var held = new FileStream(heldTempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);

                    Assert.True(File.Exists(legacyPath),
                        "A compact write that keeps hitting a sharing violation must retain the legacy file.");
                    Assert.True(File.Exists(Path.Combine(temp.Path, otherCompactFile)));
                    Assert.False(File.Exists(Path.Combine(temp.Path, heldCompactFile)));
                    AssertLegacyBaselinesServed(legacy);
                }
                finally {
                    held.Dispose();
                }

                // Drop the empty temp file the held handle created so the second Initialize
                // takes the plain migrate path rather than promoting it.
                File.Delete(heldTempPath);
                GsSyncHashIndex.Initialize(temp.Path);

                AssertLegacyBaselinesServed(legacy);
                Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                Assert.True(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                Assert.False(File.Exists(legacyPath));
            }
        }

        [Fact]
        public void Persistence_MigrationWithBothWritesFailing_RetainsLegacyAndServesBaselines() {
            // Pins: when neither compact write succeeds, nothing is lost: the legacy file stays,
            // the migrated baselines are still served this session, and the next Initialize
            // writes both files and deletes the legacy file.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var libBlock = BlockTempPath(temp.Path, LibraryFile);
                var achBlock = BlockTempPath(temp.Path, AchievementFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);

                    Assert.True(File.Exists(legacyPath));
                    Assert.False(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                    Assert.False(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                    AssertLegacyBaselinesServed(legacy);

                    Unblock(libBlock);
                    Unblock(achBlock);
                    GsSyncHashIndex.Initialize(temp.Path);

                    Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                    Assert.True(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                    Assert.False(File.Exists(legacyPath));
                    AssertLegacyBaselinesServed(legacy);
                }
                finally {
                    Unblock(libBlock);
                    Unblock(achBlock);
                }
            }
        }

        [Fact]
        public void Initialize_DeletesRetainedLegacyOnceBothCompactFilesWereWrittenBySync() {
            // Pins: a legacy file retained after a failed migration save is cleaned up by a later
            // Initialize even though that call does not migrate, and the compact data written by
            // sync wins over the stale legacy values.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var libBlock = BlockTempPath(temp.Path, LibraryFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);
                    Assert.True(File.Exists(legacyPath));

                    Unblock(libBlock);
                    Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                        { "synced-game", "synced-fp" }
                    }));
                    Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                    Assert.True(File.Exists(Path.Combine(temp.Path, AchievementFile)));

                    GsSyncHashIndex.Initialize(temp.Path);

                    Assert.False(File.Exists(legacyPath),
                        "With both compact files valid the retained legacy file is no longer needed.");
                    var fps = GsSyncHashIndex.GetLibraryFingerprints();
                    Assert.Equal("synced-fp", fps["synced-game"]);
                    Assert.False(fps.ContainsKey("legacy-game"),
                        "The legacy values must not be re-migrated over data written by sync.");
                    Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                }
                finally {
                    Unblock(libBlock);
                }
            }
        }

        [Fact]
        public void ClearAll_DeletesRetainedLegacySnapshot() {
            // Pins: ClearAll removes a legacy file retained by a failed migration so it cannot be
            // re-migrated after the user reset the index.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var libBlock = BlockTempPath(temp.Path, LibraryFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);
                    Assert.True(File.Exists(legacyPath));

                    Unblock(libBlock);
                    Assert.True(GsSyncHashIndex.ClearAll());

                    Assert.False(File.Exists(legacyPath));
                    Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                    Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
                }
                finally {
                    Unblock(libBlock);
                }
            }
        }

        [Fact]
        public void ClearAll_DeletesLegacySnapshotEvenWhenCompactSaveFails() {
            // Pins: ClearAll always deletes the legacy file, even while a compact save is still
            // failing (it then reports false), because a reset must never let old data return.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var libBlock = BlockTempPath(temp.Path, LibraryFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);
                    Assert.True(File.Exists(legacyPath));

                    Assert.False(GsSyncHashIndex.ClearAll());

                    Assert.False(File.Exists(legacyPath));
                    Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                    Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
                }
                finally {
                    Unblock(libBlock);
                }
            }
        }

        [Fact]
        public void Initialize_UnreadableCompactFile_FallsBackToLegacyForThatHalf() {
            // Pins: a compact file that exists but does not parse counts as missing, so that half
            // migrates from the legacy snapshot and the unreadable file is overwritten, while the
            // other half keeps its valid compact data.
            using (var temp = TempPluginDir.Create()) {
                var gen = CurrentGeneration();
                var legacy = BuildLegacySnapshot(gen);
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                var libraryPath = Path.Combine(temp.Path, LibraryFile);
                File.WriteAllText(libraryPath, "{ not json");
                WriteCompactFile(temp.Path, AchievementFile, gen, new Dictionary<string, string> {
                    { "other-game", "fp" }
                });

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                var expectedLib = GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]);
                Assert.Equal(expectedLib, GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);

                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                var achFps = GsSyncHashIndex.GetAchievementFingerprints();
                Assert.Equal("fp", achFps["other-game"]);
                Assert.False(achFps.ContainsKey("legacy-game"),
                    "A valid compact file must win over the legacy snapshot for its half.");

                var rewritten = System.Text.Json.JsonSerializer.Deserialize<GsSyncHashIndexFile>(
                    File.ReadAllText(libraryPath));
                Assert.Equal(expectedLib, rewritten.Entries["legacy-game"]);
                Assert.False(File.Exists(legacyPath));
            }
        }

        [Fact]
        public void Initialize_UnreadableCompactFileWithFailedSave_RetainsLegacy() {
            // Pins: when the unreadable compact file cannot be rewritten, the legacy snapshot is
            // the only remaining copy of that half and must not be deleted.
            using (var temp = TempPluginDir.Create()) {
                var gen = CurrentGeneration();
                var legacy = BuildLegacySnapshot(gen);
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);
                File.WriteAllText(Path.Combine(temp.Path, LibraryFile), "{ not json");
                WriteCompactFile(temp.Path, AchievementFile, gen, new Dictionary<string, string> {
                    { "other-game", "fp" }
                });
                var libBlock = BlockTempPath(temp.Path, LibraryFile);
                try {
                    GsSyncHashIndex.Initialize(temp.Path);

                    Assert.True(File.Exists(legacyPath));
                    Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                    Assert.Equal(
                        GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]),
                        GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);
                }
                finally {
                    Unblock(libBlock);
                }
            }
        }

        [Fact]
        public void Initialize_UnreadableLegacyWithNoCompactFiles_KeepsLegacyAndStartsEmpty() {
            // Pins: an unreadable legacy file is kept while any half still lacks a valid compact
            // file (it may be repairable by hand), and the index starts empty meanwhile.
            using (var temp = TempPluginDir.Create()) {
                var legacyPath = Path.Combine(temp.Path, LegacyFile);
                File.WriteAllText(legacyPath, "{ not json");

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(File.Exists(legacyPath));
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
        }

        [Fact]
        public void Initialize_UnreadableLegacyUnderNonZeroGeneration_KeepsLegacyAcrossLaunches() {
            // Pins: a half that started empty is not written just to re-stamp its generation.
            // Writing empty compact files here made the second launch load them as valid halves
            // and delete the legacy snapshot the first launch could not read.
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => d.IdentityGeneration = d.IdentityGeneration + 1);
                Assert.NotEqual(0, CurrentGeneration());
                var legacyPath = Path.Combine(temp.Path, LegacyFile);
                File.WriteAllText(legacyPath, "{ not json");

                GsSyncHashIndex.Initialize(temp.Path);
                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(File.Exists(legacyPath));
                Assert.False(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                Assert.False(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
        }

        [Fact]
        public void Initialize_LegacyHalfThatFailsToDerive_KeepsLegacy() {
            // Pins: a half whose fingerprint recipe throws on a malformed legacy entry counts as
            // not durable, so the other half migrating cannot delete its only copy.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration());
                // A null achievement row makes the recipe dereference null.
                legacy.Achievements["legacy-game"].achievements = new List<AchievementSnapshot> { null };
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.True(File.Exists(legacyPath));

                // The next launch loads the library from its compact file and still keeps the
                // legacy file for the half it cannot derive.
                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(File.Exists(legacyPath));
                Assert.Equal(
                    GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]),
                    GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);
            }
        }

        [Fact]
        public void Initialize_UnreadableLegacyWithBothCompactFilesValid_DeletesLegacy() {
            // Pins: an unreadable legacy file that is no longer needed (both compact files are
            // valid) is deleted, and the compact baselines are left intact.
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "lib-fp" }
                }));
                Assert.True(GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "game1", "ach-fp" }
                }));
                var legacyPath = Path.Combine(temp.Path, LegacyFile);
                File.WriteAllText(legacyPath, "{ not json");

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.False(File.Exists(legacyPath));
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Equal("lib-fp", GsSyncHashIndex.GetLibraryFingerprints()["game1"]);
                Assert.Equal("ach-fp", GsSyncHashIndex.GetAchievementFingerprints()["game1"]);
            }
        }

        [Fact]
        public void Initialize_LegacyWithOnlyLibraryHalf_MigratesLibraryAndDeletesLegacy() {
            // Pins: a legacy snapshot with nothing for the achievements half does not block
            // cleanup; the library migrates, achievements stay without a baseline, legacy goes.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration(), includeAchievements: false);
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.Equal(
                    GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]),
                    GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Equal(0, GsSyncHashIndex.AchievementEntryCount);
                Assert.False(File.Exists(legacyPath));
            }
        }

        [Fact]
        public void Initialize_LegacyWithStaleIdentityGeneration_DiscardsBothHalvesAndDeletesLegacy() {
            // Pins: a legacy snapshot written under another install identity is discarded rather
            // than migrated, the (empty) compact files are written, and the legacy file is deleted.
            using (var temp = TempPluginDir.Create()) {
                var legacy = BuildLegacySnapshot(CurrentGeneration() + 1);
                var legacyPath = WriteLegacySnapshot(temp.Path, legacy);

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Equal(0, GsSyncHashIndex.LibraryEntryCount);
                Assert.Equal(0, GsSyncHashIndex.AchievementEntryCount);
                Assert.True(File.Exists(Path.Combine(temp.Path, LibraryFile)));
                Assert.True(File.Exists(Path.Combine(temp.Path, AchievementFile)));
                Assert.False(File.Exists(legacyPath));
            }
        }

        private const string LegacyFile = "gs_snapshot.json";
        private const string LibraryFile = "gs_library_hashes.json";
        private const string AchievementFile = "gs_achievement_hashes.json";

        // The snapshot's generation must match the current install identity or the migration is
        // (correctly) discarded. Read the current generation rather than re-initializing
        // GsDataManager, which would pollute this shared-static test collection with a data
        // folder that gets deleted when the fixture disposes.
        private static int CurrentGeneration() => GsDataManager.DataOrNull?.IdentityGeneration ?? 0;

        /// <summary>
        /// Builds a legacy combined snapshot. A half that is not requested is null, matching a
        /// legacy file that carries nothing for it.
        /// </summary>
        private static GsSnapshot BuildLegacySnapshot(
            int generation,
            bool includeLibrary = true,
            bool includeAchievements = true) {
            var legacy = new GsSnapshot { IdentityGeneration = generation };
            if (includeLibrary) {
                legacy.LibraryFullSyncAt = DateTime.UtcNow;
                legacy.Library = new Dictionary<string, GameSnapshot> {
                    {
                        "legacy-game",
                        new GameSnapshot {
                            playnite_id = "legacy-game",
                            playtime_seconds = 7,
                            play_count = 2,
                            last_activity = "2020-01-01T00:00:00Z",
                            metadata_hash = "deadbeef"
                        }
                    }
                };
            }
            else {
                legacy.Library = null;
            }

            if (includeAchievements) {
                legacy.AchievementsFullSyncAt = DateTime.UtcNow;
                legacy.Achievements = new Dictionary<string, GameAchievementSnapshot> {
                    {
                        "legacy-game",
                        new GameAchievementSnapshot {
                            playnite_id = "legacy-game",
                            achievements = new List<AchievementSnapshot> {
                                new AchievementSnapshot { name = "Old", is_unlocked = false }
                            }
                        }
                    }
                };
            }
            else {
                legacy.Achievements = null;
            }

            return legacy;
        }

        private static string WriteLegacySnapshot(string folder, GsSnapshot legacy) {
            var legacyPath = Path.Combine(folder, LegacyFile);
            File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(legacy));
            return legacyPath;
        }

        private static void WriteCompactFile(
            string folder,
            string fileName,
            int generation,
            Dictionary<string, string> entries) {
            var file = new GsSyncHashIndexFile {
                IdentityGeneration = generation,
                FullSyncAt = DateTime.UtcNow,
                Entries = entries
            };
            File.WriteAllText(
                Path.Combine(folder, fileName),
                System.Text.Json.JsonSerializer.Serialize(file));
        }

        /// <summary>
        /// A directory at the atomic writer's temp path fails that write (UnauthorizedAccessException,
        /// not retried) without depending on the timing of a real file lock or antivirus scan.
        /// </summary>
        private static string BlockTempPath(string folder, string compactFile) {
            var blockedTempPath = Path.Combine(folder, compactFile) + ".tmp";
            Directory.CreateDirectory(blockedTempPath);
            return blockedTempPath;
        }

        private static void Unblock(string blockedTempPath) {
            if (blockedTempPath != null && Directory.Exists(blockedTempPath)) {
                Directory.Delete(blockedTempPath, recursive: true);
            }
        }

        private static void AssertLegacyBaselinesServed(GsSnapshot legacy) {
            Assert.True(GsSyncHashIndex.HasLibraryBaseline);
            Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
            var expectedLib = GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]);
            Assert.Equal(expectedLib, GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);
            var expectedAch = GsHashUtils.AchievementFingerprintFromSnapshot(legacy.Achievements["legacy-game"]);
            Assert.Equal(expectedAch, GsSyncHashIndex.GetAchievementFingerprints()["legacy-game"]);
        }

        [Fact]
        public void Persistence_RecoversAndMigratesLegacyCombinedSnapshotTempFile() {
            using (var temp = TempPluginDir.Create()) {
                var legacy = new GsSnapshot {
                    IdentityGeneration = GsDataManager.DataOrNull?.IdentityGeneration ?? 0,
                    LibraryFullSyncAt = DateTime.UtcNow,
                    AchievementsFullSyncAt = DateTime.UtcNow,
                    Library = new Dictionary<string, GameSnapshot> {
                        { "recovered", new GameSnapshot {
                            playnite_id = "recovered",
                            playtime_seconds = 42,
                            metadata_hash = "meta"
                        } }
                    },
                    Achievements = new Dictionary<string, GameAchievementSnapshot> {
                        { "recovered", new GameAchievementSnapshot {
                            playnite_id = "recovered",
                            achievements = new List<AchievementSnapshot>()
                        } }
                    }
                };
                var legacyPath = Path.Combine(temp.Path, "gs_snapshot.json");
                File.WriteAllText(
                    legacyPath + ".tmp",
                    System.Text.Json.JsonSerializer.Serialize(legacy));

                GsSyncHashIndex.Initialize(temp.Path);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Contains("recovered", GsSyncHashIndex.GetLibraryFingerprints().Keys);
                Assert.False(File.Exists(legacyPath));
                Assert.False(File.Exists(legacyPath + ".tmp"));
            }
        }

        [Fact]
        public void ComputeLibraryDiff_DetectsPlaytimeAndMetadataChanges() {
            var baseDto = new GameSyncDto {
                playnite_id = "g1",
                game_name = "Game",
                playtime_seconds = 100,
                play_count = 1,
                last_activity = null,
                plugin_id = "cb91dfc9-7cee-4ea8-ad08-8bd6360b8e91",
                game_id = "1"
            };
            var fp = GsHashUtils.ComputeLibraryItemFingerprint(baseDto);
            var index = new Dictionary<string, string> { { "g1", fp }, { "gone", "old" } };

            var playtimeChanged = new GameSyncDto {
                playnite_id = "g1",
                game_name = "Game",
                playtime_seconds = 200,
                play_count = 1,
                plugin_id = baseDto.plugin_id,
                game_id = "1"
            };
            var (added1, updated1, removed1, _) = GsScrobblingService.ComputeLibraryDiff(
                new List<GameSyncDto> { playtimeChanged }, index);
            Assert.Empty(added1);
            Assert.Single(updated1);
            Assert.Equal("g1", updated1[0].playnite_id);
            Assert.Contains("gone", removed1);

            var metadataChanged = new GameSyncDto {
                playnite_id = "g1",
                game_name = "Renamed",
                playtime_seconds = 100,
                play_count = 1,
                plugin_id = baseDto.plugin_id,
                game_id = "1"
            };
            var (added2, updated2, removed2, _) = GsScrobblingService.ComputeLibraryDiff(
                new List<GameSyncDto> { metadataChanged },
                new Dictionary<string, string> { { "g1", fp } });
            Assert.Empty(added2);
            Assert.Single(updated2);
            Assert.Empty(removed2);

            var newGame = new GameSyncDto {
                playnite_id = "g2",
                game_name = "New",
                playtime_seconds = 0,
                play_count = 0,
                plugin_id = baseDto.plugin_id,
                game_id = "2"
            };
            var (added3, updated3, removed3, _) = GsScrobblingService.ComputeLibraryDiff(
                new List<GameSyncDto> { playtimeChanged, newGame },
                new Dictionary<string, string> { { "g1", GsHashUtils.ComputeLibraryItemFingerprint(playtimeChanged) } });
            Assert.Single(added3);
            Assert.Equal("g2", added3[0].playnite_id);
            Assert.Empty(updated3);
            Assert.Empty(removed3);
        }

        [Fact]
        public void Fingerprint_ChangesWhenPlaytimeChanges() {
            var a = new GameSyncDto {
                playnite_id = "x",
                game_name = "A",
                playtime_seconds = 10,
                play_count = 1,
                plugin_id = "p",
                game_id = "1"
            };
            var b = new GameSyncDto {
                playnite_id = "x",
                game_name = "A",
                playtime_seconds = 11,
                play_count = 1,
                plugin_id = "p",
                game_id = "1"
            };
            Assert.NotEqual(
                GsHashUtils.ComputeLibraryItemFingerprint(a),
                GsHashUtils.ComputeLibraryItemFingerprint(b));
        }

        [Fact]
        public void ApplyAchievementDiff_UpsertsAndRemoves() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "keep", "a" },
                    { "update", "b" },
                    { "remove", "c" }
                });

                Assert.True(GsSyncHashIndex.ApplyAchievementDiff(
                    new Dictionary<string, string> {
                        { "new", "d" },
                        { "update", "e" }
                    },
                    new List<string> { "remove" }));

                var fps = GsSyncHashIndex.GetAchievementFingerprints();
                Assert.Equal(3, fps.Count);
                Assert.Equal("a", fps["keep"]);
                Assert.Equal("e", fps["update"]);
                Assert.Equal("d", fps["new"]);
                Assert.False(fps.ContainsKey("remove"));
            }
        }

        [Fact]
        public void ClearAchievementIndex_ResetsBaseline() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "g", "fp" }
                });
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.True(GsSyncHashIndex.ClearAchievementIndex());
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
        }

        [Fact]
        public void ClearAll_ResetsBothBaselines() {
            using (var temp = TempPluginDir.CreateWithHashIndex()) {
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { { "g", "fp" } });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { { "g", "fp" } });
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);

                Assert.True(GsSyncHashIndex.ClearAll());

                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
        }

        [Fact]
        public void Reset_BeforeInitialize_DoesNotThrow() {
            // Identity rotation (GsDataManager.RotateInstallId) can reach Reset() before the index
            // is initialized; the legacy manager never threw here, so neither may this.
            var ex = Record.Exception(() => GsSyncHashIndex.Reset());
            Assert.Null(ex);
        }

        [Fact]
        public void LibraryFingerprintFromSnapshot_MatchesLive_ForRoundtripDate() {
            // Legacy snapshots persisted last_activity via DateTime.ToString("o") (fractional
            // seconds); the live fingerprint uses second-precision UTC. Migration must normalize
            // the stored string so the migrated fingerprint equals the live one — otherwise every
            // played game is flagged 'updated' on the first post-migration diff.
            var live = new GameSyncDto {
                playnite_id = "g1",
                game_name = "Game",
                playtime_seconds = 100,
                play_count = 2,
                last_activity = new DateTime(2024, 5, 1, 12, 34, 56, DateTimeKind.Utc),
                plugin_id = "p",
                game_id = "1"
            };
            var liveFp = GsHashUtils.ComputeLibraryItemFingerprint(live);

            var snap = new GameSnapshot {
                playnite_id = "g1",
                playtime_seconds = 100,
                play_count = 2,
                last_activity = new DateTime(2024, 5, 1, 12, 34, 56, DateTimeKind.Utc).ToString("o"),
                metadata_hash = GsHashUtils.ComputeGameMetadataHash(live)
            };

            Assert.Equal(liveFp, GsHashUtils.LibraryFingerprintFromSnapshot(snap));
        }

        [Fact]
        public void AchievementFingerprint_ChangesOnRarityAndUnlockSwap() {
            GameAchievementsDto Make(bool aUnlocked, bool bUnlocked, float aRarity) =>
                new GameAchievementsDto {
                    playnite_id = "g",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto { name = "A", is_unlocked = aUnlocked, rarity_percent = aRarity },
                        new AchievementItemDto { name = "B", is_unlocked = bUnlocked, rarity_percent = 50f }
                    }
                };

            var baseFp = GsHashUtils.ComputeAchievementGameFingerprint(Make(true, false, 10f));
            // Rarity-only change (name/count/unlocked-count all identical) must be detected.
            Assert.NotEqual(baseFp, GsHashUtils.ComputeAchievementGameFingerprint(Make(true, false, 20f)));
            // Unlock swap: A locks, B unlocks — unlocked count unchanged — must be detected.
            Assert.NotEqual(baseFp, GsHashUtils.ComputeAchievementGameFingerprint(Make(false, true, 10f)));
        }

        [Fact]
        public void AchievementFingerprintFromSnapshot_MatchesLive() {
            var live = new GameAchievementsDto {
                playnite_id = "g",
                achievements = new List<AchievementItemDto> {
                    new AchievementItemDto { name = "A", is_unlocked = true, rarity_percent = 12.5f },
                    new AchievementItemDto { name = "B", is_unlocked = false, rarity_percent = null }
                }
            };
            var snap = new GameAchievementSnapshot {
                playnite_id = "g",
                achievements = new List<AchievementSnapshot> {
                    new AchievementSnapshot { name = "A", is_unlocked = true, rarity_percent = 12.5f },
                    new AchievementSnapshot { name = "B", is_unlocked = false, rarity_percent = null }
                }
            };

            Assert.Equal(
                GsHashUtils.ComputeAchievementGameFingerprint(live),
                GsHashUtils.AchievementFingerprintFromSnapshot(snap));
        }
    }

    [Collection("StaticManagerTests")]
    public class GsV4ChunkedSyncClientTests {
        private sealed class TrackingMockApiClient : IGsApiClient {
            public List<string> Calls { get; } = new List<string>();
            public bool FailOnChunkIndex { get; set; }
            public int FailChunkAt { get; set; } = -1;
            public bool FailLibraryCommit { get; set; }
            public bool FailAchievementsCommit { get; set; }
            public string SyncId { get; set; } = "sync-1";

            public Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData) =>
                Task.FromResult(new ScrobbleStartRes { session_id = Guid.NewGuid().ToString() });
            public Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) =>
                Task.FromResult(new ScrobbleFinishRes());

            public Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req) =>
                Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });
            public Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req) =>
                Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

            public Task<V4SyncBeginRes> SyncLibraryFullBegin(LibraryV4FullSyncBeginReq req) {
                Calls.Add("begin");
                return Task.FromResult(new V4SyncBeginRes {
                    success = true,
                    status = "started",
                    sync_id = SyncId,
                    max_chunk_items = 2
                });
            }

            public Task<V4SyncChunkRes> SyncLibraryFullChunk(LibraryV4ChunkReq req) {
                Calls.Add($"chunk:{req.chunk_index}:{req.items?.Count ?? 0}");
                if (FailOnChunkIndex && req.chunk_index == FailChunkAt) {
                    return Task.FromResult(new V4SyncChunkRes {
                        success = false,
                        status = "error",
                        error = "chunk failed"
                    });
                }
                return Task.FromResult(new V4SyncChunkRes {
                    success = true,
                    status = "accepted",
                    sync_id = req.sync_id,
                    chunk_index = req.chunk_index,
                    items_accepted = req.items?.Count ?? 0
                });
            }

            public Task<AsyncQueuedResponse> SyncLibraryFullCommit(LibraryV4CommitReq req) {
                Calls.Add($"commit:{req.chunk_count}:{req.item_count}");
                if (FailLibraryCommit) {
                    return Task.FromResult<AsyncQueuedResponse>(null);
                }
                return Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });
            }

            public Task SyncLibraryFullAbort(string syncId) {
                Calls.Add($"abort:{syncId}");
                return Task.CompletedTask;
            }

            public Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req) =>
                Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });
            public Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req) =>
                Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });
            public Task<V4SyncBeginRes> SyncAchievementsFullBegin(AchievementsV4FullSyncBeginReq req) {
                Calls.Add("ach-begin");
                return Task.FromResult(new V4SyncBeginRes {
                    success = true,
                    status = "started",
                    sync_id = "ach-1",
                    max_chunk_items = 500
                });
            }
            public Task<V4SyncChunkRes> SyncAchievementsFullChunk(AchievementsV4ChunkReq req) {
                Calls.Add($"ach-chunk:{req.chunk_index}:{req.items?.Count ?? 0}");
                return Task.FromResult(new V4SyncChunkRes {
                    success = true,
                    status = "accepted",
                    sync_id = req.sync_id,
                    chunk_index = req.chunk_index,
                    items_accepted = req.items?.Count ?? 0
                });
            }
            public Task<AsyncQueuedResponse> SyncAchievementsFullCommit(AchievementsV4CommitReq req) {
                Calls.Add($"ach-commit:{req.chunk_count}:{req.item_count}");
                return Task.FromResult(FailAchievementsCommit
                    ? new AsyncQueuedResponse { success = false, status = "rejected" }
                    : new AsyncQueuedResponse { success = true, status = "queued" });
            }
            public Task SyncAchievementsFullAbort(string syncId) {
                Calls.Add($"ach-abort:{syncId}");
                return Task.CompletedTask;
            }

            public Task<AllowedPluginsRes> GetAllowedPlugins() =>
                Task.FromResult(new AllowedPluginsRes());
            public Task<TokenVerificationRes> VerifyToken(string token, string playniteId) =>
                Task.FromResult(new TokenVerificationRes());
            public Task FlushPendingScrobblesAsync() => Task.CompletedTask;
            public Task<UnlinkRes> UnlinkAccount() =>
                Task.FromResult(new UnlinkRes { success = true });
            public Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req) =>
                Task.FromResult(new DeleteDataRes { success = true });
            public Task<OptInRes> RequestOptIn(OptInReq req) =>
                Task.FromResult(new OptInRes { success = true });
            public Task<RegisterInstallTokenRes> RegisterInstallToken(string installId) =>
                Task.FromResult(new RegisterInstallTokenRes { success = true, token = "t" });
            public Task<string> GetDashboardToken() => Task.FromResult("tok");
            public Task<PlayniteNotificationsRes> GetNotifications() =>
                Task.FromResult(new PlayniteNotificationsRes());
            public Task<QueueStatusRes> GetQueueStatus(string queueId) =>
                Task.FromResult(new QueueStatusRes {
                    success = true,
                    data = new QueueStatusData { status = "completed" }
                });
        }

        private static GsScrobblingService CreateService(IGsApiClient client) {
            return new GsScrobblingService(client, new StubAchievementProvider(), null);
        }

        private sealed class StubAchievementProvider : IAchievementProvider {
            public bool IsInstalled => false;
            public bool IsPluginLoaded => false;
            public string ProviderName => "stub";
            public string GetVersion() => "0";
            public (int unlocked, int total)? GetCounts(Guid gameId) => null;
            public List<AchievementItem> GetAchievements(Guid gameId) =>
                new List<AchievementItem>();
        }

        [Fact]
        public async Task UploadLibraryFullChunked_OrdersBeginChunksCommit() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var mock = new TrackingMockApiClient();
                var svc = CreateService(mock);
                var items = Enumerable.Range(0, 5)
                    .Select(i => new GameSyncDto {
                        playnite_id = $"g{i}",
                        game_name = $"Game {i}",
                        game_id = i.ToString(),
                        plugin_id = "p",
                        playtime_seconds = i
                    })
                    .ToList();

                var res = await svc.UploadLibraryFullChunkedAsync(items, "hash", new List<IntegrationAccountDto>());

                Assert.NotNull(res);
                Assert.Equal("queued", res.status);
                Assert.Equal(new[] {
                    "begin",
                    "chunk:0:2",
                    "chunk:1:2",
                    "chunk:2:1",
                    "commit:3:5"
                }, mock.Calls);
            }
        }

        [Fact]
        public async Task UploadLibraryFullChunked_FailedChunk_AbortsAndDoesNotCommit() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var mock = new TrackingMockApiClient {
                    FailOnChunkIndex = true,
                    FailChunkAt = 1
                };
                var svc = CreateService(mock);
                var items = Enumerable.Range(0, 5)
                    .Select(i => new GameSyncDto {
                        playnite_id = $"g{i}",
                        game_name = $"Game {i}",
                        game_id = i.ToString(),
                        plugin_id = "p"
                    })
                    .ToList();

                var res = await svc.UploadLibraryFullChunkedAsync(items, "hash", new List<IntegrationAccountDto>());

                Assert.Null(res);
                Assert.Equal(new[] {
                    "begin",
                    "chunk:0:2",
                    "chunk:1:2",
                    "abort:sync-1"
                }, mock.Calls);
                Assert.DoesNotContain(mock.Calls, c => c.StartsWith("commit:"));
            }
        }

        [Fact]
        public async Task UploadLibraryFullChunked_FailedCommit_Aborts() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var mock = new TrackingMockApiClient { FailLibraryCommit = true };
                var res = await CreateService(mock).UploadLibraryFullChunkedAsync(
                    new List<GameSyncDto> { new GameSyncDto { playnite_id = "g" } },
                    "hash",
                    new List<IntegrationAccountDto>());

                Assert.Null(res);
                Assert.Equal(new[] {
                    "begin", "chunk:0:1", "commit:1:1", "abort:sync-1"
                }, mock.Calls);
            }
        }

        [Fact]
        public void CreateV4FullSyncChunks_SplitsBySerializedByteSize() {
            var largeDescription = new string('x', 3 * 1024 * 1024);
            var items = new List<GameAchievementsDto> {
                new GameAchievementsDto {
                    playnite_id = "g1",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto { name = "A", description = largeDescription }
                    }
                },
                new GameAchievementsDto {
                    playnite_id = "g2",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto { name = "B", description = largeDescription }
                    }
                }
            };

            var chunks = GsScrobblingService.CreateV4FullSyncChunks(items, 500);

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, chunk => Assert.Single(chunk));
            Assert.All(chunks, chunk => Assert.True(
                System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(chunk))
                    <= 5 * 1024 * 1024));
        }

        /// <summary>
        /// gs-playnite#89: the only trace of a days-long sync outage a user could send us was
        /// "Library v4 commit failed: status=" three times over. A response that never arrived and a
        /// response the server rejected have to read differently or the log cannot start a diagnosis.
        /// When nothing came back, the API client has already logged the HTTP status and body, so the
        /// line has to send the reader there instead of printing an empty <c>status=</c>.
        /// </summary>
        [Fact]
        public void DescribeV4Failure_NoResponse_PointsAtTheHttpLogLineInsteadOfAnEmptyStatus() {
            var described = GsScrobblingService.DescribeV4Failure(
                responded: false, status: null, error: null, message: null, reason: null);

            Assert.DoesNotContain("status=", described);
            Assert.Contains("no usable response", described);
        }

        /// <summary>
        /// A server that answered but set no status still tells us the request reached it. Printing
        /// "(none)" keeps that case distinguishable from the no-response case, which prints no
        /// <c>status=</c> at all rather than an empty one.
        /// </summary>
        [Fact]
        public void DescribeV4Failure_ResponseWithoutStatus_SaysNoneRatherThanNothing() {
            var described = GsScrobblingService.DescribeV4Failure(
                responded: true, status: null, error: null, message: null, reason: null);

            Assert.Equal("status=(none)", described);
        }

        /// <summary>
        /// Any one of status, error, reason or message may be the only clue the server gave, so all
        /// four have to survive into the line when they are set. This pins the wording a support
        /// reader will be grepping the log for.
        /// </summary>
        [Fact]
        public void DescribeV4Failure_IncludesEveryFieldTheServerSet() {
            var described = GsScrobblingService.DescribeV4Failure(
                responded: true,
                status: "rejected",
                error: "HASH_MISMATCH",
                message: "recomputed digest did not match",
                reason: "hash_mismatch");

            Assert.Equal(
                "status=rejected, error=HASH_MISMATCH, reason=hash_mismatch, "
                    + "message=recomputed digest did not match",
                described);
        }

        /// <summary>
        /// The counterpart: unset fields are dropped rather than printed empty, so a field appearing
        /// in the line means the server actually set it. Trailing "error=" noise is exactly what made
        /// the original lines unreadable.
        /// </summary>
        [Fact]
        public void DescribeV4Failure_OmitsFieldsTheServerLeftUnset() {
            var described = GsScrobblingService.DescribeV4Failure(
                responded: true, status: "rejected", error: null, message: null, reason: "cooldown");

            Assert.Equal("status=rejected, reason=cooldown", described);
        }

        [Fact]
        public async Task UploadAchievementsFullChunked_ItemOverByteLimit_AbortsWithoutSendingChunk() {
            var mock = new TrackingMockApiClient();
            var items = new List<GameAchievementsDto> {
                new GameAchievementsDto {
                    playnite_id = "g",
                    achievements = new List<AchievementItemDto> {
                        new AchievementItemDto {
                            name = "oversized",
                            description = new string('x', 5 * 1024 * 1024)
                        }
                    }
                }
            };

            var res = await CreateService(mock).UploadAchievementsFullChunkedAsync(items, "hash");

            Assert.Null(res);
            Assert.Equal(new[] { "ach-begin", "ach-abort:ach-1" }, mock.Calls);
        }

        [Fact]
        public async Task UploadAchievementsFullChunked_RejectedCommit_Aborts() {
            var mock = new TrackingMockApiClient { FailAchievementsCommit = true };
            var res = await CreateService(mock).UploadAchievementsFullChunkedAsync(
                new List<GameAchievementsDto> { new GameAchievementsDto { playnite_id = "g" } },
                "hash");

            Assert.Null(res);
            Assert.Equal(new[] {
                "ach-begin", "ach-chunk:0:1", "ach-commit:1:1", "ach-abort:ach-1"
            }, mock.Calls);
        }
    }
}
