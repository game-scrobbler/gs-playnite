# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Game Scrobbler is a Playnite plugin that tracks game sessions and provides statistics visualization. It's the official Playnite plugin for GameScrobbler. It's built as a .NET 10 SDK-style C# project targeting `net10.0-windows`, using the Playnite 11 SDK.

## Build & Development Commands

- **Build project**: `dotnet build GsPlugin.csproj -p:Configuration=Release`
- **Format code**: `dotnet format GsPlugin.sln`
- **Verify formatting**: `dotnet format GsPlugin.sln --verify-no-changes`
- **Run all tests**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --verbosity normal`
- **Run a single test**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --filter "FullyQualifiedName~ClassName.MethodName"`
- **Setup git hooks**: `powershell -ExecutionPolicy Bypass -File scripts/setup-hooks.ps1`
- **Manual formatting**: `powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1`
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release\net10.0-windows" "PackingOutput"` (produces `.pext2`)

## Architecture Overview

### Project Structure
```
GsPlugin.cs              — Entry point (namespace: GsPlugin); extends Plugin (P11)
│
├── Api/                 — namespace: GsPlugin.Api
│   ├── ApiResult.cs         — Generic API result wrapper
│   ├── Dtos.cs              — All API request/response DTOs (namespace-level, not nested)
│   ├── GsApiClient.cs       — HTTP API client
│   ├── IGsApiClient.cs      — API client interface
│   └── GsCircuitBreaker.cs  — Circuit breaker with exponential backoff
│
├── Services/            — namespace: GsPlugin.Services
│   ├── GsScrobblingService.cs       — Game session tracking and library/achievement sync
│   ├── IAchievementProvider.cs      — Achievement provider interface
│   ├── GsAchievementAggregator.cs   — Multi-provider achievement aggregation
│   ├── GsSuccessStoryHelper.cs      — SuccessStory addon integration (direct JSON file reads)
│   ├── GsPlayniteAchievementsHelper.cs — Playnite Achievements addon integration (direct SQLite reads)
│   ├── GsAccountLinkingService.cs   — Account linking operations
│   ├── GsNotificationService.cs      — Server notification fetch and display
│   ├── GsUriHandler.cs              — Deep link processing (Func<PlayniteUriEventArgs, Task>)
│   └── GsUpdateChecker.cs           — Plugin update checking
│
├── Models/              — namespace: GsPlugin.Models
│   ├── GsData.cs            — Persistent data (GsDataManager, GsTime, PendingScrobble)
│   ├── GsSnapshot.cs        — Diff-based sync state (GsSnapshotManager)
│   └── GsPluginSettings.cs  — Settings data model and view model
│
├── Infrastructure/      — namespace: GsPlugin.Infrastructure
│   ├── GsLocalization.cs    — XAML resource string lookup helper (still used; Fluent migration pending)
│   ├── GsLogger.cs          — Logging wrapper
│   └── GsSentry.cs          — Sentry error tracking
│
├── Localization/        — Locale resource files
│   ├── Localization.cs      — Fluent wrapper (Loc / LocalizedString)
│   └── en_US.xaml (+ ru_RU, pt_BR, de_DE, fr_FR, zh_CN, hi_IN) — still in use until Fluent migration
│
├── View/                — namespace: GsPlugin.View
│   ├── GsPluginSettingsView.xaml/.cs — Settings UI
│   ├── GsPluginSettingsHandler.cs    — PluginSettingsHandler subclass (P11)
│   ├── GsDashboardView.cs            — AppViewItem subclass wrapping MySidebarView (P11)
│   └── MySidebarView.xaml/.cs        — Sidebar WebView2 content
│
├── extension.toml       — Extension manifest (replaces extension.yaml)
├── scripts/             — PowerShell build/dev scripts
├── hooks/               — Git hook scripts
├── GsPlugin.Tests/      — xUnit test project (net10.0-windows)
└── Directory.Build.props / global.json — Shared SDK-style build settings
```

### Service Dependency Graph
```
GsPlugin (entry point, IDisposable)
├── GsScrobblingService → GsApiClient → GsCircuitBreaker
│                       → GsAchievementAggregator → GsSuccessStoryHelper (JSON file reads)
│                       │                         → GsPlayniteAchievementsHelper (SQLite reads)
│                       → GsSnapshotManager (diff-based sync state)
├── GsAccountLinkingService → GsApiClient
├── GsNotificationService → GsApiClient (fire-and-forget background)
├── GsUriHandler → GsAccountLinkingService
├── GsUpdateChecker
└── All services use GsDataManager for persistent state
```

