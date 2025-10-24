using System;
using System.Windows;
using System.Windows.Controls;
using GsPlugin;

namespace MySidebarPlugin {
    public partial class MySidebarView : UserControl {

        private string viewPluginVer { get; set; }
        public MySidebarView(string pluginVersion) {
            InitializeComponent();

            // One approach is to wait until the control is actually loaded in the visual tree.
            this.Loaded += MySidebarView_Loaded;
            viewPluginVer = pluginVersion;
        }

        private async void MySidebarView_Loaded(object sender, RoutedEventArgs e) {
            // Ensure the CoreWebView2 is ready to receive commands
            await MyWebView2.EnsureCoreWebView2Async();

            // Now you can navigate to a URL directly.
            // Properly encode all URL parameters to prevent injection attacks
            string userId = Uri.EscapeDataString(GsDataManager.Data.InstallID);
            string theme = Uri.EscapeDataString(GsDataManager.Data.Theme.ToLower());
            bool isScrobblingDisabled = GsDataManager.Data.Flags.Contains("no-scrobble");
            bool isSentryDisabled = GsDataManager.Data.Flags.Contains("no-sentry");
            string url = $"https://gamescrobbler.com/game-spectrum?user_id={userId}&plugin_version={Uri.EscapeDataString(viewPluginVer)}&theme={theme}&scrobbling_disabled={isScrobblingDisabled.ToString().ToLower()}&sentry_disabled={isSentryDisabled.ToString().ToLower()}";

            // Navigate to the URL
            MyWebView2.CoreWebView2.Navigate(url);
        }
    }
}
