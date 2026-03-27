using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Manages the set of Playnite library plugin IDs that are allowed for scrobbling.
    /// Lazy-initialized from disk cache or hardcoded fallback, refreshed at runtime from the server.
    /// </summary>
    internal static class GsAllowedPlugins {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Hardcoded fallback list of official Playnite library plugin IDs.
        /// Used when the server endpoint is unreachable and no disk cache exists.
        /// </summary>
        private static readonly HashSet<Guid> HardcodedPluginIds = new HashSet<Guid> {
            Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB"), // Steam
            Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E"), // GOG
            Guid.Parse("00000002-DBD1-46C6-B5D0-B1BA559D10E4"), // Epic Games
            Guid.Parse("7E4FBB5E-2AE3-48D4-8BA0-6B30E7A4E287"), // Xbox
            Guid.Parse("E3C26A3D-D695-4CB7-A769-5FF7612C7EDD"), // Battle.net
            Guid.Parse("C2F038E5-8B92-4877-91F1-DA9094155FC5"), // Ubisoft Connect
            Guid.Parse("00000001-EBB2-4EEC-ABCB-7C89937A42BB"), // itch.io
            Guid.Parse("96E8C4BC-EC5C-4C8B-87E7-18EE5A690626"), // Humble
            Guid.Parse("402674CD-4AF6-4886-B6EC-0E695BFA0688"), // Amazon Games
            Guid.Parse("85DD7072-2F20-4E76-A007-41035E390724"), // Origin (deprecated, kept for legacy game scrobbling)
            Guid.Parse("0E2E793E-E0DD-4447-835C-C44A1FD506EC"), // Bethesda (deprecated, kept for legacy game scrobbling)
            Guid.Parse("E2A7D494-C138-489D-BB3F-1D786BEEB675"), // Twitch (deprecated, kept for legacy game scrobbling)
            Guid.Parse("E4AC81CB-1B1A-4EC9-8639-9A9633989A71"), // PlayStation
        };

        private static volatile HashSet<Guid> _allowedPluginIds;
        private static readonly object _pluginLock = new object();

        /// <summary>
        /// Dynamic allowed plugin set. Initialized from disk cache or hardcoded fallback.
        /// Updated at runtime via RefreshAsync().
        /// </summary>
        public static HashSet<Guid> AllowedPluginIds {
            get {
                if (_allowedPluginIds != null) return _allowedPluginIds;
                lock (_pluginLock) {
                    if (_allowedPluginIds != null) return _allowedPluginIds;
                    var persisted = GsDataManager.Data.AllowedPlugins;
                    if (persisted != null && persisted.Count > 0) {
                        var parsed = new HashSet<Guid>();
                        foreach (var id in persisted) {
                            if (Guid.TryParse(id, out var guid)) {
                                parsed.Add(guid);
                            }
                        }
                        _allowedPluginIds = parsed.Count > 0 ? parsed : new HashSet<Guid>(HardcodedPluginIds);
                    }
                    else {
                        _allowedPluginIds = new HashSet<Guid>(HardcodedPluginIds);
                    }
                    return _allowedPluginIds;
                }
            }
        }

        /// <summary>
        /// Fetch allowed plugins from server and update the local cache.
        /// Fallback chain: server -> disk cache (24h) -> stale cache -> hardcoded.
        /// </summary>
        public static async Task RefreshAsync(IGsApiClient apiClient) {
            try {
                var response = await apiClient.GetAllowedPlugins();
                if (response?.plugins != null && response.plugins.Count > 0) {
                    var newIds = new HashSet<Guid>();
                    foreach (var plugin in response.plugins) {
                        if (plugin.status == "active" && Guid.TryParse(plugin.pluginId, out var guid)) {
                            newIds.Add(guid);
                        }
                    }

                    if (newIds.Count > 0) {
                        lock (_pluginLock) {
                            _allowedPluginIds = newIds;
                        }

                        var pluginStrings = newIds.Select(g => g.ToString()).ToList();
                        GsDataManager.MutateAndSave(d => {
                            d.AllowedPlugins = pluginStrings;
                            d.AllowedPluginsLastFetched = DateTime.UtcNow;
                        });

                        _logger.Info($"Refreshed allowed plugins from server ({response.source}): {newIds.Count} active plugins");
                    }
                }
            }
            catch (Exception ex) {
                var lastFetched = GsDataManager.Data.AllowedPluginsLastFetched;
                if (lastFetched.HasValue && (DateTime.UtcNow - lastFetched.Value).TotalHours < 24) {
                    _logger.Info("Server unreachable, using cached plugin list (still fresh)");
                }
                else if (GsDataManager.Data.AllowedPlugins?.Count > 0) {
                    _logger.Warn($"Server unreachable, using stale cached plugin list: {ex.Message}");
                }
                else {
                    _logger.Warn($"Failed to fetch allowed plugins, using hardcoded fallback: {ex.Message}");
                }
            }
        }
    }
}
