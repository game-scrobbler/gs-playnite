using System.Threading.Tasks;
using Playnite;
using GsPlugin.Api;

namespace GsPlugin.View {
    public class GsDashboardView : AppViewItem {
        public GsDashboardView(IGsApiClient apiClient) {
            View = new MySidebarView(apiClient);
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