### Install Token Authentication
- `GsPlugin.OnApplicationStarted()` kicks off `EnsureInstallTokenAsync()` as a best-effort, fire-and-forget startup task so first-run registration does not block plugin startup.
- `EnsureInstallTokenAsync()` retries up to 3 times with exponential backoff (2 s, 4 s) for transient network errors; a non-null result (success or known error code) breaks the loop immediately.
- Each install registers with the server via `/api/playnite/v2/register`, receiving a per-install token stored in `GsData.InstallToken`.
- Authenticated write calls use the shared `PostJsonAsync()` path, which adds the `x-playnite-token` header when `InstallToken` is present. `RequestDeleteMyData()` and `GetDashboardToken()` also attach this header explicitly.
- `InstallIdForBody` returns `null` when a token is present, and request DTOs use `JsonIgnore(WhenWritingNull)` on `user_id`, so the server resolves identity from the header instead of the body.
- Pending scrobble DTOs still keep whatever `user_id` they were queued with, so old queued items can replay without depending on the current `InstallIdForBody` value.
- If `/v2/register` returns `PLAYNITE_TOKEN_ALREADY_REGISTERED`, the plugin treats the local token as lost, rotates to a fresh `InstallID`, clears identity-bound state, resets snapshots, and immediately re-registers under the new identity.
- `RotateInstallId()` clears token, linked user, sessions, pending scrobbles, sync hashes, cooldowns, and integration-account hashes before calling `GsSnapshotManager.Reset()`.
- `SetInstallTokenIfActive()` atomically checks opt-out status before persisting the token, preventing races with `PerformOptOut()`.
- Deletion requests require a valid `InstallToken`; the server resolves install identity from the `x-playnite-token` header. No `user_id` is sent in the body. `DeleteDataRes.rateLimited` is set when the server returns HTTP 429.
- `GetDashboardToken()` sends a POST request with a dashboard context object (`plugin_version`, flags, preferences) in the body. The server stores this context alongside the token and returns it tamper-proof when the frontend resolves the token — eliminating the need for client-side URL query params. If the token fetch fails for a registered install, the dashboard fails closed instead of falling back to `user_id`.
- `IdentityGeneration` is incremented on fresh-install `InstallID` creation and on `RotateInstallId()`. `GsSnapshotManager` stamps this generation into `gs_snapshot.json` and discards snapshots whose generation no longer matches current data.
- `ResetInstallToken()` exists on `IGsApiClient`/`GsApiClient`, but the current lost-token recovery path uses local `InstallID` rotation plus re-registration rather than token reset.

### Server Notifications
- `GsNotificationService` fetches notifications from `GET /api/playnite/v2/notifications` at startup and displays them in Playnite's native notification tray.
- Runs as fire-and-forget via `FetchNotificationsAfterTokenAsync()` which awaits `EnsureInstallTokenAsync()` first, ensuring the install token is available before fetching. Never blocks the startup critical path.
- Auth: `x-playnite-token` header only — no `user_id`/`install_id` fallback.
- `GetNotifications()` in `GsApiClient` intentionally bypasses the shared circuit breaker so notification failures cannot affect core sync/scrobble paths.
- UI thread safety: in P11, `Notifications.Add()` no longer requires explicit UI-thread marshalling — the old `Application.Current.Dispatcher.Invoke()` wrapper has been removed.
- `GsDataManager.GetShownNotificationIds()` returns a lock-protected snapshot; `RecordShownNotifications()` atomically appends and persists under `_lock`, preventing cross-thread races with concurrent startup writes.
- `ShownNotificationIds` is capped at 100 entries and cleared on `RotateInstallId()` alongside other identity-bound state.
- Action URL handling: `gs://settings` opens plugin settings via `OpenPluginSettings(Id)`, `gs://addons` opens the add-ons dialog, `https://` URLs are opened in the browser but only for trusted hosts (`gamescrobbler.com`, `playnite.link`). Plain `http://` and untrusted hosts are rejected.
- Two user-facing settings (`ShowUpdateNotifications`, `ShowImportantNotifications`) control whether update and server notifications appear. Both default to `true` and are synced to `GsData` via `GsPluginSettingsViewModel.EndEdit()` and `LoadExistingSettings()`.

