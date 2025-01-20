using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;



namespace GsPlugin
{
    public class GsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private GsPluginSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        public GsPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new GsPluginSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            // Add code to be executed when game is started running.
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Add code to be executed when Playnite is initialized.

            


        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            // Add code to be executed when Playnite is shutting down.
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
            base.OnLibraryUpdated(args);

            // Retrieve the game database:
            var allGames = PlayniteApi.Database.Games;
            var allData = PlayniteApi.Database.Genres.ToList();
            var json = 

            // For example, show a dialog with the count of games
            //PlayniteApi.Dialogs.ShowMessage($"You have {allGames.Count} games in your Playnite library.");
            PlayniteApi.Dialogs.ShowMessage($"You have {allData.Count()} ,{allData[0]} , {allData[1]} , {allData[2]} , {allData[3]}, {allData[4]}, {allData[5]}, {allData[6]} games in your Playnite library.");


        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GsPluginSettingsView();
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            // Return one or more SidebarItem objects
            yield return new SidebarItem
            {
                Type = (SiderbarItemType)1,
                Title = "Show My Data",
                Icon = new TextBlock { Text = "📋" }, // or a path to an image icon
                Opened = () =>
                {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView();
                },

            };


            // If you want a simple *action* instead of a custom panel, you can
            // return an item with Type = SidebarItemType.Action, plus an OpenCommand.
        }

    }
}