using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using GsPlugin.Api;
using static GsPlugin.Api.GsApiClient;

namespace GsPlugin.Tests {
    /// <summary>
    /// Tests for IGsApiClient signature changes: useAsync parameter removed,
    /// v2 endpoints always used. Validates that the interface and DTOs are
    /// consistent with the simplified async-only contract.
    /// </summary>
    public class GsApiClientValidationTests {
        // --- ScrobbleStartReq DTO tests ---

        [Fact]
        public void ScrobbleStartReq_CanBeConstructed_WithAllFields() {
            var req = new ScrobbleStartReq {
                user_id = "user-123",
                game_name = "Test Game",
                game_id = "game-guid",
                plugin_id = "plugin-guid",
                external_game_id = "ext-456",
                started_at = "2025-01-01T10:00:00+00:00",
                metadata = new { PluginId = "plugin-guid" }
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Equal("Test Game", req.game_name);
            Assert.Equal("game-guid", req.game_id);
            Assert.Equal("plugin-guid", req.plugin_id);
            Assert.Equal("ext-456", req.external_game_id);
            Assert.Equal("2025-01-01T10:00:00+00:00", req.started_at);
            Assert.NotNull(req.metadata);
        }

        [Fact]
        public void ScrobbleStartReq_DefaultsToNull() {
            var req = new ScrobbleStartReq();
            Assert.Null(req.user_id);
            Assert.Null(req.game_name);
            Assert.Null(req.game_id);
            Assert.Null(req.plugin_id);
            Assert.Null(req.external_game_id);
            Assert.Null(req.started_at);
            Assert.Null(req.metadata);
        }

        // --- ScrobbleFinishReq DTO tests ---

        [Fact]
        public void ScrobbleFinishReq_CanBeConstructed_WithAllFields() {
            var req = new ScrobbleFinishReq {
                user_id = "user-123",
                game_name = "Test Game",
                game_id = "game-guid",
                plugin_id = "plugin-guid",
                external_game_id = "ext-456",
                session_id = "session-abc",
                finished_at = "2025-01-01T11:00:00+00:00",
                metadata = new { reason = "application_stopped" }
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Equal("Test Game", req.game_name);
            Assert.Equal("session-abc", req.session_id);
            Assert.Equal("2025-01-01T11:00:00+00:00", req.finished_at);
        }

        [Fact]
        public void ScrobbleFinishReq_DefaultsToNull() {
            var req = new ScrobbleFinishReq();
            Assert.Null(req.user_id);
            Assert.Null(req.session_id);
            Assert.Null(req.finished_at);
        }

        // --- ScrobbleStartRes DTO tests ---

        [Fact]
        public void ScrobbleStartRes_HasSessionId() {
            var id = Guid.NewGuid().ToString();
            var res = new ScrobbleStartRes { session_id = id };
            Assert.Equal(id, res.session_id);
        }

        // --- ScrobbleFinishRes DTO tests ---

        [Fact]
        public void ScrobbleFinishRes_CanBeConstructed() {
            var res = new ScrobbleFinishRes();
            Assert.NotNull(res);
        }

        // --- AsyncQueuedResponse DTO tests ---

        [Fact]
        public void AsyncQueuedResponse_CanBeConstructed() {
            var res = new AsyncQueuedResponse {
                success = true,
                status = "queued",
                queueId = "q-123",
                message = "Queued",
                timestamp = "2025-01-01T10:00:00Z",
                estimatedProcessingTime = "5s"
            };

            Assert.True(res.success);
            Assert.Equal("queued", res.status);
            Assert.Equal("q-123", res.queueId);
        }

        [Fact]
        public void AsyncQueuedResponse_Defaults() {
            var res = new AsyncQueuedResponse();
            Assert.False(res.success);
            Assert.Null(res.status);
            Assert.Null(res.queueId);
        }

        [Fact]
        public void V4BeginDtos_DoNotExposeBodyIdentity() {
            Assert.Null(typeof(LibraryV4FullSyncBeginReq).GetProperty("user_id"));
            Assert.Null(typeof(AchievementsV4FullSyncBeginReq).GetProperty("user_id"));
        }

        // --- IGsApiClient interface contract tests via mock ---

        [Fact]
        public async Task MockClient_StartGameSession_NoUseAsyncParam() {
            // Verifies that the interface no longer has a useAsync parameter —
            // callers must use the single-argument overload only.
            IGsApiClient client = new MockGsApiClient();
            var req = new ScrobbleStartReq { user_id = "u1", game_name = "Game" };

            // This call must compile with exactly one argument (no useAsync)
            var result = await client.StartGameSession(req);
            Assert.NotNull(result);
            Assert.NotNull(result.session_id);
            Assert.True(Guid.TryParse(result.session_id, out _));
        }

        [Fact]
        public async Task MockClient_FinishGameSession_NoUseAsyncParam() {
            IGsApiClient client = new MockGsApiClient();
            var req = new ScrobbleFinishReq {
                user_id = "u1",
                session_id = "session-abc",
                finished_at = "2025-01-01T11:00:00+00:00"
            };

            var result = await client.FinishGameSession(req);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task MockClient_StartGameSession_ReturnsNull_WhenUserIdMissing() {
            IGsApiClient client = new MockGsApiClient(rejectMissingUserId: true);
            var req = new ScrobbleStartReq { user_id = null, game_name = "Game" };

            var result = await client.StartGameSession(req);
            Assert.Null(result);
        }

        [Fact]
        public async Task MockClient_FinishGameSession_ReturnsNull_WhenIdentifiersMissing() {
            IGsApiClient client = new MockGsApiClient(rejectMissingSessionId: true);
            var req = new ScrobbleFinishReq { user_id = "u1", session_id = null, game_name = null };

            var result = await client.FinishGameSession(req);
            Assert.Null(result);
        }

        [Fact]
        public async Task MockClient_FlushPendingScrobblesAsync_DoesNotThrow() {
            IGsApiClient client = new MockGsApiClient();
            // Should complete without throwing
            await client.FlushPendingScrobblesAsync();
        }

        // --- GameSyncDto DTO tests (slim v3 shape — see ADR-011 in gs-mono) ---

        [Fact]
        public void GameSyncDto_AchievementFields_DefaultToNull() {
            var dto = new GameSyncDto();
            Assert.Null(dto.achievement_count_unlocked);
            Assert.Null(dto.achievement_count_total);
        }

        [Fact]
        public void GameSyncDto_CanBePopulated_WithAllFields() {
            var dto = new GameSyncDto {
                game_id = "game-guid",
                plugin_id = "plugin-guid",
                game_name = "Test Game",
                playnite_id = "playnite-user-id",
                playtime_seconds = 3600,
                play_count = 5,
                last_activity = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                is_installed = true,
                completion_status_id = "status-guid",
                completion_status_name = "Completed",
                achievement_count_unlocked = 10,
                achievement_count_total = 50,
                user_score = 85,
                date_added = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                is_favorite = true,
                is_hidden = false,
                source_name = "Steam",
                modified = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            Assert.Equal("Test Game", dto.game_name);
            Assert.Equal(3600, dto.playtime_seconds);
            Assert.Equal(10, dto.achievement_count_unlocked);
            Assert.Equal(50, dto.achievement_count_total);
            Assert.Equal(85, dto.user_score);
            Assert.True(dto.is_favorite);
            Assert.False(dto.is_hidden);
            Assert.Equal("Steam", dto.source_name);
            Assert.NotNull(dto.modified);
        }

        [Fact]
        public void GameSyncDto_MetadataFields_DefaultToNull() {
            var dto = new GameSyncDto();
            Assert.Null(dto.user_score);
            Assert.Null(dto.date_added);
            Assert.Null(dto.source_name);
            Assert.Null(dto.modified);
        }

        // --- v2 Library sync DTO tests ---

        [Fact]
        public void LibraryFullSyncReq_CanBeConstructed() {
            var req = new LibraryFullSyncReq {
                user_id = "user-123",
                library = new List<GameSyncDto> {
                    new GameSyncDto { game_id = "g1", game_name = "Game 1" }
                },
                flags = new[] { "no-sentry" }
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Single(req.library);
            Assert.Single(req.flags);
        }

        [Fact]
        public void LibraryDiffSyncReq_CanBeConstructed() {
            var req = new LibraryDiffSyncReq {
                user_id = "user-123",
                added = new List<GameSyncDto> { new GameSyncDto { game_id = "g1" } },
                updated = new List<GameSyncDto> { new GameSyncDto { game_id = "g2" } },
                removed = new List<string> { "playnite-id-3" },
                base_snapshot_hash = "abc123",
                result_snapshot_hash = "def456",
                flags = new[] { "no-sentry" }
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Single(req.added);
            Assert.Single(req.updated);
            Assert.Single(req.removed);
            Assert.Equal("abc123", req.base_snapshot_hash);
            Assert.Equal("def456", req.result_snapshot_hash);
        }

        // --- v2 Achievement DTO tests ---

        [Fact]
        public void AchievementItemDto_CanBeConstructed() {
            var dto = new AchievementItemDto {
                name = "First Blood",
                description = "Get your first kill",
                date_unlocked = new DateTime(2025, 3, 15, 10, 0, 0, DateTimeKind.Utc),
                is_unlocked = true,
                rarity_percent = 45.5f
            };

            Assert.Equal("First Blood", dto.name);
            Assert.True(dto.is_unlocked);
            Assert.NotNull(dto.date_unlocked);
            Assert.Equal(45.5f, dto.rarity_percent);
        }

        [Fact]
        public void AchievementItemDto_Defaults() {
            var dto = new AchievementItemDto();
            Assert.Null(dto.name);
            Assert.Null(dto.description);
            Assert.Null(dto.date_unlocked);
            Assert.False(dto.is_unlocked);
            Assert.Null(dto.rarity_percent);
        }

        [Fact]
        public void GameAchievementsDto_CanBeConstructed() {
            var dto = new GameAchievementsDto {
                playnite_id = "pid-1",
                game_id = "gid-1",
                plugin_id = "plid-1",
                achievements = new List<AchievementItemDto> {
                    new AchievementItemDto { name = "A1", is_unlocked = true },
                    new AchievementItemDto { name = "A2", is_unlocked = false }
                }
            };

            Assert.Equal("pid-1", dto.playnite_id);
            Assert.Equal(2, dto.achievements.Count);
        }

        [Fact]
        public void AchievementsFullSyncReq_CanBeConstructed() {
            var req = new AchievementsFullSyncReq {
                user_id = "user-123",
                games = new List<GameAchievementsDto> {
                    new GameAchievementsDto {
                        playnite_id = "pid-1",
                        game_id = "gid-1",
                        plugin_id = "plid-1",
                        achievements = new List<AchievementItemDto>()
                    }
                }
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Single(req.games);
        }

        [Fact]
        public void AchievementsDiffSyncReq_CanBeConstructed() {
            var req = new AchievementsDiffSyncReq {
                user_id = "user-123",
                changed = new List<GameAchievementsDto>(),
                base_snapshot_hash = "hash123"
            };

            Assert.Equal("user-123", req.user_id);
            Assert.Empty(req.changed);
            Assert.Equal("hash123", req.base_snapshot_hash);
        }


    }

    /// <summary>
    /// Simple in-memory mock for IGsApiClient, used to verify the simplified
    /// interface contract (no useAsync param) without making real HTTP calls.
    /// </summary>
    internal class MockGsApiClient : IGsApiClient {
        private readonly bool _rejectMissingUserId;
        private readonly bool _rejectMissingSessionId;

        public MockGsApiClient(bool rejectMissingUserId = false, bool rejectMissingSessionId = false) {
            _rejectMissingUserId = rejectMissingUserId;
            _rejectMissingSessionId = rejectMissingSessionId;
        }

        public Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData) {
            if (startData == null || (_rejectMissingUserId && string.IsNullOrEmpty(startData.user_id)))
                return Task.FromResult<ScrobbleStartRes>(null);
            return Task.FromResult(new ScrobbleStartRes { session_id = Guid.NewGuid().ToString() });
        }

        public Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) {
            if (endData == null)
                return Task.FromResult<ScrobbleFinishRes>(null);
            if (_rejectMissingSessionId) {
                var hasValidSessionId = !string.IsNullOrEmpty(endData.session_id) && Guid.TryParse(endData.session_id, out _);
                var hasGameName = !string.IsNullOrEmpty(endData.game_name);
                if (!hasValidSessionId && !hasGameName) {
                    return Task.FromResult<ScrobbleFinishRes>(null);
                }
            }
            return Task.FromResult(new ScrobbleFinishRes());
        }

        public Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task<V4SyncBeginRes> SyncLibraryFullBegin(LibraryV4FullSyncBeginReq req) =>
            Task.FromResult(new V4SyncBeginRes {
                success = true,
                status = "started",
                sync_id = "mock-sync",
                max_chunk_items = 500
            });

        public Task<V4SyncChunkRes> SyncLibraryFullChunk(LibraryV4ChunkReq req) =>
            Task.FromResult(new V4SyncChunkRes {
                success = true,
                status = "accepted",
                sync_id = req?.sync_id,
                chunk_index = req?.chunk_index ?? 0,
                items_accepted = req?.items?.Count ?? 0
            });

        public Task<AsyncQueuedResponse> SyncLibraryFullCommit(LibraryV4CommitReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task SyncLibraryFullAbort(string syncId) => Task.CompletedTask;

        public Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task<V4SyncBeginRes> SyncAchievementsFullBegin(AchievementsV4FullSyncBeginReq req) =>
            Task.FromResult(new V4SyncBeginRes {
                success = true,
                status = "started",
                sync_id = "mock-ach-sync",
                max_chunk_items = 500
            });

        public Task<V4SyncChunkRes> SyncAchievementsFullChunk(AchievementsV4ChunkReq req) =>
            Task.FromResult(new V4SyncChunkRes {
                success = true,
                status = "accepted",
                sync_id = req?.sync_id,
                chunk_index = req?.chunk_index ?? 0,
                items_accepted = req?.items?.Count ?? 0
            });

        public Task<AsyncQueuedResponse> SyncAchievementsFullCommit(AchievementsV4CommitReq req) =>
            Task.FromResult(new AsyncQueuedResponse { success = true, status = "queued" });

        public Task SyncAchievementsFullAbort(string syncId) => Task.CompletedTask;

        public Task<AllowedPluginsRes> GetAllowedPlugins() =>
            Task.FromResult(new AllowedPluginsRes());

        public Task<TokenVerificationRes> VerifyToken(string token, string playniteId) =>
            Task.FromResult(new TokenVerificationRes());

        public Task FlushPendingScrobblesAsync() => Task.CompletedTask;

        public Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req) =>
            Task.FromResult(new DeleteDataRes { success = true, message = "mock" });

        public Task<OptInRes> RequestOptIn(OptInReq req) =>
            Task.FromResult(new OptInRes { success = true, message = "mock" });

        public Task<RegisterInstallTokenRes> RegisterInstallToken(string installId) =>
            Task.FromResult(new RegisterInstallTokenRes { success = true, token = "mock-token" });

        public Task<string> GetDashboardToken() =>
            Task.FromResult("mock-dashboard-token");

        public Task<PlayniteNotificationsRes> GetNotifications() =>
            Task.FromResult(new PlayniteNotificationsRes());

        public Task<UnlinkRes> UnlinkAccount() =>
            Task.FromResult(new UnlinkRes { success = true });
    }
}
