using System.Threading.Tasks;

namespace GsPlugin.Api {
    /// <summary>
    /// Interface for API communication with GameScrobbler.
    /// Enables unit testing with mock implementations.
    /// </summary>
    public interface IGsApiClient {
        Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData);
        Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData);
        Task<AsyncQueuedResponse> SyncLibraryFull(LibraryFullSyncReq req);
        Task<AsyncQueuedResponse> SyncLibraryDiff(LibraryDiffSyncReq req);
        Task<V4SyncBeginRes> SyncLibraryFullBegin(LibraryV4FullSyncBeginReq req);
        Task<V4SyncChunkRes> SyncLibraryFullChunk(LibraryV4ChunkReq req);
        Task<AsyncQueuedResponse> SyncLibraryFullCommit(LibraryV4CommitReq req);
        Task SyncLibraryFullAbort(string syncId);
        Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req);
        Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req);
        Task<V4SyncBeginRes> SyncAchievementsFullBegin(AchievementsV4FullSyncBeginReq req);
        Task<V4SyncChunkRes> SyncAchievementsFullChunk(AchievementsV4ChunkReq req);
        Task<AsyncQueuedResponse> SyncAchievementsFullCommit(AchievementsV4CommitReq req);
        Task SyncAchievementsFullAbort(string syncId);
        Task<AllowedPluginsRes> GetAllowedPlugins();
        Task<TokenVerificationRes> VerifyToken(string token, string playniteId);
        Task FlushPendingScrobblesAsync();
        Task<UnlinkRes> UnlinkAccount();
        Task<DeleteDataRes> RequestDeleteMyData(DeleteDataReq req);
        Task<OptInRes> RequestOptIn(OptInReq req);
        Task<RegisterInstallTokenRes> RegisterInstallToken(string installId);
        Task<string> GetDashboardToken();
        Task<PlayniteNotificationsRes> GetNotifications();
    }
}
