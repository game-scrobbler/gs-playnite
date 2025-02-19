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
            string userId = GsDataManager.Data.InstallID; // Or get it from plugin settings
            string theme = GsDataManager.Data.Theme.ToLower();
#if DEBUG
            MessageBox.Show($"Debug Info:\nUser ID: {userId}\nTheme: {theme}\nPlugin Version: {viewPluginVer}");
#endif
            string url = $"https://playnite.gamescrobbler.com?user_id={userId}&plugin_version={viewPluginVer}&theme={theme}";

            // Navigate to the URL
            MyWebView2.CoreWebView2.Navigate(url);
        }
    }
}
