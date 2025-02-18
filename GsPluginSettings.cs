using System.Collections.Generic;
using Playnite.SDK;
using Playnite.SDK.Data;
using System.Windows;

namespace GsPlugin {
    public class GsPluginSettings : ObservableObject {
        private string theme = "Dark";
        public string Theme {
            get => theme;
            set => SetValue(ref theme, value);
        }

        // InstallID should be preserved across upgrades and stored in both settings and GSData
        public string InstallID { get; set; }
    }

    public class GsPluginSettingsViewModel : ObservableObject, ISettings {
        private readonly GsPlugin plugin;

        public string InstallID;
        private GsPluginSettings editingClone { get; set; }

        private GsPluginSettings settings;
        public GsPluginSettings Settings {
            get => settings;
            set {
                settings = value;
                OnPropertyChanged();
            }
        }

        public List<string> AvailableThemes { get; set; }

        public GsPluginSettingsViewModel(GsPlugin plugin) {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;
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
        }

        public void BeginEdit() {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit() {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to options.
            Settings = editingClone;
#if DEBUG
            MessageBox.Show($"Edit Cancelled - Reverted to:\nTheme: {Settings.Theme}\nInstallID: {Settings.InstallID}",
                "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
        }

        public void EndEdit() {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            plugin.SavePluginSettings(Settings);
            // Sync with GSDataManager
            GSDataManager.Data.Theme = Settings.Theme;
            GSDataManager.Save();
#if DEBUG
            MessageBox.Show($"Settings saved:\nTheme: {Settings.Theme}\nInstallID: {Settings.InstallID}\nGSData Theme: {GSDataManager.Data.Theme}",
                "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
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
