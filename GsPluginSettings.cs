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
#if DEBUG
                MessageBox.Show($"Loaded saved settings:\nInstallID: {InstallID}\nTheme: {savedSettings.Theme}",
                    "Debug - Settings Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
            }
            else {
                Settings = new GsPluginSettings();
                Settings.Theme = AvailableThemes[0]; // Set default theme

                // Log creation of new settings to Sentry
                SentrySdk.AddBreadcrumb(
                    message: "Created new plugin settings",
                    category: "settings"
                );
#if DEBUG
                MessageBox.Show("No setting found. Created new settings instance - No saved settings found",
                    "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
#endif
            }

            // Subscribe to settings property changes
            if (Settings != null) {
                Settings.PropertyChanged += (s, e) => OnPropertyChanged("Settings");
            }
        }

        public void BeginEdit() {
            // Code executed when settings view is opened and user starts editing values.
            _editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit() {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to options.
            Settings = _editingClone;
#if DEBUG
            MessageBox.Show($"Edit Cancelled - Reverted to:\nTheme: {Settings.Theme}\nInstallID: {Settings.InstallID}",
                "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
        }

        public void EndEdit() {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            _plugin.SavePluginSettings(Settings);
            // Sync with GsDataManager
            GsDataManager.Data.Theme = Settings.Theme;
            GsDataManager.Data.UpdateFlags(Settings.DisableSentry, Settings.DisableScrobbling);
            GsDataManager.Save();
#if DEBUG
            MessageBox.Show($"Settings saved:\nTheme: {Settings.Theme}\nFlags: {string.Join(", ", GsDataManager.Data.Flags)}",
                "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
        }

        public async void LinkAccount() {
            if (string.IsNullOrWhiteSpace(Settings.LinkToken)) {
                Settings.LinkStatusMessage = "Please enter a token";
#if DEBUG
                MessageBox.Show("Link Account failed: Token is empty or whitespace",
                    "Debug - Link Account", MessageBoxButton.OK, MessageBoxImage.Warning);
#endif
                return;
            }

            Settings.IsLinking = true;
            Settings.LinkStatusMessage = "Verifying token...";

            try {
                var response = await _apiClient.VerifyToken(Settings.LinkToken, GsDataManager.Data.InstallID);

#if DEBUG
                MessageBox.Show($"API Response received:\nStatus: {response?.status ?? "null"}\nMessage: {response?.message ?? "null"}\nUserId: {response?.userId ?? "null"}",
                    "Debug - API Response", MessageBoxButton.OK, MessageBoxImage.Information);
#endif

                if (response != null && response.status == "success") {
                    GsDataManager.Data.IsLinked = true;
                    GsDataManager.Data.LinkedUserId = response.userId;
                    GsDataManager.Save();

                    Settings.LinkStatusMessage = "Successfully linked account!";
                    Settings.LinkToken = ""; // Clear the token

                    OnLinkingStatusChanged();

#if DEBUG
                    MessageBox.Show($"Account successfully linked!\nLinked User ID: {response.userId}\nIsLinked: {GsDataManager.Data.IsLinked}",
                        "Debug - Link Success", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
                }
                else {
                    Settings.LinkStatusMessage = response?.message ?? "Unknown error occurred";
#if DEBUG
                    MessageBox.Show($"Link failed:\nStatus: {response?.status ?? "null"}\nMessage: {response?.message ?? "Unknown error occurred"}",
                        "Debug - Link Failed", MessageBoxButton.OK, MessageBoxImage.Error);
#endif
                }
            }
            catch (Exception ex) {
                Settings.LinkStatusMessage = $"Error: {ex.Message}";
                SentrySdk.CaptureException(ex);
            }
            finally {
                Settings.IsLinking = false;
#if DEBUG
                MessageBox.Show($"Link account process completed.\nFinal Status: IsLinked = {GsDataManager.Data.IsLinked}\nLinked User ID: {GsDataManager.Data.LinkedUserId ?? "null"}",
                    "Debug - Link Complete", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
            }
        }

        public async Task<bool> LinkAccountWithToken(string token) {
            if (string.IsNullOrWhiteSpace(token)) {
                return false;
            }

            Settings.IsLinking = true;
            Settings.LinkStatusMessage = "Verifying token...";

            try {
                var response = await _apiClient.VerifyToken(token, GsDataManager.Data.InstallID);

                if (response != null && response.status == "success") {
                    GsDataManager.Data.IsLinked = true;
                    GsDataManager.Data.LinkedUserId = response.userId;
                    GsDataManager.Save();

                    Settings.LinkStatusMessage = "Successfully linked account!";
                    Settings.LinkToken = ""; // Clear any existing token

                    OnLinkingStatusChanged();
                    return true;
                }
                else {
                    Settings.LinkStatusMessage = response?.message ?? "Unknown error occurred";
                    return false;
                }
            }
            catch (Exception ex) {
                Settings.LinkStatusMessage = $"Error: {ex.Message}";
                SentrySdk.CaptureException(ex);
                return false;
            }
            finally {
                Settings.IsLinking = false;
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
