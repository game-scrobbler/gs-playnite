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
        private static readonly ILogger _logger = LogManager.GetLogger(typeof(GsAllowedPlugins));

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
        private static volatile HashSet<string>? _allowedSourceAliases;
        private static readonly object _pluginLock = new object();
        private static readonly string[] DefaultSourceAliases = new[] {
            "amazon",
            "amazon games",
            "battle.net",
            "battlenet",
            "bethesda",
            "blizzard",
            "ea",
            "ea app",
            "epic",
            "epic games",
            "epic games store",
            "galaxy",
            "gog",
            "gog galaxy",
            "gog oss",
            "humble",
            "humble bundle",
            "itch",
            "itch.io",
            "legendary",
            "origin",
            "playstation",
            "playstation network",
            "psn",
            "steam",
            "steam library",
            "twitch",
            "ubisoft",
            "ubisoft connect",
            "uplay",
            "xbox",
            "xbox live"
        };

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

        private static HashSet<string> AllowedSourceAliases {
            get {
                if (_allowedSourceAliases != null) return _allowedSourceAliases;
                lock (_pluginLock) {
                    if (_allowedSourceAliases != null) return _allowedSourceAliases;
                    _allowedSourceAliases = new HashSet<string>(
                        DefaultSourceAliases.Select(NormalizeSourceName).OfType<string>());
                    return _allowedSourceAliases;
                }
            }
        }

        /// <summary>
        /// Resolves a Game.SourceId to its display name. P11's Game carries only the id, so the
        /// name has to come from ILibraryApi.Sources, which this static class has no access to.
        /// GsScrobblingService installs the resolver once at construction; until then the source
        /// alias fallback is simply skipped and only the plugin-id allowlist applies.
        /// </summary>
        private static volatile Func<string, string?>? _sourceNameResolver;

        public static void ConfigureSourceNameResolver(Func<string, string?>? resolver) {
            _sourceNameResolver = resolver;
        }

        public static bool IsAllowed(Game game) {
            // An empty LibraryId is P11's equivalent of P10's Guid.Empty PluginId: a manual or
            // custom game with no owning library plugin, which must never be sent.
            if (game == null || string.IsNullOrEmpty(game.LibraryId)) {
                return false;
            }

            if (AllowedPluginIds.Contains(game.LibraryId)) {
                return true;
            }

            var sourceName = string.IsNullOrEmpty(game.SourceId)
                ? null
                : _sourceNameResolver?.Invoke(game.SourceId);
            return IsRecognizedSourceName(sourceName);
        }

        internal static bool IsRecognizedSourceName(string? sourceName) {
            var normalized = NormalizeSourceName(sourceName);
            return normalized != null && AllowedSourceAliases.Contains(normalized);
        }

        private static string? NormalizeSourceName(string? sourceName) {
            if (string.IsNullOrWhiteSpace(sourceName)) {
                return null;
            }

            return string.Join(" ", sourceName.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
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
                    var newAliases = new HashSet<string>(
                        DefaultSourceAliases.Select(NormalizeSourceName).OfType<string>());
                    foreach (var plugin in response.plugins) {
                        if (plugin.status == "active" && !string.IsNullOrEmpty(plugin.pluginId)) {
                            newIds.Add(plugin.pluginId);
                        }
                        if (plugin.status == "active" && plugin.sourceAliases != null) {
                            foreach (var alias in plugin.sourceAliases) {
                                var normalizedAlias = NormalizeSourceName(alias);
                                if (normalizedAlias != null) {
                                    newAliases.Add(normalizedAlias);
                                }
                            }
                        }
                    }

                    if (newIds.Count > 0) {
                        lock (_pluginLock) {
                            _allowedPluginIds = newIds;
                            if (newAliases.Count > 0) {
                                _allowedSourceAliases = newAliases;
                            }
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
