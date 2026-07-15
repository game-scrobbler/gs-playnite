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

        public bool IsPluginLoaded => _providers.Any(p => p.IsPluginLoaded);

        public string GetVersion() => null;

        /// <summary>
        /// Installed providers in resolution order: live plugins first (their data is
        /// kept fresh), then data-only providers whose plugin is no longer loaded but
        /// whose data directory still lingers on disk (possibly stale). Priority order
        /// is preserved within each group. This prevents a leftover SuccessStory data
        /// folder from shadowing an actually-installed provider such as Playnite
        /// Achievements.
        /// </summary>
        private IEnumerable<IAchievementProvider> ResolutionOrder() {
            return _providers.Where(p => p.IsInstalled && p.IsPluginLoaded)
                .Concat(_providers.Where(p => p.IsInstalled && !p.IsPluginLoaded));
        }

        /// <summary>
        /// Returns both counts atomically from the first provider that has data,
        /// preventing cross-provider mixing. Skips (0, 0) results so providers
        /// that return an empty game-achievements object don't block fallback.
        /// </summary>
        public (int unlocked, int total)? GetCounts(Guid gameId) {
            foreach (var p in ResolutionOrder()) {
                var counts = p.GetCounts(gameId);
                if (counts.HasValue && counts.Value.total > 0) return counts;
            }
            return null;
        }

        public List<AchievementItem> GetAchievements(Guid gameId) {
            foreach (var p in ResolutionOrder()) {
                var achievements = p.GetAchievements(gameId);
                if (achievements != null) return achievements;
            }
            return null;
        }

        /// <summary>
        /// Returns achievements and the name of the provider that supplied them.
        /// Used for diagnostic logging.
        /// </summary>
        public (List<AchievementItem> achievements, string providerName) GetAchievementsWithSource(Guid gameId) {
            foreach (var p in ResolutionOrder()) {
                var achievements = p.GetAchievements(gameId);
                if (achievements != null) return (achievements, p.ProviderName);
            }
            return (null, null);
        }

        /// <summary>
        /// Returns all providers that are currently installed. Used by the settings UI.
        /// </summary>
        public List<IAchievementProvider> GetInstalledProviders() {
            return _providers.Where(p => p.IsInstalled).ToList();
        }
    }
}
