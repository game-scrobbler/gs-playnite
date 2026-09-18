using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using Microsoft.Web.WebView2.Core;

namespace GsPlugin.View {
    public partial class MySidebarView : UserControl, IDisposable {

        private readonly IGsApiClient _apiClient;
        private readonly string _userDataFolder;
        private readonly Action _openSettings;
        private bool _webView2Ready;
        private DateTime _lastNavigatedAtUtc = DateTime.MinValue;
        private bool _disposed;
        private bool _optedOutUiShown;

        /// <param name="userDataFolder">
        /// Private WebView2 profile directory. Without one, WebView2 falls back to a folder derived
        /// from the host process, which every Playnite extension hosting a WebView2 shares — so the
        /// cookie jar, localStorage and browsing history of the signed-in gamescrobbler.com dashboard
        /// (including the access_token carried in the URL) would be readable by any other extension.
        /// Passing null keeps the shared default, for callers that cannot supply a path.
        /// </param>
        public MySidebarView(IGsApiClient apiClient, string userDataFolder = null, Action openSettings = null) {
            InitializeComponent();
            _apiClient = apiClient;
            _userDataFolder = userDataFolder;
            _openSettings = openSettings;

            this.Loaded += MySidebarView_Loaded;
            this.IsVisibleChanged += MySidebarView_IsVisibleChanged;
            this.Unloaded += MySidebarView_Unloaded;
            GsDataManager.DiagnosticsStateChanged += OnDiagnosticsStateChanged;
        }

        private async void MySidebarView_Loaded(object sender, RoutedEventArgs e) {
            try {
                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }

                CoreWebView2Environment environment = null;
                if (!string.IsNullOrEmpty(_userDataFolder)) {
                    try {
                        Directory.CreateDirectory(_userDataFolder);
                        environment = await CoreWebView2Environment.CreateAsync(
                            browserExecutableFolder: null, userDataFolder: _userDataFolder);
                    }
                    catch (Exception envEx) {
                        // Fail closed. Falling back to the shared default used to look harmless
                        // because the dashboard still rendered, but the default profile is derived
                        // from the host process and shared with every other extension hosting a
                        // WebView2, so the fallback quietly handed them this dashboard's
                        // authenticated cookies and the access_token in its URL history, which is
                        // the exact exposure the private profile exists to prevent. A caller that
                        // asked for isolation gets isolation or an error, never a silent downgrade.
                        if (CannotLoadHub()) {
                            ShowOptedOutState();
                            return;
                        }
                        GsLogger.Error($"Could not create a private WebView2 profile: {envEx.Message}");
                        ShowErrorMessage(GsLocalization.Get("LOCGsPluginDashboardProfileFailed",
                            "Game Scrobbler could not open a private browser profile for the dashboard, "
                            + "so it was not loaded. Restart Playnite to try again."));
                        return;
                    }
                }

                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }

                await MyWebView2.EnsureCoreWebView2Async(environment);

                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }

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

                MyWebView2.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                MyWebView2.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                MyWebView2.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                _webView2Ready = true;
                await NavigateToDashboard();
            }
            catch (Exception ex) {
                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }
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

        private async void MySidebarView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (!(bool)e.NewValue || !_webView2Ready || CannotLoadHub()) {
                if ((bool)e.NewValue && (GsDataManager.IsOptedOut || GsDataManager.PendingRestartAfterOptIn)) {
                    ShowOptedOutState();
                }
                return;
            }
            if ((DateTime.UtcNow - _lastNavigatedAtUtc).TotalMinutes > 8) {
                GsLogger.Info("Sidebar became visible after token likely expired — refreshing dashboard");
                await NavigateToDashboard();
            }
        }

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
        /// Fetches a fresh dashboard token and navigates the WebView2 to the hub.
        /// Only theme is passed as a URL param (cosmetic, needed for instant rendering).
        /// All other context is sent server-side via the POST /v2/dashboard-token body.
        /// </summary>
        private async System.Threading.Tasks.Task NavigateToDashboard() {
            try {
                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }

                string theme = Uri.EscapeDataString((GsDataManager.Data.Theme ?? "Dark").ToLower());

                string url;
                bool hasInstallToken = !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

                if (hasInstallToken) {
                    var dashboardToken = _apiClient != null
                        ? await _apiClient.GetDashboardToken()
                        : null;

                    if (CannotLoadHub()) {
                        ShowOptedOutState();
                        return;
                    }

                    if (!string.IsNullOrEmpty(dashboardToken)) {
                        url = $"https://gamescrobbler.com/dashboard/hub?access_token={Uri.EscapeDataString(dashboardToken)}&theme={theme}";
                        GsLogger.Info("Dashboard URL built with access_token (install UUID not in URL)");
                    }
                    else {
                        GsLogger.Error("GetDashboardToken failed for a registered install; aborting dashboard navigation");
                        ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                        return;
                    }
                }
                else {
                    GsLogger.Error("NavigateToDashboard called without install token; aborting");
                    ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                    return;
                }

                if (MyWebView2?.CoreWebView2 == null) {
                    ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
                    return;
                }

                _lastNavigatedAtUtc = DateTime.UtcNow;
                MyWebView2.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) {
                if (CannotLoadHub()) {
                    ShowOptedOutState();
                    return;
                }
                GsLogger.Error("Failed to navigate to dashboard", ex);
                GsSentry.CaptureException(ex, "Failed to navigate to dashboard");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
            }
        }

        private void OnDiagnosticsStateChanged(object sender, EventArgs e) {
            if (!GsDataManager.IsOptedOut) {
                return;
            }
            Dispatcher.BeginInvoke(new Action(ShowOptedOutState));
        }

        private bool CannotLoadHub() {
            return _optedOutUiShown || _disposed || GsDataManager.IsOptedOut
                || GsDataManager.PendingRestartAfterOptIn;
        }

        private void ShowOptedOutState() {
            if (_optedOutUiShown || _disposed) {
                return;
            }
            _optedOutUiShown = true;
            _webView2Ready = false;
            TearDownWebView();
            try {
                var grid = (Grid)Content;
                grid.Children.Clear();
                grid.Children.Add(new OptedOutView(_apiClient, _openSettings));
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to show opted-out dashboard: {ex.Message}");
            }
        }

        private void MySidebarView_Unloaded(object sender, RoutedEventArgs e) {
            Dispose();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            try {
                GsDataManager.DiagnosticsStateChanged -= OnDiagnosticsStateChanged;
                this.Loaded -= MySidebarView_Loaded;
                this.IsVisibleChanged -= MySidebarView_IsVisibleChanged;
                this.Unloaded -= MySidebarView_Unloaded;
                TearDownWebView();
            }
            catch (Exception ex) {
                GsLogger.Warn($"Error disposing MySidebarView: {ex.Message}");
            }
        }

        private void TearDownWebView() {
            try {
                if (MyWebView2?.CoreWebView2 != null) {
                    MyWebView2.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    MyWebView2.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    MyWebView2.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }
                MyWebView2?.Dispose();
            }
            catch (Exception ex) {
                GsLogger.Warn($"Error tearing down sidebar WebView2: {ex.Message}");
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
