using System;
using System.IO;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;
using static GsPlugin.Api.GsApiClient;

namespace GsPlugin.Tests {
    /// <summary>
    /// Tests for flush retry semantics (FlushAttempts / re-queue) and
    /// the start-fail / stop pairing state (PendingStartGameId).
    ///
    /// Note: service-level tests that require Playnite SDK types (Game, OnGameStoppedEventArgs)
    /// are not feasible here because the test project does not have a direct Playnite.SDK reference.
    /// These tests verify the data-layer contracts that the service code depends on.
    /// </summary>
    [Collection("StaticManagerTests")]
    public class GsFlushAndPairingTests : IDisposable {
        private readonly string _tempDir;

        public GsFlushAndPairingTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
            GsDataManager.Initialize(_tempDir, null);
            // Start with clean state
            GsDataManager.Data.PendingScrobbles.Clear();
            GsDataManager.Data.PendingStartGameId = null;
            GsDataManager.Data.ActiveSessionId = null;
            GsDataManager.Save();
        }

        public void Dispose() {
            Directory.Delete(_tempDir, true);
        }

        #region FlushAttempts field

        [Fact]
        public void FlushAttempts_DefaultsToZero() {
            var item = new PendingScrobble { Type = "start", QueuedAt = DateTime.UtcNow };
            Assert.Equal(0, item.FlushAttempts);
        }

        [Fact]
        public void FlushAttempts_RoundtripsViaPersistence() {
            var item = new PendingScrobble {
                Type = "start",
                QueuedAt = DateTime.UtcNow,
                FlushAttempts = 3
            };
            GsDataManager.EnqueuePendingScrobble(item);

            // Re-initialize to force a load from disk
            GsDataManager.Initialize(_tempDir, null);

            Assert.Single(GsDataManager.Data.PendingScrobbles);
            Assert.Equal(3, GsDataManager.Data.PendingScrobbles[0].FlushAttempts);
        }

        [Fact]
        public void FlushAttempts_CanBeIncremented() {
            var item = new PendingScrobble { Type = "finish", QueuedAt = DateTime.UtcNow, FlushAttempts = 0 };
            item.FlushAttempts++;
            Assert.Equal(1, item.FlushAttempts);
            item.FlushAttempts++;
            Assert.Equal(2, item.FlushAttempts);
        }

        [Fact]
        public void FlushAttempts_PreservedAfterDequeueAndReEnqueue() {
            // Enqueue an item with FlushAttempts = 2
            GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                Type = "start",
                StartData = new ScrobbleStartReq { user_id = "u1", game_name = "G" },
                QueuedAt = DateTime.UtcNow,
                FlushAttempts = 2
            });

            // Dequeue (simulates what FlushPendingScrobblesAsync does)
            var dequeued = GsDataManager.DequeuePendingScrobbles();
            Assert.Single(dequeued);
            Assert.Equal(2, dequeued[0].FlushAttempts);

            // Increment and re-enqueue (simulates a failed flush)
            dequeued[0].FlushAttempts++;
            GsDataManager.EnqueuePendingScrobble(dequeued[0]);

            // Verify
            Assert.Single(GsDataManager.Data.PendingScrobbles);
            Assert.Equal(3, GsDataManager.Data.PendingScrobbles[0].FlushAttempts);

            // Verify survives persistence
            GsDataManager.Initialize(_tempDir, null);
            Assert.Single(GsDataManager.Data.PendingScrobbles);
            Assert.Equal(3, GsDataManager.Data.PendingScrobbles[0].FlushAttempts);
        }

        #endregion

        #region PendingStartGameId field

        [Fact]
        public void PendingStartGameId_DefaultsToNull() {
            Assert.Null(GsDataManager.Data.PendingStartGameId);
        }

        [Fact]
        public void PendingStartGameId_RoundtripsViaPersistence() {
            var gameId = Guid.NewGuid().ToString();
            GsDataManager.Data.PendingStartGameId = gameId;
            GsDataManager.Save();

            GsDataManager.Initialize(_tempDir, null);
            Assert.Equal(gameId, GsDataManager.Data.PendingStartGameId);
        }

        [Fact]
        public void PendingStartGameId_CanBeCleared() {
            GsDataManager.Data.PendingStartGameId = "game-123";
            GsDataManager.Save();

            GsDataManager.Data.PendingStartGameId = null;
            GsDataManager.Save();

            GsDataManager.Initialize(_tempDir, null);
            Assert.Null(GsDataManager.Data.PendingStartGameId);
        }

        #endregion

        #region Start-fail queue + paired finish queue pattern

        [Fact]
        public void QueuedStartAndFinish_ArePairedInOrder() {
            // Simulates what OnGameStartAsync does on failure + what OnGameStoppedAsync does when pairing
            var gameId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            // Queue a start (simulates OnGameStartAsync failure path)
            GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                Type = "start",
                StartData = new ScrobbleStartReq {
                    user_id = "u1",
                    game_name = "Test Game",
                    game_id = gameId,
                    started_at = now.ToString("yyyy-MM-ddTHH:mm:ssK")
                },
                QueuedAt = now
            });
            GsDataManager.Data.PendingStartGameId = gameId;
            GsDataManager.Save();

            // Queue a finish (simulates OnGameStoppedAsync pairing path)
            GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                Type = "finish",
                FinishData = new ScrobbleFinishReq {
                    user_id = "u1",
                    game_name = "Test Game",
                    game_id = gameId,
                    session_id = "queued",
                    finished_at = now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssK")
                },
                QueuedAt = now.AddMinutes(30)
            });
            GsDataManager.Data.PendingStartGameId = null;
            GsDataManager.Save();

            // Verify the queue has the correct order
            Assert.Equal(2, GsDataManager.Data.PendingScrobbles.Count);
            Assert.Equal("start", GsDataManager.Data.PendingScrobbles[0].Type);
            Assert.Equal("finish", GsDataManager.Data.PendingScrobbles[1].Type);
            Assert.Equal("queued", GsDataManager.Data.PendingScrobbles[1].FinishData.session_id);
            Assert.Null(GsDataManager.Data.PendingStartGameId);
        }

        [Fact]
        public void QueuedStartAndFinish_SurvivePersistenceRoundtrip() {
            var gameId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;

            GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                Type = "start",
                StartData = new ScrobbleStartReq {
                    user_id = "u1",
                    game_name = "Test",
                    game_id = gameId,
                    started_at = now.ToString("yyyy-MM-ddTHH:mm:ssK")
                },
                QueuedAt = now
            });
            GsDataManager.EnqueuePendingScrobble(new PendingScrobble {
                Type = "finish",
                FinishData = new ScrobbleFinishReq {
                    user_id = "u1",
                    game_name = "Test",
                    game_id = gameId,
                    session_id = "queued",
                    finished_at = now.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssK")
                },
                QueuedAt = now.AddMinutes(30)
            });

            // Reload from disk
            GsDataManager.Initialize(_tempDir, null);

            var items = GsDataManager.Data.PendingScrobbles;
            Assert.Equal(2, items.Count);
            Assert.Equal("start", items[0].Type);
            Assert.NotNull(items[0].StartData);
            Assert.Equal(gameId, items[0].StartData.game_id);
            Assert.Equal("finish", items[1].Type);
            Assert.NotNull(items[1].FinishData);
            Assert.Equal(gameId, items[1].FinishData.game_id);
            Assert.Equal("queued", items[1].FinishData.session_id);
        }

        #endregion
    }
}
