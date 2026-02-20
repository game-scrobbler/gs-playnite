using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace GsPlugin.Tests {
    public class GsDataTests {
        [Fact]
        public void DefaultValues_AreCorrect() {
            var data = new GsData();
            Assert.Null(data.InstallID);
            Assert.Null(data.ActiveSessionId);
            Assert.Equal("Dark", data.Theme);
            Assert.NotNull(data.Flags);
            Assert.Empty(data.Flags);
            Assert.Null(data.LinkedUserId);
            Assert.False(data.NewDashboardExperience);
            Assert.NotNull(data.AllowedPlugins);
            Assert.Empty(data.AllowedPlugins);
            Assert.Null(data.AllowedPluginsLastFetched);
            Assert.Null(data.LastSyncAt);
            Assert.Null(data.LastSyncGameCount);
        }

        [Fact]
        public void SerializationRoundtrip_PreservesAllFields() {
            var original = new GsData {
                InstallID = "test-id-123",
                ActiveSessionId = "session-456",
                Theme = "Light",
                Flags = new List<string> { "no-sentry", "no-scrobble" },
                LinkedUserId = "user-789",
                NewDashboardExperience = true,
                AllowedPlugins = new List<string> { "plugin-a", "plugin-b" },
                AllowedPluginsLastFetched = new System.DateTime(2025, 1, 15, 10, 30, 0, System.DateTimeKind.Utc)
            };

            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Equal(original.InstallID, deserialized.InstallID);
            Assert.Equal(original.ActiveSessionId, deserialized.ActiveSessionId);
            Assert.Equal(original.Theme, deserialized.Theme);
            Assert.Equal(original.Flags, deserialized.Flags);
            Assert.Equal(original.LinkedUserId, deserialized.LinkedUserId);
            Assert.Equal(original.NewDashboardExperience, deserialized.NewDashboardExperience);
            Assert.Equal(original.AllowedPlugins, deserialized.AllowedPlugins);
            Assert.Equal(original.AllowedPluginsLastFetched, deserialized.AllowedPluginsLastFetched);
        }

        [Fact]
        public void DeserializeUnknownFields_DoesNotThrow() {
            var json = "{\"InstallID\":\"test\",\"UnknownField\":\"value\"}";
            var data = JsonSerializer.Deserialize<GsData>(json);
            Assert.Equal("test", data.InstallID);
        }

        [Fact]
        public void UpdateFlags_ClearsAndSetsCorrectly() {
            var data = new GsData();

            data.UpdateFlags(disableSentry: true, disableScrobbling: false);
            Assert.Single(data.Flags);
            Assert.Contains("no-sentry", data.Flags);

            data.UpdateFlags(disableSentry: false, disableScrobbling: true);
            Assert.Single(data.Flags);
            Assert.Contains("no-scrobble", data.Flags);

            data.UpdateFlags(disableSentry: true, disableScrobbling: true);
            Assert.Equal(2, data.Flags.Count);
            Assert.Contains("no-sentry", data.Flags);
            Assert.Contains("no-scrobble", data.Flags);

            data.UpdateFlags(disableSentry: false, disableScrobbling: false);
            Assert.Empty(data.Flags);
        }

        [Fact]
        public void NotLinkedValue_IsExpectedString() {
            Assert.Equal("not_linked", GsData.NotLinkedValue);
        }

        [Fact]
        public void UpdateFlags_ClearsPreviousFlags() {
            var data = new GsData();
            data.Flags.Add("custom-flag");
            data.Flags.Add("another-flag");

            data.UpdateFlags(disableSentry: true, disableScrobbling: false);

            Assert.Single(data.Flags);
            Assert.DoesNotContain("custom-flag", data.Flags);
            Assert.DoesNotContain("another-flag", data.Flags);
        }

        [Fact]
        public void SerializeEmptyData_ProducesValidJson() {
            var data = new GsData();
            var json = JsonSerializer.Serialize(data);
            Assert.False(string.IsNullOrEmpty(json));

            var deserialized = JsonSerializer.Deserialize<GsData>(json);
            Assert.NotNull(deserialized);
            Assert.Equal("Dark", deserialized.Theme);
        }

        [Fact]
        public void DeserializeEmptyJson_ReturnsDefaults() {
            var data = JsonSerializer.Deserialize<GsData>("{}");
            Assert.NotNull(data);
            Assert.Null(data.InstallID);
            Assert.Equal("Dark", data.Theme);
        }

        [Fact]
        public void DeserializeNullFields_HandlesGracefully() {
            var json = "{\"InstallID\":null,\"LinkedUserId\":null,\"Theme\":null}";
            var data = JsonSerializer.Deserialize<GsData>(json);
            Assert.Null(data.InstallID);
            Assert.Null(data.LinkedUserId);
            Assert.Null(data.Theme);
        }

        [Fact]
        public void FlagsContains_WorksForScrobbleCheck() {
            var data = new GsData();
            Assert.DoesNotContain("no-scrobble", data.Flags);

            data.UpdateFlags(disableSentry: false, disableScrobbling: true);
            Assert.Contains("no-scrobble", data.Flags);
        }

        [Fact]
        public void AllowedPlugins_CanBeSetAndSerialized() {
            var data = new GsData {
                AllowedPlugins = new System.Collections.Generic.List<string> {
                    "CB91DFC9-B977-43BF-8E70-55F46E410FAB",
                    "AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E"
                }
            };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Equal(2, deserialized.AllowedPlugins.Count);
            Assert.Contains("CB91DFC9-B977-43BF-8E70-55F46E410FAB", deserialized.AllowedPlugins);
        }

        [Fact]
        public void AllowedPluginsLastFetched_RoundtripsCorrectly() {
            var now = System.DateTime.UtcNow;
            var data = new GsData { AllowedPluginsLastFetched = now };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.NotNull(deserialized.AllowedPluginsLastFetched);
            // Compare with tolerance since JSON serialization may lose some precision
            Assert.True((now - deserialized.AllowedPluginsLastFetched.Value).TotalSeconds < 1);
        }

        [Fact]
        public void PendingScrobbles_DefaultsToEmpty() {
            var data = new GsData();
            Assert.NotNull(data.PendingScrobbles);
            Assert.Empty(data.PendingScrobbles);
        }

        [Fact]
        public void LastSyncAt_RoundtripsCorrectly() {
            var syncTime = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var data = new GsData { LastSyncAt = syncTime };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.NotNull(deserialized.LastSyncAt);
            Assert.True((syncTime - deserialized.LastSyncAt.Value).TotalSeconds < 1);
        }

        [Fact]
        public void LastSyncGameCount_RoundtripsCorrectly() {
            var data = new GsData { LastSyncGameCount = 42 };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Equal(42, deserialized.LastSyncGameCount);
        }

        [Fact]
        public void LastSyncFields_NullRoundtripsCorrectly() {
            var data = new GsData { LastSyncAt = null, LastSyncGameCount = null };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Null(deserialized.LastSyncAt);
            Assert.Null(deserialized.LastSyncGameCount);
        }

        [Fact]
        public void SerializationRoundtrip_IncludesLastSyncFields() {
            var original = new GsData {
                LastSyncAt = new DateTime(2025, 3, 10, 8, 30, 0, DateTimeKind.Utc),
                LastSyncGameCount = 1234
            };

            var json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Equal(original.LastSyncGameCount, deserialized.LastSyncGameCount);
            Assert.Equal(original.LastSyncAt, deserialized.LastSyncAt);
        }

        [Fact]
        public void PendingScrobbles_SerializationRoundtrip() {
            var data = new GsData {
                PendingScrobbles = new List<PendingScrobble> {
                    new PendingScrobble {
                        Type = "start",
                        StartData = new GsApiClient.ScrobbleStartReq {
                            user_id = "user-1",
                            game_name = "Test Game",
                            game_id = "game-guid-1",
                            plugin_id = "plugin-guid-1",
                            external_game_id = "ext-123",
                            started_at = "2025-01-01T10:00:00+00:00"
                        },
                        QueuedAt = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)
                    },
                    new PendingScrobble {
                        Type = "finish",
                        FinishData = new GsApiClient.ScrobbleFinishReq {
                            user_id = "user-1",
                            session_id = "session-abc",
                            finished_at = "2025-01-01T11:00:00+00:00"
                        },
                        QueuedAt = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc)
                    }
                }
            };

            var json = JsonSerializer.Serialize(data);
            var deserialized = JsonSerializer.Deserialize<GsData>(json);

            Assert.Equal(2, deserialized.PendingScrobbles.Count);
            Assert.Equal("start", deserialized.PendingScrobbles[0].Type);
            Assert.Equal("Test Game", deserialized.PendingScrobbles[0].StartData.game_name);
            Assert.Equal("finish", deserialized.PendingScrobbles[1].Type);
            Assert.Equal("session-abc", deserialized.PendingScrobbles[1].FinishData.session_id);
        }
    }
}
