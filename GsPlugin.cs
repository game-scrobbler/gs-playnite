﻿using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Sentry;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace GsPlugin {
    public class GsPlugin : GenericPlugin {
        private static readonly ILogger logger = LogManager.GetLogger();

        private GsPluginSettings settings { get; set; }

        private string sessionID { get; set; }
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        // Use a single shared HttpClient instance per best practices.
        private static readonly HttpClient httpClient = new HttpClient(new SentryHttpMessageHandler());

        // Preconfigured JSON options to be used for serialization.
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Prevents escaping +
        };

        public GsPlugin(IPlayniteAPI api) : base(api) {
            settings = new GsPluginSettings(this);
            Properties = new GenericPluginProperties {
                HasSettings = true
            };
        }

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
            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;

            // Build the payload for scrobbling the game start.
            TimeTracker startData = new TimeTracker {
                user_id = settings.InstallID,
                game_name = startedGame.Name,
                gameID = startedGame.Id.ToString(),
                metadata = new { },
                started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            // Send POST request using the helper.
            SessionData sessionData = await PostJsonAsync<SessionData>(
                "https://api.gamescrobbler.com/api/playnite/scrobble/start", startData);
            if (sessionData != null) {
                sessionID = sessionData.session_id;
            }
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args) {
            // Add code to be executed when game is preparing to be started.
            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;

            // Build the payload for scrobbling the game finish.
            TimeTrackerEnd startData = new TimeTrackerEnd {
                user_id = settings.InstallID,
                session_id = sessionID,
                metadata = new { },
                finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            // Send POST request and ensure success.
            FinishScrobbleResponse finishResponse = await PostJsonAsync<FinishScrobbleResponse>(
                "https://api.gamescrobbler.com/api/playnite/scrobble/finish", startData, true);
            // Optionally log finishResponse.status if needed.
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args) {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args) {
            SentryInit();
            // Add code to be executed when Playnite is initialized.
            SyncLib();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) {
            // Add code to be executed when Playnite is shutting down.
            SyncLib();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) {
            // Add code to be executed when library is updated.
            base.OnLibraryUpdated(args);
            SyncLib();
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GsPluginSettingsView(PlayniteApi, settings);
        }

        public override UserControl GetSettingsView(bool firstRunSettings) => new GsPluginSettingsView();

        public override IEnumerable<SidebarItem> GetSidebarItems() {
            // Return one or more SidebarItem objects
            yield return new SidebarItem {
                Type = (SiderbarItemType)1,
                Title = "Show My Data",
                Icon = new TextBlock { Text = "📋" }, // or a path to an image icon
                Opened = () => {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView(settings, PlayniteApi, GetPluginVersion());
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
            LibrarySync librarySync = new LibrarySync {
                user_id = settings.InstallID,
                library = library
            };

            // Send POST request and ensure success.
            SyncResponse syncResponse = await PostJsonAsync<SyncResponse>(
                "https://api.gamescrobbler.com/api/playnite/sync", librarySync, true);
            // Optionally, use syncResponse.result.added and syncResponse.result.updated as needed.
        }

        public static void SentryInit() {
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
                options.Debug = true;

                // This option is recommended. It enables Sentry's "Release Health" feature.
                options.AutoSessionTracking = true;

                options.CaptureFailedRequests = true;
                options.FailedRequestStatusCodes.Add((400, 499));

                // Set TracesSampleRate to 1.0 to capture 100%
                // of transactions for tracing.
                // We recommend adjusting this value in production.
                options.TracesSampleRate = 1.0;

                // Sample rate for profiling, applied on top of other TracesSampleRate,
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

        /// <summary>
        /// Helper method to POST JSON data to the specified URL and optionally ensure a successful response.
        /// All errors are captured to Sentry with extra context (the request body and, if available, the response body).
        /// </summary>
        /// <typeparam name="TResponse">The expected type of the response data.</typeparam>
        /// <param name="url">The target URL.</param>
        /// <param name="payload">The payload object to serialize as JSON.</param>
        /// <param name="ensureSuccess">If true, the response.EnsureSuccessStatusCode() is called.</param>
        /// <returns>The deserialized response object, or null if an exception occurs.</returns>
        private static async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, jsonOptions);
            using (var content = new StringContent(jsonData, Encoding.UTF8, "application/json")) {

                HttpResponseMessage response = null;
                string responseBody = null;

                try {
                    response = await httpClient.PostAsync(url, content).ConfigureAwait(false);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (ensureSuccess && !response.IsSuccessStatusCode) {
                        var httpEx = new HttpRequestException(
                            $"Request failed with status {(int)response.StatusCode} ({response.StatusCode}) for URL {url}");

                        SentrySdk.CaptureException(httpEx, scope => {
                            scope.SetExtra("RequestUrl", url);
                            scope.SetExtra("RequestBody", jsonData);
                            scope.SetExtra("ResponseStatus", (int)response.StatusCode);
                            scope.SetExtra("ResponseBody", responseBody);
                        });

                        return null;
                    }

                    return JsonSerializer.Deserialize<TResponse>(responseBody);
                }
                catch (Exception ex) {
                    SentrySdk.CaptureException(ex, scope => {
                        scope.SetExtra("RequestUrl", url);
                        scope.SetExtra("RequestBody", jsonData);
                        scope.SetExtra("ResponseStatus", response?.StatusCode);
                        scope.SetExtra("ResponseBody", responseBody);
                    });

                    return null;
                }
            }
        }
    }

    // --------------------
    // Request and Response Models
    // --------------------

    class TimeTracker {
        public string user_id { get; set; }
        public string game_name { get; set; }
        public string gameID { get; set; }
        public object metadata { get; set; }
        public string started_at { get; set; }
    };

    class TimeTrackerEnd {
        public string user_id { get; set; }
        public object metadata { get; set; }
        public string finished_at { get; set; }
        public string session_id { get; set; }
    };

    // A simple type representing the session data returned by the start scrobble endpoint.
    public class SessionData {
        public string session_id { get; set; }
    }

    // A type to represent the full library sync payload.
    class LibrarySync {
        public string user_id { get; set; }
        public List<Playnite.SDK.Models.Game> library { get; set; }
    }

    // Response model for the sync endpoint.
    public class SyncResponse {
        public string status { get; set; }
        public SyncResult result { get; set; }
    }

    public class SyncResult {
        public int added { get; set; }
        public int updated { get; set; }
    }

    // Response model for the finish scrobble endpoint.
    public class FinishScrobbleResponse {
        public string status { get; set; }
    }
}
