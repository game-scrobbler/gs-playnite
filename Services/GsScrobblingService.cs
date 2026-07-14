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
        /// Clears the active session ID for a specific game. Playnite allows multiple
        /// games to run simultaneously, so sessions are tracked per game ID.
        /// </summary>
        private static void ClearActiveSession(string gameId) {
            if (string.IsNullOrEmpty(gameId)) return;
            if (GsDataManager.HasActiveSession(gameId)) {
                _logger.Info($"Clearing active session ID for game ID: {gameId}");
                GsDataManager.MutateAndSave(d => d.ActiveSessionsByGameId.Remove(gameId));
            }
        }

        /// <summary>
        /// Sets the active session ID for a specific game and persists it to storage.
        /// </summary>
        /// <param name="gameId">The Playnite game ID the session belongs to.</param>
        /// <param name="sessionId">The session ID to set as active.</param>
        private static void SetActiveSession(string gameId, string sessionId) {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(sessionId)) {
                _logger.Warn("Attempted to set active session with empty or null game ID or session ID");
                return;
            }

            _logger.Info($"Setting active session ID for game ID {gameId}: {sessionId}");
            GsDataManager.MutateAndSave(d => d.ActiveSessionsByGameId[gameId] = sessionId);
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
                if (!GsAllowedPlugins.IsAllowed(startedGame)) {
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
                    source_name = startedGame.Source?.Name,
                    metadata = new { PluginId = startedGame.PluginId.ToString(), SourceName = startedGame.Source?.Name },
                    started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                });
                var startedGameId = startedGame.Id.ToString();

                if (sessionData != null && !string.IsNullOrEmpty(sessionData.session_id)) {
                    SetActiveSession(startedGameId, sessionData.session_id);
                    // Clear any stale pending-start marker for this game so the stop handler
                    // uses the normal active-session path instead of the queued-pair branch.
                    if (GsDataManager.HasPendingStart(startedGameId)) {
                        GsDataManager.MutateAndSave(d => d.PendingStartGameIds.Remove(startedGameId));
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
                            source_name = startedGame.Source?.Name,
                            metadata = new { PluginId = startedGame.PluginId.ToString(), SourceName = startedGame.Source?.Name },
                            started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    // Mark that this game has a queued start so OnGameStoppedAsync can pair it
                    // with a finish even though there is no active session for it.
                    if (!GsDataManager.HasPendingStart(startedGameId)) {
                        GsDataManager.MutateAndSave(d => d.PendingStartGameIds.Add(startedGameId));
                    }
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
                var stoppedGameId = stoppedGame.Id.ToString();

                // If the start was queued (failed to send), queue a matching finish so the
                // replay produces a paired session. No API call is needed here.
                if (GsDataManager.HasPendingStart(stoppedGameId)) {
                    _logger.Info($"Queuing finish to pair with pending start for game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                    GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                        Type = "finish",
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_name = stoppedGame.Name,
                            game_id = stoppedGame.Id.ToString(),
                            plugin_id = stoppedGame.PluginId.ToString(),
                            external_game_id = stoppedGame.GameId,
                            source_name = stoppedGame.Source?.Name,
                            session_id = null,
                            metadata = new { PluginId = stoppedGame.PluginId.ToString(), SourceName = stoppedGame.Source?.Name },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    GsDataManager.MutateAndSave(d => d.PendingStartGameIds.Remove(stoppedGameId));
                    return;
                }

                // Capture the session ID for this game once, up front — re-reading shared
                // state after the awaited network call below could observe a different
                // game's session if OnGameStartAsync ran concurrently in the interim.
                if (!GsDataManager.TryGetActiveSession(stoppedGameId, out var activeSessionId) || string.IsNullOrEmpty(activeSessionId)) {
                    _logger.Warn($"No active session ID found when stopping game: {stoppedGame.Name} (ID: {stoppedGame.Id})");
                    return;
                }

                // Skip scrobbling for unsupported plugins
                if (!GsAllowedPlugins.IsAllowed(stoppedGame)) {
                    _logger.Info($"Skipping scrobble finish for unsupported plugin: {stoppedGame.PluginId}");
                    // Still clear the active session since we may have tracked start before this filter existed
                    ClearActiveSession(stoppedGameId);
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
                    source_name = stoppedGame.Source?.Name,
                    session_id = activeSessionId,
                    metadata = new { PluginId = stoppedGame.PluginId.ToString(), SourceName = stoppedGame.Source?.Name },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                });
                if (finishResponse != null) {
                    // Only clear the session ID if the request was successful
                    ClearActiveSession(stoppedGameId);
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
                            source_name = stoppedGame.Source?.Name,
                            session_id = activeSessionId,
                            metadata = new { PluginId = stoppedGame.PluginId.ToString(), SourceName = stoppedGame.Source?.Name },
                            finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                        },
                        QueuedAt = localDate
                    });
                    // Leave this game's active session entry in place so a manual retry still has the session ID
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Error stopping scrobble session for game: {args?.Game?.Name ?? "<null>"} (ID: {(args?.Game != null ? args.Game.Id.ToString() : "<null>")})");
            }
        }

        /// <summary>
        /// Handles the application stopped event and cleans up any active scrobbling session(s).
        /// This ensures that if Playnite is closed while one or more games are running, each
        /// session is properly finished.
        /// </summary>
        /// <remarks>
        /// Playnite invokes this from an async-void event handler and does not wait for it to
        /// complete before tearing down the process, so any <c>await</c> here might never resume.
        /// To survive that, the finish for each session is durably queued to
        /// <see cref="PendingScrobble"/> storage (a synchronous disk write) BEFORE attempting the
        /// live network call — only removing the queued copy if the send actually completes. If
        /// the process exits mid-call, the queued finish survives and is sent on next launch.
        /// </remarks>
        public async Task OnApplicationStoppedAsync() {
            try {
                if (GsDataManager.IsOptedOut) return;

                if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                    _logger.Info("Scrobbling disabled, skipping application stop cleanup");
                    return;
                }

                // Snapshot so we don't enumerate a collection that OnGameStoppedAsync could be
                // concurrently mutating, and so each session's cleanup is independent below.
                var activeSessions = GsDataManager.SnapshotActiveSessions();
                if (activeSessions.Count == 0) {
                    _logger.Debug("No active session to clean up on application stop");
                    return;
                }

                _logger.Info($"Application stopping with {activeSessions.Count} active session(s), finishing scrobble session(s)");

                DateTime localDate = DateTime.Now;

                foreach (var kvp in activeSessions) {
                    var gameId = kvp.Key;
                    var sessionId = kvp.Value;

                    var finishData = new ScrobbleFinishReq {
                        user_id = GsDataManager.InstallIdForBody,
                        session_id = sessionId,
                        metadata = new { reason = "application_stopped" },
                        finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                    };
                    var pendingFinish = new PendingScrobble {
                        Type = "finish",
                        FinishData = finishData,
                        QueuedAt = localDate
                    };
                    // Durable write happens before any await — see remarks above. Once the finish
                    // is queued, the pending-scrobble queue is the source of truth for completing
                    // it, so the active-session marker is cleared immediately rather than only on
                    // live-send success — otherwise it lingers indefinitely (the app is shutting
                    // down, so there is no later OnGameStoppedAsync to clear it) and a subsequent
                    // application-stop cycle would re-enqueue a duplicate finish for this game.
                    GsDataManager.EnqueuePendingScrobble(pendingFinish);
                    ClearActiveSession(gameId);

                    try {
                        // Re-check opt-out before sending data (user may have opted out mid-flight)
                        if (GsDataManager.IsOptedOut) continue;

                        var finishResponse = await _apiClient.FinishGameSession(finishData);
                        if (finishResponse != null) {
                            GsDataManager.RemovePendingScrobble(pendingFinish);
                            _logger.Info($"Successfully cleaned up active session on application stop for game ID: {gameId}");
                        }
                        else {
                            _logger.Error($"Failed to finish active session on application stop for game ID: {gameId}. Left queued for retry.");
                        }
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, $"Error finishing session on application stop for game ID: {gameId}. Left queued for retry.");
                    }
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
        /// Computes the diff between the current library DTOs and the local fingerprint index.
        /// </summary>
        internal static (List<GameSyncDto> added, List<GameSyncDto> updated, List<string> removed,
            Dictionary<string, string> currentFingerprints)
            ComputeLibraryDiff(List<GameSyncDto> current, Dictionary<string, string> fingerprints) {
            var added = new List<GameSyncDto>();
            var updated = new List<GameSyncDto>();
            // Computed once here and returned so the caller can reuse it for the index upsert
            // instead of hashing every changed game a second time.
            var currentFingerprints = new Dictionary<string, string>(current.Count);

            foreach (var g in current) {
                var fp = GsHashUtils.ComputeLibraryItemFingerprint(g);
                currentFingerprints[g.playnite_id] = fp;
                if (!fingerprints.TryGetValue(g.playnite_id, out var prev) || prev != fp) {
                    if (prev == null) {
                        added.Add(g);
                    }
                    else {
                        updated.Add(g);
                    }
                }
            }

            var removed = fingerprints.Keys
                .Where(id => !currentFingerprints.ContainsKey(id))
                .ToList();

            return (added, updated, removed, currentFingerprints);
        }

        private const int V4FullSyncChunkSize = 500;

        /// <summary>
        /// Generic v4 begin→chunk→commit upload driver shared by the library and achievement
        /// paths. On a begin/chunk failure, a rejected/failed commit, or a thrown exception it
        /// aborts the server-side session so a retry can start fresh rather than being refused
        /// until the abandoned session's TTL lapses. Never updates any local baseline itself —
        /// the caller commits baselines only after a "queued" response.
        /// </summary>
        private static async Task<AsyncQueuedResponse> UploadFullChunkedAsync<TItem>(
            string label,
            List<TItem> items,
            Func<int, Task<V4SyncBeginRes>> beginAsync,
            Func<string, int, List<TItem>, Task<V4SyncChunkRes>> chunkAsync,
            Func<string, int, Task<AsyncQueuedResponse>> commitAsync,
            Func<string, Task> abortAsync) {
            string syncId = null;
            try {
                var begin = await beginAsync(items.Count);
                if (begin == null || !begin.success || begin.status != "started"
                    || string.IsNullOrEmpty(begin.sync_id)) {
                    _logger.Error($"{label} v4 begin failed: status={begin?.status}, error={begin?.error}");
                    return null;
                }
                syncId = begin.sync_id;
                var chunkSize = begin.max_chunk_items > 0 ? begin.max_chunk_items : V4FullSyncChunkSize;
                var chunkCount = items.Count == 0 ? 0 : (int)Math.Ceiling(items.Count / (double)chunkSize);

                for (var i = 0; i < chunkCount; i++) {
                    var start = i * chunkSize;
                    // GetRange is O(chunk); Skip/Take re-walks from the start each iteration (O(n²)).
                    var slice = items.GetRange(start, Math.Min(chunkSize, items.Count - start));
                    var chunkRes = await chunkAsync(syncId, i, slice);
                    if (chunkRes == null || !chunkRes.success || chunkRes.status != "accepted") {
                        _logger.Error($"{label} v4 chunk {i} failed: status={chunkRes?.status}");
                        await abortAsync(syncId);
                        return null;
                    }
                }

                var commit = await commitAsync(syncId, chunkCount);
                if (commit == null || !commit.success) {
                    // A commit that never resolved (null transport failure) or that the server
                    // rejected leaves the session open; abort so the next attempt can begin anew.
                    _logger.Error($"{label} v4 commit failed: status={commit?.status}");
                    await abortAsync(syncId);
                    return null;
                }
                return commit;
            }
            catch (Exception ex) {
                _logger.Error(ex, $"UploadFullChunkedAsync({label}) failed");
                if (!string.IsNullOrEmpty(syncId)) {
                    await abortAsync(syncId);
                }
                throw;
            }
        }

        /// <summary>
        /// Uploads a full library via v4 begin→chunk→commit. On any failure aborts the session
        /// and does not update the local hash index or LastLibraryHash.
        /// </summary>
        internal async Task<AsyncQueuedResponse> UploadLibraryFullChunkedAsync(
            List<GameSyncDto> library,
            string libraryHash,
            List<IntegrationAccountDto> integrationAccounts) {
            return await UploadFullChunkedAsync(
                "Library",
                library,
                expectedCount => _apiClient.SyncLibraryFullBegin(new LibraryV4FullSyncBeginReq {
                    expected_total_items = expectedCount,
                    result_snapshot_hash = libraryHash,
                    flags = GsDataManager.Data.Flags.ToArray(),
                    integration_accounts = integrationAccounts.Count > 0 ? integrationAccounts : null
                }),
                (syncId, index, slice) => _apiClient.SyncLibraryFullChunk(new LibraryV4ChunkReq {
                    sync_id = syncId,
                    chunk_index = index,
                    items = slice
                }),
                (syncId, chunkCount) => _apiClient.SyncLibraryFullCommit(new LibraryV4CommitReq {
                    sync_id = syncId,
                    result_snapshot_hash = libraryHash,
                    chunk_count = chunkCount,
                    item_count = library.Count
                }),
                syncId => _apiClient.SyncLibraryFullAbort(syncId));
        }

        internal async Task<AsyncQueuedResponse> UploadAchievementsFullChunkedAsync(
            List<GameAchievementsDto> games,
            string achievementHash) {
            return await UploadFullChunkedAsync(
                "Achievements",
                games,
                expectedCount => _apiClient.SyncAchievementsFullBegin(new AchievementsV4FullSyncBeginReq {
                    expected_total_items = expectedCount,
                    result_snapshot_hash = achievementHash
                }),
                (syncId, index, slice) => _apiClient.SyncAchievementsFullChunk(new AchievementsV4ChunkReq {
                    sync_id = syncId,
                    chunk_index = index,
                    items = slice
                }),
                (syncId, chunkCount) => _apiClient.SyncAchievementsFullCommit(new AchievementsV4CommitReq {
                    sync_id = syncId,
                    result_snapshot_hash = achievementHash,
                    chunk_count = chunkCount,
                    item_count = games.Count
                }),
                syncId => _apiClient.SyncAchievementsFullAbort(syncId));
        }

        /// <summary>
        /// Sends the full library via v4 chunked sync and writes the local hash index.
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

                _logger.Info("Starting full library sync (v4 chunked)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                var integrationAccounts = ReadIntegrationAccountsSafe();
                var accountsHash = GsHashUtils.ComputeIntegrationAccountsHash(integrationAccounts);
                var accountsChanged = accountsHash != (GsDataManager.Data.LastIntegrationAccountsHash ?? "");

                if (libraryHash == GsDataManager.Data.LastLibraryHash && GsSyncHashIndex.HasLibraryBaseline && !accountsChanged) {
                    var indexCount = GsSyncHashIndex.LibraryEntryCount;
                    if (indexCount == library.Count) {
                        _logger.Info("Library hash unchanged since last sync — skipping full sync.");
                        return SyncLibraryResult.Skipped;
                    }

                    _logger.Warn($"Library hash matches but index count ({indexCount}) != library ({library.Count}) — repairing local hash index.");
                    var repairDict = library.ToDictionary(
                        g => g.playnite_id,
                        g => GsHashUtils.ComputeLibraryItemFingerprint(g));
                    if (!GsSyncHashIndex.ReplaceLibraryIndex(repairDict)) {
                        _logger.Error("Failed to repair local library hash index.");
                        return SyncLibraryResult.Error;
                    }
                    return SyncLibraryResult.Success;
                }

                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await UploadLibraryFullChunkedAsync(library, libraryHash, integrationAccounts);

                if (response == null) {
                    _logger.Error("Failed to queue full library sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "force-full-sync") {
                    _logger.Error($"Library v4 commit requested force-full-sync (reason: {response.reason}) — not committing baselines.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "skipped" && response.reason != null && response.reason.StartsWith("cooldown_")) {
                    HandleCooldownResponse(response);
                    return SyncLibraryResult.Cooldown;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info($"Full library sync queued ({library.Count} games).");

                    var indexDict = library.ToDictionary(
                        g => g.playnite_id,
                        g => GsHashUtils.ComputeLibraryItemFingerprint(g));
                    if (!GsSyncHashIndex.ReplaceLibraryIndex(indexDict)) {
                        _logger.Error("Full library sync queued but local hash index save failed — " +
                            "not committing hash baseline. Will retry full sync next run.");
                        return SyncLibraryResult.Error;
                    }

                    var libCount = library.Count;
                    GsDataManager.MutateAndSave(d => {
                        d.LastSyncAt = DateTime.UtcNow;
                        d.LastSyncGameCount = libCount;
                        d.LastLibraryHash = libraryHash;
                        d.LastIntegrationAccountsHash = accountsHash;
                        d.SyncCooldownExpiresAt = null;
                    });

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
                    .Where(GsAllowedPlugins.IsAllowed)
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
                    var indexCount = GsSyncHashIndex.LibraryEntryCount;
                    if (indexCount == library.Count) {
                        _logger.Info("Library hash unchanged since last sync — skipping diff sync.");
                        return SyncLibraryResult.Skipped;
                    }

                    _logger.Warn($"Library hash matches but index count ({indexCount}) != library ({library.Count}) — repairing local hash index.");
                    var repairDict = library.ToDictionary(
                        g => g.playnite_id,
                        g => GsHashUtils.ComputeLibraryItemFingerprint(g));
                    if (!GsSyncHashIndex.ReplaceLibraryIndex(repairDict)) {
                        _logger.Error("Failed to repair local library hash index.");
                        return SyncLibraryResult.Error;
                    }
                    return SyncLibraryResult.Success;
                }

                var fingerprints = GsSyncHashIndex.GetLibraryFingerprints();
                var (added, updated, removed, currentFingerprints) = await Task.Run(() =>
                    ComputeLibraryDiff(library, fingerprints));

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
                    GsSyncHashIndex.ClearLibraryIndex();
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

                    var upserted = added.Concat(updated).ToDictionary(
                        g => g.playnite_id,
                        g => currentFingerprints[g.playnite_id]);
                    if (!GsSyncHashIndex.ApplyLibraryDiff(upserted, removed)) {
                        _logger.Error("Library diff queued but local hash index save failed — " +
                            "not committing hash baseline.");
                        return SyncLibraryResult.Error;
                    }

                    var libCount = library.Count;
                    GsDataManager.MutateAndSave(d => {
                        d.LastSyncAt = DateTime.UtcNow;
                        d.LastSyncGameCount = libCount;
                        d.LastLibraryHash = libraryHash;
                        d.LastIntegrationAccountsHash = accountsHash;
                        d.LibraryDiffSyncCooldownExpiresAt = null;
                    });

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

                _logger.Info("Starting full achievements sync (v4 chunked)");
                List<Playnite.SDK.Models.Game> allGames;
                try {
                    allGames = playniteDatabaseGames.ToList();
                }
                catch (InvalidOperationException) {
                    allGames = playniteDatabaseGames.ToList();
                }

                var games = await Task.Run(() => {
                    return allGames
                        .Where(GsAllowedPlugins.IsAllowed)
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
                                source_name = g.Source?.Name,
                                achievements = dedupedByName.Values.ToList()
                            };
                        })
                        .Where(x => x != null)
                        .ToList();
                });

                var achHash = GsHashUtils.ComputeAchievementHash(games);

                if (games.Count == 0) {
                    _logger.Info("No games with achievements found — setting empty baseline.");
                    if (!GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string>())) {
                        _logger.Error("Failed to persist empty achievements baseline.");
                        return SyncLibraryResult.Error;
                    }
                    GsDataManager.MutateAndSave(d => d.LastAchievementHash = achHash);
                    return SyncLibraryResult.Skipped;
                }

                if (achHash == GsDataManager.Data.LastAchievementHash && GsSyncHashIndex.HasAchievementsBaseline) {
                    if (GsSyncHashIndex.AchievementEntryCount == games.Count) {
                        _logger.Info("Achievement hash unchanged since last sync — skipping full sync.");
                        return SyncLibraryResult.Skipped;
                    }
                    _logger.Warn("Achievement hash matches but index count diverged — repairing local index.");
                    var repair = games.ToDictionary(
                        g => g.playnite_id,
                        g => GsHashUtils.ComputeAchievementGameFingerprint(g));
                    if (!GsSyncHashIndex.ReplaceAchievementIndex(repair)) {
                        return SyncLibraryResult.Error;
                    }
                    return SyncLibraryResult.Success;
                }

                _logger.Info($"Sending full achievements for {games.Count} games.");

                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await UploadAchievementsFullChunkedAsync(games, achHash);

                if (response == null) {
                    _logger.Error("Failed to queue full achievements sync.");
                    return SyncLibraryResult.Error;
                }

                if (response.status == "force-full-sync") {
                    _logger.Error($"Achievements v4 commit requested force-full-sync (reason: {response.reason})");
                    return SyncLibraryResult.Error;
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Full achievements sync queued successfully.");

                    var indexDict = games.ToDictionary(
                        g => g.playnite_id,
                        g => GsHashUtils.ComputeAchievementGameFingerprint(g));
                    if (!GsSyncHashIndex.ReplaceAchievementIndex(indexDict)) {
                        _logger.Error("Full achievements sync queued but local hash index save failed — " +
                            "not committing hash baseline.");
                        return SyncLibraryResult.Error;
                    }

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
                var achievementFingerprints = GsSyncHashIndex.GetAchievementFingerprints();

                if (_achievementHelper is GsAchievementAggregator agg) {
                    var installed = agg.GetInstalledProviders();
                    _logger.Info($"Achievement providers installed: {installed.Count} — " +
                        string.Join(", ", installed.Select(p => $"{p.ProviderName} (v{p.GetVersion() ?? "?"})")));
                }
                _logger.Info($"Achievement diff: {allGames.Count} total games, " +
                    $"index has {achievementFingerprints.Count} entries");

                var (changed, clearedIds, liveWithAchievements, changedFingerprints) = await Task.Run(() => {
                    var result = new List<GameAchievementsDto>();
                    var live = new List<GameAchievementsDto>();
                    var currentGameIds = new HashSet<string>();
                    // Fingerprints for changed games that still have achievements — reused below
                    // for the index upsert so we don't hash each changed game a second time.
                    var changedFps = new Dictionary<string, string>();
                    int filteredCount = 0;
                    int nullCount = 0;
                    int withDataCount = 0;

                    foreach (var g in allGames) {
                        if (!GsAllowedPlugins.IsAllowed(g))
                            continue;

                        filteredCount++;
                        var playniteId = g.Id.ToString();
                        List<AchievementItem> achievements;
                        string sourceProvider = null;

                        if (_achievementHelper is GsAchievementAggregator diagAgg) {
                            var (achs, src) = diagAgg.GetAchievementsWithSource(g.Id);
                            achievements = achs;
                            sourceProvider = src;
                        }
                        else {
                            achievements = _achievementHelper.GetAchievements(g.Id);
                        }

                        if (achievements == null || achievements.Count == 0) {
                            nullCount++;
                            if (nullCount <= 3) {
                                _logger.Debug($"Achievement diag: game '{g.Name}' (plugin={g.PluginId}) returned no achievements");
                            }
                        }
                        else {
                            withDataCount++;
                            if (withDataCount == 1) {
                                _logger.Info($"Achievement diag: first hit from '{sourceProvider ?? "unknown"}' — " +
                                    $"game '{g.Name}' has {achievements.Count} achievements");
                            }
                        }

                        if ((achievements == null || achievements.Count == 0)
                            && achievementFingerprints.ContainsKey(playniteId)) {
                            currentGameIds.Add(playniteId);
                            result.Add(new GameAchievementsDto {
                                playnite_id = playniteId,
                                game_id = g.GameId,
                                plugin_id = g.PluginId.ToString(),
                                source_name = g.Source?.Name,
                                achievements = new List<AchievementItemDto>()
                            });
                            continue;
                        }

                        if (achievements == null || achievements.Count == 0)
                            continue;

                        currentGameIds.Add(playniteId);

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

                        var dto = new GameAchievementsDto {
                            playnite_id = playniteId,
                            game_id = g.GameId,
                            plugin_id = g.PluginId.ToString(),
                            source_name = g.Source?.Name,
                            achievements = dedupedByName.Values.ToList()
                        };
                        live.Add(dto);

                        var fp = GsHashUtils.ComputeAchievementGameFingerprint(dto);
                        if (!achievementFingerprints.TryGetValue(playniteId, out var prevFp) || prevFp != fp) {
                            result.Add(dto);
                            changedFps[playniteId] = fp;
                        }
                    }

                    _logger.Info($"Achievement diff scan: {filteredCount} eligible games, " +
                        $"{withDataCount} with data, {nullCount} with no data, " +
                        $"{result.Count} changed");

                    var cleared = achievementFingerprints.Keys
                        .Where(id => !currentGameIds.Contains(id))
                        .ToList();

                    return (result, cleared, live, changedFps);
                });

                if (changed.Count == 0 && clearedIds.Count == 0) {
                    _logger.Info("Achievement diff is empty — skipping.");
                    return SyncLibraryResult.Skipped;
                }

                foreach (var clearedId in clearedIds) {
                    changed.Add(new GameAchievementsDto {
                        playnite_id = clearedId,
                        achievements = new List<AchievementItemDto>()
                    });
                }

                _logger.Info($"Achievement diff: {changed.Count} games total ({clearedIds.Count} cleared).");

                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var upsertedFingerprints = changedFingerprints;
                var allCleared = changed
                    .Where(g => g.achievements == null || g.achievements.Count == 0)
                    .Select(g => g.playnite_id)
                    .Concat(clearedIds)
                    .Distinct()
                    .ToList();

                var resultAchievementHash = GsHashUtils.ComputeAchievementHash(liveWithAchievements);

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
                    GsSyncHashIndex.ClearAchievementIndex();
                    GsDataManager.MutateAndSave(d => d.LastAchievementHash = null);
                    return await SyncAchievementsFullAsync(playniteDatabaseGames, bypassCooldown: true);
                }

                if (response.success && response.status == "queued") {
                    _logger.Info("Achievement diff sync queued successfully.");

                    if (!GsSyncHashIndex.ApplyAchievementDiff(upsertedFingerprints, allCleared)) {
                        _logger.Error("Achievement diff queued but local hash index save failed — " +
                            "not committing hash baseline.");
                        return SyncLibraryResult.Error;
                    }
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
