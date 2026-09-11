using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    /// <summary>
    /// Tests for <see cref="GsAccountLinkingService.IsLinkedUserId"/> — the guard that prevents a
    /// verify response which succeeds but resolves to the "not_linked" sentinel (or an empty user id)
    /// from being reported as a successful link. Without it the settings UI showed
    /// "Successfully linked!" while the connection status stayed "Disconnected" and the website's
    /// linking page kept polling "not linked" (issue #54).
    /// </summary>
    public class AccountLinkingResponseTests {
        [Fact]
        public void RealUserId_IsLinked() {
            Assert.True(GsAccountLinkingService.IsLinkedUserId("user_abc123"));
        }

        [Fact]
        public void NotLinkedSentinel_IsNotLinked() {
            Assert.False(GsAccountLinkingService.IsLinkedUserId(GsData.NotLinkedValue));
        }

        [Fact]
        public void LiteralNotLinkedString_IsNotLinked() {
            // Guards against the sentinel value drifting away from the literal the server sends.
            Assert.False(GsAccountLinkingService.IsLinkedUserId("not_linked"));
        }

        [Fact]
        public void Null_IsNotLinked() {
            Assert.False(GsAccountLinkingService.IsLinkedUserId(null));
        }

        [Fact]
        public void Empty_IsNotLinked() {
            Assert.False(GsAccountLinkingService.IsLinkedUserId(""));
        }

        [Fact]
        public void Whitespace_IsNotLinked() {
            Assert.False(GsAccountLinkingService.IsLinkedUserId("   "));
        }
    }

    /// <summary>
    /// Tests for <see cref="GsAccountLinkingService.IsExpectedLinkingRejection"/> — the guard that
    /// keeps "this Playnite installation is already linked to another account" out of Sentry.
    /// The server answers that with a 409 and a human message but no errorCode, so it was captured
    /// as a warning and its wording became the issue title (GS-PLAYNITE-P7).
    /// </summary>
    public class ExpectedLinkingRejectionTests {
        [Fact]
        public void Conflict_IsExpected() {
            Assert.True(GsAccountLinkingService.IsExpectedLinkingRejection(
                new TokenVerificationRes { success = false, statusCode = 409 }));
        }

        // Only the conflict. A 500 is the server actually failing and still deserves an issue.
        [Theory]
        [InlineData(0)]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(429)]
        [InlineData(500)]
        [InlineData(503)]
        public void OtherStatuses_AreNotExpected(int statusCode) {
            Assert.False(GsAccountLinkingService.IsExpectedLinkingRejection(
                new TokenVerificationRes { success = false, statusCode = statusCode }));
        }

        // A transport failure yields no response at all, which is not an expected rejection.
        [Fact]
        public void NullResponse_IsNotExpected() {
            Assert.False(GsAccountLinkingService.IsExpectedLinkingRejection(null));
        }

        // Server wording must never reach the issue title again.
        [Fact]
        public void FailureMessage_CarriesNoServerWordingOrContext() {
            Assert.Equal("Account linking failed", GsAccountLinkingService.LinkingFailureMessage);
            Assert.DoesNotContain("{", GsAccountLinkingService.LinkingFailureMessage);
            Assert.NotEmpty(GsAccountLinkingService.LinkingFailureFingerprint);
        }

        // statusCode is ours, not the server's: it must never be read off the wire.
        [Fact]
        public void StatusCode_IsNotDeserializedFromTheBody() {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<TokenVerificationRes>(
                "{\"success\":false,\"statusCode\":418}");
            Assert.Equal(0, parsed.statusCode);
        }
    }
}
