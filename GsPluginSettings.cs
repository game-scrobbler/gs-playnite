using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;
using Playnite.SDK.Data;
using System.Windows;
using Sentry;

namespace GsPlugin {
    public class GsPluginSettings : ObservableObject {
        private string _theme = "Dark";
        public string Theme {
            get => _theme;
            set => SetValue(ref _theme, value);
        }

        // InstallID should be preserved across upgrades and stored in both settings and GSData
        public string InstallID { get; set; }

        private bool disableSentry = false;
        private bool disableScrobbling = false;
        private string _linkToken = "";
        private bool _isLinking = false;
        private string _linkStatusMessage = "";

        public bool DisableSentry {
            get => disableSentry;
            set {
                disableSentry = value;
                OnPropertyChanged();
            }
        }

        public bool DisableScrobbling {
            get => disableScrobbling;
            set {
                disableScrobbling = value;
                OnPropertyChanged();
            }
        }

        public string LinkToken {
            get => _linkToken;
            set {
                _linkToken = value;
                OnPropertyChanged();
            }
        }

        public bool IsLinking {
            get => _isLinking;
            set {
                _isLinking = value;
                OnPropertyChanged();
            }
        }

        public string LinkStatusMessage {
            get => _linkStatusMessage;
            set {
                _linkStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public class GsPluginSettingsViewModel : ObservableObject, ISettings {
        private readonly GsPlugin _plugin;

        public string InstallID;
        private GsPluginSettings _editingClone { get; set; }

        private GsPluginSettings _settings;
        public GsPluginSettings Settings {
            get => _settings;
            set {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public List<string> AvailableThemes { get; set; }

        private GsApiClient _apiClient;

        public static bool IsLinked => GsDataManager.Data.IsLinked;

        public static string ConnectionStatus => IsLinked
            ? $"Connected (User ID: {GsDataManager.Data.LinkedUserId})"
            : "Disconnected";

        public static bool ShowLinkingControls => !IsLinked;

        // Static event to notify when linking status changes
        public static event EventHandler LinkingStatusChanged;

        // Public static method to trigger the linking status changed event
        public static void OnLinkingStatusChanged() {
            LinkingStatusChanged?.Invoke(null, EventArgs.Empty);
        }

        public GsPluginSettingsViewModel(GsPlugin plugin) {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            _plugin = plugin;
            _apiClient = new GsApiClient();
            AvailableThemes = new List<string> { "Dark", "Light" };

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<GsPluginSettings>();

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null) {
                Settings = savedSettings;
                InstallID = savedSettings.InstallID;

                // Log successful settings load to Sentry
                SentrySdk.AddBreadcrumb(
                    message: "Successfully loaded plugin settings",
                    category: "settings",
                    data: new Dictionary<string, string> {
                        { "InstallID", InstallID },
                        { "Theme", savedSettings.Theme }
                    }
                );
                GsLogger.ShowDebugInfoBox($"Loaded saved settings:\nInstallID: {InstallID}\nTheme: {savedSettings.Theme}",
                    "Debug - Settings Loaded");
            }
            else {
                Settings = new GsPluginSettings();
                Settings.Theme = AvailableThemes[0];

                // Log creation of new settings to Sentry
                SentrySdk.AddBreadcrumb(
                    message: "Created new plugin settings",
                    category: "settings"
                );
                GsLogger.ShowDebugInfoBox("No setting found. Created new settings instance - No saved settings found",
                    "Debug");
            }

            // Subscribe to settings property changes
            if (Settings != null) {
                Settings.PropertyChanged += (s, e) => OnPropertyChanged("Settings");
            }
        }

        public void BeginEdit() {
            _editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit() {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to options.
            Settings = _editingClone;
            GsLogger.ShowDebugInfoBox($"Edit Cancelled - Reverted to:\nTheme: {Settings.Theme}\nInstallID: {Settings.InstallID}",
                "Debug");
        }

        public void EndEdit() {
            _plugin.SavePluginSettings(Settings);
            GsDataManager.Data.Theme = Settings.Theme;
            GsDataManager.Data.UpdateFlags(Settings.DisableSentry, Settings.DisableScrobbling);
            GsDataManager.Save();

            GsLogger.ShowDebugInfoBox($"Settings saved:\nTheme: {Settings.Theme}\nFlags: {string.Join(", ", GsDataManager.Data.Flags)}",
                "Debug");
        }

        public async void LinkAccount() {
            if (string.IsNullOrWhiteSpace(Settings.LinkToken)) {
                Settings.LinkStatusMessage = "Please enter a token";
                return;
            }

            Settings.IsLinking = true;
            Settings.LinkStatusMessage = "Verifying token...";

            try {
                var response = await _apiClient.VerifyToken(Settings.LinkToken, GsDataManager.Data.InstallID);

                if (response != null && response.status == "success") {
                    GsDataManager.Data.IsLinked = true;
                    GsDataManager.Data.LinkedUserId = response.userId;
                    GsDataManager.Save();

                    Settings.LinkStatusMessage = "Successfully linked account!";
                    Settings.LinkToken = "";

                    OnLinkingStatusChanged();

                    GsLogger.ShowDebugInfoBox($"Account successfully linked!\nLinked User ID: {response.userId}\nIsLinked: {GsDataManager.Data.IsLinked}",
                        "Debug - Link Success");
                }
                else {
                    Settings.LinkStatusMessage = response?.message ?? "Unknown error occurred";
                }
            }
            catch (Exception ex) {
                Settings.LinkStatusMessage = $"Error: {ex.Message}";
                SentrySdk.CaptureException(ex);
            }
            finally {
                Settings.IsLinking = false;
                GsLogger.ShowDebugInfoBox($"Link account process completed.\nFinal Status: IsLinked = {GsDataManager.Data.IsLinked}\nLinked User ID: {GsDataManager.Data.LinkedUserId ?? "null"}",
                    "Debug - Link Complete");
            }
        }


        public bool VerifySettings(out List<string> errors) {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }
    }
}
