using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using Sentry;
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
    }

    /// <summary>
    /// View model for plugin settings that implements ISettings interface.
    /// Handles settings persistence, validation, and account linking operations.
    /// </summary>
    public class GsPluginSettingsViewModel : ObservableObject, ISettings {
        private readonly PluginClass _plugin;
        private readonly GsAccountLinkingService _linkingService;
        private readonly GsAchievementAggregator _achievementHelper;
        private GsPluginSettings _editingClone;
        private GsPluginSettings _settings;

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
            ? $"Connected (User ID: {GsDataManager.Data.LinkedUserId})"
            : "Disconnected";
        public static bool ShowLinkingControls => !IsLinked;

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
        public GsPluginSettingsViewModel(
            PluginClass plugin,
            GsAccountLinkingService linkingService,
            GsAchievementAggregator achievementHelper
        ) {
            // Store plugin reference for save/load operations
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
            _achievementHelper =
                achievementHelper ?? throw new ArgumentNullException(nameof(achievementHelper));
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
            GsDataManager.Data.NewDashboardExperience = savedSettings.NewDashboardExperience;
            GsDataManager.Data.SyncAchievements = savedSettings.SyncAchievements;

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
            GsDataManager.Data.Theme = Settings.Theme;
            GsDataManager.Data.UpdateFlags(Settings.DisableSentry, Settings.DisableScrobbling);
            GsDataManager.Data.NewDashboardExperience = Settings.NewDashboardExperience;
            GsDataManager.Data.SyncAchievements = Settings.SyncAchievements;
            GsDataManager.Save();

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
        /// Performs account linking with the provided token.
        /// </summary>
        public async void LinkAccount() {
            try {
                if (!ValidateLinkToken()) return;
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
                // Preserve the token on network errors so the user can retry without re-entering it
                if (Settings.LinkStatusMessage?.IndexOf("retry", System.StringComparison.OrdinalIgnoreCase) < 0) {
                    Settings.LinkToken = "";
                }
            }
        }

        #endregion

    }
}
