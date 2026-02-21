using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

            _sharedHttpClient = new HttpClient(new SentryHttpMessageHandler()) {
                Timeout = TimeSpan.FromSeconds(30)
            };
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
            _circuitBreaker.OnCircuitClosed += () => _ = FlushPendingScrobblesAsync();
        }

        #region Game Session Management

        public class ScrobbleStartReq {
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

            if (string.IsNullOrEmpty(startData.user_id)) {
                _logger.Error("StartGameSession called with null or empty user_id");
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

            if (string.IsNullOrEmpty(endData.user_id)) {
                _logger.Error("FinishGameSession called with null or empty user_id");
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
            var pending = GsDataManager.DequeuePendingScrobbles();
            if (pending == null || pending.Count == 0) {
                return;
            }

            _logger.Info($"Flushing {pending.Count} pending scrobble(s)");
            var failed = new List<PendingScrobble>();

            foreach (var item in pending) {
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

        public class LibrarySyncReq {
            public string user_id { get; set; }
            public List<GameSyncDto> library { get; set; }
            public string[] flags { get; set; }
        }

        public class LibrarySyncRes {
            public string status { get; set; }
            public LibrarySyncDetails result { get; set; }
            // If account not linked yet it will be "not_linked"
            public string userId { get; set; }
            // Set when the server skipped the sync due to the 24-hour cooldown
            public bool isCooldown { get; set; }
            public DateTime? cooldownExpiresAt { get; set; }
        }

        public class LibrarySyncDetails {
            public int added { get; set; }
            public int updated { get; set; }
        }

        // --- v2 Library sync DTOs ---

        public class LibraryFullSyncReq {
            public string user_id { get; set; }
            public List<GameSyncDto> library { get; set; }
            public string[] flags { get; set; }
        }

        public class LibraryDiffSyncReq {
            public string user_id { get; set; }
            public List<GameSyncDto> added { get; set; }
            public List<GameSyncDto> updated { get; set; }
            public List<string> removed { get; set; }
            public string base_snapshot_hash { get; set; }
            public string[] flags { get; set; }
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
            public string user_id { get; set; }
            public List<GameAchievementsDto> games { get; set; }
        }

        public class AchievementsDiffSyncReq {
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

        public async Task<LibrarySyncRes> SyncLibrary(LibrarySyncReq librarySyncReq) {
            // Validate input before making API call
            if (librarySyncReq == null) {
                _logger.Error("SyncLibrary called with null librarySyncReq");
                return null;
            }

            if (string.IsNullOrEmpty(librarySyncReq.user_id)) {
                _logger.Error("SyncLibrary called with null or empty user_id");
                return null;
            }

            if (librarySyncReq.library == null) {
                _logger.Warn("SyncLibrary called with null library, treating as empty");
                librarySyncReq.library = new List<GameSyncDto>();
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/sync";

            var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, librarySyncReq, true);
            }, maxRetries: 1);

            if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                _logger.Info($"Library sync queued with ID: {asyncResponse.queueId}");
                return new LibrarySyncRes {
                    status = "queued",
                    result = new LibrarySyncDetails { added = 0, updated = 0 },
                    userId = null
                };
            }
            else if (asyncResponse != null && asyncResponse.status == "skipped"
                     && asyncResponse.reason != null && asyncResponse.reason.StartsWith("cooldown_")) {
                DateTime? expiresAt = null;
                if (!string.IsNullOrEmpty(asyncResponse.cooldownExpiresAt)
                    && DateTime.TryParse(asyncResponse.cooldownExpiresAt, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) {
                    expiresAt = parsed.ToUniversalTime();
                }
                _logger.Info($"Library sync skipped by server cooldown. Expires: {expiresAt?.ToString("O") ?? "unknown"}");
                return new LibrarySyncRes {
                    status = "skipped",
                    isCooldown = true,
                    cooldownExpiresAt = expiresAt
                };
            }
            else {
                GsLogger.Error("Failed to queue library sync request");
                return null;
            }
        }

        public async Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req) {
            if (req == null) {
                _logger.Error("SyncLibraryFull called with null request");
                return null;
            }
            if (string.IsNullOrEmpty(req.user_id)) {
                _logger.Error("SyncLibraryFull called with null or empty user_id");
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
            if (string.IsNullOrEmpty(req.user_id)) {
                _logger.Error("SyncLibraryDiff called with null or empty user_id");
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
            if (string.IsNullOrEmpty(req.user_id)) {
                _logger.Error("SyncAchievementsFull called with null or empty user_id");
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
            if (string.IsNullOrEmpty(req.user_id)) {
                _logger.Error("SyncAchievementsDiff called with null or empty user_id");
                return null;
            }

            string url = $"{_apiBaseUrl}/api/playnite/v2/achievements/sync-diff";
            return await _circuitBreaker.ExecuteAsync(async () => {
                return await PostJsonAsync<AsyncQueuedResponse>(url, req, true);
            }, maxRetries: 1);
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
                return await GetJsonAsync<AllowedPluginsRes>($"{_apiBaseUrl}/api/playnite/allowed-plugins");
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

        private async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);
            using (var content = new StringContent(jsonData, Encoding.UTF8, "application/json")) {
                HttpResponseMessage response = null;
                string responseBody = null;

                try {
                    response = await _sharedHttpClient.PostAsync(url, content).ConfigureAwait(false);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    GsLogger.ShowHTTPDebugBox(
                        requestData: $"URL: {url}\nPayload: {jsonData}",
                        responseData: $"Status: {response.StatusCode}\nBody: {responseBody}");

                    if (ensureSuccess && !response.IsSuccessStatusCode) {
                        var httpEx = new HttpRequestException(
                            $"Request failed with status {(int)response.StatusCode} ({response.StatusCode}) for URL {url}");

                        CaptureHttpException(httpEx, url, jsonData, response, responseBody);
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
