using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Playnite;
using Sentry;
using GsPlugin.Api;
using GsPlugin.Infrastructure;
using GsPlugin.Models;
using GsPlugin.Services;
using GsPlugin.View;

namespace GsPlugin {

    /// <summary>
    /// Static plugin identity referenced by the generated Localization.cs.
    /// The plugin ID must match extension.toml.
    /// </summary>
    public static class GsPluginPlugin {
        public const string Id = "GameScrobbler.GsPlugin";
    }

    public class GsPlugin : Plugin {
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

        /// <summary>
        /// The static Playnite API instance, set during InitializeAsync.
        /// Used by services that need the API after construction.
        /// </summary>
        public static IPlayniteApi PlayniteApi { get; private set; } = null!;

        private GsPluginSettingsViewModel _settings;
        private GsApiClient _apiClient;
        private GsAccountLinkingService _linkingService;
        private GsUriHandler _uriHandler;
        private GsScrobblingService _scrobblingService;
        private GsAchievementAggregator _achievementHelper;
        private GsUpdateChecker _updateChecker;
        private GsNotificationService _notificationService;
        private bool _disposed;
        private int _librarySyncInFlight;
        private int _achievementSyncInFlight;
        private Timer _pendingFlushTimer;

        /// <summary>
        /// Tracks the known set of SessionIds per game to detect game starts/stops
        /// via OnGameCollectionChange (P11 session-based tracking model).
        /// Key: game.Id.ToString(), Value: set of known session IDs.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> _knownSessionIds = new Dictionary<string, HashSet<string>>();

        // No-arg constructor required by P11.
        public GsPlugin() { }

        public override async Task InitializeAsync(InitializeArgs args) {
            PlayniteApi = args.Api;
            Loc.Api = args.Api;

            // Initialize GsDataManager with the plugin-specific data directory
            GsDataManager.Initialize(args.Api.UserDataDir, null);

            // Initialize snapshot manager for diff-based sync
            GsSnapshotManager.Initialize(args.Api.UserDataDir);

            // Initialize Sentry for error tracking
            GsSentry.Initialize();

            // Initialize PostHog for product analytics
            GsPostHog.Initialize();

            // Initialize API client
            _apiClient = new GsApiClient();

            // Initialize centralized account linking service
            _linkingService = new GsAccountLinkingService(_apiClient, args.Api);

            // Initialize achievement providers (SuccessStory and Playnite Achievements)
            var successStoryHelper = new GsSuccessStoryHelper(args.Api);
            var playniteAchievementsHelper = new GsPlayniteAchievementsHelper(args.Api);
            _achievementHelper = new GsAchievementAggregator(successStoryHelper, playniteAchievementsHelper);

            // Initialize settings view model
            _settings = new GsPluginSettingsViewModel(
                args.Api.UserDataDir, _linkingService, _achievementHelper, _apiClient);

            // Initialize scrobbling service
            var integrationAccountReader = new GsIntegrationAccountReader(args.Api);
            _scrobblingService = new GsScrobblingService(_apiClient, _achievementHelper, integrationAccountReader);

            // Initialize and register URI handler for automatic account linking
            _uriHandler = new GsUriHandler(args.Api, _linkingService);
            _uriHandler.RegisterUriHandler();

            // Initialize update checker
            _updateChecker = new GsUpdateChecker(args.Api);

            // Initialize server notification service
            _notificationService = new GsNotificationService(args.Api, _apiClient, GsPluginPlugin.Id);

            // Run startup sequence asynchronously (non-blocking)
            _ = RunStartupAsync();
        }

        private async Task RunStartupAsync() {
            if (GsDataManager.IsOptedOut) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isFirstRun = GsDataManager.Data.LastSyncAt == null
                && string.IsNullOrEmpty(GsDataManager.Data.InstallToken);
            try {
                GsPostHog.Capture("plugin_started", new Dictionary<string, object> {
                    { "version", GsSentry.GetPluginVersion() },
                    { "linked", !string.IsNullOrEmpty(GsDataManager.DataOrNull?.LinkedUserId) },
                    { "first_run", isFirstRun }
                });

                if (isFirstRun) {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        "gs-first-run-setup",
                        "Game Scrobbler: Setting up for the first time\u2026",
                        NotificationSeverity.Info));
                }

                var tokenTask = EnsureInstallTokenAsync();
                _ = FetchNotificationsAfterTokenAsync(tokenTask);

                if (GsDataManager.IsOptedOut) return;