### Pending Scrobble Flush
- Flush uses a peek-then-remove strategy: `PeekPendingScrobbles()` returns a snapshot without clearing, each item stays on disk until its send is confirmed, then `RemovePendingScrobble()` removes it atomically. A mid-flush crash loses nothing.
- `_flushInFlight` Interlocked guard prevents concurrent flush invocations (circuit recovery + periodic timer + startup can overlap).
- Failed items stay in the queue with an incremented `FlushAttempts` counter (persisted via `Save()`) and are dropped after `MaxFlushAttempts` (5).
- A periodic 5-minute timer (`_pendingFlushTimer`) retries queued scrobbles independently of circuit breaker recovery. Disposed in `Dispose()`.

### Startup Flow
- Plugin refresh (`RefreshAllowedPluginsAsync`) and update check (`CheckForUpdateAsync`) run in parallel via `Task.WhenAll` — they are independent network calls.
- Pending scrobble flush is fire-and-forget so library sync starts immediately; the periodic timer catches remaining items.
- First-run detection: when `LastSyncAt` is null and `InstallToken` is empty, progress notifications guide the user through initial setup.
- `startup_completed` PostHog event captures elapsed time and sync result for startup performance tracking.
- Achievement sync (`SyncAchievementsWithDiffAsync`) has been removed from the startup flow; it will be re-added once SuccessStory and Playnite Achievements release P11-compatible versions.

### Sidebar Dashboard
- `MySidebarView` constructor takes only `IGsApiClient` — plugin version and flags are sent server-side via the dashboard token POST body.
- Dashboard URL passes only `theme` as a query param (cosmetic, needed for instant rendering); all other context is tamper-proof via the token.
- Auto-refreshes the dashboard token when the sidebar becomes visible after 8+ minutes (tokens have a 10-minute TTL).
- Handles `gs:refresh-token` postMessage from the frontend for manual retry when the session expires.

### Achievement Provider Architecture
Achievement data reading infrastructure is present but **achievement sync to the server is disabled** pending SuccessStory and Playnite Achievements releasing P11-compatible versions:
- `IAchievementProvider` — common interface (`GetCounts`, `GetAchievements`, `IsInstalled`)
- `GsSuccessStoryHelper` — reads SuccessStory's per-game JSON files from `{ExtensionsDataPath}/{pluginGuid}/SuccessStory/{gameId}.json` (priority 1)
- `GsPlayniteAchievementsHelper` — reads Playnite Achievements' SQLite database at `{ExtensionsDataPath}/{pluginGuid}/achievement_cache.db` via `Microsoft.Data.Sqlite` in read-only mode (priority 2)
- `GsAchievementAggregator` — iterates providers in order; first with data wins. Skips `(0, 0)` results to allow fallback.
- `IsInstalled` checks data directory/file existence on disk, not plugin presence in `_api.Addons.Plugins`.
- `GetVersion()` returns `null` permanently — `PlayniteApi.Addons.Plugins` is gone in P11.
- `SyncAchievementsFullAsync` and `SyncAchievementsDiffAsync` are removed from `GsScrobblingService`; the API client methods (`SyncAchievementsFull`, `SyncAchievementsDiff`) are retained for when P11-compatible addon versions ship.
- `Microsoft.Data.Sqlite` has been removed from the project — SQLite was only needed for `GsPlayniteAchievementsHelper` which is removed pending P11 addon compatibility.

