using System;
using System.IO;
using System.Linq;
using Xunit;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    public class SuccessStoryFileReaderTests : IDisposable {
        private readonly TempPluginDir _temp;
        private readonly string _tempDir;

        public SuccessStoryFileReaderTests() {
            _temp = TempPluginDir.Create("gs-test-ss-");
            _tempDir = _temp.Path;
        }

        public void Dispose() {
            _temp.Dispose();
        }

        private GsSuccessStoryHelper CreateHelper() {
            return new GsSuccessStoryHelper(_tempDir);
        }

        [Fact]
        public void IsInstalled_ReturnsTrueWhenDirectoryExists() {
            var helper = CreateHelper();
            Assert.True(helper.IsInstalled);
        }

        [Fact]
        public void IsInstalled_ReturnsFalseWhenDirectoryMissing() {
            var helper = new GsSuccessStoryHelper(Path.Combine(_tempDir, "nonexistent"));
            Assert.False(helper.IsInstalled);
        }

        [Fact]
        public void IsInstalled_ReturnsFalseWhenPathNull() {
            var helper = new GsSuccessStoryHelper((string)null);
            Assert.False(helper.IsInstalled);
        }

        [Fact]
        public void GetAchievements_ReturnsNullForMissingFile() {
            var helper = CreateHelper();
            var result = helper.GetAchievements(Guid.NewGuid());
            Assert.Null(result);
        }

        [Fact]
        public void GetAchievements_ReturnsNullForEmptyItems() {
            var gameId = Guid.NewGuid();
            var json = @"{""IsManual"":false,""IsIgnored"":false,""Items"":[],""Id"":""" + gameId + @""",""Name"":""Test Game""}";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);
            Assert.Null(result);
            var read = helper.ReadAchievements(gameId);
            Assert.True(read.IsAvailable);
            Assert.Empty(read.Achievements);
        }

        [Fact]
        public void GetAchievements_ReturnsNullWhenIsIgnored() {
            var gameId = Guid.NewGuid();
            var json = @"{""IsIgnored"":true,""Items"":[{""Name"":""Test"",""DateUnlocked"":""2025-01-15T10:00:00Z"",""Percent"":5.0}],""Id"":""" + gameId + @"""}";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            Assert.Null(helper.GetAchievements(gameId));
        }

        [Fact]
        public void GetAchievements_ParsesUnlockedAchievement() {
            var gameId = Guid.NewGuid();
            var json = @"{
                ""IsIgnored"": false,
                ""Items"": [
                    {
                        ""Name"": ""First Blood"",
                        ""Description"": ""Win your first match"",
                        ""DateUnlocked"": ""2025-06-01T12:00:00Z"",
                        ""Percent"": 42.5
                    }
                ],
                ""Id"": """ + gameId + @"""
            }";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("First Blood", result[0].Name);
            Assert.Equal("Win your first match", result[0].Description);
            Assert.True(result[0].IsUnlocked);
            Assert.NotNull(result[0].DateUnlocked);
            Assert.Equal(42.5f, result[0].RarityPercent);
        }

        [Fact]
        public void GetAchievements_ParsesLockedAchievement() {
            var gameId = Guid.NewGuid();
            var json = @"{
                ""Items"": [
                    {
                        ""Name"": ""Locked One"",
                        ""DateUnlocked"": ""0001-01-01T00:00:00"",
                        ""Percent"": 100.0
                    }
                ]
            }";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("Locked One", result[0].Name);
            Assert.False(result[0].IsUnlocked);
            Assert.Null(result[0].DateUnlocked);
        }

        [Fact]
        public void GetAchievements_MixedUnlockedAndLocked() {
            var gameId = Guid.NewGuid();
            var json = @"{
                ""Items"": [
                    { ""Name"": ""A"", ""DateUnlocked"": ""2025-03-10T08:00:00Z"", ""Percent"": 10.0 },
                    { ""Name"": ""B"", ""DateUnlocked"": ""0001-01-01T00:00:00"", ""Percent"": 80.0 },
                    { ""Name"": ""C"", ""DateUnlocked"": ""2025-05-20T16:30:00Z"", ""Percent"": 5.0 }
                ]
            }";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Equal(3, result.Count);
            Assert.True(result[0].IsUnlocked);
            Assert.False(result[1].IsUnlocked);
            Assert.True(result[2].IsUnlocked);
        }

        [Fact]
        public void GetCounts_ReturnsCorrectValues() {
            var gameId = Guid.NewGuid();
            var json = @"{
                ""Items"": [
                    { ""Name"": ""A"", ""DateUnlocked"": ""2025-03-10T08:00:00Z"" },
                    { ""Name"": ""B"", ""DateUnlocked"": ""0001-01-01T00:00:00"" },
                    { ""Name"": ""C"", ""DateUnlocked"": ""2025-05-20T16:30:00Z"" }
                ]
            }";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var counts = helper.GetCounts(gameId);

            Assert.NotNull(counts);
            Assert.Equal(2, counts.Value.unlocked);
            Assert.Equal(3, counts.Value.total);
        }

        [Fact]
        public void GetAchievements_ReturnsNullForMalformedJson() {
            var gameId = Guid.NewGuid();
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), "{ not valid json");

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);
            Assert.Null(result);
            Assert.False(helper.ReadAchievements(gameId).IsAvailable);
        }

        [Fact]
        public void GetAchievements_HandlesNullDateUnlocked() {
            var gameId = Guid.NewGuid();
            var json = @"{
                ""Items"": [
                    { ""Name"": ""No Date"", ""Percent"": 50.0 }
                ]
            }";
            File.WriteAllText(Path.Combine(_tempDir, $"{gameId}.json"), json);

            var helper = CreateHelper();
            var result = helper.GetAchievements(gameId);

            Assert.NotNull(result);
            Assert.Single(result);
            Assert.False(result[0].IsUnlocked);
            Assert.Null(result[0].DateUnlocked);
        }

        [Fact]
        public void ProviderName_IsSuccessStory() {
            var helper = CreateHelper();
            Assert.Equal("SuccessStory", helper.ProviderName);
        }
    }
}
