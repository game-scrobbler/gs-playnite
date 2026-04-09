using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite;
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
        private readonly GsIntegrationAccountReader _integrationAccountReader;
        private readonly ILibraryApi _libraryApi;

        /// <summary>
        /// Initializes a new instance of the GsScrobblingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        /// <param name="integrationAccountReader">Reader for extracting integration account identities from library plugin configs.</param>
        /// <param name="libraryApi">Playnite library API for resolving game metadata IDs to names.</param>
        public GsScrobblingService(IGsApiClient apiClient, GsIntegrationAccountReader integrationAccountReader, ILibraryApi libraryApi) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _integrationAccountReader = integrationAccountReader;
            _libraryApi = libraryApi ?? throw new ArgumentNullException(nameof(libraryApi));
        }

        /// <summary>
        /// Manually clears the active session ID. Use with caution.
        /// </summary>
        private static void ClearActiveSession() {
            if (!string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                _logger.Info("Manually clearing active session ID");
                GsDataManager.MutateAndSave(d => d.ActiveSessionId = null);
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
            GsDataManager.MutateAndSave(d => d.ActiveSessionId = sessionId);
        }

        /// <summary>
        /// Sets the linked user information in the data manager.
        /// </summary>
        /// <param name="userId">The linked user ID, or null if not linked</param>
        private static void SetLinkedUser(string? userId = null) {
            bool oldLinked = GsDataManager.IsAccountLinked;
            var oldId = GsDataManager.Data.LinkedUserId;

            // Only set LinkedUserId if it's a valid ID (not the sentinel value)
            var newValue = (userId == GsData.NotLinkedValue || string.IsNullOrEmpty(userId))
                ? null : userId;
            GsDataManager.MutateAndSave(d => d.LinkedUserId = newValue);

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
        /// <param name="game">The game that started.</param>
        public async Task OnGameStartAsync(Game game) {
            try {
                if (GsDataManager.IsOptedOut) return;

                // Skip scrobbling if disabled
                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping game start tracking");
                    return;
                }

                if (game == null) {
                    _logger.Warn("OnGameStartAsync called with null game; skipping.");
                    return;
                }

                DateTime localDate = DateTime.Now;

                // Skip scrobbling for unsupported plugins
                if (string.IsNullOrEmpty(game.LibraryId) || !GsAllowedPlugins.AllowedPluginIds.Contains(game.LibraryId)) {
                    _logger.Info($"Skipping scrobble start for unsupported plugin: {game.LibraryId}");
                    return;
                }

                _logger.Info($"Starting scrobble session for game: {game.Name} (ID: {game.Id})");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return;

                var sessionData = await _apiClient.StartGameSession(new ScrobbleStartReq {
                    user_id = GsDataManager.InstallIdForBody,
                    game_name = game.Name,
                    game_id = game.Id.ToString(),
                    plugin_id = game.LibraryId,
                    external_game_id = game.LibraryGameId,
                    metadata = new { LibraryId = game.LibraryId },
                    started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                });
                if (sessionData != null && !string.IsNullOrEmpty(sessionData.session_id)) {
                    SetActiveSession(sessionData.session_id);
                    // Clear any stale pending-start marker so the stop handler uses the
                    // normal active-session path instead of the queued-pair branch.
                    if (GsDataManager.Data.PendingStartGameId != null) {
                        GsDataManager.MutateAndSave(d => d.PendingStartGameId = null);
                    }
                    _logger.Info($"Successfully started scrobble session with ID: {sessionData.session_id}");
                }
                else {
                    _logger.Error($"Failed to start scrobble session for game: {game.Name} (ID: {game.Id}). Queuing start for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "start",
                        StartData = new ScrobbleStartReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_name = game.Name,
                            game_id = game.Id.ToString(),
                            plugin_id = game.LibraryId,
                            external_game_id = game.LibraryGameId,
                            metadata = new { LibraryId = game.LibraryId },
                            started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    // Mark that this game has a queued start so OnGameStoppedAsync can pair it
                    // with a finish even though there is no ActiveSessionId.
                    GsDataManager.MutateAndSave(d => d.PendingStartGameId = game.Id.ToString());
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Error starting scrobble session for game: {game?.Name ?? "<null>"} (ID: {game?.Id.ToString() ?? "<null>"})");
            }
        }

        /// <summary>
        /// Handles the game stopped event and finishes the active scrobbling session.
        /// </summary>
        /// <param name="game">The game that stopped.</param>
        public async Task OnGameStoppedAsync(Game game) {
            try {
                if (GsDataManager.IsOptedOut) return;

                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping game stop tracking");
                    return;
                }
                if (game == null) {
                    _logger.Warn("OnGameStoppedAsync called with null game; skipping.");
                    return;
                }

                DateTime localDate = DateTime.Now;

                // If the start was queued (failed to send), queue a matching finish so the
                // replay produces a paired session. No API call is needed here.
                var pendingStartGameId = GsDataManager.Data.PendingStartGameId;
                if (!string.IsNullOrEmpty(pendingStartGameId) && pendingStartGameId == game.Id.ToString()) {
                    _logger.Info($"Queuing finish to pair with pending start for game: {game.Name} (ID: {game.Id})");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_name = game.Name,
                            game_id = game.Id.ToString(),
                            plugin_id = game.LibraryId,
                            external_game_id = game.LibraryGameId,
                            session_id = null,
                            metadata = new { LibraryId = game.LibraryId },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    GsDataManager.MutateAndSave(d => d.PendingStartGameId = null);
                    return;
                }

                if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                    _logger.Warn("No active session ID found when stopping game");
                    return;
                }

                // Skip scrobbling for unsupported plugins
                if (string.IsNullOrEmpty(game.LibraryId) || !GsAllowedPlugins.AllowedPluginIds.Contains(game.LibraryId)) {
                    _logger.Info($"Skipping scrobble finish for unsupported plugin: {game.LibraryId}");
                    // Still clear the active session since we may have tracked start before this filter existed
                    ClearActiveSession();
                    return;
                }

                _logger.Info($"Stopping scrobble session for game: {game.Name} (ID: {game.Id})");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return;

                var finishResponse = await _apiClient.FinishGameSession(new ScrobbleFinishReq {
                    user_id = GsDataManager.InstallIdForBody,
                    game_name = game.Name,
                    game_id = game.Id.ToString(),
                    plugin_id = game.LibraryId,
                    external_game_id = game.LibraryGameId,
                    session_id = GsDataManager.Data.ActiveSessionId,
                    metadata = new { LibraryId = game.LibraryId },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                });
                if (finishResponse != null) {
                    // Only clear the session ID if the request was successful
                    ClearActiveSession();
                    _logger.Info($"Successfully finished scrobble session for game: {game.Name} (ID: {game.Id})");
                }
                else {
                    _logger.Error($"Failed to finish game session for {game.Name} (ID: {game.Id}). Queuing for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_name = game.Name,
                            game_id = game.Id.ToString(),
                            plugin_id = game.LibraryId,
                            external_game_id = game.LibraryGameId,
                            session_id = GsDataManager.Data.ActiveSessionId,
                            metadata = new { LibraryId = game.LibraryId },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    // Leave ActiveSessionId in place so a manual retry still has the session ID
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Error stopping scrobble session for game: {game?.Name ?? "<null>"} (ID: {game?.Id.ToString() ?? "<null>"})");
            }
        }

        /// <summary>
        /// Handles the application stopped event and cleans up any active scrobbling session.
        /// This ensures that if Playnite is closed while a game is running, the session is properly finished.
        /// </summary>
        public async Task OnApplicationStoppedAsync() {
            try {
                if (GsDataManager.IsOptedOut) return;

                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping application stop cleanup");
                    return;
                }
                if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                    _logger.Debug("No active session to clean up on application stop");
                    return;
                }

                _logger.Info("Application stopping with active session, finishing scrobble session");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return;

                DateTime localDate = DateTime.Now;
                var finishResponse = await _apiClient.FinishGameSession(new ScrobbleFinishReq {
                    user_id = GsDataManager.InstallIdForBody,
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
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
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
        /// Builds an ISO 8601 date string from a nullable PartialDate (Playnite 11 Game.ReleaseDate).
        /// Returns "YYYY-MM-DD" or null.
        /// </summary>
        private static string? BuildReleaseDateString(PartialDate? rd) {
            if (rd == null) return null;
            // PartialDate.Year is int (not int?); Month/Day are int?
            return $"{rd.Year:D4}-{(rd.Month ?? 1):D2}-{(rd.Day ?? 1):D2}";
        }


        #region v2 Sync Methods

        /// <summary>
        /// Maps a Playnite Game to the API DTO. Shared by all sync paths.
        /// </summary>
        private GameSyncDto MapGameToDto(Game g) {
            return new GameSyncDto {
                game_id = g.LibraryGameId,
                plugin_id = g.LibraryId,
                game_name = g.Name,
                playnite_id = g.Id.ToString(),
                playtime_seconds = (long)g.PlayTime,
                play_count = (int)(g.SessionIds?.Count ?? 0),
                last_activity = g.LastPlayedDate?.UtcDateTime,
                is_installed = g.InstallState == InstallState.Installed,
                completion_status_id = g.CompletionStatusId,
                completion_status_name = g.CompletionStatusId != null
                    ? _libraryApi.CompletionStatuses.Get(g.CompletionStatusId)?.Name
                    : null,
                genres = ResolveNames(g.GenreIds, _libraryApi.Genres),
                platforms = ResolveNames(g.PlatformIds, _libraryApi.Platforms),
                developers = ResolveNames(g.DeveloperIds, _libraryApi.Companies),
                publishers = ResolveNames(g.PublisherIds, _libraryApi.Companies),
                tags = ResolveNames(g.TagIds, _libraryApi.Tags),
                features = ResolveNames(g.FeatureIds, _libraryApi.Features),
                categories = ResolveNames(g.CategoryIds, _libraryApi.Categories),
                series = ResolveNames(g.SeriesIds, _libraryApi.Series),
                user_score = g.UserScore,
                critic_score = g.CriticScore,
                community_score = g.CommunityScore,
                release_year = g.ReleaseDate?.Year,
                date_added = g.AddedDate?.UtcDateTime,
                is_favorite = g.Favorite,
                is_hidden = g.Hidden,
                source_name = g.SourceId != null
                    ? _libraryApi.Sources.Get(g.SourceId)?.Name
                    : null,
                release_date = BuildReleaseDateString(g.ReleaseDate),
                modified = g.ModifiedDate?.UtcDateTime,
                age_ratings = ResolveNames(g.AgeRatingIds, _libraryApi.AgeRatings),
                regions = ResolveNames(g.RegionIds, _libraryApi.Regions)
            };
        }

        private static List<string>? ResolveNames<T>(HashSet<string>? ids, ILibraryCollection<T> collection)
            where T : LibraryObject {
            if (ids == null || ids.Count == 0) return null;
            var names = new List<string>(ids.Count);
            foreach (var id in ids) {
                var name = collection.Get(id)?.Name;
                if (name != null) names.Add(name);
            }
            return names.Count > 0 ? names : null;
        }


        /// <summary>
        /// Creates a GameSnapshot from a DTO for the local snapshot store.
        /// </summary>
        private static GameSnapshot BuildGameSnapshot(GameSyncDto g) {
            return new GameSnapshot {
                playnite_id = g.playnite_id,
                game_id = g.game_id,
                plugin_id = g.plugin_id,
                playtime_seconds = g.playtime_seconds,
                play_count = g.play_count,
                last_activity = g.last_activity?.ToString("o"),
                metadata_hash = GsHashUtils.ComputeGameMetadataHash(g),
                achievement_count_unlocked = g.achievement_count_unlocked,
                achievement_count_total = g.achievement_count_total
            };
        }

        /// <summary>
        /// Computes the diff between the current library DTOs and the stored snapshot.
        /// </summary>
        private static (List<GameSyncDto> added, List<GameSyncDto> updated, List<string> removed)
            ComputeLibraryDiff(List<GameSyncDto> current, Dictionary<string, GameSnapshot> snapshot) {
            var added = new List<GameSyncDto>();
            var updated = new List<GameSyncDto>();

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
                var currentMetaHash = GsHashUtils.ComputeGameMetadataHash(g);
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
        private async Task<(List<GameSyncDto> library, string libraryHash, int totalCount, int filteredCount)>
            BuildLibraryDtosAsync(IEnumerable<Game> playniteDatabaseGames) {
            // Snapshot the live Playnite collection to avoid "Collection was modified" if Playnite
            // updates its database concurrently (e.g. metadata download or library import).
            List<Game> allGames;
            try {
                allGames = playniteDatabaseGames.ToList();
            }
            catch (InvalidOperationException ex) {
                _logger.Warn(ex, "Database collection modified during snapshot — retrying once");
                allGames = playniteDatabaseGames.ToList();
            }
            var (library, libraryHash, filteredCount) = await Task.Run(() => {
                var filtered = allGames
                    .Where(g => !string.IsNullOrEmpty(g.LibraryId) && GsAllowedPlugins.AllowedPluginIds.Contains(g.LibraryId))
                    .ToList();

                var dtos = filtered.Select(g => MapGameToDto(g)).ToList();

                return (dtos, GsHashUtils.ComputeLibraryHash(dtos), allGames.Count - filtered.Count);
            });

            if (filteredCount > 0) {
                _logger.Info($"Filtered {filteredCount} games from unsupported plugins (sending {library.Count}/{allGames.Count})");
            }

            return (library, libraryHash, allGames.Count, filteredCount);
        }

        /// <summary>
        /// Reads integration account identities from library plugin configs.
        /// Returns an empty list on failure — never blocks sync.
        /// </summary>
        private List<IntegrationAccountDto> ReadIntegrationAccountsSafe() {
            if (_integrationAccountReader == null) {
                return new List<IntegrationAccountDto>();
            }
            try {
                var accounts = _integrationAccountReader.ReadAll();
                if (accounts.Count > 0) {
                    _logger.Info($"Discovered {accounts.Count} integration account(s): {string.Join(", ", accounts.Select(a => a.provider_id))}");
                }
                return accounts;
            }
            catch (Exception ex) {
                _logger.Warn($"Failed to read integration accounts: {ex.Message}");
                return new List<IntegrationAccountDto>();
            }
        }


        /// <summary>
        /// Sends the full library to the v2/library/sync-full endpoint and writes the snapshot.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        /// <param name="bypassCooldown">When true, skip the client-side cooldown check (used when server requests force-full-sync)</param>
        public async Task<SyncLibraryResult> SyncLibraryFullAsync(
            IEnumerable<Game> playniteDatabaseGames, bool bypassCooldown = false) {
            try {
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                if (!bypassCooldown) {
                    var cooldownExpiry = GsDataManager.Data.SyncCooldownExpiresAt;
                    if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                        _logger.Info($"Library full sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                        return SyncLibraryResult.Cooldown;
                    }
                }

                _logger.Info("Starting full library sync (v2)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                var integrationAccounts = ReadIntegrationAccountsSafe();
                var accountsHash = GsHashUtils.ComputeIntegrationAccountsHash(integrationAccounts);
                var accountsChanged = accountsHash != (GsDataManager.Data.LastIntegrationAccountsHash ?? "");

                // Only skip on hash match if a snapshot baseline exists.
                // Without a baseline, we must proceed to write the snapshot even if the hash matches.
                // Also force a sync when integration accounts have changed (e.g. user linked a new Steam account).
                if (libraryHash == GsDataManager.Data.LastLibraryHash && GsSnapshotManager.HasLibraryBaseline && !accountsChanged) {
                    _logger.Info("Library hash unchanged since last sync — skipping full sync.");
                    return SyncLibraryResult.Skipped;
                }

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await _apiClient.SyncLibraryFull(new LibraryFullSyncReq {
                    user_id = GsDataManager.InstallIdForBody,
                    library = library,
                    flags = GsDataManager.Data.Flags.ToArray(),
                    integration_accounts = integrationAccounts.Count > 0 ? integrationAccounts : null
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
                    var libCount = library.Count;
                    GsDataManager.MutateAndSave(d => {
                        d.LastSyncAt = DateTime.UtcNow;
                        d.LastSyncGameCount = libCount;
                        d.LastLibraryHash = libraryHash;
                        d.LastIntegrationAccountsHash = accountsHash;
                        d.SyncCooldownExpiresAt = null;
                    });

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
            IEnumerable<Game> playniteDatabaseGames) {
            try {
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var cooldownExpiry = GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                    _logger.Info($"Library diff sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                    return SyncLibraryResult.Cooldown;
                }

                _logger.Info("Starting diff library sync (v2)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                var integrationAccounts = ReadIntegrationAccountsSafe();
                var accountsHash = GsHashUtils.ComputeIntegrationAccountsHash(integrationAccounts);
                var accountsChanged = accountsHash != (GsDataManager.Data.LastIntegrationAccountsHash ?? "");

                if (libraryHash == GsDataManager.Data.LastLibraryHash && !accountsChanged) {
                    _logger.Info("Library hash unchanged since last sync — skipping diff sync.");
                    return SyncLibraryResult.Skipped;
                }

                var snapshot = GsSnapshotManager.GetLibrarySnapshot();
                var (added, updated, removed) = await Task.Run(() =>
                    ComputeLibraryDiff(library, snapshot));

                // If only integration accounts changed (no library diff), still send the request
                // with empty diff so the backend can process the new accounts.
                if (added.Count == 0 && updated.Count == 0 && removed.Count == 0 && !accountsChanged) {
                    _logger.Info("Library diff is empty — skipping.");
                    GsDataManager.MutateAndSave(d => d.LastLibraryHash = libraryHash);
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info($"Library diff: {added.Count} added, {updated.Count} updated, {removed.Count} removed" +
                    (accountsChanged ? " (integration accounts also changed)" : ""));

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await _apiClient.SyncLibraryDiff(new LibraryDiffSyncReq {
                    user_id = GsDataManager.InstallIdForBody,
                    added = added,
                    updated = updated,
                    removed = removed.ToList(),
                    base_snapshot_hash = GsDataManager.Data.LastLibraryHash ?? "",
                    flags = GsDataManager.Data.Flags.ToArray(),
                    integration_accounts = integrationAccounts.Count > 0 ? integrationAccounts : null
                });

                if (response == null) {
                    _logger.Error("Failed to queue library diff sync.");
                    return SyncLibraryResult.Error;
                }

                // Server requests a full sync instead
                if (response.status == "force-full-sync") {
                    _logger.Info($"Server requested full sync (reason: {response.reason}). Falling back.");
                    GsSnapshotManager.ClearLibrarySnapshot();
                    GsDataManager.MutateAndSave(d => {
                        d.LastLibraryHash = null;
                        d.SyncCooldownExpiresAt = null;
                    });
                    return await SyncLibraryFullAsync(playniteDatabaseGames, bypassCooldown: true);
                }

                if (response.status == "skipped" && response.reason != null && response.reason.StartsWith("cooldown_")) {
                    HandleCooldownResponse(response, isDiffSync: true);
                    return SyncLibraryResult.Cooldown;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Library diff sync queued successfully.");
                    var libCount = library.Count;
                    GsDataManager.MutateAndSave(d => {
                        d.LastSyncAt = DateTime.UtcNow;
                        d.LastSyncGameCount = libCount;
                        d.LastLibraryHash = libraryHash;
                        d.LastIntegrationAccountsHash = accountsHash;
                        d.LibraryDiffSyncCooldownExpiresAt = null;
                    });

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

        // SyncAchievementsFullAsync removed — will be re-added after SuccessStory/PlayniteAchievements v11 releases.

        // SyncAchievementsDiffAsync removed — will be re-added after SuccessStory/PlayniteAchievements v11 releases.

        /// <summary>
        /// Parses cooldown info from an AsyncQueuedResponse and persists it to the appropriate field.
        /// </summary>
        private static void HandleCooldownResponse(AsyncQueuedResponse response, bool isDiffSync = false) {
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(response.cooldownExpiresAt)
                && DateTime.TryParse(response.cooldownExpiresAt, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) {
                expiresAt = parsed.ToUniversalTime();
            }
            _logger.Info($"Sync skipped by server cooldown. Expires: {expiresAt?.ToString("O") ?? "unknown"}");
            if (expiresAt.HasValue) {
                GsDataManager.MutateAndSave(d => {
                    if (isDiffSync)
                        d.LibraryDiffSyncCooldownExpiresAt = expiresAt.Value;
                    else
                        d.SyncCooldownExpiresAt = expiresAt.Value;
                });
            }
        }

        #endregion
    }
}
