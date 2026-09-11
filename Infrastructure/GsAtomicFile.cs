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
        /// Attempts allowed for a file operation that can hit a transient Windows sharing
        /// violation, and the backoff between them: 20, 40, 80, 160, 320 ms, about 620 ms in
        /// total.
        ///
        /// The previous budget was three attempts over 75 ms, which is not enough for the
        /// antivirus and indexer scans this retry exists for. Driving the shipped WriteJson
        /// through a save that is forced to fail and then the save that has to succeed lost that
        /// race four times in four thousand rounds, every one of them "Unable to remove the file
        /// to be replaced".
        /// That sequence leaves the .tmp freshly written and abandoned, so it provokes a scan far
        /// more often than an ordinary save does; treat the figure as the rate for a save retried
        /// after a failure, not as a field rate. GsDataManager.SaveInternal does report the
        /// failure to Sentry, but MutateAndSave and Save ignore its result, so the in-memory state
        /// is kept while the disk copy stays stale.
        ///
        /// The cost is paid only while a write is already failing, so the common path is
        /// unchanged. It is bounded on purpose: GsDataManager saves under a process-wide lock, so
        /// retrying forever would block every reader including the UI thread.
        /// </summary>
        private const int MaxAttempts = 6;

        private static int BackoffMs(int attempt) => 20 * (1 << (attempt - 1));

        /// <summary>
        /// Runs a file operation, retrying a transient sharing violation on the schedule above.
        /// The final attempt's IOException propagates: callers decide what a genuinely failed
        /// write means, and swallowing it here would hide it from all of them.
        ///
        /// Internal rather than private so the retry contract can be tested against an injected
        /// operation. Timing a real lock cannot pin it: whether the writer or the thread holding
        /// the file wins a given moment is not something a test can schedule, which makes such a
        /// test both non-discriminating and flaky.
        /// </summary>
        internal static void WithRetry(Action operation) {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
                try {
                    operation();
                    return;
                }
                catch (IOException) when (attempt < MaxAttempts) {
                    Thread.Sleep(BackoffMs(attempt));
                }
            }
        }

        /// <summary>
        /// Moves/replaces tempPath onto destPath, retrying on IOException. A just-written file can
        /// be transiently locked (antivirus/indexer scan) on Windows; without a retry that sharing
        /// violation surfaces as a failed save.
        /// </summary>
        public static void ReplaceWithRetry(string tempPath, string destPath) {
            WithRetry(() => {
                if (File.Exists(destPath)) {
                    File.Replace(tempPath, destPath, destinationBackupFileName: null);
                }
                else {
                    File.Move(tempPath, destPath);
                }
            });
        }

        /// <summary>
        /// Streams <paramref name="value"/> to a temp file (avoids allocating one giant string for
        /// large payloads) then atomically replaces the target with retry.
        /// </summary>
        /// <param name="durable">
        /// Forces the temp file's bytes onto the physical disk before the replace. File.Replace is
        /// atomic for the directory-entry swap, but without this the contents may still be sitting
        /// in the OS write cache, so a power loss can commit the rename over a truncated file, and
        /// RecoverTemp cannot help because the destination now exists.
        ///
        /// Off by default because it is a FlushFileBuffers-class syscall, and GsDataManager saves
        /// under a process-wide lock on every game start, game stop and queued-scrobble transition
        /// so paying a hardware commit there blocks every other reader, including the UI thread, and
        /// draining a backed-up queue pays it once per item. Reserve it for state that cannot be
        /// reconstructed by replaying work: install identity, tokens and consent.
        /// </param>
        public static void WriteJson<T>(string filePath, T value, JsonSerializerOptions options, bool durable = false) {
            var tempPath = filePath + ".tmp";
            // Opening the temp file gets the same retry as the replace below. A save that just
            // failed leaves this exact path freshly written and abandoned, which is precisely the
            // file a scanner is holding, so the open is exposed to the hazard the replace already
            // defends against. Only the replace has been observed losing the race; this is the
            // same defect class rather than a second measurement.
            WithRetry(() => {
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    JsonSerializer.Serialize(stream, value, options);
                    stream.Flush(flushToDisk: durable);
                }
            });
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
