using System;
using System.IO;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    /// <summary>
    /// Creates a unique temporary directory for a test and removes it on Dispose.
    /// Optionally initializes the static GsDataManager / GsSyncHashIndex singletons
    /// against that directory, which is the pairing several test classes need.
    ///
    /// Pick the factory that matches what the test actually exercises. Both managers
    /// are process-wide statics, so initializing one that a test did not previously
    /// initialize changes the ambient state the test runs against.
    /// </summary>
    internal sealed class TempPluginDir : IDisposable {
        /// <summary>Absolute path of the temporary directory.</summary>
        public string Path { get; }

        private TempPluginDir(string path) {
            Path = path;
        }

        /// <summary>
        /// Creates the directory only. No static manager is touched, so ambient
        /// GsDataManager / GsSyncHashIndex state is left exactly as it was.
        /// </summary>
        public static TempPluginDir Create(string prefix = null) {
            var name = (prefix ?? string.Empty) + Guid.NewGuid().ToString();
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
            Directory.CreateDirectory(path);
            return new TempPluginDir(path);
        }

        /// <summary>
        /// Creates the directory and points GsDataManager at it. When
        /// <paramref name="installToken"/> is non-null it is stored through
        /// SetInstallTokenIfActive, matching what the API client tests need.
        /// </summary>
        public static TempPluginDir CreateWithDataManager(string installToken = null) {
            var temp = Create();
            try {
                GsDataManager.Initialize(temp.Path, null);
                if (installToken != null) {
                    GsDataManager.SetInstallTokenIfActive(installToken);
                }
            }
            catch {
                temp.Dispose();
                throw;
            }
            return temp;
        }

        /// <summary>
        /// Creates the directory and initializes GsSyncHashIndex only, leaving the
        /// current GsDataManager identity in place.
        /// </summary>
        public static TempPluginDir CreateWithHashIndex() {
            var temp = Create();
            try {
                GsSyncHashIndex.Initialize(temp.Path);
            }
            catch {
                temp.Dispose();
                throw;
            }
            return temp;
        }

        /// <summary>
        /// Creates the directory, initializes GsDataManager, then GsSyncHashIndex.
        /// The order is load-bearing: the index validates itself against the identity
        /// generation held by GsDataManager.
        /// </summary>
        public static TempPluginDir CreateWithDataManagerAndHashIndex() {
            var temp = CreateWithDataManager();
            try {
                GsSyncHashIndex.Initialize(temp.Path);
            }
            catch {
                temp.Dispose();
                throw;
            }
            return temp;
        }

        public void Dispose() {
            try {
                if (Directory.Exists(Path)) {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException) {
                // A file still locked by the test (a SQLite handle, an antivirus scan)
                // must never fail an otherwise passing test.
            }
            catch (UnauthorizedAccessException) {
                // Same rationale as above for read-only or in-use files.
            }
        }
    }
}
