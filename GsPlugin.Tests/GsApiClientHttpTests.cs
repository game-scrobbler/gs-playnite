using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    /// <summary>
    /// Tests that exercise real GsApiClient methods against a mock HTTP handler.
    /// Requires InternalsVisibleTo("GsPlugin.Tests") for the internal constructor.
    /// </summary>
    [Collection("StaticManagerTests")]
    public class GsApiClientHttpTests {
        private string CreateTempDir() {
            var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        private void InitDataManager(string tempDir, string installToken = null) {
            GsDataManager.Initialize(tempDir, null);
            if (installToken != null) {
                GsDataManager.SetInstallTokenIfActive(installToken);
            }
        }

        /// <summary>
        /// A test HttpMessageHandler that returns a preconfigured response.
        /// Captures the last request for assertion.
        /// </summary>
        private class MockHttpHandler : HttpMessageHandler {
            public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
            public string ResponseBody { get; set; } = "{}";
            public HttpRequestMessage LastRequest { get; private set; }
            public string LastRequestBody { get; private set; }
            public int CallCount { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) {
                LastRequest = request;
                CallCount++;
                if (request.Content != null) {
                    LastRequestBody = await request.Content.ReadAsStringAsync();
                }
                return new HttpResponseMessage(StatusCode) {
                    Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json")
                };
            }
        }

        // --- StartGameSession Tests ---

        [Fact]
        public async Task StartGameSession_NullInput_ReturnsNull() {
            var handler = new MockHttpHandler();
            var client = new GsApiClient(new HttpClient(handler));

            var result = await client.StartGameSession(null);

            Assert.Null(result);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task StartGameSession_Successful_ReturnsSessionId() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "test-token-abc");

                var sessionId = Guid.NewGuid().ToString();
                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        status = "success",
                        data = new {
                            session_id = sessionId
                        },
                        message = "Scrobble started successfully"
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.StartGameSession(new ScrobbleStartReq {
                    user_id = "user-1",
                    game_name = "Test Game",
                    game_id = "game-1",
                    plugin_id = "plugin-1"
                });

                Assert.NotNull(result);
                Assert.Equal(sessionId, result.session_id);
                Assert.True(handler.CallCount > 0);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task StartGameSession_ServerError_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "test-token");

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseBody = "{\"error\":\"internal\"}"
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.StartGameSession(new ScrobbleStartReq {
                    user_id = "user-1",
                    game_name = "Test",
                    game_id = "g1",
                    plugin_id = "p1"
                });

                Assert.Null(result);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- FinishGameSession Tests ---

        [Fact]
        public async Task FinishGameSession_NullSessionId_ProceedsWithNameBasedMatching() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "test-token-abc");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        status = "success",
                        data = new { duration_seconds = 120 },
                        message = "Scrobble finished successfully"
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.FinishGameSession(new ScrobbleFinishReq {
                    user_id = "user-1",
                    game_name = "Test Game",
                    session_id = null
                });

                // Null session_id is allowed — backend uses name-based matching (Strategy 2)
                Assert.NotNull(result);
                Assert.True(handler.CallCount > 0);
                Assert.Contains("\"game_name\":\"Test Game\"", handler.LastRequestBody ?? "");
                Assert.DoesNotContain("\"session_id\"", handler.LastRequestBody ?? "");
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task FinishGameSession_NonUuidSessionId_ClearsToNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "test-token-abc");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        status = "success",
                        data = new { duration_seconds = 60 },
                        message = "Scrobble finished successfully"
                    }),
                };
                var client = new GsApiClient(new HttpClient(handler));

                var req = new ScrobbleFinishReq {
                    user_id = "user-1",
                    game_name = "Test Game",
                    session_id = "queued"
                };
                var result = await client.FinishGameSession(req);

                // Non-UUID "queued" is cleared to null on the cloned send payload, not on the caller's object
                Assert.NotNull(result);
                Assert.Equal("queued", req.session_id); // original object must not be mutated
                Assert.True(handler.CallCount > 0);
                Assert.Contains("\"game_name\":\"Test Game\"", handler.LastRequestBody ?? "");
                Assert.DoesNotContain("\"session_id\"", handler.LastRequestBody ?? ""); // null fields are omitted
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- RegisterInstallToken Tests ---

        [Fact]
        public async Task RegisterInstallToken_NullInstallId_ReturnsNull() {
            var handler = new MockHttpHandler();
            var client = new GsApiClient(new HttpClient(handler));

            var result = await client.RegisterInstallToken(null);

            Assert.Null(result);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task RegisterInstallToken_SuccessResponse_ReturnsToken() {
            var handler = new MockHttpHandler {
                ResponseBody = JsonSerializer.Serialize(new {
                    success = true,
                    token = "new-install-token-hex"
                })
            };
            var client = new GsApiClient(new HttpClient(handler));

            var result = await client.RegisterInstallToken("install-uuid-123");

            Assert.NotNull(result);
            Assert.True(result.success);
            Assert.Equal("new-install-token-hex", result.token);
        }

        [Fact]
        public async Task RegisterInstallToken_ConflictResponse_ReturnsErrorCode() {
            var handler = new MockHttpHandler {
                StatusCode = HttpStatusCode.Conflict,
                ResponseBody = JsonSerializer.Serialize(new {
                    success = false,
                    error_code = "PLAYNITE_TOKEN_ALREADY_REGISTERED",
                    error = "Token already registered"
                })
            };
            var client = new GsApiClient(new HttpClient(handler));

            var result = await client.RegisterInstallToken("install-uuid-123");

            Assert.NotNull(result);
            Assert.False(result.success);
            Assert.Equal("PLAYNITE_TOKEN_ALREADY_REGISTERED", result.error_code);
        }

        [Fact]
        public async Task RegisterInstallToken_MalformedJson_ReturnsFallback() {
            var handler = new MockHttpHandler {
                ResponseBody = "not json at all {{"
            };
            var client = new GsApiClient(new HttpClient(handler));

            var result = await client.RegisterInstallToken("install-uuid-123");

            Assert.NotNull(result);
            Assert.False(result.success);
        }

        // --- GetDashboardToken Tests ---

        [Fact]
        public async Task GetDashboardToken_NoInstallToken_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir); // no token
                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.GetDashboardToken();

                Assert.Null(result);
                Assert.Equal(0, handler.CallCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetDashboardToken_WithToken_ReturnsToken() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-install-token");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = true,
                        token = "short-lived-dashboard-token",
                        expires_in = 600
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.GetDashboardToken();

                Assert.Equal("short-lived-dashboard-token", result);
                Assert.Contains("x-playnite-token", handler.LastRequest.Headers.ToString());
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- RequestDeleteMyData Tests ---

        [Fact]
        public async Task RequestDeleteMyData_NoInstallToken_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir); // no token
                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.RequestDeleteMyData(new DeleteDataReq());

                Assert.Null(result);
                Assert.Equal(0, handler.CallCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RequestDeleteMyData_RateLimited_ReturnsFlaggedResult() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    StatusCode = (HttpStatusCode)429
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.RequestDeleteMyData(new DeleteDataReq());

                Assert.NotNull(result);
                Assert.False(result.success);
                Assert.True(result.rateLimited);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RequestDeleteMyData_Forbidden_ReturnsAlreadyOptedOut() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.Forbidden
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.RequestDeleteMyData(new DeleteDataReq());

                Assert.NotNull(result);
                Assert.False(result.success);
                Assert.True(result.alreadyOptedOut);
                Assert.False(result.authFailed);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task RequestDeleteMyData_Unauthorized_ReturnsAuthFailed() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.Unauthorized
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.RequestDeleteMyData(new DeleteDataReq());

                Assert.NotNull(result);
                Assert.False(result.success);
                Assert.True(result.authFailed);
                Assert.False(result.alreadyOptedOut);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- UnlinkAccount Tests ---

        [Fact]
        public async Task UnlinkAccount_NoInstallToken_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir); // no token
                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.UnlinkAccount();

                Assert.Null(result);
                Assert.Equal(0, handler.CallCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task UnlinkAccount_WithToken_ReturnsSuccessAndAttachesHeader() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = true
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.UnlinkAccount();

                Assert.NotNull(result);
                Assert.True(result.success);
                Assert.NotNull(handler.LastRequest);
                Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
                Assert.True(handler.LastRequest.Headers.Contains("x-playnite-token"));
                Assert.Equal("{}", handler.LastRequestBody);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task UnlinkAccount_ServerError_PreservesErrorMessage() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.BadRequest,
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = false,
                        error = "Already disconnected"
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.UnlinkAccount();

                Assert.NotNull(result);
                Assert.False(result.success);
                Assert.Equal("Already disconnected", result.error);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- GetNotifications Tests ---

        [Fact]
        public async Task GetNotifications_NoInstallToken_ReturnsNull() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir); // no token
                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.GetNotifications();

                Assert.Null(result);
                Assert.Equal(0, handler.CallCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task GetNotifications_WithToken_ReturnsNotifications() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = true,
                        notifications = new[] {
                            new { id = "n1", title = "Hello", message = "World", notification_type = "info" }
                        }
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                var result = await client.GetNotifications();

                Assert.NotNull(result);
                Assert.True(result.success);
                Assert.Single(result.notifications);
                Assert.Equal("n1", result.notifications[0].id);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- FlushPendingScrobblesAsync Tests ---

        [Fact]
        public async Task FlushPendingScrobblesAsync_EmptyQueue_DoesNothing() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");
                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                await client.FlushPendingScrobblesAsync();

                Assert.Equal(0, handler.CallCount);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task FlushPendingScrobblesAsync_WithPendingItems_SendsAndRemoves() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                    Type = "start",
                    StartData = new ScrobbleStartReq {
                        user_id = "u1",
                        game_name = "Game",
                        game_id = "g1",
                        plugin_id = "p1"
                    },
                    QueuedAt = DateTime.UtcNow
                });

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = true,
                        status = "queued",
                        queueId = "q-1"
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                await client.FlushPendingScrobblesAsync();

                Assert.True(handler.CallCount > 0);
                Assert.Empty(GsDataManager.PeekPendingScrobbles());
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task FlushPendingScrobblesAsync_FailedItem_IncrementsAttempts() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                    Type = "start",
                    StartData = new ScrobbleStartReq {
                        user_id = "u1",
                        game_name = "Game",
                        game_id = "g1",
                        plugin_id = "p1"
                    },
                    QueuedAt = DateTime.UtcNow,
                    FlushAttempts = 0
                });

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseBody = "{\"error\":\"fail\"}"
                };
                var client = new GsApiClient(new HttpClient(handler));

                await client.FlushPendingScrobblesAsync();

                // Item should still be in the queue with incremented FlushAttempts
                var remaining = GsDataManager.PeekPendingScrobbles();
                Assert.Single(remaining);
                Assert.Equal(1, remaining[0].FlushAttempts);
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task FlushPendingScrobblesAsync_MaxAttempts_DropsItem() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                    Type = "start",
                    StartData = new ScrobbleStartReq {
                        user_id = "u1",
                        game_name = "Game",
                        game_id = "g1",
                        plugin_id = "p1"
                    },
                    QueuedAt = DateTime.UtcNow,
                    FlushAttempts = 4 // One more failure will hit max (5)
                });

                var handler = new MockHttpHandler {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseBody = "{\"error\":\"fail\"}"
                };
                var client = new GsApiClient(new HttpClient(handler));

                await client.FlushPendingScrobblesAsync();

                // Item should be dropped after reaching max attempts
                Assert.Empty(GsDataManager.PeekPendingScrobbles());
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task FlushPendingScrobblesAsync_InvalidType_DropsItem() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "valid-token");
                GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                    Type = "unknown",
                    QueuedAt = DateTime.UtcNow
                });

                var handler = new MockHttpHandler();
                var client = new GsApiClient(new HttpClient(handler));

                await client.FlushPendingScrobblesAsync();

                Assert.Empty(GsDataManager.PeekPendingScrobbles());
                Assert.Equal(0, handler.CallCount); // No HTTP call for invalid type
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }

        // --- PostJsonAsync gzip behavior ---

        [Fact]
        public async Task StartGameSession_AttachesInstallTokenHeader() {
            var tempDir = CreateTempDir();
            try {
                InitDataManager(tempDir, "my-secret-token");

                var handler = new MockHttpHandler {
                    ResponseBody = JsonSerializer.Serialize(new {
                        success = true,
                        status = "queued",
                        queueId = "q-1"
                    })
                };
                var client = new GsApiClient(new HttpClient(handler));

                await client.StartGameSession(new ScrobbleStartReq {
                    game_name = "Test",
                    game_id = "g1",
                    plugin_id = "p1"
                });

                Assert.NotNull(handler.LastRequest);
                Assert.True(handler.LastRequest.Headers.Contains("x-playnite-token"));
            }
            finally {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