                var refreshTask = GsAllowedPlugins.RefreshAsync(_apiClient)
                    .ContinueWith(t => {
                        if (t.IsFaulted)
                            _logger.Warn(t.Exception.GetBaseException(), "Plugin refresh failed, continuing with cached/hardcoded list");
                    });
                var updateTask = _updateChecker.CheckForUpdateAsync()
                    .ContinueWith(t => {
                        if (t.IsFaulted)
                            _logger.Warn(t.Exception.GetBaseException(), "Update check failed");
                    });
                await Task.WhenAll(refreshTask, updateTask);

                if (GsDataManager.IsOptedOut) return;

                _ = _apiClient.FlushPendingScrobblesAsync().ContinueWith(t => {
                    if (t.IsFaulted)
                        _logger.Warn(t.Exception.GetBaseException(), "Startup flush failed");
                });

                _pendingFlushTimer = new Timer(_ => {
                    if (_disposed) return;
                    var api = _apiClient;
                    if (api == null || GsDataManager.IsOptedOut) return;
                    try {
                        _ = api.FlushPendingScrobblesAsync().ContinueWith(t => {
                            if (t.IsFaulted)
                                _logger.Warn(t.Exception?.GetBaseException(), "Periodic pending flush failed");
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (ObjectDisposedException) { }
                }, null, (int)TimeSpan.FromMinutes(5).TotalMilliseconds, (int)TimeSpan.FromMinutes(5).TotalMilliseconds);

                var startupSyncResult = await SyncLibraryWithDiffAsync();
                if (startupSyncResult == SyncLibraryResult.Cooldown) {
                    _logger.Info("Startup library sync skipped: sync cooldown is still active.");
                }

                if (isFirstRun) {
                    PlayniteApi.Notifications.Remove("gs-first-run-setup");
                    if (startupSyncResult == SyncLibraryResult.Success) {
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "gs-first-run-done",
                            "Game Scrobbler: Setup complete \u2014 your library has been synced.",
                            NotificationSeverity.Info));
                    }
                    else if (startupSyncResult == SyncLibraryResult.Error) {
                        PlayniteApi.Notifications.Add(new NotificationMessage(
                            "gs-first-run-error",
                            "Game Scrobbler: First-time sync failed. It will retry automatically on next launch.",
                            NotificationSeverity.Error));
                    }
                }

                if (startupSyncResult != SyncLibraryResult.Error) {
                    _ = SyncAchievementsWithDiffAsync().ContinueWith(t => {
                        if (t.IsFaulted)
                            _logger.Warn(t.Exception?.GetBaseException(), "Startup achievement sync failed");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                }

                sw.Stop();
                GsPostHog.Capture("startup_completed", new Dictionary<string, object> {
                    { "elapsed_ms", sw.ElapsedMilliseconds },
                    { "sync_result", startupSyncResult.ToString() }
                });
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in RunStartupAsync");
                GsSentry.CaptureException(ex, "Unhandled exception in RunStartupAsync");
            }
            finally {
                if (isFirstRun) {
                    PlayniteApi.Notifications.Remove("gs-first-run-setup");
                }
            }
        }

        /// <summary>
        /// Called when the game collection changes. Used to detect game sessions starting/stopping
        /// (P11 replaced OnGameStarting/OnGameStopped with session-based collection change events)
        /// and to trigger library sync when games are added.
        /// </summary>
        public override async Task OnGameCollectionChange(DataCollectionChangeArgs<Game> args) {
            try {
                // Detect game session start/stop via SessionIds changes
                var changedGames = args.UpdatedItems.Select(x => x.NewData).Concat(args.AddedItems).ToList();
                foreach (var game in changedGames) {
                    if (GsDataManager.IsOptedOut) break;
                    if (string.IsNullOrEmpty(game.LibraryId)
                        || !GsAllowedPlugins.AllowedPluginIds.Contains(game.LibraryId))
                        continue;

                    var gameId = game.Id.ToString();
                    var currentIds = new HashSet<string>(game.SessionIds ?? Enumerable.Empty<string>());

                    if (!_knownSessionIds.TryGetValue(gameId, out var knownIds)) {
                        // First time seeing this game — seed baseline without triggering a start
                        _knownSessionIds[gameId] = new HashSet<string>(currentIds);
                        continue;
                    }

                    // New session IDs → game just started
                    var newIds = currentIds.Except(knownIds).ToList();
                    if (newIds.Count > 0) {
                        _knownSessionIds[gameId] = new HashSet<string>(currentIds);
                        GsPostHog.Capture("game_session_started", new Dictionary<string, object> {
                            { "platform_id", game.LibraryId ?? "unknown" }
                        });
                        _ = _scrobblingService.OnGameStartAsync(game).ContinueWith(t => {
                            if (t.IsFaulted)
                                _logger.Warn(t.Exception?.GetBaseException(), $"Game start scrobble failed for {game.Name}");
                        });
                    }
                    // TODO P11: detect game stop when a GameSession's Length updates from 0 to non-zero.
                    // Requires access to PlayniteApi.Library.GameSessions to read session length.
                    // For now, game stop is handled via OnApplicationStopped cleanup only.
                }

                // Trigger library sync when games are added (e.g. after a library import)
                if (!GsDataManager.IsOptedOut && args.AddedItems.Count > 0) {
                    _ = SyncLibraryWithDiffAsync().ContinueWith(t => {
                        if (t.IsFaulted)
                            _logger.Warn(t.Exception?.GetBaseException(), "Library sync after game add failed");
                    });
                }
            }
            catch (Exception ex) {
                _logger.Error(ex, "Unhandled exception in OnGameCollectionChange");
                GsSentry.CaptureException(ex, "Unhandled exception in OnGameCollectionChange");
            }
        }

        public override ICollection<AppViewItemDescriptor>? GetAppViewItemDescriptors(GetAppViewItemDescriptorsArgs args) {
            if (GsDataManager.IsOptedOut) return null;

            string? iconPath = null;
            try {
                var candidate = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location) ?? string.Empty, "icon.png");
                if (File.Exists(candidate)) iconPath = candidate;
            }
            catch (Exception ex) {
                _logger.Warn(ex, "Failed to locate sidebar icon");
            }

