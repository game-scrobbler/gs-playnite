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
├── GsUriHandler → GsAccountLinkingService
├── GsUpdateChecker
└── All services use GsDataManager for persistent state
```

### Achievement Provider Architecture
Achievement data comes from two optional addons via an aggregator pattern:
- `IAchievementProvider` — common interface (`GetCounts`, `GetAchievements`, `IsInstalled`)
- `GsSuccessStoryHelper` — reads from SuccessStory addon via reflection (priority 1)
- `GsPlayniteAchievementsHelper` — reads from Playnite Achievements addon via reflection (priority 2)
- `GsAchievementAggregator` — iterates providers in order; first with data wins. Skips `(0, 0)` results to allow fallback.

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, net462)
- Test classes: `AchievementProviderTests`, `ApiResultTests`, `GsApiClientValidationTests`, `GsCircuitBreakerTests`, `GsDataManagerTests`, `GsDataTests`, `GsFlushAndPairingTests`, `GsMetadataHashTests`, `GsPluginSettingsViewModelTests`, `GsScrobblingServiceHashTests`, `GsSnapshotTests`, `GsTimeTests`, `LinkingResultTests`, `ValidateTokenTests`

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
