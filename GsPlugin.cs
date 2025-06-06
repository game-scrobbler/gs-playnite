using System;
using System.Collections.Generic;
using System.Windows.Controls;
using MySidebarPlugin;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace GsPlugin {

    public class GsPlugin : GenericPlugin {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private GsPluginSettingsViewModel _settings { get; set; }
        private GsApiClient _apiClient;
        private GsAccountLinkingService _linkingService;
        private GsUriHandler _uriHandler;
        private GsScrobblingService _scrobblingService;
        /// Unique identifier for the plugin itself.
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        /// <summary>
        /// Constructor for the plugin. Initializes all required services and components.
        /// </summary>
        /// <param name="api">Instance of Playnite API to be injected.</param>
        public GsPlugin(IPlayniteAPI api) : base(api) {
            // Initialize Sentry for error tracking
            GsSentry.Initialize();

            // Initialize API client
            _apiClient = new GsApiClient();

            // Initialize centralized account linking service
            _linkingService = new GsAccountLinkingService(_apiClient, api);

            // Create settings with linking service dependency
            _settings = new GsPluginSettingsViewModel(this, _linkingService);
            Properties = new GenericPluginProperties {
                HasSettings = true
            };

            // Initialize GsDataManager first
            GsDataManager.Initialize(GetPluginUserDataPath(), _settings.InstallID);

            // Initialize scrobbling services
            _scrobblingService = new GsScrobblingService(_apiClient);

            // Initialize and register URI handler for automatic account linking
            _uriHandler = new GsUriHandler(api, _linkingService);
            _uriHandler.RegisterUriHandler();
        }

        /// <summary>
        /// Called when a game has been installed.
        /// </summary>
        public override void OnGameInstalled(OnGameInstalledEventArgs args) {
            base.OnGameInstalled(args);
        }

        /// <summary>
        /// Called when a game has started running.
        /// </summary>
        public override void OnGameStarted(OnGameStartedEventArgs args) {
            base.OnGameStarted(args);
        }

        /// <summary>
        /// Called before a game is started. This happens when the user clicks Play but before the game actually launches.
        /// </summary>
        public override async void OnGameStarting(OnGameStartingEventArgs args) {
            await _scrobblingService.OnGameStartAsync(args);
            base.OnGameStarting(args);
        }

        /// <summary>
        /// Called when a game stops running. This happens when the game process exits.
        /// </summary>
        public override async void OnGameStopped(OnGameStoppedEventArgs args) {
            await _scrobblingService.OnGameStoppedAsync(args);
            base.OnGameStopped(args);
        }

        /// <summary>
        /// Called when a game has been uninstalled.
        /// </summary>
        public override void OnGameUninstalled(OnGameUninstalledEventArgs args) {
            base.OnGameUninstalled(args);
        }

        /// <summary>
        /// Called when the application is started and initialized. This is a good place for one-time initialization tasks.
        /// </summary>
        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args) {
            await _scrobblingService.SyncLibraryAsync(PlayniteApi.Database.Games);
            base.OnApplicationStarted(args);
        }

        /// <summary>
        /// Called when the application is shutting down. This is the place to clean up resources.
        /// </summary>
        public override async void OnApplicationStopped(OnApplicationStoppedEventArgs args) {
            await _scrobblingService.OnApplicationStoppedAsync();
            base.OnApplicationStopped(args);
        }

        /// <summary>
        /// Called when a library update has been finished. This happens after games are imported or metadata is updated.
        /// </summary>
        public override async void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) {
            await _scrobblingService.SyncLibraryAsync(PlayniteApi.Database.Games);
            base.OnLibraryUpdated(args);
        }

        /// <summary>
        /// Called when game selection changes in the UI.
        /// </summary>
        public override void OnGameSelected(OnGameSelectedEventArgs args) {
            base.OnGameSelected(args);
        }

        /// <summary>
        /// Called when game startup is cancelled by the user or the system.
        /// </summary>
        public override void OnGameStartupCancelled(OnGameStartupCancelledEventArgs args) {
            base.OnGameStartupCancelled(args);
        }

        /// <summary>
        /// Gets plugin settings or null if plugin doesn't provide any settings.
        /// Called by Playnite when it needs to access the plugin's settings.
        /// </summary>
        /// <param name="firstRunSettings">True if this is the first time settings are being requested (e.g., during first run of the plugin).</param>
        /// <returns>The settings object for this plugin.</returns>
        public override ISettings GetSettings(bool firstRunSettings) {
            return (ISettings)_settings;
        }

        /// <summary>
        /// Gets plugin settings view or null if plugin doesn't provide settings view.
        /// Called by Playnite when it needs to display the plugin's settings UI.
        /// </summary>
        /// <param name="firstRunSettings">True if this is the first time settings are being displayed (e.g., during first run of the plugin).</param>
        /// <returns>A UserControl that represents the settings view.</returns>
        public override UserControl GetSettingsView(bool firstRunSettings) {
            return new GsPluginSettingsView();
        }

        /// <summary>
        /// Gets sidebar items provided by this plugin.
        /// Called by Playnite when building the sidebar menu.
        /// </summary>
        /// <returns>A collection of SidebarItem objects to be displayed in the sidebar.</returns>
        public override IEnumerable<SidebarItem> GetSidebarItems() {
            // Return one or more SidebarItem objects
            yield return new SidebarItem {
                Type = (SiderbarItemType)1,
                Title = "Show My Data",
                Icon = new TextBlock { Text = "📋" }, // or a path to an image icon
                Opened = () => {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView(GsSentry.GetPluginVersion());
                },
            };
            // If you want a simple *action* instead of a custom panel, you can
            // return an item with Type = SidebarItemType.Action, plus an OpenCommand.
        }
    }

}