            return new[] {
                new AppViewItemDescriptor(
                    "gs-dashboard",
                    "Game Scrobbler",
                    iconPath != null ? (_) => UIIcon.FromBitmapFile(iconPath) : (_) => UIIcon.FromFontIcon("f11b", Fonts.NerdFont),
                    iconPath != null ? (_) => UIIcon.FromBitmapFile(iconPath) : (_) => UIIcon.FromFontIcon("f11b", Fonts.NerdFont))
            };
        }

        public override AppViewItem? GetAppViewItem(GetAppViewItemsArgs args) {
            if (args.ViewId == "gs-dashboard") {
                return new GsDashboardView(_apiClient);
            }
            return null;
        }

        public override async Task<PluginSettingsHandler?> GetSettingsHandlerAsync(GetSettingsHandlerArgs args) {
            return new GsPluginSettingsHandler(_settings);
        }

        public override ICollection<MenuItemDescriptor>? GetAppMenuItemDescriptors(GetAppMenuItemDescriptorsArgs args) {
            var items = new List<MenuItemDescriptor>();
            if (GsDataManager.IsOptedOut) {
                items.Add(new MenuItemDescriptor("gs-settings", "Open Settings", "Game Scrobbler"));
            }
            else {
                items.Add(new MenuItemDescriptor("gs-sync", GsLocalization.Get("LOCGsPluginMenuSyncLibrary", "Sync Library Now"), "Game Scrobbler"));
                items.Add(new MenuItemDescriptor("gs-settings", GsLocalization.Get("LOCGsPluginMenuOpenSettings", "Open Settings"), "Game Scrobbler"));
            }
            return items;
        }

