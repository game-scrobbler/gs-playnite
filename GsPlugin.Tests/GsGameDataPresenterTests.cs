using System.Threading;
using System.Threading.Tasks;
using GsPlugin.Api;
using GsPlugin.View;
using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Covers the theme-facing contract of GsGameDataPresenter: it must not fetch
    /// until the details view actually loads, must fetch only once, and must degrade
    /// to HasData=false rather than throwing into Playnite's details view.
    /// </summary>
    public class GsGameDataPresenterTests {
        private class FakeApiClient : IGsApiClient {
            public int GetGameDataCalls;
            public string LastRequestedId;
            public GameDataRes Response;
            public System.Exception ThrowOnCall;

            public Task<GameDataRes> GetGameData(string playniteGameId) {
                GetGameDataCalls++;
                LastRequestedId = playniteGameId;
                if (ThrowOnCall != null) {
                    return Task.FromException<GameDataRes>(ThrowOnCall);
                }
                return Task.FromResult(Response);
            }

            // Unused members of the interface.
            public Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq d) => Task.FromResult<ScrobbleStartRes>(null);
            public Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq d) => Task.FromResult<ScrobbleFinishRes>(null);
            public Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq r) => Task.FromResult<AsyncQueuedResponse>(null);
            public Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq r) => Task.FromResult<AsyncQueuedResponse>(null);
            public Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq r) => Task.FromResult<AsyncQueuedResponse>(null);
            public Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq r) => Task.FromResult<AsyncQueuedResponse>(null);
            public Task<AllowedPluginsRes> GetAllowedPlugins() => Task.FromResult<AllowedPluginsRes>(null);
            public Task<TokenVerificationRes> VerifyToken(string t, string p) => Task.FromResult<TokenVerificationRes>(null);
            public Task FlushPendingScrobblesAsync() => Task.CompletedTask;
            public Task<UnlinkRes> UnlinkAccount() => Task.FromResult<UnlinkRes>(null);
            public Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq r) => Task.FromResult<DeleteDataRes>(null);
            public Task<OptInRes> RequestOptIn(OptInReq r) => Task.FromResult<OptInRes>(null);
            public Task<RegisterInstallTokenRes> RegisterInstallToken(string i) => Task.FromResult<RegisterInstallTokenRes>(null);
            public Task<string> ResetInstallToken(string t) => Task.FromResult<string>(null);
            public Task<string> GetDashboardToken() => Task.FromResult<string>(null);
            public Task<PlayniteNotificationsRes> GetNotifications() => Task.FromResult<PlayniteNotificationsRes>(null);
        }

        private static GameDataRes Synced() => new GameDataRes {
            success = true,
            game = new GameDataDto {
                playnite_id = "pn-1",
                name = "Hades",
                playtime_sec = 7200,
                play_count = 12,
                completion_status_name = "Playing",
                achievement_count_unlocked = 20,
                achievement_count_total = 49,
            }
        };

        /// <summary>Spin briefly so the fire-and-forget load can settle.</summary>
        private static async Task Settle() {
            for (int i = 0; i < 50; i++) {
                await Task.Delay(10);
            }
        }

        [Fact]
        public void DoesNotFetchBeforeTheDetailsViewLoads() {
            var api = new FakeApiClient { Response = Synced() };

            var presenter = new GsGameDataPresenter(api, "pn-1");

            // Constructing a presenter per game while scrolling must not hit the API.
            Assert.Equal(0, api.GetGameDataCalls);
            Assert.False(presenter.HasData);
        }

        [Fact]
        public async Task PopulatesBindablePropertiesOnLoad() {
            var api = new FakeApiClient { Response = Synced() };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.True(presenter.HasData);
            Assert.Equal("pn-1", api.LastRequestedId);
            Assert.Equal(7200, presenter.PlaytimeSeconds);
            Assert.Equal(2.0, presenter.PlaytimeHours);
            Assert.Equal(12, presenter.PlayCount);
            Assert.Equal("Playing", presenter.CompletionStatus);
            Assert.True(presenter.HasAchievements);
            Assert.False(presenter.IsLoading);
        }

        [Fact]
        public async Task FetchesOnlyOnceAcrossRepeatedLoads() {
            var api = new FakeApiClient { Response = Synced() };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.Equal(1, api.GetGameDataCalls);
        }

        [Fact]
        public async Task UnsyncedGameYieldsNoDataRatherThanThrowing() {
            var api = new FakeApiClient {
                Response = new GameDataRes { success = true, game = null, reason = "not_synced" }
            };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.False(presenter.HasData);
            Assert.False(presenter.IsLoading);
        }

        [Fact]
        public async Task TransportFailureYieldsNoDataRatherThanThrowing() {
            var api = new FakeApiClient { Response = null };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.False(presenter.HasData);
        }

        [Fact]
        public async Task ApiExceptionIsSwallowedSoTheDetailsViewSurvives() {
            var api = new FakeApiClient { ThrowOnCall = new System.InvalidOperationException("boom") };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.False(presenter.HasData);
            Assert.False(presenter.IsLoading);
        }

        [Fact]
        public async Task GameWithNoAchievementsReportsHasAchievementsFalse() {
            var res = Synced();
            res.game.achievement_count_unlocked = 0;
            res.game.achievement_count_total = 0;
            var api = new FakeApiClient { Response = res };
            var presenter = new GsGameDataPresenter(api, "pn-1");

            presenter.LoadedInDetailsView(null);
            await Settle();

            // A theme should hide the block rather than render "0 of 0".
            Assert.False(presenter.HasAchievements);
        }

        [Fact]
        public async Task EmptyGameIdSkipsTheFetch() {
            var api = new FakeApiClient { Response = Synced() };
            var presenter = new GsGameDataPresenter(api, "");

            presenter.LoadedInDetailsView(null);
            await Settle();

            Assert.Equal(0, api.GetGameDataCalls);
            Assert.False(presenter.HasData);
        }
    }
}
