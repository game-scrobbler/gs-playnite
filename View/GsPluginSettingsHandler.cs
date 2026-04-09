using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Playnite;
using GsPlugin.Models;

namespace GsPlugin.View {
    public class GsPluginSettingsHandler : PluginSettingsHandler {
        private readonly GsPluginSettingsViewModel _viewModel;

        public GsPluginSettingsHandler(GsPluginSettingsViewModel viewModel) {
            _viewModel = viewModel;
        }

        public override FrameworkElement GetEditView(GetSettingsViewArgs args)
            => new GsPluginSettingsView { DataContext = _viewModel };

        public override async Task BeginEditAsync(BeginEditArgs args) {
            await Task.CompletedTask;
            _viewModel.BeginEdit();
        }

        public override async Task EndEditAsync(EndEditArgs args) {
            await Task.CompletedTask;
            _viewModel.EndEdit();
        }

        public override async Task CancelEditAsync(CancelEditArgs args) {
            await Task.CompletedTask;
            _viewModel.CancelEdit();
        }

        public override async Task<ICollection<string>> VerifySettingsAsync(VerifySettingsArgs args) {
            await Task.CompletedTask;
            _viewModel.VerifySettings(out var errors);
            return errors;
        }
    }
}