### Settings UI & Localization
- All user-facing strings use Project Fluent (`.ftl`) localization via `Loc.*()` typed methods generated into `Localization/Localization.g.cs`.
- `Loc.Api = args.Api;` is set in `InitializeAsync` before any `Loc.*()` calls.
- `Loc.GetString()` null-guards with `?? stringId` fallback so tests (which have no live `Loc.Api`) see the FTL key string rather than NPE.
- Time-formatting methods in `GsData.cs` (`FormatElapsed`, `FormatRemaining`) and status strings in `GsPluginSettingsViewModel.cs` use **inline C# string interpolation** — no `Loc.*()` calls — so that `GsTimeTests` and `GsPluginSettingsViewModelTests` continue to assert exact English strings without a live `Loc.Api`.
- `Infrastructure/GsLocalization.cs` is deleted; `GsPlugin.Infrastructure` using directives remain in files that still use `GsLogger`, `GsSentry`, `GsPostHog`.
- To add a new localization key: add it to `Localization/en_US.ftl`, run `Toolbox.exe ftlgen "Localization/" "Localization/"` to regenerate `Localization.g.cs`, then call the new typed `Loc.*()` method.
- `GsDataManager.DiagnosticsStateChanged` event fires (outside the lock) when install-token or pending-scrobble state changes; the settings UI subscribes for live status updates.
- `GsPluginSettingsViewModel` exposes diagnostic properties: `IsInstallTokenActive`, `PendingScrobbleCount`, `HasPendingScrobbles`.

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, `net10.0-windows` — must match main project TFM because it references it)
- 301 tests pass against the P11 SDK.
- Test classes: `AchievementProviderTests`, `ApiResultTests`, `GsApiClientValidationTests`, `GsCircuitBreakerTests`, `GsDataManagerTests`, `GsDataTests`, `GsFlushAndPairingTests`, `GsMetadataHashTests`, `GsPluginSettingsViewModelTests`, `GsScrobblingServiceHashTests`, `GsSnapshotTests`, `GsTimeTests`, `LinkingResultTests`, `PlayniteAchievementsSqliteTests`, `SuccessStoryFileReaderTests`, `ValidateTokenTests`
- `GsDataManagerTests` and `GsDataTests` include coverage for install-token persistence, `IdentityGeneration`, `RotateInstallId()`, `SetInstallTokenIfActive()`, `InstallIdForBody`, opt-out token clearing, and `RecordShownNotifications()`/`GetShownNotificationIds()` thread-safe notification state.
- `InternalsVisibleTo` is declared via `<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">` in an `<ItemGroup>` in `GsPlugin.csproj` — **not** via `<PropertyGroup>` (which does not work for SDK-style projects).

## Build Environment

- Targets `net10.0-windows` — SDK-style csproj; use `dotnet build GsPlugin.csproj` (not `MSBuild.exe`)
- Both main project and test project use SDK-style csproj; `dotnet test` works directly without a prior MSBuild step
- XAML code-gen (`GenerateTemporaryTargetAssembly`) in the SDK-style WPF project compiles all `<Compile>` items in the project; the test project files are excluded via `<Compile Remove="GsPlugin.Tests\**\*.cs" />` in `Directory.Build.props` or the main csproj to prevent double-compilation
- `TreatWarningsAsErrors=true` is set for Release builds; `CA1822` (mark member static) is listed in `WarningsNotAsErrors` to avoid false positives on plugin override methods
- `ServicePointManager` is not available on .NET 10 (SYSLIB0014) — do not use it; TLS 1.2+ is the default
- API endpoints: All builds (Debug and Release) use the production URL `api.gamescrobbler.com`
- Extension manifest: `extension.toml` (TOML) replaces the old `extension.yaml`; packed output is `.pext2`
- NuGet feed: `https://nuget.playnite.link/v3/index.json` is required for `Playnite.SDK 11.0.0-alpha*` packages (configured in `nuget.config`)
- When upgrading NuGet packages, prefer packages with a `net10.0` or `net8.0` target; avoid packages that only ship `netstandard2.0` without native AOT or trimming support if the package involves native interop

## Important Notes

### Thread-Safe Data Mutations
- Use `GsDataManager.MutateAndSave(d => { ... })` instead of directly modifying `GsDataManager.Data` fields followed by `GsDataManager.Save()`. The `MutateAndSave` method acquires the lock, executes the action, and persists atomically — preventing concurrent threads from interleaving mutations.
- Direct field access via `GsDataManager.Data` is still available for reads, but all write-then-save sequences should use `MutateAndSave`.

### API DTOs
- All API request/response DTOs live in `Api/Dtos.cs` at namespace level (`GsPlugin.Api`), not nested inside `GsApiClient`. Reference them directly (e.g., `new ScrobbleStartReq { ... }`) — no `GsApiClient.` prefix needed.

### Code Formatting
All code must be formatted with `dotnet format` before commits. The pre-commit hook checks with `--verify-no-changes` and fails if unformatted.

### Git Hooks
Hook scripts in `hooks/` are installed to `.git/hooks/` via `scripts/setup-hooks.ps1`:
- **pre-commit**: Verifies code formatting on staged `.cs` files
- **commit-msg**: Validates conventional commit message format (`feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert`)

**Never use `--no-verify` when pushing or committing.** Git hooks enforce formatting and commit message standards; bypassing them is not allowed.

