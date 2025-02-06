using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace GsPlugin
{
    public class GsPluginSettings : ObservableObject , ISettings
    {
        private string option1 = string.Empty;
        private bool option2 = false;
        private bool optionThatWontBeSaved = false;
        public string InstallID { get; set; } = string.Empty;

        private readonly GsPlugin plugin;


        public string Option1 { get => option1; set => SetValue(ref option1, value); }
        public bool Option2 { get => option2; set => SetValue(ref option2, value); }
        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        [DontSerialize]
        public bool OptionThatWontBeSaved { get => optionThatWontBeSaved; set => SetValue(ref optionThatWontBeSaved, value); }

        // Parameterless constructor required for serialization
        public GsPluginSettings() { }

        // Constructor linking settings with the plugin
        public GsPluginSettings(GsPlugin plugin)
        {
            this.plugin = plugin;

            // Attempt to load existing settings
            var savedSettings = plugin.LoadPluginSettings<GsPluginSettings>();
            if (savedSettings != null && !string.IsNullOrEmpty(savedSettings.InstallID))
            {
                InstallID = savedSettings.InstallID;
            }
            else
            {
                // Generate a new GUID if not already set
                InstallID = System.Guid.NewGuid().ToString();
                
                // Save the new settings immediately to persist the InstallID
                plugin.SavePluginSettings(this);
            }
        }

        // ISettings interface methods
        public void BeginEdit() { }

        public void EndEdit()
        {
            // Save settings when editing ends
            plugin.SavePluginSettings(this);
        }

        public void CancelEdit() { }
        public bool VerifySettings(out List<string> errors)
        {

            errors = new List<string>();
            return true;
        }

    }

    public class GsPluginSettingsViewModel : ObservableObject, ISettings
    {
        private readonly GsPlugin plugin;
        private GsPluginSettings editingClone { get; set; }
        


        private GsPluginSettings settings;
        public GsPluginSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public GsPluginSettingsViewModel(GsPlugin plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;
            
            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<GsPluginSettings>();

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null)
            {
                
                Settings = savedSettings;

            }
            else
            {
               
                
                Settings = new GsPluginSettings();
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }
    }
    class InitSync
    {


        public string user_id { get; set; }

    };
}