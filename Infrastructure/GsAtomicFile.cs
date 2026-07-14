using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace GsPlugin.Infrastructure {
    /// <summary>
    /// Shared crash-safe single-file JSON persistence helpers: temp-file recovery, streaming
    /// writes, and replace-with-retry to survive transient Windows file locks (antivirus /
    /// indexer scans). Centralized here so the robustness (retry, recovery) lives in exactly one
    /// place instead of being re-implemented per store (GsDataManager, GsSyncHashIndex).
    /// </summary>
    internal static class GsAtomicFile {
        /// <summary>
        /// Recover from a crash between the temp write and the rename: if the destination is
        /// missing but its <c>.tmp</c> exists, promote the temp file — it holds the last
        /// successful write.
        /// </summary>
        public static void RecoverTemp(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                return;
            }
            var tempPath = filePath + ".tmp";
            if (!File.Exists(filePath) && File.Exists(tempPath)) {
                try {
                    File.Move(tempPath, filePath);
                    GsLogger.Info($"[GsAtomicFile] Recovered {Path.GetFileName(filePath)} from .tmp");
                }
                catch (Exception ex) {
                    GsLogger.Warn($"[GsAtomicFile] Failed to recover {Path.GetFileName(filePath)}.tmp: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Moves/replaces tempPath onto destPath, retrying briefly on IOException. A just-written
        /// file can be transiently locked (antivirus/indexer scan) on Windows; without a retry
        /// that sharing violation surfaces as a failed save.
        /// </summary>
        public static void ReplaceWithRetry(string tempPath, string destPath, int maxAttempts = 3) {
            for (var attempt = 1; attempt <= maxAttempts; attempt++) {
                try {
                    if (File.Exists(destPath)) {
                        File.Replace(tempPath, destPath, destinationBackupFileName: null);
                    }
                    else {
                        File.Move(tempPath, destPath);
                    }
                    return;
                }
                catch (IOException) when (attempt < maxAttempts) {
                    Thread.Sleep(25 * attempt);
                }
            }
        }

        /// <summary>
        /// Streams <paramref name="value"/> to a temp file (avoids allocating one giant string for
        /// large payloads) then atomically replaces the target with retry.
        /// </summary>
        public static void WriteJson<T>(string filePath, T value, JsonSerializerOptions options) {
            var tempPath = filePath + ".tmp";
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, value, options);
            }
            ReplaceWithRetry(tempPath, filePath);
        }

        /// <summary>Deserializes a JSON file; returns null (with a warning) on any read/parse error.</summary>
        public static T LoadJson<T>(string path, JsonSerializerOptions options) where T : class {
            try {
                using (var stream = File.OpenRead(path)) {
                    return JsonSerializer.Deserialize<T>(stream, options);
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsAtomicFile] Failed to load {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Best-effort delete; swallows and logs failures.</summary>
        public static void TryDelete(string path) {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                return;
            }
            try {
                File.Delete(path);
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsAtomicFile] Failed to delete {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }
}
