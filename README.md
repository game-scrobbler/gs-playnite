# Game Scrobbler — Playnite Plugin

[![GitHub release](https://img.shields.io/github/v/release/game-scrobbler/gs-playnite?cacheSeconds=5000&logo=github)](https://github.com/game-scrobbler/gs-playnite/releases)
[![Downloads](https://img.shields.io/github/downloads/game-scrobbler/gs-playnite/latest/total.svg)](https://github.com/game-scrobbler/gs-playnite/releases)
[![License](https://img.shields.io/github/license/game-scrobbler/gs-playnite?cacheSeconds=50000)](LICENSE)

The official [Game Scrobbler](https://gamescrobbler.com) plugin for [Playnite](https://playnite.link). Automatically tracks your game sessions and syncs your library to provide rich statistics and insights on your gaming habits.

## Features

- **Automatic Session Tracking** — scrobbles game start/stop events in the background
- **Library Sync** — syncs your Playnite library with diff-based incremental updates
- **Achievement Sync** — syncs per-game achievement data from the [SuccessStory](https://playnite.link/addons.html#Success_Story_Addon) and/or [Playnite Achievements](https://playnite.link/addons.html#PlayniteAchievements_e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b) addons (optional, either or both)
- **Interactive Dashboard** — embedded WebView2 sidebar with playtime charts and statistics
- **Account Linking** — link your plugin to a Game Scrobbler account to own and persist your data
- **Privacy Controls** — disable scrobbling or error reporting from settings
- **Resilient Networking** — circuit breaker pattern, exponential backoff, and graceful Sentry fallback

## Achievement Sync

The plugin syncs per-game achievement progress during library sync using an aggregator that checks multiple providers in priority order:

1. **[SuccessStory](https://playnite.link/addons.html#Success_Story_Addon)** — community addon with broad platform support
2. **[Playnite Achievements](https://playnite.link/addons.html#PlayniteAchievements_e6aad2c9-6e06-4d8d-ac55-ac3b252b5f7b)** — addon for tracking achievements natively within Playnite

For each game, the first provider that returns data wins. If neither addon is installed, achievement fields are sent as `null` (the backend treats this as "unknown", not zero). Both providers are accessed via reflection so the plugin works regardless of whether they're installed.

**Settings toggle:** *Sync achievement data* is enabled by default. Disable it in **Settings > Experimental Features** to omit achievement fields from sync payloads.

## Project Structure

```
gs-playnite/
├── GsPlugin.cs                       # Main plugin entry point
├── Api/
│   ├── ApiResult.cs                  # Generic API result wrapper
│   ├── GsApiClient.cs               # HTTP client; DTOs (GameSyncDto, AchievementItemDto, etc.)
│   ├── IGsApiClient.cs              # API client interface
│   └── GsCircuitBreaker.cs          # Circuit breaker with exponential backoff
├── Services/
│   ├── GsScrobblingService.cs       # Session tracking and library/achievement sync
│   ├── GsAchievementAggregator.cs   # Multi-provider achievement aggregation
│   ├── GsSuccessStoryHelper.cs      # SuccessStory addon integration (reflection)
│   ├── GsPlayniteAchievementsHelper.cs # Playnite Achievements integration (reflection)
│   ├── GsAccountLinkingService.cs   # Account linking operations
│   ├── GsUriHandler.cs              # Deep link processing
│   └── GsUpdateChecker.cs           # Plugin update checking
├── Models/
│   ├── GsData.cs                    # Persistent data (GsDataManager)
│   ├── GsSnapshot.cs               # Diff-based sync state (GsSnapshotManager)
│   └── GsPluginSettings.cs         # Settings data model and view model
├── Infrastructure/
│   ├── GsLogger.cs                  # Logging wrapper
│   └── GsSentry.cs                  # Sentry error tracking
├── View/
│   ├── GsPluginSettingsView.xaml/.cs # Settings UI
│   └── MySidebarView.xaml/.cs        # Sidebar with WebView2
├── GsPlugin.Tests/                   # xUnit test project (net462)
├── scripts/                          # PowerShell build/dev scripts
├── hooks/                            # Git hook source scripts
└── Localization/                     # Language resources
```

## Development

### Prerequisites

- .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild)
- [Playnite SDK](https://playnite.link/docs/)

### Build

```bash
MSBuild.exe GsPlugin.sln -p:Configuration=Release -restore
```

### Test

```bash
dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --no-build
```

### Git Hooks

Run once after cloning to install formatting and commit message validation hooks:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-hooks.ps1
```

- **Pre-commit** — verifies `dotnet format` on staged `.cs` files
- **Commit-msg** — enforces [Conventional Commits](https://www.conventionalcommits.org/) format

### Commit Message Format

```
<type>[optional scope]: <description>
```

Valid types: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `revert`

## Release Management

This project uses [Release Please](https://github.com/googleapis/release-please) for automated versioning based on Conventional Commits.

When commits are pushed to `main`, Release Please:
- Determines the version bump from commit messages (`fix` → patch, `feat` → minor, `feat!` → major)
- Updates `extension.yaml`, `Properties/AssemblyInfo.cs`, and `GsPlugin.csproj`
- Creates a GitHub release with changelog and `.pext` package

## Contributing

1. Fork and clone the repo
2. Run `powershell -ExecutionPolicy Bypass -File scripts/setup-hooks.ps1`
3. Format code with `dotnet format GsPlugin.sln` before submitting a PR
4. All commits must follow [Conventional Commits](https://www.conventionalcommits.org/) format

Issues and PRs welcome at [game-scrobbler/gs-playnite](https://github.com/game-scrobbler/gs-playnite).

---

If you enjoy this plugin, consider supporting [Playnite on Patreon](https://www.patreon.com/playnite).
