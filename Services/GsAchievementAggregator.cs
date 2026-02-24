using System;
using System.Collections.Generic;
using System.Linq;

namespace GsPlugin.Services {
    /// <summary>
    /// Aggregates multiple achievement providers (e.g. SuccessStory, Playnite Achievements).
    /// For each game, returns data from the first provider that has it.
    /// </summary>
    public class GsAchievementAggregator : IAchievementProvider {
        private readonly List<IAchievementProvider> _providers;

        public GsAchievementAggregator(params IAchievementProvider[] providers) {
            _providers = providers?.ToList() ?? new List<IAchievementProvider>();
        }

        public string ProviderName => "Aggregator";

        public bool IsInstalled => _providers.Any(p => p.IsInstalled);

        public string GetVersion() => null;

        /// <summary>
        /// Returns both counts atomically from the first provider that has data,
        /// preventing cross-provider mixing. Skips (0, 0) results so providers
        /// that return an empty game-achievements object don't block fallback.
        /// </summary>
        public (int unlocked, int total)? GetCounts(Guid gameId) {
            foreach (var p in _providers) {
                if (!p.IsInstalled) continue;
                var counts = p.GetCounts(gameId);
                if (counts.HasValue && counts.Value.total > 0) return counts;
            }
            return null;
        }

        public int? GetUnlockedCount(Guid gameId) => GetCounts(gameId)?.unlocked;

        public int? GetTotalCount(Guid gameId) => GetCounts(gameId)?.total;

        public List<AchievementItem> GetAchievements(Guid gameId) {
            foreach (var p in _providers) {
                if (!p.IsInstalled) continue;
                var achievements = p.GetAchievements(gameId);
                if (achievements != null) return achievements;
            }
            return null;
        }

        /// <summary>
        /// Returns all providers that are currently installed. Used by the settings UI.
        /// </summary>
        public List<IAchievementProvider> GetInstalledProviders() {
            return _providers.Where(p => p.IsInstalled).ToList();
        }
    }
}
