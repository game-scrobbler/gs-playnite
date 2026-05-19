using System;
using System.Collections.Generic;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    public class GsMetadataHashTests {
        [Fact]
        public void ComputeGameMetadataHash_DefaultDto_ReturnsConsistentHash() {
            var dto = new GameSyncDto();
            var hash1 = GsHashUtils.ComputeGameMetadataHash(dto);
            var hash2 = GsHashUtils.ComputeGameMetadataHash(dto);

            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void ComputeGameMetadataHash_GameNameChange_ProducesDifferentHash() {
            var before = new GameSyncDto { game_name = "Original" };
            var after = new GameSyncDto { game_name = "Renamed" };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_CompletionStatusChange_ProducesDifferentHash() {
            var before = new GameSyncDto { completion_status_name = "Playing" };
            var after = new GameSyncDto { completion_status_name = "Completed" };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsInstalledChange_ProducesDifferentHash() {
            var before = new GameSyncDto { is_installed = false };
            var after = new GameSyncDto { is_installed = true };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_AchievementCountChange_ProducesDifferentHash() {
            var before = new GameSyncDto { achievement_count_unlocked = 5, achievement_count_total = 10 };
            var after = new GameSyncDto { achievement_count_unlocked = 6, achievement_count_total = 10 };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsFavoriteChange_ProducesDifferentHash() {
            var before = new GameSyncDto { is_favorite = false };
            var after = new GameSyncDto { is_favorite = true };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsHiddenChange_ProducesDifferentHash() {
            var before = new GameSyncDto { is_hidden = false };
            var after = new GameSyncDto { is_hidden = true };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_UserScoreChange_ProducesDifferentHash() {
            var before = new GameSyncDto { user_score = 80 };
            var after = new GameSyncDto { user_score = 90 };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_SourceNameChange_ProducesDifferentHash() {
            var before = new GameSyncDto { source_name = "Steam" };
            var after = new GameSyncDto { source_name = "GOG" };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_ModifiedDateChange_ProducesDifferentHash() {
            var before = new GameSyncDto { modified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var after = new GameSyncDto { modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_DateAddedChange_ProducesDifferentHash() {
            var before = new GameSyncDto { date_added = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var after = new GameSyncDto { date_added = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) };

            Assert.NotEqual(
                GsHashUtils.ComputeGameMetadataHash(before),
                GsHashUtils.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_ActivityFieldsIgnored() {
            // Activity fields (playtime, play_count, last_activity) should NOT affect metadata hash
            var dto1 = new GameSyncDto {
                game_name = "Test Game",
                playtime_seconds = 100,
                play_count = 5,
                last_activity = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            var dto2 = new GameSyncDto {
                game_name = "Test Game",
                playtime_seconds = 999,
                play_count = 99,
                last_activity = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            Assert.Equal(
                GsHashUtils.ComputeGameMetadataHash(dto1),
                GsHashUtils.ComputeGameMetadataHash(dto2));
        }

        // ──────────────────────────────────────────────────────────
        // Cross-repo hash contract (golden vectors)
        //
        // GsHashUtils.ComputeGameMetadataHash / ComputeLibraryHash must produce
        // byte-identical output to the gs-mono server's computeGameMetadataHashV3 /
        // createLibraryHashV3 (apps/backend/src/services/playnite/playniteUtils/
        // hashUtils.ts). The expected values below match the golden vectors in
        // gs-mono's __tests__/hashUtils.test.ts. If either side changes field
        // order, null handling, separators, or date formatting, one of these
        // assertions fails before the divergence ships and causes permanent
        // force-full-sync loops in production.
        //
        // Note: this plugin only implements the slim v3 recipe. The v2 26-field
        // hash was removed in the v3 migration; gs-mono retains it solely for its
        // still-live v2 endpoints, so the v2 golden vectors are asserted there.
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// The reference game used for golden vectors — every hashed field set
        /// to a non-default value. Pre-image (pipe-joined, SHA-256'd):
        /// "Test Game|cs-1|Playing|1|80|Steam|1|0|2025-12-01T00:00:00Z|2026-01-15T12:00:00Z|10|20"
        /// </summary>
        private static GameSyncDto GoldenVectorGame() => new GameSyncDto {
            playnite_id = "GAME-1",
            game_name = "Test Game",
            playtime_seconds = 3600,
            play_count = 5,
            last_activity = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            is_installed = true,
            completion_status_id = "cs-1",
            completion_status_name = "Playing",
            user_score = 80,
            source_name = "Steam",
            is_favorite = true,
            is_hidden = false,
            date_added = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            modified = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            achievement_count_unlocked = 10,
            achievement_count_total = 20
        };

        [Fact]
        public void ComputeGameMetadataHash_GoldenVector_MatchesServerContract() {
            const string expected =
                "636b397dfab8e80d52528c8737caf9512fe6cf9ecb013e728c84fbf144571141";
            Assert.Equal(expected, GsHashUtils.ComputeGameMetadataHash(GoldenVectorGame()));
        }

        [Fact]
        public void ComputeLibraryHash_GoldenVector_MatchesServerContract() {
            const string expected =
                "b385891b423f7b6f2a149f7ed7022b64b72eb70be743ef4ccb2f2f8ad7a2ee34";
            var library = new List<GameSyncDto> { GoldenVectorGame() };
            Assert.Equal(expected, GsHashUtils.ComputeLibraryHash(library));
        }
    }
}
