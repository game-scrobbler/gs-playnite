using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Events;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Service responsible for handling game scrobbling functionality.
    /// Tracks game sessions by recording start/stop events and communicating with the API.
    /// </summary>
    public class GsScrobblingService {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private readonly IGsApiClient _apiClient;
        private readonly IAchievementProvider _achievementHelper;

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
        /// <param name="achievementHelper">Helper for reading achievement data from the SuccessStory plugin.</param>
        public GsScrobblingService(IGsApiClient apiClient, IAchievementProvider achievementHelper) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _achievementHelper = achievementHelper ?? throw new ArgumentNullException(nameof(achievementHelper));
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
                });
                if (sessionData != null && !string.IsNullOrEmpty(sessionData.session_id)) {
                    SetActiveSession(sessionData.session_id);
                    // Clear any stale pending-start marker so the stop handler uses the
                    // normal active-session path instead of the queued-pair branch.
                    if (GsDataManager.Data.PendingStartGameId != null) {
                        GsDataManager.Data.PendingStartGameId = null;
                        GsDataManager.Save();
                    }
                    _logger.Info($"Successfully started scrobble session with ID: {sessionData.session_id}");
                }
                else {
                    _logger.Error($"Failed to start scrobble session for game: {startedGame.Name} (ID: {startedGame.Id}). Queuing start for retry.");
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
                    // Mark that this game has a queued start so OnGameStoppedAsync can pair it
                    // with a finish even though there is no ActiveSessionId.
                    GsDataManager.Data.PendingStartGameId = startedGame.Id.ToString();
                    GsDataManager.Save();
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
                if (args?.Game == null) {
                    _logger.Warn("OnGameStoppedAsync called with null game; skipping.");
                    return;
                }

                DateTime localDate = DateTime.Now;
                var stoppedGame = args.Game;

                // If the start was queued (failed to send), queue a matching finish so the
                // replay produces a paired session. No API call is needed here.
                var pendingStartGameId = GsDataManager.Data.PendingStartGameId;
                if (!string.IsNullOrEmpty(pendingStartGameId) && pendingStartGameId == stoppedGame.Id.ToString()) {
                    _logger.Info($"Queuing finish to pair with pending start for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new GsApiClient.ScrobbleFinishReq {
                            user_id = GsDataManager.Data.InstallID,
                            game_name = stoppedGame.Name,
                            game_id = stoppedGame.Id.ToString(),
                            plugin_id = stoppedGame.PluginId.ToString(),
                            external_game_id = stoppedGame.GameId,
                            session_id = "queued",
                            metadata = new { PluginId = stoppedGame.PluginId.ToString() },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    GsDataManager.Data.PendingStartGameId = null;
                    GsDataManager.Save();
                    return;
                }

                if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                    _logger.Warn("No active session ID found when stopping game");
                    return;
                }

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
                });
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
                });
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

        public enum SyncLibraryResult {
            Success,
            Cooldown,
            Skipped,
            Error
        }

        /// <summary>
        /// Builds an ISO 8601 date string from a Playnite ReleaseDate struct.
        /// Returns "YYYY-MM-DD" when day and month are known, "YYYY" when only year is known, or null.
        /// </summary>
        private static string BuildReleaseDateString(Playnite.SDK.Models.ReleaseDate? rd) {
            if (!rd.HasValue || rd.Value.Year == 0)
                return null;
            var v = rd.Value;
            if (v.Month > 0 && v.Day > 0)
                return $"{v.Year:D4}-{v.Month:D2}-{v.Day:D2}";
            return v.Year.ToString("D4");
        }

        /// <summary>
        /// Computes a SHA-256 hex digest of the library for change detection.
        /// Includes both activity fields (playtime, play_count, last_activity) and a per-game
        /// metadata hash so that metadata-only changes (renames, genre edits, etc.) are also detected.
        /// </summary>
        /// <summary>
        /// Format a DateTime for hashing — strips fractional seconds for deterministic
        /// cross-platform matching between C# and TypeScript.
        /// Both sides normalize to "yyyy-MM-ddTHH:mm:ssZ" (no fractional seconds, UTC).
        /// </summary>
        private static string FormatDateForHash(DateTime? dt) =>
            dt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "";

        public static string ComputeLibraryHash(List<GsApiClient.GameSyncDto> library) {
            var keys = library
                .Select(g =>
                    $"{g.playnite_id ?? ""}:{g.playtime_seconds}:{g.play_count}:{FormatDateForHash(g.last_activity)}:{ComputeGameMetadataHash(g)}")
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            var separator = new byte[] { (byte)'|' };
            using (var sha256 = SHA256.Create()) {
                foreach (var key in keys) {
                    var bytes = Encoding.UTF8.GetBytes(key);
                    sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    sha256.TransformBlock(separator, 0, 1, null, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Synchronizes the Playnite library with the external API.
        /// Returns <see cref="SyncLibraryResult.Cooldown"/> when the server enforces the 24-hour sync limit.
        /// Returns <see cref="SyncLibraryResult.Skipped"/> when the library hash matches the last sent hash.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        public async Task<SyncLibraryResult> SyncLibraryAsync(IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            try {
                // Client-side cooldown guard: skip the API call if the server told us to wait
                var cooldownExpiry = GsDataManager.Data.SyncCooldownExpiresAt;
                if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                    _logger.Info($"Library sync skipped: client-side cooldown active until {cooldownExpiry.Value:O}");
                    return SyncLibraryResult.Cooldown;
                }

                _logger.Info("Starting library sync with GameScrobbler API");
                var allGames = playniteDatabaseGames.ToList();
                var syncAchievements = GsDataManager.Data.SyncAchievements;

                // Build the DTO list and compute the library hash on a background thread.
                // This avoids blocking the main/UI thread for large libraries (10,000+ games),
                // since the LINQ mapping and achievement lookups are CPU-bound.
                var (library, libraryHash, filteredGames) = await Task.Run(() => {
                    var filtered = allGames
                        .Where(g => g.PluginId != Guid.Empty && AllowedPluginIds.Contains(g.PluginId))
                        .ToList();

                    var dtos = filtered.Select(g => MapGameToDto(g, syncAchievements)).ToList();

                    return (dtos, ComputeLibraryHash(dtos), filtered);
                });

                var filteredCount = allGames.Count - filteredGames.Count;
                if (filteredCount > 0) {
                    _logger.Info($"Filtered {filteredCount} games from unsupported plugins (sending {filteredGames.Count}/{allGames.Count})");
                }

                // Client-side hash guard: skip the API call if the library hasn't changed since the last sync.
                if (libraryHash == GsDataManager.Data.LastLibraryHash) {
                    _logger.Info("Library hash unchanged since last sync — skipping.");
                    return SyncLibraryResult.Skipped;
                }

                var syncResponse = await _apiClient.SyncLibraryFull(new GsApiClient.LibraryFullSyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    library = library,
                    flags = GsDataManager.Data.Flags.ToArray()
                });

                if (syncResponse == null) {
                    _logger.Error("Failed to synchronize library with the external API.");
                    return SyncLibraryResult.Error;
                }

                if (syncResponse.status == "skipped"
                    && syncResponse.reason != null && syncResponse.reason.StartsWith("cooldown_")) {
                    // Server enforced cooldown — persist the expiry so we don't hammer the API
                    HandleCooldownResponse(syncResponse);
                    _logger.Info($"Library sync skipped by server cooldown.");
                    return SyncLibraryResult.Cooldown;
                }

                if (syncResponse.success && syncResponse.status == "queued") {
                    _logger.Info("Library sync request queued successfully.");
                    GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                    GsDataManager.Data.LastSyncGameCount = filteredGames.Count;
                    // Store hash so the next sync can skip if the library hasn't changed
                    GsDataManager.Data.LastLibraryHash = libraryHash;
                    // Sync succeeded — clear any stale cooldown
                    GsDataManager.Data.SyncCooldownExpiresAt = null;
                    GsDataManager.Save();
                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from library sync: status={syncResponse.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error synchronizing library with GameScrobbler API");
                return SyncLibraryResult.Error;
            }
        }

        #region v2 Sync Methods

        /// <summary>
        /// Maps a Playnite Game to the API DTO. Shared by all sync paths.
        /// </summary>
        private GsApiClient.GameSyncDto MapGameToDto(Playnite.SDK.Models.Game g, bool syncAchievements) {
            var achievementCounts = syncAchievements
                ? _achievementHelper.GetCounts(g.Id)
                : null;

            return new GsApiClient.GameSyncDto {
                game_id = g.GameId,
                plugin_id = g.PluginId.ToString(),
                game_name = g.Name,
                playnite_id = g.Id.ToString(),
                playtime_seconds = (long)g.Playtime,
                play_count = (int)g.PlayCount,
                last_activity = g.LastActivity,
                is_installed = g.IsInstalled,
                completion_status_id = g.CompletionStatusId != Guid.Empty
                    ? g.CompletionStatusId.ToString()
                    : null,
                completion_status_name = g.CompletionStatus?.Name,
                achievement_count_unlocked = achievementCounts?.unlocked,
                achievement_count_total = achievementCounts?.total,
                genres = g.Genres != null && g.Genres.Count > 0
                    ? g.Genres.Select(x => x.Name).ToList()
                    : null,
                platforms = g.Platforms != null && g.Platforms.Count > 0
                    ? g.Platforms.Select(x => x.Name).ToList()
                    : null,
                developers = g.Developers != null && g.Developers.Count > 0
                    ? g.Developers.Select(x => x.Name).ToList()
                    : null,
                publishers = g.Publishers != null && g.Publishers.Count > 0
                    ? g.Publishers.Select(x => x.Name).ToList()
                    : null,
                tags = g.Tags != null && g.Tags.Count > 0
                    ? g.Tags.Select(x => x.Name).ToList()
                    : null,
                features = g.Features != null && g.Features.Count > 0
                    ? g.Features.Select(x => x.Name).ToList()
                    : null,
                categories = g.Categories != null && g.Categories.Count > 0
                    ? g.Categories.Select(x => x.Name).ToList()
                    : null,
                series = g.Series != null && g.Series.Count > 0
                    ? g.Series.Select(x => x.Name).ToList()
                    : null,
                user_score = g.UserScore,
                critic_score = g.CriticScore,
                community_score = g.CommunityScore,
                release_year = g.ReleaseDate?.Year,
                date_added = g.Added,
                is_favorite = g.Favorite,
                is_hidden = g.Hidden,
                source_name = g.Source?.Name,
                release_date = BuildReleaseDateString(g.ReleaseDate),
                modified = g.Modified,
                age_ratings = g.AgeRatings != null && g.AgeRatings.Count > 0
                    ? g.AgeRatings.Select(x => x.Name).ToList()
                    : null,
                regions = g.Regions != null && g.Regions.Count > 0
                    ? g.Regions.Select(x => x.Name).ToList()
                    : null
            };
        }

        /// <summary>
        /// Computes a SHA-256 hex digest of per-game metadata for diff detection.
        /// Includes all DTO fields except activity fields (playtime, play_count, last_activity)
        /// which are already covered by the library-level hash key.
        /// </summary>
        public static string ComputeGameMetadataHash(GsApiClient.GameSyncDto g) {
            var sb = new StringBuilder();
            sb.Append(g.game_name ?? "");
            sb.Append('|');
            sb.Append(g.completion_status_id ?? "");
            sb.Append('|');
            sb.Append(g.completion_status_name ?? "");
            sb.Append('|');
            sb.Append(g.is_installed ? "1" : "0");
            sb.Append('|');
            sb.Append(g.genres != null ? string.Join(",", g.genres) : "");
            sb.Append('|');
            sb.Append(g.platforms != null ? string.Join(",", g.platforms) : "");
            sb.Append('|');
            sb.Append(g.developers != null ? string.Join(",", g.developers) : "");
            sb.Append('|');
            sb.Append(g.publishers != null ? string.Join(",", g.publishers) : "");
            sb.Append('|');
            sb.Append(g.tags != null ? string.Join(",", g.tags) : "");
            sb.Append('|');
            sb.Append(g.features != null ? string.Join(",", g.features) : "");
            sb.Append('|');
            sb.Append(g.categories != null ? string.Join(",", g.categories) : "");
            sb.Append('|');
            sb.Append(g.series != null ? string.Join(",", g.series) : "");
            sb.Append('|');
            sb.Append(g.age_ratings != null ? string.Join(",", g.age_ratings) : "");
            sb.Append('|');
            sb.Append(g.regions != null ? string.Join(",", g.regions) : "");
            sb.Append('|');
            sb.Append(g.release_date ?? "");
            sb.Append('|');
            sb.Append(g.release_year?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.user_score?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.critic_score?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.community_score?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.source_name ?? "");
            sb.Append('|');
            sb.Append(g.is_favorite ? "1" : "0");
            sb.Append('|');
            sb.Append(g.is_hidden ? "1" : "0");
            sb.Append('|');
            sb.Append(FormatDateForHash(g.date_added));
            sb.Append('|');
            sb.Append(FormatDateForHash(g.modified));
            sb.Append('|');
            sb.Append(g.achievement_count_unlocked?.ToString() ?? "");
            sb.Append('|');
            sb.Append(g.achievement_count_total?.ToString() ?? "");

            using (var sha256 = SHA256.Create()) {
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Compute a SHA-256 hash of achievement data for change detection.
        /// Per-game key: {playnite_id}:{achievement_count}:{unlocked_count}:{sorted_names_hash}
        /// Keys are sorted ordinally, then hashed with "|" separator.
        /// Must match server's createAchievementHashV2() exactly.
        /// </summary>
        public static string ComputeAchievementHash(List<GsApiClient.GameAchievementsDto> games) {
            var keys = games
                .Select(g => {
                    var achs = g.achievements ?? new List<GsApiClient.AchievementItemDto>();
                    var unlockedCount = achs.Count(a => a.is_unlocked);
                    var sortedNames = string.Join(",", achs.Select(a => a.name).OrderBy(n => n, StringComparer.Ordinal));
                    string namesHash;
                    using (var sha = SHA256.Create()) {
                        var bytes = Encoding.UTF8.GetBytes(sortedNames);
                        var hash = sha.ComputeHash(bytes);
                        namesHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                    return $"{g.playnite_id}:{achs.Count}:{unlockedCount}:{namesHash}";
                })
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

            var separator = new byte[] { (byte)'|' };
            using (var sha256 = SHA256.Create()) {
                foreach (var key in keys) {
                    var bytes = Encoding.UTF8.GetBytes(key);
                    sha256.TransformBlock(bytes, 0, bytes.Length, null, 0);
                    sha256.TransformBlock(separator, 0, 1, null, 0);
                }
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Creates a GameSnapshot from a DTO for the local snapshot store.
        /// </summary>
        private static GameSnapshot BuildGameSnapshot(GsApiClient.GameSyncDto g) {
            return new GameSnapshot {
                playnite_id = g.playnite_id,
                game_id = g.game_id,
                plugin_id = g.plugin_id,
                playtime_seconds = g.playtime_seconds,
                play_count = g.play_count,
                last_activity = g.last_activity?.ToString("o"),
                metadata_hash = ComputeGameMetadataHash(g),
                achievement_count_unlocked = g.achievement_count_unlocked,
                achievement_count_total = g.achievement_count_total
            };
        }

        /// <summary>
        /// Computes the diff between the current library DTOs and the stored snapshot.
        /// </summary>
        private static (List<GsApiClient.GameSyncDto> added, List<GsApiClient.GameSyncDto> updated, List<string> removed)
            ComputeLibraryDiff(List<GsApiClient.GameSyncDto> current, Dictionary<string, GameSnapshot> snapshot) {
            var added = new List<GsApiClient.GameSyncDto>();
            var updated = new List<GsApiClient.GameSyncDto>();

            var currentIds = new HashSet<string>();
            foreach (var g in current) {
                currentIds.Add(g.playnite_id);

                if (!snapshot.TryGetValue(g.playnite_id, out var prev)) {
                    added.Add(g);
                    continue;
                }

                // Check activity fields
                if (g.playtime_seconds != prev.playtime_seconds
                    || g.play_count != prev.play_count
                    || (g.last_activity?.ToString("o") ?? "") != (prev.last_activity ?? "")) {
                    updated.Add(g);
                    continue;
                }

                // Check metadata hash
                var currentMetaHash = ComputeGameMetadataHash(g);
                if (currentMetaHash != prev.metadata_hash) {
                    updated.Add(g);
                }
            }

            var removed = snapshot.Keys
                .Where(id => !currentIds.Contains(id))
                .ToList();

            return (added, updated, removed);
        }

        /// <summary>
        /// Builds the filtered DTO list from Playnite games on a background thread.
        /// Shared by full and diff sync paths.
        /// </summary>
        private async Task<(List<GsApiClient.GameSyncDto> library, string libraryHash, int totalCount, int filteredCount)>
            BuildLibraryDtosAsync(IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            var allGames = playniteDatabaseGames.ToList();
            var syncAchievements = GsDataManager.Data.SyncAchievements;

            var (library, libraryHash, filteredCount) = await Task.Run(() => {
                var filtered = allGames
                    .Where(g => g.PluginId != Guid.Empty && AllowedPluginIds.Contains(g.PluginId))
                    .ToList();

                var dtos = filtered.Select(g => MapGameToDto(g, syncAchievements)).ToList();

                return (dtos, ComputeLibraryHash(dtos), allGames.Count - filtered.Count);
            });

            if (filteredCount > 0) {
                _logger.Info($"Filtered {filteredCount} games from unsupported plugins (sending {library.Count}/{allGames.Count})");
            }

            return (library, libraryHash, allGames.Count, filteredCount);
        }

        /// <summary>
        /// Sends the full library to the v2/library/sync-full endpoint and writes the snapshot.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        /// <param name="bypassCooldown">When true, skip the client-side cooldown check (used when server requests force-full-sync)</param>
        public async Task<SyncLibraryResult> SyncLibraryFullAsync(
            IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames, bool bypassCooldown = false) {
            try {
                if (!bypassCooldown) {
                    var cooldownExpiry = GsDataManager.Data.SyncCooldownExpiresAt;
                    if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                        _logger.Info($"Library full sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                        return SyncLibraryResult.Cooldown;
                    }
                }

                _logger.Info("Starting full library sync (v2)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                // Only skip on hash match if a snapshot baseline exists.
                // Without a baseline, we must proceed to write the snapshot even if the hash matches.
                if (libraryHash == GsDataManager.Data.LastLibraryHash && GsSnapshotManager.HasLibraryBaseline) {
                    _logger.Info("Library hash unchanged since last sync — skipping full sync.");
                    return SyncLibraryResult.Skipped;
                }

                var response = await _apiClient.SyncLibraryFull(new GsApiClient.LibraryFullSyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    library = library,
                    flags = GsDataManager.Data.Flags.ToArray()
                });

                if (response == null) {
                    _logger.Error("Failed to queue full library sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "skipped" && response.reason != null && response.reason.StartsWith("cooldown_")) {
                    HandleCooldownResponse(response);
                    return SyncLibraryResult.Cooldown;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info($"Full library sync queued ({library.Count} games).");
                    GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                    GsDataManager.Data.LastSyncGameCount = library.Count;
                    GsDataManager.Data.LastLibraryHash = libraryHash;
                    GsDataManager.Data.SyncCooldownExpiresAt = null;
                    GsDataManager.Save();

                    // Write full snapshot
                    var snapshotDict = library.ToDictionary(
                        g => g.playnite_id,
                        g => BuildGameSnapshot(g));
                    GsSnapshotManager.UpdateLibrarySnapshot(snapshotDict);

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from full library sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncLibraryFullAsync");
                return SyncLibraryResult.Error;
            }
        }

        /// <summary>
        /// Computes library diff against snapshot and sends to v2/library/sync-diff.
        /// Falls back to full sync if the server requests it.
        /// </summary>
        public async Task<SyncLibraryResult> SyncLibraryDiffAsync(
            IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            try {
                var cooldownExpiry = GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                    _logger.Info($"Library diff sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                    return SyncLibraryResult.Cooldown;
                }

                _logger.Info("Starting diff library sync (v2)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                if (libraryHash == GsDataManager.Data.LastLibraryHash) {
                    _logger.Info("Library hash unchanged since last sync — skipping diff sync.");
                    return SyncLibraryResult.Skipped;
                }

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                var (added, updated, removed) = await Task.Run(() =>
                    ComputeLibraryDiff(library, snapshot));

                if (added.Count == 0 && updated.Count == 0 && removed.Count == 0) {
                    _logger.Info("Library diff is empty — skipping.");
                    GsDataManager.Data.LastLibraryHash = libraryHash;
                    GsDataManager.Save();
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info($"Library diff: {added.Count} added, {updated.Count} updated, {removed.Count} removed");

                var response = await _apiClient.SyncLibraryDiff(new GsApiClient.LibraryDiffSyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    added = added,
                    updated = updated,
                    removed = removed.ToList(),
                    base_snapshot_hash = GsDataManager.Data.LastLibraryHash ?? "",
                    flags = GsDataManager.Data.Flags.ToArray()
                });

                if (response == null) {
                    _logger.Error("Failed to queue library diff sync.");
                    return SyncLibraryResult.Error;
                }

                // Server requests a full sync instead
                if (response.status == "force-full-sync") {
                    _logger.Info($"Server requested full sync (reason: {response.reason}). Falling back.");
                    GsSnapshotManager.ClearLibrarySnapshot();
                    GsDataManager.Data.LastLibraryHash = null;
                    // Clear cooldown so the full-sync fallback isn't blocked
                    GsDataManager.Data.SyncCooldownExpiresAt = null;
                    GsDataManager.Save();
                    return await SyncLibraryFullAsync(playniteDatabaseGames, bypassCooldown: true);
                }

                if (response.status == "skipped" && response.reason != null && response.reason.StartsWith("cooldown_")) {
                    HandleCooldownResponse(response, isDiffSync: true);
                    return SyncLibraryResult.Cooldown;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Library diff sync queued successfully.");
                    GsDataManager.Data.LastSyncAt = DateTime.UtcNow;
                    GsDataManager.Data.LastSyncGameCount = library.Count;
                    GsDataManager.Data.LastLibraryHash = libraryHash;
                    GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt = null;
                    GsDataManager.Save();

                    // Update snapshot with diff
                    var addedSnapshots = added.ToDictionary(g => g.playnite_id, g => BuildGameSnapshot(g));
                    var updatedSnapshots = updated.ToDictionary(g => g.playnite_id, g => BuildGameSnapshot(g));
                    GsSnapshotManager.ApplyLibraryDiff(addedSnapshots, updatedSnapshots, removed);

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from library diff sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncLibraryDiffAsync");
                return SyncLibraryResult.Error;
            }
        }

        /// <summary>
        /// Sends all per-achievement data to v2/achievements/sync-full and writes the achievement snapshot.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        /// <param name="bypassCooldown">When true, skip the client-side cooldown check (used when server requests force-full-sync)</param>
        public async Task<SyncLibraryResult> SyncAchievementsFullAsync(
            IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames, bool bypassCooldown = false) {
            try {
                if (!GsDataManager.Data.SyncAchievements || !_achievementHelper.IsInstalled) {
                    _logger.Info("Achievement sync skipped: disabled or no achievement provider installed.");
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info("Starting full achievements sync (v2)");
                var allGames = playniteDatabaseGames.ToList();

                var games = await Task.Run(() => {
                    return allGames
                        .Where(g => g.PluginId != Guid.Empty && AllowedPluginIds.Contains(g.PluginId))
                        .Select(g => {
                            var achievements = _achievementHelper.GetAchievements(g.Id);
                            if (achievements == null || achievements.Count == 0)
                                return null;

                            // Deduplicate by achievement name — last entry wins.
                            // Achievement providers may return duplicates; matches diff sync behavior.
                            var dedupedByName = new Dictionary<string, GsApiClient.AchievementItemDto>();
                            foreach (var a in achievements) {
                                dedupedByName[a.Name ?? ""] = new GsApiClient.AchievementItemDto {
                                    name = a.Name,
                                    description = a.Description,
                                    date_unlocked = a.DateUnlocked,
                                    is_unlocked = a.IsUnlocked,
                                    rarity_percent = a.RarityPercent
                                };
                            }

                            return new GsApiClient.GameAchievementsDto {
                                playnite_id = g.Id.ToString(),
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                achievements = dedupedByName.Values.ToList()
                            };
                        })
                        .Where(x => x != null)
                        .ToList();
                });

                if (games.Count == 0) {
                    _logger.Info("No games with achievements found — setting empty baseline.");
                    GsSnapshotManager.UpdateAchievementsSnapshot(new Dictionary<string, GameAchievementSnapshot>());
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info($"Sending full achievements for {games.Count} games.");
                var response = await _apiClient.SyncAchievementsFull(new GsApiClient.AchievementsFullSyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    games = games
                });

                if (response == null) {
                    _logger.Error("Failed to queue full achievements sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Full achievements sync queued successfully.");

                    // Write achievement snapshot
                    var snapshotDict = games.ToDictionary(
                        g => g.playnite_id,
                        g => new GameAchievementSnapshot {
                            playnite_id = g.playnite_id,
                            achievements = g.achievements.Select(a => new AchievementSnapshot {
                                name = a.name,
                                is_unlocked = a.is_unlocked,
                                date_unlocked = a.date_unlocked?.ToString("o"),
                                rarity_percent = a.rarity_percent
                            }).ToList()
                        });
                    GsSnapshotManager.UpdateAchievementsSnapshot(snapshotDict);

                    // Store achievement hash for diff sync change detection
                    GsDataManager.Data.LastAchievementHash = ComputeAchievementHash(games);
                    GsDataManager.Save();

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from full achievements sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncAchievementsFullAsync");
                return SyncLibraryResult.Error;
            }
        }

        /// <summary>
        /// Computes achievement diff against snapshot and sends to v2/achievements/sync-diff.
        /// Falls back to full sync if the server requests it.
        /// </summary>
        public async Task<SyncLibraryResult> SyncAchievementsDiffAsync(
            IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            try {
                if (!GsDataManager.Data.SyncAchievements || !_achievementHelper.IsInstalled) {
                    _logger.Info("Achievement diff sync skipped: disabled or no achievement provider installed.");
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info("Starting diff achievements sync (v2)");
                var allGames = playniteDatabaseGames.ToList();
                var achievementSnapshot = GsSnapshotManager.GetAchievementsSnapshot();

                var (changed, clearedIds) = await Task.Run(() => {
                    var result = new List<GsApiClient.GameAchievementsDto>();
                    var currentGameIds = new HashSet<string>();

                    foreach (var g in allGames) {
                        if (g.PluginId == Guid.Empty || !AllowedPluginIds.Contains(g.PluginId))
                            continue;

                        var playniteId = g.Id.ToString();
                        var achievements = _achievementHelper.GetAchievements(g.Id);

                        // Game previously had achievements but now has none — send empty list to clear server-side
                        if ((achievements == null || achievements.Count == 0)
                            && achievementSnapshot.ContainsKey(playniteId)) {
                            currentGameIds.Add(playniteId);
                            result.Add(new GsApiClient.GameAchievementsDto {
                                playnite_id = playniteId,
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                achievements = new List<GsApiClient.AchievementItemDto>()
                            });
                            continue;
                        }

                        if (achievements == null || achievements.Count == 0)
                            continue;

                        currentGameIds.Add(playniteId);
                        bool hasChanged = false;

                        if (!achievementSnapshot.TryGetValue(playniteId, out var prevSnap)) {
                            hasChanged = true;
                        }
                        else {
                            // Compare: different count, new unlocks, rarity changes
                            if (prevSnap.achievements == null || achievements.Count != prevSnap.achievements.Count) {
                                hasChanged = true;
                            }
                            else {
                                // Use a loop instead of ToDictionary to handle duplicate achievement names gracefully.
                                // Last entry wins, matching the most recent snapshot state.
                                var prevByName = new Dictionary<string, AchievementSnapshot>();
                                foreach (var snap in prevSnap.achievements) {
                                    prevByName[snap.name ?? ""] = snap;
                                }
                                foreach (var a in achievements) {
                                    if (!prevByName.TryGetValue(a.Name ?? "", out var prev)) {
                                        hasChanged = true;
                                        break;
                                    }
                                    if (a.IsUnlocked != prev.is_unlocked
                                        || a.RarityPercent != prev.rarity_percent) {
                                        hasChanged = true;
                                        break;
                                    }
                                }
                            }
                        }

                        if (hasChanged) {
                            result.Add(new GsApiClient.GameAchievementsDto {
                                playnite_id = playniteId,
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                achievements = achievements.Select(a => new GsApiClient.AchievementItemDto {
                                    name = a.Name,
                                    description = a.Description,
                                    date_unlocked = a.DateUnlocked,
                                    is_unlocked = a.IsUnlocked,
                                    rarity_percent = a.RarityPercent
                                }).ToList()
                            });
                        }
                    }

                    // IDs in snapshot but not in current library (game uninstalled/removed)
                    var cleared = achievementSnapshot.Keys
                        .Where(id => !currentGameIds.Contains(id))
                        .ToList();

                    return (result, cleared);
                });

                if (changed.Count == 0 && clearedIds.Count == 0) {
                    _logger.Info("Achievement diff is empty — skipping.");
                    return SyncLibraryResult.Skipped;
                }

                // Include removed-library games as empty-achievement entries so the server deletes them
                foreach (var clearedId in clearedIds) {
                    changed.Add(new GsApiClient.GameAchievementsDto {
                        playnite_id = clearedId,
                        achievements = new List<GsApiClient.AchievementItemDto>()
                    });
                }

                _logger.Info($"Achievement diff: {changed.Count} games total ({clearedIds.Count} cleared).");
                var response = await _apiClient.SyncAchievementsDiff(new GsApiClient.AchievementsDiffSyncReq {
                    user_id = GsDataManager.Data.InstallID,
                    changed = changed,
                    base_snapshot_hash = GsDataManager.Data.LastAchievementHash ?? ""
                });

                if (response == null) {
                    _logger.Error("Failed to queue achievements diff sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "force-full-sync") {
                    _logger.Info($"Server requested full achievement sync (reason: {response.reason}). Falling back.");
                    GsSnapshotManager.ClearAchievementsSnapshot();
                    GsDataManager.Data.LastAchievementHash = null;
                    GsDataManager.Save();
                    return await SyncAchievementsFullAsync(playniteDatabaseGames, bypassCooldown: true);
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Achievement diff sync queued successfully.");

                    // Update snapshot: upsert changed games (with achievements), remove cleared ones
                    var changedSnapshots = changed
                        .Where(g => g.achievements != null && g.achievements.Count > 0)
                        .ToDictionary(
                            g => g.playnite_id,
                            g => new GameAchievementSnapshot {
                                playnite_id = g.playnite_id,
                                achievements = g.achievements.Select(a => new AchievementSnapshot {
                                    name = a.name,
                                    is_unlocked = a.is_unlocked,
                                    date_unlocked = a.date_unlocked?.ToString("o"),
                                    rarity_percent = a.rarity_percent
                                }).ToList()
                            });
                    // Combine games sent with empty achievements and games removed from library
                    var allCleared = changed
                        .Where(g => g.achievements == null || g.achievements.Count == 0)
                        .Select(g => g.playnite_id)
                        .Concat(clearedIds)
                        .ToList();
                    GsSnapshotManager.ApplyAchievementsDiff(changedSnapshots, allCleared);

                    // Recompute achievement hash from the full updated snapshot
                    var updatedSnapshot = GsSnapshotManager.GetAchievementsSnapshot();
                    var snapshotAsDtos = updatedSnapshot.Values.Select(snap => new GsApiClient.GameAchievementsDto {
                        playnite_id = snap.playnite_id,
                        achievements = snap.achievements?.Select(a => new GsApiClient.AchievementItemDto {
                            name = a.name,
                            is_unlocked = a.is_unlocked,
                            date_unlocked = a.date_unlocked != null && DateTime.TryParse(a.date_unlocked, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (DateTime?)null,
                            rarity_percent = a.rarity_percent
                        }).ToList() ?? new List<GsApiClient.AchievementItemDto>()
                    }).ToList();
                    GsDataManager.Data.LastAchievementHash = ComputeAchievementHash(snapshotAsDtos);
                    GsDataManager.Save();

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from achievements diff sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncAchievementsDiffAsync");
                return SyncLibraryResult.Error;
            }
        }

        /// <summary>
        /// Parses cooldown info from an AsyncQueuedResponse and persists it to the appropriate field.
        /// </summary>
        private static void HandleCooldownResponse(GsApiClient.AsyncQueuedResponse response, bool isDiffSync = false) {
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(response.cooldownExpiresAt)
                && DateTime.TryParse(response.cooldownExpiresAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) {
                expiresAt = parsed.ToUniversalTime();
            }
            _logger.Info($"Sync skipped by server cooldown. Expires: {expiresAt?.ToString("O") ?? "unknown"}");
            if (expiresAt.HasValue) {
                if (isDiffSync) {
                    GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt = expiresAt.Value;
                }
                else {
                    GsDataManager.Data.SyncCooldownExpiresAt = expiresAt.Value;
                }
                GsDataManager.Save();
            }
        }

        #endregion
    }
}