        public override ICollection<MenuItemImpl>? GetAppMenuItems(GetAppMenuItemsArgs args) {
            if (args.ItemId == "gs-sync") {
                return [new MenuItemImpl(
                    GsLocalization.Get("LOCGsPluginMenuSyncLibrary", "Sync Library Now"),
                    async () => {
                        try {
                            var result = await SyncLibraryWithDiffAsync();
                            string message;
                            if (result == SyncLibraryResult.Success) {
                                message = GsLocalization.Get("LOCGsPluginSyncCompleted", "Library sync completed.");
                            }
                            else if (result == SyncLibraryResult.Skipped) {
                                message = GsLocalization.Get("LOCGsPluginSyncUpToDate", "Library is already up to date.");
                            }
                            else if (result == SyncLibraryResult.Cooldown) {
                                var expiry = GsDataManager.Data.SyncCooldownExpiresAt
                                    ?? GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                                if (expiry.HasValue) {
                                    var timeLeft = GsTime.FormatRemaining(expiry.Value - DateTime.UtcNow);
                                    message = GsLocalization.Format("LOCGsPluginSyncCooldownFormat",
                                        $"Library was already synced recently. Try again in {timeLeft}.", timeLeft);
                                }
                                else {
                                    message = GsLocalization.Get("LOCGsPluginSyncCooldownGeneric", "Library was already synced recently. Please try again later.");
                                }
                            }
                            else {
                                message = GsLocalization.Get("LOCGsPluginSyncFailed", "Library sync failed. Check logs for details.");
                            }

                            if (result != SyncLibraryResult.Error) {
                                _ = SyncAchievementsWithDiffAsync().ContinueWith(t => {
                                    if (t.IsFaulted)
                                        _logger.Warn(t.Exception?.GetBaseException(), "Manual achievement sync failed");
                                }, TaskContinuationOptions.OnlyOnFaulted);
                            }
                            await PlayniteApi.Dialogs.ShowMessageAsync(message, "Game Scrobbler");
                        }
                        catch (Exception ex) {
                            _logger.Error(ex, "Error in Sync Library Now menu action");
                            GsSentry.CaptureException(ex, "Error in Sync Library Now menu action");
                            await PlayniteApi.Dialogs.ShowMessageAsync(
                                GsLocalization.Get("LOCGsPluginSyncError", "Library sync encountered an error."), "Game Scrobbler");
                        }
                    })];
            }

            if (args.ItemId == "gs-settings") {
                return [new MenuItemImpl(
                    GsLocalization.Get("LOCGsPluginMenuOpenSettings", "Open Settings"),
                    async () => await PlayniteApi.MainView.OpenPluginSettingsAsync(GsPluginPlugin.Id))];
            }

            return null;
        }

        private async Task EnsureInstallTokenAsync() {
            if (!string.IsNullOrEmpty(GsDataManager.Data.InstallToken)) {
                return;
            }

            var installId = GsDataManager.Data.InstallID;
            if (string.IsNullOrEmpty(installId)) {
                _logger.Warn("EnsureInstallTokenAsync: no InstallID available, skipping registration");
                return;
            }

            try {
                _logger.Info("Registering install token with server");

                RegisterInstallTokenRes result = null;
                for (int attempt = 0; attempt < 3; attempt++) {
                    if (attempt > 0) {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    }
                    result = await _apiClient.RegisterInstallToken(installId);
                    if (result != null) break;
                    _logger.Warn($"EnsureInstallTokenAsync: attempt {attempt + 1}/3 returned null");
                }

                if (result == null) {
                    _logger.Warn("EnsureInstallTokenAsync: all registration attempts failed; will retry on next startup");
                    return;
                }

                if (result.success && !string.IsNullOrEmpty(result.token)) {
                    if (GsDataManager.SetInstallTokenIfActive(result.token)) {
                        _logger.Info("Install token registered and stored successfully");
                    }
                    else {
                        _logger.Warn("EnsureInstallTokenAsync: opt-out occurred during registration; token discarded");
                    }
                    return;
                }

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

        private async Task<SyncLibraryResult> SyncLibraryWithDiffAsync() {
            if (Interlocked.CompareExchange(ref _librarySyncInFlight, 1, 0) != 0) {
                _logger.Info("Library sync already in flight — skipping.");
                return SyncLibraryResult.Skipped;
            }
            try {
                if (GsSnapshotManager.HasLibraryBaseline) {
                    return await _scrobblingService.SyncLibraryDiffAsync(PlayniteApi.Library.Games);
                }
                return await _scrobblingService.SyncLibraryFullAsync(PlayniteApi.Library.Games);
            }
            finally {
                Interlocked.Exchange(ref _librarySyncInFlight, 0);
            }
        }

        private async Task SyncAchievementsWithDiffAsync() {
            if (Interlocked.CompareExchange(ref _achievementSyncInFlight, 1, 0) != 0) {
                _logger.Info("Achievement sync already in flight — skipping.");
                return;
            }
            try {
                if (GsSnapshotManager.HasAchievementsBaseline) {
                    await _scrobblingService.SyncAchievementsDiffAsync(PlayniteApi.Library.Games);
                }
                else {
                    await _scrobblingService.SyncAchievementsFullAsync(PlayniteApi.Library.Games);
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

        public override async ValueTask DisposeAsync() {
            if (!_disposed) {
                _disposed = true;

                // Clean up active game session on shutdown
                if (!GsDataManager.IsOptedOut) {
                    try {
                        GsPostHog.Capture("plugin_stopped");
                        await _scrobblingService.OnApplicationStoppedAsync();
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Error during application stop cleanup");
                    }
                }

                try {
                    _pendingFlushTimer?.Dispose();
                    _pendingFlushTimer = null;
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error disposing flush timer");
                }

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
        }
    }

}
