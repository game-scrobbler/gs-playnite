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
        private readonly GsApiClient _apiClient;

        /// <summary>
        /// Initializes a new instance of the GsScrobblingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        public GsScrobblingService(GsApiClient apiClient) {
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
            bool oldLinked = !string.IsNullOrEmpty(GsDataManager.Data.LinkedUserId) && GsDataManager.Data.LinkedUserId != "not_linked";
            var oldId = GsDataManager.Data.LinkedUserId;

            // Only set LinkedUserId if it's a valid ID (not "not_linked")
            if (userId == "not_linked" || string.IsNullOrEmpty(userId)) {
                GsDataManager.Data.LinkedUserId = null;
            }
            else {
                GsDataManager.Data.LinkedUserId = userId;
            }
            GsDataManager.Save();

            // Log state changes
            bool newLinked = !string.IsNullOrEmpty(GsDataManager.Data.LinkedUserId) && GsDataManager.Data.LinkedUserId != "not_linked";
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
                _logger.Info($"Starting scrobble session for game: {startedGame.Name} (ID: {startedGame.Id})");
                var sessionData = await _apiClient.StartGameSession(new GsApiClient.ScrobbleStartReq {
                    user_id = GsDataManager.Data.InstallID,
                    game_name = startedGame.Name,
                    game_id = startedGame.Id.ToString(),
                    metadata = new { },
                    started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                }, useAsync: true);
                if (sessionData != null && !string.IsNullOrEmpty(sessionData.session_id)) {
                    SetActiveSession(sessionData.session_id);
                    _logger.Info($"Successfully started scrobble session with ID: {sessionData.session_id}");
                }
                else {
                    _logger.Error($"Failed to start scrobble session for game: {startedGame.Name} (ID: {startedGame.Id})");
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
                _logger.Info($"Stopping scrobble session for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                var finishResponse = await _apiClient.FinishGameSession(new GsApiClient.ScrobbleFinishReq {
                    user_id = GsDataManager.Data.InstallID,
                    game_name = stoppedGame.Name,
                    game_id = stoppedGame.Id.ToString(),
                    session_id = GsDataManager.Data.ActiveSessionId,
                    metadata = new { },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                }, useAsync: true);
                if (finishResponse != null) {
                    // Only clear the session ID if the request was successful
                    ClearActiveSession();
                    _logger.Info($"Successfully finished scrobble session for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                }
                else {
                    _logger.Error($"Failed to finish game session for {stoppedGame.Name} (ID: {stoppedGame.Id})");
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
                    _logger.Error("Failed to finish active session on application stop");
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error cleaning up active session on application stop");
            }
        }

        /// <summary>
        /// Synchronizes the Playnite library with the external API.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        public async Task<bool> SyncLibraryAsync(IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            try {
                _logger.Info("Starting library sync with GameScrobbler API");
                var library = playniteDatabaseGames.ToList();
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
