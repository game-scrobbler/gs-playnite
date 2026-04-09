using System;
using System.Collections.Generic;
using System.IO;
using Playnite;

namespace GsPlugin.Services {
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
