using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Data;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Services;
using PluginClass = GsPlugin.GsPlugin;

namespace GsPlugin.Models {
    /// <summary>
    /// Represents the settings data model for the GS Plugin.
    /// Contains all user-configurable options and runtime state.
    /// </summary>
    public class GsPluginSettings : ObservableObject {
        private string _theme = "Dark";
        public string Theme {
            get => _theme;
            set => SetValue(ref _theme, value);
        }

        private bool _disableSentry = false;
        public bool DisableSentry {
            get => _disableSentry;
            set {
                _disableSentry = value;
                OnPropertyChanged();
            }
        }
        private bool _disableScrobbling = false;
        public bool DisableScrobbling {
            get => _disableScrobbling;
            set {
                _disableScrobbling = value;
                OnPropertyChanged();
            }
        }

        private bool _disablePostHog = false;
        public bool DisablePostHog {
            get => _disablePostHog;
            set {
                _disablePostHog = value;
                OnPropertyChanged();
            }
        }

        private bool _newDashboardExperience = false;
        public bool NewDashboardExperience {
            get => _newDashboardExperience;
            set {
                _newDashboardExperience = value;
                OnPropertyChanged();
            }
        }

        private bool _syncAchievements = true;
        public bool SyncAchievements {
            get => _syncAchievements;
            set {
                _syncAchievements = value;
                OnPropertyChanged();
            }
        }

        private bool _showUpdateNotifications = true;
        public bool ShowUpdateNotifications {
            get => _showUpdateNotifications;
            set {
                _showUpdateNotifications = value;
                OnPropertyChanged();
            }
        }

        private bool _showImportantNotifications = true;
        public bool ShowImportantNotifications {
            get => _showImportantNotifications;
            set {
                _showImportantNotifications = value;
                OnPropertyChanged();
            }
        }

        private string _linkToken = "";
        public string LinkToken {
            get => _linkToken;
            set {
                _linkToken = value;
                OnPropertyChanged();
            }
        }
        private bool _isLinking = false;
        public bool IsLinking {
            get => _isLinking;
            set {
                _isLinking = value;
                OnPropertyChanged();
            }
        }
        private string _linkStatusMessage = "";
        public string LinkStatusMessage {
            get => _linkStatusMessage;
            set {
                _linkStatusMessage = value;
                OnPropertyChanged();
            }
        }

        private string _tokenCountdown = "";
        public string TokenCountdown {
            get => _tokenCountdown;
            set {
                _tokenCountdown = value;
                OnPropertyChanged();
            }
        }

        private bool _isDeleting = false;
        public bool IsDeleting {
            get => _isDeleting;
            set {
                _isDeleting = value;
                OnPropertyChanged();
            }
        }
        private string _deleteStatusMessage = "";
        public string DeleteStatusMessage {
            get => _deleteStatusMessage;
            set {
                _deleteStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// View model for plugin settings that implements ISettings interface.
    /// Handles settings persistence, validation, and account linking operations.
    /// </summary>
    public class GsPluginSettingsViewModel : ObservableObject, ISettings {
        private readonly PluginClass _plugin;
        private readonly GsAccountLinkingService _linkingService;
        private readonly GsAchievementAggregator _achievementHelper;
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

        private bool? _isAnyAchievementProviderInstalled;
        public bool IsAnyAchievementProviderInstalled {
            get {
                if (!_isAnyAchievementProviderInstalled.HasValue)
                    _isAnyAchievementProviderInstalled = _achievementHelper.IsInstalled;
                return _isAnyAchievementProviderInstalled.Value;
            }
        }

        public bool IsAllAchievementProvidersMissing => !IsAnyAchievementProviderInstalled;

        private string _achievementProviderStatusText;
        public string AchievementProviderStatusText {
            get {
                if (_achievementProviderStatusText == null) {
                    var installed = _achievementHelper.GetInstalledProviders();
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var p in installed) {
                        var version = p.GetVersion();
                        parts.Add(version != null
                            ? $"{p.ProviderName} (v{version})"
                            : p.ProviderName);
                    }
                    _achievementProviderStatusText = parts.Count > 0
                        ? string.Join(", ", parts) + " detected"
                        : "";
                }
                return _achievementProviderStatusText;
            }
        }

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
                return $"Last synced: {count:N0} games · {ago}";
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
        /// <param name="plugin">The plugin instance for settings persistence.</param>
        /// <param name="linkingService">The account linking service.</param>
        /// <param name="achievementHelper">The aggregated achievement provider for detection status.</param>
        /// <param name="apiClient">The API client for server communication.</param>
        public GsPluginSettingsViewModel(
            PluginClass plugin,
            GsAccountLinkingService linkingService,
            GsAchievementAggregator achievementHelper,
            IGsApiClient apiClient
        ) {
            // Store plugin reference for save/load operations
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
            _achievementHelper =
                achievementHelper ?? throw new ArgumentNullException(nameof(achievementHelper));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            AvailableThemes = new List<string> { "Dark", "Light", "System" };

            InitializeSettings();
        }

        /// <summary>
        /// Initializes settings by loading saved data or creating defaults.
        /// </summary>
        private void InitializeSettings() {
            var savedSettings = _plugin.LoadPluginSettings<GsPluginSettings>();
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
                d.SyncAchievements = savedSettings.SyncAchievements;
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
            _editingClone = Serialization.GetClone(Settings);
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
            // Save settings to Playnite storage
            _plugin.SavePluginSettings(Settings);

            // Update global data manager
            var s = Settings;
            GsDataManager.MutateAndSave(d => {
                d.Theme = s.Theme;
                d.UpdateFlags(s.DisableSentry, s.DisableScrobbling, s.DisablePostHog);
                d.NewDashboardExperience = s.NewDashboardExperience;
                d.SyncAchievements = s.SyncAchievements;
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
                    Settings.LinkStatusMessage = GsLocalization.Get("LOCGsPluginStatusDisconnected", "Account disconnected.");
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

            try {
                var result = await _linkingService.LinkAccountAsync(Settings.LinkToken, LinkingContext.ManualSettings);

                if (result.Success) {
                    Settings.LinkStatusMessage = "Successfully linked account!";
                    // Note: OnLinkingStatusChanged() is already called inside LinkAccountAsync
                }
                else if (result.IsTokenExpiry) {
                    Settings.LinkStatusMessage = GsLocalization.Get("LOCGsPluginStatusTokenExpired", "Token expired — click \"Open website to link\" to get a new one.");
                }
                else if (result.IsNetworkError) {
                    Settings.LinkStatusMessage = $"{result.ErrorMessage} Click \"Link Account\" to retry.";
                }
                else {
                    Settings.LinkStatusMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex) {
                Settings.LinkStatusMessage = $"Error: {ex.Message} Click \"Link Account\" to retry.";
            }
            finally {
                Settings.IsLinking = false;
                StopCountdown();
                // Preserve the token on network errors so the user can retry without re-entering it
                if (Settings.LinkStatusMessage?.IndexOf("retry", System.StringComparison.OrdinalIgnoreCase) < 0) {
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
            Settings.TokenCountdown = GsLocalization.Format("LOCGsPluginTokenCountdownFormat", "Token expires in ~{0}:{1}", (int)remaining.TotalMinutes, remaining.Seconds.ToString("D2"));
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
        /// </summary>
        public void OptBackIn() {
            GsDataManager.PerformOptIn();
            Settings.DeleteStatusMessage = "Plugin re-enabled. Please restart Playnite to resume syncing.";
            OnLinkingStatusChanged();
        }

        #endregion

    }
}
