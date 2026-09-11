using System;
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

        private const string Stj = "cc7b13ffcd2ddd51";

        private static AssemblyName Named(string version, string token = Stj, string name = "System.Text.Json") =>
            new AssemblyName(name + ", Version=" + version + ", Culture=neutral, PublicKeyToken=" + token);

        // The 2.8.3 regression, pinned. Sentry 6.1.0 references System.Text.Json 8.0.0.5 and the
        // package set we ship resolves to 9.0.0.9. Refusing that request threw
        // FileNotFoundException out of the GsPlugin constructor, so the extension did not load at
        // all -- no settings, no opt-out, no "Delete My Data".
        [Theory]
        [InlineData("System.Text.Json", "8.0.0.5", "9.0.0.9")]
        [InlineData("System.Text.Json", "8.0.0.0", "9.0.0.9")]
        [InlineData("Microsoft.Bcl.AsyncInterfaces", "8.0.0.0", "9.0.0.9")]
        public void CanServe_CrossMajorReferenceFromOurOwnSet_IsServed(string name, string want, string have) {
            Assert.True(GsAssemblyIdentity.CanServe(Named(want, name: name), Named(have, name: name)));
        }

        // Same, for the two Sentry pulls in with the other signing key.
        [Theory]
        [InlineData("System.Collections.Immutable")]
        [InlineData("System.Reflection.Metadata")]
        public void CanServe_CrossMajorReferenceFromSentry_IsServed(string name) {
            const string Ecma = "b03f5f7f11d50a3a";
            Assert.True(GsAssemblyIdentity.CanServe(
                Named("5.0.0.0", Ecma, name), Named("9.0.0.9", Ecma, name)));
        }

        // The behaviour the major check was added for, which must survive the fix: a foreign
        // extension compiled against 4.x must not be handed our 9.x. 4.0.1.0 is not a version
        // anything in our own set asks for, so it stays refused.
        [Fact]
        public void CanServe_CrossMajorRequestOutsideOurSet_IsRefused() {
            Assert.False(GsAssemblyIdentity.CanServe(Named("4.0.1.0"), Named("9.0.0.9")));
        }

        [Fact]
        public void CanServe_SameMajor_IsServed() {
            Assert.True(GsAssemblyIdentity.CanServe(Named("9.0.0.1"), Named("9.0.0.9")));
        }

        // The lower bound is not relaxed by the cross-major list: a candidate older than the
        // request means the shipped set is wrong, and serving it hides that somewhere worse.
        [Theory]
        [InlineData("9.0.0.9", "9.0.0.1")]
        [InlineData("8.0.0.5", "8.0.0.1")]
        public void CanServe_CandidateOlderThanRequested_IsRefused(string want, string have) {
            Assert.False(GsAssemblyIdentity.CanServe(Named(want), Named(have)));
        }

        // Signing identity still gates everything; being on the cross-major list does not buy
        // past it.
        [Fact]
        public void CanServe_ListedIdentityWithDifferentToken_IsRefused() {
            Assert.False(GsAssemblyIdentity.CanServe(
                Named("8.0.0.5"), Named("9.0.0.9", "b03f5f7f11d50a3a")));
        }

        // The netstandard facade we ship declares its type-forward targets with a zero version.
        // That is a partial-name bind, not a request for 0.0.0.0, and refusing it bricks startup
        // the same way the 2.8.3 rule did.
        [Fact]
        public void CanServe_ZeroVersionRequest_IsServed() {
            Assert.True(GsAssemblyIdentity.CanServe(
                Named("0.0.0.0", name: "System.ValueTuple"), Named("4.0.5.0", name: "System.ValueTuple")));
        }

        [Fact]
        public void ReferenceKey_MatchesTheListedShape() {
            Assert.Contains(
                GsAssemblyIdentity.ReferenceKey("System.Text.Json", new Version(8, 0, 0, 5)),
                GsAssemblyIdentity.KnownCrossMajorReferences);
        }
    }
}
