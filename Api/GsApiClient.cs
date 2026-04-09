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
using Playnite;
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
            // TLS 1.2+ is the default on .NET 10; ServicePointManager is not used.

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
        }

        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly GsCircuitBreaker _circuitBreaker;

        public GsApiClient() : this(_defaultHttpClient) { }

        /// <summary>
        /// Constructor that accepts a custom HttpClient for testing.
        /// Production code uses the parameterless constructor which provides the shared Sentry-traced client.
        /// </summary>
        internal GsApiClient(HttpClient httpClient) {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };
            _circuitBreaker = new GsCircuitBreaker(
                failureThreshold: 3,
                timeout: TimeSpan.FromMinutes(2),
                retryDelay: TimeSpan.FromSeconds(10));
            _circuitBreaker.OnCircuitClosed += () => {
                _ = FlushPendingScrobblesAsync().ContinueWith(t => {
                    if (t.Exception != null) {
                        _logger.Error(t.Exception.GetBaseException(), "Unhandled exception in FlushPendingScrobblesAsync (circuit recovery)");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            };
        }

        #region Game Session Management

        public async Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData) {
            // Validate input before making API call
            if (startData == null) {
                _logger.Error("StartGameSession called with null startData");
                return null;
            }

            if (string.IsNullOrEmpty(startData.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("StartGameSession called with no user_id and no install token");
                return null;
            }

            if (string.IsNullOrEmpty(startData.game_name)) {
                _logger.Warn("StartGameSession called with null or empty game_name");
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/scrobble/start";

            var envelope = await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<ApiResponse<ScrobbleStartData>>(url, startData);
            }, maxRetries: 2, isFailure: r => r == null);

            switch (envelope?.Outcome) {
                case ApiOutcome.Success when envelope.data?.session_id != null:
                    _logger.Info($"Scrobble start complete with session ID: {envelope.data.session_id}");
                    return new ScrobbleStartRes { session_id = envelope.data.session_id };

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

        public async Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) {
            // Validate input before making API call
            if (endData == null) {
                _logger.Error("FinishGameSession called with null endData");
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

            if (string.IsNullOrEmpty(sendData.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("FinishGameSession called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v3/scrobble/finish";

            var envelope = await _circuitBreaker.ExecuteAsync(async () => {
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
                // Peek without clearing: items remain persisted until individually confirmed.
                var pending = GsDataManager.PeekPendingScrobbles();
                if (pending == null || pending.Count == 0) {
                    return;
                }

                _logger.Info($"Flushing {pending.Count} pending scrobble(s)");

                foreach (var item in pending) {
                    // Re-check opt-out before each send (user may have opted out mid-flush)
                    if (GsDataManager.IsOptedOut) break;

                    bool success = false;
                    try {
                        if (item.Type == "start" && item.StartData != null) {
                            var res = await StartGameSession(item.StartData);
                            success = res != null;
                        }
                        else if (item.Type == "finish" && item.FinishData != null) {
                            var res = await FinishGameSession(item.FinishData);
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
                    }

                    if (success) {
                        // Remove from the persisted queue now that the server has accepted it.
                        GsDataManager.RemovePendingScrobble(item);
                    }
                    else {
                        item.FlushAttempts++;
                        if (item.FlushAttempts >= MaxFlushAttempts) {
                            _logger.Warn($"Dropping pending scrobble after {item.FlushAttempts} failed flush attempts (type={item.Type}, queued={item.QueuedAt:O})");
                            GsDataManager.RemovePendingScrobble(item);
                            GsDataManager.MutateAndSave(d => d.DroppedScrobbleCount++);
                            GsSentry.AddBreadcrumb(
                                message: $"Dropped pending scrobble after {MaxFlushAttempts} attempts",
                                category: "flush",
                                data: new System.Collections.Generic.Dictionary<string, string> {
                                    { "type", item.Type },
                                    { "queued_at", item.QueuedAt.ToString("O") }
                                });
                        }
                        else {
                            // Item stays in the queue with its incremented FlushAttempts counter.
                            // Persist the updated attempt count so the drop threshold survives a restart.
                            GsDataManager.Save();
                        }
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
            if (req == null) {
                _logger.Error("SyncLibraryFull called with null request");
                return null;
            }
            if (string.IsNullOrEmpty(req.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("SyncLibraryFull called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/library/sync-full";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req) {
            if (req == null) {
                _logger.Error("SyncLibraryDiff called with null request");
                return null;
            }
            if (string.IsNullOrEmpty(req.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("SyncLibraryDiff called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/library/sync-diff";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req) {
            if (req == null) {
                _logger.Error("SyncAchievementsFull called with null request");
                return null;
            }
            if (string.IsNullOrEmpty(req.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("SyncAchievementsFull called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/achievements/sync-full";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        public async Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req) {
            if (req == null) {
                _logger.Error("SyncAchievementsDiff called with null request");
                return null;
            }
            if (string.IsNullOrEmpty(req.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("SyncAchievementsDiff called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/achievements/sync-diff";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1, isFailure: r => r == null);
        }

        #endregion

        #region Install Token Registration

        /// <summary>
        /// Registers the install with the server and retrieves a per-install auth token.
        /// Call once on first boot (or when InstallToken is missing from persistent storage).
        /// Returns the response body so the caller can inspect error_code.
        /// HTTP 409 (error_code PLAYNITE_TOKEN_ALREADY_REGISTERED) means the server already has
        /// a token for this install ID — the local copy was lost; call ResetInstallToken to recover.
        /// </summary>
        public async Task<RegisterInstallTokenRes> RegisterInstallToken(string installId) {
            if (string.IsNullOrEmpty(installId)) {
                _logger.Error("RegisterInstallToken called with null or empty installId");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/register";
            var req = new RegisterInstallTokenReq { playnite_user_id = installId };

            try {
                string jsonData = JsonSerializer.Serialize(req, _jsonOptions);
                using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                    request.Content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    try {
                        return JsonSerializer.Deserialize<RegisterInstallTokenRes>(responseBody, _jsonOptions)
                            ?? new RegisterInstallTokenRes { success = false };
                    }
                    catch {
                        return new RegisterInstallTokenRes { success = false };
                    }
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "RegisterInstallToken HTTP error");
                GsSentry.CaptureException(ex, "RegisterInstallToken HTTP error");
                return null;
            }
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

            string url = $"{_apiBaseUrl}/api/playnite/v2/dashboard-token";

            try {
                var data = GsDataManager.DataOrNull;
                var context = new {
                    plugin_version = GsSentry.GetPluginVersion(),
                    scrobbling_disabled = data?.Flags?.Contains("no-scrobble") ?? false,
                    sentry_disabled = data?.Flags?.Contains("no-sentry") ?? false,
                    posthog_disabled = data?.Flags?.Contains("no-posthog") ?? false,
                    new_dashboard = data?.NewDashboardExperience ?? false,
                    sync_achievements = data?.SyncAchievements ?? false,
                };

                string jsonBody = JsonSerializer.Serialize(new { context }, _jsonOptions);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                    request.Headers.Add("x-playnite-token", installToken);
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode) {
                        _logger.Warn($"GetDashboardToken returned {(int)response.StatusCode}");
                        return null;
                    }

                    var res = JsonSerializer.Deserialize<DashboardTokenRes>(responseBody, _jsonOptions);
                    return res?.token;
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "GetDashboardToken HTTP error");
                GsSentry.CaptureException(ex, "GetDashboardToken HTTP error");
                return null;
            }
        }

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

            string url = $"{_apiBaseUrl}/api/playnite/v2/notifications";

            try {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                    request.Headers.Add("x-playnite-token", installToken);
                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode) {
                        _logger.Warn($"GetNotifications returned {(int)response.StatusCode}");
                        return null;
                    }

                    return JsonSerializer.Deserialize<PlayniteNotificationsRes>(responseBody, _jsonOptions);
                }
            }
            catch (Exception ex) {
                _logger.Warn(ex, "GetNotifications HTTP error");
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Rotates the install token by calling /v2/reset-token with the current token.
        /// Use when the server returns PLAYNITE_TOKEN_ALREADY_REGISTERED on registration
        /// (meaning the plugin lost its locally-stored token and needs to rotate to recover).
        /// Returns the new raw token on success, or null on failure.
        /// </summary>
        public async Task<string> ResetInstallToken(string currentToken) {
            if (string.IsNullOrEmpty(currentToken)) {
                _logger.Error("ResetInstallToken called with null or empty currentToken");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/reset-token";

            try {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                    request.Headers.Add("x-playnite-token", currentToken);
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode) {
                        _logger.Warn($"ResetInstallToken returned {(int)response.StatusCode}");
                        return null;
                    }

                    var res = JsonSerializer.Deserialize<RegisterInstallTokenRes>(responseBody, _jsonOptions);
                    return res?.token;
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "ResetInstallToken HTTP error");
                GsSentry.CaptureException(ex, "ResetInstallToken HTTP error");
                return null;
            }
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

            string url = $"{_nextApiBaseUrl}/api/auth/playnite/verify";

            try {
                string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);
                using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                    request.Content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(responseBody)) {
                        _logger.Warn($"VerifyToken received empty response body (status {(int)response.StatusCode})");
                        return null;
                    }

                    // Parse response body even on non-2xx status codes so the caller
                    // receives the actual server error message (e.g. "Token expired")
                    // instead of a generic "network error".
                    TokenVerificationRes res;
                    try {
                        res = JsonSerializer.Deserialize<TokenVerificationRes>(responseBody, _jsonOptions);
                    }
                    catch (JsonException jsonEx) {
                        _logger.Error(jsonEx, $"VerifyToken failed to parse response (status {(int)response.StatusCode})");
                        return null;
                    }

                    if (res == null) return null;

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
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "VerifyToken HTTP error");
                GsSentry.CaptureException(ex, "VerifyToken HTTP error");
                return null; // True network error — caller will show network error message
            }
        }

        #endregion

        #region Account Unlinking

        public async Task<UnlinkRes> UnlinkAccount() {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (string.IsNullOrEmpty(installToken)) {
                _logger.Error("UnlinkAccount called with no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/unlink";

            try {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                    request.Headers.Add("x-playnite-token", installToken);
                    var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(responseBody)) {
                        _logger.Warn($"UnlinkAccount received empty response (status {(int)response.StatusCode})");
                        return null;
                    }

                    var res = JsonSerializer.Deserialize<UnlinkRes>(responseBody, _jsonOptions);
                    if (res == null) return null;

                    if (!response.IsSuccessStatusCode) {
                        res.success = false;
                        _logger.Warn($"UnlinkAccount returned {(int)response.StatusCode}: {res.error ?? "unknown"}");
                    }

                    return res;
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "UnlinkAccount HTTP error");
                GsSentry.CaptureException(ex, "UnlinkAccount HTTP error");
                return null;
            }
        }

        #endregion

        #region Data Deletion

        public async Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req) {
            var installToken = GsDataManager.DataOrNull?.InstallToken;
            if (req == null || string.IsNullOrEmpty(installToken)) {
                _logger.Error("RequestDeleteMyData called with no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/delete-data";
            string jsonData = JsonSerializer.Serialize(req, _jsonOptions);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            try {
                HttpResponseMessage response;
                using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content }) {
                    request.Headers.Add("x-playnite-token", installToken);
                    response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                }
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if ((int)response.StatusCode == 429) {
                    _logger.Warn("RequestDeleteMyData rate limited by server");
                    return new DeleteDataRes { success = false, rateLimited = true };
                }

                if (!response.IsSuccessStatusCode) {
                    _logger.Warn($"RequestDeleteMyData returned {(int)response.StatusCode}");
                    return new DeleteDataRes { success = false };
                }

                return JsonSerializer.Deserialize<DeleteDataRes>(responseBody, _jsonOptions)
                    ?? new DeleteDataRes { success = false };
            }
            catch (Exception ex) {
                _logger.Error(ex, "RequestDeleteMyData HTTP error");
                GsSentry.CaptureException(ex, "RequestDeleteMyData HTTP error");
                return null;
            }
        }

        public async Task<OptInRes> RequestOptIn(OptInReq req) {
            var installId = GsDataManager.DataOrNull?.InstallID;
            if (req == null || string.IsNullOrEmpty(installId)) {
                _logger.Error("RequestOptIn called with no install ID");
                return null;
            }

            req.user_id = installId;
            string url = $"{_apiBaseUrl}/api/playnite/v2/opt-in";
            string jsonData = JsonSerializer.Serialize(req, _jsonOptions);
            HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            try {
                HttpResponseMessage response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
                string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if ((int)response.StatusCode == 429) {
                    _logger.Warn("RequestOptIn rate limited by server");
                    return new OptInRes { success = false, rateLimited = true };
                }

                if (!response.IsSuccessStatusCode) {
                    _logger.Warn($"RequestOptIn returned {(int)response.StatusCode}");
                    return new OptInRes { success = false };
                }

                return JsonSerializer.Deserialize<OptInRes>(responseBody, _jsonOptions)
                    ?? new OptInRes { success = false };
            }
            catch (Exception ex) {
                _logger.Error(ex, "RequestOptIn HTTP error");
                GsSentry.CaptureException(ex, "RequestOptIn HTTP error");
                return null;
            }
        }

        #endregion

        #region HTTP Helper Methods

        /// <summary>
        /// Helper method to capture HTTP-related exceptions with consistent context.
        /// </summary>
        private static void CaptureHttpException(Exception exception, string url, string requestBody, HttpResponseMessage? response = null, string? responseBody = null) {
            string contextMessage = $"HTTP request failed for {url}. Status: {response?.StatusCode}";
            GsSentry.CaptureException(exception, contextMessage);
        }

        private static void CaptureSentryMessage(string message, SentryLevel level, string? gameName = null, string? userId = null, string? sessionId = null) {
            string contextMessage = message;
            if (!string.IsNullOrEmpty(gameName)) {
                contextMessage += $" [Game: {gameName}]";
            }
            if (!string.IsNullOrEmpty(userId)) {
                contextMessage += $" [User: {userId}]";
            }
            if (!string.IsNullOrEmpty(sessionId)) {
                contextMessage += $" [Session: {sessionId}]";
            }
            GsSentry.CaptureMessage(contextMessage, level);
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

        private async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);

            HttpContent content;
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonData);
            if (jsonBytes.Length >= GzipThresholdBytes) {
                var compressedStream = new MemoryStream();
                using (var gzip = new GZipStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true)) {
                    gzip.Write(jsonBytes, 0, jsonBytes.Length);
                }
                compressedStream.Position = 0;
                content = new StreamContent(compressedStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                content.Headers.ContentEncoding.Add("gzip");
            }
            else {
                content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            }

            using (content) {
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
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    GsLogger.ShowHTTPDebugBox(
                        requestData: $"URL: {url}\nPayload: {jsonData}",
                        responseData: $"Status: {response.StatusCode}\nBody: {responseBody}");

                    if (!response.IsSuccessStatusCode) {
                        if (ensureSuccess) {
                            var httpEx = new HttpRequestException(
                                $"Request failed with status {(int)response.StatusCode} ({response.StatusCode}) for URL {url}");

                            CaptureHttpException(httpEx, url, jsonData, response, responseBody);
                        }
                        else {
                            _logger.Warn($"POST {url} returned {(int)response.StatusCode} ({response.StatusCode})");
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

        #endregion
    }
}
