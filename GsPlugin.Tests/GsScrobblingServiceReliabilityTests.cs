using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;
using Playnite;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsScrobblingServiceReliabilityTests {
        // "Crow.Steam" is one of the P11 library plugin ids GsAllowedPlugins accepts, so these
        // games pass the allowlist the scrobble paths check.
        private static Game Game() => new Game {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Game",
            LibraryGameId = "123",
            LibraryId = "Crow.Steam"
        };

        private static GsData ReloadSaved(TempPluginDir temp) =>
            JsonSerializer.Deserialize<GsData>(File.ReadAllText(Path.Combine(temp.Path, "gs_data.json")));

        private static GsScrobblingService Service(FakeApi api) =>
            new GsScrobblingService(api, null, null) {
                QueueStatusPollInterval = TimeSpan.Zero,
                QueueStatusPollBudget = TimeSpan.Zero
            };

        [Fact]
        public async Task StopWhileStartPending_PersistsBothEventsAndCompletesMatchingSession() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var response = new TaskCompletionSource<ScrobbleStartRes>();
                var api = new FakeApi { OnStart = _ => response.Task };
                var service = Service(api);
                var game = Game();
                var start = service.OnGameStartAsync(game);
                var stop = service.OnGameStoppedAsync(game);
                Assert.False(stop.IsCompleted);
                Assert.Empty(api.Finishes);
                Assert.Equal(new[] { "start", "finish" }, ReloadSaved(temp).PendingScrobbles.Select(p => p.Type));
                Assert.Empty(GsDataManager.PeekPendingScrobbles()); // Live handlers own their claims.

                response.SetResult(new ScrobbleStartRes { session_id = "session-1" });
                await Task.WhenAll(start, stop);
                Assert.Equal("session-1", Assert.Single(api.Finishes).session_id);
                Assert.Empty(ReloadSaved(temp).PendingScrobbles);
                Assert.Empty(GsDataManager.SnapshotActiveSessions());
            }
        }

        [Fact]
        public async Task RestartWhilePreviousFinishPending_PreservesNewActiveSession() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var response = new TaskCompletionSource<ScrobbleFinishRes>();
                var number = 0;
                var api = new FakeApi {
                    OnStart = _ => Task.FromResult(new ScrobbleStartRes { session_id = "session-" + (++number) }),
                    OnFinish = _ => response.Task
                };
                var service = Service(api);
                var game = Game();
                await service.OnGameStartAsync(game);
                var stop = service.OnGameStoppedAsync(game);
                var restart = service.OnGameStartAsync(game);
                Assert.False(restart.IsCompleted);
                response.SetResult(new ScrobbleFinishRes());
                await Task.WhenAll(stop, restart);
                Assert.True(GsDataManager.TryGetActiveSession(game.Id.ToString(), out var active));
                Assert.Equal("session-2", active);
                Assert.Equal("session-1", Assert.Single(api.Finishes).session_id);
            }
        }

        [Fact]
        public async Task FailedStartAndStop_RemainInOrderWithoutSendingFinishEarly() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var api = new FakeApi { OnStart = _ => Task.FromResult<ScrobbleStartRes>(null) };
                var service = Service(api);
                var game = Game();
                await service.OnGameStartAsync(game);
                await service.OnGameStoppedAsync(game);
                Assert.Empty(api.Finishes);
                var pending = ReloadSaved(temp).PendingScrobbles;
                Assert.Equal(new[] { "start", "finish" }, pending.Select(p => p.Type));
                Assert.Null(pending[1].FinishData.session_id);
            }
        }

        [Fact]
        public async Task Shutdown_PersistsEveryFinishBeforeAwaitingFirstResponse() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                GsDataManager.MutateAndSave(d => {
                    d.ActiveSessionsByGameId["game-a"] = "session-a";
                    d.ActiveSessionsByGameId["game-b"] = "session-b";
                });
                var response = new TaskCompletionSource<ScrobbleFinishRes>();
                var api = new FakeApi { OnFinish = _ => response.Task };
                var stopping = Service(api).OnApplicationStoppedAsync();
                Assert.False(stopping.IsCompleted);
                Assert.Single(api.Finishes);
                var saved = ReloadSaved(temp);
                Assert.Equal(2, saved.PendingScrobbles.Count);
                Assert.Empty(saved.ActiveSessionsByGameId);
                Assert.Single(saved.PendingScrobbles.Select(p => p.FinishData.finished_at).Distinct());
                response.SetResult(new ScrobbleFinishRes());
                await stopping;
                Assert.Empty(ReloadSaved(temp).PendingScrobbles);
            }
        }

        [Fact]
        public async Task ShutdownDuringPendingStart_PersistsPairedFinishForReplay() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var response = new TaskCompletionSource<ScrobbleStartRes>();
                var api = new FakeApi { OnStart = _ => response.Task };
                var service = Service(api);
                var start = service.OnGameStartAsync(Game());
                await service.OnApplicationStoppedAsync();
                Assert.Equal(new[] { "start", "finish" }, ReloadSaved(temp).PendingScrobbles.Select(p => p.Type));
                Assert.Empty(GsDataManager.PeekPendingScrobbles()); // Finish cannot overtake claimed start.
                response.SetResult(new ScrobbleStartRes { session_id = "shutdown-session" });
                await start;
                var finish = Assert.Single(GsDataManager.PeekPendingScrobbles());
                Assert.Equal("shutdown-session", finish.FinishData.session_id);
            }
        }

        /// <summary>
        /// A status the server actually disowned is the one gs-playnite#83 is about, and it must
        /// leave both baselines alone so the next sync retries.
        /// </summary>
        [Theory]
        [InlineData("library-full", "failed")]
        [InlineData("library-diff", "failed")]
        public async Task RejectedQueueJob_DoesNotCommitEitherBaseline(string path, string status) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                var id = game.Id.ToString();
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { [id] = "old-library" });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [id] = "old-achievement" });
                GsDataManager.MutateAndSave(d => { d.LastLibraryHash = "old-library-global"; d.LastAchievementHash = "old-achievement-global"; });
                var api = new FakeApi { QueueStatus = status };
                var service = Service(api);
                SyncLibraryResult result;
                switch (path) {
                    case "library-full": result = await service.SyncLibraryFullAsync(new[] { game }); break;
                    default: result = await service.SyncLibraryDiffAsync(new[] { game }); break;
                }
                Assert.Equal(SyncLibraryResult.Error, result);
                Assert.Equal("old-library-global", GsDataManager.Data.LastLibraryHash);
                Assert.Equal("old-achievement-global", GsDataManager.Data.LastAchievementHash);
                Assert.Equal("old-library", GsSyncHashIndex.GetLibraryFingerprints()[id]);
                Assert.Equal("old-achievement", GsSyncHashIndex.GetAchievementFingerprints()[id]);
            }
        }

        /// <summary>
        /// The counterpart: a job the server admitted and is still working on must advance the
        /// baseline. Refusing to means a library whose job outruns the poll budget re-uploads in
        /// full on every launch and never records a sync: the force-full-sync loop the confirm
        /// step exists to avoid.
        /// </summary>
        [Theory]
        [InlineData("library-full")]
        [InlineData("library-diff")]
        public async Task StillProcessingQueueJob_CommitsBaselineSoTheNextRunIsADiff(string path) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                var id = game.Id.ToString();
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { [id] = "old-library" });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [id] = "old-achievement" });
                GsDataManager.MutateAndSave(d => { d.LastLibraryHash = "old-library-global"; d.LastAchievementHash = "old-achievement-global"; });
                var api = new FakeApi { QueueStatus = "processing" };
                var service = Service(api);
                SyncLibraryResult result;
                switch (path) {
                    case "library-full": result = await service.SyncLibraryFullAsync(new[] { game }); break;
                    default: result = await service.SyncLibraryDiffAsync(new[] { game }); break;
                }
                Assert.Equal(SyncLibraryResult.Success, result);
                var isLibrary = path.StartsWith("library");
                Assert.NotEqual(isLibrary ? "old-library-global" : "old-achievement-global",
                    isLibrary ? GsDataManager.Data.LastLibraryHash : GsDataManager.Data.LastAchievementHash);
                Assert.NotEqual(isLibrary ? "old-library" : "old-achievement",
                    isLibrary ? GsSyncHashIndex.GetLibraryFingerprints()[id] : GsSyncHashIndex.GetAchievementFingerprints()[id]);
            }
        }

        [Fact]
        public async Task MissingQueueId_DoesNotCommitBaseline() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var api = new FakeApi { QueueId = null };
                var result = await Service(api).SyncLibraryFullAsync(new[] { Game() });
                Assert.Equal(SyncLibraryResult.Error, result);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.False(GsSyncHashIndex.HasLibraryBaseline);
            }
        }

        [Theory]
        [InlineData("library-full")]
        [InlineData("library-diff")]
        public async Task OptOutDuringQueuePoll_RejectsOldIdentityBaseline(string path) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                var id = game.Id.ToString();
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { [id] = "old-library" });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [id] = "old-achievement" });
                GsDataManager.MutateAndSave(d => { d.LastLibraryHash = "old-library-global"; d.LastAchievementHash = "old-achievement-global"; });
                var pollReached = new TaskCompletionSource<bool>();
                var response = new TaskCompletionSource<QueueStatusRes>();
                var api = new FakeApi {
                    OnQueueStatus = () => { pollReached.TrySetResult(true); return response.Task; }
                };
                var service = Service(api);
                Task<SyncLibraryResult> sync;
                switch (path) {
                    case "library-full": sync = service.SyncLibraryFullAsync(new[] { game }); break;
                    default: sync = service.SyncLibraryDiffAsync(new[] { game }); break;
                }
                await pollReached.Task;
                GsDataManager.PerformOptOut();
                var libraryAfterOptOut = GsSyncHashIndex.GetLibraryFingerprints();
                var achievementsAfterOptOut = GsSyncHashIndex.GetAchievementFingerprints();
                response.SetResult(new QueueStatusRes { success = true, data = new QueueStatusData { status = "completed" } });
                Assert.Equal(SyncLibraryResult.Error, await sync);
                Assert.Null(GsDataManager.Data.LastLibraryHash);
                Assert.Null(GsDataManager.Data.LastAchievementHash);
                Assert.Equal(libraryAfterOptOut, GsSyncHashIndex.GetLibraryFingerprints());
                Assert.Equal(achievementsAfterOptOut, GsSyncHashIndex.GetAchievementFingerprints());
            }
        }

        /// <summary>
        /// A game whose source stops being eligible mid-session must not stay tracked. The stop
        /// handler declines to report it, and if it also leaves the active-session entry behind,
        /// the shutdown sweep, which applies no allow-list filter, finishes it at Playnite's
        /// exit time and reports a session that ran until the app closed.
        /// </summary>
        [Fact]
        public async Task StopOfNoLongerAllowedGame_ClearsSessionSoShutdownInventsNoFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var api = new FakeApi();
                var service = Service(api);
                var game = Game();
                await service.OnGameStartAsync(game);
                Assert.True(GsDataManager.TryGetActiveSession(game.Id.ToString(), out _));

                // The user retags the running game to a source the allowlist does not recognize.
                game.LibraryId = null;
                await service.OnGameStoppedAsync(game);

                Assert.Empty(GsDataManager.SnapshotActiveSessions());
                api.Finishes.Clear();
                await service.OnApplicationStoppedAsync();
                Assert.Empty(api.Finishes);
                Assert.Empty(ReloadSaved(temp).PendingScrobbles);
            }
        }

        /// <summary>Same leak, reached by switching scrobbling off while a game runs.</summary>
        [Fact]
        public async Task StopWhileScrobblingDisabled_ClearsSessionSoShutdownInventsNoFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var api = new FakeApi();
                var service = Service(api);
                var game = Game();
                await service.OnGameStartAsync(game);

                GsDataManager.MutateAndSave(d => d.Flags.Add("no-scrobble"));
                await service.OnGameStoppedAsync(game);
                Assert.Empty(GsDataManager.SnapshotActiveSessions());

                // Re-enabled later in the same session, so the shutdown sweep actually runs.
                GsDataManager.MutateAndSave(d => d.Flags.Remove("no-scrobble"));
                api.Finishes.Clear();
                await service.OnApplicationStoppedAsync();
                Assert.Empty(api.Finishes);
            }
        }

        /// <summary>
        /// The shutdown sweep can race a live stop for the same game. Appending unconditionally
        /// left a second finish for an already-finished session persisted in the queue, which a
        /// later flush would send.
        /// </summary>
        [Fact]
        public async Task ShutdownRacingALiveStop_QueuesNoDuplicateFinish() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var release = new TaskCompletionSource<ScrobbleFinishRes>();
                var api = new FakeApi { OnFinish = _ => release.Task };
                var service = Service(api);
                var game = Game();
                await service.OnGameStartAsync(game);
                var sessions = GsDataManager.SnapshotActiveSessions();
                Assert.Single(sessions);

                // The live stop lands its durable finish first; shutdown still holds the older snapshot.
                var stop = service.OnGameStoppedAsync(game);
                var finishes = sessions.Select(entry => new PendingScrobble {
                    Type = "finish",
                    QueuedAt = DateTime.Now,
                    FinishData = new ScrobbleFinishReq {
                        game_id = entry.Key,
                        plugin_id = game.LibraryId,
                        session_id = entry.Value
                    }
                }).ToList();
                Assert.True(GsDataManager.QueueSessionFinishesAndClearActive(
                    sessions, finishes, GsDataManager.Data.InstallID, GsDataManager.Data.IdentityGeneration));

                Assert.Single(GsDataManager.Data.PendingScrobbles, p => p.Type == "finish");
                release.SetResult(new ScrobbleFinishRes());
                await stop;
                Assert.Empty(ReloadSaved(temp).PendingScrobbles);
            }
        }

        /// <summary>
        /// A handler can take the gate from the map and be descheduled before awaiting it. If the
        /// other handler's cleanup retires the entry on "looks uncontended" alone, the paused one
        /// keeps an instance nobody else shares and two handlers for the same game run at once.
        /// Two concurrent starts must serialize: only one may be inside the API call at a time,
        /// and the gate must still be retired afterwards so the map does not grow.
        /// </summary>
        [Fact]
        public async Task ConcurrentHandlersForOneGame_StaySerializedAndStillRetireTheGate() {
            using (var temp = TempPluginDir.CreateWithDataManager()) {
                var inFlight = 0;
                var maxConcurrent = 0;
                var entered = new TaskCompletionSource<bool>();
                var release = new TaskCompletionSource<bool>();
                var api = new FakeApi {
                    OnStart = async _ => {
                        var now = Interlocked.Increment(ref inFlight);
                        InterlockedMax(ref maxConcurrent, now);
                        entered.TrySetResult(true);
                        await release.Task;
                        Interlocked.Decrement(ref inFlight);
                        return new ScrobbleStartRes { session_id = "session" };
                    }
                };
                var service = Service(api);
                var game = Game();

                // Neither is awaited yet: the first holds the gate inside the API call while the
                // second is contending for the same gate.
                var first = service.OnGameStartAsync(game);
                var second = service.OnGameStartAsync(game);
                await entered.Task;

                release.SetResult(true);
                await Task.WhenAll(first, second);

                Assert.Equal(1, maxConcurrent);
                Assert.Equal(0, service.SessionGateCount);
            }
        }

        private static void InterlockedMax(ref int target, int value) {
            int seen;
            while (value > (seen = Volatile.Read(ref target))) {
                if (Interlocked.CompareExchange(ref target, value, seen) == seen) return;
            }
        }

        private sealed class FakeApi : IGsApiClient {
            public Func<ScrobbleStartReq, Task<ScrobbleStartRes>> OnStart = _ => Task.FromResult(new ScrobbleStartRes { session_id = "session" });
            public Func<ScrobbleFinishReq, Task<ScrobbleFinishRes>> OnFinish = _ => Task.FromResult(new ScrobbleFinishRes());
            public List<ScrobbleFinishReq> Finishes = new List<ScrobbleFinishReq>();
            public int AchievementUploads;
            public AchievementsDiffSyncReq AchievementDiff;
            public string QueueId = "job";
            public string QueueStatus = "completed";
            public Func<Task<QueueStatusRes>> OnQueueStatus;
            private AsyncQueuedResponse Queued() => new AsyncQueuedResponse { success = true, status = "queued", queueId = QueueId };
            public Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq req) => OnStart(req);
            public Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq req) { Finishes.Add(req); return OnFinish(req); }
            public Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req) => throw new NotSupportedException();
            public Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req) => Task.FromResult(Queued());
            public Task<V4SyncBeginRes> SyncLibraryFullBegin(LibraryV4FullSyncBeginReq req) => Task.FromResult(new V4SyncBeginRes { success = true, status = "started", sync_id = "sync", max_chunk_items = 500 });
            public Task<V4SyncChunkRes> SyncLibraryFullChunk(LibraryV4ChunkReq req) => Task.FromResult(new V4SyncChunkRes { success = true, status = "accepted", sync_id = "sync", chunk_index = req.chunk_index });
            public Task<AsyncQueuedResponse> SyncLibraryFullCommit(LibraryV4CommitReq req) => Task.FromResult(Queued());
            public Task SyncLibraryFullAbort(string syncId) => Task.CompletedTask;
            public Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req) => throw new NotSupportedException();
            public Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req) { AchievementUploads++; AchievementDiff = req; return Task.FromResult(Queued()); }
            public Task<V4SyncBeginRes> SyncAchievementsFullBegin(AchievementsV4FullSyncBeginReq req) { AchievementUploads++; return Task.FromResult(new V4SyncBeginRes { success = true, status = "started", sync_id = "sync", max_chunk_items = 500 }); }
            public Task<V4SyncChunkRes> SyncAchievementsFullChunk(AchievementsV4ChunkReq req) => Task.FromResult(new V4SyncChunkRes { success = true, status = "accepted", sync_id = "sync", chunk_index = req.chunk_index });
            public Task<AsyncQueuedResponse> SyncAchievementsFullCommit(AchievementsV4CommitReq req) => Task.FromResult(Queued());
            public Task SyncAchievementsFullAbort(string syncId) => Task.CompletedTask;
            public Task<QueueStatusRes> GetQueueStatus(string queueId) => OnQueueStatus != null
                ? OnQueueStatus()
                : Task.FromResult(new QueueStatusRes { success = true, data = new QueueStatusData { status = QueueStatus } });
            public Task<AllowedPluginsRes> GetAllowedPlugins() => throw new NotSupportedException();
            public Task<TokenVerificationRes> VerifyToken(string token, string playniteId) => throw new NotSupportedException();
            public Task FlushPendingScrobblesAsync() => throw new NotSupportedException();
            public Task<UnlinkRes> UnlinkAccount() => throw new NotSupportedException();
            public Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req) => throw new NotSupportedException();
            public Task<OptInRes> RequestOptIn(OptInReq req) => throw new NotSupportedException();
            public Task<RegisterInstallTokenRes> RegisterInstallToken(string installId) => throw new NotSupportedException();
            public Task<string> GetDashboardToken() => throw new NotSupportedException();
            public Task<PlayniteNotificationsRes> GetNotifications() => throw new NotSupportedException();
            public Task<GameDataRes> GetGameData(string playniteGameId) => throw new NotSupportedException();
        }
    }
}
