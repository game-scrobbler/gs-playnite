using System;
using System.Linq;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    /// <summary>
    /// Covers the finish event carrying its own <c>started_at</c>.
    ///
    /// The point of the field is that a finish no longer depends on its start having
    /// been accepted: the server matches on (install, started_at, game), and failing
    /// that reconstructs the whole session from the finish alone. These tests pin the
    /// client half of that: where the start instant is recorded, how it reaches a
    /// queued finish, and the queue rule it relaxes.
    /// </summary>
    [Collection("StaticManagerTests")]
    public class GsSelfContainedFinishTests {
        private const string StartedAt = "2026-01-01T10:00:00+02:00";

        private static PendingScrobble Start(string game = "game-a", string plugin = "plugin-a",
            string startedAt = StartedAt) => new PendingScrobble {
                Type = "start",
                StartData = new ScrobbleStartReq { game_id = game, plugin_id = plugin, started_at = startedAt },
                QueuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

        private static PendingScrobble Finish(string game = "game-a", string plugin = "plugin-a",
            string session = null, string startedAt = null) => new PendingScrobble {
                Type = "finish",
                FinishData = new ScrobbleFinishReq {
                    game_id = game,
                    plugin_id = plugin,
                    session_id = session,
                    started_at = startedAt
                },
                QueuedAt = new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc)
            };

        private static void Queue(params PendingScrobble[] items) {
            GsDataManager.MutateAndSave(d => {
                d.PendingScrobbles.AddRange(items);
                foreach (var item in items.Where(p => p.Type == "start")) {
                    if (!d.PendingStartGameIds.Contains(item.StartData.game_id)) {
                        d.PendingStartGameIds.Add(item.StartData.game_id);
                    }
                }
            });
        }

        [Fact]
        public void SetAndRemoveActiveSession_KeepBothMapsInLockstep() {
            var data = new GsData();

            data.SetActiveSession("game-a", "session-1", StartedAt);
            Assert.Equal("session-1", data.ActiveSessionsByGameId["game-a"]);
            Assert.Equal(StartedAt, data.ActiveSessionStartsByGameId["game-a"]);

            data.RemoveActiveSession("game-a");
            Assert.Empty(data.ActiveSessionsByGameId);
            Assert.Empty(data.ActiveSessionStartsByGameId);
        }

        [Fact]
        public void SetActiveSession_WithoutAStartInstant_LeavesNoStaleEntry() {
            var data = new GsData();
            data.SetActiveSession("game-a", "session-1", StartedAt);

            // A session recorded without a start time must not inherit the previous
            // session's, and the finish would then report an interval that never happened.
            data.SetActiveSession("game-a", "session-2", null);

            Assert.Equal("session-2", data.ActiveSessionsByGameId["game-a"]);
            Assert.False(data.ActiveSessionStartsByGameId.ContainsKey("game-a"));
        }

        [Fact]
        public void ClearIdentityBoundState_ClearsRecordedStartInstants() {
            var data = new GsData();
            data.SetActiveSession("game-a", "session-1", StartedAt);

            data.ClearIdentityBoundState(IdentityClearScope.InstallToken);

            Assert.Empty(data.ActiveSessionStartsByGameId);
        }

        [Fact]
        public void CompletePendingStart_RecordsTheStartInstantAlongsideTheSession() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                Queue(start);

                Assert.True(GsDataManager.CompletePendingStart(start, "session-1"));

                Assert.Equal(StartedAt, GsDataManager.Data.ActiveSessionStartsByGameId["game-a"]);
                Assert.Equal(StartedAt, GsDataManager.ResolveSessionStart("game-a"));

                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal(StartedAt, GsDataManager.Data.ActiveSessionStartsByGameId["game-a"]);
            }
        }

        [Fact]
        public void CompletePendingStart_StampsTheStartInstantOntoItsPairedFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var pairedFinish = Finish();
                var otherGame = Finish("game-b");
                Queue(start, otherGame, pairedFinish);

                Assert.True(GsDataManager.CompletePendingStart(start, "session-1"));

                Assert.Equal(StartedAt, pairedFinish.FinishData.started_at);
                Assert.Null(otherGame.FinishData.started_at);
            }
        }

        [Fact]
        public void ResolveSessionStart_FallsBackToTheNewestQueuedStart() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                Queue(Start(startedAt: "2026-01-01T08:00:00+02:00"), Finish(), Start(startedAt: StartedAt));

                // No active session yet: both starts are still queued, and a finish
                // queued now belongs to the most recent launch.
                Assert.Equal(StartedAt, GsDataManager.ResolveSessionStart("game-a"));
                Assert.Null(GsDataManager.ResolveSessionStart("game-unknown"));
            }
        }

        [Fact]
        public void DropPendingScrobble_KeepsAPairedFinishThatCarriesItsOwnStartInstant() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var pairedFinish = Finish(startedAt: StartedAt);
                Queue(start, pairedFinish);

                Assert.True(GsDataManager.DropPendingScrobble(start));

                // The whole point: a start exhausting its retries used to take the
                // session's only surviving timestamp with it.
                Assert.Contains(pairedFinish, GsDataManager.Data.PendingScrobbles);
                Assert.DoesNotContain(start, GsDataManager.Data.PendingScrobbles);
                Assert.Equal(1, GsDataManager.Data.DroppedScrobbleCount);
            }
        }

        [Fact]
        public void DropPendingScrobble_StillDropsAPairedFinishThatCanSayNothingOnItsOwn() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var pairedFinish = Finish();
                Queue(start, pairedFinish);

                Assert.True(GsDataManager.DropPendingScrobble(start));

                // Neither a session id nor a start instant: replaying it could only
                // close whichever session the server guessed at.
                Assert.DoesNotContain(pairedFinish, GsDataManager.Data.PendingScrobbles);
                Assert.Equal(2, GsDataManager.Data.DroppedScrobbleCount);
            }
        }

        [Fact]
        public void QueueSessionFinishesAndClearActive_FillsInAKnownStartInstant() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => d.SetActiveSession("game-a", "session-1", StartedAt));
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;

                var shutdownFinish = Finish(session: "session-1");
                var sessions = GsDataManager.SnapshotActiveSessions();

                Assert.True(GsDataManager.QueueSessionFinishesAndClearActive(
                    sessions,
                    new System.Collections.Generic.List<PendingScrobble> { shutdownFinish },
                    installId,
                    generation));

                Assert.Equal(StartedAt, shutdownFinish.FinishData.started_at);
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
                Assert.Empty(GsDataManager.Data.ActiveSessionStartsByGameId);
            }
        }

        [Fact]
        public void ActiveSessionStarts_SurvivePersistenceRoundtrip() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => d.SetActiveSession("game-a", "session-1", StartedAt));

                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal("session-1", GsDataManager.Data.ActiveSessionsByGameId["game-a"]);
                Assert.Equal(StartedAt, GsDataManager.Data.ActiveSessionStartsByGameId["game-a"]);
            }
        }

        [Fact]
        public void FinishRequest_OmitsStartedAtWhenUnknown() {
            var req = new ScrobbleFinishReq {
                user_id = "install-1",
                game_id = "game-a",
                finished_at = "2026-01-01T11:00:00+02:00"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(req);

            // WhenWritingNull: an older queued finish must not start sending a null
            // field the server would have to special-case.
            Assert.DoesNotContain("started_at", json);
        }

        [Fact]
        public void FinishRequest_SerializesTheStartInstantVerbatim() {
            var req = new ScrobbleFinishReq {
                user_id = "install-1",
                game_id = "game-a",
                started_at = StartedAt,
                finished_at = "2026-01-01T11:00:00+02:00"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(req);
            var sent = System.Text.Json.JsonSerializer.Deserialize<ScrobbleFinishReq>(json);

            // The server matches this as an exact instant, so the offset the start
            // sent has to survive the round trip unaltered. Compared after decoding
            // because System.Text.Json escapes the plus sign on the wire.
            Assert.Equal(StartedAt, sent.started_at);
        }
    }
}
