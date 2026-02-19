# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Game Spectrum (GS) is a Playnite plugin that integrates with GameScrobbler to provide game session tracking and statistics visualization. It's built as a .NET Framework 4.6.2 C# project using the Playnite SDK.

## Build & Development Commands

### Core Build Commands
- **Build solution**: `MSBuild.exe .\GsPlugin.sln -p:Configuration=Release -restore`
- **Restore NuGet packages**: `nuget restore .\GsPlugin.sln`
- **Format code**: `dotnet format .\GsPlugin.sln`
- **Verify formatting**: `dotnet format .\GsPlugin.sln --verify-no-changes`

### Pre-commit Hook Setup
- **Setup hooks**: `powershell -ExecutionPolicy Bypass -File setup-hooks.ps1`
- **Manual formatting**: `powershell -ExecutionPolicy Bypass -File format-code.ps1`

### Testing
- **Run tests**: `dotnet test GsPlugin.Tests\GsPlugin.Tests.csproj --configuration Release --no-build --verbosity normal`
- Note: Build the solution with MSBuild first, then run tests with `--no-build`

### Packaging
The project uses Playnite's Toolbox for packaging:
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"`

## Architecture Overview

### Core Plugin Structure
- **GsPlugin.cs** - Main plugin entry point, orchestrates all services and handles Playnite lifecycle events (implements IDisposable)
- **IGsApiClient.cs** - Interface for API client (enables testing and dependency injection)
- **GsApiClient.cs** - HTTP API communication layer with circuit breaker protection, retry logic, and TLS 1.2 enforcement; defines `GameSyncDto` (snake_case) — the library sync payload sent to `POST /api/playnite/v2/sync`
- **ApiResult.cs** - Generic result wrapper for API responses with success/failure status
- **GsCircuitBreaker.cs** - Implements circuit breaker pattern with exponential backoff for API resilience
- **GsScrobblingService.cs** - Handles game session tracking (start/stop events) and library sync; maps `Playnite.SDK.Models.Game` → `GameSyncDto` (including completion status and achievement counts) before sending to the API
- **GsSuccessStoryHelper.cs** - Retrieves per-game achievement counts from the SuccessStory addon (GUID `cebe6d32-8c46-4459-b993-5a5189d60788`) via reflection; returns `null` gracefully when SuccessStory is absent or a game has no data
- **GsAccountLinkingService.cs** - Manages account linking between Playnite and GameScrobbler with token validation
- **GsUriHandler.cs** - Processes deep links for automatic account linking (tokens redacted in logs)
- **GsData.cs** - Thread-safe persistent data models and state management (GsDataManager with locking); `SyncAchievements` bool (default `true`) gates achievement lookups at runtime
- **GsPluginSettings.cs** - Plugin settings with UI binding support and theme validation

### UI Components
- **View/GsPluginSettingsView.xaml/.cs** - Main settings interface with account linking UI
- **View/MySidebarView.xaml/.cs** - Sidebar integration displaying user statistics via WebView2
- **Localization/en_US.xaml** - English language resources

