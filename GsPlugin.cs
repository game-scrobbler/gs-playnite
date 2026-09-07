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
        private static readonly ILogger _logger = LogManager.GetLogger<GsPlugin>();

        // Playnite 11 loads each extension into its own AssemblyLoadContext and the project
        // targets .NET 10, so the AppDomain.AssemblyResolve shim the Playnite 10 build needed
        // for binding redirects is neither necessary nor effective here and has been removed.

        /// <summary>
        /// The static Playnite API instance, set during InitializeAsync.
        /// Used by services that need the API after construction.
        /// </summary>
        public static IPlayniteApi PlayniteApi { get; private set; } = null!;

        private GsPluginSettingsViewModel _settings;
        /// <summary>
        /// Private WebView2 profile directory, kept inside the plugin's own data folder. The default
        /// profile is derived from the host process and is therefore shared by every Playnite
        /// extension that hosts a WebView2 - including the dashboard's authenticated cookies and the
        /// URL history holding its access_token.
        /// </summary>
        private static string WebViewUserDataFolder => Path.Combine(PlayniteApi.UserDataDir, "WebView2");
        private GsApiClient _apiClient;
        private GsAccountLinkingService _linkingService;
        private GsUriHandler _uriHandler;
        private GsScrobblingService _scrobblingService;
        private GsUpdateChecker _updateChecker;
        private GsNotificationService _notificationService;
        private bool _disposed;
        /// <summary>
        /// Set when <see cref="GsDataManager.Initialize"/> could not read the data file. Every
        /// entry point below returns early on it, because the services it would use were never
        /// constructed. Checked before <see cref="GsDataManager.IsOptedOut"/>, which reports false
        /// when data is unavailable and so cannot stand in for this.
        /// </summary>
        private bool _dataUnavailable;
        private int _librarySyncInFlight;
        private Timer _pendingFlushTimer;
        /// <summary>
        /// Synchronizes _pendingFlushTimer creation (OnApplicationStarted, after several
        /// awaits) with its disposal (Dispose()) so a shutdown racing startup can't leave
        /// an orphaned timer created after _disposed was already set.
        /// </summary>
        private readonly object _timerLock = new object();
        /// <summary>
        /// Serializes the install-token registration flow so startup (EnsureInstallTokenAsync)
        /// and on-demand callers (EnsureInstallTokenReadyAsync, e.g. the "Delete My Data"
        /// button) cannot register concurrently and double-register the install.
        /// </summary>
        private readonly SemaphoreSlim _installTokenGate = new SemaphoreSlim(1, 1);
        // No-arg constructor required by P11.
        public GsPlugin() { }

        public override async Task InitializeAsync(InitializeArgs args) {
            PlayniteApi = args.Api;
            Loc.Api = args.Api;

            // Initialize GsDataManager with the plugin-specific data directory
            try {
                GsDataManager.Initialize(args.Api.UserDataDir, null);
            }
            catch (Exception ex) {
                // Initialize deliberately refuses to invent a fresh install over a file it could
                // not read: consent lives in that file, and a new identity would silently re-enable
                // telemetry for someone who had opted out. Preserving it is right. Letting the
                // throw escape was not: the extension then failed to load at all, so settings,
                // "Delete My Data" and opt-out were unreachable and the only remedy was deleting
                // the file by hand.
                //
                // Stay loaded and completely inert instead: no services, no sync, no scrobbling,
                // no telemetry (nothing is sent, so consent is still honoured), and tell the user
                // where the file is so they can move or repair it.
                _dataUnavailable = true;
                _logger.Error(ex, "Plugin data could not be read; starting in a disabled state.");
                try {
                    args.Api.Notifications.Add(new NotificationMessage(
                        "gs-data-unreadable",
                        Loc.data_unreadable(Path.Combine(args.Api.UserDataDir, "gs_data.json")),
                        NotificationSeverity.Error));
                }
                catch (Exception notifyEx) {
                    _logger.Error(notifyEx, "Could not surface the unreadable-data notification");
                }
                return;
            }

            // Initialize the per-item hash index used for diff-based sync
            GsSyncHashIndex.Initialize(args.Api.UserDataDir);

            // Initialize Sentry for error tracking
            GsSentry.Initialize();

            // Initialize PostHog for product analytics
            GsPostHog.Initialize();

            // Initialize API client
            _apiClient = new GsApiClient();

            // Initialize centralized account linking service
            _linkingService = new GsAccountLinkingService(_apiClient, args.Api);

            // Initialize settings view model
            // Create settings with the linking service dependency.
            _settings = new GsPluginSettingsViewModel(
                args.Api.UserDataDir, _linkingService, _apiClient, this);
            // Load saved privacy preferences before starting either telemetry SDK.
            GsSentry.ApplyPreferences();
            GsPostHog.ApplyPreferences();

            // Initialize scrobbling service
            var integrationAccountReader = new GsIntegrationAccountReader(args.Api);
            _scrobblingService = new GsScrobblingService(_apiClient, integrationAccountReader, args.Api.Library);

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

        /// <summary>
        /// Shared wrapper for the Playnite event-handler overrides. Skips <paramref name="body"/>
        /// when the user has opted out, and turns any unhandled exception into a log entry plus a
        /// Sentry report so a fault in one handler cannot take Playnite down with it.
        /// </summary>
        /// <param name="name">Handler name used in the log and Sentry messages.</param>
        /// <param name="body">Handler work, run only when the user has not opted out.</param>
        private static async Task GuardedAsync(string name, Func<Task> body) {
            if (GsDataManager.IsOptedOut) {
                return;
            }
            try {
                await body();
            }
            catch (Exception ex) {
                _logger.Error(ex, $"Unhandled exception in {name}");
                GsSentry.CaptureException(ex, $"Unhandled exception in {name}");
            }
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

                // Start install-token registration immediately, in parallel with the independent
                // startup work below. v4 sync is token-authenticated, so the task is joined just
                // before the first sync rather than sending an unauthenticated begin request.
                var tokenTask = EnsureInstallTokenAsync();
                _ = FetchNotificationsAfterTokenAsync(tokenTask);

                if (GsDataManager.IsOptedOut) return;

                var refreshTask = GsAllowedPlugins.RefreshAsync(_apiClient)
                    .LogFaults("Plugin refresh failed, continuing with cached/hardcoded list");
                var updateTask = _updateChecker.CheckForUpdateAsync()
                    .LogFaults("Update check failed");
                try {
                    await Task.WhenAll(refreshTask, updateTask);
                }
                catch (Exception) {
                    // Both tasks are best-effort and LogFaults already logged the failure.
                    // Swallow here so a failed refresh or update check cannot abort the
                    // startup work below.
                }

                if (GsDataManager.IsOptedOut) return;

                // Flush pending scrobbles fire-and-forget so library sync starts immediately.
                // The periodic timer below catches any items not flushed by the time it fires.
                _ = _apiClient.FlushPendingScrobblesAsync().LogFaults("Startup flush failed");

                // Start periodic flush timer — every 5 minutes, retry any remaining queued scrobbles.
                // Guarded by _timerLock so a Dispose() racing this (e.g. plugin unloaded or
                // Playnite closed shortly after startup) can't create a timer after shutdown
                // already ran, which would otherwise leak an un-disposed Timer for the life
                // of the process.
                lock (_timerLock) {
                    if (!_disposed) {
                        _pendingFlushTimer = new Timer(_ => {
                            if (_disposed) return;
                            var api = _apiClient;
                            if (api == null || GsDataManager.IsOptedOut) return;
                            try {
                                _ = api.FlushPendingScrobblesAsync().LogFaults("Periodic pending flush failed");
                            }
                            catch (ObjectDisposedException) {
                                // Timer fired after Dispose() — safe to ignore
                            }
                        }, null, (int)TimeSpan.FromMinutes(5).TotalMilliseconds, (int)TimeSpan.FromMinutes(5).TotalMilliseconds);
                    }
                }

                await tokenTask;
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
        /// <summary>
        /// Fired before a game launches. Playnite 11 alpha exposes the real game lifecycle
        /// callbacks, so the SessionIds-diffing workaround this branch used while the API was
        /// missing is gone, and stops are reported again rather than being inferred at exit.
        /// </summary>
        public override async Task OnGameStartingAsync(OnGameStartingEventArgs args) {
            if (_dataUnavailable) return;
            await GuardedAsync("OnGameStartingAsync", async () => {
                GsPostHog.Capture("game_session_started", new Dictionary<string, object> {
                    { "platform_id", args.Game?.LibraryId ?? "unknown" }
                });
                await _scrobblingService.OnGameStartAsync(args.Game);
            });
        }

        /// <summary>
        /// Fired when the game process exits. OnGameStoppedEventArgs carries no Game of its own;
        /// it nests the starting args that opened the session.
        /// </summary>
        public override async Task OnGameStoppedAsync(OnGameStoppedEventArgs args) {
            if (_dataUnavailable) return;
            await GuardedAsync("OnGameStoppedAsync", async () => {
                GsPostHog.Capture("game_session_ended", new Dictionary<string, object> {
                    { "elapsed_seconds", args.StoppedArgs?.SessionLength ?? 0 },
                    { "platform_id", args.StartingArgs?.Game?.LibraryId ?? "unknown" }
                });
                await _scrobblingService.OnGameStoppedAsync(args.StartingArgs?.Game);
            });
        }

        public override async Task OnApplicationShutdownAsync(OnApplicationShutdownArgs args) {
            if (_dataUnavailable) return;
            await GuardedAsync("OnApplicationShutdownAsync", async () => {
                GsPostHog.Capture("plugin_stopped");
                await _scrobblingService.OnApplicationStoppedAsync();
            });
        }

        /// <summary>
        /// Runs after a library plugin finishes importing, which is when new games appear.
        /// </summary>
        public override async Task OnLibraryUpdateFinishedAsync(OnLibraryUpdateFinishedArgs args) {
            if (_dataUnavailable) return;
            await GuardedAsync("OnLibraryUpdateFinishedAsync", async () => {
                GsPostHog.Capture("library_synced", new Dictionary<string, object> {
                    { "game_count", PlayniteApi.Library.Games?.Count ?? 0 }
                });
                var librarySyncResult = await SyncLibraryWithDiffAsync();
                if (librarySyncResult == SyncLibraryResult.Cooldown) {
                    _logger.Info("Library updated sync skipped: sync cooldown is still active.");
                }
            });
        }

        public override async Task OnGameCollectionChange(DataCollectionChangeArgs<Game> args) {
            if (_dataUnavailable) return;
            await GuardedAsync("OnGameCollectionChange", async () => {
                // Trigger library sync when games are added (e.g. after a library import)
                if (args.AddedItems.Count > 0) {
                    _ = SyncLibraryWithDiffAsync().LogFaults("Library sync after game add failed");
                }
                await Task.CompletedTask;
            });
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

        /// <summary>
        /// Supplies this install's Game Scrobbler data for a single game so themes can
        /// bind to it (see <see cref="GsGameDataPresenter"/> for the binding shape).
        /// This is the Playnite 11 replacement for the Playnite 10 custom-element and
        /// PluginSettings-binding surfaces, which no longer exist in the v11 SDK.
        ///
        /// Returns null when opted out, or for games we would never have synced, so
        /// themes get no presenter at all rather than one that is permanently empty.
        /// </summary>
        public override PluginGameDataPresenter? GetPluginGameDataPresenter(GetPluginGameDataPresenterArgs args) {
            if (GsDataManager.IsOptedOut) return null;

            var game = args.Game;
            if (game == null || string.IsNullOrEmpty(game.Id)) return null;
            if (string.IsNullOrEmpty(game.LibraryId)
                || !GsAllowedPlugins.AllowedPluginIds.Contains(game.LibraryId)) {
                return null;
            }

            return new GsGameDataPresenter(_apiClient, game.Id);
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
                items.Add(new MenuItemDescriptor("gs-sync", Loc.menu_sync_library(), "Game Scrobbler"));
                items.Add(new MenuItemDescriptor("gs-settings", Loc.menu_open_settings(), "Game Scrobbler"));
            }
            return items;
        }

        public override ICollection<MenuItemImpl>? GetAppMenuItems(GetAppMenuItemsArgs args) {
            if (args.ItemId == "gs-sync") {
                return [new MenuItemImpl(
                    Loc.menu_sync_library(),
                    async (_) => {
                        try {
                            var result = await SyncLibraryWithDiffAsync();
                            string message;
                            if (result == SyncLibraryResult.Success) {
                                message = Loc.sync_completed();
                            }
                            else if (result == SyncLibraryResult.Skipped) {
                                message = Loc.sync_up_to_date();
                            }
                            else if (result == SyncLibraryResult.Cooldown) {
                                var expiry = GsDataManager.Data.SyncCooldownExpiresAt
                                    ?? GsDataManager.Data.LibraryDiffSyncCooldownExpiresAt;
                                if (expiry.HasValue) {
                                    var timeLeft = GsTime.FormatRemaining(expiry.Value - DateTime.UtcNow);
                                    message = Loc.sync_cooldown_format(timeLeft);
                                }
                                else {
                                    message = Loc.sync_cooldown_generic();
                                }
                            }
                            else {
                                message = Loc.sync_failed();
                            }

                            await PlayniteApi.Dialogs.ShowMessageAsync(message, "Game Scrobbler");                        }
                        catch (Exception ex) {
                            _logger.Error(ex, "Error in Sync Library Now menu action");
                            GsSentry.CaptureException(ex, "Error in Sync Library Now menu action");
                            await PlayniteApi.Dialogs.ShowMessageAsync(
                                Loc.sync_error(), "Game Scrobbler");
                        }
                    })];
            }

            if (args.ItemId == "gs-settings") {
                return [new MenuItemImpl(
                    Loc.menu_open_settings(),
                    async (_) => await PlayniteApi.MainView.OpenPluginSettingsAsync(GsPluginPlugin.Id))];
            }

            return null;
        }

        /// <summary>
        /// Registers an install token on demand from a user-initiated action (e.g. the
        /// "Delete My Data" button). Startup token registration can fail on a transient
        /// network error and does not retry until the next Playnite restart, which would
        /// otherwise leave a token-authenticated action failing for the whole session.
        /// Reuses the same gated registration + rotation-recovery logic as startup.
        /// </summary>
        internal Task EnsureInstallTokenReadyAsync() => EnsureInstallTokenAsync();

        /// <summary>
        /// Ensures the install has a valid server-issued auth token stored in GsData.
        /// Starts in parallel with other startup work and is awaited before the first v4 sync.
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
                return;
            }

            // Serialize the entire registration + conflict-recovery sequence. Startup and
            // on-demand callers can otherwise enter concurrently with an empty token and both
            // register, producing a 409 and a RotateInstallId() that races the in-flight
            // registration. The gate is held across registration, token storage, rotation, and retry.
            await _installTokenGate.WaitAsync();
            try {
                // Re-check under the gate: a concurrent caller may have stored a token while we
                // waited, making our registration redundant.
                if (!string.IsNullOrEmpty(GsDataManager.Data.InstallToken)) {
                    return;
                }
                await EnsureInstallTokenCoreAsync();
            }
            finally {
                _installTokenGate.Release();
            }
        }

        /// <summary>
        /// Performs the actual token registration and 409 rotation-recovery. Always invoked
        /// under <see cref="_installTokenGate"/> by <see cref="EnsureInstallTokenAsync"/>, so
        /// concurrent startup and on-demand callers run one at a time.
        /// </summary>
        private async Task EnsureInstallTokenCoreAsync() {
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
                if (GsSyncHashIndex.HasLibraryBaseline) {
                    return await _scrobblingService.SyncLibraryDiffAsync(PlayniteApi.Library.Games);
                }
                return await _scrobblingService.SyncLibraryFullAsync(PlayniteApi.Library.Games);
            }
            finally {
                Interlocked.Exchange(ref _librarySyncInFlight, 0);
            }
        }

        public override async ValueTask DisposeAsync() {
            // The flush timer is created several awaits into startup, so a shutdown racing
            // startup could otherwise dispose the field before it was assigned, or miss a
            // timer assigned right after. Take the same lock the creation path takes.
            bool alreadyDisposed;
            lock (_timerLock) {
                alreadyDisposed = _disposed;
                _disposed = true;
            }

            if (!alreadyDisposed) {
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

                lock (_timerLock) {
                    try {
                        _pendingFlushTimer?.Dispose();
                        _pendingFlushTimer = null;
                    }
                    catch (Exception ex) {
                        _logger.Error(ex, "Error disposing flush timer");
                    }
                }

                try {
                    GsPostHog.Shutdown();
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error closing PostHog");
                }

                try {
                    GsSentry.Shutdown();
                    // Only here. Consent changes call Shutdown() too, and detaching the global
                    // handlers there would let a privacy preference disable the plugin's own
                    // fault observation; process teardown is the one moment it is right to.
                    GsSentry.ReleaseGlobalExceptionHandlers();
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error closing Sentry");
                }

                try {
                    _installTokenGate.Dispose();
                }
                catch (Exception ex) {
                    _logger.Error(ex, "Error disposing install-token gate");
                }

                _apiClient = null;
                _linkingService = null;
                _uriHandler = null;
                _scrobblingService = null;
            }
        }
    }

}
