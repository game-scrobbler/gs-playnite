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
        private static readonly HttpClient _sharedHttpClient;

        static GsApiClient() {
            // Enforce TLS 1.2+ to avoid negotiating insecure protocol versions on .NET Framework 4.6.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            try {
                _sharedHttpClient = new HttpClient(new SentryHttpMessageHandler()) {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }
            catch {
                // Fallback to plain HttpClient if Sentry SDK is unavailable (e.g. expired account)
                _sharedHttpClient = new HttpClient() {
                    Timeout = TimeSpan.FromSeconds(30)
                };
            }
        }

        private readonly JsonSerializerOptions _jsonOptions;
        private readonly GsCircuitBreaker _circuitBreaker;

        public GsApiClient() {
            _jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };
            _circuitBreaker = new GsCircuitBreaker(
                failureThreshold: 3,
                timeout: TimeSpan.FromMinutes(2),
                retryDelay: TimeSpan.FromSeconds(10));
            _circuitBreaker.OnCircuitClosed += () => {
                _ = FlushPendingScrobblesAsync().ContinueWith(t => {
                    if (t.IsFaulted && t.Exception != null) {
                        _logger.Error(t.Exception.GetBaseException(), "Unhandled exception in FlushPendingScrobblesAsync (circuit recovery)");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            };
        }

        #region Game Session Management

        public class ScrobbleStartReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public string game_name { get; set; }
            public string game_id { get; set; }
            public string plugin_id { get; set; }
            public string external_game_id { get; set; }
            public object metadata { get; set; }
            public string started_at { get; set; }
        }

        public class ScrobbleStartRes {
            public string session_id { get; set; }
        }

        public class AsyncQueuedResponse {
            public bool success { get; set; }
            public string status { get; set; }
            public string queueId { get; set; }
            public string message { get; set; }
            public string timestamp { get; set; }
            public string estimatedProcessingTime { get; set; }
            // Populated on cooldown responses (status == "skipped", reason starts with "cooldown_")
            public string reason { get; set; }
            public string cooldownExpiresAt { get; set; }
            public string lastSyncAt { get; set; }
        }

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

            string url = $"{_apiBaseUrl}/api/playnite/v2/scrobble/start";

            var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, startData);
            }, maxRetries: 2);

            if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                _logger.Info($"Scrobble start queued with ID: {asyncResponse.queueId}");
                return new ScrobbleStartRes { session_id = "queued" };
            }
            else {
                GsLogger.Error("Failed to queue scrobble start request");
                CaptureSentryMessage("Failed to queue scrobble start", SentryLevel.Error, startData.game_name, startData.user_id);
                return null;
            }
        }

        public class ScrobbleFinishReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public string game_name { get; set; }
            public string game_id { get; set; }
            public string plugin_id { get; set; }
            public string external_game_id { get; set; }
            public object metadata { get; set; }
            public string finished_at { get; set; }
            public string session_id { get; set; }
        }

        public class ScrobbleFinishRes {
            public string status { get; set; }
        }

        public async Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) {
            // Validate input before making API call
            if (endData == null) {
                _logger.Error("FinishGameSession called with null endData");
                return null;
            }

            if (string.IsNullOrEmpty(endData.session_id)) {
                GsLogger.Error("Attempted to finish session with null session_id");
                CaptureSentryMessage("Null session ID in finish request", SentryLevel.Error, endData.game_name, endData.user_id);
                return null;
            }

            if (string.IsNullOrEmpty(endData.user_id) && string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken)) {
                _logger.Error("FinishGameSession called with no user_id and no install token");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/scrobble/finish";

            var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, endData, true);
            }, maxRetries: 2);

            if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                _logger.Info($"Scrobble finish queued with ID: {asyncResponse.queueId}");
                return new ScrobbleFinishRes { status = "queued" };
            }
            else {
                GsLogger.Error("Failed to queue scrobble finish request");
                return null;
            }
        }

        /// <summary>
        /// Maximum number of flush attempts before a pending scrobble is permanently dropped.
        /// Prevents infinite re-queue loops when the server consistently rejects a request.
        /// </summary>
        private const int MaxFlushAttempts = 5;

        /// <summary>
        /// Flushes all pending scrobbles that were queued when the API was unavailable.
        /// Called on circuit breaker recovery and on application startup.
        /// Failed items are re-queued (up to <see cref="MaxFlushAttempts"/> times) so they survive transient failures.
        /// </summary>
        public async Task FlushPendingScrobblesAsync() {
            if (GsDataManager.IsOptedOut) return;

            var pending = GsDataManager.DequeuePendingScrobbles();
            if (pending == null || pending.Count == 0) {
                return;
            }

            _logger.Info($"Flushing {pending.Count} pending scrobble(s)");
            var failed = new List<PendingScrobble>();

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
                        continue;
                    }
                }
                catch (Exception ex) {
                    _logger.Error(ex, $"Exception flushing pending scrobble (type={item.Type}, queued={item.QueuedAt:O})");
                }

                if (!success) {
                    item.FlushAttempts++;
                    if (item.FlushAttempts >= MaxFlushAttempts) {
                        _logger.Warn($"Dropping pending scrobble after {item.FlushAttempts} failed flush attempts (type={item.Type}, queued={item.QueuedAt:O})");
                    }
                    else {
                        failed.Add(item);
                    }
                }
            }

            // Re-queue items that failed but haven't exhausted their retry budget
            if (failed.Count > 0) {
                _logger.Info($"Re-queuing {failed.Count} pending scrobble(s) for later retry");
                foreach (var item in failed) {
                    GsDataManager.EnqueuePendingScrobble(item);
                }
            }
        }

        #endregion

        #region Library Synchronization

        public class GameSyncDto {
            public string game_id { get; set; }
            public string plugin_id { get; set; }
            public string game_name { get; set; }
            public string playnite_id { get; set; }
            public long playtime_seconds { get; set; }
            public int play_count { get; set; }
            public DateTime? last_activity { get; set; }
            public bool is_installed { get; set; }
            public string completion_status_id { get; set; }
            public string completion_status_name { get; set; }
            // Populated from SuccessStory plugin (cebe6d32-...) via reflection if installed.
            // null when SuccessStory is absent or the game has no achievement data.
            public int? achievement_count_unlocked { get; set; }
            public int? achievement_count_total { get; set; }
            // null when the collection is empty/not set in Playnite.
            public List<string> genres { get; set; }
            public List<string> platforms { get; set; }
            public List<string> developers { get; set; }
            public List<string> publishers { get; set; }
            public List<string> tags { get; set; }
            public List<string> features { get; set; }
            public List<string> categories { get; set; }
            public List<string> series { get; set; }
            // Scores: null when not set in Playnite metadata.
            public int? user_score { get; set; }
            public int? critic_score { get; set; }
            public int? community_score { get; set; }
            // Release year only (Playnite's ReleaseDate is a partial struct; month/day are optional).
            public int? release_year { get; set; }
            // Date the game was added to the Playnite library.
            public DateTime? date_added { get; set; }
            public bool is_favorite { get; set; }
            public bool is_hidden { get; set; }
            // Library source name (e.g. "Steam", "GOG") — distinct from plugin_id.
            public string source_name { get; set; }
            // Full release date string: "YYYY-MM-DD" when day/month known, "YYYY" when year-only, null if unknown.
            public string release_date { get; set; }
            // When the game entry was last modified in Playnite.
            public DateTime? modified { get; set; }
            // null when the collection is empty/not set in Playnite.
            public List<string> age_ratings { get; set; }
            public List<string> regions { get; set; }
        }

        // --- v2 Library sync DTOs ---

        public class LibraryFullSyncReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public List<GameSyncDto> library { get; set; }
            public string[] flags { get; set; }
            public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
        }

        public class LibraryDiffSyncReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public List<GameSyncDto> added { get; set; }
            public List<GameSyncDto> updated { get; set; }
            public List<string> removed { get; set; }
            public string base_snapshot_hash { get; set; }
            public string[] flags { get; set; }
            public List<Services.IntegrationAccountDto> integration_accounts { get; set; }
        }

        // --- v2 Achievement DTOs ---

        public class AchievementItemDto {
            public string name { get; set; }
            public string description { get; set; }
            public DateTime? date_unlocked { get; set; }
            public bool is_unlocked { get; set; }
            public float? rarity_percent { get; set; }
        }

        public class GameAchievementsDto {
            public string playnite_id { get; set; }
            public string game_id { get; set; }
            public string plugin_id { get; set; }
            public List<AchievementItemDto> achievements { get; set; }
        }

        public class AchievementsFullSyncReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public List<GameAchievementsDto> games { get; set; }
        }

        public class AchievementsDiffSyncReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
            public List<GameAchievementsDto> changed { get; set; }
            public string base_snapshot_hash { get; set; }
        }

        public class AchievementSyncRes {
            public bool success { get; set; }
            public string status { get; set; }
            public string reason { get; set; }
            public string message { get; set; }
            public string timestamp { get; set; }
        }

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
            }, maxRetries: 1);
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
            }, maxRetries: 1);
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
            }, maxRetries: 1);
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
            }, maxRetries: 1);
        }

        #endregion

        #region Install Token Registration

        public class RegisterInstallTokenReq {
            public string playnite_user_id { get; set; }
        }

        public class RegisterInstallTokenRes {
            public bool success { get; set; }
            public string token { get; set; }
            public string message { get; set; }
            public string error { get; set; }
            public string error_code { get; set; }
        }

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
                    HttpResponseMessage response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
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

        public class DashboardTokenRes {
            public bool success { get; set; }
            public string token { get; set; }
            public int? expires_in { get; set; }
        }

        /// <summary>
        /// Requests a short-lived (10-minute) dashboard read token from the server.
        /// The plugin embeds this token in the WebView2 URL as ?access_token=...
        /// instead of the raw install UUID, keeping the UUID out of browser history.
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
                using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                    request.Headers.Add("x-playnite-token", installToken);
                    HttpResponseMessage response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
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

        public class PlayniteNotificationDto {
            public string id { get; set; }
            public string title { get; set; }
            public string message { get; set; }
            public string notification_type { get; set; }
            public string priority { get; set; }
            public string action_url { get; set; }
            public string action_label { get; set; }
            public string created_at { get; set; }
        }

        public class PlayniteNotificationsRes {
            public bool success { get; set; }
            public List<PlayniteNotificationDto> notifications { get; set; }
        }

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
                    HttpResponseMessage response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
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
                    HttpResponseMessage response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
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

        public class AllowedPluginsRes {
            public List<AllowedPluginEntry> plugins { get; set; }
            public string source { get; set; }
        }

        public class AllowedPluginEntry {
            public string pluginId { get; set; }
            public string libraryName { get; set; }
            public string sourceSlug { get; set; }
            public string status { get; set; }
        }

        public async Task<AllowedPluginsRes> GetAllowedPlugins() {
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await GetJsonAsync<AllowedPluginsRes>($"{_apiBaseUrl}/api/playnite/v2/allowed-plugins");
            }, maxRetries: 1);
        }

        #endregion

        #region Token Verification

        public class TokenVerificationReq {
            public string token { get; set; }
            public string playniteId { get; set; }
        }

        public class TokenVerificationRes {
            public bool success { get; set; }
            public string message { get; set; }
            public string userId { get; set; }
            // Error fields returned on non-2xx responses
            public string error { get; set; }
            public string errorCode { get; set; }
        }

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

            return await _circuitBreaker.ExecuteAsync(async () => {
                var res = await PostJsonAsync<TokenVerificationRes>(
                    $"{_nextApiBaseUrl}/api/auth/playnite/verify", payload);

                // Promote the error field to message so callers always read result.message
                if (res != null && !res.success && string.IsNullOrEmpty(res.message) && !string.IsNullOrEmpty(res.error)) {
                    res.message = res.error;
                }

                return res;
            }, maxRetries: 1); // Token verification is less critical, only retry once
        }

        #endregion

        #region Data Deletion

        public class DeleteDataReq {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string user_id { get; set; }
        }

        public class DeleteDataRes {
            public bool success { get; set; }
            public string message { get; set; }
            // Set to true when the server returns 429 Too Many Requests
            public bool rateLimited { get; set; }
        }

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
                    response = await _sharedHttpClient.SendAsync(request).ConfigureAwait(false);
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

        #endregion

        #region HTTP Helper Methods

        /// <summary>
        /// Helper method to capture HTTP-related exceptions with consistent context.
        /// </summary>
        private static void CaptureHttpException(Exception exception, string url, string requestBody, HttpResponseMessage response = null, string responseBody = null) {
            string contextMessage = $"HTTP request failed for {url}. Status: {response?.StatusCode}";
            GsSentry.CaptureException(exception, contextMessage);
        }

        private static void CaptureSentryMessage(string message, SentryLevel level, string gameName = null, string userId = null, string sessionId = null) {
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
                var response = await _sharedHttpClient.GetAsync(url).ConfigureAwait(false);
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

                try {
                    return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
                }
                catch (JsonException jsonEx) {
                    _logger.Error(jsonEx, $"Failed to deserialize JSON response from GET {url}. Response: {responseBody}");
                    GsSentry.CaptureException(jsonEx, $"JSON deserialization failed for GET {url}");
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
                        response = await _sharedHttpClient.SendAsync(requestMessage).ConfigureAwait(false);
                    }
                    else {
                        response = await _sharedHttpClient.PostAsync(url, content).ConfigureAwait(false);
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

                    try {
                        var deserializedResponse = JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
                        if (deserializedResponse == null) {
                            _logger.Warn($"Deserialization returned null for {url}. Response: {responseBody}");
                        }
                        return deserializedResponse;
                    }
                    catch (JsonException jsonEx) {
                        _logger.Error(jsonEx, $"Failed to deserialize JSON response from {url}. Response: {responseBody}");
                        GsSentry.CaptureException(jsonEx, $"JSON deserialization failed for {url}");
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
