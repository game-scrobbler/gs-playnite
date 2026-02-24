using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using GsPlugin.Infrastructure;

namespace GsPlugin.Services {
    /// <summary>
    /// Retrieves per-game achievement data from the Playnite Achievements plugin via reflection.
    /// All methods return null if Playnite Achievements is not installed or an error occurs.
    /// </summary>
    public class GsPlayniteAchievementsHelper : IAchievementProvider {
        private static readonly Guid PlayniteAchievementsId = new Guid(
            "e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b"
        );

        private readonly IPlayniteAPI _api;
        private Plugin _cachedPlugin;
        private bool _pluginSearched;

        public GsPlayniteAchievementsHelper(IPlayniteAPI api) {
            _api = api;
        }

        public string ProviderName => "Playnite Achievements";

        public bool IsInstalled => GetPlugin() != null;

        public (int unlocked, int total)? GetCounts(Guid gameId) {
            var achievements = GetAchievements(gameId);
            if (achievements == null) return null;
            return (achievements.Count(a => a.IsUnlocked), achievements.Count);
        }

        public int? GetUnlockedCount(Guid gameId) => GetCounts(gameId)?.unlocked;

        public int? GetTotalCount(Guid gameId) => GetCounts(gameId)?.total;

        /// <summary>
        /// Returns per-achievement details for a game, or null if the plugin is absent or the game has no data.
        /// </summary>
        public List<AchievementItem> GetAchievements(Guid gameId) {
            try {
                var plugin = GetPlugin();
                if (plugin == null) {
                    return null;
                }

                // Access plugin.AchievementManager
                var mgrProp = plugin
                    .GetType()
                    .GetProperty("AchievementManager", BindingFlags.Public | BindingFlags.Instance);
                var mgr = mgrProp?.GetValue(plugin);
                if (mgr == null) {
                    return null;
                }

                // Call AchievementManager.GetGameAchievementData(Guid)
                var getMethod = mgr.GetType()
                    .GetMethod(
                        "GetGameAchievementData",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(Guid) },
                        null
                    );
                var gameData = getMethod?.Invoke(mgr, new object[] { gameId });
                if (gameData == null) {
                    return null;
                }

                // Check HasAchievements
                var hasAchievements = gameData.GetType()
                    .GetProperty("HasAchievements")?.GetValue(gameData) as bool?;
                if (hasAchievements != true) {
                    return null;
                }

                // Get Achievements list
                var items = gameData.GetType()
                    .GetProperty("Achievements")?.GetValue(gameData) as IEnumerable;
                if (items == null) {
                    return null;
                }

                var result = new List<AchievementItem>();
                foreach (var item in items) {
                    var itemType = item.GetType();
                    var displayName = itemType.GetProperty("DisplayName")?.GetValue(item) as string;
                    var description = itemType.GetProperty("Description")?.GetValue(item) as string;
                    var dateUnlocked = itemType.GetProperty("DateUnlocked")?.GetValue(item) as DateTime?;
                    var unlocked = itemType.GetProperty("Unlocked")?.GetValue(item) as bool?;
                    var percent = itemType.GetProperty("Percent")?.GetValue(item);

                    bool isUnlocked = unlocked == true;

                    // DateUnlocked may be DateTime.MinValue for locked achievements
                    if (dateUnlocked.HasValue
                        && (dateUnlocked.Value <= DateTime.MinValue || dateUnlocked.Value.Year <= 1)) {
                        dateUnlocked = null;
                    }

                    result.Add(new AchievementItem {
                        Name = displayName,
                        Description = description,
                        DateUnlocked = isUnlocked ? dateUnlocked : null,
                        IsUnlocked = isUnlocked,
                        RarityPercent = percent != null ? Convert.ToSingle(percent) : (float?)null
                    });
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception ex) {
                GsLogger.Warn(
                    $"[GsPlayniteAchievementsHelper] Achievement lookup failed for game {gameId}: {ex.Message}"
                );
                return null;
            }
        }

        public string GetVersion() {
            try {
                var plugin = GetPlugin();
                if (plugin == null)
                    return null;
                return plugin.GetType().Assembly.GetName().Version?.ToString(3);
            }
            catch (Exception ex) {
                GsLogger.Warn(
                    $"[GsPlayniteAchievementsHelper] Version lookup failed: {ex.Message}"
                );
                return null;
            }
        }

        private Plugin GetPlugin() {
            if (_pluginSearched) {
                return _cachedPlugin;
            }

            _pluginSearched = true;
            _cachedPlugin = _api.Addons.Plugins.FirstOrDefault(p => p.Id == PlayniteAchievementsId);
            return _cachedPlugin;
        }
    }
}
