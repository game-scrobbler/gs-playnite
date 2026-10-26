using System.Threading.Tasks;
using System.Windows;
using Playnite;

namespace GsPlugin.View {
    /// <summary>
    /// Hosts the dashboard surface (WebView2 hub or the native opted-out panel, chosen by
    /// <see cref="GsDashboardSurface"/>) as a Playnite 11 app view.
    /// </summary>
    public class GsDashboardView : AppViewItem {
        public GsDashboardView(FrameworkElement surface) {
            View = surface;
        }

        public override async Task ActivateViewAsync(ActivateViewAsyncArgs args) {
            if (View is MySidebarView sv) {
                await sv.OnActivatedAsync();
            }
        }

        public override async Task DeactivateViewAsync(DeactivateViewAsyncArgs args) {
            await Task.CompletedTask;
        }
    }
}
