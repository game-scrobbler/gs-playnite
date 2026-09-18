using System;
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
        [InlineData(0, false, 0, null, false)]
        [InlineData(0, true, 0, null, false)]
        [InlineData(1, true, 0, null, false)]
        [InlineData(3, true, 0, null, false)]
        [InlineData(1, false, 0, null, true)]
        [InlineData(3, false, 0, null, true)]
        [InlineData(1, false, 429, null, false)]
        [InlineData(1, false, 403, "OPTED_OUT", false)]
        [InlineData(1, false, 403, null, true)]
        public void ShouldCapture_OnlyLivePathAfterAnAttempt(
            int attempts, bool isFlushRetry, int httpStatus, string code, bool expected) {
            Assert.Equal(expected, ScrobbleStartFailure.ShouldCapture(attempts, isFlushRetry, httpStatus, code));
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
        }
    }
}
