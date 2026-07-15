using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsSyncHashIndexTests {
        private static string NewTempDir() {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Initialize_DiscardsIndexWhenIdentityGenerationMismatches() {
            var tempDir = NewTempDir();
            try {
                GsDataManager.Initialize(tempDir, null);
                GsSyncHashIndex.Initialize(tempDir);
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "g1", "fp" }
                }));
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);

                // Bump identity generation as RotateInstallId would
                GsDataManager.MutateAndSave(d => d.IdentityGeneration = d.IdentityGeneration + 1);
                GsSyncHashIndex.Initialize(tempDir);

                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.Equal(0, GsSyncHashIndex.LibraryEntryCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasLibraryBaseline_EmptyIndexWithTimestamp_ReturnsTrue() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string>()));
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.Equal(0, GsSyncHashIndex.LibraryEntryCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void HasLibraryBaseline_NoSync_ReturnsFalse() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetLibraryFingerprints_ReturnsShallowCopy() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
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
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ApplyLibraryDiff_UpsertsAndRemoves() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
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
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ClearLibraryIndex_ResetsBaseline() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "fp" }
                });
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.ClearLibraryIndex());
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Persistence_SurvivesReinitialize() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                Assert.True(GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> {
                    { "game1", "42|1||meta" }
                }));
                Assert.True(GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "game1", "game1:1:0:abc" }
                }));

                GsSyncHashIndex.Initialize(tempDir);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Equal("42|1||meta", GsSyncHashIndex.GetLibraryFingerprints()["game1"]);
                Assert.Equal("game1:1:0:abc", GsSyncHashIndex.GetAchievementFingerprints()["game1"]);
                Assert.True(File.Exists(Path.Combine(tempDir, "gs_library_hashes.json")));
                Assert.True(File.Exists(Path.Combine(tempDir, "gs_achievement_hashes.json")));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Persistence_MigratesLegacyCombinedSnapshot() {
            var tempDir = NewTempDir();
            try {
                // The snapshot's generation must match the current install identity or the
                // migration is (correctly) discarded. Read the current generation rather than
                // re-initializing GsDataManager, which would pollute this shared-static test
                // collection with a data folder that gets deleted in the finally block.
                var gen = GsDataManager.DataOrNull?.IdentityGeneration ?? 0;
                var legacy = new GsSnapshot {
                    IdentityGeneration = gen,
                    LibraryFullSyncAt = DateTime.UtcNow,
                    AchievementsFullSyncAt = DateTime.UtcNow,
                    Library = new Dictionary<string, GameSnapshot> {
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
                    },
                    Achievements = new Dictionary<string, GameAchievementSnapshot> {
                        {
                            "legacy-game",
                            new GameAchievementSnapshot {
                                playnite_id = "legacy-game",
                                achievements = new List<AchievementSnapshot> {
                                    new AchievementSnapshot { name = "Old", is_unlocked = false }
                                }
                            }
                        }
                    }
                };
                var legacyPath = Path.Combine(tempDir, "gs_snapshot.json");
                File.WriteAllText(legacyPath, System.Text.Json.JsonSerializer.Serialize(legacy));

                GsSyncHashIndex.Initialize(tempDir);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                var expectedLib = GsHashUtils.LibraryFingerprintFromSnapshot(legacy.Library["legacy-game"]);
                Assert.Equal(expectedLib, GsSyncHashIndex.GetLibraryFingerprints()["legacy-game"]);
                var expectedAch = GsHashUtils.AchievementFingerprintFromSnapshot(legacy.Achievements["legacy-game"]);
                Assert.Equal(expectedAch, GsSyncHashIndex.GetAchievementFingerprints()["legacy-game"]);
                Assert.True(File.Exists(Path.Combine(tempDir, "gs_library_hashes.json")));
                Assert.True(File.Exists(Path.Combine(tempDir, "gs_achievement_hashes.json")));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Persistence_RecoversAndMigratesLegacyCombinedSnapshotTempFile() {
            var tempDir = NewTempDir();
            try {
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
                var legacyPath = Path.Combine(tempDir, "gs_snapshot.json");
                File.WriteAllText(
                    legacyPath + ".tmp",
                    System.Text.Json.JsonSerializer.Serialize(legacy));

                GsSyncHashIndex.Initialize(tempDir);

                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.Contains("recovered", GsSyncHashIndex.GetLibraryFingerprints().Keys);
                Assert.False(File.Exists(legacyPath));
                Assert.False(File.Exists(legacyPath + ".tmp"));
            }
            finally {
                Directory.Delete(tempDir, true);
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
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
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
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ClearAchievementIndex_ResetsBaseline() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> {
                    { "g", "fp" }
                });
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);
                Assert.True(GsSyncHashIndex.ClearAchievementIndex());
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ClearAll_ResetsBothBaselines() {
            var tempDir = NewTempDir();
            try {
                GsSyncHashIndex.Initialize(tempDir);
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { { "g", "fp" } });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { { "g", "fp" } });
                Assert.True(GsSyncHashIndex.HasLibraryBaseline);
                Assert.True(GsSyncHashIndex.HasAchievementsBaseline);

                Assert.True(GsSyncHashIndex.ClearAll());

                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
                Assert.False(GsSyncHashIndex.HasAchievementsBaseline);
            }
            finally {
                Directory.Delete(tempDir, true);
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
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsDataManager.Initialize(tempDir, null);
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
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task UploadLibraryFullChunked_FailedChunk_AbortsAndDoesNotCommit() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsDataManager.Initialize(tempDir, null);
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
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task UploadLibraryFullChunked_FailedCommit_Aborts() {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            try {
                GsDataManager.Initialize(tempDir, null);
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
            finally {
                Directory.Delete(tempDir, true);
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
