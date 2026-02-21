using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Playnite.SDK;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.Services {
    internal class GsUpdateChecker {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/game-scrobbler/gs-playnite/releases/latest";
        private const string NotificationId = "gs-update-available";
        private const string TagPrefix = "GsPlugin-v";

        private static readonly HttpClient _httpClient;

        static GsUpdateChecker() {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("GsPlugin", GetCurrentVersion()));
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        private readonly IPlayniteAPI _playniteApi;

        public GsUpdateChecker(IPlayniteAPI playniteApi) {
            _playniteApi = playniteApi;
        }

        public async Task CheckForUpdateAsync() {
            try {
                var currentVersion = GetCurrentVersion();
                var latestTag = await FetchLatestTagAsync();
                if (latestTag == null) {
                    return;
                }

                if (!latestTag.StartsWith(TagPrefix)) {
                    _logger.Warn($"Unexpected GitHub release tag format: {latestTag}");
                    return;
                }

                var latestVersion = latestTag.Substring(TagPrefix.Length);

                if (!IsNewer(latestVersion, currentVersion)) {
                    return;
                }

                if (GsDataManager.Data.LastNotifiedVersion == latestVersion) {
                    return;
                }

                _playniteApi.Notifications.Add(new NotificationMessage(
                    NotificationId,
                    $"Game Spectrum {latestVersion} is available. Open Add-ons to update.",
                    NotificationType.Info));

                GsDataManager.Data.LastNotifiedVersion = latestVersion;
                GsDataManager.Save();

                GsLogger.Info($"Update notification shown: current={currentVersion}, latest={latestVersion}");
            }
            catch (Exception ex) {
                _logger.Warn(ex, "Update check failed");
            }
        }

        private static async Task<string> FetchLatestTagAsync() {
            var response = await _httpClient.GetAsync(ReleasesApiUrl);
            if (!response.IsSuccessStatusCode) {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(body)) {
                if (doc.RootElement.TryGetProperty("tag_name", out var tagEl)) {
                    return tagEl.GetString();
                }
            }

            return null;
        }

        private static bool IsNewer(string remote, string local) {
            return Version.TryParse(remote, out var r)
                && Version.TryParse(local, out var l)
                && r > l;
        }

        private static string GetCurrentVersion() {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
