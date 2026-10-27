using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GsPlugin.Infrastructure;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Pins the retry budget <see cref="GsAtomicFile"/> gives a transient Windows sharing
    /// violation.
    ///
    /// The budget used to be three attempts over 75 ms, which is not enough for the antivirus and
    /// indexer scans the retry exists for. Driving the shipped WriteJson through a save forced to
    /// fail followed by the save that has to succeed lost that race four times in four thousand
    /// rounds, every one of them "Unable to remove the file to be replaced". In the suite the same
    /// dice roll surfaced as one unrelated test failing per full run, a different one each time,
    /// because every assertion about persisted state rides on the save underneath it.
    /// </summary>
    public class GsAtomicFileRetryTests {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions();

        private sealed class Payload {
            public string Value { get; set; }
        }

        /// <summary>
        /// The give-up path through the real writer, which is what proves the policy is actually
        /// wired into WriteJson rather than only into the helper below: a lock that is never
        /// released must still end in an IOException, and only after the write has spent the
        /// budget trying. The elapsed-time bound is a lower bound, so load can only push it
        /// further from the threshold, never towards it.
        /// </summary>
        [Fact]
        public void WriteJson_LockHeldThroughout_ThrowsOnlyAfterSpendingTheRetryBudget() {
            using (var temp = TempPluginDir.Create()) {
                var path = Path.Combine(temp.Path, "gs_data.json");
                File.WriteAllText(path, "{}");

                var stopwatch = Stopwatch.StartNew();
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    Assert.Throws<IOException>(
                        () => GsAtomicFile.WriteJson(path, new Payload { Value = "new" }, Options));
                }
                stopwatch.Stop();

                // 20 + 40 + 80 + 160 + 320 = 620 ms of backoff across six attempts. Asserting well
                // under that keeps timer granularity out of it while still failing loudly if the
                // budget is ever cut back towards the 75 ms that lost saves.
                Assert.True(stopwatch.ElapsedMilliseconds >= 500,
                    $"Gave up after only {stopwatch.ElapsedMilliseconds} ms; the retry budget is "
                    + "too thin to survive an antivirus scan.");
            }
        }

        /// <summary>
        /// A lock that clears partway through must be ridden out rather than reported as a failed
        /// save. Injected rather than timed against a real file handle: whether the writer or the
        /// thread holding the file wins a given moment is not something a test can schedule, and
        /// the timed version of this passed against the old 75 ms policy while taking 600 ms,
        /// which is a test that discriminates nothing and flakes on a loaded machine.
        /// </summary>
        [Fact]
        public void WithRetry_TransientFailuresThenSuccess_IsRiddenOut() {
            var attempts = 0;
            GsAtomicFile.WithRetry(() => {
                if (++attempts < 4) {
                    throw new IOException("The process cannot access the file");
                }
            });
            Assert.Equal(4, attempts);
        }

        /// <summary>
        /// The budget is six attempts, and the last one's exception reaches the caller so
        /// GsDataManager can roll the mutation back instead of believing a lost write succeeded.
        /// </summary>
        [Fact]
        public void WithRetry_FailureThroughout_TriesSixTimesThenRethrows() {
            var attempts = 0;
            Assert.Throws<IOException>(() => GsAtomicFile.WithRetry(() => {
                attempts++;
                throw new IOException("The process cannot access the file");
            }));
            Assert.Equal(6, attempts);
        }

        /// <summary>
        /// Only a sharing violation is worth retrying. Anything else is a real fault that must
        /// surface on the first attempt rather than being retried six times.
        /// </summary>
        [Fact]
        public void WithRetry_NonIoFailure_IsNotRetried() {
            var attempts = 0;
            Assert.Throws<InvalidOperationException>(() => GsAtomicFile.WithRetry(() => {
                attempts++;
                throw new InvalidOperationException("not a lock");
            }));
            Assert.Equal(1, attempts);
        }

        /// <summary>
        /// The ordinary path stays fast. A retry policy that cost anything when nothing is locked
        /// would be paid on every game start and stop, under GsDataManager's process-wide lock.
        /// </summary>
        [Fact]
        public void WriteJson_NothingLocked_DoesNotPayForTheRetryPolicy() {
            using (var temp = TempPluginDir.Create()) {
                var path = Path.Combine(temp.Path, "gs_data.json");

                var stopwatch = Stopwatch.StartNew();
                for (var i = 0; i < 20; i++) {
                    GsAtomicFile.WriteJson(path, new Payload { Value = i.ToString() }, Options);
                }
                stopwatch.Stop();

                // Generous on purpose. The failure this guards against is the policy running when
                // nothing is locked, which would cost twenty times 620 ms, so a two second bound
                // separates it by a wide margin while leaving room for a machine whose antivirus
                // is scanning every one of these writes. A tight bound here would make this test
                // the very kind of load-sensitive flake it exists to prevent.
                Assert.True(stopwatch.ElapsedMilliseconds < 2000,
                    $"Twenty uncontended writes took {stopwatch.ElapsedMilliseconds} ms, which "
                    + "means the retry backoff is running on the uncontended path.");
                var written = JsonSerializer.Deserialize<Payload>(File.ReadAllText(path), Options);
                Assert.Equal("19", written.Value);
            }
        }
    }
}
