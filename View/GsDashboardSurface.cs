using System;
using System.Windows.Controls;
using GsPlugin.Api;
using GsPlugin.Models;

namespace GsPlugin.View {
    /// <summary>
    /// Picks the dashboard control for sidebar, theme embed, and the Extensions window.
    /// Opted-out installs get a native panel so WebView2 never talks to the server.
    /// </summary>
    internal static class GsDashboardSurface {
        public static Control Create(
            IGsApiClient apiClient,
            string webViewUserDataFolder,
            Action openSettings) {
            if (GsDataManager.IsTrackingPaused) {
                return new OptedOutView(apiClient, openSettings);
            }
            return new MySidebarView(apiClient, webViewUserDataFolder, openSettings);
        }
    }
}
