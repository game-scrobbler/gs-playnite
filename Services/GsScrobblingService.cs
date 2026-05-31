using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Events;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
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
        private readonly GsIntegrationAccountReader _integrationAccountReader;

        /// <summary>
        /// Initializes a new instance of the GsScrobblingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        /// <param name="achievementHelper">Helper for reading achievement data from the SuccessStory plugin.</param>
        /// <param name="integrationAccountReader">Reader for extracting integration account identities from library plugin configs.</param>
        public GsScrobblingService(IGsApiClient apiClient, IAchievementProvider achievementHelper, GsIntegrationAccountReader integrationAccountReader) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _achievementHelper = achievementHelper ?? throw new ArgumentNullException(nameof(achievementHelper));
            _integrationAccountReader = integrationAccountReader;
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
        private static void SetLinkedUser(string userId = null) {
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
        /// <param name="args">Event arguments containing game information.</param>
        public async Task OnGameStartAsync(OnGameStartingEventArgs args) {
            try {
                if (GsDataManager.IsOptedOut) return;

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
                if (startedGame.PluginId == Guid.Empty || !GsAllowedPlugins.AllowedPluginIds.Contains(startedGame.PluginId)) {
                    _logger.Info($"Skipping scrobble start for unsupported plugin: {startedGame.PluginId}");
                    return;
                }

                _logger.Info($"Starting scrobble session for game: {startedGame.Name} (ID: {startedGame.Id})");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return;

                var sessionData = await _apiClient.StartGameSession(new ScrobbleStartReq {
                    user_id = GsDataManager.InstallIdForBody,
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
                        GsDataManager.MutateAndSave(d => d.PendingStartGameId = null);
                    }
                    _logger.Info($"Successfully started scrobble session with ID: {sessionData.session_id}");
                }
                else {
                    _logger.Error($"Failed to start scrobble session for game: {startedGame.Name} (ID: {startedGame.Id}). Queuing start for retry.");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "start",
                        StartData = new ScrobbleStartReq {
                            user_id = GsDataManager.InstallIdForBody,
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
                    GsDataManager.MutateAndSave(d => d.PendingStartGameId = startedGame.Id.ToString());
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
                if (GsDataManager.IsOptedOut) return;

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
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_name = stoppedGame.Name,
                            game_id = stoppedGame.Id.ToString(),
                            plugin_id = stoppedGame.PluginId.ToString(),
                            external_game_id = stoppedGame.GameId,
                            session_id = null,
                            metadata = new { PluginId = stoppedGame.PluginId.ToString() },
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
                if (stoppedGame.PluginId == Guid.Empty || !GsAllowedPlugins.AllowedPluginIds.Contains(stoppedGame.PluginId)) {
                    _logger.Info($"Skipping scrobble finish for unsupported plugin: {stoppedGame.PluginId}");
                    // Still clear the active session since we may have tracked start before this filter existed
                    ClearActiveSession();
                    return;
                }

                _logger.Info($"Stopping scrobble session for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return;

                var finishResponse = await _apiClient.FinishGameSession(new ScrobbleFinishReq {
                    user_id = GsDataManager.InstallIdForBody,
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
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
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



        #region v3 Sync Methods

        /// <summary>
        /// Maps a Playnite Game to the slim v3 sync DTO.
        /// Genre/theme/company/score/release-date metadata is intentionally
        /// not mapped — see ADR-011 in gs-mono.
        /// </summary>
        private GameSyncDto MapGameToDto(Playnite.SDK.Models.Game g, bool syncAchievements) {
            var achievementCounts = syncAchievements
                ? _achievementHelper.GetCounts(g.Id)
                : null;

            // v3 slim DTO — IGDB owns genre/theme/company/score/release-date
            // metadata server-side, so we only ship identity, activity, and
            // per-user state fields. See ADR-011 in gs-mono.
            return new GameSyncDto {
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
                user_score = g.UserScore,
                date_added = g.Added,
                is_favorite = g.Favorite,
                is_hidden = g.Hidden,
                source_name = g.Source?.Name,
                modified = g.Modified
            };
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
            BuildLibraryDtosAsync(IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames) {
            // Snapshot the live Playnite collection to avoid "Collection was modified" if Playnite
            // updates its database concurrently (e.g. metadata download or library import).
            List<Playnite.SDK.Models.Game> allGames;
            try {
                allGames = playniteDatabaseGames.ToList();
            }
            catch (InvalidOperationException ex) {
                _logger.Warn(ex, "Database collection modified during snapshot — retrying once");
                allGames = playniteDatabaseGames.ToList();
            }
            var syncAchievements = GsDataManager.Data.SyncAchievements;

            var (library, libraryHash, filteredCount) = await Task.Run(() => {
                var filtered = allGames
                    .Where(g => g.PluginId != Guid.Empty && GsAllowedPlugins.AllowedPluginIds.Contains(g.PluginId))
                    .ToList();

                var dtos = filtered.Select(g => MapGameToDto(g, syncAchievements)).ToList();

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
            IEnumerable<Playnite.SDK.Models.Game> playniteDatabaseGames, bool bypassCooldown = false) {
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
                GsSentry.CaptureException(ex, "SyncLibraryFullAsync: unexpected exception");
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
                    // libraryHash is computed over the current (post-diff) library, so it is
                    // the exact baseline for the server to store — no DB reconstruction needed.
                    result_snapshot_hash = libraryHash,
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
                GsSentry.CaptureException(ex, "SyncLibraryDiffAsync: unexpected exception");
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
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                if (!GsDataManager.Data.SyncAchievements || !_achievementHelper.IsInstalled) {
                    _logger.Info("Achievement sync skipped: disabled or no achievement provider installed.");
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info("Starting full achievements sync (v2)");
                List<Playnite.SDK.Models.Game> allGames;
                try {
                    allGames = playniteDatabaseGames.ToList();
                }
                catch (InvalidOperationException) {
                    allGames = playniteDatabaseGames.ToList();
                }

                var games = await Task.Run(() => {
                    return allGames
                        .Where(g => g.PluginId != Guid.Empty && GsAllowedPlugins.AllowedPluginIds.Contains(g.PluginId))
                        .Select(g => {
                            var achievements = _achievementHelper.GetAchievements(g.Id);
                            if (achievements == null || achievements.Count == 0)
                                return null;

                            // Deduplicate by achievement name — last entry wins.
                            // Achievement providers may return duplicates; matches diff sync behavior.
                            var dedupedByName = new Dictionary<string, AchievementItemDto>();
                            foreach (var a in achievements) {
                                dedupedByName[a.Name ?? ""] = new AchievementItemDto {
                                    name = a.Name,
                                    description = a.Description,
                                    date_unlocked = a.DateUnlocked,
                                    is_unlocked = a.IsUnlocked,
                                    rarity_percent = a.RarityPercent
                                };
                            }

                            return new GameAchievementsDto {
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

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await _apiClient.SyncAchievementsFull(new AchievementsFullSyncReq {
                    user_id = GsDataManager.InstallIdForBody,
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
                    var achHash = GsHashUtils.ComputeAchievementHash(games);
                    GsDataManager.MutateAndSave(d => d.LastAchievementHash = achHash);

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from full achievements sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncAchievementsFullAsync");
                GsSentry.CaptureException(ex, "SyncAchievementsFullAsync: unexpected exception");
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
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                if (!GsDataManager.Data.SyncAchievements || !_achievementHelper.IsInstalled) {
                    _logger.Info("Achievement diff sync skipped: disabled or no achievement provider installed.");
                    return SyncLibraryResult.Skipped;
                }

                _logger.Info("Starting diff achievements sync (v2)");
                List<Playnite.SDK.Models.Game> allGames;
                try {
                    allGames = playniteDatabaseGames.ToList();
                }
                catch (InvalidOperationException) {
                    allGames = playniteDatabaseGames.ToList();
                }
                var achievementSnapshot = GsSnapshotManager.GetAchievementsSnapshot();

                // Diagnostic: log provider status and game counts
                if (_achievementHelper is GsAchievementAggregator agg) {
                    var installed = agg.GetInstalledProviders();
                    _logger.Info($"Achievement providers installed: {installed.Count} — " +
                        string.Join(", ", installed.Select(p => $"{p.ProviderName} (v{p.GetVersion() ?? "?"})")));
                }
                _logger.Info($"Achievement diff: {allGames.Count} total games, " +
                    $"snapshot has {achievementSnapshot.Count} entries");

                var (changed, clearedIds) = await Task.Run(() => {
                    var result = new List<GameAchievementsDto>();
                    var currentGameIds = new HashSet<string>();
                    int filteredCount = 0;
                    int nullCount = 0;
                    int withDataCount = 0;

                    foreach (var g in allGames) {
                        if (g.PluginId == Guid.Empty || !GsAllowedPlugins.AllowedPluginIds.Contains(g.PluginId))
                            continue;

                        filteredCount++;
                        var playniteId = g.Id.ToString();
                        List<AchievementItem> achievements;
                        string sourceProvider = null;

                        // Use source-aware lookup when available for diagnostics
                        if (_achievementHelper is GsAchievementAggregator diagAgg) {
                            var (achs, src) = diagAgg.GetAchievementsWithSource(g.Id);
                            achievements = achs;
                            sourceProvider = src;
                        } else {
                            achievements = _achievementHelper.GetAchievements(g.Id);
                        }

                        if (achievements == null || achievements.Count == 0) {
                            nullCount++;
                            // Log first 3 games with no data for diagnosis
                            if (nullCount <= 3) {
                                _logger.Debug($"Achievement diag: game '{g.Name}' (plugin={g.PluginId}) returned no achievements");
                            }
                        } else {
                            withDataCount++;
                            // Log first game with data to confirm which provider works
                            if (withDataCount == 1) {
                                _logger.Info($"Achievement diag: first hit from '{sourceProvider ?? "unknown"}' — " +
                                    $"game '{g.Name}' has {achievements.Count} achievements");
                            }
                        }

                        // Game previously had achievements but now has none — send empty list to clear server-side
                        if ((achievements == null || achievements.Count == 0)
                            && achievementSnapshot.ContainsKey(playniteId)) {
                            currentGameIds.Add(playniteId);
                            result.Add(new GameAchievementsDto {
                                playnite_id = playniteId,
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                achievements = new List<AchievementItemDto>()
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
                            // Deduplicate by achievement name — last entry wins.
                            // Matches full sync deduplication to avoid data inconsistency.
                            var dedupedByName = new Dictionary<string, AchievementItemDto>();
                            foreach (var a in achievements) {
                                dedupedByName[a.Name ?? ""] = new AchievementItemDto {
                                    name = a.Name,
                                    description = a.Description,
                                    date_unlocked = a.DateUnlocked,
                                    is_unlocked = a.IsUnlocked,
                                    rarity_percent = a.RarityPercent
                                };
                            }

                            result.Add(new GameAchievementsDto {
                                playnite_id = playniteId,
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                achievements = dedupedByName.Values.ToList()
                            });
                        }
                    }

                    _logger.Info($"Achievement diff scan: {filteredCount} eligible games, " +
                        $"{withDataCount} with data, {nullCount} with no data, " +
                        $"{result.Count} changed");

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
                    changed.Add(new GameAchievementsDto {
                        playnite_id = clearedId,
                        achievements = new List<AchievementItemDto>()
                    });
                }

                _logger.Info($"Achievement diff: {changed.Count} games total ({clearedIds.Count} cleared).");

                // Re-check opt-out before sending data (user may have opted out mid-flight)
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

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
                var allCleared = changed
                    .Where(g => g.achievements == null || g.achievements.Count == 0)
                    .Select(g => g.playnite_id)
                    .Concat(clearedIds)
                    .ToList();

                // Preview post-diff hash before HTTP so the server can store the same
                // baseline the plugin will adopt on `status: queued`.
                var previewSnapshot = new Dictionary<string, GameAchievementSnapshot>(
                    GsSnapshotManager.GetAchievementsSnapshot());
                foreach (var kvp in changedSnapshots) {
                    previewSnapshot[kvp.Key] = kvp.Value;
                }
                foreach (var id in allCleared) {
                    previewSnapshot.Remove(id);
                }
                var resultAchievementHash = GsHashUtils.ComputeAchievementHash(
                    previewSnapshot.Values.Select(snap => new GameAchievementsDto {
                        playnite_id = snap.playnite_id,
                        achievements = snap.achievements?.Select(a => new AchievementItemDto {
                            name = a.name,
                            is_unlocked = a.is_unlocked,
                            date_unlocked = a.date_unlocked != null && DateTime.TryParse(a.date_unlocked, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : (DateTime?)null,
                            rarity_percent = a.rarity_percent
                        }).ToList() ?? new List<AchievementItemDto>()
                    }).ToList());

                var response = await _apiClient.SyncAchievementsDiff(new AchievementsDiffSyncReq {
                    user_id = GsDataManager.InstallIdForBody,
                    changed = changed,
                    base_snapshot_hash = GsDataManager.Data.LastAchievementHash ?? "",
                    result_snapshot_hash = resultAchievementHash
                });

                if (response == null) {
                    _logger.Error("Failed to queue achievements diff sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "force-full-sync") {
                    _logger.Info($"Server requested full achievement sync (reason: {response.reason}). Falling back.");
                    GsSnapshotManager.ClearAchievementsSnapshot();
                    GsDataManager.MutateAndSave(d => d.LastAchievementHash = null);
                    return await SyncAchievementsFullAsync(playniteDatabaseGames, bypassCooldown: true);
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Achievement diff sync queued successfully.");

                    GsSnapshotManager.ApplyAchievementsDiff(changedSnapshots, allCleared);
                    GsDataManager.MutateAndSave(d => d.LastAchievementHash = resultAchievementHash);

                    return SyncLibraryResult.Success;
                }

                _logger.Error($"Unexpected response from achievements diff sync: status={response.status}");
                return SyncLibraryResult.Error;
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error in SyncAchievementsDiffAsync");
                GsSentry.CaptureException(ex, "SyncAchievementsDiffAsync: unexpected exception");
                return SyncLibraryResult.Error;
            }
        }

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
