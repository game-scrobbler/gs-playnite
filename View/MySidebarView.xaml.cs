using System;
using System.Windows;
using System.Windows.Controls;
using GsPlugin;
using Playnite.SDK;   // For WebView2

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

            // Now you can navigate to an HTML string or a URL.
            // Example: injecting the same HTML snippet with the flourish iframe
            string userId = GSDataManager.Data.InstallID; // Or get it from plugin settings
            Boolean isDark = GSDataManager.Data.IsDark;
            // Use $@ for verbatim + interpolation
            string htmlContent = $@"
<html style='width:100%;height:100%;'>
  <head>
    <meta charset='UTF-8' />
  </head>
  <body style='margin:0; padding:0; width:100%;height:100%;'>
    <iframe
      src='https://playnite.gamescrobbler.com?user_id={userId}&plugin_version={viewPluginVer}&is_dark={isDark.ToString()}'
      frameborder='0'
      scrolling='yes'
      style='width:100%;height:100%;'
      sandbox='allow-same-origin allow-forms allow-scripts allow-downloads
               allow-popups allow-popups-to-escape-sandbox
               allow-top-navigation-by-user-activation'>
    </iframe>
  </body>
</html>
";

            // Navigate to the embedded HTML
            MyWebView2.CoreWebView2.NavigateToString(htmlContent);
        }
    }
}
