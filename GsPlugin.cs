using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using Sentry;

namespace GsPlugin {

    public class GsPlugin : GenericPlugin {
        private static readonly ILogger _logger = LogManager.GetLogger();
        private GsPluginSettingsViewModel _settings { get; set; }
        private GsApiClient _apiClient;
        private GsAccountLinkingService _linkingService;
        private GsUriHandler _uriHandler;
        private GsScrobblingService _scrobblingService;
        private GsSuccessStoryHelper _achievementHelper;
        private GsUpdateChecker _updateChecker;
        private bool _disposed;
        private int _achievementSyncInFlight;
        /// <summary>
        /// Unique identifier for the plugin itself.
        /// </summary>
        public override Guid Id { get; } = Guid.Parse("32975fed-6915-4dd3-a230-030cdc5265ae");

        /// <summary>
        /// Constructor for the plugin. Initializes all required services and components.
        /// </summary>
        /// <param name="api">Instance of Playnite API to be injected.</param>
        public GsPlugin(IPlayniteAPI api) : base(api) {

            // Initialize GsDataManager
            GsDataManager.Initialize(GetPluginUserDataPath(), null);

            // Initialize snapshot manager for diff-based sync
            GsSnapshotManager.Initialize(GetPluginUserDataPath());

            // Initialize Sentry for error tracking
            GsSentry.Initialize();

            // Initialize API client
            _apiClient = new GsApiClient();

            // Initialize centralized account linking service
            _linkingService = new GsAccountLinkingService(_apiClient, api);

            // Initialize achievement helper (reads from SuccessStory plugin if installed)
            _achievementHelper = new GsSuccessStoryHelper(api);

            // Create settings with linking service and achievement helper dependencies
            _settings = new GsPluginSettingsViewModel(this, _linkingService, _achievementHelper);
            Properties = new GenericPluginProperties {
                HasSettings = true
            };

            // Initialize scrobbling services
            _scrobblingService = new GsScrobblingService(_apiClient, _achievementHelper);

            // Initialize and register URI handler for automatic account linking
            _uriHandler = new GsUriHandler(api, _linkingService);
            _uriHandler.RegisterUriHandler();

            // Initialize update checker
            _updateChecker = new GsUpdateChecker(api);
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
            try {
                await _scrobblingService.OnGameStartAsync(args);
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnGameStarting");
                GsSentry.CaptureException(ex, "Unhandled exception in OnGameStarting");
            }
            finally {
                base.OnGameStarting(args);
            }
        }

        /// <summary>
        /// Called when a game stops running. This happens when the game process exits.
        /// </summary>
        public override async void OnGameStopped(OnGameStoppedEventArgs args) {
            try {
                await _scrobblingService.OnGameStoppedAsync(args);
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnGameStopped");
                GsSentry.CaptureException(ex, "Unhandled exception in OnGameStopped");
            }
            finally {
                base.OnGameStopped(args);
            }
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
            try {
                // Refresh allowed plugins before syncing library (best-effort, don't block on failure)
                try {
                    await _scrobblingService.RefreshAllowedPluginsAsync();
                }
                catch (Exception ex) {
                    _logger.Warn(ex, "Plugin refresh failed, continuing with cached/hardcoded list");
                }

                // Check for plugin updates and notify user if a newer version is available
                try {
                    await _updateChecker.CheckForUpdateAsync();
                }
                catch (Exception ex) {
                    _logger.Warn(ex, "Update check failed");
                }

                // Flush any scrobbles that were queued during a previous session when the API was unavailable
                await _apiClient.FlushPendingScrobblesAsync();

                var startupSyncResult = await SyncLibraryWithDiffAsync();
                if (startupSyncResult == GsScrobblingService.SyncLibraryResult.Cooldown) {
                    _logger.Info("Startup library sync skipped: sync cooldown is still active.");
                }

                // Run achievement sync unless library sync errored.
                // Cooldown/Skipped mean library items already exist in the DB,
                // so achievement FK references are valid.
                if (startupSyncResult != GsScrobblingService.SyncLibraryResult.Error) {
                    _ = SyncAchievementsWithDiffAsync();
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnApplicationStarted");
                GsSentry.CaptureException(ex, "Unhandled exception in OnApplicationStarted");
            }
            finally {
                base.OnApplicationStarted(args);
            }
        }

        /// <summary>
        /// Called when the application is shutting down. This is the place to clean up resources.
        /// </summary>
        public override async void OnApplicationStopped(OnApplicationStoppedEventArgs args) {
            try {
                await _scrobblingService.OnApplicationStoppedAsync();
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnApplicationStopped");
                GsSentry.CaptureException(ex, "Unhandled exception in OnApplicationStopped");
            }
            finally {
                base.OnApplicationStopped(args);
            }
        }

        /// <summary>
        /// Called when a library update has been finished. This happens after games are imported or metadata is updated.
        /// </summary>
        public override async void OnLibraryUpdated(OnLibraryUpdatedEventArgs args) {
            try {
                var librarySyncResult = await SyncLibraryWithDiffAsync();
                if (librarySyncResult == GsScrobblingService.SyncLibraryResult.Cooldown) {
                    _logger.Info("Library updated sync skipped: sync cooldown is still active.");
                }

                if (librarySyncResult != GsScrobblingService.SyncLibraryResult.Error) {
                    _ = SyncAchievementsWithDiffAsync();
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnLibraryUpdated");
                GsSentry.CaptureException(ex, "Unhandled exception in OnLibraryUpdated");
            }
            finally {
                base.OnLibraryUpdated(args);
            }
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
            // Load the icon from the plugin directory
            var iconPath = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "icon.png");
            var iconImage = new Image {
                Source = new BitmapImage(new Uri(iconPath))
            };

            yield return new SidebarItem {
                Type = (SiderbarItemType)1,
                Title = "Game Spectrum",
                Icon = iconImage,
                Opened = () => {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView(GsSentry.GetPluginVersion());
                },
            };
        }

        /// <summary>
        /// Gets main menu items provided by this plugin.
        /// Called by Playnite when building the Extensions top-level menu.
        /// </summary>
        /// <returns>A collection of MainMenuItem objects to be displayed under Extensions → Game Spectrum.</returns>
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) {
            yield return new MainMenuItem {
                Description = "Open Dashboard",
                MenuSection = "@Game Spectrum",
                Action = _ => {
                    var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions {
                        ShowMinimizeButton = true,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });
                    window.Title = "Game Spectrum Dashboard";
                    window.Width = 1200;
                    window.Height = 800;
                    window.Content = new MySidebarView(GsSentry.GetPluginVersion());
                    window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                    window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                    window.ShowDialog();
                }
            };

