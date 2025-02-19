using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Sentry;

namespace GsPlugin {
    public class GsApiClient {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private static readonly string _apiBaseUrl = "https://api.gamescrobbler.com";
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        public GsApiClient() {
            _httpClient = new HttpClient(new SentryHttpMessageHandler());
            _jsonOptions = new JsonSerializerOptions {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public async Task<GameSessionResponse> StartGameSession(TimeTracker startData) {
            return await PostJsonAsync<GameSessionResponse>(
                $"{_apiBaseUrl}/api/playnite/scrobble/start", startData);
        }

        public async Task<FinishScrobbleResponse> FinishGameSession(TimeTrackerEnd endData) {
            return await PostJsonAsync<FinishScrobbleResponse>(
                $"{_apiBaseUrl}/api/playnite/scrobble/finish", endData, true);
        }

        public async Task<SyncResponse> SyncLibrary(LibrarySync librarySync) {
            return await PostJsonAsync<SyncResponse>(
                $"{_apiBaseUrl}/api/playnite/sync", librarySync, true);
        }

        private async Task<TResponse> PostJsonAsync<TResponse>(string url, object payload, bool ensureSuccess = false)
            where TResponse : class {
            string jsonData = JsonSerializer.Serialize(payload, _jsonOptions);
            using (var content = new StringContent(jsonData, Encoding.UTF8, "application/json")) {
                HttpResponseMessage response = null;
                string responseBody = null;

                try {
                    response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

#if DEBUG
                    ShowDebugNotification($"Request URL: {url}\nPayload: {jsonData}\nResponse Status: {response.StatusCode}\nBody: {responseBody}");
#endif

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
#if DEBUG
                    ShowDebugNotification($"Error: {ex.Message}\nStack Trace: {ex.StackTrace}");
#endif

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

#if DEBUG
        private static void ShowDebugNotification(string message, string title = "HTTP Debug") {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                var messageBox = new TextBox {
                    Text = message,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    IsReadOnly = false,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    Background = System.Windows.Media.Brushes.Black,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(5)
                };

                var toast = new Window {
                    Title = title,
                    Content = messageBox,
                    Width = 600,
                    Height = 400,
                    WindowStyle = WindowStyle.SingleBorderWindow, // This gives us the native title bar with close button
                    Background = System.Windows.Media.Brushes.Black,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = true,
                    Topmost = true,
                    ResizeMode = ResizeMode.CanResizeWithGrip
                };

                // Create fade-out animation
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(0.3)),
                    BeginTime = TimeSpan.FromSeconds(3)
                };

                bool isMouseOver = false;
                toast.MouseEnter += (s, e) => {
                    isMouseOver = true;
                    toast.BeginAnimation(UIElement.OpacityProperty, null);
                    toast.Opacity = 1.0;
                };

                toast.MouseLeave += (s, e) => {
                    isMouseOver = false;
                    toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                };

                fadeOut.Completed += (s, e) => {
                    if (!isMouseOver) {
                        toast.Close();
                    }
                };

                toast.Show();
                toast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }));
        }
#endif

        #region API Models

        /// <summary>
        /// Model for tracking game start events.
        /// </summary>
        public class TimeTracker {
            public string user_id { get; set; }
            public string game_name { get; set; }
            public string gameID { get; set; }
            public object metadata { get; set; }
            public string started_at { get; set; }
        }

        /// <summary>
        /// Model for tracking game end events.
        /// </summary>
        public class TimeTrackerEnd {
            public string user_id { get; set; }
            public object metadata { get; set; }
            public string finished_at { get; set; }
            public string session_id { get; set; }
        }

        /// <summary>
        /// Response model for start scrobble request.
        /// </summary>
        public class GameSessionResponse {
            public string SessionId { get; set; }
        }

        /// <summary>
        /// Model for library synchronization request.
        /// </summary>
        public class LibrarySync {
            public string user_id { get; set; }
            public List<Playnite.SDK.Models.Game> library { get; set; }
            public string[] flags { get; set; }
        }

        /// <summary>
        /// Response model for library synchronization.
        /// </summary>
        public class SyncResponse {
            public string status { get; set; }
            public SyncResult result { get; set; }
        }

        /// <summary>
        /// Details of library synchronization results.
        /// </summary>
        public class SyncResult {
            public int added { get; set; }
            public int updated { get; set; }
        }

        /// <summary>
        /// Response model for the finish scrobble endpoint.
        /// </summary>
        public class FinishScrobbleResponse {
            public string status { get; set; }
        }

        #endregion
    }
}
