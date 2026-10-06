using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using GsPlugin.Api;
using Xunit;

namespace GsPlugin.Tests {
    public class ScrobbleStartFailureTests {
        [Fact]
        public void Message_DoesNotIncludeGameOrUserIdentity() {
            Assert.Equal("Failed to start scrobble session", ScrobbleStartFailure.Message);
            Assert.DoesNotContain("Game:", ScrobbleStartFailure.Message);
            Assert.DoesNotContain("[", ScrobbleStartFailure.Message);
        }

        [Fact]
        public void Fingerprint_IsStableAndIndependentOfGame() {
            Assert.Equal(new[] { "gs-playnite", "scrobble-start-failed" }, ScrobbleStartFailure.Fingerprint);
        }

        [Fact]
        public void FinishFailure_MessageAndFingerprintStayAnonymous() {
            Assert.Equal("Failed to finish scrobble session", ScrobbleFinishFailure.Message);
            Assert.Equal(new[] { "gs-playnite", "scrobble-finish-failed" }, ScrobbleFinishFailure.Fingerprint);
            Assert.DoesNotContain("Game:", ScrobbleFinishFailure.Message);
        }

        [Theory]
        [InlineData(0, false, 0, null, null, false)]
        [InlineData(0, true, 0, null, null, false)]
        [InlineData(1, true, 0, null, null, false)]
        [InlineData(3, true, 0, null, null, false)]
        [InlineData(1, false, 0, null, null, true)]
        [InlineData(3, false, 0, null, null, true)]
        [InlineData(1, false, 429, null, "http", false)]
        [InlineData(1, false, 403, "OPTED_OUT", "http", false)]
        [InlineData(1, false, 403, null, "http", true)]
        [InlineData(1, false, 404, null, "http", true)]
        [InlineData(1, false, 200, null, "json", true)]
        [InlineData(1, false, 200, null, "html", true)]
        [InlineData(1, false, 0, null, "exception", true)]
        public void ShouldCapture_OnlyLivePathAfterAnAttempt(
            int attempts, bool isFlushRetry, int httpStatus, string code, string failureKind, bool expected) {
            Assert.Equal(expected, ScrobbleStartFailure.ShouldCapture(attempts, isFlushRetry, httpStatus, code, failureKind));
        }

        // GS-PLAYNITE-PT regressed on two transport failures whose starts the pending queue
        // replayed five minutes later. A blip the queue recovers from is not an issue.
        [Theory]
        [InlineData(0, "transport")]
        [InlineData(0, "timeout")]
        [InlineData(408, "http")]
        [InlineData(429, "http")]
        [InlineData(500, "http")]
        [InlineData(502, "html")]
        [InlineData(503, "http")]
        [InlineData(504, "empty")]
        public void ShouldCapture_SkipsTransientFailuresTheQueueReplays(int httpStatus, string failureKind) {
            Assert.True(ScrobbleStartFailure.IsTransient(httpStatus, failureKind));
            Assert.False(ScrobbleStartFailure.ShouldCapture(3, false, httpStatus, null, failureKind));
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(0, "exception")]
        [InlineData(200, "json")]
        [InlineData(400, "http")]
        [InlineData(403, "http")]
        public void IsTransient_FalseForFailuresRetryingCannotFix(int httpStatus, string failureKind) {
            Assert.False(ScrobbleStartFailure.IsTransient(httpStatus, failureKind));
        }

        public static TheoryData<Exception, string> ClassifiedExceptions => new TheoryData<Exception, string> {
            { new TaskCanceledException(), "timeout" },
            { new TimeoutException(), "timeout" },
            { new HttpRequestException("send failed", new WebException("name not resolved")), "transport" },
            { new WebException("connection reset"), "transport" },
            { new IOException("unexpected EOF"), "transport" },
            { new SocketException(10054), "transport" },
            { new InvalidOperationException(), "exception" },
            { new ObjectDisposedException("HttpClient"), "exception" },
            { new NotSupportedException(), "exception" },
            { new NullReferenceException(), "exception" },
        };