### Test Project
- **GsPlugin.Tests/** - xUnit test project targeting .NET Framework 4.6.2
- Tests cover: GsCircuitBreaker, GsData, ValidateToken, LinkingResult, ApiResult
- References main project via ProjectReference; requires `System.Text.Json` NuGet package

### Utilities & Cross-cutting Concerns
- **GsLogger.cs** - Centralized logging with debug UI feedback and HTTP request/response logging
- **GsSentry.cs** - Error tracking with global exception handlers and contextual information

### Service Dependencies
- GsPlugin → GsAccountLinkingService → GsApiClient → GsCircuitBreaker
- GsPlugin → GsScrobblingService → GsApiClient
- GsPlugin → GsScrobblingService → GsSuccessStoryHelper (achievement counts, optional)
- GsPlugin → GsUriHandler → GsAccountLinkingService
- All services use GsDataManager for persistent state

## Key Technologies & Patterns

### Reliability Features
- **Circuit Breaker Pattern**: Automatic failure detection and recovery for API calls
- **Exponential Backoff**: Smart retry logic with jitter to prevent thundering herd
- **Global Exception Handling**: UnobservedTaskException protection and comprehensive error reporting

### Configuration Files
- **extension.yaml** - Plugin metadata (ID, name, version, author)
- **manifest.yaml** - Plugin manifest for Playnite extension system
- **app.config** - .NET Framework application configuration
- **packages.config** - NuGet package dependencies
- **global.json** - Pins .NET SDK version

## Development Environment

### API Endpoints
- **Debug**: Uses staging environment (api.stage.gamescrobbler.com)
- **Release**: Uses production environment (api.gamescrobbler.com)

### Key Dependencies
- **Playnite SDK 6.12.0** - Main plugin framework
- **Microsoft.Web.WebView2** - For embedded web views
- **Sentry 5.15.1** - Error tracking and monitoring
- **System.Text.Json 6.0.10** - JSON serialization

### Build Environment
- Targets .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild, not `dotnet build`)
- Uses MSBuild for compilation (XAML code-gen requires VS Build Tools or full VS install)
- Windows-only development (PowerShell scripts for tooling)
- GitHub Actions for CI/CD with Windows 2022 runners
- Test project uses SDK-style .csproj and can be built/run with `dotnet test`

## Important Notes

### Code Formatting
All code must be formatted using `dotnet format` before commits. The pre-commit hook checks formatting with `--verify-no-changes` and fails the commit if code is unformatted. Run `powershell -ExecutionPolicy Bypass -File format-code.ps1` to fix formatting.

### Git Hooks
Hook scripts live in `hooks/` and are installed to `.git/hooks/` via `setup-hooks.ps1`:
- **pre-commit**: Verifies code formatting on staged `.cs` files
- **commit-msg**: Validates conventional commit message format

**Never use `--no-verify` when pushing or committing.** Git hooks enforce formatting and commit message standards; bypassing them with `--no-verify` is not allowed.

### Error Handling
The codebase emphasizes robust error handling with circuit breakers, comprehensive logging, and Sentry integration. When modifying API calls, ensure proper error handling and logging context.

### Sentry Release Management
Automatic release tracking is configured for comprehensive error monitoring:
- **Runtime Tracking**: Plugin reports version to Sentry using `GsPlugin@X.Y.Z` format from AssemblyInfo
- **CI/CD Integration**: GitHub Actions automatically creates Sentry releases, uploads debug symbols (PDB files), and associates commits
- **Debug Symbols**: Portable PDB files (`.pdb`) are uploaded using `--type=portablepdb` for proper stack trace symbolication and source code context
- **Version Sync**: release-please keeps versions synchronized across AssemblyInfo.cs, extension.yaml, and manifests
- **Configuration**: `.sentryclirc` is gitignored; use `.sentryclirc.template` as reference; Sentry project name is `gs-playnite`; requires `SENTRY_AUTH_TOKEN` secret in GitHub with scopes: `project:read`, `project:write`, `project:releases`, `org:read`
- **Workflow**: Only runs when `release-please` creates a GitHub release (conditional on `${{ steps.release.outputs.release_created }}`)
- **Troubleshooting**: "Project not found" errors indicate missing Sentry project or invalid auth token; "Invalid value 'portable'" means wrong type (should be `portablepdb`)

### UI Development
Settings UI uses two-way data binding with GsPluginSettings. The sidebar uses WebView2 for displaying GameScrobbler statistics. WebView2 navigation is restricted to `gamescrobbler.com` domains, and external links are opened in the system browser (https only).

### Playnite SDK Type Gotchas
- `Game.Playtime` and `Game.PlayCount` are `ulong` — cast explicitly to `long`/`int` when assigning to DTO fields (no implicit conversion).
- `Game.CompletionStatusId` defaults to `Guid.Empty` (not `null`) when unset — guard with `g.CompletionStatusId != Guid.Empty` before calling `.ToString()`.
- `Game.CompletionStatus` is a user-defined named object (not an enum) with a `.Name` string property; access null-safely (`g.CompletionStatus?.Name`).
- Adding a new `.cs` file requires a `<Compile Include="FileName.cs" />` entry in `GsPlugin.csproj` (old-style non-SDK project — files are not auto-included).
- New `.cs` files written with LF line endings will fail `dotnet format --verify-no-changes`; run `dotnet format` to auto-correct to CRLF.