using System;
using System.IO;
using System.Linq;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsPendingScrobbleStateTests {
        private static PendingScrobble Start(string game = "game-a", string plugin = "plugin-a") => new PendingScrobble {
            Type = "start",
            StartData = new ScrobbleStartReq { game_id = game, plugin_id = plugin },
            QueuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        private static PendingScrobble Finish(string game = "game-a", string plugin = "plugin-a", string session = null) => new PendingScrobble {
            Type = "finish",
            FinishData = new ScrobbleFinishReq { game_id = game, plugin_id = plugin, session_id = session },
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
        public void CompletePendingStart_PairsOnlyItsGameAndPluginAcrossInterleavedEvents() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var unrelatedGame = Finish("game-b");
                var unrelatedPluginStart = Start(plugin: "plugin-b");
                var unrelatedPluginFinish = Finish(plugin: "plugin-b");
                var matchingFinish = Finish();
                Queue(start, unrelatedGame, unrelatedPluginStart, unrelatedPluginFinish, matchingFinish);

                Assert.True(GsDataManager.CompletePendingStart(start, "replayed-session"));

                Assert.Equal("replayed-session", matchingFinish.FinishData.session_id);
                Assert.Null(unrelatedGame.FinishData.session_id);
                Assert.Null(unrelatedPluginFinish.FinishData.session_id);
                Assert.DoesNotContain(start, GsDataManager.Data.PendingScrobbles);
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal("replayed-session", GsDataManager.Data.PendingScrobbles.Last().FinishData.session_id);
                Assert.Equal(4, GsDataManager.Data.PendingScrobbles.Count);
            }
        }

        [Fact]
        public void CompletePendingStart_NeverPairsAcrossTheNextLaunch() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var firstStart = Start();
                var secondStart = Start();
                var secondFinish = Finish();
                Queue(firstStart, secondStart, secondFinish);

                Assert.True(GsDataManager.CompletePendingStart(firstStart, "first-session"));
                Assert.Null(secondFinish.FinishData.session_id);
                Assert.True(GsDataManager.HasPendingStart("game-a"));
                Assert.False(GsDataManager.HasActiveSession("game-a"));

                Assert.True(GsDataManager.CompletePendingStart(secondStart, "second-session"));
                Assert.Equal("second-session", secondFinish.FinishData.session_id);
                Assert.False(GsDataManager.HasPendingStart("game-a"));
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal("second-session", Assert.Single(GsDataManager.PeekPendingScrobbles()).FinishData.session_id);
            }
        }

        [Fact]
        public void CompletePendingStart_PersistsSessionForAStopThatHasNotArrivedYet() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                Queue(start);

                Assert.True(GsDataManager.CompletePendingStart(start, "active-session"));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal("active-session", GsDataManager.Data.ActiveSessionsByGameId["game-a"]);
                Assert.False(GsDataManager.HasPendingStart("game-a"));
                Assert.Empty(GsDataManager.PeekPendingScrobbles());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CompletePendingStart_AcceptedQueuedResponseSupportsMissingSessionId(bool hasQueuedFinish) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                if (hasQueuedFinish) Queue(start, Finish());
                else Queue(start);

                Assert.True(GsDataManager.CompletePendingStart(start, null));
                GsDataManager.Initialize(temp.Path, null);

                Assert.False(GsDataManager.HasActiveSession("game-a"));
                Assert.Equal(!hasQueuedFinish, GsDataManager.HasPendingStart("game-a"));
                if (hasQueuedFinish) {
                    var finish = Assert.Single(GsDataManager.PeekPendingScrobbles());
                    Assert.Equal("finish", finish.Type);
                    Assert.Null(finish.FinishData.session_id);
                }
                else {
                    Assert.Empty(GsDataManager.PeekPendingScrobbles());
                }
            }
        }

        [Fact]
        public void CompletePendingStart_DoesNotOverwriteAnAlreadyBoundFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var finish = Finish(session: "already-bound-session");
                Queue(start, finish);

                Assert.True(GsDataManager.CompletePendingStart(start, "new-response-session"));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal("already-bound-session", Assert.Single(GsDataManager.PeekPendingScrobbles()).FinishData.session_id);
            }
        }

        [Theory]
        [InlineData("finished-session", false)]
        [InlineData("newer-session", true)]
        public void CompletePendingScrobble_ClearsOnlyTheMatchingActiveSession(string activeSession, bool remainsActive) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var finish = Finish(session: "finished-session");
                Queue(finish);
                GsDataManager.MutateAndSave(d => d.ActiveSessionsByGameId["game-a"] = activeSession);

                Assert.True(GsDataManager.CompletePendingScrobble(finish));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Empty(GsDataManager.PeekPendingScrobbles());
                Assert.Equal(remainsActive, GsDataManager.HasActiveSession("game-a"));
                if (remainsActive) Assert.Equal(activeSession, GsDataManager.Data.ActiveSessionsByGameId["game-a"]);
            }
        }

        [Fact]
        public void DropPendingScrobble_DropsOnlyTheRejectedLaunchAndItsFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var firstStart = Start();
                var firstFinish = Finish();
                var nextStart = Start();
                var nextFinish = Finish();
                var unrelatedFinish = Finish("game-b", session: "unrelated-session");
                Queue(firstStart, unrelatedFinish, firstFinish, nextStart, nextFinish);

                Assert.True(GsDataManager.DropPendingScrobble(firstStart));

                Assert.Equal(new[] { unrelatedFinish, nextStart, nextFinish }, GsDataManager.PeekPendingScrobbles());
                Assert.Equal(2, GsDataManager.Data.DroppedScrobbleCount);
                Assert.True(GsDataManager.HasPendingStart("game-a"));
                Assert.True(GsDataManager.DropPendingScrobble(nextStart));
                Assert.False(GsDataManager.HasPendingStart("game-a"));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal("unrelated-session", Assert.Single(GsDataManager.PeekPendingScrobbles()).FinishData.session_id);
                Assert.Equal(4, GsDataManager.Data.DroppedScrobbleCount);
            }
        }

        [Fact]
        public void PeekPendingScrobbles_StopsAtClaimedWorkAndClaimsDoNotSurviveRestart() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var beforeClaim = Finish("game-b", session: "earlier-session");
                var claimedStart = Start();
                var laterFinish = Finish();
                Queue(beforeClaim, claimedStart, laterFinish);
                GsDataManager.ClaimPendingScrobble(claimedStart);

                Assert.Same(beforeClaim, Assert.Single(GsDataManager.PeekPendingScrobbles()));
                GsDataManager.ReleasePendingScrobble(claimedStart);
                Assert.Equal(new[] { beforeClaim, claimedStart, laterFinish }, GsDataManager.PeekPendingScrobbles());
                GsDataManager.ClaimPendingScrobble(claimedStart);
                GsDataManager.Save();

                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal(3, GsDataManager.PeekPendingScrobbles().Count);
                Assert.Equal("start", GsDataManager.PeekPendingScrobbles()[1].Type);
            }
        }

        [Fact]
        public void HasEarlierPendingScrobble_OrdersOnlyTheSameGameAndPlugin() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var otherGame = Start("game-b");
                var otherPlugin = Start(plugin: "plugin-b");
                var start = Start();
                var finish = Finish();
                Queue(otherGame, otherPlugin, start, finish);

                Assert.False(GsDataManager.HasEarlierPendingScrobble(start));
                Assert.True(GsDataManager.HasEarlierPendingScrobble(finish));
                Assert.True(GsDataManager.CompletePendingStart(start, "paired-session"));
                Assert.False(GsDataManager.HasEarlierPendingScrobble(finish));
            }
        }

        [Fact]
        public void CompletionAfterOptOutAndOptInCannotRestoreOldQueueOrSessions() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var finish = Finish();
                Queue(start, finish);
                GsDataManager.PerformOptOut();
                Assert.False(GsDataManager.CompletePendingStart(start, "stale-session"));
                Assert.False(GsDataManager.CompletePendingScrobble(finish));
                Assert.False(GsDataManager.DropPendingScrobble(start));
                GsDataManager.PerformOptIn();

                Assert.False(GsDataManager.CompletePendingStart(start, "stale-session"));
                Assert.False(GsDataManager.CompletePendingScrobble(finish));
                Assert.False(GsDataManager.DropPendingScrobble(start));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Empty(GsDataManager.PeekPendingScrobbles());
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
                Assert.Empty(GsDataManager.Data.PendingStartGameIds);
                Assert.Equal(0, GsDataManager.Data.DroppedScrobbleCount);
            }
        }

        [Theory]
        [InlineData("start")]
        [InlineData("finish")]
        [InlineData("drop")]
        public void CompletionWriteFailure_RestoresPairingClaimsAndCollectionsWithoutNotifying(string operation) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = Start();
                var finish = Finish(session: operation == "finish" ? "completed-session" : null);
                if (operation == "finish") Queue(finish);
                else Queue(start, finish);
                GsDataManager.MutateAndSave(d => d.ActiveSessionsByGameId["game-a"] = "completed-session");
                var target = operation == "finish" ? finish : start;
                GsDataManager.ClaimPendingScrobble(target);
                var before = GsDataManager.Data;
                var previousQueue = before.PendingScrobbles;
                var previousSessions = before.ActiveSessionsByGameId;
                var previousMarkers = before.PendingStartGameIds;
                var originalFinishSession = finish.FinishData.session_id;
                var path = Path.Combine(temp.Path, "gs_data.json");
                var originalBytes = File.ReadAllBytes(path);
                Func<bool> complete = () => operation == "start"
                    ? GsDataManager.CompletePendingStart(start, "replayed-session")
                    : operation == "finish"
                        ? GsDataManager.CompletePendingScrobble(finish)
                        : GsDataManager.DropPendingScrobble(start);
                var notifications = 0;
                EventHandler handler = (sender, args) => notifications++;
                GsDataManager.DiagnosticsStateChanged += handler;
                try {
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        Assert.False(complete());

                        Assert.Same(before, GsDataManager.Data);
                        Assert.Same(previousQueue, GsDataManager.Data.PendingScrobbles);
                        Assert.Same(previousSessions, GsDataManager.Data.ActiveSessionsByGameId);
                        Assert.Same(previousMarkers, GsDataManager.Data.PendingStartGameIds);
                        Assert.Equal(originalFinishSession, finish.FinishData.session_id);
                        Assert.Equal("completed-session", GsDataManager.Data.ActiveSessionsByGameId["game-a"]);
                        Assert.Empty(GsDataManager.PeekPendingScrobbles());
                        Assert.Equal(0, GsDataManager.Data.DroppedScrobbleCount);
                        Assert.Equal(0, notifications);
                        Assert.Equal(originalBytes, File.ReadAllBytes(path));
                    }

                    Assert.True(complete());
                    Assert.Equal(1, notifications);
                    GsDataManager.Initialize(temp.Path, null);
                    if (operation == "start") {
                        Assert.Equal("replayed-session", Assert.Single(GsDataManager.PeekPendingScrobbles()).FinishData.session_id);
                    }
                    else {
                        Assert.Empty(GsDataManager.PeekPendingScrobbles());
                    }
                    Assert.Equal(operation == "drop" ? 2 : 0, GsDataManager.Data.DroppedScrobbleCount);
                }
                finally {
                    GsDataManager.DiagnosticsStateChanged -= handler;
                    GsDataManager.ReleasePendingScrobble(target);
                }
            }
        }
    }
}
