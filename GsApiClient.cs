using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Playnite.SDK;
using Sentry;

namespace GsPlugin {
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
        }

        public async Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData, bool useAsync = false) {
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

            // Build URL with async query parameter if needed
            string url = $"{_apiBaseUrl}/api/playnite/scrobble/start";
            if (useAsync) {
                url += "?async=true";
            }

            // If async mode, we expect AsyncQueuedResponse, otherwise ScrobbleStartRes
            if (useAsync) {
                var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<AsyncQueuedResponse>(url, startData);
                }, maxRetries: 2);

                if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                    _logger.Info($"Scrobble start queued with ID: {asyncResponse.queueId}");
                    // Return a placeholder response for async mode
                    // Session ID will be created on the backend
                    return new ScrobbleStartRes { session_id = "queued" };
                }
                else {
                    GsLogger.Error("Failed to queue scrobble start request");
                    CaptureSentryMessage("Failed to queue scrobble start", SentryLevel.Error, startData.game_name, startData.user_id);
                    return null;
                }
            }
            else {
                var response = await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<ScrobbleStartRes>(url, startData);
                }, maxRetries: 2);

                if (response == null || string.IsNullOrEmpty(response.session_id)) {
                    GsLogger.Error("Failed to get valid session ID from start session response");
                    CaptureSentryMessage("Invalid session response", SentryLevel.Error, startData.game_name, startData.user_id);
                }

                return response;
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

        public async Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData, bool useAsync = false) {
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

            // Build URL with async query parameter if needed
            string url = $"{_apiBaseUrl}/api/playnite/scrobble/finish";
            if (useAsync) {
                url += "?async=true";
            }

            // If async mode, we expect AsyncQueuedResponse, otherwise ScrobbleFinishRes
            if (useAsync) {
                var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<AsyncQueuedResponse>(url, endData, true);
                }, maxRetries: 2);

                if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                    _logger.Info($"Scrobble finish queued with ID: {asyncResponse.queueId}");
                    // Return a success response for async mode
                    return new ScrobbleFinishRes { status = "queued" };
                }
                else {
                    GsLogger.Error("Failed to queue scrobble finish request");
                    return null;
                }
            }
            else {
                return await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<ScrobbleFinishRes>(url, endData, true);
                }, maxRetries: 2);
            }
        }

        /// <summary>
        /// Flushes all pending scrobbles that were queued when the API was unavailable.
        /// Called on circuit breaker recovery and on application startup.
        /// </summary>
        public async Task FlushPendingScrobblesAsync() {
            var pending = GsDataManager.DequeuePendingScrobbles();
            if (pending == null || pending.Count == 0) {
                return;
            }

            _logger.Info($"Flushing {pending.Count} pending scrobble(s)");
            foreach (var item in pending) {
                try {
                    if (item.Type == "start" && item.StartData != null) {
                        await StartGameSession(item.StartData, useAsync: true);
                    }
                    else if (item.Type == "finish" && item.FinishData != null) {
                        await FinishGameSession(item.FinishData, useAsync: true);
                    }
                }
                catch (Exception ex) {
                    // Log and drop — do not re-enqueue to avoid infinite loops.
                    // If the circuit opens again during flush, new failures will be re-queued normally.
                    _logger.Error(ex, $"Failed to flush pending scrobble (type={item.Type}, queued={item.QueuedAt:O})");
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
        }

        public class LibrarySyncDetails {
            public int added { get; set; }
            public int updated { get; set; }
        }

        public async Task<LibrarySyncRes> SyncLibrary(LibrarySyncReq librarySyncReq, bool useAsync = false) {
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

            // Build URL with async query parameter if needed
            string url = $"{_apiBaseUrl}/api/playnite/v2/sync";
            if (useAsync) {
                url += "?async=true";
            }

            // If async mode, we expect AsyncQueuedResponse, otherwise LibrarySyncRes
            if (useAsync) {
                var asyncResponse = await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<AsyncQueuedResponse>(url, librarySyncReq, true);
                }, maxRetries: 1);

                if (asyncResponse != null && asyncResponse.success && asyncResponse.status == "queued") {
                    _logger.Info($"Library sync queued with ID: {asyncResponse.queueId}");
                    // Return a placeholder response for async mode
                    return new LibrarySyncRes {
                        status = "queued",
                        result = new LibrarySyncDetails { added = 0, updated = 0 },
                        userId = null
                    };
                }
                else {
                    GsLogger.Error("Failed to queue library sync request");
                    return null;
                }
            }
            else {
                return await _circuitBreaker.ExecuteAsync(async () => {
                    return await PostJsonAsync<LibrarySyncRes>(url, librarySyncReq, true);
                }, maxRetries: 1); // Library sync is less critical, only retry once
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
                return await PostJsonAsync<TokenVerificationRes>(
                    $"{_nextApiBaseUrl}/api/auth/playnite/verify", payload);
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
