using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GsPlugin.Infrastructure;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Guards the AssemblyResolve handler against the failure that made 2.8.3 unloadable.
    ///
    /// Playnite ignores plugin-level binding redirects, so every version skew inside the package
    /// set we ship has to be answered by the handler at runtime. The skew is not visible in
    /// source -- it comes out of NuGet resolution -- so the only honest check is to read the real
    /// build output and confirm the handler would serve every reference it contains.
    ///
    /// When a package upgrade introduces new skew, this test fails with the exact identity to add
    /// to <see cref="GsAssemblyIdentity.KnownCrossMajorReferences"/>. That is the whole point: the
    /// alternative is finding out from a release that cannot load, which is what happened.
    /// </summary>
    public class AssemblyResolveDriftTests {
        private sealed class Reference {
            public string From;
            public AssemblyName Requested;
            public AssemblyName Shipped;
            public string Key => GsAssemblyIdentity.ReferenceKey(Requested.Name, Requested.Version);
            public override string ToString() =>
                $"{From} -> {Requested.Name} {Requested.Version} (shipped {Shipped.Version})";
        }

        private static readonly Version ZeroVersion = new Version(0, 0, 0, 0);

        /// <summary>
        /// Every reference a shipped assembly makes to another shipped assembly. References to
        /// assemblies we do not ship are irrelevant: the handler declines those before any version
        /// check, because there is no file beside GsPlugin.dll to serve.
        /// </summary>
        private static List<Reference> ReadShippedReferences(string outputDirectory) {
            var shipped = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(outputDirectory, "*.dll")) {
                try {
                    var name = AssemblyName.GetAssemblyName(file);
                    shipped[name.Name] = name;
                }
                catch (BadImageFormatException) {
                    // Native payloads such as SQLite.Interop.dll. Never resolved as managed.
                }
                catch (FileLoadException) {
                }
            }

            var references = new List<Reference>();
            foreach (var file in Directory.GetFiles(outputDirectory, "*.dll")) {
                Assembly assembly;
                try {
                    assembly = Assembly.ReflectionOnlyLoadFrom(file);
                }
                catch (BadImageFormatException) {
                    continue;
                }
                catch (FileLoadException) {
                    continue;
                }

                foreach (var requested in assembly.GetReferencedAssemblies()) {
                    if (!shipped.TryGetValue(requested.Name, out var match)) {
                        continue;
                    }
                    references.Add(new Reference {
                        From = assembly.GetName().Name,
                        Requested = requested,
                        Shipped = match,
                    });
                }
            }
            return references;
        }

        /// <summary>
        /// The handler must be able to answer every reference inside the set we ship. A refusal
        /// here is a FileNotFoundException escaping the GsPlugin constructor on a user's machine.
        /// </summary>
        [Fact]
        public void EveryReferenceWithinTheShippedSetCanBeServed() {
            var references = ReadShippedReferences(FindPluginOutputDirectory());

            var refused = references
                .Where(r => !GsAssemblyIdentity.CanServe(r.Requested, r.Shipped))
                .Select(r => r.ToString())
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Assert.True(refused.Count == 0,
                "The AssemblyResolve handler would refuse references made inside our own shipped "
                + "set, which is what made 2.8.3 unloadable. Add each identity below to "
                + "GsAssemblyIdentity.KnownCrossMajorReferences, or realign the package versions:"
                + Environment.NewLine + string.Join(Environment.NewLine, refused));
        }

        /// <summary>
        /// A reference to a version newer than the one we ship is a broken package set. The
        /// handler refuses it by design (serving an older assembly only moves the failure), so it
        /// has to be caught here rather than at runtime.
        /// </summary>
        [Fact]
        public void NoShippedAssemblyReferencesANewerVersionThanWeShip() {
            var references = ReadShippedReferences(FindPluginOutputDirectory());

            var tooOld = references
                .Where(r => r.Requested.Version != null
                            && r.Requested.Version != ZeroVersion
                            && r.Shipped.Version < r.Requested.Version)
                .Select(r => r.ToString())
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Assert.True(tooOld.Count == 0,
                "We ship an assembly older than something else in the set was built against:"
                + Environment.NewLine + string.Join(Environment.NewLine, tooOld));
        }

        /// <summary>
        /// Stale entries are not harmless: each one widens the set of foreign requests we answer
        /// across a major boundary, which is the case the version check exists to refuse.
        /// </summary>
        [Fact]
        public void EveryListedCrossMajorReferenceIsStillReal() {
            var references = ReadShippedReferences(FindPluginOutputDirectory());

            var live = new HashSet<string>(
                references
                    .Where(r => r.Requested.Version != null
                                && r.Requested.Version != ZeroVersion
                                && r.Shipped.Version != null
                                && r.Shipped.Version.Major != r.Requested.Version.Major)
                    .Select(r => r.Key),
                StringComparer.OrdinalIgnoreCase);

            var stale = GsAssemblyIdentity.KnownCrossMajorReferences
                .Where(entry => !live.Contains(entry))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            Assert.True(stale.Count == 0,
                "GsAssemblyIdentity.KnownCrossMajorReferences lists identities nothing in the "
                + "shipped set asks for any more. Remove them:"
                + Environment.NewLine + string.Join(Environment.NewLine, stale));
        }

        /// <summary>
        /// The configuration these tests were compiled in, which is the only plugin output they
        /// may read. Taken from the compiler rather than sniffed out of the binary's path so it
        /// cannot be wrong.
        /// </summary>
#if DEBUG
        private const string BuildConfiguration = "Debug";
#else
        private const string BuildConfiguration = "Release";
#endif

        /// <summary>
        /// Walks up from the test binary to the repository root and returns the plugin output for
        /// this build's configuration, and only that one. Falling back to the other configuration
        /// would let a stale build satisfy the check, which defeats the point of reading the real
        /// output. Missing output fails loudly rather than skipping: a silent pass here would mean
        /// the check is not running at all.
        /// </summary>
        private static string FindPluginOutputDirectory() {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var directory = new DirectoryInfo(baseDirectory);
            string root = null;
            for (var depth = 0; directory != null && depth < 8; depth++) {
                if (File.Exists(Path.Combine(directory.FullName, "GsPlugin.csproj"))) {
                    root = directory.FullName;
                    break;
                }
                directory = directory.Parent;
            }
            Assert.True(root != null, "Could not locate the repository root from " + baseDirectory);

            var output = Path.Combine(root, "bin", BuildConfiguration);
            if (File.Exists(Path.Combine(output, "GsPlugin.dll"))) {
                return output;
            }

            throw new Xunit.Sdk.XunitException(
                "No " + BuildConfiguration + " plugin build output at " + output
                + ". Build the plugin with MSBuild in that configuration before running the tests.");
        }
    }
}
