using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GsPlugin.View {
    public partial class MySidebarView : UserControl, IDisposable {
        private readonly IGsApiClient _apiClient;
        private readonly string? _userDataFolder;
        private bool _webView2Ready;
        private DateTime _lastNavigatedAtUtc = DateTime.MinValue;
        private bool _disposed;
        // Created programmatically to avoid a XAML compile-time reference to Microsoft.Web.WebView2.Wpf.
        private WebView2 _browser;

        /// <param name="userDataFolder">
        /// Private WebView2 profile directory. Without one, WebView2 falls back to a folder derived
        /// from the host process, which every Playnite extension hosting a WebView2 shares — so the
        /// cookie jar, localStorage and browsing history of the signed-in gamescrobbler.com dashboard
        /// (including the access_token carried in the URL) would be readable by any other extension.
        /// Passing null keeps the shared default, for callers that cannot supply a path.
        /// </param>
        public MySidebarView(IGsApiClient apiClient, string? userDataFolder = null) {
            InitializeComponent();
            _apiClient = apiClient;
            _userDataFolder = userDataFolder;

            _browser = new WebView2();
            BrowserHost.Children.Add(_browser);

            this.IsVisibleChanged += MySidebarView_IsVisibleChanged;
            this.Unloaded += MySidebarView_Unloaded;
        }

        /// <summary>
        /// Called by GsDashboardView.ActivateViewAsync when the sidebar becomes active.
        /// Navigates to the dashboard on first load or if the token has likely expired.
        /// </summary>
        public async Task OnActivatedAsync() {
            if (_lastNavigatedAtUtc == DateTime.MinValue
                || (DateTime.UtcNow - _lastNavigatedAtUtc).TotalMinutes > 8) {
                GsLogger.Info("Dashboard activation - navigating to dashboard");
                await NavigateToDashboard();
            }
        }

        private async void MySidebarView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if ((bool)e.NewValue && _lastNavigatedAtUtc != DateTime.MinValue) {
                if ((DateTime.UtcNow - _lastNavigatedAtUtc).TotalMinutes > 8) {
                    GsLogger.Info("Sidebar became visible after token likely expired — refreshing dashboard");
                    await NavigateToDashboard();
                }
            }
        }

        /// <summary>
        /// Called by the frontend refresh-token postMessage.
        /// </summary>
        internal async Task HandleRefreshTokenMessage() {
            GsLogger.Info("Received refresh-token request from dashboard");
            await NavigateToDashboard();
        }

        private async Task NavigateToDashboard() {
            try {
                string theme = Uri.EscapeDataString((GsDataManager.Data.Theme ?? "Dark").ToLower());
                bool hasInstallToken = !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

                string url;
                if (hasInstallToken) {
                    var dashboardToken = _apiClient != null
                        ? await _apiClient.GetDashboardToken()
                        : null;

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

                if (!await EnsureWebViewReadyAsync()) {
                    return;
                }
                var core = _browser.CoreWebView2!;

                core.NavigationStarting -= OnNavigationStarting;
                core.NavigationStarting += OnNavigationStarting;
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NavigationCompleted += OnNavigationCompleted;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.NewWindowRequested += OnNewWindowRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
                core.WebMessageReceived += OnWebMessageReceived;

                _lastNavigatedAtUtc = DateTime.UtcNow;
                core.Navigate(url);
            }
            catch (Exception ex) {
                GsLogger.Error("Failed to navigate to dashboard", ex);
                GsSentry.CaptureException(ex, "Failed to navigate to dashboard");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. Please try again later.");
            }
        }

        /// <summary>
        /// Brings CoreWebView2 up against the private user-data profile and applies the
        /// Release hardening settings. Returns false when the view could not be brought up
        /// safely, in which case an error message is already on screen.
        /// </summary>
        private async Task<bool> EnsureWebViewReadyAsync() {
            if (_webView2Ready) {
                return true;
            }

            CoreWebView2Environment? environment = null;
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
                    GsLogger.Error($"Could not create a private WebView2 profile: {envEx.Message}");
                    ShowErrorMessage(Loc.dashboard_profile_failed());
                    return false;
                }
            }

            await _browser.EnsureCoreWebView2Async(environment);

            if (_browser.CoreWebView2 == null) {
                GsLogger.Error("WebView2 initialization failed: CoreWebView2 is null after initialization");
                ShowErrorMessage("Failed to load Game Scrobbler dashboard. WebView2 runtime may not be installed.");
                return false;
            }

            // Harden WebView2 in Release builds; DevTools stay available in Debug.
            var settings = _browser.CoreWebView2.Settings;
#if !DEBUG
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
#endif
            settings.IsStatusBarEnabled = false;

            _webView2Ready = true;
            return true;
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e) {
            try {
                var uri = new Uri(e.Uri);
                bool isTrustedHost = uri.Host == "gamescrobbler.com" || uri.Host.EndsWith(".gamescrobbler.com");
                // Require https even for the trusted host: an on-path attacker (e.g. open
                // Wi-Fi) could otherwise serve arbitrary content over plain http inside this
                // trusted, chrome-less sidebar frame.
                if (isTrustedHost && uri.Scheme == "https") {
                    return; // Allow navigation
                }
                e.Cancel = true;
                if (uri.Scheme == "https" && GsPlayniteHelper.IsTrustedUrl(e.Uri)) {
                    Application.Current.Dispatcher.Invoke(() =>
                        Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }));
                }
                else if (uri.Scheme == "https") {
                    GsLogger.Warn($"Blocked untrusted external URL: {uri.Host}");
                }
            }
            catch (UriFormatException) {
                e.Cancel = true;
            }
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) {
            e.Handled = true; // Suppress popup
            try {
                var uri = new Uri(e.Uri);
                if (uri.Scheme == "https" && GsPlayniteHelper.IsTrustedUrl(e.Uri)) {
                    Application.Current.Dispatcher.Invoke(() =>
                        Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }));
                }
                else if (uri.Scheme == "https") {
                    GsLogger.Warn($"Blocked untrusted new-window URL: {uri.Host}");
                }
            }
            catch (Exception ex) {
                GsLogger.Warn($"Failed to open new window URL in browser: {ex.Message}");
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e) {
            if (e.TryGetWebMessageAsString() == "gs:refresh-token") {
                _ = HandleRefreshTokenMessage();
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) {
            if (!e.IsSuccess) return;

            // Inject postMessage shim: forwards "gs:*" messages to C# via WebMessageReceived.
            const string script = @"
                (function() {
                    var _orig = window.postMessage.bind(window);
                    window.postMessage = function(msg, target) {
                        if (typeof msg === 'string' && msg === 'gs:refresh-token') {
                            window.chrome.webview.postMessage('gs:refresh-token');
                        } else {
                            _orig(msg, target || '*');
                        }
                    };
                })();
            ";
            _ = _browser.CoreWebView2!.ExecuteScriptAsync(script);
        }

        private void MySidebarView_Unloaded(object sender, RoutedEventArgs e) {
            Dispose();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            try {
                this.IsVisibleChanged -= MySidebarView_IsVisibleChanged;
                this.Unloaded -= MySidebarView_Unloaded;
                var core = _browser.CoreWebView2;
                if (core != null) {
                    core.NavigationStarting -= OnNavigationStarting;
                    core.NavigationCompleted -= OnNavigationCompleted;
                    core.NewWindowRequested -= OnNewWindowRequested;
                    core.WebMessageReceived -= OnWebMessageReceived;
                }
                _browser.Dispose();
            }
            catch (Exception ex) {
                GsLogger.Warn($"Error disposing MySidebarView: {ex.Message}");
            }
        }

        private void ShowErrorMessage(string message) {
            try {
                Application.Current.Dispatcher.Invoke(() => {
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
                });
            }
            catch { }
        }
    }
}
