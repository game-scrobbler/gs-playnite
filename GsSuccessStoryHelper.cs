using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace GsPlugin {
    /// <summary>
    /// Retrieves per-game achievement data from the SuccessStory plugin via reflection.
    /// All methods return null if SuccessStory is not installed or an error occurs.
    /// </summary>
    public class GsSuccessStoryHelper {
        private static readonly Guid SuccessStoryId = new Guid(
            "cebe6d32-8c46-4459-b993-5a5189d60788"
        );

        private readonly IPlayniteAPI _api;
        private Plugin _cachedPlugin;
        private bool _pluginSearched;

        public GsSuccessStoryHelper(IPlayniteAPI api) {
            _api = api;
        }

        public int? GetUnlockedCount(Guid gameId) => GetAchievementCounts(gameId)?.unlocked;

        public int? GetTotalCount(Guid gameId) => GetAchievementCounts(gameId)?.total;

        public bool IsInstalled => GetSuccessStoryPlugin() != null;

        public string GetVersion() {
            try {
                var plugin = GetSuccessStoryPlugin();
                if (plugin == null)
                    return null;
                return plugin.GetType().Assembly.GetName().Version?.ToString(3);
            }
            catch (Exception ex) {
                GsLogger.Warn(
                    $"[GsSuccessStoryHelper] Version lookup failed: {ex.Message}"
                );
                return null;
            }
        }

        private (int unlocked, int total)? GetAchievementCounts(Guid gameId) {
            try {
                var plugin = GetSuccessStoryPlugin();
                if (plugin == null) {
                    return null;
                }

                var dbProp = plugin
                    .GetType()
                    .GetProperty("PluginDatabase", BindingFlags.Public | BindingFlags.Instance);
                var db = dbProp?.GetValue(plugin);
                if (db == null) {
                    return null;
                }

                var getMethod = db.GetType()
                    .GetMethod(
                        "Get",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(Guid), typeof(bool), typeof(bool) },
                        null
                    );
                var ga = getMethod?.Invoke(db, new object[] { gameId, true, false });
                if (ga == null) {
                    return null;
                }

                var gaType = ga.GetType();
                var unlocked = (int?)gaType.GetProperty("Unlocked")?.GetValue(ga) ?? 0;
                var items = gaType.GetProperty("Items")?.GetValue(ga) as ICollection;
                var total = items?.Count ?? 0;
                return (unlocked, total);
            }
            catch (Exception ex) {
                GsLogger.Warn(
                    $"[GsSuccessStoryHelper] Achievement lookup failed for game {gameId}: {ex.Message}"
                );
                return null;
            }
        }

        private Plugin GetSuccessStoryPlugin() {
            if (_pluginSearched) {
                return _cachedPlugin;
            }

            _pluginSearched = true;
            _cachedPlugin = _api.Addons.Plugins.FirstOrDefault(p => p.Id == SuccessStoryId);
            return _cachedPlugin;
        }
    }
}