### Playnite Plugin Hosting Constraints (P11)
- Playnite 11 loads plugins under .NET 10 — the old `AppDomain.CurrentDomain.AssemblyResolve` handler and `app.config` binding redirects are **gone**; do not re-add them.
- The plugin base class is `Plugin` (not `GenericPlugin`). Entry point is `InitializeAsync(InitializeArgs args)`; dispose is `async ValueTask DisposeAsync()`.
- `IPlayniteAPI` is obtained from `args.Api` inside `InitializeAsync` — there is no constructor injection in P11.
- After building, the extension folder in `%APPDATA%\Playnite\Extensions\<plugin-guid>\` must contain the updated DLLs. Stale DLLs from a previous version will cause `FileNotFoundException` at runtime.
- `GsSentry` methods (`CaptureException`, `CaptureMessage`, `AddBreadcrumb`) use `GsDataManager.DataOrNull` instead of `GsDataManager.Data` to avoid a circular crash when called during `GsDataManager.Initialize()` before `_data` is assigned.
- All `SentrySdk` calls are wrapped in try/catch so the plugin continues working if the Sentry SDK is unavailable (e.g., expired account). `GsApiClient` similarly falls back to a plain `HttpClient` if `SentryHttpMessageHandler` throws.
- `MaxBreadcrumbs` is capped at 50 (default 100) to reduce per-session memory overhead.
- `IDialogs.ShowMessage` (sync) is removed in P11 — use `await IDialogs.ShowMessageAsync(...)` for informational dialogs; for Yes/No prompts use `System.Windows.MessageBox.Show()` and qualify the result type as `System.Windows.MessageBoxResult` to avoid ambiguity with `Playnite.MessageBoxResult`.
- `GetAppMenuItems` matches menu items via `args.ItemId` string comparison (not `args.Descriptors` iteration); menu items are created with `MenuItemImpl(name, asyncAction)`.
- `GetAppViewItem` matches via `args.ViewId` (not `args.ItemId`).
- App view icons use `UIIcon.FromBitmapFile(path)` or `UIIcon.FromFontIcon(code, Playnite.Fonts.NerdFont)`.
- `CollectionItemUpdateData<T>.NewData` is the property to read the updated item from `OnLibraryUpdated`-style events.

### Playnite SDK Type Gotchas (P11)
- `Game.Playtime` and `Game.PlayCount` are `ulong` — cast explicitly to `long`/`int` when assigning to DTO fields (no implicit conversion).
- `Game.CompletionStatusId` defaults to `Guid.Empty` (not `null`) when unset — guard with `g.CompletionStatusId != Guid.Empty` before calling `.ToString()`.
- `Game.CompletionStatus` is a user-defined named object (not an enum) with a `.Name` string property; access null-safely (`g.CompletionStatus?.Name`).
- `Game.Id` is `string` in P11 (was `Guid` in P10). Use `Guid.TryParse(g.Id, out var guid)` before passing to any method that expects a `Guid`.
- `Game.ReleaseDate` is `Playnite.PartialDate?` in P11 — a **class** (reference type), not a struct. Check `!= null`, not `.HasValue`. Its properties are `int Year`, `int? Month`, `int? Day`.
- `Game.LibraryId` (was `Game.PluginId` in P10), `Game.LibraryGameId` (was `Game.GameId`), `Game.PlayTime` (was `Game.Playtime`), `Game.LastPlayedDate` (was `Game.LastActivity`), `Game.AddedDate` (was `Game.Added`), `Game.ModifiedDate` (was `Game.Modified`).
- New `.cs` files are auto-included in SDK-style projects — no manual `<Compile Include=.../>` entries needed. Place files in the appropriate namespace folder (`Api/`, `Services/`, `Models/`, `Infrastructure/`, `View/`).
- New `.cs` files written with LF line endings will fail `dotnet format --verify-no-changes`; run `dotnet format` to auto-correct to CRLF.

### Sentry Release Management
- Runtime: Plugin reports version as `GsPlugin@X.Y.Z` from `extension.toml`
- CI/CD: GitHub Actions creates Sentry releases, uploads portable PDB files (`--type=portablepdb`), and associates commits
- release-please keeps versions synchronized across `extension.toml` and manifests
- Only runs when release-please creates a GitHub release (conditional on `${{ steps.release.outputs.release_created }}`)
