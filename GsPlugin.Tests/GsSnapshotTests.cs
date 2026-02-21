using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsSnapshotBaselineTests {
        /// <summary>
        /// Validates that HasLibraryBaseline uses LibraryFullSyncAt timestamp
        /// rather than dictionary count, so empty libraries are valid baselines.
        /// </summary>
        [Fact]
        public void HasLibraryBaseline_EmptyLibraryWithTimestamp_ReturnsTrue() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);

                // Simulate a full sync that wrote an empty library
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot>());

                Assert.True(GsSnapshotManager.HasLibraryBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Validates that HasAchievementsBaseline uses AchievementsFullSyncAt timestamp
        /// rather than dictionary count, so empty achievement sets are valid baselines.
        /// </summary>
        [Fact]
        public void HasAchievementsBaseline_EmptyAchievementsWithTimestamp_ReturnsTrue() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);

                GsSnapshotManager.UpdateAchievementsSnapshot(new Dictionary<string, GameAchievementSnapshot>());

                Assert.True(GsSnapshotManager.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Validates that HasLibraryBaseline returns false before any full sync.
        /// </summary>
        [Fact]
        public void HasLibraryBaseline_NoSync_ReturnsFalse() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);

                Assert.False(GsSnapshotManager.HasLibraryBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Validates that ClearLibrarySnapshot resets the baseline.
        /// </summary>
        [Fact]
        public void HasAchievementsBaseline_NoSync_ReturnsFalse() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);

                Assert.False(GsSnapshotManager.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetLibrarySnapshot_ReturnsShallowCopy_NotSameReference() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1" } }
                });

                var copy1 = GsSnapshotManager.GetLibrarySnapshot();
                var copy2 = GsSnapshotManager.GetLibrarySnapshot();

                Assert.NotSame(copy1, copy2);
                Assert.Equal(copy1.Count, copy2.Count);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetLibrarySnapshot_MutatingReturnedDict_DoesNotAffectInternal() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1" } }
                });

                var copy = GsSnapshotManager.GetLibrarySnapshot();
                copy.Remove("game1");
                copy.Add("game99", new GameSnapshot { playnite_id = "game99" });

                var fresh = GsSnapshotManager.GetLibrarySnapshot();
                Assert.True(fresh.ContainsKey("game1"));
                Assert.False(fresh.ContainsKey("game99"));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyLibraryDiff_AddsNewGame() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot>());

                GsSnapshotManager.ApplyLibraryDiff(
                    added: new Dictionary<string, GameSnapshot> {
                        { "new-game", new GameSnapshot { playnite_id = "new-game", playtime_seconds = 100 } }
                    },
                    updated: new Dictionary<string, GameSnapshot>(),
                    removed: new List<string>());

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                Assert.True(snapshot.ContainsKey("new-game"));
                Assert.Equal(100, snapshot["new-game"].playtime_seconds);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyLibraryDiff_UpdatesExistingGame() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1", playtime_seconds = 100 } }
                });

                GsSnapshotManager.ApplyLibraryDiff(
                    added: new Dictionary<string, GameSnapshot>(),
                    updated: new Dictionary<string, GameSnapshot> {
                        { "game1", new GameSnapshot { playnite_id = "game1", playtime_seconds = 500 } }
                    },
                    removed: new List<string>());

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                Assert.Equal(500, snapshot["game1"].playtime_seconds);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyLibraryDiff_RemovesDeletedGame() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1" } },
                    { "game2", new GameSnapshot { playnite_id = "game2" } }
                });

                GsSnapshotManager.ApplyLibraryDiff(
                    added: new Dictionary<string, GameSnapshot>(),
                    updated: new Dictionary<string, GameSnapshot>(),
                    removed: new List<string> { "game1" });

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                Assert.False(snapshot.ContainsKey("game1"));
                Assert.True(snapshot.ContainsKey("game2"));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyLibraryDiff_MixedOperations() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "keep", new GameSnapshot { playnite_id = "keep", playtime_seconds = 10 } },
                    { "update", new GameSnapshot { playnite_id = "update", playtime_seconds = 20 } },
                    { "remove", new GameSnapshot { playnite_id = "remove", playtime_seconds = 30 } }
                });

                GsSnapshotManager.ApplyLibraryDiff(
                    added: new Dictionary<string, GameSnapshot> {
                        { "new", new GameSnapshot { playnite_id = "new", playtime_seconds = 99 } }
                    },
                    updated: new Dictionary<string, GameSnapshot> {
                        { "update", new GameSnapshot { playnite_id = "update", playtime_seconds = 200 } }
                    },
                    removed: new List<string> { "remove" });

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                Assert.Equal(3, snapshot.Count);
                Assert.Equal(10, snapshot["keep"].playtime_seconds);
                Assert.Equal(200, snapshot["update"].playtime_seconds);
                Assert.Equal(99, snapshot["new"].playtime_seconds);
                Assert.False(snapshot.ContainsKey("remove"));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void UpdateAchievementsSnapshot_SetsBaselineTimestamp() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                Assert.False(GsSnapshotManager.HasAchievementsBaseline);

                GsSnapshotManager.UpdateAchievementsSnapshot(new Dictionary<string, GameAchievementSnapshot>());

                Assert.True(GsSnapshotManager.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyAchievementsDiff_UpdatesExistingEntry() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateAchievementsSnapshot(new Dictionary<string, GameAchievementSnapshot> {
                    { "game1", new GameAchievementSnapshot {
                        playnite_id = "game1",
                        achievements = new List<AchievementSnapshot> {
                            new AchievementSnapshot { name = "First Win", is_unlocked = false }
                        }
                    }}
                });

                GsSnapshotManager.ApplyAchievementsDiff(
                    new Dictionary<string, GameAchievementSnapshot> {
                        { "game1", new GameAchievementSnapshot {
                            playnite_id = "game1",
                            achievements = new List<AchievementSnapshot> {
                                new AchievementSnapshot { name = "First Win", is_unlocked = true }
                            }
                        }}
                    },
                    new List<string>());

                var snapshot = GsSnapshotManager.GetAchievementsSnapshot();
                Assert.True(snapshot["game1"].achievements[0].is_unlocked);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ClearAchievementsSnapshot_ResetsBaseline() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateAchievementsSnapshot(new Dictionary<string, GameAchievementSnapshot> {
                    { "game1", new GameAchievementSnapshot { playnite_id = "game1" } }
                });

                Assert.True(GsSnapshotManager.HasAchievementsBaseline);

                GsSnapshotManager.ClearAchievementsSnapshot();

                Assert.False(GsSnapshotManager.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Persistence_LibrarySnapshot_SurvivesReinitialize() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1", playtime_seconds = 42 } }
                });

                // Re-initialize from same directory
                GsSnapshotManager.Initialize(tempDir);

                Assert.True(GsSnapshotManager.HasLibraryBaseline);
                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                Assert.True(snapshot.ContainsKey("game1"));
                Assert.Equal(42, snapshot["game1"].playtime_seconds);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ClearLibrarySnapshot_ResetsBaseline() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsSnapshotManager.Initialize(tempDir);
                GsSnapshotManager.UpdateLibrarySnapshot(new Dictionary<string, GameSnapshot> {
                    { "game1", new GameSnapshot { playnite_id = "game1" } }
                });

                Assert.True(GsSnapshotManager.HasLibraryBaseline);

                GsSnapshotManager.ClearLibrarySnapshot();

                Assert.False(GsSnapshotManager.HasLibraryBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
