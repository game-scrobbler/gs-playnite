using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.Models;
using GsPlugin.Services;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Xunit;

namespace GsPlugin.Tests {
    [Collection("StaticManagerTests")]
    public class GsScrobblingServiceReliabilityTests {
        private static Game Game() => new Game {
            Id = Guid.NewGuid(),
            Name = "Test Game",
            GameId = "123",
            PluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB")
        };

        private static T Event<T>(Game game) where T : new() {
            var result = new T();
            typeof(T).GetProperty("Game").SetValue(result, game);
            return result;
        }

        private static GsData ReloadSaved(TempPluginDir temp) =>
            JsonSerializer.Deserialize<GsData>(File.ReadAllText(Path.Combine(temp.Path, "gs_data.json")));

        private static GsScrobblingService Service(FakeApi api, Provider provider = null) =>
            new GsScrobblingService(api, provider ?? new Provider(), null) {
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
                var start = service.OnGameStartAsync(Event<OnGameStartingEventArgs>(game));
                var stop = service.OnGameStoppedAsync(Event<OnGameStoppedEventArgs>(game));
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
                await service.OnGameStartAsync(Event<OnGameStartingEventArgs>(game));
                var stop = service.OnGameStoppedAsync(Event<OnGameStoppedEventArgs>(game));
                var restart = service.OnGameStartAsync(Event<OnGameStartingEventArgs>(game));
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
                await service.OnGameStartAsync(Event<OnGameStartingEventArgs>(game));
                await service.OnGameStoppedAsync(Event<OnGameStoppedEventArgs>(game));
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
                var start = service.OnGameStartAsync(Event<OnGameStartingEventArgs>(Game()));
                await service.OnApplicationStoppedAsync();
                Assert.Equal(new[] { "start", "finish" }, ReloadSaved(temp).PendingScrobbles.Select(p => p.Type));
                Assert.Empty(GsDataManager.PeekPendingScrobbles()); // Finish cannot overtake claimed start.
                response.SetResult(new ScrobbleStartRes { session_id = "shutdown-session" });
                await start;
                var finish = Assert.Single(GsDataManager.PeekPendingScrobbles());
                Assert.Equal("shutdown-session", finish.FinishData.session_id);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailedAchievementRead_PreservesBaselineWithoutUploading(bool full) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [game.Id.ToString()] = "old-item" });
                GsDataManager.MutateAndSave(d => d.LastAchievementHash = "old-global");
                var api = new FakeApi();
                var provider = new Provider { Result = AchievementReadResult.Unavailable("unreadable") };
                var service = Service(api, provider);
                var result = full
                    ? await service.SyncAchievementsFullAsync(new[] { game })
                    : await service.SyncAchievementsDiffAsync(new[] { game });
                Assert.Equal(SyncLibraryResult.Error, result);
                Assert.Equal(0, api.AchievementUploads);
                Assert.Equal("old-global", GsDataManager.Data.LastAchievementHash);
                Assert.Equal("old-item", GsSyncHashIndex.GetAchievementFingerprints()[game.Id.ToString()]);
            }
        }

        [Fact]
        public async Task ConfirmedEmptyAchievementRead_ClearsOnlyAfterCompletedServerJob() {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [game.Id.ToString()] = "old-item" });
                GsDataManager.MutateAndSave(d => d.LastAchievementHash = "old-global");
                var api = new FakeApi();
                var result = await Service(api).SyncAchievementsDiffAsync(new[] { game });
                Assert.Equal(SyncLibraryResult.Success, result);
                Assert.Empty(Assert.Single(api.AchievementDiff.changed).achievements);
                Assert.Equal(0, GsSyncHashIndex.AchievementEntryCount);
            }
        }

        [Theory]
        [InlineData("library-full", "processing")]
        [InlineData("library-diff", "processing")]
        [InlineData("achievement-full", "processing")]
        [InlineData("achievement-diff", "processing")]
        [InlineData("library-full", "failed")]
        [InlineData("achievement-diff", "partial")]
        public async Task UnconfirmedQueueJob_DoesNotCommitEitherBaseline(string path, string status) {
            using (var temp = TempPluginDir.CreateWithDataManagerAndHashIndex()) {
                var game = Game();
                var id = game.Id.ToString();
                GsSyncHashIndex.ReplaceLibraryIndex(new Dictionary<string, string> { [id] = "old-library" });
                GsSyncHashIndex.ReplaceAchievementIndex(new Dictionary<string, string> { [id] = "old-achievement" });
                GsDataManager.MutateAndSave(d => { d.LastLibraryHash = "old-library-global"; d.LastAchievementHash = "old-achievement-global"; });
                var api = new FakeApi { QueueStatus = status };
                var provider = new Provider { Result = AchievementReadResult.Available(new List<AchievementItem> { new AchievementItem { Name = "new" } }) };
                var service = Service(api, provider);
                SyncLibraryResult result;
                switch (path) {
                    case "library-full": result = await service.SyncLibraryFullAsync(new[] { game }); break;
                    case "library-diff": result = await service.SyncLibraryDiffAsync(new[] { game }); break;
                    case "achievement-full": result = await service.SyncAchievementsFullAsync(new[] { game }); break;
                    default: result = await service.SyncAchievementsDiffAsync(new[] { game }); break;
                }
                Assert.Equal(SyncLibraryResult.Error, result);
                Assert.Equal("old-library-global", GsDataManager.Data.LastLibraryHash);
                Assert.Equal("old-achievement-global", GsDataManager.Data.LastAchievementHash);
                Assert.Equal("old-library", GsSyncHashIndex.GetLibraryFingerprints()[id]);
                Assert.Equal("old-achievement", GsSyncHashIndex.GetAchievementFingerprints()[id]);
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
        [InlineData("achievement-full")]
        [InlineData("achievement-diff")]
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
                var provider = new Provider { Result = AchievementReadResult.Available(new List<AchievementItem> { new AchievementItem { Name = "new" } }) };
                var service = Service(api, provider);
                Task<SyncLibraryResult> sync;
                switch (path) {
                    case "library-full": sync = service.SyncLibraryFullAsync(new[] { game }); break;
                    case "library-diff": sync = service.SyncLibraryDiffAsync(new[] { game }); break;
                    case "achievement-full": sync = service.SyncAchievementsFullAsync(new[] { game }); break;
                    default: sync = service.SyncAchievementsDiffAsync(new[] { game }); break;
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

        private sealed class Provider : IAchievementProvider, IReliableAchievementProvider {
            public AchievementReadResult Result = AchievementReadResult.Available(null);
            public bool IsInstalled => true;
            public bool IsPluginLoaded => true;
            public string ProviderName => "test";
            public string GetVersion() => "1";
            public (int unlocked, int total)? GetCounts(Guid gameId) => null;
            public List<AchievementItem> GetAchievements(Guid gameId) => Result.IsAvailable ? Result.Achievements : null;
            public AchievementReadResult ReadAchievements(Guid gameId) => Result;
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
        }
    }
}