        // The POST helper catches every exception, so "transport" has to mean the network.
        // Anything else is a plugin fault and must stay reportable.
        [Theory]
        [MemberData(nameof(ClassifiedExceptions))]
        public void ClassifyException_OnlyNetworkFaultsAreTransport(Exception ex, string expected) {
            Assert.Equal(expected, HttpCallDiagnostics.ClassifyException(ex));
        }

        [Theory]
        [InlineData("UNSUPPORTED_PLUGIN", true)]
        [InlineData("OPTED_OUT", true)]
        [InlineData("TOKEN_REQUIRED", true)]
        [InlineData("TOKEN_INVALID", true)]
        [InlineData("RATE_LIMITED", true)]
        [InlineData("INTERNAL_ERROR", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void IsExpectedRejection_MatchesConsentAuthAllowListAndThrottle(string code, bool expected) {
            Assert.Equal(expected, ScrobbleStartFailure.IsExpectedRejection(code));
        }

        [Fact]
        public void BuildExtras_CircuitSkip_ClassifiesAsCircuit() {
            var extras = ScrobbleStartFailure.BuildExtras(0, new HttpCallDiagnostics(), null);

            Assert.Equal("circuit", extras["failure_kind"]);
            Assert.Equal("0", extras["attempts"]);
            Assert.Equal("0", extras["retry_count"]);
            Assert.False(extras.ContainsKey("http_status"));
            Assert.False(extras.ContainsKey("exception_type"));
            Assert.False(extras.ContainsKey("game"));
        }

        [Fact]
        public void BuildExtras_AttachesGameNameWithoutTouchingMessage() {
            var extras = ScrobbleStartFailure.BuildExtras(
                1, new HttpCallDiagnostics { StatusCode = 400, FailureKind = "http" },
                "Fail", "Aliens: Fireteam Elite 2");

            Assert.Equal("Aliens: Fireteam Elite 2", extras["game"]);
            Assert.Equal("Fail", extras["outcome"]);
            Assert.DoesNotContain("Aliens", ScrobbleStartFailure.Message);
            Assert.DoesNotContain("Game:", ScrobbleStartFailure.Message);
        }

        [Fact]
        public void BuildExtras_IncludesHttpStatusOutcomeAndException() {
            var extras = ScrobbleStartFailure.BuildExtras(3, new HttpCallDiagnostics {
                StatusCode = 503,
                FailureKind = "timeout",
                ExceptionType = nameof(TimeoutException)
            }, "Error");

            Assert.Equal("timeout", extras["failure_kind"]);
            Assert.Equal("3", extras["attempts"]);
            Assert.Equal("2", extras["retry_count"]);
            Assert.Equal("503", extras["http_status"]);
            Assert.Equal(nameof(TimeoutException), extras["exception_type"]);
            Assert.Equal("Error", extras["outcome"]);
            foreach (var value in extras.Values) {
                Assert.DoesNotContain("Game:", value ?? "");
            }
        }

        [Fact]
        public void BuildExtras_HttpStatusWithoutKind_FallsBackToHttp() {
            var extras = ScrobbleStartFailure.BuildExtras(1, new HttpCallDiagnostics {
                StatusCode = 502
            }, null);

            Assert.Equal("http", extras["failure_kind"]);
            Assert.Equal("502", extras["http_status"]);
            Assert.False(extras.ContainsKey("response_content_type"));
        }

        [Fact]
        public void BuildExtras_ReportsErrorResponseContentType() {
            var extras = ScrobbleStartFailure.BuildExtras(1, new HttpCallDiagnostics {
                StatusCode = 403,
                FailureKind = "http",
                ResponseContentType = "text/html"
            }, null);

            Assert.Equal("403", extras["http_status"]);
            Assert.Equal("text/html", extras["response_content_type"]);
        }
    }
}
