using System;
using System.Threading.Tasks;
using System.Windows;
using GsPlugin.Infrastructure;
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

        /// <summary>
        /// Closing Playnite never raises <c>Unloaded</c>, so this is the only point the WebView2
        /// controller gets closed on exit. Without it the browser processes of the private
        /// profile outlived Playnite: five msedgewebview2 processes were still running two
        /// minutes after a shutdown in alpha 11.0.11.0.
        /// </summary>
        public override async ValueTask DisposeAsync() {
            if (View is IDisposable disposable) {
                try {
                    if (View.Dispatcher.CheckAccess()) {
                        disposable.Dispose();
                    }
                    else {
                        await View.Dispatcher.InvokeAsync(disposable.Dispose);
                    }

                    GsLogger.Info("Dashboard view disposed");
                }
                catch (Exception ex) {
                    GsLogger.Warn($"Failed to dispose the dashboard view: {ex.Message}");
                }
            }

            await base.DisposeAsync();
        }
    }
}
