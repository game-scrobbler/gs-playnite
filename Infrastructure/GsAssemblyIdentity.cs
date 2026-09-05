using System;
using System.Linq;
using System.Reflection;

namespace GsPlugin.Infrastructure {
    /// <summary>Identity checks used before resolving a dependency in Playnite's shared AppDomain.</summary>
    internal static class GsAssemblyIdentity {
        /// <summary>Unsigned identities match only other unsigned identities, never a signed candidate.</summary>
        internal static bool PublicKeyTokensMatch(AssemblyName requested, AssemblyName candidate) {
            var requestedToken = requested.GetPublicKeyToken() ?? Array.Empty<byte>();
            var candidateToken = candidate.GetPublicKeyToken() ?? Array.Empty<byte>();
            return requestedToken.SequenceEqual(candidateToken);
        }
    }
}
