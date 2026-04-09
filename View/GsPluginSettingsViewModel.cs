using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Services;

namespace GsPlugin.Models {
    /// <summary>
    /// View model for plugin settings. Handles settings persistence, validation, and account linking operations.
    /// </summary>
    public class GsPluginSettingsViewModel : ObservableObject {
        private readonly string _settingsFilePath;
        private readonly GsAccountLinkingService _linkingService;
        private readonly IGsApiClient _apiClient;
        private GsPluginSettings _editingClone;
        private GsPluginSettings _settings;

        private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(5);
        private DispatcherTimer _countdownTimer;
        private DateTimeOffset _countdownDeadline;

        public GsPluginSettings Settings {
            get => _settings;
            set {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public List<string> AvailableThemes { get; set; }

        public static bool IsLinked => GsDataManager.IsAccountLinked;
        public static string ConnectionStatus => IsLinked
            ? "Connected to GameScrobbler"
            : "Disconnected";
        public static bool ShowLinkingControls => !IsLinked;

        /// <summary>True when the install has a server-issued auth token.</summary>
        public static bool IsInstallTokenActive =>
            !string.IsNullOrEmpty(GsDataManager.DataOrNull?.InstallToken);

        /// <summary>Number of scrobbles waiting to be sent to the server.</summary>
        public static int PendingScrobbleCount =>
            GsDataManager.DataOrNull?.PendingScrobbles?.Count ?? 0;

        /// <summary>True when there is at least one pending scrobble.</summary>
        public static bool HasPendingScrobbles => PendingScrobbleCount > 0;

        /// <summary>Number of scrobbles permanently lost due to repeated server failures.</summary>
        public static int DroppedScrobbleCount =>
            GsDataManager.DataOrNull?.DroppedScrobbleCount ?? 0;

        public static string LastSyncStatus {
            get {
                var syncAt = GsDataManager.Data.LastSyncAt;
                var count = GsDataManager.Data.LastSyncGameCount;
                if (syncAt == null || count == null)
                    return "Never synced";

                var ago = GsTime.FormatElapsed(DateTime.UtcNow - syncAt.Value);
                return $"Last synced: {count.Value:N0} games · {ago}";
            }
        }

        // Linking status change notifications are consolidated on GsAccountLinkingService.LinkingStatusChanged.
        // This forwarding method is kept for convenience from within the view model.
        public static void OnLinkingStatusChanged() {
            GsAccountLinkingService.OnLinkingStatusChanged();
        }

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the GsPluginSettingsViewModel.
        /// </summary>
        /// <param name="userDataDir">The plugin's user data directory (PlayniteApi.UserDataDir).</param>
        /// <param name="linkingService">The account linking service.</param>
        /// <param name="apiClient">The API client for server communication.</param>
        public GsPluginSettingsViewModel(
            string userDataDir,
            GsAccountLinkingService linkingService,
            IGsApiClient apiClient
        ) {
            _settingsFilePath = Path.Combine(
                userDataDir ?? throw new ArgumentNullException(nameof(userDataDir)),
                "settings.json");
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            AvailableThemes = new List<string> { "Dark", "Light", "System" };

            InitializeSettings();
        }

        /// <summary>
        /// Initializes settings by loading saved data or creating defaults.
        /// </summary>
        private void InitializeSettings() {
            GsPluginSettings savedSettings = null;
            if (File.Exists(_settingsFilePath)) {
                try {
                    var json = File.ReadAllText(_settingsFilePath);
                    savedSettings = JsonSerializer.Deserialize<GsPluginSettings>(json);
                }
                catch (Exception ex) {
                    GsLogger.Warn($"[GsPluginSettingsViewModel] Failed to load settings: {ex.Message}");
                }
            }
            if (savedSettings != null) {
                LoadExistingSettings(savedSettings);
            }
            else {
                CreateDefaultSettings();
            }
            // Subscribe to property changes for UI updates
            if (Settings != null) {
                Settings.PropertyChanged += (s, e) => OnPropertyChanged("Settings");
            }
        }

        /// <summary>
        /// Loads and validates existing settings from storage.
        /// </summary>
        private void LoadExistingSettings(GsPluginSettings savedSettings) {
            Settings = savedSettings;

            // Sync settings to GsDataManager
            GsDataManager.MutateAndSave(d => {
                d.NewDashboardExperience = savedSettings.NewDashboardExperience;
                d.ShowUpdateNotifications = savedSettings.ShowUpdateNotifications;
                d.ShowImportantNotifications = savedSettings.ShowImportantNotifications;
            });

            // Log successful load for debugging
            GsSentry.AddBreadcrumb(
                message: "Successfully loaded plugin settings",
                category: "settings",
                data: new Dictionary<string, string> {
                    { "Theme", savedSettings.Theme },
                    { "NewDashboard", savedSettings.NewDashboardExperience.ToString() }
                }
            );

            GsLogger.ShowDebugInfoBox($"Loaded saved settings:\nTheme: {savedSettings.Theme}\nNew Dashboard: {savedSettings.NewDashboardExperience}", "Debug - Settings Loaded");
        }

        /// <summary>
        /// Creates default settings for first-time use.
        /// </summary>
        private void CreateDefaultSettings() {
            Settings = new GsPluginSettings {
                Theme = AvailableThemes[0]
            };

            // Log creation for debugging
            GsSentry.AddBreadcrumb(
                message: "Created new plugin settings",
                category: "settings"
            );

            GsLogger.ShowDebugInfoBox("No saved settings found. Created new settings instance", "Debug - New Settings");
        }

        #endregion

        #region ISettings Implementation

        /// <summary>
        /// Begins the editing process by creating a backup of current settings.
        /// </summary>
        public void BeginEdit() {
            var json = JsonSerializer.Serialize(Settings);
            _editingClone = JsonSerializer.Deserialize<GsPluginSettings>(json);
        }

        /// <summary>
        /// Cancels the editing process and reverts to the original settings.
        /// </summary>
        public void CancelEdit() {
            Settings = _editingClone;
            GsLogger.ShowDebugInfoBox($"Edit Cancelled - Reverted to:\nTheme: {Settings.Theme}", "Debug - Edit Cancelled");
        }

        /// <summary>
        /// Commits the changes and saves settings to storage.
        /// </summary>
        public void EndEdit() {
            // Save settings to disk
            try {
                File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(Settings));
            }
            catch (Exception ex) {
                GsLogger.Warn($"[GsPluginSettingsViewModel] Failed to save settings: {ex.Message}");
            }

            // Update global data manager
            var s = Settings;
            GsDataManager.MutateAndSave(d => {
                d.Theme = s.Theme;
                d.UpdateFlags(s.DisableSentry, s.DisableScrobbling, s.DisablePostHog);
                d.NewDashboardExperience = s.NewDashboardExperience;
                d.ShowUpdateNotifications = s.ShowUpdateNotifications;
                d.ShowImportantNotifications = s.ShowImportantNotifications;
            });

            GsLogger.ShowDebugInfoBox($"Settings saved:\nTheme: {Settings.Theme}\nNew Dashboard: {Settings.NewDashboardExperience}\nFlags: {string.Join(", ", GsDataManager.Data.Flags)}", "Debug - Settings Saved");
        }

        public bool VerifySettings(out List<string> errors) {
            errors = new List<string>();

            if (string.IsNullOrEmpty(Settings.Theme) || !AvailableThemes.Contains(Settings.Theme)) {
                errors.Add($"Invalid theme. Valid options: {string.Join(", ", AvailableThemes)}");
            }

            return errors.Count == 0;
        }

        #endregion

        #region Account Linking Operations

        /// <summary>
        /// Disconnects the linked account via the centralized service.
        /// </summary>
        public async Task UnlinkAccount() {
            try {
                var result = await _linkingService.UnlinkAccountAsync();
                if (result.Success) {
                    Settings.LinkStatusMessage = "Account disconnected.";
                }
                else if (result.ErrorMessage != "Cancelled") {
                    Settings.LinkStatusMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex) {
                GsLogger.Error("Unhandled exception in UnlinkAccount", ex);
                GsSentry.CaptureException(ex, "Unhandled exception in UnlinkAccount");
                Settings.LinkStatusMessage = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Performs account linking with the provided token.
        /// </summary>
        public async void LinkAccount() {
            try {
                if (!ValidateLinkToken()) return;
                StartCountdown();
                await PerformLinking();
            }
            catch (Exception ex) {
                GsLogger.Error("Unhandled exception in LinkAccount", ex);
                GsSentry.CaptureException(ex, "Unhandled exception in LinkAccount");
            }
        }

        /// <summary>
        /// Validates the link token before attempting to link.
        /// </summary>
        /// <returns>True if token is valid, false otherwise.</returns>
        private bool ValidateLinkToken() {
            if (string.IsNullOrWhiteSpace(Settings.LinkToken)) {
                Settings.LinkStatusMessage = "Please enter a token";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Performs the actual account linking operation using the centralized service.
        /// </summary>
        private async Task PerformLinking() {
            Settings.IsLinking = true;
            Settings.LinkStatusMessage = "Verifying token...";
            bool preserveToken = false;

            try {
                var result = await _linkingService.LinkAccountAsync(Settings.LinkToken, LinkingContext.ManualSettings);

                if (result.Success) {
                    Settings.LinkStatusMessage = "Successfully linked account!";
                    // Note: OnLinkingStatusChanged() is already called inside LinkAccountAsync
                }
                else if (result.IsTokenExpiry) {
                    Settings.LinkStatusMessage = "Token expired — click \"Open website to link\" to get a new one.";
                }
                else if (result.IsNetworkError) {
                    preserveToken = true;
                    Settings.LinkStatusMessage = $"{result.ErrorMessage} Click \"Link Account\" to retry.";
                }
                else {
                    Settings.LinkStatusMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex) {
                preserveToken = true;
                Settings.LinkStatusMessage = $"Error: {ex.Message} Click \"Link Account\" to retry.";
            }
            finally {
                Settings.IsLinking = false;
                StopCountdown();
                // Preserve the token on network errors so the user can retry without re-entering it
                if (!preserveToken) {
                    Settings.LinkToken = "";
                }
            }
        }

        /// <summary>
        /// Starts a local countdown timer from now, assuming the token was just generated
        /// and has the standard 5-minute TTL. The countdown is a UX hint — the server is
        /// still the authority on actual expiry.
        /// </summary>
        public void StartCountdown() {
            _countdownDeadline = DateTimeOffset.UtcNow.Add(TokenTtl);
            UpdateCountdownText();

            if (_countdownTimer == null) {
                _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _countdownTimer.Tick += (s, e) => UpdateCountdownText();
            }
            _countdownTimer.Start();
        }

        /// <summary>
        /// Stops the countdown timer and clears the display.
        /// </summary>
        public void StopCountdown() {
            _countdownTimer?.Stop();
            if (Settings != null) {
                Settings.TokenCountdown = "";
            }
        }

        private void UpdateCountdownText() {
            var remaining = _countdownDeadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) {
                // Timer expired — clear the countdown display but do NOT
                // set an expiry status message. The server is authoritative;
                // the in-flight VerifyToken call may still succeed.
                _countdownTimer?.Stop();
                Settings.TokenCountdown = "";
                return;
            }
            Settings.TokenCountdown = $"Token expires in ~{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
        }

        #endregion

        #region Data Deletion

        /// <summary>
        /// Requests data deletion from the server and transitions the plugin to opted-out state.
        /// </summary>
        public async void DeleteMyData() {
            try {
                Settings.IsDeleting = true;
                Settings.DeleteStatusMessage = "Requesting data deletion...";

                var result = await _apiClient.RequestDeleteMyData(new DeleteDataReq());

                if (result != null && result.success) {
                    // Capture analytics before opt-out disables telemetry
                    GsPostHog.Capture("data_deletion_requested");
                    GsDataManager.PerformOptOut();
                    GsSnapshotManager.ClearAll();
                    Settings.DeleteStatusMessage = "Your data has been deleted. The plugin is now disabled.";
                    // Notify UI to refresh connection status and button visibility
                    OnLinkingStatusChanged();
                }
                else if (result != null && result.rateLimited) {
                    Settings.DeleteStatusMessage = "Too many deletion requests. Please wait 15 minutes and try again.";
                }
                else {
                    Settings.DeleteStatusMessage = "Failed to request data deletion. Please try again later.";
                }
            }
            catch (Exception ex) {
                Settings.DeleteStatusMessage = "An error occurred. Please try again later.";
                GsLogger.Error("Error requesting data deletion", ex);
                GsSentry.CaptureException(ex, "Error requesting data deletion");
            }
            finally {
                Settings.IsDeleting = false;
            }
        }

        /// <summary>
        /// Re-enables the plugin after a previous opt-out / data deletion.
        /// Calls the server to clear the opted-out flag and reset the token,
        /// then clears local opt-out state so /v2/register runs on next startup.
        /// </summary>
        public async void OptBackIn() {
            try {
                Settings.DeleteStatusMessage = "Re-enabling...";

                var result = await _apiClient.RequestOptIn(new OptInReq());

                if (result == null || !result.success) {
                    if (result?.rateLimited == true) {
                        Settings.DeleteStatusMessage = "Too many attempts. Please wait and try again.";
                    }
                    else {
                        Settings.DeleteStatusMessage = "Failed to re-enable. Please restart Playnite to try again.";
                    }
                    return;
                }

                GsDataManager.PerformOptIn();
                Settings.DeleteStatusMessage = "Plugin re-enabled. Please restart Playnite to resume syncing.";
                OnLinkingStatusChanged();
            }
            catch (Exception ex) {
                Settings.DeleteStatusMessage = "An error occurred. Please restart Playnite to try again.";
                GsLogger.Error("Error during opt-back-in", ex);
                GsSentry.CaptureException(ex, "Error during opt-back-in");
            }
        }

        #endregion

    }
}
