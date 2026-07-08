using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using Microsoft.Web.WebView2.Core;

namespace GsPlugin.View {
    public partial class MySidebarView : UserControl, IDisposable {

        private readonly IGsApiClient _apiClient;
        private bool _webView2Ready;
        private DateTime _lastNavigatedAtUtc = DateTime.MinValue;
        private bool _disposed;

        public MySidebarView(IGsApiClient apiClient) {
            InitializeComponent();
            _apiClient = apiClient;

            // One approach is to wait until the control is actually loaded in the visual tree.
            this.Loaded += MySidebarView_Loaded;
            this.IsVisibleChanged += MySidebarView_IsVisibleChanged;
            this.Unloaded += MySidebarView_Unloaded;
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

                // Harden WebView2 security in Release builds.
                // Keep DevTools enabled in Debug for development.
                var settings = MyWebView2.CoreWebView2.Settings;
#if !DEBUG
                settings.AreDevToolsEnabled = false;
                settings.AreHostObjectsAllowed = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
#endif
                settings.IsStatusBarEnabled = false;

                // Restrict navigation to gamescrobbler.com domains only
                MyWebView2.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                MyWebView2.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                // Listen for messages from the frontend (e.g. "gs:refresh-token" when
                // the dashboard session expires and the user clicks Retry).
                MyWebView2.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                _webView2Ready = true;
                await NavigateToDashboard();
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to initialize sidebar WebView2", ex);
                GsSentry.CaptureException(ex, "Failed to initialize sidebar WebView2");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please check that WebView2 runtime is installed.");
            }
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args) {
            if (args.Uri != null) {
                try {
                    var uri = new Uri(args.Uri);
                    bool isTrustedHost = uri.Host == "gamescrobbler.com" || uri.Host.EndsWith(".gamescrobbler.com");
                    // Require https even for the trusted host: an on-path attacker (e.g. open
                    // Wi-Fi) could otherwise serve arbitrary content over plain http inside this
                    // trusted, chrome-less sidebar frame.
                    if (!isTrustedHost || uri.Scheme != "https") {
                        args.Cancel = true;
                        // Only open trusted https links in the system browser
                        if (uri.Scheme == "https" && GsPlayniteHelper.IsTrustedUrl(args.Uri)) {
                            Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                        }
                        else if (uri.Scheme == "https") {
                            GsLogger.Warn($"Blocked untrusted external URL: {uri.Host}");
                        }
                    }
                }
                catch (UriFormatException) {
                    args.Cancel = true;
                }
            }
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs args) {
            args.Handled = true;
            try {
                var uri = new Uri(args.Uri);
                // Only open trusted https links in the system browser
                if (uri.Scheme == "https" && GsPlayniteHelper.IsTrustedUrl(args.Uri)) {
                    Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                }
                else if (uri.Scheme == "https") {
                    GsLogger.Warn($"Blocked untrusted new-window URL: {uri.Host}");
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to open new window URL in browser: {ex.Message}");
            }
        }

        /// <summary>
        /// When the sidebar becomes visible again after being hidden, re-navigate
        /// with a fresh dashboard token if the previous one has likely expired
        /// (dashboard tokens have a 10-minute TTL).
        /// </summary>
        private async void MySidebarView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if ((bool)e.NewValue && _webView2Ready) {
                if ((DateTime.UtcNow - _lastNavigatedAtUtc).TotalMinutes > 8) {
                    GsLogger.Info("Sidebar became visible after token likely expired — refreshing dashboard");
                    await NavigateToDashboard();
                }
            }
        }

        /// <summary>
        /// Handles postMessage calls from the frontend. The dashboard sends
        /// "gs:refresh-token" when the session has expired and the user clicks Retry,
        /// so the plugin can fetch a fresh dashboard token and re-navigate.
        /// Only the known "gs:" protocol prefix is accepted.
        /// </summary>
        private async void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
            try {
                string message = e.TryGetWebMessageAsString();
                if (message == "gs:refresh-token") {
                    GsLogger.Info("Received refresh-token request from dashboard");
                    await NavigateToDashboard();
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to process web message: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches a fresh dashboard token (or falls back to user_id for legacy installs)
        /// and navigates the WebView2 to the dashboard URL.
        /// Only theme is passed as a URL param (cosmetic, needed for instant rendering).
        /// All other context (plugin_version, flags, preferences) is sent server-side
        /// via the POST /v2/dashboard-token body and returned to the frontend when
        /// the token is resolved — tamper-proof.
        /// </summary>
        private async System.Threading.Tasks.Task NavigateToDashboard() {
            try {
                string theme = Uri.EscapeDataString((GsDataManager.Data.Theme ?? "Dark").ToLower());

                string url;
                bool hasInstallToken = !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

                if (hasInstallToken) {
                    // Install is registered — request a short-lived dashboard token to keep
                    // the install UUID out of the browser URL and history.
                    // The POST body includes dashboard context (plugin_version, flags, etc.)
                    // which the server stores alongside the token.
                    var dashboardToken = _apiClient != null
                        ? await _apiClient.GetDashboardToken()
                        : null;

                    if (!string.IsNullOrEmpty(dashboardToken)) {
                        url = $"https://gamescrobbler.com/dashboard/hub?access_token={Uri.EscapeDataString(dashboardToken)}&theme={theme}";
                        GsLogger.Info("Dashboard URL built with access_token (install UUID not in URL)");
                    }
                    else {
                        // Dashboard-token request failed (network/server error) — fail closed.
                        GsLogger.Error("GetDashboardToken failed for a registered install; aborting dashboard navigation");
                        ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                        return;
                    }
                }
                else {
                    // No install token yet — should not happen (EnsureInstallTokenAsync runs
                    // before sidebar is accessible), but handle gracefully.
                    GsLogger.Error("NavigateToDashboard called without install token; aborting");
                    ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                    return;
                }

                _lastNavigatedAtUtc = DateTime.UtcNow;
                MyWebView2.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to navigate to dashboard", ex);
                GsSentry.CaptureException(ex, "Failed to navigate to dashboard");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
            }
        }

        private void MySidebarView_Unloaded(object sender, RoutedEventArgs e) {
            Dispose();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            try {
                this.Loaded -= MySidebarView_Loaded;
                this.IsVisibleChanged -= MySidebarView_IsVisibleChanged;
                this.Unloaded -= MySidebarView_Unloaded;

                if (MyWebView2?.CoreWebView2 != null) {
                    MyWebView2.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    MyWebView2.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    MyWebView2.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }

                MyWebView2?.Dispose();
            }
            catch (Exception ex) {
                GsLogger.Warn($"Error disposing MySidebarView: {ex.Message}");
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
