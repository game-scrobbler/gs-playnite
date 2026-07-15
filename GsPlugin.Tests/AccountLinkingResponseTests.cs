using Xunit;
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
}
