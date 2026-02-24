using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    /// <summary>
    /// Minimal IAchievementProvider stub for testing the aggregator.
    /// </summary>
    internal class StubAchievementProvider : IAchievementProvider {
        public bool IsInstalled { get; set; }
        public string ProviderName { get; set; } = "Stub";
        public string Version { get; set; }

        /// <summary>
        /// Per-game achievement data. Key = gameId.
        /// </summary>
        public Dictionary<Guid, List<AchievementItem>> Data { get; set; }
            = new Dictionary<Guid, List<AchievementItem>>();

        public string GetVersion() => Version;

        public (int unlocked, int total)? GetCounts(Guid gameId) {
            if (!IsInstalled || !Data.TryGetValue(gameId, out var list))
                return null;
            return (list.Count(a => a.IsUnlocked), list.Count);
        }

        public int? GetUnlockedCount(Guid gameId) => GetCounts(gameId)?.unlocked;
        public int? GetTotalCount(Guid gameId) => GetCounts(gameId)?.total;

        public List<AchievementItem> GetAchievements(Guid gameId) {
            if (!IsInstalled || !Data.TryGetValue(gameId, out var list))
                return null;
            return list.Count > 0 ? list : null;
        }
    }

    public class AchievementItemTests {
        [Fact]
        public void AchievementItem_Defaults() {
            var item = new AchievementItem();
            Assert.Null(item.Name);
            Assert.Null(item.Description);
            Assert.Null(item.DateUnlocked);
            Assert.False(item.IsUnlocked);
            Assert.Null(item.RarityPercent);
        }

        [Fact]
        public void AchievementItem_CanBeConstructed() {
            var date = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var item = new AchievementItem {
                Name = "First Blood",
                Description = "Win your first match",
                DateUnlocked = date,
                IsUnlocked = true,
                RarityPercent = 42.5f
            };

            Assert.Equal("First Blood", item.Name);
            Assert.Equal("Win your first match", item.Description);
            Assert.Equal(date, item.DateUnlocked);
            Assert.True(item.IsUnlocked);
            Assert.Equal(42.5f, item.RarityPercent);
        }
    }

    public class GsAchievementAggregatorTests {
        private static readonly Guid GameA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid GameB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        private static List<AchievementItem> MakeAchievements(int unlocked, int total) {
            var list = new List<AchievementItem>();
            for (int i = 0; i < total; i++) {
                list.Add(new AchievementItem {
                    Name = $"Achievement {i + 1}",
                    IsUnlocked = i < unlocked
                });
            }
            return list;
        }

        [Fact]
        public void IsInstalled_NoProviders_ReturnsFalse() {
            var agg = new GsAchievementAggregator();
            Assert.False(agg.IsInstalled);
        }

        [Fact]
        public void IsInstalled_AllProvidersUninstalled_ReturnsFalse() {
            var p1 = new StubAchievementProvider { IsInstalled = false };
            var p2 = new StubAchievementProvider { IsInstalled = false };
            var agg = new GsAchievementAggregator(p1, p2);
            Assert.False(agg.IsInstalled);
        }

        [Fact]
        public void IsInstalled_OneProviderInstalled_ReturnsTrue() {
            var p1 = new StubAchievementProvider { IsInstalled = false };
            var p2 = new StubAchievementProvider { IsInstalled = true };
            var agg = new GsAchievementAggregator(p1, p2);
            Assert.True(agg.IsInstalled);
        }

        [Fact]
        public void GetCounts_ReturnsFirstProviderWithData() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(3, 10) }
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(7, 20) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var counts = agg.GetCounts(GameA);
            Assert.NotNull(counts);
            Assert.Equal(3, counts.Value.unlocked);
            Assert.Equal(10, counts.Value.total);
        }

        [Fact]
        public void GetCounts_FallsToSecondProvider_WhenFirstHasNoData() {
            var p1 = new StubAchievementProvider { IsInstalled = true };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(5, 15) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var counts = agg.GetCounts(GameA);
            Assert.NotNull(counts);
            Assert.Equal(5, counts.Value.unlocked);
            Assert.Equal(15, counts.Value.total);
        }

        [Fact]
        public void GetCounts_ReturnsNull_WhenNoProviderHasData() {
            var p1 = new StubAchievementProvider { IsInstalled = true };
            var agg = new GsAchievementAggregator(p1);

            Assert.Null(agg.GetCounts(GameA));
        }

        [Fact]
        public void GetCounts_SkipsUninstalledProvider() {
            var p1 = new StubAchievementProvider {
                IsInstalled = false,
                Data = { [GameA] = MakeAchievements(9, 9) }
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(2, 5) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var counts = agg.GetCounts(GameA);
            Assert.NotNull(counts);
            Assert.Equal(2, counts.Value.unlocked);
            Assert.Equal(5, counts.Value.total);
        }

        [Fact]
        public void GetCounts_Atomic_BothValuesFromSameProvider() {
            // Provider 1 has GameA, Provider 2 has GameA with different counts.
            // Verify unlocked and total are from the same provider.
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(1, 3) }
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(10, 50) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var counts = agg.GetCounts(GameA);
            // Must be from p1 (first provider with data), not a mix
            Assert.Equal(1, counts.Value.unlocked);
            Assert.Equal(3, counts.Value.total);
        }

        [Fact]
        public void GetUnlockedCount_DelegatesToGetCounts() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(4, 8) }
            };
            var agg = new GsAchievementAggregator(p1);

            Assert.Equal(4, agg.GetUnlockedCount(GameA));
        }

        [Fact]
        public void GetTotalCount_DelegatesToGetCounts() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(4, 8) }
            };
            var agg = new GsAchievementAggregator(p1);

            Assert.Equal(8, agg.GetTotalCount(GameA));
        }

        [Fact]
        public void GetAchievements_ReturnsFirstProviderWithData() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(2, 3) }
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(5, 10) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var achievements = agg.GetAchievements(GameA);
            Assert.NotNull(achievements);
            Assert.Equal(3, achievements.Count);
        }

        [Fact]
        public void GetAchievements_FallsToSecondProvider() {
            var p1 = new StubAchievementProvider { IsInstalled = true };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(1, 5) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var achievements = agg.GetAchievements(GameA);
            Assert.NotNull(achievements);
            Assert.Equal(5, achievements.Count);
        }

        [Fact]
        public void GetAchievements_ReturnsNull_WhenNoData() {
            var p1 = new StubAchievementProvider { IsInstalled = true };
            var agg = new GsAchievementAggregator(p1);

            Assert.Null(agg.GetAchievements(GameA));
        }

        [Fact]
        public void GetAchievements_SkipsUninstalledProvider() {
            var p1 = new StubAchievementProvider {
                IsInstalled = false,
                Data = { [GameA] = MakeAchievements(5, 5) }
            };
            var agg = new GsAchievementAggregator(p1);

            Assert.Null(agg.GetAchievements(GameA));
        }

        [Fact]
        public void DifferentGames_ResolveIndependently() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = MakeAchievements(1, 2) }
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameB] = MakeAchievements(3, 4) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            // GameA from p1
            var countsA = agg.GetCounts(GameA);
            Assert.Equal(1, countsA.Value.unlocked);
            Assert.Equal(2, countsA.Value.total);

            // GameB from p2
            var countsB = agg.GetCounts(GameB);
            Assert.Equal(3, countsB.Value.unlocked);
            Assert.Equal(4, countsB.Value.total);
        }

        [Fact]
        public void GetInstalledProviders_ReturnsOnlyInstalled() {
            var p1 = new StubAchievementProvider { IsInstalled = true, ProviderName = "A" };
            var p2 = new StubAchievementProvider { IsInstalled = false, ProviderName = "B" };
            var p3 = new StubAchievementProvider { IsInstalled = true, ProviderName = "C" };
            var agg = new GsAchievementAggregator(p1, p2, p3);

            var installed = agg.GetInstalledProviders();
            Assert.Equal(2, installed.Count);
            Assert.Equal("A", installed[0].ProviderName);
            Assert.Equal("C", installed[1].ProviderName);
        }

        [Fact]
        public void GetInstalledProviders_Empty_WhenNoneInstalled() {
            var p1 = new StubAchievementProvider { IsInstalled = false };
            var agg = new GsAchievementAggregator(p1);

            Assert.Empty(agg.GetInstalledProviders());
        }

        [Fact]
        public void GetCounts_SkipsZeroTotalProvider_FallsToNext() {
            // Simulates SuccessStory returning (0, 0) for a game it knows about
            // but has no achievements for — should fall through to next provider.
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                ProviderName = "SuccessStory",
                Data = { [GameA] = new List<AchievementItem>() } // yields (0, 0)
            };
            var p2 = new StubAchievementProvider {
                IsInstalled = true,
                ProviderName = "Playnite Achievements",
                Data = { [GameA] = MakeAchievements(5, 12) }
            };
            var agg = new GsAchievementAggregator(p1, p2);

            var counts = agg.GetCounts(GameA);
            Assert.NotNull(counts);
            Assert.Equal(5, counts.Value.unlocked);
            Assert.Equal(12, counts.Value.total);
        }

        [Fact]
        public void GetCounts_ReturnsNull_WhenAllProvidersReturnZero() {
            var p1 = new StubAchievementProvider {
                IsInstalled = true,
                Data = { [GameA] = new List<AchievementItem>() }
            };
            var agg = new GsAchievementAggregator(p1);

            Assert.Null(agg.GetCounts(GameA));
        }

        [Fact]
        public void GetVersion_ReturnsNull() {
            var agg = new GsAchievementAggregator();
            Assert.Null(agg.GetVersion());
        }

        [Fact]
        public void ProviderName_IsAggregator() {
            var agg = new GsAchievementAggregator();
            Assert.Equal("Aggregator", agg.ProviderName);
        }
    }
}
