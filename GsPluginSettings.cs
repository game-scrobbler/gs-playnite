using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using System.Windows;
using Sentry;

namespace GsPlugin {
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

        /// <summary>
        /// Unique installation identifier preserved across upgrades.
        /// Stored in both settings and GSData for redundancy.
        /// </summary>
        public string InstallID { get; set; }

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
        private readonly GsPlugin _plugin;
        private readonly GsAccountLinkingService _linkingService;
        private GsPluginSettings _editingClone;
        private GsPluginSettings _settings;

        public string InstallID { get; set; }
        public GsPluginSettings Settings {
            get => _settings;
            set {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public List<string> AvailableThemes { get; set; }
        public static bool IsLinked => GsDataManager.Data.IsLinked;
        public static string ConnectionStatus => IsLinked
            ? $"Connected (User ID: {GsDataManager.Data.LinkedUserId})"
            : "Disconnected";
        public static bool ShowLinkingControls => !IsLinked;

        public static event EventHandler LinkingStatusChanged;
        public static void OnLinkingStatusChanged() {
            LinkingStatusChanged?.Invoke(null, EventArgs.Empty);
        }

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the GsPluginSettingsViewModel.
        /// </summary>
        /// <param name="plugin">The plugin instance for settings persistence.</param>
        /// <param name="linkingService">The account linking service.</param>
        public GsPluginSettingsViewModel(GsPlugin plugin, GsAccountLinkingService linkingService) {
            // Store plugin reference for save/load operations
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            _linkingService = linkingService ?? throw new ArgumentNullException(nameof(linkingService));
            AvailableThemes = new List<string> { "Dark", "Light" };

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
            InstallID = savedSettings.InstallID;

            // Log successful load for debugging
            SentrySdk.AddBreadcrumb(
                message: "Successfully loaded plugin settings",
                category: "settings",
                data: new Dictionary<string, string> {
                    { "InstallID", InstallID },
                    { "Theme", savedSettings.Theme }
                }
            );

            GsLogger.ShowDebugInfoBox($"Loaded saved settings:\nInstallID: {InstallID}\nTheme: {savedSettings.Theme}", "Debug - Settings Loaded");
        }

        /// <summary>
        /// Creates default settings for first-time use.
        /// </summary>
        private void CreateDefaultSettings() {
            Settings = new GsPluginSettings {
                Theme = AvailableThemes[0]
            };

            // Log creation for debugging
            SentrySdk.AddBreadcrumb(
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
            GsLogger.ShowDebugInfoBox($"Edit Cancelled - Reverted to:\nTheme: {Settings.Theme}\nInstallID: {Settings.InstallID}", "Debug - Edit Cancelled");
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
            GsDataManager.Save();

            GsLogger.ShowDebugInfoBox($"Settings saved:\nTheme: {Settings.Theme}\nFlags: {string.Join(", ", GsDataManager.Data.Flags)}", "Debug - Settings Saved");
        }

        public bool VerifySettings(out List<string> errors) {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }

        #endregion

        #region Account Linking Operations

        /// <summary>
        /// Performs account linking with the provided token.
        /// </summary>
        public async void LinkAccount() {
            if (!ValidateLinkToken()) return;
            await PerformLinking();
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
                    Settings.LinkToken = "";
                    OnLinkingStatusChanged();
                }
                else {
                    Settings.LinkStatusMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex) {
                Settings.LinkStatusMessage = $"Error: {ex.Message}";
            }
            finally {
                Settings.IsLinking = false;
            }
        }

        #endregion

    }
}
