using System;
using System.Collections.Generic;
using System.IO;
using Playnite.SDK.Plugins;

namespace GsPlugin.Services {
    /// <summary>
    /// A successful read may contain no achievements. An unavailable read must never be
    /// interpreted as a request to delete previously synchronized achievements.
    /// </summary>
    public sealed class AchievementReadResult {
        public bool IsAvailable { get; }
        public List<AchievementItem> Achievements { get; }
        public string ProviderName { get; }

        private AchievementReadResult(bool isAvailable, List<AchievementItem> achievements, string providerName) {
            IsAvailable = isAvailable;
            Achievements = achievements ?? new List<AchievementItem>();
            ProviderName = providerName;
        }

        public static AchievementReadResult Available(List<AchievementItem> achievements, string providerName = null) =>
            new AchievementReadResult(true, achievements, providerName);

        public static AchievementReadResult Unavailable(string providerName = null) =>
            new AchievementReadResult(false, null, providerName);

        public static AchievementReadResult Read(IAchievementProvider provider, Guid gameId) {
            if (provider is IReliableAchievementProvider reliable) {
                return reliable.ReadAchievements(gameId);
            }
            var items = provider.GetAchievements(gameId);
            // Older providers cannot distinguish absence from a read failure. Preserve the
            // existing baseline when their answer is ambiguous.
            return items == null ? Unavailable(provider.ProviderName) : Available(items, provider.ProviderName);
        }
    }

    public interface IReliableAchievementProvider {
        AchievementReadResult ReadAchievements(Guid gameId);
    }

    /// <summary>
    /// Reads the Version field from extension.yaml next to the plugin DLL.
    /// Assembly versions are often wrong in Playnite plugins; extension.yaml is authoritative.
    /// </summary>
    internal static class PluginVersionHelper {
        internal static string GetExtensionYamlVersion(Plugin plugin) {
            try {
                var dllPath = plugin.GetType().Assembly.Location;
                if (string.IsNullOrEmpty(dllPath)) return null;
                var yamlPath = Path.Combine(Path.GetDirectoryName(dllPath), "extension.yaml");
                if (!File.Exists(yamlPath)) return null;
                foreach (var line in File.ReadLines(yamlPath)) {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase)) {
                        return trimmed.Substring("Version:".Length).Trim();
                    }
                }
            }
            catch { }
            return null;
        }
    }

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

        /// <summary>
        /// True when the provider's actual Playnite plugin is currently loaded
        /// (present in <c>Addons.Plugins</c>), as opposed to only having a lingering
        /// data directory on disk after the plugin was uninstalled or disabled.
        /// Data reads work off-disk either way, but a live plugin keeps its data fresh,
        /// so the aggregator prefers live providers to avoid serving stale achievements.
        /// </summary>
        bool IsPluginLoaded { get; }

        string ProviderName { get; }
        string GetVersion();

        /// <summary>
        /// Returns both unlocked and total counts atomically from one lookup,
        /// or null if the provider has no data for this game.
        /// </summary>
        (int unlocked, int total)? GetCounts(Guid gameId);

        List<AchievementItem> GetAchievements(Guid gameId);
    }
}
