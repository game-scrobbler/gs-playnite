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
    }
}
