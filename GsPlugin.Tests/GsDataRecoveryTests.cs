using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsDataRecoveryTests {
        [Fact]
        public void QueueSessionFinishes_RejectsSnapshotFromAnEarlierIdentity() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var originalId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                var sessions = new Dictionary<string, string> { ["game"] = "old-session" };
                var finishes = new List<PendingScrobble> {
                    new PendingScrobble { Type = "finish", FinishData = new ScrobbleFinishReq { session_id = "old-session" } }
                };
                GsDataManager.RotateInstallId();
                Assert.False(GsDataManager.QueueSessionFinishesAndClearActive(sessions, finishes, originalId, generation));
                GsDataManager.Initialize(temp.Path, null);
                Assert.Empty(GsDataManager.Data.PendingScrobbles);
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
            }
        }

        private static string DataPath(TempPluginDir temp) => Path.Combine(temp.Path, "gs_data.json");

        private static void AssertDataUnavailable() {
            Assert.Null(GsDataManager.DataOrNull);
            Assert.Throws<InvalidOperationException>(() => { var unused = GsDataManager.Data; });
        }

        [Theory]
        [InlineData("{\"InstallID\":\"saved-install\",\"OptedOut\":true,")]
        [InlineData("null")]
        [InlineData("[]")]
        public void Initialize_InvalidExistingJson_PreservesFileAndAllowsRepair(string invalidJson) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var originalId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                var path = DataPath(temp);
                File.WriteAllText(path, invalidJson);
                var originalBytes = File.ReadAllBytes(path);

                Assert.Throws<JsonException>(() => GsDataManager.Initialize(temp.Path, null));
                AssertDataUnavailable();
                GsDataManager.Save();

                Assert.Equal(originalBytes, File.ReadAllBytes(path));
                Assert.False(File.Exists(path + ".tmp"));
                File.WriteAllText(path, JsonSerializer.Serialize(new GsData {
                    InstallID = originalId,
                    IdentityGeneration = generation,
                    OptedOut = true,
                    LinkedUserId = "synthetic-saved-user"
                }));

                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal(originalId, GsDataManager.Data.InstallID);
                Assert.Equal(generation, GsDataManager.Data.IdentityGeneration);
                Assert.True(GsDataManager.IsOptedOut);
                Assert.Equal("synthetic-saved-user", GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public void Initialize_ReadLockedExistingFile_DoesNotReplaceIdentityOrConsent() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => {
                    d.OptedOut = true;
                    d.InstallToken = "synthetic-saved-token";
                    d.PendingStartGameIds.Add("saved-game");
                });
                var originalId = GsDataManager.Data.InstallID;
                var path = DataPath(temp);
                var originalBytes = File.ReadAllBytes(path);

                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    Assert.Throws<IOException>(() => GsDataManager.Initialize(temp.Path, null));
                    AssertDataUnavailable();
                    GsDataManager.Save();
                    Assert.False(File.Exists(path + ".tmp"));
                }

                Assert.Equal(originalBytes, File.ReadAllBytes(path));
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal(originalId, GsDataManager.Data.InstallID);
                Assert.True(GsDataManager.IsOptedOut);
                Assert.Equal("synthetic-saved-token", GsDataManager.Data.InstallToken);
                Assert.Contains("saved-game", GsDataManager.Data.PendingStartGameIds);
            }
        }

        [Fact]
        public void Initialize_MissingDirectory_CreatesOneDurableIdentity() {
            using (var temp = TempPluginDir.Create()) {
                var freshDirectory = Path.Combine(temp.Path, "fresh-install");
                GsDataManager.Initialize(freshDirectory, null);
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;

                Assert.True(Guid.TryParse(installId, out _));
                Assert.Equal(1, generation);
                Assert.False(GsDataManager.IsOptedOut);
                Assert.True(File.Exists(Path.Combine(freshDirectory, "gs_data.json")));

                GsDataManager.Initialize(freshDirectory, null);
                Assert.Equal(installId, GsDataManager.Data.InstallID);
                Assert.Equal(generation, GsDataManager.Data.IdentityGeneration);
            }
        }

        [Theory]
        [InlineData("install")]
        [InlineData("generation")]
        [InlineData("opt-out")]
        public void TryMutateIfActiveIdentity_RejectsStaleOrOptedOutResponses(string changedField) {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                GsDataManager.MutateAndSave(d => {
                    if (changedField == "install") d.InstallID = Guid.NewGuid().ToString();
                    if (changedField == "generation") d.IdentityGeneration++;
                    if (changedField == "opt-out") d.OptedOut = true;
                });
                var originalBytes = File.ReadAllBytes(DataPath(temp));
                var invoked = false;

                Assert.False(GsDataManager.TryMutateIfActiveIdentity(installId, generation, d => {
                    invoked = true;
                    d.LinkedUserId = "stale-response-user";
                }));

                Assert.False(invoked);
                Assert.Null(GsDataManager.Data.LinkedUserId);
                Assert.Equal(originalBytes, File.ReadAllBytes(DataPath(temp)));
            }
        }

        [Fact]
        public void TryMutateIfActiveIdentity_OptingBackInDoesNotReviveAnOldResponse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var installId = GsDataManager.Data.InstallID;
                var generation = GsDataManager.Data.IdentityGeneration;
                GsDataManager.PerformOptOut();
                GsDataManager.PerformOptIn();

                Assert.False(GsDataManager.TryMutateIfActiveIdentity(installId, generation,
                    d => d.LinkedUserId = "stale-response-user"));
                Assert.True(GsDataManager.TryMutateIfActiveIdentity(installId, GsDataManager.Data.IdentityGeneration,
                    d => d.LinkedUserId = "current-response-user"));

                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal("current-response-user", GsDataManager.Data.LinkedUserId);
            }
        }

        [Fact]
        public void QueueSessionFinishes_WriteFailureRestoresCollectionsAndCanBeRetried() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var existing = new PendingScrobble { Type = "start", StartData = new ScrobbleStartReq { game_id = "other-game" } };
                GsDataManager.MutateAndSave(d => {
                    d.PendingScrobbles.Add(existing);
                    d.ActiveSessionsByGameId["stopped-game"] = "session-to-finish";
                });
                var previousQueue = GsDataManager.Data.PendingScrobbles;
                var previousSessions = GsDataManager.Data.ActiveSessionsByGameId;
                var sessions = GsDataManager.SnapshotActiveSessions();
                var finish = new PendingScrobble {
                    Type = "finish",
                    FinishData = new ScrobbleFinishReq { game_id = "stopped-game", session_id = "session-to-finish" }
                };
                var finishes = new List<PendingScrobble> { finish };
                var path = DataPath(temp);
                var originalBytes = File.ReadAllBytes(path);

                // Read sharing allows inspection but prevents File.Replace from deleting
                // the destination, exercising the real atomic writer's failure path.
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    Assert.False(GsDataManager.QueueSessionFinishesAndClearActive(sessions, finishes));
                    Assert.Same(previousQueue, GsDataManager.Data.PendingScrobbles);
                    Assert.Same(previousSessions, GsDataManager.Data.ActiveSessionsByGameId);
                    Assert.Same(existing, Assert.Single(GsDataManager.PeekPendingScrobbles()));
                    Assert.Equal("session-to-finish", GsDataManager.Data.ActiveSessionsByGameId["stopped-game"]);
                    Assert.Equal(originalBytes, File.ReadAllBytes(path));
                }

                Assert.True(GsDataManager.QueueSessionFinishesAndClearActive(sessions, finishes));
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal(2, GsDataManager.Data.PendingScrobbles.Count);
                Assert.Equal("session-to-finish", GsDataManager.Data.PendingScrobbles[1].FinishData.session_id);
                Assert.Empty(GsDataManager.Data.ActiveSessionsByGameId);
            }
        }

        [Fact]
        public void QueueSessionFinishes_ResolvesStartThatCompletedAfterShutdownSnapshot() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var start = new PendingScrobble {
                    Type = "start",
                    StartData = new ScrobbleStartReq { game_id = "starting-game", plugin_id = "source" }
                };
                GsDataManager.MutateAndSave(d => {
                    d.PendingScrobbles.Add(start);
                    d.PendingStartGameIds.Add("starting-game");
                });
                var snapshot = GsDataManager.SnapshotActiveSessions();
                var finish = new PendingScrobble {
                    Type = "finish",
                    FinishData = new ScrobbleFinishReq { game_id = "starting-game", plugin_id = "source" }
                };
                Assert.True(GsDataManager.CompletePendingStart(start, "late-session"));

                Assert.True(GsDataManager.QueueSessionFinishesAndClearActive(snapshot, new List<PendingScrobble> { finish }));
                GsDataManager.Initialize(temp.Path, null);
                Assert.Equal("late-session", Assert.Single(GsDataManager.PeekPendingScrobbles()).FinishData.session_id);
                Assert.Empty(GsDataManager.SnapshotActiveSessions());
                Assert.False(GsDataManager.HasPendingStart("starting-game"));
            }
        }

        [Fact]
        public void QueueSessionFinishes_DoesNotClearANewerSessionForTheSameGame() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => {
                    d.ActiveSessionsByGameId["restarted-game"] = "new-session";
                    d.ActiveSessionsByGameId["stopped-game"] = "matching-session";
                });
                var snapshot = new Dictionary<string, string> {
                    { "restarted-game", "old-session" }, { "stopped-game", "matching-session" }
                };
                var finishes = new List<PendingScrobble> {
                    new PendingScrobble { Type = "finish", FinishData = new ScrobbleFinishReq { game_id = "restarted-game", session_id = "old-session" } },
                    new PendingScrobble { Type = "finish", FinishData = new ScrobbleFinishReq { game_id = "stopped-game", session_id = "matching-session" } }
                };

                Assert.True(GsDataManager.QueueSessionFinishesAndClearActive(snapshot, finishes));
                GsDataManager.Initialize(temp.Path, null);

                Assert.Equal("new-session", GsDataManager.Data.ActiveSessionsByGameId["restarted-game"]);
                Assert.False(GsDataManager.Data.ActiveSessionsByGameId.ContainsKey("stopped-game"));
                Assert.Equal(2, GsDataManager.Data.PendingScrobbles.Count);
            }
        }
    }
}
