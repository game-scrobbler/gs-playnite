using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;

using Utf8Json;
using Utf8Json.Resolvers;



namespace GsPlugin
{
    public class GsPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        


        private GsPluginSettings settings { get; set; }
        private string sessionID { get; set; }
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        public GsPlugin(IPlayniteAPI api) : base(api)
        {
            settings = new GsPluginSettings(this);

            
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

        public override async void OnGameStarting(OnGameStartingEventArgs args)
        {
            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;
            var emptyObj = new { };
           
            TimeTracker startData = new TimeTracker
            {
                user_id = settings.InstallID,
                game_name = startedGame.Name,
                gameID = startedGame.Id.ToString(),
                metadata = emptyObj,
                started_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            string jsonData = JsonSerializer.ToJsonString(startData, StandardResolver.AllowPrivate);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using (HttpClient httpClient = new HttpClient())
            {

                try
                {


                    
                    var response = await httpClient.PostAsync(
                        "https://api.gamescrobbler.com/api/playnite/scrobble/start",
                        content
                    );

                    var responseBody = await response.Content.ReadAsStringAsync();
                    PlayniteApi.Dialogs.ShowMessage(responseBody);
                    SessionData obj = JsonSerializer.Deserialize<SessionData>(responseBody);
                  
                    PlayniteApi.Dialogs.ShowMessage(obj.session_id);
                 
                    string sessionId = obj.session_id;
                    sessionID = sessionId;
                  




                }
                catch (HttpRequestException ex)
                {

                    PlayniteApi.Dialogs.ShowMessage(ex.Message);
                }



            }
        }

        public override async void OnGameStopped(OnGameStoppedEventArgs args)
        {
            // Add code to be executed when game is preparing to be started.
            DateTime localDate = DateTime.Now;
            var startedGame = args.Game;
            var emptyObj = new { };
            
          
            TimeTrackerEnd startData = new TimeTrackerEnd
            {
                user_id = settings.InstallID,
               session_id = sessionID,
                metadata = emptyObj,
                finished_at = localDate.ToString("yyyy-MM-ddTHH:mm:ssK")
            };

            string jsonData = JsonSerializer.ToJsonString(startData, StandardResolver.AllowPrivate);
            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");

            using (HttpClient httpClient = new HttpClient())
            {

                try
                {


                    // Send the POST request to the endpoint
                    var response = await httpClient.PostAsync(
                        "https://api.gamescrobbler.com/api/playnite/scrobble/finish",
                        content
                    );

                    // Ensure the request was successful or throw an exception if not
                    response.EnsureSuccessStatusCode();


                    var responseBody = await response.Content.ReadAsStringAsync();
                    
                }
                catch (HttpRequestException ex)
                {

                    PlayniteApi.Dialogs.ShowMessage(ex.Message);
                }



            }

        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            // Add code to be executed when game is uninstalled.
        }

        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            
          
            // Add code to be executed when Playnite is initialized.
            var library = PlayniteApi.Database.Games.ToList();
            Sync sync = new Sync
            {
                user_id = settings.InstallID,

            };


            string jsonLib = JsonSerializer.ToJsonString(library, StandardResolver.AllowPrivate);
            string jsonSync = JsonSerializer.ToJsonString(sync, StandardResolver.AllowPrivate);
            string input = jsonSync + jsonLib;
            string modified = Regex.Replace(input, "(\"user_id\":\"[^\"]+\")}\\[", "$1, \"library\": [");
            var content = new StringContent(modified += "}", Encoding.UTF8, "application/json");

            using (HttpClient httpClient = new HttpClient())
            {

                try
                {


                    // Send the POST request to the endpoint
                    var response = await httpClient.PostAsync(
                        "https://api.gamescrobbler.com/api/playnite/sync",
                        content
                    );

                    // Ensure the request was successful or throw an exception if not
                    response.EnsureSuccessStatusCode();

                    // Optionally read and process the response content
                    var responseBody = await response.Content.ReadAsStringAsync();
                   
                }
                catch (HttpRequestException ex)
                {

                    PlayniteApi.Dialogs.ShowMessage(ex.Message);
                }



            }

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
                    return new MySidebarView(settings, PlayniteApi);
                },

            };


            // If you want a simple *action* instead of a custom panel, you can
            // return an item with Type = SidebarItemType.Action, plus an OpenCommand.
        }

    }
    class TimeTracker
    {
       public string user_id { get; set; }
        public string game_name { get; set; }
        public string gameID { get; set; }
        public object metadata { get; set; }
        public string started_at { get; set; }

    };
    class TimeTrackerEnd
    {
        public string user_id { get; set; }
        public object metadata { get; set; }
        public string finished_at { get; set; }
        public string session_id { get; set; }
    };
    class Sync
{
    

    public string user_id { get; set; }

};
    public class SessionData
    {
        public string session_id { get; set; }
    }

}