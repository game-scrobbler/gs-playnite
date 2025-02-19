using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using Sentry;

namespace GsPlugin {
    /// <summary>
    /// Main plugin class that handles integration with Playnite.
    /// </summary>
    public class GsPlugin : GenericPlugin {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// Plugin settings view model.
        private GsPluginSettingsViewModel _settings { get; set; }
        private GsApiClient _apiClient;

        /// Unique identifier for the plugin itself.
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        public GsPlugin(IPlayniteAPI api) : base(api) {
            // Ceate settings
            _settings = new GsPluginSettingsViewModel(this);
            Properties = new GenericPluginProperties {
                HasSettings = true
            };
            _apiClient = new GsApiClient();
            // Initialize GsDataManager
            GsDataManager.Initialize(GetPluginUserDataPath(), _settings.InstallID);
        }

        /// <summary>
        /// Retrieves the current version of the plugin from the extension.yaml file.
        /// </summary>
        /// <returns>The version string or "Unknown Version" if not found.</returns>
        private static string GetPluginVersion() {
            string pluginFolder = Path.GetDirectoryName(typeof(GsPlugin).Assembly.Location);
            string yamlPath = Path.Combine(pluginFolder, "extension.yaml");

            if (File.Exists(yamlPath)) {
                foreach (var line in File.ReadAllLines(yamlPath)) {
                    if (line.StartsWith("Version:")) {
                        return line.Split(':')[1].Trim();
                    }
                }
            }

            return "Unknown Version";
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args) {
            // Add code to be executed when game is finished installing.
        }

        public override void OnGameStarted(OnGameStartedEventArgs args) {
            // Add code to be executed when game is started running.
        }

        public override async void OnGameStarting(OnGameStartingEventArgs args) {
            // Skip scrobbling if disabled
            if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                return;
            }

            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;

            // Build the payload for scrobbling the game start.
            var startData = new GsApiClient.TimeTracker {
                user_id = GsDataManager.Data.InstallID,
                game_name = startedGame.Name,
                game_id = startedGame.Id.ToString(),
                metadata = new { },
                started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            // Send POST request using the helper.
            var sessionData = await _apiClient.StartGameSession(startData);
            if (sessionData != null) {
                GsDataManager.Data.ActiveSessionId = sessionData.session_id;
                GsDataManager.Save();
            }
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args) {
            // Skip scrobbling if disabled
            if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                return;
            }

            // Check if we have a valid session ID
            if (string.IsNullOrEmpty(GsDataManager.Data.ActiveSessionId)) {
                GsLogger.Warn("No active session ID found when stopping game");
                return;
            }

            DateTime localDate = DateTime.Now;
            var stoppedGame = args.Game;

            // Build the payload for scrobbling the game finish
            var endData = new GsApiClient.TimeTrackerEnd {
                user_id = GsDataManager.Data.InstallID,
                game_name = stoppedGame.Name,
                game_id = stoppedGame.Id.ToString(),
                session_id = GsDataManager.Data.ActiveSessionId,
                metadata = new { },
                finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            // Send POST request and ensure success
            var finishResponse = await _apiClient.FinishGameSession(endData);
            if (finishResponse != null) {
                // Only clear the session ID if the request was successful
                GsDataManager.Data.ActiveSessionId = null;
                GsDataManager.Save();
            }
            else {
                GsLogger.Error($"Failed to finish game session for {stoppedGame.Name}");
            }
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args) {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args) {
            SentryInit();
            SyncLib();
        }

        public override async void OnApplicationStopped(OnApplicationStoppedEventArgs args) {
            // Skip scrobbling if disabled
            if (GsDataManager.Data.Flags.Contains("no-scrobble")) {
                return;
            }

            if (GsDataManager.Data.ActiveSessionId != null) {
                DateTime localDate = DateTime.Now;

                var startData = new GsApiClient.TimeTrackerEnd {
                    user_id = GsDataManager.Data.InstallID,
                    session_id = GsDataManager.Data.ActiveSessionId,
                    metadata = new { },
                    finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
                };

                var finishResponse = await _apiClient.FinishGameSession(startData);
                if (finishResponse != null) {
                    GsDataManager.Data.ActiveSessionId = null;
                    GsDataManager.Save();
                }
            }
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) {
            // Add code to be executed when library is updated.
            base.OnLibraryUpdated(args);
            SyncLib();
        }

        public override ISettings GetSettings(bool firstRunSettings) {
            return (ISettings)_settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings) {
            return new GsPluginSettingsView();
        }

        public override IEnumerable<SidebarItem> GetSidebarItems() {
            // Return one or more SidebarItem objects
            yield return new SidebarItem {
                Type = (SiderbarItemType)1,
                Title = "Show My Data",
                Icon = new TextBlock { Text = "📋" }, // or a path to an image icon
                Opened = () => {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView(GetPluginVersion());
                },
            };
            // If you want a simple *action* instead of a custom panel, you can
            // return an item with Type = SidebarItemType.Action, plus an OpenCommand.
        }

        /// <summary>
        /// Synchronizes the Playnite library with the external API.
        /// </summary>
        public async void SyncLib() {
            var library = PlayniteApi.Database.Games.ToList();
            var librarySync = new GsApiClient.LibrarySync {
                user_id = GsDataManager.Data.InstallID,
                library = library,
                flags = GsDataManager.Data.Flags
            };

            // Send POST request and ensure success.
            var syncResponse = await _apiClient.SyncLibrary(librarySync);
            // Optionally, use syncResponse.result.added and syncResponse.result.updated as needed.
        }

        public static void SentryInit() {
            // Set sampling rates based on user preference
            bool disableSentryFlag = GsDataManager.Data.Flags.Contains("no-sentry");

            SentrySdk.Init(options => {
                // A Sentry Data Source Name (DSN) is required.
                // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
                options.Dsn = "https://af79b5bda2a052b04b3f490b79d0470a@o4508777256124416.ingest.de.sentry.io/4508777265627216";

                // Use a static method to get the plugin version.
                options.Release = GsPlugin.GetPluginVersion();

                // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
                // This might be helpful, or might interfere with the normal operation of your application.
                // We enable it here for demonstration purposes when first trying Sentry.
                // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
#if DEBUG
                options.Environment = "development";
                options.Debug = true;
#else
                options.Environment = "production";
                options.Debug = false;
#endif

                // Set sample rates to 0 if user opted out of Sentry.
                options.SendDefaultPii = false;
                options.SampleRate = disableSentryFlag ? (float?)null : 1.0f;
                options.TracesSampleRate = disableSentryFlag ? (float?)null : 1.0f;
                options.ProfilesSampleRate = disableSentryFlag ? (float?)null : 1.0f;
                options.AutoSessionTracking = !disableSentryFlag;
                options.CaptureFailedRequests = !disableSentryFlag;
                options.FailedRequestStatusCodes.Add((400, 499));

                options.StackTraceMode = StackTraceMode.Enhanced;
                options.IsGlobalModeEnabled = false;
                options.DiagnosticLevel = SentryLevel.Warning;
                options.AttachStacktrace = true;

                // Requires NuGet package: Sentry.Profiling
                // Note: By default, the profiler is initialized asynchronously. This can
                // be tuned by passing a desired initialization timeout to the constructor.
                //options.AddIntegration(new ProfilingIntegration(
                // During startup, wait up to 500ms to profile the app startup code.
                // This could make launching the app a bit slower so comment it out if you
                // prefer profiling to start asynchronously
                //TimeSpan.FromMilliseconds(500)
                //));
            });
        }
    }
}
