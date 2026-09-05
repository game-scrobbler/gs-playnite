using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Playnite.SDK;
using Sentry;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Api {
    public class GsApiClient : IGsApiClient {
        private static readonly ILogger _logger = LogManager.GetLogger();

        private static readonly string _apiBaseUrl = "https://api.gamescrobbler.com";
        private static readonly string _nextApiBaseUrl = "https://gamescrobbler.com";

        // Reuse a single HttpClient instance across all API client instances
        // This prevents socket exhaustion and improves performance
        private static readonly HttpClient _defaultHttpClient;

        static GsApiClient() {
            // Enforce TLS 1.2+ to avoid negotiating insecure protocol versions on .NET Framework 4.6.2.
            // Use |= rather than assignment: SecurityProtocol is process-wide and shared with every
            // other Playnite plugin, so overwriting it would silently strip out whatever protocols
            // another plugin or Playnite itself had already configured.
            // Try to also opt into TLS 1.3 (SecurityProtocolType.Tls13, value 12288) via its raw
            // numeric value since the .NET Framework 4.6.2 enum predates that member. The setter
            // validates against a known bitmask and throws NotSupportedException on older/unpatched
            // runtimes that don't recognize the bit — a throw here would permanently break this type
            // (TypeInitializationException on every later access), so fall back to Tls12-only.
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
            }
            catch (NotSupportedException) {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }

            try {
                _defaultHttpClient = new HttpClient(new SentryHttpMessageHandler()) {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }
            catch {
                // Fallback to plain HttpClient if Sentry SDK is unavailable (e.g. expired account)
                _defaultHttpClient = new HttpClient() {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }

            // The server reads this on every token-authenticated request to populate
            // playnite_users.plugin_version. Setting it as a default header covers registration,
            // all v3/v4 writes, notifications and the dashboard token in one place; without it,
            // version telemetry only exists for users who happen to open the sidebar.
            // Guarded because a throw in a static constructor is unrecoverable: it would surface as
            // TypeInitializationException on every later access to this type.
            try {
                _defaultHttpClient.DefaultRequestHeaders.Add(
                    "x-playnite-plugin-version", GsSentry.GetPluginVersion());
            }
            catch (Exception ex) {
                GsLogger.Warn($"Could not set plugin version header: {ex.Message}");
            }
        }

        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly GsCircuitBreaker _circuitBreaker;

        public GsApiClient() : this(_defaultHttpClient, true) { }

        /// <summary>
        /// Constructor that accepts a custom HttpClient for testing.
        /// Production code uses the parameterless constructor which provides the shared Sentry-traced client.
        /// </summary>
        internal GsApiClient(HttpClient httpClient, GsCircuitBreaker circuitBreaker = null)
            : this(httpClient, false, circuitBreaker) { }

        private GsApiClient(HttpClient httpClient, bool enableRecoveryFlush, GsCircuitBreaker circuitBreaker = null) {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };
            _circuitBreaker = circuitBreaker ?? new GsCircuitBreaker(
                failureThreshold: 3,
                timeout: TimeSpan.FromMinutes(2),
                retryDelay: TimeSpan.FromSeconds(10));
            if (enableRecoveryFlush) {
                _circuitBreaker.OnCircuitClosed += () => {
                    _ = FlushPendingScrobblesAsync().LogFaults("Unhandled exception in FlushPendingScrobblesAsync (circuit recovery)", asError: true);
                };
            }
        }

        #region Request Preconditions

        /// <summary>
        /// Legacy v2/v3 write routes resolve the install from either the x-playnite-token header
        /// or the body user_id, so a request is sendable when at least one of them is present.
        /// v4 routes are strictly token-authenticated, see <see cref="HasInstallToken"/>.
        /// </summary>
        private static bool HasIdentity(string userId) =>
            !string.IsNullOrEmpty(userId) || !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

        /// <summary>
        /// Rejects (and logs) a request that carries neither a user_id nor an install token.
        /// Returns true when the caller may proceed.
        /// </summary>
        private static bool RequireIdentity(string userId, string caller) {
            if (HasIdentity(userId)) {
                return true;
            }
            _logger.Error($"{caller} called with no user_id and no install token");
            return false;
        }

        /// <summary>
        /// Rejects (and logs) a null request DTO. <paramref name="argName"/> keeps each endpoint's
        /// own wording, e.g. "startData" rather than the generic "request".
        /// Returns true when the caller may proceed.
        /// </summary>
        private static bool RequireRequest<TReq>(TReq req, string caller, string argName = "request") where TReq : class {
            if (req != null) {
                return true;
            }
            _logger.Error($"{caller} called with null {argName}");
            return false;
        }

        #endregion

        #region Game Session Management

        public Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData) =>
            StartGameSession(startData, null);

        private async Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData, Action onAttempt) {
            // Validate input before making API call
            if (!RequireRequest(startData, nameof(StartGameSession), "startData") ||
                !RequireIdentity(startData.user_id, nameof(StartGameSession))) {
                return null;
            }

            if (string.IsNullOrEmpty(startData.game_name)) {
                _logger.Warn("StartGameSession called with null or empty game_name");
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/scrobble/start";

            var envelope = await _circuitBreaker.ExecuteAsync(async () => {
                onAttempt?.Invoke();
                return await PostJsonAsync<ApiResponse<ScrobbleStartData>>(url, startData);
            }, maxRetries: 2, isFailure: r => r == null);

            switch (envelope?.Outcome) {
                case ApiOutcome.Success when envelope.data?.session_id != null:
                    _logger.Info($"Scrobble start complete with session ID: {envelope.data.session_id}");
                    return new ScrobbleStartRes { session_id = envelope.data.session_id };

                case ApiOutcome.Success:
                case ApiOutcome.Queued:
                    _logger.Info("Scrobble start accepted without a session ID");
                    return new ScrobbleStartRes();

                case ApiOutcome.Fail when envelope.code == "UNSUPPORTED_PLUGIN":
                    _logger.Info($"Scrobble start skipped: {envelope.message}");
                    return null;

                case ApiOutcome.Fail:
                    _logger.Warn($"Scrobble start rejected by server: [{envelope.code}] {envelope.message}");
                    CaptureSentryMessage($"Scrobble start fail: {envelope.code}", SentryLevel.Warning, startData.game_name, startData.user_id);
                    return null;

                default:
                    GsLogger.Error("Failed to start scrobble session");
                    CaptureSentryMessage("Failed to start scrobble session", SentryLevel.Warning, startData.game_name, startData.user_id);
                    return null;
            }
        }

        public Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) =>
            FinishGameSession(endData, null);

        private async Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData, Action onAttempt) {
            // Validate input before making API call
            if (!RequireRequest(endData, nameof(FinishGameSession), "endData")) {
                return null;
            }

            // Clone before mutating so the caller's persisted PendingScrobble is not modified
            // before the send is confirmed (peek-then-remove flush strategy).
            var sendData = new ScrobbleFinishReq {
                user_id = endData.user_id,
                game_name = endData.game_name,
                game_id = endData.game_id,
                plugin_id = endData.plugin_id,
                external_game_id = endData.external_game_id,
                source_name = endData.source_name,
                metadata = endData.metadata,
                finished_at = endData.finished_at,
                session_id = endData.session_id,
            };

            if (string.IsNullOrEmpty(sendData.session_id) || !Guid.TryParse(sendData.session_id, out _)) {
                if (!string.IsNullOrEmpty(sendData.session_id)) {
                    // Non-null but non-UUID: likely a stale "queued" placeholder from an older
                    // plugin version in the pending-scrobble queue. Cleared so the backend falls
                    // back to name-based matching (Strategy 2) instead of rejecting the request.
                    GsLogger.Warn($"Clearing non-UUID session_id '{sendData.session_id}' before finish — backend will use name-based matching (game: {sendData.game_name ?? "unknown"})");
                }
                else {
                    _logger.Info($"Finishing session without session_id (game: {sendData.game_name ?? "unknown"}), backend will use name-based matching");
                }
                sendData.session_id = null;
            }

            if (sendData.session_id == null && string.IsNullOrEmpty(sendData.game_name)) {
                GsLogger.Error("FinishGameSession aborted: no session_id and no game_name — Strategy 2 would match an arbitrary open session");
                return null;
            }

            if (!RequireIdentity(sendData.user_id, nameof(FinishGameSession))) {
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/scrobble/finish";

            var envelope = await _circuitBreaker.ExecuteAsync(async () => {
                onAttempt?.Invoke();
                return await PostJsonAsync<ApiResponse<ScrobbleFinishData>>(url, sendData, true);
            }, maxRetries: 2, isFailure: r => r == null);

            switch (envelope?.Outcome) {
                case ApiOutcome.Success:
                    _logger.Info($"Scrobble finish complete ({envelope.data?.duration_seconds}s)");
                    return new ScrobbleFinishRes();

                case ApiOutcome.Fail when envelope.code == "UNSUPPORTED_PLUGIN":
                    _logger.Info($"Scrobble finish skipped: {envelope.message}");
                    return new ScrobbleFinishRes();

                case ApiOutcome.Fail:
                    _logger.Warn($"Scrobble finish rejected by server: [{envelope.code}] {envelope.message}");
                    return null;

                default:
                    GsLogger.Error("Failed to finish scrobble session");
                    return null;
            }
        }

        /// <summary>
        /// Maximum number of flush attempts before a pending scrobble is permanently dropped.
        /// Prevents infinite re-queue loops when the server consistently rejects a request.
        /// </summary>
        private const int MaxFlushAttempts = 5;

        /// <summary>
        /// Guards against concurrent flush invocations (circuit recovery + periodic timer + startup).
        /// 0 = idle, 1 = in flight.
        /// </summary>
        private int _flushInFlight;

        /// <summary>
        /// Flushes all pending scrobbles that were queued when the API was unavailable.
        /// Uses a peek-then-remove-on-success strategy so a mid-flush crash never loses items:
        /// each scrobble stays on disk until its send is confirmed, then is removed atomically.
        /// Called on circuit breaker recovery, on application startup, and by the periodic timer.
        /// </summary>
        public async Task FlushPendingScrobblesAsync() {
            if (GsDataManager.IsOptedOut) return;

            // Prevent concurrent flushes from sending duplicates when two callers overlap
            // (e.g. circuit-recovery fires while the startup flush or periodic timer is running).
            if (System.Threading.Interlocked.CompareExchange(ref _flushInFlight, 1, 0) != 0) {
                _logger.Info("FlushPendingScrobblesAsync already in flight — skipping");
                return;
            }

            try {
                // An open circuit fast-fails every send without touching the network, but the loop
                // below cannot tell that from a real rejection and would still increment
                // FlushAttempts. Five such passes (~25 minutes offline, or five launches) would
                // drop the entire queue for data that would have synced fine once connectivity
                // returned. Skip only while the cooldown is active; after it expires this flush
                // must itself be able to probe the server, even when no other API calls occur.
                if (_circuitBreaker.IsBlocking) {
                    _logger.Info("Skipping pending-scrobble flush: circuit breaker is open");
                    return;
                }

                // Peek without clearing: items remain persisted until individually confirmed.
                var pending = GsDataManager.PeekPendingScrobbles();
                if (pending == null || pending.Count == 0) {
                    return;
                }

                _logger.Info($"Flushing {pending.Count} pending scrobble(s)");

                foreach (var item in pending) {
                    // Re-check opt-out before each send (user may have opted out mid-flush)
                    if (GsDataManager.IsOptedOut) break;

                    // The breaker can trip partway through a pass: the first real send fails and
                    // opens it, then every remaining item fast-fails with no network call. Stop
                    // rather than burning an attempt per item on a circuit we already know is open.
                    if (_circuitBreaker.IsBlocking) {
                        _logger.Info("Stopping pending-scrobble flush: circuit breaker opened mid-pass");
                        break;
                    }

                    bool success = false;
                    bool attempted = false;
                    string startedSessionId = null;
                    try {
                        if (item.Type == "start" && item.StartData != null) {
                            var res = await StartGameSession(item.StartData, () => attempted = true);
                            success = res != null;
                            startedSessionId = res?.session_id;
                        }
                        else if (item.Type == "finish" && item.FinishData != null) {
                            var res = await FinishGameSession(item.FinishData, () => attempted = true);
                            success = res != null;
                        }
                        else {
                            _logger.Warn($"Dropping invalid pending scrobble (type={item.Type})");
                            GsDataManager.RemovePendingScrobble(item);
                            continue;
                        }
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, $"Exception flushing pending scrobble (type={item.Type}, queued={item.QueuedAt:O})");
                        GsSentry.CaptureException(ex, $"FlushPendingScrobblesAsync: unexpected exception (type={item.Type})");
                    }

                    if (success) {
                        // Persist replay pairing before removing a start, so a crash cannot lose
                        // its returned session id and let the finish target another open session.
                        bool persisted = item.Type == "start"
                            ? GsDataManager.CompletePendingStart(item, startedSessionId)
                            : GsDataManager.CompletePendingScrobble(item);
                        if (!persisted) {
                            break;
                        }
                    }
                    else {
                        // Another request may open the circuit after our entry check. A fast-fail
                        // in that window is not a failed send and must not consume the retry budget.
                        if (!attempted && _circuitBreaker.IsBlocking) {
                            break;
                        }
                        // Mutate-then-save under GsDataManager's lock, rather than incrementing
                        // the field directly and calling Save() separately — keeps this in line
                        // with every other queue mutation (see RemovePendingScrobble) instead of
                        // racing a concurrent RotateInstallId()/PerformOptOut() that clears the queue.
                        GsDataManager.IncrementPendingScrobbleFlushAttempts(item);
                        if (item.FlushAttempts >= MaxFlushAttempts) {
                            _logger.Warn($"Dropping pending scrobble after {item.FlushAttempts} failed flush attempts (type={item.Type}, queued={item.QueuedAt:O})");
                            GsDataManager.DropPendingScrobble(item);
                            GsSentry.AddBreadcrumb(
                                message: $"Dropped pending scrobble after {MaxFlushAttempts} attempts",
                                category: "flush",
                                data: new System.Collections.Generic.Dictionary<string, string> {
                                    { "type", item.Type },
                                    { "queued_at", item.QueuedAt.ToString("O") }
                                });
                        }
                        // else: item stays in the queue with its incremented FlushAttempts counter,
                        // already persisted by IncrementPendingScrobbleFlushAttempts above.
                        // Keep FIFO order: a finish must never overtake its failed start, even if
                        // that start failed permanently without opening the circuit. Terminally
                        // dropped starts also drop their dependent finish in the same persisted edit.
                        break;
                    }
                }
            }
            finally {
                System.Threading.Interlocked.Exchange(ref _flushInFlight, 0);
            }
        }

        #endregion

        #region Library Synchronization

        public async Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req) {
            if (!RequireRequest(req, nameof(SyncLibraryFull)) ||
                !RequireIdentity(req.user_id, nameof(SyncLibraryFull))) {
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/library/sync-full";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req) {
            if (!RequireRequest(req, nameof(SyncLibraryDiff)) ||
                !RequireIdentity(req.user_id, nameof(SyncLibraryDiff))) {
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/library/sync-diff";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<V4SyncBeginRes> SyncLibraryFullBegin(LibraryV4FullSyncBeginReq req) {
            if (!HasInstallToken()) {
                _logger.Error("SyncLibraryFullBegin called with no install token");
                return null;
            }
            return await PostV4Async<V4SyncBeginRes>(
                $"{_apiBaseUrl}/api/playnite/v4/library/sync-full/begin", req, nameof(SyncLibraryFullBegin));
        }

        public async Task<V4SyncChunkRes> SyncLibraryFullChunk(LibraryV4ChunkReq req) {
            return await PostV4Async<V4SyncChunkRes>(
                $"{_apiBaseUrl}/api/playnite/v4/library/sync-full/chunk", req, nameof(SyncLibraryFullChunk));
        }

        public async Task<AsyncQueuedResponse> SyncLibraryFullCommit(LibraryV4CommitReq req) {
            return await PostV4Async<AsyncQueuedResponse>(
                $"{_apiBaseUrl}/api/playnite/v4/library/sync-full/commit", req, nameof(SyncLibraryFullCommit));
        }

        public async Task SyncLibraryFullAbort(string syncId) {
            await PostV4AbortAsync(
                $"{_apiBaseUrl}/api/playnite/v4/library/sync-full/abort", syncId, nameof(SyncLibraryFullAbort));
        }

        public async Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req) {
            if (!RequireRequest(req, nameof(SyncAchievementsFull)) ||
                !RequireIdentity(req.user_id, nameof(SyncAchievementsFull))) {
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/achievements/sync-full";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req) {
            if (!RequireRequest(req, nameof(SyncAchievementsDiff)) ||
                !RequireIdentity(req.user_id, nameof(SyncAchievementsDiff))) {
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/achievements/sync-diff";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<V4SyncBeginRes> SyncAchievementsFullBegin(AchievementsV4FullSyncBeginReq req) {
            if (!HasInstallToken()) {
                _logger.Error("SyncAchievementsFullBegin called with no install token");
                return null;
            }
            return await PostV4Async<V4SyncBeginRes>(
                $"{_apiBaseUrl}/api/playnite/v4/achievements/sync-full/begin", req, nameof(SyncAchievementsFullBegin));
        }

        public async Task<V4SyncChunkRes> SyncAchievementsFullChunk(AchievementsV4ChunkReq req) {
            return await PostV4Async<V4SyncChunkRes>(
                $"{_apiBaseUrl}/api/playnite/v4/achievements/sync-full/chunk", req, nameof(SyncAchievementsFullChunk));
        }

        public async Task<AsyncQueuedResponse> SyncAchievementsFullCommit(AchievementsV4CommitReq req) {
            return await PostV4Async<AsyncQueuedResponse>(
                $"{_apiBaseUrl}/api/playnite/v4/achievements/sync-full/commit", req, nameof(SyncAchievementsFullCommit));
        }

        public async Task SyncAchievementsFullAbort(string syncId) {
            await PostV4AbortAsync(
                $"{_apiBaseUrl}/api/playnite/v4/achievements/sync-full/abort", syncId, nameof(SyncAchievementsFullAbort));
        }

        /// <summary>
        /// v4 is token-authenticated by contract; unlike legacy routes, the server never trusts
        /// a body identity. Do not start a session that its chunk/commit calls cannot authenticate.
        /// </summary>
        private static bool HasInstallToken() =>
            !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

        /// <summary>
        /// Shared v4 begin/chunk/commit POST. Business rejections (success=false) are NOT circuit
        /// failures — only transport failures (null) are, so a server rejecting a stale sync session
        /// cannot trip the shared breaker and starve unrelated scrobble/diff calls. The service layer
        /// inspects success/status itself.
        /// </summary>
        private async Task<TRes> PostV4Async<TRes>(string url, object req, string logName) where TRes : class {
            if (!RequireRequest(req, logName)) {
                return null;
            }
            // 0 until a response arrives, so a transport failure stays retryable.
            var lastStatus = 0;
            return await _circuitBreaker.ExecuteAsync(
                async () => {
                    lastStatus = 0;
                    return await PostJsonAsync<TRes>(url, req, true, status => lastStatus = status,
                        parseErrorBody: true);
                },
                maxRetries: 1,
                isFailure: r => r == null,
                isPermanent: () => IsPermanentRejection(lastStatus));
        }

        /// <summary>
        /// A status that will come back identically however many times we ask, so
        /// retrying only doubles the load and the noise. 408 and 429 are excluded
        /// because both explicitly invite a later retry.
        /// </summary>
        private static bool IsPermanentRejection(int status) =>
            status >= 400 && status < 500
            && status != (int)HttpStatusCode.RequestTimeout
            && status != 429;

        /// <summary>Best-effort v4 session abort — swallows failures so cleanup never surfaces an error.</summary>
        private async Task PostV4AbortAsync(string url, string syncId, string logName) {
            if (string.IsNullOrEmpty(syncId)) {
                return;
            }
            try {
                await PostJsonAsync<object>(url, new V4SyncAbortReq { sync_id = syncId }, true);
            }
            catch (Exception ex) {
                _logger.Warn($"{logName} failed: {ex.Message}");
            }
        }

        #endregion

        #region Install Token Registration

        /// <summary>
        /// Registers the install with the server and retrieves a per-install auth token.
        /// Call once on first boot (or when InstallToken is missing from persistent storage).
        /// Returns the response body so the caller can inspect error_code.
        /// HTTP 409 (error_code PLAYNITE_TOKEN_ALREADY_REGISTERED) means the server already has
        /// a token for this install ID — the local copy was lost. Recovery rotates to a fresh
        /// InstallID (see GsDataManager.RotateInstallId) and re-registers under the new identity.
        /// </summary>
        public async Task<RegisterInstallTokenRes> RegisterInstallToken(string installId) {
            if (string.IsNullOrEmpty(installId)) {
                _logger.Error("RegisterInstallToken called with null or empty installId");
                return null;
            }

            // No onNonSuccess mapping: the body is parsed on every status code so the caller can
            // read error_code (HTTP 409 carries PLAYNITE_TOKEN_ALREADY_REGISTERED).
            // attachToken is false because this call mints the token, and during lost-token
            // recovery a stale local token must not travel with the new install id.
            return await SendAndParseAsync<RegisterInstallTokenRes>(
                HttpMethod.Post,
                $"{_apiBaseUrl}/api/playnite/v2/register",
                new RegisterInstallTokenReq { playnite_user_id = installId },
                attachToken: false,
                logName: nameof(RegisterInstallToken),
                // An unreadable body still means the server answered: returning an empty failure
                // rather than null tells EnsureInstallTokenAsync to stop retrying. Only a
                // transport exception (null from the helper) keeps the retry loop going.
                onParsed: (response, res) => res ?? new RegisterInstallTokenRes { success = false });
        }

        /// <summary>
        /// Requests a short-lived (10-minute) dashboard read token from the server.
        /// The plugin embeds this token in the WebView2 URL as ?access_token=...
        /// instead of the raw install UUID, keeping the UUID out of browser history.
        /// Sends a dashboard context object in the POST body so the server can store
        /// it alongside the token and return it (tamper-proof) when the frontend resolves
        /// the token — eliminating the need for client-side URL query params.
        /// Requires a valid InstallToken (x-playnite-token header).
        /// Returns the raw token string on success, or null on failure.
        /// </summary>
        public async Task<string> GetDashboardToken() {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (string.IsNullOrEmpty(installToken)) {
                _logger.Warn("GetDashboardToken: no install token available, cannot request dashboard token");
                return null;
            }

            var data = GsDataManager.DataOrNull;
            var context = new {
                plugin_version = GsSentry.GetPluginVersion(),
                scrobbling_disabled = data?.Flags?.Contains("no-scrobble") ?? false,
                sentry_disabled = data?.Flags?.Contains("no-sentry") ?? false,
                posthog_disabled = data?.Flags?.Contains("no-posthog") ?? false,
                new_dashboard = data?.NewDashboardExperience ?? false,
                sync_achievements = data?.SyncAchievements ?? false,
            };

            var tokenRes = await SendAndParseAsync<DashboardTokenRes>(
                HttpMethod.Post,
                $"{_apiBaseUrl}/api/playnite/v2/dashboard-token",
                new { context },
                attachToken: true,
                logName: nameof(GetDashboardToken),
                onNonSuccess: (response, responseBody) => {
                    _logger.Warn($"GetDashboardToken returned {(int)response.StatusCode}");
                    return null;
                });

            return tokenRes?.token;
        }

        #endregion

        #region Notifications

        /// <summary>
        /// Fetches active notifications from the server for this install.
        /// Requires a valid InstallToken (x-playnite-token header).
        /// Returns null on failure or when no token is available.
        /// Intentionally bypasses the shared circuit breaker so that notification
        /// failures cannot affect the failure budget of core sync/scrobble paths.
        /// </summary>
        public async Task<PlayniteNotificationsRes> GetNotifications() {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (string.IsNullOrEmpty(installToken)) {
                return null;
            }

            // Deliberately not wrapped in _circuitBreaker (see the summary above), and
            // captureExceptions is false so a flaky notifications endpoint stays out of Sentry.
            return await SendAndParseAsync<PlayniteNotificationsRes>(
                HttpMethod.Get,
                $"{_apiBaseUrl}/api/playnite/v2/notifications",
                body: null,
                attachToken: true,
                logName: nameof(GetNotifications),
                onNonSuccess: (response, responseBody) => {
                    _logger.Warn($"GetNotifications returned {(int)response.StatusCode}");
                    return null;
                },
                captureExceptions: false);
        }

        /// <summary>
        /// Polls the terminal status of a previously queued sync job. Unauthenticated on the
        /// server (keyed by the opaque queueId returned at enqueue time) and intentionally
        /// bypasses the shared circuit breaker — a poll loop can call this several times per
        /// sync attempt, and transient failures here must not burn the failure budget shared
        /// with core sync/scrobble calls.
        /// </summary>
        public async Task<QueueStatusRes> GetQueueStatus(string queueId) {
            if (string.IsNullOrEmpty(queueId)) {
                return null;
            }
            return await GetJsonAsync<QueueStatusRes>($"{_apiBaseUrl}/api/playnite/queue/status/{queueId}");
        }

        #endregion

        #region Allowed Plugins

        public async Task<AllowedPluginsRes> GetAllowedPlugins() {
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await GetJsonAsync<AllowedPluginsRes>($"{_apiBaseUrl}/api/playnite/v2/allowed-plugins");
            }, maxRetries: 1, isFailure: r => r == null);
        }

        #endregion

        #region Token Verification

        public async Task<TokenVerificationRes> VerifyToken(string token, string playniteId) {
            // Validate input before making API call
            if (string.IsNullOrEmpty(token)) {
                _logger.Error("VerifyToken called with null or empty token");
                return null;
            }

            if (string.IsNullOrEmpty(playniteId)) {
                _logger.Error("VerifyToken called with null or empty playniteId");
                return null;
            }

            var payload = new TokenVerificationReq {
                token = token,
                playniteId = playniteId,
            };

            // Targets _nextApiBaseUrl (the web app), not the plugin API, and stays anonymous:
            // the token being verified is the credential here.
            // No onNonSuccess mapping, so the body is parsed even on non-2xx status codes and the
            // caller receives the actual server error message (e.g. "Token expired") instead of a
            // generic "network error". An unusable body yields null, as does a transport error.
            return await SendAndParseAsync<TokenVerificationRes>(
                HttpMethod.Post,
                $"{_nextApiBaseUrl}/api/auth/playnite/verify",
                payload,
                attachToken: false,
                logName: nameof(VerifyToken),
                onParsed: (response, res) => {
                    if (res == null) {
                        return null;
                    }

                    // On non-2xx, mark as failed and surface the server error message
                    if (!response.IsSuccessStatusCode) {
                        res.success = false;
                        _logger.Warn($"VerifyToken returned {(int)response.StatusCode}: {res.error ?? res.message ?? "unknown"}");
                    }

                    // Promote the error field to message so callers always read result.message
                    if (!res.success && string.IsNullOrEmpty(res.message) && !string.IsNullOrEmpty(res.error)) {
                        res.message = res.error;
                    }

                    return res;
                });
        }

        #endregion

        #region Account Unlinking

        public async Task<UnlinkRes> UnlinkAccount() {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (string.IsNullOrEmpty(installToken)) {
                _logger.Error("UnlinkAccount called with no install token");
                return null;
            }

            // The install is identified by the token header alone, so the body is an empty JSON
            // object. No onNonSuccess mapping: the body is parsed on every status so the server's
            // error text survives. An unusable body yields null, as does a transport error.
            return await SendAndParseAsync<UnlinkRes>(
                HttpMethod.Post,
                $"{_apiBaseUrl}/api/playnite/v2/unlink",
                new { },
                attachToken: true,
                logName: nameof(UnlinkAccount),
                onParsed: (response, res) => {
                    if (res == null) {
                        return null;
                    }

                    if (!response.IsSuccessStatusCode) {
                        res.success = false;
                        _logger.Warn($"UnlinkAccount returned {(int)response.StatusCode}: {res.error ?? "unknown"}");
                    }

                    return res;
                });
        }

        #endregion

        #region Data Deletion

        public async Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req) {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (req == null || string.IsNullOrEmpty(installToken)) {
                _logger.Error("RequestDeleteMyData called with no install token");
                return null;
            }

            // The request DTO carries no identity: the server resolves the install from the
            // x-playnite-token header alone. Only a transport error returns null.
            return await SendAndParseAsync<DeleteDataRes>(
                HttpMethod.Post,
                $"{_apiBaseUrl}/api/playnite/v2/delete-data",
                req,
                attachToken: true,
                logName: nameof(RequestDeleteMyData),
                onNonSuccess: (response, responseBody) => {
                    switch ((int)response.StatusCode) {
                        case 429:
                            _logger.Warn("RequestDeleteMyData rate limited by server");
                            return new DeleteDataRes { success = false, rateLimited = true };

                        // 403 = install already opted out server-side (data already deleted, or
                        // removed via a linked web-account deletion). Retrying can never succeed;
                        // signal the caller to sync local opt-out state instead of looping on a
                        // generic "failed" message.
                        case 403:
                            _logger.Warn("RequestDeleteMyData: install already opted out (403)");
                            return new DeleteDataRes { success = false, alreadyOptedOut = true };

                        // 401 = stored token does not resolve to an install. Retrying with the
                        // same token is futile, so surface a distinct "reconnect" signal.
                        case 401:
                            _logger.Warn("RequestDeleteMyData: install token rejected (401)");
                            return new DeleteDataRes { success = false, authFailed = true };

                        default:
                            _logger.Warn($"RequestDeleteMyData returned {(int)response.StatusCode}");
                            return new DeleteDataRes { success = false };
                    }
                },
                onParsed: (response, res) => res ?? new DeleteDataRes { success = false });
        }

        public async Task<OptInRes> RequestOptIn(OptInReq req) {
            var installId = GsDataManager.DataOrNull?.InstallID;
            if (req == null || string.IsNullOrEmpty(installId)) {
                _logger.Error("RequestOptIn called with no install ID");
                return null;
            }

            req.user_id = installId;

            // Stays unauthenticated: opt-in runs while the install is opted out, when the local
            // token has been cleared, so the server keys off the body user_id instead.
            return await SendAndParseAsync<OptInRes>(
                HttpMethod.Post,
                $"{_apiBaseUrl}/api/playnite/v2/opt-in",
                req,
                attachToken: false,
                logName: nameof(RequestOptIn),
                onNonSuccess: (response, responseBody) => {
                    if ((int)response.StatusCode == 429) {
                        _logger.Warn("RequestOptIn rate limited by server");
                        return new OptInRes { success = false, rateLimited = true };
                    }

                    _logger.Warn($"RequestOptIn returned {(int)response.StatusCode}");
                    return new OptInRes { success = false };
                },
                onParsed: (response, res) => res ?? new OptInRes { success = false });
        }

        #endregion

        #region HTTP Helper Methods

        /// <summary>
        /// Helper method to capture HTTP-related exceptions with consistent context.
        /// </summary>
        private static void CaptureHttpException(Exception exception, string url, string requestBody, HttpResponseMessage response = null, string responseBody = null) {
            string contextMessage = $"HTTP request failed for {url}. Status: {response?.StatusCode}";
            GsSentry.CaptureException(exception, contextMessage);
        }

        /// <summary>
        /// Captures a scrobble failure. The message is the Sentry issue fingerprint, so game name,
        /// user id and session id are attached as a breadcrumb rather than interpolated into it:
        /// putting a game title in the title split one failure mode into a separate issue per game
        /// (making the real rate invisible) and published a per-title event stream nobody asked for.
        /// The context is still available on the event, just not as its identity.
        /// </summary>
        private static void CaptureSentryMessage(string message, SentryLevel level, string gameName = null, string userId = null, string sessionId = null) {
            var data = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(gameName)) {
                data["game"] = gameName;
            }
            if (!string.IsNullOrEmpty(userId)) {
                data["user_id"] = userId;
            }
            if (!string.IsNullOrEmpty(sessionId)) {
                data["session_id"] = sessionId;
            }
            if (data.Count > 0) {
                GsSentry.AddBreadcrumb(message: "scrobble failure context", category: "scrobble", data: data);
            }
            GsSentry.CaptureMessage(message, level);
        }

        private async Task<TResponse> GetJsonAsync<TResponse>(string url) where TResponse : class {
            try {
                var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                GsLogger.ShowHTTPDebugBox(
                    requestData: $"URL: {url}\nMethod: GET",
                    responseData: $"Status: {response.StatusCode}\nBody: {responseBody}");

                if (!response.IsSuccessStatusCode) {
                    _logger.Warn($"GET {url} returned {(int)response.StatusCode} ({response.StatusCode})");
                    return null;
                }

                if (string.IsNullOrWhiteSpace(responseBody)) {
                    _logger.Warn($"Received empty response body from GET {url}");
                    return null;
                }

                var contentType = response?.Content?.Headers?.ContentType?.MediaType;
                if (contentType != null && contentType.Contains("html")) {
                    _logger.Warn($"GET {url} returned HTML content-type instead of JSON — likely a proxy error page");
                    return null;
                }

                try {
                    return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
                }
                catch (JsonException jsonEx) {
                    _logger.Error(jsonEx, $"Failed to deserialize JSON response from GET {url}. Response body starts with: {(responseBody.Length > 100 ? responseBody.Substring(0, 100) : responseBody)}");
                    return null;
                }
            }
            catch (Exception ex) {
                GsLogger.ShowHTTPDebugBox(
                    requestData: $"URL: {url}\nMethod: GET",
                    responseData: $"Error: {ex.Message}\nStack Trace: {ex.StackTrace}",
                    isError: true);

                CaptureHttpException(ex, url, null);
                return null;
            }
        }

        /// <summary>
        /// Minimum JSON payload size (in bytes) before gzip compression is applied.
        /// Payloads below this threshold are sent uncompressed to avoid overhead.
        /// </summary>
        private const int GzipThresholdBytes = 4096;

        /// <summary>
        /// Builds the request body for a JSON payload, gzipping it once it reaches
        /// <see cref="GzipThresholdBytes"/>. The caller owns the returned content.
        /// </summary>
        private static HttpContent CreateJsonContent(string jsonData) {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonData);
            if (jsonBytes.Length < GzipThresholdBytes) {
                return new StringContent(jsonData, Encoding.UTF8, "application/json");
            }

            var compressedStream = new MemoryStream();
            using (var gzip = new GZipStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true)) {
                gzip.Write(jsonBytes, 0, jsonBytes.Length);
            }
            compressedStream.Position = 0;
            var content = new StreamContent(compressedStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            content.Headers.ContentEncoding.Add("gzip");
            return content;
        }

        /// <summary>
        /// How much of an error response body reaches the local log. Enough to
        /// carry the server's reason phrase without pasting a whole payload into
        /// a log the user may share for support.
        /// </summary>
        private const int MaxLoggedBodyChars = 512;

        /// <param name="parseErrorBody">
        /// When true, a permanent 4xx whose body deserializes into <typeparamref name="TResponse"/>
        /// is returned instead of null. Transient HTTP errors always stay retryable failures.
        /// Some rejections are business outcomes rather than failures:
        /// the v4 commit answers a snapshot-hash mismatch with HTTP 400 and
        /// {status:"force-full-sync", reason:"hash_mismatch"}. Collapsing that to null makes it
        /// indistinguishable from a dropped connection, leaves the caller's recovery branch
        /// unreachable, and lets a deterministic rejection count against the shared circuit breaker.
        /// Opt-in so endpoints whose callers only test for null keep their current behaviour.
        /// </param>
        private async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false,
            Action<int> onStatus = null, bool parseErrorBody = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);

            using (HttpContent content = CreateJsonContent(jsonData)) {
                HttpResponseMessage response = null;
                string responseBody = null;

                try {
                    // Attach the per-install auth token when available. The server resolves the
                    // install identity from the token, so the route handler does not need to trust
                    // the user_id field in the request body.
                    var installToken = GsDataManager.DataOrNull?.InstallToken;

                    HttpRequestMessage requestMessage = null;
                    if (!string.IsNullOrEmpty(installToken)) {
                        requestMessage = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                        requestMessage.Headers.Add("x-playnite-token", installToken);
                        response = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);
                    }
                    else {
                        response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
                    }
                    // Report the status before reading the body: if the body read
                    // throws, the server still answered, and a 4xx must stay
                    // classified as a permanent rejection rather than falling back
                    // to the retryable transport-failure path.
                    onStatus?.Invoke((int)response.StatusCode);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    GsLogger.ShowHTTPDebugBox(
                        requestData: $"URL: {url}\nPayload: {jsonData}",
                        responseData: $"Status: {response.StatusCode}\nBody: {responseBody}");

                    if (!response.IsSuccessStatusCode) {
                        // Always write the status to the local log. This used to be
                        // the else-branch only: with ensureSuccess the status went to
                        // Sentry and nowhere else, so a user reading their own log saw
                        // a failure with no status attached and could not report what
                        // had actually come back. The more serious path was the quieter
                        // one.
                        var body = responseBody == null
                            ? "(no body)"
                            : responseBody.Length > MaxLoggedBodyChars
                                ? responseBody.Substring(0, MaxLoggedBodyChars) + "..."
                                : responseBody;
                        _logger.Warn(
                            $"POST {url} returned {(int)response.StatusCode} ({response.StatusCode}): {body}");

                        if (ensureSuccess) {
                            var httpEx = new HttpRequestException(
                                $"Request failed with status {(int)response.StatusCode} ({response.StatusCode}) for URL {url}");

                            CaptureHttpException(httpEx, url, jsonData, response, responseBody);
                        }

                        // Keep recognized permanent business rejections (e.g. force-full-sync)
                        // inspectable, but never turn JSON from a 408/429/5xx into circuit success.
                        if (parseErrorBody && IsPermanentRejection((int)response.StatusCode)
                            && !string.IsNullOrWhiteSpace(responseBody)) {
                            var errorContentType = response?.Content?.Headers?.ContentType?.MediaType;
                            if (errorContentType == null || !errorContentType.Contains("html")) {
                                try {
                                    var errorResponse =
                                        JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
                                    if (errorResponse != null) {
                                        return errorResponse;
                                    }
                                }
                                catch (JsonException) {
                                    // Not a shape we understand — fall through to the null result so the
                                    // caller still treats it as a failure rather than a parsed outcome.
                                }
                            }
                        }
                        return null;
                    }

                    // Validate response body before deserialization
                    if (string.IsNullOrWhiteSpace(responseBody)) {
                        _logger.Warn($"Received empty response body from {url}");
                        return null;
                    }

                    // Detect HTML error pages returned by reverse proxies (e.g. Cloudflare, nginx)
                    // that arrive with a 200 status code but are not JSON.
                    var contentType = response?.Content?.Headers?.ContentType?.MediaType;
                    if (contentType != null && contentType.Contains("html")) {
                        _logger.Warn($"POST {url} returned HTML content-type instead of JSON — likely a proxy error page");
                        return null;
                    }

                    try {
                        var deserializedResponse = JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
                        if (deserializedResponse == null) {
                            _logger.Warn($"Deserialization returned null for {url}. Response: {responseBody}");
                        }
                        return deserializedResponse;
                    }
                    catch (JsonException jsonEx) {
                        _logger.Error(jsonEx, $"Failed to deserialize JSON response from {url}. Response body starts with: {(responseBody.Length > 100 ? responseBody.Substring(0, 100) : responseBody)}");
                        return null;
                    }
                }
                catch (Exception ex) {
                    GsLogger.ShowHTTPDebugBox(
                        requestData: $"URL: {url}\nPayload: {jsonData}",
                        responseData: $"Error: {ex.Message}\nStack Trace: {ex.StackTrace}",
                        isError: true);

                    CaptureHttpException(ex, url, jsonData, response, responseBody);
                    return null;
                }
            }
        }

        /// <summary>
        /// Shared send-and-parse path for the one-off endpoints whose status-code mapping is too
        /// specific for <see cref="PostJsonAsync{TResponse}"/>. Centralizes serialization (with the
        /// same gzip rule), the optional x-playnite-token header, the HTTP debug box, HTML
        /// proxy-error detection, deserialization, and the transport catch, so those endpoints no
        /// longer hand-roll a second HTTP path. Returns null on transport failure.
        /// </summary>
        /// <param name="body">Request payload. Null sends no content (e.g. a GET).</param>
        /// <param name="attachToken">
        /// Attaches x-playnite-token when a token is available. Endpoints that must stay anonymous
        /// (registration, token verification, opt-in) pass false so a stale local token is never
        /// sent to a route that would reject it.
        /// </param>
        /// <param name="logName">Endpoint name used in every log and Sentry message.</param>
        /// <param name="onNonSuccess">
        /// Maps a non-2xx response to a result, and its return value is final. When null, the body
        /// is parsed regardless of status so the caller can read the server's error payload.
        /// </param>
        /// <param name="onParsed">
        /// Post-processes the deserialized value together with the response, e.g. to flip success
        /// on a non-2xx status. The value is null when the body was empty, HTML, or unparseable,
        /// which lets a caller substitute an empty failure object instead of null.
        /// </param>
        /// <param name="captureExceptions">
        /// False for best-effort endpoints: failures log at Warn and are not sent to Sentry.
        /// </param>
        private async Task<TRes> SendAndParseAsync<TRes>(
            HttpMethod method,
            string url,
            object body,
            bool attachToken,
            string logName,
            Func<HttpResponseMessage, string, TRes> onNonSuccess = null,
            Func<HttpResponseMessage, TRes, TRes> onParsed = null,
            bool captureExceptions = true) where TRes : class {
            string jsonData = body != null ? JsonSerializer.Serialize(body, _jsonOptions) : null;
            string requestSummary = jsonData != null
                ? $"URL: {url}\nMethod: {method}\nPayload: {jsonData}"
                : $"URL: {url}\nMethod: {method}";

            HttpResponseMessage response = null;
            string responseBody = null;

            try {
                using (var request = new HttpRequestMessage(method, url)) {
                    if (jsonData != null) {
                        request.Content = CreateJsonContent(jsonData);
                    }
                    if (attachToken) {
                        var installToken = GsDataManager.DataOrNull?.InstallToken;
                        if (!string.IsNullOrEmpty(installToken)) {
                            request.Headers.Add("x-playnite-token", installToken);
                        }
                    }

                    response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }

                GsLogger.ShowHTTPDebugBox(
                    requestData: requestSummary,
                    responseData: $"Status: {response.StatusCode}\nBody: {responseBody}");

                if (!response.IsSuccessStatusCode && onNonSuccess != null) {
                    return onNonSuccess(response, responseBody);
                }

                TRes parsed = null;
                var contentType = response.Content?.Headers?.ContentType?.MediaType;
                if (string.IsNullOrWhiteSpace(responseBody)) {
                    _logger.Warn($"{logName} received empty response body (status {(int)response.StatusCode})");
                }
                else if (contentType != null && contentType.Contains("html")) {
                    // Reverse proxies (Cloudflare, nginx) can answer with an HTML error page,
                    // sometimes even under a 200 status code.
                    _logger.Warn($"{logName}: {url} returned HTML content-type instead of JSON, likely a proxy error page");
                }
                else {
                    try {
                        parsed = JsonSerializer.Deserialize<TRes>(responseBody, _jsonOptions);
                    }
                    catch (JsonException jsonEx) {
                        if (captureExceptions) {
                            _logger.Error(jsonEx, $"{logName} failed to parse response (status {(int)response.StatusCode})");
                            GsSentry.CaptureException(jsonEx, $"{logName}: JSON parse failed (status {(int)response.StatusCode})");
                        }
                        else {
                            _logger.Warn(jsonEx, $"{logName} failed to parse response (status {(int)response.StatusCode})");
                        }
                    }
                }

                return onParsed != null ? onParsed(response, parsed) : parsed;
            }
            catch (Exception ex) {
                GsLogger.ShowHTTPDebugBox(
                    requestData: requestSummary,
                    responseData: $"Error: {ex.Message}\nStack Trace: {ex.StackTrace}",
                    isError: true);

                if (captureExceptions) {
                    _logger.Error(ex, $"{logName} HTTP error");
                    GsSentry.CaptureException(ex, $"{logName} HTTP error");
                }
                else {
                    _logger.Warn(ex, $"{logName} HTTP error");
                }
                return null;
            }
        }

        #endregion
    }
}
