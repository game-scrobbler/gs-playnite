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

### Testing & Packaging
The project uses Playnite's Toolbox for packaging:
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"`

## Architecture Overview

### Core Plugin Structure
- **GsPlugin.cs** - Main plugin entry point, orchestrates all services and handles Playnite lifecycle events
- **GsApiClient.cs** - HTTP API communication layer with circuit breaker protection and retry logic
- **GsCircuitBreaker.cs** - Implements circuit breaker pattern with exponential backoff for API resilience
- **GsScrobblingService.cs** - Handles game session tracking (start/stop events)
- **GsAccountLinkingService.cs** - Manages account linking between Playnite and GameScrobbler
- **GsUriHandler.cs** - Processes deep links for automatic account linking
- **GsData.cs** - Persistent data models and state management
- **GsPluginSettings.cs** - Plugin settings with UI binding support

### UI Components
- **View/GsPluginSettingsView.xaml/.cs** - Main settings interface with account linking UI
- **View/MySidebarView.xaml/.cs** - Sidebar integration displaying user statistics via WebView2
- **Localization/en_US.xaml** - English language resources

### Utilities & Cross-cutting Concerns
- **GsLogger.cs** - Centralized logging with debug UI feedback and HTTP request/response logging
- **GsSentry.cs** - Error tracking with global exception handlers and contextual information

### Service Dependencies
- GsPlugin → GsAccountLinkingService → GsApiClient → GsCircuitBreaker
- GsPlugin → GsScrobblingService → GsApiClient
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

## Development Environment

### API Endpoints
- **Debug**: Uses staging environment (api.stage.gamescrobbler.com)
- **Release**: Uses production environment (api.gamescrobbler.com)

### Key Dependencies
- **Playnite SDK 6.12.0** - Main plugin framework
- **Microsoft.Web.WebView2** - For embedded web views
- **Sentry 5.15.1** - Error tracking and monitoring
- **System.Text.Json 9.0.9** - JSON serialization

### Build Environment
- Targets .NET Framework 4.6.2
- Uses MSBuild for compilation
- Windows-only development (PowerShell scripts for tooling)
- GitHub Actions for CI/CD with Windows 2022 runners

## Important Notes

### Code Formatting
All code must be formatted using `dotnet format` before commits. The pre-commit hooks enforce this automatically.

### Error Handling
The codebase emphasizes robust error handling with circuit breakers, comprehensive logging, and Sentry integration. When modifying API calls, ensure proper error handling and logging context.

### Sentry Release Management
Automatic release tracking is configured for comprehensive error monitoring:
- **Runtime Tracking**: Plugin reports version to Sentry using `GsPlugin@X.Y.Z` format from AssemblyInfo
- **CI/CD Integration**: GitHub Actions automatically creates Sentry releases, uploads debug symbols (PDB files), and associates commits
- **Debug Symbols**: Portable PDB files are uploaded for proper stack trace symbolication and source code context
- **Version Sync**: release-please keeps versions synchronized across AssemblyInfo.cs, extension.yaml, and manifests
- **Configuration**: `.sentryclirc` provides sentry-cli defaults; requires `SENTRY_AUTH_TOKEN` secret in GitHub
- See `.github/SENTRY_RELEASES.md` for detailed documentation

### UI Development
Settings UI uses two-way data binding with GsPluginSettings. The sidebar uses WebView2 for displaying GameScrobbler statistics.