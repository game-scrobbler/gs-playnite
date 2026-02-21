using Xunit;
using GsPlugin.Services;

namespace GsPlugin.Tests {
    public class ValidateTokenTests {
        [Fact]
        public void NullToken_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken(null));
        }

        [Fact]
        public void EmptyToken_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken(""));
        }

        [Fact]
        public void WhitespaceToken_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken("   "));
        }

        [Fact]
        public void SingleChar_ReturnsTrue() {
            // No minimum length constraint — short tokens are allowed
            Assert.True(GsAccountLinkingService.ValidateToken("a"));
        }

        [Fact]
        public void MaxLength512_ReturnsTrue() {
            var token = new string('a', 512);
            Assert.True(GsAccountLinkingService.ValidateToken(token));
        }

        [Fact]
        public void TooLong_ReturnsFalse() {
            var token = new string('a', 513);
            Assert.False(GsAccountLinkingService.ValidateToken(token));
        }

        [Fact]
        public void ValidAlphanumeric_ReturnsTrue() {
            Assert.True(GsAccountLinkingService.ValidateToken("abc123XYZ789"));
        }

        [Fact]
        public void ValidWithHyphensAndUnderscores_ReturnsTrue() {
            Assert.True(GsAccountLinkingService.ValidateToken("a1b2-c3d_4e5f"));
        }

        [Fact]
        public void ValidWithDotsAndSlashes_ReturnsTrue() {
            // JWT and base64 tokens may contain dots, plus, equals, slashes
            Assert.True(GsAccountLinkingService.ValidateToken("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc123"));
        }

        [Fact]
        public void ValidWithBase64Chars_ReturnsTrue() {
            Assert.True(GsAccountLinkingService.ValidateToken("dGVzdA=="));
        }

        [Fact]
        public void ValidWithPlusSign_ReturnsTrue() {
            Assert.True(GsAccountLinkingService.ValidateToken("abc+def+ghi"));
        }

        [Fact]
        public void ContainsSpaces_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken("abc def ghij"));
        }

        [Fact]
        public void ContainsAtSign_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken("abc@def#ghij"));
        }

        [Fact]
        public void ContainsAngleBrackets_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken("abc<script>def"));
        }

        [Fact]
        public void ContainsSemicolon_ReturnsFalse() {
            Assert.False(GsAccountLinkingService.ValidateToken("abc;def;ghij"));
        }

        [Theory]
        [InlineData("abcdefghij")]       // lowercase only
        [InlineData("ABCDEFGHIJ")]       // uppercase only
        [InlineData("0123456789")]       // digits only
        [InlineData("----------")]       // hyphens only
        [InlineData("__________")]       // underscores only
        [InlineData("aB3-cD5_eF")]       // mixed valid chars
        [InlineData("a.b.c.d.e.")]       // dots
        [InlineData("a+b=c/d+e=")]       // base64 chars
        public void ValidCharacterSets_ReturnsTrue(string token) {
            Assert.True(GsAccountLinkingService.ValidateToken(token));
        }
    }
}
