using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace GsPlugin {
    /// <summary>
    /// Service responsible for handling game scrobbling functionality.
    /// Tracks game sessions by recording start/stop events and communicating with the API.
    /// </summary>
    public class GsScrobblingService {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly IGsApiClient _apiClient;

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
        /// Updated at runtime via RefreshAllowedPluginsAsync().
        /// </summary>
        private static HashSet<Guid> AllowedPluginIds {
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
        /// Initializes a new instance of the GsScrobblingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        public GsScrobblingService(IGsApiClient apiClient) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        }

        /// <summary>
        /// Manually clears the active session ID. Use with caution.
        /// </summary>
        private static void ClearActiveSession() {
            if (!string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                _logger.Info("Manually clearing active session ID");
                GsDataManager.Data.ActiveSessionId = null;
                GsDataManager.Save();
            }
        }

        /// <summary>
        /// Sets the active session ID and persists it to storage.
        /// </summary>
        /// <param name="sessionId">The session ID to set as active.</param>
        private static void SetActiveSession(string sessionId) {
            if (string.IsNullOrEmpty(sessionId)) {
                _logger.Warn("Attempted to set empty or null session ID");
                return;
            }

            _logger.Info($"Setting active session ID: {sessionId}");
            GsDataManager.Data.ActiveSessionId = sessionId;
            GsDataManager.Save();
        }

        /// <summary>
        /// Sets the linked user information in the data manager.
        /// </summary>
        /// <param name="userId">The linked user ID, or null if not linked</param>
        private static void SetLinkedUser(string userId = null) {
            bool oldLinked = GsDataManager.IsAccountLinked;
            var oldId = GsDataManager.Data.LinkedUserId;

            // Only set LinkedUserId if it's a valid ID (not the sentinel value)
            if (userId == GsData.NotLinkedValue || string.IsNullOrEmpty(userId)) {
                GsDataManager.Data.LinkedUserId = null;
            }
            else {
                GsDataManager.Data.LinkedUserId = userId;
            }
            GsDataManager.Save();

            // Log state changes
            bool newLinked = GsDataManager.IsAccountLinked;
            if (oldLinked != newLinked) {
                _logger.Info($"User link status changed: {oldLinked} -> {newLinked}");
            }

            if (oldId != GsDataManager.Data.LinkedUserId) {
                _logger.Info($"Linked user ID changed: {oldId ?? "null"} -> {GsDataManager.Data.LinkedUserId ?? "null"}");
            }
        }

        /// <summary>
        /// Handles the game starting event and initiates a new scrobbling session.
        /// </summary>
        /// <param name="args">Event arguments containing game information.</param>
        public async Task OnGameStartAsync(OnGameStartingEventArgs args) {
            try {
                // Skip scrobbling if disabled
                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping game start tracking");
                    return;
                }

                if (args?.Game == null) {
                    _logger.Warn("OnGameStartAsync called with null game; skipping.");
                    return;
                }

                DateTime localDate = DateTime.Now;
                var startedGame = args.Game;

                // Skip scrobbling for unsupported plugins
                if (startedGame.PluginId == Guid.Empty || !AllowedPluginIds.Contains(startedGame.PluginId)) {
                    _logger.Info($"Skipping scrobble start for unsupported plugin: {startedGame.PluginId}");
                    return;
                }

                _logger.Info($"Starting scrobble session for game: {startedGame.Name} (ID: {startedGame.Id})");
                var sessionData = await _apiClient.StartGameSession(new GsApiClient.ScrobbleStartReq {
                    user_id = GsDataManager.Data.InstallID,
                    game_name = startedGame.Name,
                    game_id = startedGame.Id.ToString(),
                    plugin_id = startedGame.PluginId.ToString(),
                    external_game_id = startedGame.GameId,
                    metadata = new { PluginId = startedGame.PluginId.ToString() },
                    started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                }, useAsync: true);
                if (sessionData != null && !string.IsNullOrEmpty(sessionData.session_id)) {
                    SetActiveSession(sessionData.session_id);
                    _logger.Info($"Successfully started scrobble session with ID: {sessionData.session_id}");
                }
                else {
                    _logger.Error($"Failed to start scrobble session for game: {startedGame.Name} (ID: {startedGame.Id}). Queuing for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "start",
                        StartData = new GsApiClient.ScrobbleStartReq {
                            user_id = GsDataManager.Data.InstallID,
                            game_name = startedGame.Name,
                            game_id = startedGame.Id.ToString(),
                            plugin_id = startedGame.PluginId.ToString(),
                            external_game_id = startedGame.GameId,
                            metadata = new { PluginId = startedGame.PluginId.ToString() },
                            started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Error starting scrobble session for game: {args?.Game?.Name ?? "<null>"} (ID: {(args?.Game != null ? args.Game.Id.ToString() : "<null>")})");
            }
        }

        /// <summary>
        /// Handles the game stopped event and finishes the active scrobbling session.
        /// </summary>
        /// <param name="args">Event arguments containing game information.</param>
        public async Task OnGameStoppedAsync(OnGameStoppedEventArgs args) {
            try {
                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping game stop tracking");
                    return;
                }
                if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                    _logger.Warn("No active session ID found when stopping game");
                    return;
                }
                if (args?.Game == null) {
                    _logger.Warn("OnGameStoppedAsync called with null game; skipping.");
                    return;
                }

                DateTime localDate = DateTime.Now;
                var stoppedGame = args.Game;

                // Skip scrobbling for unsupported plugins
                if (stoppedGame.PluginId == Guid.Empty || !AllowedPluginIds.Contains(stoppedGame.PluginId)) {
                    _logger.Info($"Skipping scrobble finish for unsupported plugin: {stoppedGame.PluginId}");
                    // Still clear the active session since we may have tracked start before this filter existed
                    ClearActiveSession();
                    return;
                }

                _logger.Info($"Stopping scrobble session for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                var finishResponse = await _apiClient.FinishGameSession(new GsApiClient.ScrobbleFinishReq {
                    user_id = GsDataManager.Data.InstallID,
                    game_name = stoppedGame.Name,
                    game_id = stoppedGame.Id.ToString(),
                    plugin_id = stoppedGame.PluginId.ToString(),
                    external_game_id = stoppedGame.GameId,
                    session_id = GsDataManager.Data.ActiveSessionId,
                    metadata = new { PluginId = stoppedGame.PluginId.ToString() },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                }, useAsync: true);
                if (finishResponse != null) {
                    // Only clear the session ID if the request was successful
                    ClearActiveSession();
                    _logger.Info($"Successfully finished scrobble session for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                }
                else {
                    _logger.Error($"Failed to finish game session for {stoppedGame.Name} (ID: {stoppedGame.Id}). Queuing for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new GsApiClient.ScrobbleFinishReq {
                            user_id = GsDataManager.Data.InstallID,
                            game_name = stoppedGame.Name,
                            game_id = stoppedGame.Id.ToString(),
                            plugin_id = stoppedGame.PluginId.ToString(),
                            external_game_id = stoppedGame.GameId,
                            session_id = GsDataManager.Data.ActiveSessionId,
                            metadata = new { PluginId = stoppedGame.PluginId.ToString() },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    // Leave ActiveSessionId in place so a manual retry still has the session ID
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Error stopping scrobble session for game: {args?.Game?.Name ?? "<null>"} (ID: {(args?.Game != null ? args.Game.Id.ToString() : "<null>")})");
            }
        }

        /// <summary>
        /// Handles the application stopped event and cleans up any active scrobbling session.
        /// This ensures that if Playnite is closed while a game is running, the session is properly finished.
        /// </summary>
        public async Task OnApplicationStoppedAsync() {
            try {
                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping application stop cleanup");
                    return;
                }
                if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                    _logger.Debug("No active session to clean up on application stop");
                    return;
                }

                _logger.Info("Application stopping with active session, finishing scrobble session");
                DateTime localDate = DateTime.Now;
                var finishResponse = await _apiClient.FinishGameSession(new GsApiClient.ScrobbleFinishReq {
                    user_id = GsDataManager.Data.InstallID,
                    session_id = GsDataManager.Data.ActiveSessionId,
                    metadata = new { reason = "application_stopped" },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                }, useAsync: true);
                if (finishResponse != null) {
                    ClearActiveSession();
                    _logger.Info("Successfully cleaned up active session on application stop");
                }
                else {
                    _logger.Error("Failed to finish active session on application stop. Queuing for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new GsApiClient.ScrobbleFinishReq {
                            user_id = GsDataManager.Data.InstallID,
                            session_id = GsDataManager.Data.ActiveSessionId,
                            metadata = new { reason = "application_stopped" },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error cleaning up active session on application stop");
            }
        }

        /// <summary>
        /// Fetch allowed plugins from server and update the local cache.
        /// Fallback chain: server → disk cache (24h) → stale cache → hardcoded.
        /// </summary>
        public async Task RefreshAllowedPluginsAsync() {
            try {
                var response = await _apiClient.GetAllowedPlugins();
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

                        GsDataManager.Data.AllowedPlugins = newIds.Select(g => g.ToString()).ToList();
                        GsDataManager.Data.AllowedPluginsLastFetched = DateTime.UtcNow;
                        GsDataManager.Save();

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

        /// <summary>
        /// Synchronizes the Playnite library with the external API.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        public async Task<bool> SyncLibraryAsync(IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            try {
                _logger.Info("Starting library sync with GameScrobbler API");
                var allGames = playniteDatabaseGames.ToList();

                // Filter to only supported platform plugins before sending
                var library = allGames
                    .Where(g => g.PluginId != Guid.Empty && AllowedPluginIds.Contains(g.PluginId))
                    .ToList();

                var filteredCount = allGames.Count - library.Count;
                if (filteredCount > 0) {
                    _logger.Info($"Filtered {filteredCount} games from unsupported plugins (sending {library.Count}/{allGames.Count})");
                }

                var syncResponse = await _apiClient.SyncLibrary(new GsApiClient.LibrarySyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    library = library,
                    flags = GsDataManager.Data.Flags.ToArray()
                }, useAsync: true);
                if (syncResponse != null) {
                    _logger.Info("Library sync request queued successfully.");
                    return true;
                }
                else {
                    _logger.Error("Failed to synchronize library with the external API.");
                    return false;
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error synchronizing library with GameScrobbler API");
                return false;
            }
        }
    }
}
