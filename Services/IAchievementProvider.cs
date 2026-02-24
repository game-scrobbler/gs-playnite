using System;
using System.Collections.Generic;

namespace GsPlugin.Services {
    /// <summary>
    /// Shared data type returned by all achievement providers.
    /// </summary>
    public struct AchievementItem {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime? DateUnlocked { get; set; }
        public bool IsUnlocked { get; set; }
        public float? RarityPercent { get; set; }
    }

    /// <summary>
    /// Abstraction for plugins that provide per-game achievement data (e.g. SuccessStory, Playnite Achievements).
    /// All methods return null when the provider is not installed or the game has no data.
    /// </summary>
    public interface IAchievementProvider {
        bool IsInstalled { get; }
        string ProviderName { get; }
        string GetVersion();

        /// <summary>
        /// Returns both unlocked and total counts atomically from one lookup,
        /// or null if the provider has no data for this game.
        /// </summary>
        (int unlocked, int total)? GetCounts(Guid gameId);

        int? GetUnlockedCount(Guid gameId);
        int? GetTotalCount(Guid gameId);
        List<AchievementItem> GetAchievements(Guid gameId);
    }
}
