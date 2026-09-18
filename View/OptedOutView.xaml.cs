using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using GsPlugin.Services;

namespace GsPlugin.View {
    /// <summary>
    /// Native opted-out dashboard. Replaces the WebView2 hub so an opted-out
    /// install never loads gamescrobbler.com, and explains how to opt back in.
    /// </summary>
    public partial class OptedOutView : UserControl {
        private static readonly SolidColorBrush SuccessBrush = CreateFrozenBrush(76, 175, 80);
        private static readonly SolidColorBrush DangerBrush = CreateFrozenBrush(229, 57, 53);

        private readonly IGsApiClient _apiClient;
        private readonly Action _openSettings;
        private bool _busy;

        public OptedOutView(IGsApiClient apiClient, Action openSettings = null) {
            InitializeComponent();
            _apiClient = apiClient;
            _openSettings = openSettings;

            if (_openSettings == null) {
                OpenSettingsButton.Visibility = Visibility.Collapsed;
            }

            Loaded += OptedOutView_Loaded;
            Unloaded += OptedOutView_Unloaded;
        }

        private void OptedOutView_Loaded(object sender, RoutedEventArgs e) {
            GsDataManager.DiagnosticsStateChanged -= OnDiagnosticsStateChanged;
            GsDataManager.DiagnosticsStateChanged += OnDiagnosticsStateChanged;
            ApplyState();
        }

        private void OptedOutView_Unloaded(object sender, RoutedEventArgs e) {
            GsDataManager.DiagnosticsStateChanged -= OnDiagnosticsStateChanged;
        }

        private void OnDiagnosticsStateChanged(object sender, EventArgs e) {
            Dispatcher.BeginInvoke(new Action(ApplyState));
        }

        private void ApplyState() {
            if (GsDataManager.IsOptedOut) {
                ShowOptedOutCopy();
                return;
            }
            ShowRestartCopy();
        }

        private void ShowOptedOutCopy() {
            TitleText.Text = GsLocalization.Get("LOCGsPluginOptedOutTitle", "Tracking is off");
            BodyText.Text = GsLocalization.Get(
                "LOCGsPluginOptedOutBody",
                "You opted out of Game Scrobbler. Your Playnite library, sessions, and achievements were deleted from our servers, and this dashboard no longer loads any stats.");
            HintText.Text = GsLocalization.Get(
                "LOCGsPluginOptedOutHint",
                "Nothing is being synced. You can opt back in whenever you want.");
            HintText.Visibility = Visibility.Visible;
            OptBackInButton.Visibility = Visibility.Visible;
            OptBackInButton.IsEnabled = !_busy;
        }

        private void ShowRestartCopy() {
            TitleText.Text = GsLocalization.Get(
                "LOCGsPluginOptedOutRestartTitle",
                "Restart Playnite to continue");
            BodyText.Text = GsLocalization.Get(
                "LOCGsPluginOptedOutRestartBody",
                "Game Scrobbler is on again. Close and reopen Playnite to restore the dashboard and start syncing.");
            HintText.Visibility = Visibility.Collapsed;
            OptBackInButton.Visibility = Visibility.Collapsed;
        }

        private async void OptBackIn_Click(object sender, RoutedEventArgs e) {
            if (_busy) {
                return;
            }

            var confirmed = MessageBox.Show(
                GsLocalization.Get(
                    "LOCGsPluginOptBackInConfirmBody",
                    "Re-enable the GameScrobbler plugin?\n\nYou will need to restart Playnite for all features to resume."),
                GsLocalization.Get("LOCGsPluginOptBackInConfirmTitle", "Opt Back In"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmed != MessageBoxResult.Yes) {
                return;
            }

            _busy = true;
            OptBackInButton.IsEnabled = false;
            SetStatus(
                GsLocalization.Get("LOCGsPluginOptBackInRequesting", "Re-enabling..."),
                isError: false);

            var outcome = await GsOptBackIn.TryAsync(_apiClient);
            SetStatus(GsOptBackIn.MessageFor(outcome), isError: outcome != GsOptBackIn.Outcome.Success);
            _busy = false;

            if (outcome == GsOptBackIn.Outcome.Success) {
                ShowRestartCopy();
                return;
            }

            OptBackInButton.IsEnabled = true;
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e) {
            _openSettings?.Invoke();
        }

        private void SetStatus(string message, bool isError) {
            StatusText.Text = message ?? string.Empty;
            StatusText.Visibility = string.IsNullOrEmpty(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
            StatusText.Foreground = isError ? DangerBrush : SuccessBrush;
        }

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b) {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
