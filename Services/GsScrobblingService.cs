using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Playnite;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    /// <summary>
    /// Service responsible for handling game scrobbling functionality.
    /// Tracks game sessions by recording start/stop events and communicating with the API.
    /// </summary>
    public class GsScrobblingService {
        private static readonly ILogger _logger = LogManager.GetLogger<GsScrobblingService>();
        private readonly IGsApiClient _apiClient;
        private readonly GsIntegrationAccountReader _integrationAccountReader;
        private readonly ILibraryApi _libraryApi;

        /// <summary>
        /// Per-game mutual exclusion plus the count of handlers currently holding it. The count
        /// exists so the entry can be retired without a handler that already took it from the map
        /// losing its exclusion: retiring on "looks uncontended" alone let a handler that had the
        /// instance but had not yet awaited it be replaced by a fresh gate, so two handlers for
        /// the same game could run at once.
        /// </summary>
        private sealed class SessionGate {
            public readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);
            public int Users;
        }

        private readonly ConcurrentDictionary<string, SessionGate> _sessionGates =
            new ConcurrentDictionary<string, SessionGate>();

        /// <summary>
        /// A game whose start this process sent, held until it stops. The captured start
        /// instant travels with it so a shutdown finish can state its own session even
        /// while the start is still in flight.
        /// </summary>
        private sealed class RunningGame {
            public Game Game = null!;
            public DateTime StartedAt;
        }

        private readonly ConcurrentDictionary<string, RunningGame> _runningGames =
            new ConcurrentDictionary<string, RunningGame>();

        /// <summary>
        /// Initializes a new instance of the GsScrobblingService.
        /// </summary>
        /// <param name="apiClient">The API client for communicating with the GameScrobbler service.</param>
        /// <param name="integrationAccountReader">Reader for extracting integration account identities from library plugin configs.</param>
        /// <param name="libraryApi">Playnite library API for resolving game metadata IDs to names.</param>
        /// <param name="libraryApi">
        /// Playnite's library collections, used to resolve the name behind a Game's *Id fields.
        /// Only the game-mapping paths need it, so tests that drive already-built DTOs through
        /// the upload paths may pass null rather than standing up the whole interface.
        /// </param>
        public GsScrobblingService(IGsApiClient apiClient, GsIntegrationAccountReader? integrationAccountReader, ILibraryApi? libraryApi) {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _integrationAccountReader = integrationAccountReader;
            _libraryApi = libraryApi!;

            // P11's Game carries only SourceId, so GsAllowedPlugins cannot resolve a source
            // display name on its own. Hand it a resolver so the OSS/fork source-alias
            // fallback (GOG OSS, Legendary, ...) works the way it does on Playnite 10.
            if (libraryApi != null) {
                GsAllowedPlugins.ConfigureSourceNameResolver(
                    sourceId => libraryApi.Sources.Get(sourceId)?.Name);
            }
        }

        /// <summary>
        /// The scrobble timestamp format. Local time (not UTC) on purpose: the server
        /// stores the offset carried by "K" and reports sessions in the player's own clock.
        /// </summary>
        private const string ScrobbleTimestampFormat = "yyyy-MM-ddTHH:mm:ssK";

        /// <summary>
        /// Renders a scrobble timestamp in <see cref="ScrobbleTimestampFormat"/>, always with
        /// <see cref="CultureInfo.InvariantCulture"/>. In a custom format string "-" and ":" are the
        /// culture's date/time separator specifiers and "yyyy" renders through the culture's calendar,
        /// so under th-TH (ThaiBuddhist) the year would serialize as 2569 and under fi-FI the time as
        /// "14.47.53" - values the server cannot parse. Call sites must go through this helper rather
        /// than formatting inline, so a new one cannot silently reintroduce the bug.
        /// </summary>
        internal static string FormatScrobbleTimestamp(DateTime at) =>
            at.ToString(ScrobbleTimestampFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// Builds the session-start payload for a Playnite game. Shared by the live send and the
        /// queued-retry copy so a replayed start is byte-identical to the one that failed.
        /// <c>user_id</c> resolves at call time, matching the per-call-site evaluation it replaces.
        /// </summary>
        /// <summary>
        /// P11's Game carries only SourceId; the display name lives in ILibraryApi.Sources.
        /// </summary>
        private string? ResolveSourceName(Game g) =>
            string.IsNullOrEmpty(g.SourceId) ? null : _libraryApi.Sources.Get(g.SourceId)?.Name;

        private ScrobbleStartReq BuildStartReq(Game g, DateTime at) {
            return new ScrobbleStartReq {
                user_id = GsDataManager.InstallIdForBody,
                game_name = g.Name,
                game_id = g.Id.ToString(),
                plugin_id = g.LibraryId,
                external_game_id = g.LibraryGameId,
                source_name = ResolveSourceName(g),
                metadata = new { PluginId = g.LibraryId, SourceName = ResolveSourceName(g) },
                started_at = FormatScrobbleTimestamp(at)
            };
        }

        /// <summary>
        /// Builds the session-finish payload for a Playnite game. Shared by the live send and the
        /// queued-retry copy. Pass a null <paramref name="sessionId"/> for the queued-start pairing
        /// path, where no server session exists yet.
        /// <paramref name="startedAt"/> is the start event's own timestamp string, passed through
        /// unchanged: the server matches it as an exact instant, so re-formatting a
        /// <see cref="DateTime"/> here would break that match. Null when unknown.
        /// </summary>
        private ScrobbleFinishReq BuildFinishReq(Game g, string sessionId, string startedAt, DateTime at) {
            return new ScrobbleFinishReq {
                user_id = GsDataManager.InstallIdForBody,
                game_name = g.Name,
                game_id = g.Id.ToString(),
                plugin_id = g.LibraryId,
                external_game_id = g.LibraryGameId,
                source_name = ResolveSourceName(g),
                session_id = sessionId,
                started_at = startedAt,
                metadata = new { PluginId = g.LibraryId, SourceName = ResolveSourceName(g) },
                finished_at = FormatScrobbleTimestamp(at)
            };
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

        private static bool IsCurrentIdentity(string installId, int generation) =>
            GsDataManager.IsActiveIdentity(installId, generation);

        /// <summary>
        /// Forgets a game the plugin will not report on: it stopped while its source was no
        /// longer eligible, or while scrobbling was switched off. Without this both the live
        /// active-session entry and the in-memory running-game entry survive the stop, and
        /// <see cref="OnApplicationStoppedAsync"/>, which filters by neither, turns whichever
        /// one remains into a finish stamped at Playnite's exit time, reporting a session that
        /// ran until the app closed.
        /// </summary>
        /// <summary>
        /// Takes a counted reference to this game's gate, creating it if needed. Acquisition and
        /// retirement both mutate the count under the entry's own lock and retirement removes the
        /// entry inside that lock, so a caller either registers before the entry can leave the map
        /// or observes that it already has and retries against the replacement.
        /// </summary>
        /// <summary>Live gate count, so a test can assert entries are retired rather than leaked.</summary>
        internal int SessionGateCount => _sessionGates.Count;

        private SessionGate AcquireSessionGate(string gameId) {
            while (true) {
                var entry = _sessionGates.GetOrAdd(gameId, _ => new SessionGate());
                lock (entry) {
                    if (_sessionGates.TryGetValue(gameId, out var current) && ReferenceEquals(current, entry)) {
                        entry.Users++;
                        return entry;
                    }
                }
            }
        }

        /// <summary>
        /// Drops a reference and retires the gate once the last handler is done, so the map does
        /// not grow for the life of the process. The semaphore is deliberately not disposed: it is
        /// only reachable from handlers that still hold a reference, it owns no unmanaged handle
        /// (nothing here touches AvailableWaitHandle), and disposing it under a handler that had
        /// already taken it would surface as ObjectDisposedException on a live scrobble.
        /// </summary>
        private void ReleaseSessionGate(string gameId, SessionGate entry) {
            if (gameId == null || entry == null) return;
            lock (entry) {
                if (--entry.Users > 0) return;
                _sessionGates.TryRemove(gameId, out _);
            }
        }

        private void DiscardTrackedSession(string gameId) {
            _runningGames.TryRemove(gameId, out _);
            if (string.IsNullOrEmpty(gameId) || !GsDataManager.HasActiveSession(gameId)) return;
            _logger.Info($"Clearing active session for no-longer-tracked game ID: {gameId}");
            GsDataManager.MutateAndSave(d => d.RemoveActiveSession(gameId));
        }

        /// <summary>Persists the start before attempting its live send.</summary>
        public async Task OnGameStartAsync(Game game) {
            var at = DateTime.Now;
            PendingScrobble pending = null;
            SessionGate gate = null;
            // Declared out here so the finally can hand the gate back for disposal.
            string gameKey = null;
            var enteredGate = false;
            try {
                if (GsDataManager.IsOptedOut || GsDataManager.Data.Flags.Contains("no-scrobble")
                    || game == null || !GsAllowedPlugins.IsAllowed(game)) return;

                var gameId = game.Id.ToString();
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                pending = new PendingScrobble {
                    Type = "start",
                    StartData = BuildStartReq(game, at),
                    QueuedAt = at
                };
                GsDataManager.ClaimPendingScrobble(pending);
                // Persist the event before any await. Claims prevent the background flusher
                // from sending this same item while the live handler owns it.
                if (!GsDataManager.TryMutateIfActiveIdentity(installId, generation, d => {
                    d.PendingScrobbles.Add(pending);
                    if (!d.PendingStartGameIds.Contains(gameId)) d.PendingStartGameIds.Add(gameId);
                })) return;
                _runningGames[gameId] = new RunningGame { Game = game, StartedAt = at };

                gameKey = gameId;
                gate = AcquireSessionGate(gameId);
                await gate.Semaphore.WaitAsync();
                enteredGate = true;
                if (!IsCurrentIdentity(installId, generation)) return;
                if (GsDataManager.HasEarlierPendingScrobble(pending)) return;

                var response = await _apiClient.StartGameSession(pending.StartData);
                if (response != null) {
                    // Atomically associate an already-queued stop with the returned session,
                    // or record an active session if this game has not stopped yet.
                    GsDataManager.CompletePendingStart(pending, response.session_id);
                }
                else {
                    _logger.Warn($"Start for game {gameId} remains queued for retry.");
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error starting scrobble session; any persisted event remains queued.");
            }
            finally {
                if (enteredGate) gate.Semaphore.Release();
                if (pending != null) GsDataManager.ReleasePendingScrobble(pending);
                ReleaseSessionGate(gameKey, gate);
            }
        }

        /// <summary>
        /// Captures and persists the stop timestamp immediately, then waits for an earlier
        /// start of this game to resolve. A slow start cannot discard a short session's stop.
        /// </summary>
        /// <param name="game">The game that stopped.</param>
        public async Task OnGameStoppedAsync(Game game) {
            var at = DateTime.Now;
            PendingScrobble pending = null;
            SessionGate gate = null;
            // Declared out here so the finally can hand the gate back for disposal.
            string gameKey = null;
            var enteredGate = false;
            try {
                if (GsDataManager.IsOptedOut || game == null) return;

                var gameId = game.Id.ToString();
                // Split out from the opt-out check: these two say "do not report this stop",
                // not "leave the session open". Returning without clearing what the start
                // recorded is what let a stopped game be finished again at Playnite's exit.
                if (GsDataManager.Data.Flags.Contains("no-scrobble") || !GsAllowedPlugins.IsAllowed(game)) {
                    DiscardTrackedSession(gameId);
                    return;
                }

                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                var startPending = GsDataManager.HasPendingStart(gameId);
                GsDataManager.TryGetActiveSession(gameId, out var sessionId);
                if (!startPending && string.IsNullOrEmpty(sessionId)) return;

                pending = new PendingScrobble {
                    Type = "finish",
                    FinishData = BuildFinishReq(game, startPending ? null : sessionId,
                        _runningGames.TryGetValue(gameId, out var running)
                            ? FormatScrobbleTimestamp(running.StartedAt)
                            : null,
                        at),
                    QueuedAt = at
                };
                GsDataManager.ClaimPendingScrobble(pending);
                if (!GsDataManager.TryMutateIfActiveIdentity(installId, generation, d => {
                    // The start may have completed between the initial snapshot and this
                    // transaction. Resolve its session here before appending the stop.
                    if (string.IsNullOrEmpty(pending.FinishData.session_id)
                        && !d.PendingScrobbles.Any(item => item.Type == "start" && item.StartData?.game_id == gameId)
                        && d.ActiveSessionsByGameId.TryGetValue(gameId, out var completedSession)) {
                        pending.FinishData.session_id = completedSession;
                    }
                    // Same resolution the shutdown path uses: the start that recorded this
                    // instant may only be visible from inside the transaction.
                    if (string.IsNullOrEmpty(pending.FinishData.started_at)) {
                        pending.FinishData.started_at = d.ResolveSessionStart(gameId);
                    }
                    d.PendingScrobbles.Add(pending);
                    d.PendingStartGameIds.Remove(gameId);
                    // The durable finish owns completion from now on. Never erase a newer
                    // session merely because it uses the same Playnite game ID.
                    if (!string.IsNullOrEmpty(pending.FinishData.session_id)
                        && d.ActiveSessionsByGameId.TryGetValue(gameId, out var current) && current == pending.FinishData.session_id) {
                        d.RemoveActiveSession(gameId);
                    }
                })) return;
                _runningGames.TryRemove(gameId, out _);

                gameKey = gameId;
                gate = AcquireSessionGate(gameId);
                await gate.Semaphore.WaitAsync();
                enteredGate = true;
                if (!IsCurrentIdentity(installId, generation)) return;
                if (GsDataManager.HasEarlierPendingScrobble(pending)) return;
                // Nothing here identifies which session to close: no server session id, and no
                // start instant to match one on. Only the queued replay can resolve it, once
                // the start ahead of it in the queue succeeds. A finish that knows when its
                // session began needs no such help: it describes the session on its own.
                if (string.IsNullOrEmpty(pending.FinishData.session_id)
                    && string.IsNullOrEmpty(pending.FinishData.started_at)) return;

                var response = await _apiClient.FinishGameSession(pending.FinishData);
                if (response != null) GsDataManager.CompletePendingScrobble(pending);
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error stopping scrobble session; any persisted event remains queued.");
            }
            finally {
                if (enteredGate) gate.Semaphore.Release();
                if (pending != null) GsDataManager.ReleasePendingScrobble(pending);
                ReleaseSessionGate(gameKey, gate);
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
            var pendingFinishes = new List<PendingScrobble>();
            try {
                if (GsDataManager.IsOptedOut || GsDataManager.Data.Flags.Contains("no-scrobble")) return;
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                var at = DateTime.Now;
                var activeSessions = GsDataManager.SnapshotActiveSessions();
                foreach (var entry in activeSessions) {
                    pendingFinishes.Add(new PendingScrobble {
                        Type = "finish",
                        QueuedAt = at,
                        FinishData = new ScrobbleFinishReq {
                            user_id = GsDataManager.InstallIdForBody,
                            game_id = entry.Key,
                            session_id = entry.Value,
                            // This payload names no game, so without the start instant a
                            // session_id the server has since closed leaves it nothing exact
                            // to match on. Null only for sessions started before the plugin
                            // began recording it.
                            started_at = GsDataManager.ResolveSessionStart(entry.Key),
                            metadata = new { reason = "application_stopped" },
                            finished_at = FormatScrobbleTimestamp(at)
                        }
                    });
                }
                // Starts can still be awaiting the server during shutdown. Their durable
                // finishes pair with those queued starts on response or on the next launch.
                foreach (var entry in _runningGames.ToArray()) {
                    if (activeSessions.ContainsKey(entry.Key)) continue;
                    pendingFinishes.Add(new PendingScrobble {
                        Type = "finish",
                        QueuedAt = at,
                        FinishData = BuildFinishReq(entry.Value.Game, null,
                            FormatScrobbleTimestamp(entry.Value.StartedAt), at)
                    });
                }
                if (pendingFinishes.Count == 0) return;

                foreach (var pending in pendingFinishes) GsDataManager.ClaimPendingScrobble(pending);
                // This single durable write covers every session before the first await.
                if (!GsDataManager.QueueSessionFinishesAndClearActive(activeSessions, pendingFinishes, installId, generation)) return;
                _runningGames.Clear();
                foreach (var pending in pendingFinishes) {
                    if (!IsCurrentIdentity(installId, generation)) break;
                    if ((string.IsNullOrEmpty(pending.FinishData.session_id)
                            && string.IsNullOrEmpty(pending.FinishData.started_at))
                        || GsDataManager.HasEarlierPendingScrobble(pending)) continue;
                    try {
                        var response = await _apiClient.FinishGameSession(pending.FinishData);
                        if (response != null) GsDataManager.CompletePendingScrobble(pending);
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Shutdown finish remains queued for retry.");
                    }
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Error preparing shutdown scrobbles.");
            }
            finally {
                foreach (var pending in pendingFinishes) GsDataManager.ReleasePendingScrobble(pending);
            }
        }
        #region v3 Sync Methods


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
                // Achievement counts stay null on P11: no P11-compatible achievement addon
                // exists yet, so nothing reads them. They remain part of the hashed payload
                // (as empty) so the digest still matches the server's createLibraryHashV3.
                achievement_count_unlocked = null,
                achievement_count_total = null,
                user_score = g.UserScore,
                date_added = g.AddedDate?.UtcDateTime,
                is_favorite = g.Favorite,
                is_hidden = g.Hidden,
                source_name = g.SourceId != null
                    ? _libraryApi.Sources.Get(g.SourceId)?.Name
                    : null,
                modified = g.ModifiedDate?.UtcDateTime
            };
        }


        /// <summary>
        /// Builds a full { playnite_id, fingerprint } map for the library, the shape the local
        /// hash index stores as its baseline.
        /// </summary>
        private static Dictionary<string, string> BuildLibraryFingerprints(List<GameSyncDto> library) {
            return library.ToDictionary(
                g => g.playnite_id,
                g => GsHashUtils.ComputeLibraryItemFingerprint(g));
        }

        /// <summary>
        /// Builds a full { playnite_id, fingerprint } map for achievements, the shape the local
        /// hash index stores as its baseline.
        /// </summary>
        private static Dictionary<string, string> BuildAchievementFingerprints(List<GameAchievementsDto> games) {
            return games.ToDictionary(
                g => g.playnite_id,
                g => GsHashUtils.ComputeAchievementGameFingerprint(g));
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
        private const int V4FullSyncMaxChunkBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Splits v4 items by both the negotiated item limit and the backend's UTF-8 JSON
        /// payload limit. The backend measures JSON.stringify(items), so measuring the array
        /// rather than the enclosing request keeps this calculation aligned with its contract.
        /// System.Text.Json can escape more non-ASCII characters than JSON.stringify, which
        /// only makes this client-side calculation conservatively larger.
        /// </summary>
        internal static List<List<TItem>> CreateV4FullSyncChunks<TItem>(
            List<TItem> items,
            int negotiatedMaxChunkItems) {
            var maxChunkItems = negotiatedMaxChunkItems > 0
                ? Math.Min(negotiatedMaxChunkItems, V4FullSyncChunkSize)
                : V4FullSyncChunkSize;
            var chunks = new List<List<TItem>>();
            var current = new List<TItem>(Math.Min(maxChunkItems, items.Count));
            // Opening and closing brackets for the JSON array.
            var currentBytes = 2;

            foreach (var item in items) {
                var itemBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(item));
                if (itemBytes + 2 > V4FullSyncMaxChunkBytes) {
                    throw new InvalidOperationException(
                        $"A single v4 full-sync item exceeds the {V4FullSyncMaxChunkBytes}-byte payload limit.");
                }

                var separatorBytes = current.Count == 0 ? 0 : 1;
                if (current.Count >= maxChunkItems
                    || currentBytes + separatorBytes + itemBytes > V4FullSyncMaxChunkBytes) {
                    chunks.Add(current);
                    current = new List<TItem>(Math.Min(maxChunkItems, items.Count));
                    currentBytes = 2;
                    separatorBytes = 0;
                }

                current.Add(item);
                currentBytes += separatorBytes + itemBytes;
            }

            if (current.Count > 0) {
                chunks.Add(current);
            }
            return chunks;
        }

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
                List<List<TItem>> chunks;
                try {
                    chunks = CreateV4FullSyncChunks(items, begin.max_chunk_items);
                }
                catch (InvalidOperationException ex) {
                    _logger.Error(ex, $"{label} v4 payload cannot be split within the server limit");
                    await abortAsync(syncId);
                    return null;
                }
                var chunkCount = chunks.Count;

                for (var i = 0; i < chunkCount; i++) {
                    var chunkRes = await chunkAsync(syncId, i, chunks[i]);
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
                    //
                    // force-full-sync is a business outcome, not a failure: the server answers a
                    // snapshot-hash mismatch with success=false and a reason the caller acts on.
                    // Collapsing it to null here would keep the caller's recovery branch unreachable
                    // even though the body now survives the HTTP layer, so propagate it. The session
                    // is still aborted either way -- nothing was committed.
                    var isForceFullSync = commit != null && commit.status == "force-full-sync";
                    if (isForceFullSync) {
                        _logger.Warn($"{label} v4 commit rejected: force-full-sync (reason: {commit.reason})");
                    }
                    else {
                        _logger.Error($"{label} v4 commit failed: status={commit?.status}");
                    }
                    await abortAsync(syncId);
                    return isForceFullSync ? commit : null;
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

        /// <summary>Polling cadence for <see cref="TryConfirmQueueCompletionAsync"/>.</summary>
        internal TimeSpan QueueStatusPollInterval { get; set; } = TimeSpan.FromSeconds(1.5);

        /// <summary>
        /// Total time budget for <see cref="TryConfirmQueueCompletionAsync"/>. Matches the server's
        /// documented "1-5 seconds" typical processing time with margin, so a poll never meaningfully
        /// delays startup/library-updated callbacks that await these sync methods.
        /// </summary>
        internal TimeSpan QueueStatusPollBudget { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Best-effort check of a queued sync job's terminal status via GET /queue/status/:queueId.
        /// A "queued" admission only means the request was accepted onto the async queue, not that
        /// the server applied it — see gs-playnite#83, where a job that failed after admission left
        /// the client believing it had synced. That issue is a job the server *rejected*, which
        /// this reports as false and the caller treats as fatal.
        ///
        /// A null is a different answer and must not be conflated with it: the job was admitted
        /// and simply has not finished inside the budget. Treating that as failure meant a library
        /// whose server-side job routinely runs longer than the budget never advanced its baseline,
        /// so every launch re-uploaded the whole library and LastSyncAt never moved: the
        /// force-full-sync loop this whole path exists to avoid, and strictly worse than trusting
        /// an admission the server has not disowned.
        /// </summary>
        /// <returns>true if the job completed successfully; false if the server disowned it
        /// (failed/partial) or never gave a job id to poll; null if it was admitted and simply
        /// had not finished within the budget.</returns>
        private async Task<bool?> TryConfirmQueueCompletionAsync(string label, string queueId) {
            if (string.IsNullOrEmpty(queueId)) {
                // false, not null. "Admitted but still working" is a job we can come back to;
                // an admission carrying no job id can never be confirmed at all, which is a
                // malformed response rather than a slow one. Report it as a failure so the
                // baseline is held back.
                _logger.Error($"{label}: server accepted the sync but returned no job id; not committing the baseline.");
                return false;
            }

            var deadline = DateTime.UtcNow + QueueStatusPollBudget;
            do {
                await Task.Delay(QueueStatusPollInterval);

                QueueStatusRes res;
                try {
                    res = await _apiClient.GetQueueStatus(queueId);
                }
                catch (Exception ex) {
                    _logger.Warn(ex, $"{label}: queue status poll failed for {queueId}");
                    continue;
                }

                switch (res?.data?.status) {
                    case "completed":
                        return true;
                    case "partial":
                    case "failed":
                        _logger.Error($"{label}: server job {queueId} ended with status={res.data.status}" +
                            (string.IsNullOrEmpty(res.data.errorMessage) ? "" : $" ({res.data.errorMessage})"));
                        return false;
                    default:
                        // pending / processing / retrying / no response yet — keep waiting.
                        break;
                }
            } while (DateTime.UtcNow < deadline);

            _logger.Info($"{label}: no confirmed completion for job {queueId} after {QueueStatusPollBudget.TotalSeconds:F0}s — " +
                "the job was admitted and is still processing; committing the baseline optimistically.");
            return null;
        }

        /// <summary>
        /// Handles the "global hash is unchanged since the last sync" branch shared by every sync
        /// path. Normally that means there is nothing to send. If the local per-item index has
        /// drifted out of step with the live item count, the index is rewritten from live data
        /// instead: a self-heal that uploads nothing, since the server-side baseline is already
        /// correct (that is what the matching global hash proves).
        /// </summary>
        /// <param name="label">Sync path name used to prefix log lines.</param>
        /// <param name="indexCount">Entry count currently held in the local hash index.</param>
        /// <param name="liveCount">Item count computed from the live Playnite data.</param>
        /// <param name="replaceIndex">Rewrites the index from live data; returns false if the save failed.</param>
        private static SyncLibraryResult SkipOrRepairIndex(
            string label,
            int indexCount,
            int liveCount,
            Func<bool> replaceIndex) {
            if (indexCount == liveCount) {
                _logger.Info($"{label}: hash unchanged since last sync — skipping.");
                return SyncLibraryResult.Skipped;
            }

            _logger.Warn($"{label}: hash matches but index count ({indexCount}) != live count ({liveCount}) — " +
                "repairing local hash index.");
            if (!replaceIndex()) {
                _logger.Error($"{label}: failed to repair local hash index.");
                return SyncLibraryResult.Error;
            }
            return SyncLibraryResult.Success;
        }

        /// <summary>
        /// Commits local sync baselines after the server accepted a job onto its async queue.
        /// This is the single place the baseline-ordering invariant is enforced, for every sync path:
        /// <list type="number">
        /// <item>confirm the queued job actually completed (a "queued" admission is not success),</item>
        /// <item>persist the per-item hash index for ALL items,</item>
        /// <item>only then write the global Last*Hash and the rest of the sync bookkeeping.</item>
        /// </list>
        /// Any step failing returns <see cref="SyncLibraryResult.Error"/> without touching the
        /// steps after it, so a mid-failure leaves the previous good baseline intact and the next
        /// run re-syncs rather than trusting a baseline that was never durably written.
        /// </summary>
        /// <param name="label">Sync path name used to prefix log lines.</param>
        /// <param name="queueId">Server queue job id from the "queued" response.</param>
        /// <param name="persistIndex">Writes the per-item hash index; returns false if the save failed.</param>
        /// <param name="persistHashes">Writes the global hash and sync bookkeeping into <see cref="GsData"/>.</param>
        /// <param name="queuedDetail">Optional detail appended to the "queued successfully" log line.</param>
        private async Task<SyncLibraryResult> CommitSyncBaselineAsync(
            string label,
            string queueId,
            string expectedInstallId,
            int expectedGeneration,
            Func<bool> persistIndex,
            Action<GsData> persistHashes,
            string queuedDetail = null) {
            // == false, not != true. Only a status the server actually disowned (failed/partial)
            // blocks the baseline; an unconfirmed-but-admitted job keeps it, because refusing to
            // advance on a slow job is a permanent full re-upload every launch.
            if (await TryConfirmQueueCompletionAsync(label, queueId) == false) {
                return SyncLibraryResult.Error;
            }

            _logger.Info(string.IsNullOrEmpty(queuedDetail)
                ? $"{label} queued successfully."
                : $"{label} queued successfully ({queuedDetail}).");

            var indexSaved = false;
            // The data lock also fences the index write. Index stores read DataOrNull
            // without taking this lock, so this does not invert their lock order.
            var saved = GsDataManager.TryMutateIfActiveIdentity(expectedInstallId, expectedGeneration, d => {
                indexSaved = persistIndex();
                if (indexSaved) persistHashes(d);
            });
            if (!saved || !indexSaved) {
                _logger.Error($"{label} queued but local hash index save failed — " +
                    "or the installation changed; will retry next run.");
                return SyncLibraryResult.Error;
            }
            return SyncLibraryResult.Success;
        }

        /// <summary>
        /// Sends the full library via v4 chunked sync and writes the local hash index.
        /// </summary>
        /// <param name="playniteDatabaseGames">List of games from Playnite's database</param>
        /// <param name="bypassCooldown">When true, skip the client-side cooldown check (used when server requests force-full-sync)</param>
        public async Task<SyncLibraryResult> SyncLibraryFullAsync(
            IEnumerable<Game> playniteDatabaseGames, bool bypassCooldown = false) {
            try {
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;

                if (!bypassCooldown) {
                    var cooldownExpiry = GsDataManager.Data.SyncCooldownExpiresAt;
                    if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                        _logger.Info($"Library full sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                        return SyncLibraryResult.Cooldown;
                    }
                }

                _logger.Info("Starting full library sync (v4 chunked)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                if (!IsCurrentIdentity(installId, generation)) return SyncLibraryResult.Error;
                var integrationAccounts = ReadIntegrationAccountsSafe();
                var accountsHash = GsHashUtils.ComputeIntegrationAccountsHash(integrationAccounts);
                var accountsChanged = accountsHash != (GsDataManager.Data.LastIntegrationAccountsHash ?? "");

                if (libraryHash == GsDataManager.Data.LastLibraryHash && GsSyncHashIndex.HasLibraryBaseline && !accountsChanged) {
                    return SkipOrRepairIndex(
                        "Full library sync",
                        GsSyncHashIndex.LibraryEntryCount,
                        library.Count,
                        () => GsSyncHashIndex.ReplaceLibraryIndex(BuildLibraryFingerprints(library)));
                }

                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;

                var response = await UploadLibraryFullChunkedAsync(library, libraryHash, integrationAccounts);

                if (!IsCurrentIdentity(installId, generation)) return SyncLibraryResult.Error;
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
                    var libCount = library.Count;
                    return await CommitSyncBaselineAsync(
                        "Full library sync",
                        response.queueId, installId, generation,
                        () => GsSyncHashIndex.ReplaceLibraryIndex(BuildLibraryFingerprints(library)),
                        d => {
                            d.LastSyncAt = DateTime.UtcNow;
                            d.LastSyncGameCount = libCount;
                            d.LastLibraryHash = libraryHash;
                            d.LastIntegrationAccountsHash = accountsHash;
                            d.SyncCooldownExpiresAt = null;
                        },
                        queuedDetail: $"{libCount} games");
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
                    .Where(GsAllowedPlugins.IsAllowed)
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
        /// Computes library diff against snapshot and sends to v2/library/sync-diff.
        /// Falls back to full sync if the server requests it.
        /// </summary>
        public async Task<SyncLibraryResult> SyncLibraryDiffAsync(
            IEnumerable<Game> playniteDatabaseGames) {
            try {
                if (GsDataManager.IsOptedOut) return SyncLibraryResult.Skipped;
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;

                var cooldownExpiry = GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                if (cooldownExpiry.HasValue && DateTime.UtcNow < cooldownExpiry.Value) {
                    _logger.Info($"Library diff sync skipped: cooldown active until {cooldownExpiry.Value:O}");
                    return SyncLibraryResult.Cooldown;
                }

                _logger.Info("Starting diff library sync (v2)");
                var (library, libraryHash, totalCount, _) = await BuildLibraryDtosAsync(playniteDatabaseGames);

                if (!IsCurrentIdentity(installId, generation)) return SyncLibraryResult.Error;
                var integrationAccounts = ReadIntegrationAccountsSafe();
                var accountsHash = GsHashUtils.ComputeIntegrationAccountsHash(integrationAccounts);
                var accountsChanged = accountsHash != (GsDataManager.Data.LastIntegrationAccountsHash ?? "");

                if (libraryHash == GsDataManager.Data.LastLibraryHash && !accountsChanged) {
                    return SkipOrRepairIndex(
                        "Library diff sync",
                        GsSyncHashIndex.LibraryEntryCount,
                        library.Count,
                        () => GsSyncHashIndex.ReplaceLibraryIndex(BuildLibraryFingerprints(library)));
                }

                var fingerprints = GsSyncHashIndex.GetLibraryFingerprints();
                var (added, updated, removed, currentFingerprints) = await Task.Run(() =>
                    ComputeLibraryDiff(library, fingerprints));

                if (!IsCurrentIdentity(installId, generation)) return SyncLibraryResult.Error;
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

                if (!IsCurrentIdentity(installId, generation)) return SyncLibraryResult.Error;
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
                    var libCount = library.Count;
                    return await CommitSyncBaselineAsync(
                        "Library diff sync",
                        response.queueId, installId, generation,
                        () => GsSyncHashIndex.ApplyLibraryDiff(
                            added.Concat(updated).ToDictionary(
                                g => g.playnite_id,
                                g => currentFingerprints[g.playnite_id]),
                            removed),
                        d => {
                            d.LastSyncAt = DateTime.UtcNow;
                            d.LastSyncGameCount = libCount;
                            d.LastLibraryHash = libraryHash;
                            d.LastIntegrationAccountsHash = accountsHash;
                            d.LibraryDiffSyncCooldownExpiresAt = null;
                        });
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

        // SyncAchievementsFullAsync removed — will be re-added after SuccessStory/PlayniteAchievements v11 releases.

        // SyncAchievementsDiffAsync removed — will be re-added after SuccessStory/PlayniteAchievements v11 releases.

        /// <summary>
        /// Parses cooldown info from an AsyncQueuedResponse and persists it to the appropriate field.
        /// </summary>
        private static void HandleCooldownResponse(AsyncQueuedResponse response, bool isDiffSync = false) {
            DateTime? expiresAt = null;
            if (!string.IsNullOrEmpty(response.cooldownExpiresAt)
                && DateTime.TryParse(response.cooldownExpiresAt, CultureInfo.InvariantCulture,
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
