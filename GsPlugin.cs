using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using GsPlugin.Services;
using GsPlugin.View;

namespace GsPlugin {

    public class GsPlugin : GenericPlugin {
        private static readonly ILogger _logger = LogManager.GetLogger();

        /// <summary>
        /// Resolves assembly version mismatches at runtime.
        /// Playnite hosts plugins in its own AppDomain and does not honour plugin-level
        /// binding redirects, so we redirect assemblies that ship with the plugin ourselves.
        /// </summary>
        static GsPlugin() {
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                var name = new AssemblyName(args.Name);
                var path = Path.Combine(pluginDir, name.Name + ".dll");
                if (File.Exists(path)) {
                    return Assembly.LoadFrom(path);
                }
                return null;
            };
        }
        private GsPluginSettingsViewModel _settings { get; set; }
        private GsApiClient _apiClient;
        private GsAccountLinkingService _linkingService;
        private GsUriHandler _uriHandler;
        private GsScrobblingService _scrobblingService;
        private GsAchievementAggregator _achievementHelper;
        private GsUpdateChecker _updateChecker;
        private GsNotificationService _notificationService;
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

            // Initialize PostHog for product analytics
            GsPostHog.Initialize();

            // Initialize API client
            _apiClient = new GsApiClient();

            // Initialize centralized account linking service
            _linkingService = new GsAccountLinkingService(_apiClient, api);

            // Initialize achievement providers (SuccessStory and Playnite Achievements)
            var successStoryHelper = new GsSuccessStoryHelper(api);
            var playniteAchievementsHelper = new GsPlayniteAchievementsHelper(api);
            _achievementHelper = new GsAchievementAggregator(successStoryHelper, playniteAchievementsHelper);

            // Create settings with linking service and achievement helper dependencies
            _settings = new GsPluginSettingsViewModel(this, _linkingService, _achievementHelper, _apiClient);
            Properties = new GenericPluginProperties {
                HasSettings = true
            };

            // Initialize scrobbling services
            var integrationAccountReader = new GsIntegrationAccountReader(api);
            _scrobblingService = new GsScrobblingService(_apiClient, _achievementHelper, integrationAccountReader);

            // Initialize and register URI handler for automatic account linking
            _uriHandler = new GsUriHandler(api, _linkingService);
            _uriHandler.RegisterUriHandler();

            // Initialize update checker
            _updateChecker = new GsUpdateChecker(api);

            // Initialize server notification service
            _notificationService = new GsNotificationService(api, _apiClient, Id);
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
            if (GsDataManager.IsOptedOut) { base.OnGameStarting(args); return; }
            try {
                GsPostHog.Capture("game_session_started", new Dictionary<string, object> {
                    { "platform_id", args.Game?.PluginId.ToString() ?? "unknown" }
                });
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
            if (GsDataManager.IsOptedOut) { base.OnGameStopped(args); return; }
            try {
                GsPostHog.Capture("game_session_ended", new Dictionary<string, object> {
                    { "elapsed_seconds", args.ElapsedSeconds },
                    { "platform_id", args.Game?.PluginId.ToString() ?? "unknown" }
                });
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
            if (GsDataManager.IsOptedOut) { base.OnApplicationStarted(args); return; }
            try {
                GsPostHog.Capture("plugin_started", new Dictionary<string, object> {
                    { "version", GsSentry.GetPluginVersion() },
                    { "linked", !string.IsNullOrEmpty(GsDataManager.DataOrNull?.LinkedUserId) }
                });

                // Ensure the install has a server-issued auth token. Best-effort, fire-and-forget:
                // registration is a one-time step on first boot and must not stall the rest of
                // startup (plugin refresh, update check, queue flush, library sync) for up to 30 s
                // on first run or during API outages.
                var tokenTask = EnsureInstallTokenAsync();

                // Fire-and-forget: fetch server notifications on a background thread after token
                // registration completes, so we never block the startup critical path and notifications
                // are available even when the token is freshly registered on first run.
                _ = FetchNotificationsAfterTokenAsync(tokenTask);

                // Re-check opt-out after token registration (user may have opted out during startup)
                if (GsDataManager.IsOptedOut) { base.OnApplicationStarted(args); return; }

                // Refresh allowed plugins before syncing library (best-effort, don't block on failure)
                try {
                    await _scrobblingService.RefreshAllowedPluginsAsync();
                }
                catch (Exception ex) {
                    _logger.Warn(ex, "Plugin refresh failed, continuing with cached/hardcoded list");
                }

                // Re-check opt-out after async refresh (user may have opted out during startup)
                if (GsDataManager.IsOptedOut) { base.OnApplicationStarted(args); return; }

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
            if (GsDataManager.IsOptedOut) { base.OnApplicationStopped(args); return; }
            try {
                GsPostHog.Capture("plugin_stopped");
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
            if (GsDataManager.IsOptedOut) { base.OnLibraryUpdated(args); return; }
            try {
                GsPostHog.Capture("library_synced", new Dictionary<string, object> {
                    { "game_count", PlayniteApi.Database.Games?.Count ?? 0 }
                });
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
            if (GsDataManager.IsOptedOut) yield break;
            // Load the icon from the plugin directory
            var iconPath = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "icon.png");
            var iconImage = new Image {
                Source = new BitmapImage(new Uri(iconPath))
            };

            yield return new SidebarItem {
                Type = (SiderbarItemType)1,
                Title = "Game Scrobbler",
                Icon = iconImage,
                Opened = () => {
                    // Return a new instance of your custom UserControl (WPF)
                    return new MySidebarView(GsSentry.GetPluginVersion(), _apiClient);
                },
            };
        }

        /// <summary>
        /// Gets main menu items provided by this plugin.
        /// Called by Playnite when building the Extensions top-level menu.
        /// </summary>
        /// <returns>A collection of MainMenuItem objects to be displayed under Extensions → Game Scrobbler.</returns>
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args) {
            if (GsDataManager.IsOptedOut) {
                yield return new MainMenuItem {
                    Description = "Open Settings",
                    MenuSection = "@Game Scrobbler",
                    Action = _ => PlayniteApi.MainView.OpenPluginSettings(Id)
                };
                yield break;
            }
            yield return new MainMenuItem {
                Description = "Open Dashboard",
                MenuSection = "@Game Scrobbler",
                Action = _ => {
                    var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions {
                        ShowMinimizeButton = true,
                        ShowMaximizeButton = true,
                        ShowCloseButton = true
                    });
                    window.Title = "Game Scrobbler Dashboard";
                    window.Width = 1200;
                    window.Height = 800;
                    window.Content = new MySidebarView(GsSentry.GetPluginVersion(), _apiClient);
                    window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                    window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                    window.ShowDialog();
                }
            };

            yield return new MainMenuItem {
                Description = "Sync Library Now",
                MenuSection = "@Game Scrobbler",
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
                        PlayniteApi.Dialogs.ShowMessage(message, "Game Scrobbler");
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Error in Sync Library Now menu action");
                        GsSentry.CaptureException(ex, "Error in Sync Library Now menu action");
                        PlayniteApi.Dialogs.ShowMessage("Library sync encountered an error.", "Game Scrobbler");
                    }
                }
            };