            yield return new MainMenuItem {
                Description = "Sync Library Now",
                MenuSection = "@Game Spectrum",
                Action = async menuArgs => {
                    try {
                        var result = await SyncLibraryWithDiffAsync();
                        string message;
                        if (result == GsScrobblingService.SyncLibraryResult.Success) {
                            message = "Library sync completed.";
                        }
                        else if (result == GsScrobblingService.SyncLibraryResult.Skipped) {
                            message = "Library is already up to date.";
                        }
                        else if (result == GsScrobblingService.SyncLibraryResult.Cooldown) {
                            var expiry = GsDataManager.Data.SyncCooldownExpiresAt
                                ?? GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                            if (expiry.HasValue) {
                                var timeLeft = GsTime.FormatRemaining(expiry.Value - DateTime.UtcNow);
                                message = $"Library was already synced recently. Try again in {timeLeft}.";
                            }
                            else {
                                message = "Library was already synced recently. Please try again later.";
                            }
                        }
                        else {
                            message = "Library sync failed. Check logs for details.";
                        }

                        if (result != GsScrobblingService.SyncLibraryResult.Error) {
                            _ = SyncAchievementsWithDiffAsync();
                        }
                        PlayniteApi.Dialogs.ShowMessage(message, "Game Spectrum");
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Error in Sync Library Now menu action");
                        GsSentry.CaptureException(ex, "Error in Sync Library Now menu action");
                        PlayniteApi.Dialogs.ShowMessage("Library sync encountered an error.", "Game Spectrum");
                    }
                }
            };

            yield return new MainMenuItem {
                Description = "Open Settings",
                MenuSection = "@Game Spectrum",
                Action = _ => PlayniteApi.MainView.OpenPluginSettings(Id)
            };
        }

        /// <summary>
        /// Runs a library sync using full or diff based on whether a snapshot baseline exists.
        /// </summary>
        private async Task<GsScrobblingService.SyncLibraryResult> SyncLibraryWithDiffAsync() {
            if (GsSnapshotManager.HasLibraryBaseline) {
                return await _scrobblingService.SyncLibraryDiffAsync(PlayniteApi.Database.Games);
            }
            return await _scrobblingService.SyncLibraryFullAsync(PlayniteApi.Database.Games);
        }

        /// <summary>
        /// Runs an achievements sync using full or diff based on whether a snapshot baseline exists.
        /// Guarded against concurrent execution — overlapping calls are skipped.
        /// </summary>
        private async Task SyncAchievementsWithDiffAsync() {
            if (Interlocked.CompareExchange(ref _achievementSyncInFlight, 1, 0) != 0) {
                _logger.Info("Achievement sync already in flight — skipping.");
                return;
            }
            try {
                if (GsSnapshotManager.HasAchievementsBaseline) {
                    await _scrobblingService.SyncAchievementsDiffAsync(PlayniteApi.Database.Games);
                }
                else {
                    await _scrobblingService.SyncAchievementsFullAsync(PlayniteApi.Database.Games);
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Achievement sync failed");
                GsSentry.CaptureException(ex, "Achievement sync failed");
            }
            finally {
                Interlocked.Exchange(ref _achievementSyncInFlight, 0);
            }
        }

        /// <summary>
        /// Releases resources used by the plugin.
        /// </summary>
        public override void Dispose() {
            if (!_disposed) {
                _disposed = true;

                try {
                    SentrySdk.Close();
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error closing Sentry");
                }

                _apiClient = null;
                _linkingService = null;
                _uriHandler = null;
                _scrobblingService = null;
            }

            base.Dispose();
        }
    }

}
