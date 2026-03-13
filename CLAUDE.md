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
│   ├── GsApiClient.cs       — HTTP API client; defines GameSyncDto and all DTOs
│   ├── IGsApiClient.cs      — API client interface
│   └── GsCircuitBreaker.cs  — Circuit breaker with exponential backoff
│
├── Services/            — namespace: GsPlugin.Services
│   ├── GsScrobblingService.cs       — Game session tracking and library/achievement sync
│   ├── IAchievementProvider.cs      — Achievement provider interface
│   ├── GsAchievementAggregator.cs   — Multi-provider achievement aggregation
│   ├── GsSuccessStoryHelper.cs      — SuccessStory addon integration (reflection)
│   ├── GsPlayniteAchievementsHelper.cs — Playnite Achievements addon integration (reflection)
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
│                       → GsAchievementAggregator → GsSuccessStoryHelper (reflection)
│                       │                         → GsPlayniteAchievementsHelper (reflection)
│                       → GsSnapshotManager (diff-based sync state)
├── GsAccountLinkingService → GsApiClient
├── GsNotificationService → GsApiClient (fire-and-forget background)
├── GsUriHandler → GsAccountLinkingService
├── GsUpdateChecker
└── All services use GsDataManager for persistent state
```

### Install Token Authentication
- `GsPlugin.OnApplicationStarted()` kicks off `EnsureInstallTokenAsync()` as a best-effort, fire-and-forget startup task so first-run registration does not block plugin startup.
- Each install registers with the server via `/api/playnite/v2/register`, receiving a per-install token stored in `GsData.InstallToken`.
- Authenticated write calls use the shared `PostJsonAsync()` path, which adds the `x-playnite-token` header when `InstallToken` is present. `RequestDeleteMyData()` and `GetDashboardToken()` also attach this header explicitly.
- `InstallIdForBody` returns `null` when a token is present, and request DTOs use `JsonIgnore(WhenWritingNull)` on `user_id`, so the server resolves identity from the header instead of the body.
- Pending scrobble DTOs still keep whatever `user_id` they were queued with, so old queued items can replay without depending on the current `InstallIdForBody` value.
- If `/v2/register` returns `PLAYNITE_TOKEN_ALREADY_REGISTERED`, the plugin treats the local token as lost, rotates to a fresh `InstallID`, clears identity-bound state, resets snapshots, and immediately re-registers under the new identity.
- `RotateInstallId()` clears token, linked user, sessions, pending scrobbles, sync hashes, cooldowns, and integration-account hashes before calling `GsSnapshotManager.Reset()`.
- `SetInstallTokenIfActive()` atomically checks opt-out status before persisting the token, preventing races with `PerformOptOut()`.
- Deletion requests require a valid `InstallToken`; the server resolves install identity from the `x-playnite-token` header. No `user_id` is sent in the body. `DeleteDataRes.rateLimited` is set when the server returns HTTP 429.
- `GetDashboardToken()` fetches a short-lived dashboard token used by `MySidebarView` as `?access_token=...`; if token fetch fails for a registered install, the dashboard fails closed instead of falling back to `user_id`.
- `IdentityGeneration` is incremented on fresh-install `InstallID` creation and on `RotateInstallId()`. `GsSnapshotManager` stamps this generation into `gs_snapshot.json` and discards snapshots whose generation no longer matches current data.
- `ResetInstallToken()` exists on `IGsApiClient`/`GsApiClient`, but the current lost-token recovery path uses local `InstallID` rotation plus re-registration rather than token reset.

### Server Notifications
- `GsNotificationService` fetches notifications from `GET /api/playnite/v2/notifications` at startup and displays them in Playnite's native notification tray.
- Runs as fire-and-forget via `FetchNotificationsAfterTokenAsync()` which awaits `EnsureInstallTokenAsync()` first, ensuring the install token is available before fetching. Never blocks the startup critical path.
- Auth: `x-playnite-token` header only — no `user_id`/`install_id` fallback.
- `GetNotifications()` in `GsApiClient` intentionally bypasses the shared circuit breaker so notification failures cannot affect core sync/scrobble paths.
- UI thread safety: notifications are collected on the background thread, then marshaled onto `Application.Current.Dispatcher.Invoke()` for `Notifications.Add()` calls.
- `GsDataManager.GetShownNotificationIds()` returns a lock-protected snapshot; `RecordShownNotifications()` atomically appends and persists under `_lock`, preventing cross-thread races with concurrent startup writes.
- `ShownNotificationIds` is capped at 100 entries and cleared on `RotateInstallId()` alongside other identity-bound state.
- Action URL handling: `gs://settings` opens plugin settings via `OpenPluginSettings(Id)`, `gs://addons` opens the add-ons dialog, `https://` URLs are opened in the browser but only for trusted hosts (`gamescrobbler.com`, `playnite.link`). Plain `http://` and untrusted hosts are rejected.
- Two user-facing settings (`ShowUpdateNotifications`, `ShowImportantNotifications`) control whether update and server notifications appear. Both default to `true` and are synced to `GsData` via `GsPluginSettingsViewModel.EndEdit()` and `LoadExistingSettings()`.

### Achievement Provider Architecture
Achievement data comes from two optional addons via an aggregator pattern:
- `IAchievementProvider` — common interface (`GetCounts`, `GetAchievements`, `IsInstalled`)
- `GsSuccessStoryHelper` — reads from SuccessStory addon via reflection (priority 1)
- `GsPlayniteAchievementsHelper` — reads from Playnite Achievements addon via reflection (priority 2)
- `GsAchievementAggregator` — iterates providers in order; first with data wins. Skips `(0, 0)` results to allow fallback.

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, net462)
- Test classes: `AchievementProviderTests`, `ApiResultTests`, `GsApiClientValidationTests`, `GsCircuitBreakerTests`, `GsDataManagerTests`, `GsDataTests`, `GsFlushAndPairingTests`, `GsMetadataHashTests`, `GsPluginSettingsViewModelTests`, `GsScrobblingServiceHashTests`, `GsSnapshotTests`, `GsTimeTests`, `LinkingResultTests`, `ValidateTokenTests`
- `GsDataManagerTests` and `GsDataTests` include coverage for install-token persistence, `IdentityGeneration`, `RotateInstallId()`, `SetInstallTokenIfActive()`, `InstallIdForBody`, opt-out token clearing, and `RecordShownNotifications()`/`GetShownNotificationIds()` thread-safe notification state.

## Build Environment

- Targets .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild, not `dotnet build`)
- XAML code-gen (WPF `PresentationBuildTasks`) requires the full `MSBuild.exe` from VS Build Tools or a full VS install; `dotnet msbuild` does **not** generate `.g.cs` files for old-style WPF projects, so View code-behind will fail to compile without it
- Test project uses SDK-style .csproj and can be built/run with `dotnet test`
- API endpoints: Debug → `api.stage.gamescrobbler.com`, Release → `api.gamescrobbler.com`
- When upgrading NuGet packages, only upgrade to versions that explicitly ship a `net462` (or `net461`/`net45`) lib folder. Do not rely on netstandard2.0 fallbacks for core runtime packages.

## Important Notes

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
