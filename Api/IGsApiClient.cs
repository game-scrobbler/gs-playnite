using System.Threading.Tasks;
using static GsPlugin.Api.GsApiClient;

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
        Task<AsyncQueuedResponse> SyncAchievementsFull(AchievementsFullSyncReq req);
        Task<AsyncQueuedResponse> SyncAchievementsDiff(AchievementsDiffSyncReq req);
        Task<AllowedPluginsRes> GetAllowedPlugins();
        Task<TokenVerificationRes> VerifyToken(string token, string playniteId);
        Task FlushPendingScrobblesAsync();
    }
}
