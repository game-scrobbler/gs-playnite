using System.Threading.Tasks;
using static GsPlugin.GsApiClient;

namespace GsPlugin {
    /// <summary>
    /// Interface for API communication with GameScrobbler.
    /// Enables unit testing with mock implementations.
    /// </summary>
    public interface IGsApiClient {
        Task<ScrobbleStartRes> StartGameSession(ScrobbleStartReq startData, bool useAsync = false);
        Task<ScrobbleFinishRes> FinishGameSession(ScrobbleFinishReq endData, bool useAsync = false);
        Task<LibrarySyncRes> SyncLibrary(LibrarySyncReq librarySyncReq, bool useAsync = false);
        Task<AllowedPluginsRes> GetAllowedPlugins();
        Task<TokenVerificationRes> VerifyToken(string token, string playniteId);
        Task FlushPendingScrobblesAsync();
    }
}
