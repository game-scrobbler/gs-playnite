using System;
using System.Collections.Generic;
using Xunit;

namespace GsPlugin.Tests {
    public class GsMetadataHashTests {
        [Fact]
        public void ComputeGameMetadataHash_DefaultDto_ReturnsConsistentHash() {
            var dto = new GsApiClient.GameSyncDto();
            var hash1 = GsScrobblingService.ComputeGameMetadataHash(dto);
            var hash2 = GsScrobblingService.ComputeGameMetadataHash(dto);

            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void ComputeGameMetadataHash_GameNameChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { game_name = "Original" };
            var after = new GsApiClient.GameSyncDto { game_name = "Renamed" };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_CompletionStatusChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { completion_status_name = "Playing" };
            var after = new GsApiClient.GameSyncDto { completion_status_name = "Completed" };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsInstalledChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { is_installed = false };
            var after = new GsApiClient.GameSyncDto { is_installed = true };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_PlatformChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { platforms = new List<string> { "PC" } };
            var after = new GsApiClient.GameSyncDto { platforms = new List<string> { "PC", "PlayStation" } };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_AchievementCountChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { achievement_count_unlocked = 5, achievement_count_total = 10 };
            var after = new GsApiClient.GameSyncDto { achievement_count_unlocked = 6, achievement_count_total = 10 };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsFavoriteChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { is_favorite = false };
            var after = new GsApiClient.GameSyncDto { is_favorite = true };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_IsHiddenChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { is_hidden = false };
            var after = new GsApiClient.GameSyncDto { is_hidden = true };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_UserScoreChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { user_score = 80 };
            var after = new GsApiClient.GameSyncDto { user_score = 90 };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_CriticScoreChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { critic_score = 75 };
            var after = new GsApiClient.GameSyncDto { critic_score = 85 };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_CommunityScoreChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { community_score = 70 };
            var after = new GsApiClient.GameSyncDto { community_score = 60 };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_ReleaseDateChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { release_date = "2020-01-01" };
            var after = new GsApiClient.GameSyncDto { release_date = "2021-06-15" };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_ReleaseYearChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { release_year = 2020 };
            var after = new GsApiClient.GameSyncDto { release_year = 2021 };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_SourceNameChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { source_name = "Steam" };
            var after = new GsApiClient.GameSyncDto { source_name = "GOG" };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_ModifiedDateChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { modified = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var after = new GsApiClient.GameSyncDto { modified = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_GenreChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { genres = new List<string> { "Action" } };
            var after = new GsApiClient.GameSyncDto { genres = new List<string> { "Action", "RPG" } };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_TagChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { tags = new List<string> { "Singleplayer" } };
            var after = new GsApiClient.GameSyncDto { tags = new List<string> { "Multiplayer" } };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_DateAddedChange_ProducesDifferentHash() {
            var before = new GsApiClient.GameSyncDto { date_added = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var after = new GsApiClient.GameSyncDto { date_added = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) };

            Assert.NotEqual(
                GsScrobblingService.ComputeGameMetadataHash(before),
                GsScrobblingService.ComputeGameMetadataHash(after));
        }

        [Fact]
        public void ComputeGameMetadataHash_NullVsEmptyGenres_ProducesDifferentHash() {
            var withNull = new GsApiClient.GameSyncDto { genres = null };
            var withEmpty = new GsApiClient.GameSyncDto { genres = new List<string>() };

            // null genres serialize as "" while empty list serializes as "" — should be same
            Assert.Equal(
                GsScrobblingService.ComputeGameMetadataHash(withNull),
                GsScrobblingService.ComputeGameMetadataHash(withEmpty));
        }

        [Fact]
        public void ComputeGameMetadataHash_ActivityFieldsIgnored() {
            // Activity fields (playtime, play_count, last_activity) should NOT affect metadata hash
            var dto1 = new GsApiClient.GameSyncDto {
                game_name = "Test Game",
                playtime_seconds = 100,
                play_count = 5,
                last_activity = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            var dto2 = new GsApiClient.GameSyncDto {
                game_name = "Test Game",
                playtime_seconds = 999,
                play_count = 99,
                last_activity = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            Assert.Equal(
                GsScrobblingService.ComputeGameMetadataHash(dto1),
                GsScrobblingService.ComputeGameMetadataHash(dto2));
        }
    }
}
