# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Game Scrobbler is a Playnite plugin that tracks game sessions and provides statistics visualization. It's the official Playnite plugin for GameScrobbler. It's built as a .NET Framework 4.6.2 C# project using the Playnite SDK.

## Build & Development Commands

- **Build solution**: `MSBuild.exe GsPlugin.sln -p:Configuration=Release -restore`
- **Restore NuGet packages**: `nuget restore GsPlugin.sln`
- **Format code**: `dotnet format GsPlugin.sln`
- **Verify formatting**: `dotnet format GsPlugin.sln --verify-no-changes`
- **Run all tests**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --no-build --verbosity normal` (build with MSBuild first)
- **Run a single test**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ClassName.MethodName"`
- **Setup git hooks**: `powershell -ExecutionPolicy Bypass -File scripts/setup-hooks.ps1`
- **Manual formatting**: `powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1`
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"`

## Architecture Overview

### Project Structure
```
GsPlugin.cs              — Entry point (namespace: GsPlugin)
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
│   ├── GsUriHandler.cs              — Deep link processing
│   └── GsUpdateChecker.cs           — Plugin update checking
│
├── Models/              — namespace: GsPlugin.Models
│   ├── GsData.cs            — Persistent data (GsDataManager, GsTime, PendingScrobble)
│   ├── GsSnapshot.cs        — Diff-based sync state (GsSnapshotManager)
│   └── GsPluginSettings.cs  — Settings data model and view model
│
├── Infrastructure/      — namespace: GsPlugin.Infrastructure
│   ├── GsLocalization.cs    — XAML resource string lookup helper
│   ├── GsLogger.cs          — Logging wrapper
│   └── GsSentry.cs          — Sentry error tracking
│
├── View/                — namespace: GsPlugin.View
│   ├── GsPluginSettingsView.xaml/.cs — Settings UI
│   └── MySidebarView.xaml/.cs        — Sidebar with WebView2
│
├── scripts/             — PowerShell build/dev scripts
├── hooks/               — Git hook scripts
├── GsPlugin.Tests/      — xUnit test project (net462)
└── Properties/          — AssemblyInfo.cs
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

### Server Notifications
- `GsNotificationService` fetches notifications from `GET /api/playnite/v2/notifications` at startup and displays them in Playnite's native notification tray.
- Runs as fire-and-forget via `FetchNotificationsAfterTokenAsync()` which awaits `EnsureInstallTokenAsync()` first, ensuring the install token is available before fetching. Never blocks the startup critical path.
- Auth: `x-playnite-token` header only — no `user_id`/`install_id` fallback.
- `GetNotifications()` in `GsApiClient` intentionally bypasses the shared circuit breaker so notification failures cannot affect core sync/scrobble paths.
- UI thread safety: notifications are collected on the background thread, then marshaled onto `Application.Current.Dispatcher.Invoke()` for `Notifications.Add()` calls. The dispatcher invoke is wrapped in try/catch so a dispatcher fault does not surface as a false Sentry error.
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

### Allowed Library Sources
- `GsAllowedPlugins.IsAllowed(Game)` is the single predicate for deciding whether a Playnite game can sync, scrobble, or contribute achievement counts.
- Official Playnite library plugin GUIDs are accepted directly after `RefreshAllowedPluginsAsync` loads the backend allowlist.
- OSS/forked library plugins can still sync when `Game.Source.Name` contains a recognized supported source fragment such as `GOG OSS`, `Legendary`, `Epic Games`, or `Amazon Games`.
- `Guid.Empty` plugin IDs are always rejected. They represent manual/custom games and should not be sent.
- Keep DTOs source-aware: send the raw `plugin_id` plus `source_name = g.Source?.Name`; the backend canonicalizes recognized fork GUIDs to official plugin IDs before writing rows.

### Sidebar Dashboard
- `MySidebarView` constructor takes only `IGsApiClient` — plugin version and flags are sent server-side via the dashboard token POST body.
- Dashboard URL passes only `theme` as a query param (cosmetic, needed for instant rendering); all other context is tamper-proof via the token.
- Auto-refreshes the dashboard token when the sidebar becomes visible after 8+ minutes (tokens have a 10-minute TTL).
- Handles `gs:refresh-token` postMessage from the frontend for manual retry when the session expires.

### Achievement Provider Architecture
Achievement data comes from two optional addons via an aggregator pattern:
- `IAchievementProvider` — common interface (`GetCounts`, `GetAchievements`, `IsInstalled`)
- `GsSuccessStoryHelper` — reads SuccessStory's per-game JSON files from `{ExtensionsDataPath}/{pluginGuid}/SuccessStory/{gameId}.json` (priority 1)
- `GsPlayniteAchievementsHelper` — reads Playnite Achievements' SQLite database at `{ExtensionsDataPath}/{pluginGuid}/achievement_cache.db` via `System.Data.SQLite` in read-only mode (priority 2)
- `GsAchievementAggregator` — iterates providers in order; first with data wins. Skips `(0, 0)` results to allow fallback.
- `PluginVersionHelper` — reads version from `extension.yaml` next to the plugin DLL; shared by both providers for `GetVersion()`.
- `IsInstalled` checks data directory/file existence on disk, not plugin presence in `_api.Addons.Plugins`.
- `System.Data.SQLite.Core` NuGet package ships native `SQLite.Interop.dll` (x86/x64) via build targets.

### Settings UI & Localization
- All user-facing strings are localized via XAML resource dictionaries in `Localization/` and accessed from C# via `GsLocalization.Get()`/`Format()` in `Infrastructure/GsLocalization.cs`.
- Playnite auto-discovers locale files by naming convention (`Localization/{locale}.xaml`). The `en_US.xaml` is the fallback; locale-specific files override it.
- Supported locales: `en_US` (English, default), `ru_RU` (Russian), `pt_BR` (Portuguese), `de_DE` (German), `fr_FR` (French), `zh_CN` (Chinese Simplified), `hi_IN` (Hindi).
- All locale files must have the same set of keys (currently 117). When adding a new key, add it to **all 7 files**.
- `GsLocalization.Get(key, fallback)` looks up from `Application.Current.Resources`; returns the fallback when no WPF app is running (e.g., in tests). `GsLocalization.Format(key, fallback, args)` wraps `string.Format()` on the resolved template.
- For format strings with English pluralization (e.g., elapsed time), the code-behind fallback uses inline plural logic so tests see `"5 minutes ago"` while the XAML template is used at runtime for non-English locales (e.g., `"{0} мин. назад"`).
- Settings view uses localized strings from `Localization/en_US.xaml` resource dictionary, organized into card-based sections.
- `GsDataManager.DiagnosticsStateChanged` event fires (outside the lock) when install-token or pending-scrobble state changes; the settings UI subscribes for live status updates.
- `GsPluginSettingsViewModel` exposes diagnostic properties: `IsInstallTokenActive`, `PendingScrobbleCount`, `HasPendingScrobbles`.

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, net462)
- Test classes: `AchievementProviderTests`, `ApiResultTests`, `GsAllowedPluginsTests`, `GsApiClientValidationTests`, `GsCircuitBreakerTests`, `GsDataManagerTests`, `GsDataTests`, `GsFlushAndPairingTests`, `GsMetadataHashTests`, `GsPluginSettingsViewModelTests`, `GsScrobblingServiceHashTests`, `GsSnapshotTests`, `GsTimeTests`, `LinkingResultTests`, `PlayniteAchievementsSqliteTests`, `SuccessStoryFileReaderTests`, `ValidateTokenTests`
- `GsDataManagerTests` and `GsDataTests` include coverage for install-token persistence, `IdentityGeneration`, `RotateInstallId()`, `SetInstallTokenIfActive()`, `InstallIdForBody`, opt-out token clearing, and `RecordShownNotifications()`/`GetShownNotificationIds()` thread-safe notification state.

## Build Environment

- Targets .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild, not `dotnet build`)
- XAML code-gen (WPF `PresentationBuildTasks`) requires the full `MSBuild.exe` from VS Build Tools or a full VS install; `dotnet msbuild` does **not** generate `.g.cs` files for old-style WPF projects, so View code-behind will fail to compile without it
- Test project uses SDK-style .csproj and can be built/run with `dotnet test`
- API endpoints: All builds (Debug and Release) use the production URL `api.gamescrobbler.com`
- When upgrading NuGet packages, only upgrade to versions that explicitly ship a `net462` (or `net461`/`net45`) lib folder. Do not rely on netstandard2.0 fallbacks for core runtime packages.

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

### Playnite Plugin Hosting Constraints
- Playnite loads plugins in its own AppDomain and **ignores plugin-level `app.config` binding redirects**. Assembly version mismatches must be resolved at runtime via the `AppDomain.CurrentDomain.AssemblyResolve` handler in `GsPlugin`'s static constructor.
- When upgrading a NuGet package version, the plugin's dependencies (e.g., Sentry) may still reference the old assembly version. The `AssemblyResolve` handler in `GsPlugin.cs` handles this by loading whatever DLL version exists in the plugin's output directory.
- After building, the extension folder in `%APPDATA%\Playnite\Extensions\<plugin-guid>\` must contain the updated DLLs. Stale DLLs from a previous version will cause `FileNotFoundException` at runtime.
- `GsSentry` methods (`CaptureException`, `CaptureMessage`, `AddBreadcrumb`) use `GsDataManager.DataOrNull` instead of `GsDataManager.Data` to avoid a circular crash when called during `GsDataManager.Initialize()` before `_data` is assigned.
- All `SentrySdk` calls are wrapped in try/catch so the plugin continues working if the Sentry SDK is unavailable (e.g., expired account). `GsApiClient` similarly falls back to a plain `HttpClient` if `SentryHttpMessageHandler` throws.
- `MaxBreadcrumbs` is capped at 50 (default 100) to reduce per-session memory overhead.

### Playnite SDK Type Gotchas
- `Game.Playtime` and `Game.PlayCount` are `ulong` — cast explicitly to `long`/`int` when assigning to DTO fields (no implicit conversion).
- `Game.CompletionStatusId` defaults to `Guid.Empty` (not `null`) when unset — guard with `g.CompletionStatusId != Guid.Empty` before calling `.ToString()`.
- `Game.CompletionStatus` is a user-defined named object (not an enum) with a `.Name` string property; access null-safely (`g.CompletionStatus?.Name`).
- Adding a new `.cs` file requires a `<Compile Include="Folder\FileName.cs" />` entry in `GsPlugin.csproj` (old-style non-SDK project — files are not auto-included). Place files in the appropriate namespace folder (`Api/`, `Services/`, `Models/`, `Infrastructure/`, `View/`).
- New `.cs` files written with LF line endings will fail `dotnet format --verify-no-changes`; run `dotnet format` to auto-correct to CRLF.

### Sentry Release Management
- Runtime: Plugin reports version as `GsPlugin@X.Y.Z` from AssemblyInfo
- CI/CD: GitHub Actions creates Sentry releases, uploads portable PDB files (`--type=portablepdb`), and associates commits
- release-please keeps versions synchronized across `AssemblyInfo.cs`, `extension.yaml`, and manifests
- Only runs when release-please creates a GitHub release (conditional on `${{ steps.release.outputs.release_created }}`)
