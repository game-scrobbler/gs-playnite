using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Sentry;

namespace GsPlugin {
    public class GsApiClient {
        private static readonly ILogger _logger = LogManager.GetLogger();
#if DEBUG
        private static readonly string _apiBaseUrl = "https://api.stage.gamescrobbler.com";
        private static readonly string _nextApiBaseUrl = "https://stage.gamescrobbler.com";
#else
        private static readonly string _apiBaseUrl = "https://api.gamescrobbler.com";
        private static readonly string _nextApiBaseUrl = "https://gamescrobbler.com";
#endif
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public GsApiClient() {
            _httpClient = new HttpClient(new SentryHttpMessageHandler());
            _jsonOptions = new JsonSerializerOptions {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        #region Game Session Management

        public class ScrobbleStartReq {
            public string user_id { get; set; }
            public string game_name { get; set; }
            public string game_id { get; set; }
            public object metadata { get; set; }
            public string started_at { get; set; }
        }

        public class ScrobbleStartRes {
            public string session_id { get; set; }
        }

        public async Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData) {
            var response = await PostJsonAsync<ScrobbleStartRes>(
                $"{_apiBaseUrl}/api/playnite/scrobble/start", startData);

            if (response == null || string.IsNullOrEmpty(response.session_id)) {
                GsLogger.Error("Failed to get valid session ID from start session response");                CaptureSentryMessage("Invalid session response", SentryLevel.Error, startData.game_name, startData.user_id);
            }

            return response;
        }

        public class ScrobbleFinishReq {
            public string user_id { get; set; }
            public string game_name { get; set; }
            public string game_id { get; set; }
            public object metadata { get; set; }
            public string finished_at { get; set; }
            public string session_id { get; set; }
        }

        public class ScrobbleFinishRes {
            public string status { get; set; }
        }

        public async Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData) {
            if (string.IsNullOrEmpty(endData.session_id)) {
                GsLogger.Error("Attempted to finish session with null session_id");
                CaptureSentryMessage("Null session ID in finish request", SentryLevel.Error, endData.game_name, endData.user_id);
                return null;
            }

            return await PostJsonAsync<ScrobbleFinishRes>(
                $"{_apiBaseUrl}/api/playnite/scrobble/finish", endData, true);
        }

        #endregion

        #region Library Synchronization

        public class LibrarySyncReq {
            public string user_id { get; set; }
            public List<Playnite.SDK.Models.Game> library { get; set; }
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

        public async Task<LibrarySyncRes> SyncLibrary(LibrarySyncReq librarySyncReq) {
            return await PostJsonAsync<LibrarySyncRes>(
                $"{_apiBaseUrl}/api/playnite/sync", librarySyncReq, true);
        }

        #endregion

        #region Token Verification

        public class TokenVerificationReq {
            public string token { get; set; }
            public string playniteId { get; set; }
        }

        public class TokenVerificationRes {
            public string status { get; set; }
            public string message { get; set; }
            public string userId { get; set; }
        }

        public async Task<TokenVerificationRes> VerifyToken(string token, string playniteId) {
            var payload = new TokenVerificationReq {
                token = token,
                playniteId = playniteId,
            };
            return await PostJsonAsync<TokenVerificationRes>(
                $"{_nextApiBaseUrl}/api/auth/playnite/verify", payload);
        }

        #endregion

        #region HTTP Helper Methods

        /// <summary>
        /// Helper method to capture HTTP-related exceptions with consistent context.
        /// </summary>
        private void CaptureHttpException(Exception exception, string url, string requestBody, HttpResponseMessage response = null, string responseBody = null) {
            SentrySdk.CaptureException(exception, scope => {
                scope.SetExtra("RequestUrl", url);
                scope.SetExtra("RequestBody", requestBody);
                scope.SetExtra("ResponseStatus", response?.StatusCode);
                scope.SetExtra("ResponseBody", responseBody);
            });
        }

        private void CaptureSentryMessage(string message, SentryLevel level, string gameName = null, string userId = null, string sessionId = null) {
            SentrySdk.CaptureMessage(message, scope => {
                scope.Level = level;
                if (!string.IsNullOrEmpty(gameName)) {
                    scope.SetExtra("GameName", gameName);
                }
                if (!string.IsNullOrEmpty(userId)) {
                    scope.SetExtra("UserId", userId);
                }
                if (!string.IsNullOrEmpty(sessionId)) {
                    scope.SetExtra("SessionId", sessionId);
                }
            });
        }

        private async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);
            using (var content = new StringContent(jsonData, Encoding.UTF8, "application/json")) {
                HttpResponseMessage response = null;
                string responseBody = null;

                try {
                    response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
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

                    return JsonSerializer.Deserialize<TResponse>(responseBody);
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
