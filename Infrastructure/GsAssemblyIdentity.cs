using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Every reference one of our own shipped assemblies declares across a major boundary from
        /// the version we actually ship, as "{simple name}/{requested version}".
        ///
        /// These exist because a coherent package set is not a version-aligned one: Sentry 6.1.0
        /// references System.Text.Json 8.0.0.5, System.Collections.Immutable 5.0.0.0 and
        /// System.Reflection.Metadata 5.0.0.0, PostHog references System.Text.Json 8.0.0.0, and the
        /// set we resolve to and test with is 9.0.0.9. Playnite ignores plugin-level binding
        /// redirects, so papering over exactly this skew is the whole reason the resolve handler
        /// exists.
        ///
        /// AssemblyResolveDriftTests keeps this list honest against the real build output: a package
        /// upgrade that introduces new skew fails that test instead of shipping an extension that
        /// cannot load.
        /// </summary>
        internal static readonly string[] KnownCrossMajorReferences = {
            "Microsoft.Bcl.AsyncInterfaces/8.0.0.0",
            "System.Collections.Immutable/5.0.0.0",
            "System.Reflection.Metadata/5.0.0.0",
            "System.Text.Json/8.0.0.0",
            "System.Text.Json/8.0.0.5",
        };

        private static readonly HashSet<string> CrossMajorLookup =
            new HashSet<string>(KnownCrossMajorReferences, StringComparer.OrdinalIgnoreCase);

        /// <summary>Key shape used by <see cref="KnownCrossMajorReferences"/>.</summary>
        internal static string ReferenceKey(string simpleName, Version requestedVersion) =>
            simpleName + "/" + requestedVersion;

        /// <summary>
        /// Whether an assembly shipped beside GsPlugin.dll may answer a resolve request.
        ///
        /// The handler is registered on Playnite's shared AppDomain, so it is asked to resolve every
        /// extension's failures, not only ours, and it cannot tell whose failure it is looking at.
        /// ResolveEventArgs.RequestingAssembly is null for every bind that matters here -- verified
        /// against the real shipped DLLs, not assumed -- because the CLR does not attribute a
        /// JIT-triggered load of a LoadFrom-context assembly. Deciding by requester was tried and
        /// does not work; do not reach for it again.
        ///
        /// So the policy is decided by the requested identity alone:
        ///
        /// Same major, and at least the requested build, is always served. That is the ordinary
        /// case and it is safe for anyone.
        ///
        /// Across a major it is served only for the handful of identities our own package set
        /// declares (<see cref="KnownCrossMajorReferences"/>). Those requests have to be served --
        /// refusing them is precisely what made 2.8.3 unloadable, since the FileNotFoundException
        /// escaped the GsPlugin constructor and took settings, opt-out and "Delete My Data" with it.
        /// Every other cross-major request is refused, which keeps the case the check was written
        /// for: an extension compiled against System.Text.Json 4.0.1.0 is not handed our 9.x to fail
        /// later with MissingMethodException, when a plain bind failure is the honest answer.
        ///
        /// A zero version is a partial-name bind, not a request for 0.0.0.0. The netstandard facade
        /// we ship declares its type-forward targets that way, so refusing them would brick the same
        /// startup a different way.
        ///
        /// The lower bound applies to everything. A candidate older than the request means the
        /// shipped set is wrong, and serving it only moves the failure somewhere less obvious.
        /// </summary>
        internal static bool CanServe(AssemblyName requested, AssemblyName candidate) {
            if (!PublicKeyTokensMatch(requested, candidate)) {
                return false;
            }
            if (requested.Version == null || candidate.Version == null) {
                return true;
            }
            if (requested.Version == EmptyVersion) {
                return true;
            }
            if (candidate.Version < requested.Version) {
                return false;
            }
            if (candidate.Version.Major == requested.Version.Major) {
                return true;
            }
            return CrossMajorLookup.Contains(ReferenceKey(requested.Name, requested.Version));
        }

        private static readonly Version EmptyVersion = new Version(0, 0, 0, 0);
    }
}
