using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Manages the set of Playnite library plugin IDs that are allowed for scrobbling.
    /// In P11, library plugin IDs are human-readable strings (e.g. "Crow.Steam").
    /// Lazy-initialized from disk cache or hardcoded fallback, refreshed at runtime from the server.
    /// </summary>
    internal static class GsAllowedPlugins {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Hardcoded fallback list of Playnite 11 library plugin IDs.
        /// Used when the server endpoint is unreachable and no disk cache exists.
        /// TODO: Expand as more P11 library plugins ship.
        /// </summary>
        private static readonly HashSet<string> HardcodedPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Crow.Steam",
            "Crow.GOG",
            // Other P11 library plugin IDs will be provided by the server endpoint.
        };

        private static volatile HashSet<string>? _allowedPluginIds;
        private static readonly object _pluginLock = new object();

        /// <summary>
        /// Dynamic allowed plugin set. Initialized from disk cache or hardcoded fallback.
        /// Updated at runtime via RefreshAsync().
        /// </summary>
        public static HashSet<string> AllowedPluginIds {
            get {
                if (_allowedPluginIds != null) return _allowedPluginIds;
                lock (_pluginLock) {
                    if (_allowedPluginIds != null) return _allowedPluginIds;
                    var persisted = GsDataManager.Data.AllowedPlugins;
                    if (persisted != null && persisted.Count > 0) {
                        var loaded = new HashSet<string>(persisted, StringComparer.OrdinalIgnoreCase);
                        _allowedPluginIds = loaded.Count > 0 ? loaded : new HashSet<string>(HardcodedPluginIds, StringComparer.OrdinalIgnoreCase);
                    }
                    else {
                        _allowedPluginIds = new HashSet<string>(HardcodedPluginIds, StringComparer.OrdinalIgnoreCase);
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
                    var newIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var plugin in response.plugins) {
                        if (plugin.status == "active" && !string.IsNullOrEmpty(plugin.pluginId)) {
                            newIds.Add(plugin.pluginId);
                        }
                    }

                    if (newIds.Count > 0) {
                        lock (_pluginLock) {
                            _allowedPluginIds = newIds;
                        }

                        GsDataManager.MutateAndSave(d => {
                            d.AllowedPlugins = newIds.ToList();
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
