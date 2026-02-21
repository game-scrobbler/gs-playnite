# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Game Spectrum (GS) is a Playnite plugin that integrates with GameScrobbler to provide game session tracking and statistics visualization. It's built as a .NET Framework 4.6.2 C# project using the Playnite SDK.

## Build & Development Commands

- **Build solution**: `MSBuild.exe .\GsPlugin.sln -p:Configuration=Release -restore`
- **Restore NuGet packages**: `nuget restore .\GsPlugin.sln`
- **Format code**: `dotnet format .\GsPlugin.sln`
- **Verify formatting**: `dotnet format .\GsPlugin.sln --verify-no-changes`
- **Run all tests**: `dotnet test GsPlugin.Tests\GsPlugin.Tests.csproj --configuration Release --no-build --verbosity normal` (build with MSBuild first)
- **Run a single test**: `dotnet test GsPlugin.Tests\GsPlugin.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ClassName.MethodName"`
- **Setup git hooks**: `powershell -ExecutionPolicy Bypass -File setup-hooks.ps1`
- **Manual formatting**: `powershell -ExecutionPolicy Bypass -File format-code.ps1`
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"`

## Architecture Overview

### Service Dependency Graph
```
GsPlugin (entry point, IDisposable)
├── GsScrobblingService → GsApiClient → GsCircuitBreaker
│                       → GsSuccessStoryHelper (optional, achievement counts via reflection)
│                       → GsSnapshotManager (diff-based sync state)
├── GsAccountLinkingService → GsApiClient
├── GsUriHandler → GsAccountLinkingService
├── GsUpdateChecker
└── All services use GsDataManager for persistent state
```

### Core Components
- **GsPlugin.cs** — Main plugin entry point, orchestrates all services and handles Playnite lifecycle events
- **GsApiClient.cs** / **IGsApiClient.cs** — HTTP API communication with circuit breaker, retry logic, and TLS 1.2 enforcement; defines `GameSyncDto` (snake_case) sent to `POST /api/playnite/v2/sync`
- **GsScrobblingService.cs** — Game session tracking (start/stop events) and library sync; maps `Playnite.SDK.Models.Game` → `GameSyncDto` (including completion status and achievement counts)
- **GsSnapshot.cs** — Diff-based sync state management via `GsSnapshotManager` (static, thread-safe); stores library and achievement baselines in `gs_snapshot.json` separate from `gs_data.json`
- **GsData.cs** — Thread-safe persistent data models (`GsDataManager` with locking); `SyncAchievements` bool (default `true`) gates achievement lookups
- **GsCircuitBreaker.cs** — Circuit breaker pattern with exponential backoff and jitter
- **GsSuccessStoryHelper.cs** — Retrieves per-game achievement counts from SuccessStory addon (GUID `cebe6d32-8c46-4459-b993-5a5189d60788`) via reflection; returns `null` gracefully when absent
- **GsAccountLinkingService.cs** / **GsUriHandler.cs** — Account linking and deep link processing (tokens redacted in logs)
- **GsUpdateChecker.cs** — Plugin update checking

### UI Components
- **View/GsPluginSettingsView.xaml/.cs** — Settings UI with two-way data binding via `GsPluginSettings`
- **View/MySidebarView.xaml/.cs** — Sidebar displaying user statistics via WebView2 (navigation restricted to `gamescrobbler.com`; external links open in system browser, https only)

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, net462)
- Test classes: `GsCircuitBreakerTests`, `GsDataTests`, `GsDataManagerTests`, `GsTimeTests`, `GsMetadataHashTests`, `GsSnapshotTests`, `GsScrobblingServiceHashTests`, `ValidateTokenTests`, `LinkingResultTests`, `ApiResultTests`, `GsPluginSettingsViewModelTests`, `GsApiClientValidationTests`

## Build Environment

- Targets .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild, not `dotnet build`)
- XAML code-gen requires VS Build Tools or full VS install
- Test project uses SDK-style .csproj and can be built/run with `dotnet test`
- API endpoints: Debug → `api.stage.gamescrobbler.com`, Release → `api.gamescrobbler.com`

## Important Notes

### Code Formatting
All code must be formatted with `dotnet format` before commits. The pre-commit hook checks with `--verify-no-changes` and fails if unformatted.

### Git Hooks
Hook scripts in `hooks/` are installed to `.git/hooks/` via `setup-hooks.ps1`:
- **pre-commit**: Verifies code formatting on staged `.cs` files
- **commit-msg**: Validates conventional commit message format (`feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert`)

**Never use `--no-verify` when pushing or committing.** Git hooks enforce formatting and commit message standards; bypassing them is not allowed.

### Playnite SDK Type Gotchas
- `Game.Playtime` and `Game.PlayCount` are `ulong` — cast explicitly to `long`/`int` when assigning to DTO fields (no implicit conversion).
- `Game.CompletionStatusId` defaults to `Guid.Empty` (not `null`) when unset — guard with `g.CompletionStatusId != Guid.Empty` before calling `.ToString()`.
- `Game.CompletionStatus` is a user-defined named object (not an enum) with a `.Name` string property; access null-safely (`g.CompletionStatus?.Name`).
- Adding a new `.cs` file requires a `<Compile Include="FileName.cs" />` entry in `GsPlugin.csproj` (old-style non-SDK project — files are not auto-included).
- New `.cs` files written with LF line endings will fail `dotnet format --verify-no-changes`; run `dotnet format` to auto-correct to CRLF.

### Sentry Release Management
- Runtime: Plugin reports version as `GsPlugin@X.Y.Z` from AssemblyInfo
- CI/CD: GitHub Actions creates Sentry releases, uploads portable PDB files (`--type=portablepdb`), and associates commits
- release-please keeps versions synchronized across `AssemblyInfo.cs`, `extension.yaml`, and manifests
- Only runs when release-please creates a GitHub release (conditional on `${{ steps.release.outputs.release_created }}`)
