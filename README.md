# 🚀 GameSpectrum (GS): Visualize Your Playnite Stats! 📊

![GitHub release (latest by date)](https://img.shields.io/github/v/release/game-scrobbler/gs-playnite?cacheSeconds=5000&logo=github)
![GitHub Release Date](https://img.shields.io/github/release-date/game-scrobbler/gs-playnite?cacheSeconds=5000)
![Github Lastest Releases](https://img.shields.io/github/downloads/game-scrobbler/gs-playnite/latest/total.svg)
![GitHub commit activity](https://img.shields.io/github/commit-activity/m/game-scrobbler/gs-playnite)
![GitHub contributors](https://img.shields.io/github/contributors/game-scrobbler/gs-playnite?cacheSeconds=5000)
![GitHub](https://img.shields.io/github/license/game-scrobbler/gs-playnite?cacheSeconds=50000)

Game Spectrum is a Playnite plugin that provides game session tracking and statistics visualization via the help of GameScrobbler.

- ✨ **Playtime Insights**: See where your gaming hours go with beautiful charts and graphs.
- ⏳ **Achievement Visualization**: Track your milestones and spot patterns in your game play.
- 🔗 **Data Ownership**: Own and persist your data via the help of Game Scrobbler
- 📊 **Game Session Tracking**: Automatic scrobbling of game start/stop events

## Services

- **GsPlugin** acts as the central orchestrator, receiving events from Playnite SDK and delegating to appropriate services
- **GsScrobblingService** communicates with **GsApiClient** for session start/stop operations
- **GsAccountLinkingService** uses **GsApiClient** for token verification and user validation
- **GsUriHandler** delegates account linking to **GsAccountLinkingService** when processing deep links
- All services use **GsDataManager** for persistent state management
- **GsLogger** and **GsSentry** provide cross-cutting logging and error tracking
- Settings UI components use two-way data binding with **GsPluginSettings**
- Event-driven updates propagate status changes between services and UI components

## Project Structure

```md
gs-playnite/
├── GsPlugin.cs                       # Main plugin entry point
├── GsData.cs                         # Persistent data models and manager
├── GsApiClient.cs                    # HTTP API communication layer
├── GsPluginSettings.cs               # Plugin settings data model
├── GsScrobblingService.cs            # Game session tracking
├── GsAccountLinkingService.cs        # Account linking functionality
├── GsUriHandler.cs                   # Deep link handling
│
├── GsLogger.cs                       # Custom logging utilities
├── GsSentry.cs                       # Error tracking and reporting
│
├── View/
│   ├── GsPluginSettingsView.xaml     # Settings UI layout
│   ├── GsPluginSettingsView.xaml.cs  # Settings UI code-behind
│   ├── MySidebarView.xaml            # Sidebar layout
│   └── MySidebarView.xaml.cs         # Sidebar code-behind
│
├── Localization/
│   └── en_US.xaml                    # English language resources
│
│                       # Configuration:
├── extension.yaml                    # Plugin metadata
├── manifest.yaml                     # Plugin manifest
├── app.config                        # Application configuration
└── packages.config                   # NuGet package dependencies
```

## Core Services

- **GsPlugin.cs** - Main plugin entry point, orchestrates all services and handles Playnite lifecycle events
- **GsApiClient.cs** - HTTP API layer for GameScrobbler communication, handles authentication and serialization
- **GsAccountLinkingService.cs** - Manages account linking between Playnite and GameScrobbler
- **GsScrobblingService.cs** - Tracks game sessions (start/stop events) and sends data via API client
- **GsUriHandler.cs** - Processes deep links (`playnite://gamescrobbler/...`) for automatic account linking

## Data Management

- **GsData.cs** - Persistent data models and manager, handles installation ID and session state
- **GsPluginSettings.cs** - Plugin settings data model with UI binding support

## UI Components

- **GsPluginSettingsView.xaml/.cs** - Main settings interface with account linking UI and two-way data binding
- **MySidebarView.xaml/.cs** - Sidebar integration displaying web view with user statistics

## Utilities

- **GsLogger.cs** - Centralized logging with debug UI feedback and HTTP request/response logging
- **GsSentry.cs** - Error tracking and reporting with user opt-out and contextual information

## Configuration Files

- **extension.yaml** - Plugin metadata including ID, name, version, and author information
- **manifest.yaml** - Plugin manifest information for the Playnite extension system
- **app.config** - Application configuration file for .NET framework settings
- **packages.config** - NuGet package dependencies and version specifications

## Commands

- `dotnet format .\GsPlugin.sln`
-

## Contributions

We welcome all contributions! If you have an idea for a new feature or have found a bug, feel free to open an issue or submit a pull request.

To ensure consistency, please format your code with `dotnet format GsPlugin.sln` before submitting a PR.

## More

If you enjoy this plugin, help support [Playnite launcher on Patreon](https://www.patreon.com/playnite).