            yield return new MainMenuItem {
                Description = "Open Settings",
                MenuSection = "@Game Scrobbler",
                Action = _ => PlayniteApi.MainView.OpenPluginSettings(Id)
            };
        }

        /// <summary>
        /// Ensures the install has a valid server-issued auth token stored in GsData.
        /// Runs fire-and-forget from OnApplicationStarted so it never blocks startup.
        ///
        /// Flow:
        ///   - If a token is already stored → nothing to do.
        ///   - If no token → call /v2/register. On success store the token.
        ///   - If register returns 409 PLAYNITE_TOKEN_ALREADY_REGISTERED → local token was lost.
        ///     Rotate to a new InstallID (abandoning the old server-side identity) and re-register
        ///     immediately. This is deterministic and requires no missing old token.
        ///   - Persisting the token is guarded against a concurrent opt-out.
        /// </summary>
        private async Task EnsureInstallTokenAsync() {
            if (!string.IsNullOrEmpty(GsDataManager.Data.InstallToken)) {
                // Token already present — nothing to do.
                return;
            }

            var installId = GsDataManager.Data.InstallID;
            if (string.IsNullOrEmpty(installId)) {
                _logger.Warn("EnsureInstallTokenAsync: no InstallID available, skipping registration");
                return;
            }

            try {
                _logger.Info("Registering install token with server");
                var result = await _apiClient.RegisterInstallToken(installId);

                if (result == null) {
                    _logger.Warn("EnsureInstallTokenAsync: registration call returned null (network error?)");
                    return;
                }

                if (result.success && !string.IsNullOrEmpty(result.token)) {
                    // SetInstallTokenIfActive atomically checks opt-out and writes under lock,
                    // preventing a race where PerformOptOut() clears the token between our check
                    // and our write.
                    if (GsDataManager.SetInstallTokenIfActive(result.token)) {
                        _logger.Info("Install token registered and stored successfully");
                    }
                    else {
                        _logger.Warn("EnsureInstallTokenAsync: opt-out occurred during registration; token discarded");
                    }
                    return;
                }

                // 409: token already issued for this install ID but the local copy was lost.
                // Recovery: rotate to a fresh InstallID so we can re-register immediately without
                // needing the missing old token. The old server-side identity is abandoned.
                if (result.error_code == "PLAYNITE_TOKEN_ALREADY_REGISTERED") {
                    _logger.Warn("EnsureInstallTokenAsync: lost-token conflict — rotating InstallID and re-registering");
                    var newInstallId = GsDataManager.RotateInstallId();
                    var retryResult = await _apiClient.RegisterInstallToken(newInstallId);
                    if (retryResult != null && retryResult.success && !string.IsNullOrEmpty(retryResult.token)) {
                        if (GsDataManager.SetInstallTokenIfActive(retryResult.token)) {
                            _logger.Info("Install token recovered via InstallID rotation");
                        }
                        else {
                            _logger.Warn("EnsureInstallTokenAsync: opt-out during retry registration; token discarded");
                        }
                    }
                    else {
                        _logger.Warn("EnsureInstallTokenAsync: re-registration after rotation failed; will retry on next startup");
                    }
                    return;
                }

                _logger.Warn($"EnsureInstallTokenAsync: unexpected registration result — " +
                    $"success={result.success}, error_code={result.error_code ?? "(none)"}, " +
                    $"error={result.error ?? "(none)"}");
            }
            catch (Exception ex) {
                _logger.Error(ex, "EnsureInstallTokenAsync failed");
                GsSentry.CaptureException(ex, "EnsureInstallTokenAsync failed");
            }
        }

        /// <summary>
        /// Waits for token registration to complete, then fetches and displays server notifications.
        /// Runs fire-and-forget so it never blocks the startup critical path.
        /// </summary>
        private async Task FetchNotificationsAfterTokenAsync(Task tokenTask) {
            try {
                await tokenTask.ConfigureAwait(false);
                if (GsDataManager.IsOptedOut) return;
                await _notificationService.FetchAndShowNotificationsAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                _logger.Warn(ex, "Server notification fetch failed");
            }
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
                    GsPostHog.Shutdown();
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error closing PostHog");
                }

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
