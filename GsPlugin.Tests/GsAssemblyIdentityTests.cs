using System.Reflection;
using GsPlugin.Infrastructure;
using Xunit;

namespace GsPlugin.Tests {
    public class GsAssemblyIdentityTests {
        [Theory]
        [InlineData(null, null, true)]
        [InlineData(null, "null", true)]
        [InlineData("null", null, true)]
        [InlineData("null", "null", true)]
        [InlineData(null, "b03f5f7f11d50a3a", false)]
        [InlineData("null", "b03f5f7f11d50a3a", false)]
        [InlineData("b03f5f7f11d50a3a", null, false)]
        [InlineData("b03f5f7f11d50a3a", "null", false)]
        [InlineData("b03f5f7f11d50a3a", "b03f5f7f11d50a3a", true)]
        [InlineData("b03f5f7f11d50a3a", "cc7b13ffcd2ddd51", false)]
        public void PublicKeyTokensMatch_RequiresExactSigningIdentity(string requestedToken, string candidateToken, bool expected) {
            var requested = new AssemblyName("Dependency" + (requestedToken == null ? "" : ", PublicKeyToken=" + requestedToken));
            var candidate = new AssemblyName("Dependency" + (candidateToken == null ? "" : ", PublicKeyToken=" + candidateToken));
            Assert.Equal(expected, GsAssemblyIdentity.PublicKeyTokensMatch(requested, candidate));
        }
    }
}
