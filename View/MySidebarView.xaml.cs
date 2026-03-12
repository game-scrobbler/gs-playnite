using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;

namespace GsPlugin.View {
    public partial class MySidebarView : UserControl {

        private string viewPluginVer { get; set; }
        private readonly IGsApiClient _apiClient;

        public MySidebarView(string pluginVersion, IGsApiClient apiClient) {
            InitializeComponent();
            viewPluginVer = pluginVersion;
            _apiClient = apiClient;

            // One approach is to wait until the control is actually loaded in the visual tree.
            this.Loaded += MySidebarView_Loaded;
        }

        private async void MySidebarView_Loaded(object sender, RoutedEventArgs e) {
            try {
                // Ensure the CoreWebView2 is ready to receive commands
                await MyWebView2.EnsureCoreWebView2Async();

                if (MyWebView2?.CoreWebView2 == null) {
                    GsLogger.Error("WebView2 initialization failed: CoreWebView2 is null after initialization");
                    ShowErrorMessage("Failed to load Game Scrobbler dashboard. WebView2 runtime may not be installed.");
                    return;
                }

                // Restrict navigation to gamescrobbler.com domains only
                MyWebView2.CoreWebView2.NavigationStarting += (s, args) => {
                    if (args.Uri != null) {
                        try {
                            var uri = new Uri(args.Uri);
                            if (uri.Host != "gamescrobbler.com" && !uri.Host.EndsWith(".gamescrobbler.com")) {
                                args.Cancel = true;
                                // Only open https links in the system browser
                                if (uri.Scheme == "https") {
                                    Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                                }
                            }
                        }
                        catch (UriFormatException) {
                            args.Cancel = true;
                        }
                    }
                };

                MyWebView2.CoreWebView2.NewWindowRequested += (s, args) => {
                    args.Handled = true;
                    try {
                        var uri = new Uri(args.Uri);
                        // Only open https links in the system browser
                        if (uri.Scheme == "https") {
                            Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                        }
                    }
                    catch (Exception ex) {
                        GsLogger.Warn($"Failed to open new window URL in browser: {ex.Message}");
                    }
                };

                // Build common query parameters
                string theme = Uri.EscapeDataString((GsDataManager.Data.Theme ?? "Dark").ToLower());
                bool isScrobblingDisabled = GsDataManager.Data.Flags.Contains("no-scrobble");
                bool isSentryDisabled = GsDataManager.Data.Flags.Contains("no-sentry");
                bool newDashboard = GsDataManager.Data.NewDashboardExperience;
                bool syncAchievements = GsDataManager.Data.SyncAchievements;
                string commonParams = $"plugin_version={Uri.EscapeDataString(viewPluginVer)}&theme={theme}&scrobbling_disabled={isScrobblingDisabled.ToString().ToLower()}&sentry_disabled={isSentryDisabled.ToString().ToLower()}&new_dashboard={newDashboard.ToString().ToLower()}&sync_achievements={syncAchievements.ToString().ToLower()}";

                string url;
                bool hasInstallToken = !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

                if (hasInstallToken) {
                    // Install is registered — request a short-lived dashboard token to keep
                    // the install UUID out of the browser URL and history.
                    var dashboardToken = _apiClient != null
                        ? await _apiClient.GetDashboardToken()
                        : null;

                    if (!string.IsNullOrEmpty(dashboardToken)) {
                        url = $"https://gamescrobbler.com/dashboard/playnite?access_token={Uri.EscapeDataString(dashboardToken)}&{commonParams}";
                        GsLogger.Info("Dashboard URL built with access_token (install UUID not in URL)");
                    }
                    else {
                        // Dashboard-token request failed (network/server error) — fail closed
                        // rather than falling back to the user_id URL and leaking the install UUID.
                        GsLogger.Error("GetDashboardToken failed for a registered install; aborting dashboard navigation");
                        ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                        return;
                    }
                }
                else {
                    // No install token yet — use install ID (pre-registration behaviour).
                    string userId = Uri.EscapeDataString(GsDataManager.Data.InstallID);
                    url = $"https://gamescrobbler.com/dashboard/playnite?user_id={userId}&{commonParams}";
                    GsLogger.Warn("Dashboard URL built with user_id fallback (install not yet registered)");
                }

                // Navigate to the URL
                MyWebView2.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to initialize sidebar WebView2", ex);
                GsSentry.CaptureException(ex, "Failed to initialize sidebar WebView2");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please check that WebView2 runtime is installed.");
            }
        }

        private void ShowErrorMessage(string message) {
            try {
                var grid = (Grid)Content;
                grid.Children.Clear();
                grid.Children.Add(new TextBlock {
                    Text = message,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            catch {
                // Silently fail if we can't show the error UI
            }
        }
    }
}
