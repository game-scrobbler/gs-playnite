using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Sentry;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace GsPlugin
{
    public class GsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private GsPluginSettings settings;
        private string sessionID;
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        public GsPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new GsPluginSettings(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        private string GetPluginVersion()
        {
            string pluginFolder = Path.GetDirectoryName(GetType().Assembly.Location);
            string yamlPath = Path.Combine(pluginFolder, "extension.yaml");

            if (File.Exists(yamlPath))
            {
                foreach (var line in File.ReadAllLines(yamlPath))
                {
                    if (line.StartsWith("Version:"))
                    {
                        return line.Split(':')[1].Trim();
                    }
                }
            }

            return "Unknown Version";
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            // Add code to be executed when game is started running.

        }

        public override async void OnGameStarting(OnGameStartingEventArgs args)
        {
            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;

            var timeTrackerData = new TimeTracker
            {
                user_id = settings.InstallID,
                game_name = startedGame.Name,
                gameID = startedGame.Id.ToString(),
                metadata = new { },
                started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            await PostDataAsync("https://api.gamescrobbler.com/api/playnite/scrobble/start", timeTrackerData);
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
            DateTime localDate = DateTime.Now;

            var timeTrackerEndData = new TimeTrackerEnd
            {
                user_id = settings.InstallID,
                session_id = sessionID,
                metadata = new { },
                finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            await PostDataAsync("https://api.gamescrobbler.com/api/playnite/scrobble/finish", timeTrackerEndData);
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            SentryInit();
            // Add code to be executed when Playnite is initialized.
            SyncLib();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Add code to be executed when Playnite is shutting down.
            SyncLib();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            base.OnLibraryUpdated(args);
            SyncLib();
        }

        public override ISettings GetSettings(bool firstRunSettings) => settings;

        public override UserControl GetSettingsView(bool firstRunSettings) => new GsPluginSettingsView();

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            // Return one or more SidebarItem objects
            yield return new SidebarItem
            {
                Type = (SiderbarItemType)1,
                Title = "Show My Data",
                Icon = new TextBlock { Text = "📋" }, // or a path to an image icon
                // Return a new instance of your custom UserControl (WPF)
                Opened = () => new MySidebarView(settings, PlayniteApi, GetPluginVersion())
                // If you want a simple *action* instead of a custom panel, you can
                // return an item with Type = SidebarItemType.Action, plus an OpenCommand.
            };
        }

        public async void SyncLib()
        {
            var library = PlayniteApi.Database.Games.ToList();
            var syncData = new Sync
            {
                user_id = settings.InstallID,
            };

            string jsonLib = JsonSerializer.Serialize(library);
            string jsonSync = JsonSerializer.Serialize(syncData);
            string input = jsonSync + jsonLib;
            string modifiedInput = Regex.Replace(input, "(\"user_id\":\"[^\"]+\")}\\[", "$1, \"library\": [");

            await PostDataAsync("https://api.gamescrobbler.com/api/playnite/sync", modifiedInput);
        }

        private static async Task PostDataAsync(string url, object data)
        {
            var jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    // Send the POST request to the endpoint
                    var response = await httpClient.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                    await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    SentrySdk.CaptureException(ex);
                }
            }
        }

        private static async Task PostDataAsync(string url, string data)
        {
            var content = new StringContent(data, Encoding.UTF8, "application/json");

            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    var response = await httpClient.PostAsync(url, content);
                    response.EnsureSuccessStatusCode();
                    // Optionally read and process the response content
                    await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException ex)
                {
                    SentrySdk.CaptureException(ex);
                }
            }
        }

        public static void SentryInit()
        {
            SentrySdk.Init(options =>
            {
                // A Sentry Data Source Name (DSN) is required.
                // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
                // You can set it in the SENTRY_DSN environment variable, or you can set it in code here.
                options.Dsn = "https://af79b5bda2a052b04b3f490b79d0470a@o4508777256124416.ingest.de.sentry.io/4508777265627216";

                // When debug is enabled, the Sentry client will emit detailed debugging information to the console.
                // This might be helpful, or might interfere with the normal operation of your application.
                // We enable it here for demonstration purposes when first trying Sentry.
                // You shouldn't do this in your applications unless you're troubleshooting issues with Sentry.
                options.Debug = true;

                // This option is recommended. It enables Sentry's "Release Health" feature.
                options.AutoSessionTracking = true;

                // Set TracesSampleRate to 1.0 to capture 100%
                // of transactions for tracing.
                // We recommend adjusting this value in production.
                options.TracesSampleRate = 1.0;

                // Sample rate for profiling, applied on top of othe TracesSampleRate,
                // e.g. 0.2 means we want to profile 20 % of the captured transactions.
                // We recommend adjusting this value in production.
                options.ProfilesSampleRate = 1.0;
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

    class TimeTracker
    {
        public string user_id { get; set; }
        public string game_name { get; set; }
        public string gameID { get; set; }
        public object metadata { get; set; }
        public string started_at { get; set; }
    };

    class TimeTrackerEnd
    {
        public string user_id { get; set; }
        public object metadata { get; set; }
        public string finished_at { get; set; }
        public string session_id { get; set; }
    };

    class Sync
    {
        public string user_id { get; set; }
    };

    public class SessionData
    {
        public string session_id { get; set; }
    }
}
