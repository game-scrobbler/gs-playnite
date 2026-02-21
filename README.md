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
- 🏆 **Achievement Sync**: Sync per-game achievement counts via the [SuccessStory addon](https://playnite.link/addons.html#Success_Story_Addon) (optional, installed separately).
- 🔗 **Data Ownership**: Own and persist your data via the help of Game Scrobbler
- 📊 **Game Session Tracking**: Automatic scrobbling of game start/stop events
- ⚡ **Diff-Based Sync**: Only sends changed games to the server using snapshot-based diffing
- 🕐 **Server-Driven Cooldown**: Respects sync cooldown periods returned by the API
- 🛡️ **Robust Error Handling**: Advanced fault tolerance with circuit breaker pattern and retry logic
- 🔍 **Comprehensive Logging**: Detailed logging with context for better debugging and monitoring

## Services

- **GsPlugin** acts as the central orchestrator with comprehensive exception handling, receiving events from Playnite SDK and delegating to appropriate services
- **GsScrobblingService** communicates with **GsApiClient** for session start/stop operations and library sync; maps `Game` objects to `GameSyncDto` (snake_case) before sending to the API; uses **GsSnapshotManager** to diff against previous sync state and only send changes
- **GsSnapshotManager** manages diff-based sync state via `gs_snapshot.json`; stores library and achievement baselines to enable incremental sync
- **GsSuccessStoryHelper** retrieves per-game achievement counts from the SuccessStory addon via reflection; returns `null` gracefully when SuccessStory is not installed
- **GsApiClient** provides fault-tolerant HTTP communication using **GsCircuitBreaker** for resilience and retry logic; library sync uses the `POST /api/playnite/v2/sync` endpoint with `GameSyncDto` payloads; handles server-driven sync cooldown
- **GsCircuitBreaker** implements circuit breaker pattern with exponential backoff for API call protection
- **GsAccountLinkingService** uses **GsApiClient** for token verification and user validation
- **GsUriHandler** delegates account linking to **GsAccountLinkingService** when processing deep links
- **GsUpdateChecker** checks for plugin updates on startup
- All services use **GsDataManager** for persistent state management
- **GsLogger** and **GsSentry** provide cross-cutting logging, error tracking, and global exception protection
- Settings UI components use two-way data binding with **GsPluginSettings**
- Event-driven updates propagate status changes between services and UI components

## Project Structure

```md
gs-playnite/
├── GsPlugin.cs                       # Main plugin entry point
├── GsData.cs                         # Persistent data models and manager
├── IGsApiClient.cs                   # API client interface (enables DI and testing)
├── GsApiClient.cs                    # HTTP API communication layer; defines GameSyncDto
├── ApiResult.cs                      # Generic result wrapper for API responses
├── GsCircuitBreaker.cs               # Circuit breaker pattern with retry logic
├── GsSnapshot.cs                     # Diff-based sync state (GsSnapshotManager)
├── GsPluginSettings.cs               # Plugin settings data model
├── GsScrobblingService.cs            # Game session tracking and library sync
├── GsSuccessStoryHelper.cs           # Achievement counts via SuccessStory addon (reflection)
├── GsAccountLinkingService.cs        # Account linking functionality
├── GsUriHandler.cs                   # Deep link handling
├── GsUpdateChecker.cs                # Plugin update checking
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
├── GsPlugin.Tests/                   # xUnit test project (SDK-style, net462)
│   ├── GsCircuitBreakerTests.cs      # Circuit breaker state transitions and retry logic
│   ├── GsDataTests.cs                # Data model defaults and serialization round-trips
│   ├── GsDataManagerTests.cs         # IsAccountLinked, enqueue/dequeue, persistence
│   ├── GsTimeTests.cs                # FormatElapsed and FormatRemaining formatting
│   ├── GsMetadataHashTests.cs        # Per-field change detection in metadata hash
│   ├── GsSnapshotTests.cs            # Snapshot baselines, diffs, and persistence
│   ├── GsScrobblingServiceHashTests.cs # Library hash consistency and change detection
│   ├── ValidateTokenTests.cs         # Token validation rules
│   ├── LinkingResultTests.cs         # LinkingResult factory methods and IsNetworkError
│   ├── ApiResultTests.cs             # ApiResult Ok/Fail factory methods
│   ├── GsPluginSettingsViewModelTests.cs # LastSyncStatus time bucketing
│   └── GsApiClientValidationTests.cs # DTO construction and interface contract
│
│                       # Configuration:
├── extension.yaml                    # Plugin metadata
├── manifest.yaml                     # Plugin manifest
├── app.config                        # Application configuration
└── packages.config                   # NuGet package dependencies
```

## Core Services

- **GsPlugin.cs** - Main plugin entry point, orchestrates all services and handles Playnite lifecycle events with comprehensive exception handling
- **IGsApiClient.cs** - Interface for the API client, enabling dependency injection and testability
- **GsApiClient.cs** - HTTP API layer for GameScrobbler communication with circuit breaker protection, input validation, and retry logic; defines `GameSyncDto` (snake_case, includes scores, release year, dates, and user flags) for library sync payloads sent to `POST /api/playnite/v2/sync`; handles server-driven sync cooldown
- **ApiResult.cs** - Generic result wrapper for API responses with success/failure status
- **GsCircuitBreaker.cs** - Implements circuit breaker pattern with exponential backoff retry logic for API resilience
- **GsSnapshot.cs** - Diff-based sync state management via `GsSnapshotManager` (static, thread-safe); stores library and achievement baselines in `gs_snapshot.json` to enable incremental sync — only changed games are sent to the server
- **GsAccountLinkingService.cs** - Manages account linking between Playnite and GameScrobbler
- **GsScrobblingService.cs** - Tracks game sessions (start/stop events); during library sync maps each `Playnite.SDK.Models.Game` to a `GameSyncDto` including completion status and (optionally) achievement counts; skips sync when the library hash is unchanged
- **GsSuccessStoryHelper.cs** - Retrieves per-game achievement counts (`unlocked` / `total`) from the [SuccessStory addon](https://playnite.link/addons.html#Success_Story_Addon) via reflection; returns `null` for both fields when SuccessStory is not installed or the game has no achievement data
- **GsUriHandler.cs** - Processes deep links (`playnite://gamescrobbler/...`) for automatic account linking
- **GsUpdateChecker.cs** - Checks for plugin updates on startup

## Data Management

- **GsData.cs** - Persistent data models and manager, handles installation ID and session state
- **GsPluginSettings.cs** - Plugin settings data model with UI binding support

## UI Components

- **GsPluginSettingsView.xaml/.cs** - Main settings interface with account linking UI and two-way data binding
- **MySidebarView.xaml/.cs** - Sidebar integration displaying web view with user statistics

## Achievement Sync

The plugin can sync per-game achievement progress during library sync if the optional [SuccessStory addon](https://playnite.link/addons.html#Success_Story_Addon) is installed.

### How it works

1. During library sync `GsScrobblingService` builds a `GameSyncDto` for each game.
2. `GsSuccessStoryHelper` is called for each game; it locates the SuccessStory plugin by its well-known GUID (`cebe6d32-8c46-4459-b993-5a5189d60788`) via `IPlayniteAPI.Addons.Plugins`, then reads achievement data through reflection (`PluginDatabase.Get(gameId).Unlocked` / `.Items.Count`).
3. If SuccessStory is not installed or a game has no achievement data, both `achievement_count_unlocked` and `achievement_count_total` are sent as `null` — the backend treats `null` as "unknown", not zero.
4. The populated `GameSyncDto` list is sent to `POST /api/playnite/v2/sync` (snake_case payload).

### Settings toggle

**Sync achievement data (requires SuccessStory addon)** — enabled by default. When disabled, achievement fields are omitted from the sync payload. The toggle is in the plugin settings under the Experimental Features section.

The current `sync_achievements` state is also forwarded to the Game Spectrum dashboard via a URL parameter on the sidebar iframe.

## Utilities

- **GsLogger.cs** - Centralized logging with debug UI feedback, HTTP request/response logging, and enhanced context
- **GsSentry.cs** - Advanced error tracking with global exception handlers, UnobservedTaskException protection, and contextual information

## Reliability & Error Handling

The plugin includes advanced fault tolerance mechanisms to ensure stable operation:

### 🛡️ Circuit Breaker Pattern
- **Failure Detection**: Automatically detects API failures and opens circuit after threshold
- **Smart Recovery**: Half-open state tests service recovery before fully closing circuit
- **Configurable Thresholds**: Customizable failure counts and timeout periods

### ⚡ Retry Logic with Exponential Backoff
- **Intelligent Retries**: Automatic retry for failed API calls with increasing delays
- **Jitter Addition**: Random delays prevent thundering herd problems
- **Operation-Specific**: Different retry counts for critical vs. non-critical operations

### 🔍 Enhanced Logging & Monitoring
- **Contextual Logging**: Game IDs, session IDs, and detailed error information
- **Sentry Integration**: Automatic error reporting with rich context and filtering
- **Debug Utilities**: HTTP request/response logging for troubleshooting

### 🛡️ Global Exception Protection
- **UnobservedTaskException Handling**: Prevents application crashes from background task failures
- **Null Safety**: Comprehensive validation throughout the API layer
- **JSON Validation**: Safe deserialization with error recovery

## Development Tools

### 🔧 Git Hooks
The project includes automated validation via Git hooks:

#### 1. Pre-commit Hook (Code Formatting)
- **hooks/pre-commit** + **hooks/pre-commit.ps1** - Source hook scripts (checked into repo)
- **format-code.ps1** - Manual code formatting script for developers
- **setup-hooks.ps1** - Installs hooks from `hooks/` into `.git/hooks/` (run once after cloning)

#### 2. Commit-msg Hook (Conventional Commits)
- **hooks/commit-msg** + **hooks/commit-msg.ps1** - Source hook scripts (checked into repo)
- Enforces [Conventional Commits](https://www.conventionalcommits.org/) format
- Ensures Release Please can properly auto-version the project
- **Note**: Commits will be rejected if the message format is invalid — run `setup-hooks.ps1` first

**Setup Instructions:**
```powershell
# Run once to setup all Git hooks
powershell -ExecutionPolicy Bypass -File setup-hooks.ps1

# Manual formatting when needed
powershell -ExecutionPolicy Bypass -File format-code.ps1
```

**Commit Message Format:**
```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**Examples:**
```bash
feat: add new statistics dashboard
fix: resolve dependency version conflicts
fix(api): correct session tracking bug
docs: update README with commit guidelines
feat!: redesign API interface (breaking change)
```

**Valid types:**
- `feat`: New feature (triggers MINOR version bump)
- `fix`: Bug fix (triggers PATCH version bump)
- `docs`: Documentation changes
- `style`: Code style/formatting changes
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `test`: Adding or updating tests
- `build`: Build system or dependencies
- `ci`: CI/CD configuration changes
- `chore`: Other changes

**Breaking changes:** Add `!` after type or include `BREAKING CHANGE:` in footer (triggers MAJOR version bump)

The hooks provide fast local validation, while the CI pipeline handles comprehensive verification.

## Configuration Files

- **extension.yaml** - Plugin metadata including ID, name, version, and author information
- **manifest.yaml** - Plugin manifest information for the Playnite extension system
- **app.config** - Application configuration file for .NET Framework settings
- **packages.config** - NuGet package dependencies and version specifications

## Release Management

### Version Bumping

This project uses **Release Please** for automated version management based on [Conventional Commits](https://www.conventionalcommits.org/).

#### Automated Versioning (Recommended)

The version is automatically determined from your commit messages:

**PATCH version bump** (e.g., 0.6.2 → 0.6.3):
```bash
git commit -m "fix: resolve dependency version conflicts"
git commit -m "fix: correct session tracking bug"
```

**MINOR version bump** (e.g., 0.6.2 → 0.7.0):
```bash
git commit -m "feat: add new statistics dashboard"
git commit -m "feat: implement user preferences"
```

**MAJOR version bump** (e.g., 0.6.2 → 1.0.0):
```bash
git commit -m "feat!: redesign API interface

BREAKING CHANGE: API endpoints have changed"
```

When commits are pushed to `main`, Release Please automatically:
- Analyzes commit messages
- Determines the appropriate version bump
- Updates version in all configured files
- Creates/updates CHANGELOG.md
- Creates a GitHub release with release notes
- Tags the release
- Uploads the `.pext` package file

#### Manual Version Override

To manually set a specific version, update these files:

1. **`.release-please-manifest.json`**:
```json
{
  ".": "0.7.0"
}
```

2. **`release-please-config.json`** (line 11):
```json
"release-as": "0.7.0",
```

3. **`extension.yaml`** (line 4):
```yaml
Version: 0.7.0
```

4. **`Properties/AssemblyInfo.cs`** (lines 36-37):
```csharp
[assembly: AssemblyVersion("0.7.0")]
[assembly: AssemblyFileVersion("0.7.0")]
```

#### Version File Locations

Release Please automatically updates:
- `extension.yaml` - Plugin metadata version
- `Properties/AssemblyInfo.cs` - Assembly version attributes
- `GsPlugin.csproj` - Project version property

## Commands

- `MSBuild.exe .\GsPlugin.sln -p:Configuration=Release -restore` - Build solution
- `dotnet test GsPlugin.Tests\GsPlugin.Tests.csproj --configuration Release --no-build --verbosity normal` - Run tests (build first)
- `dotnet format .\GsPlugin.sln` - Format code
- `dotnet format .\GsPlugin.sln --verify-no-changes` - Verify formatting
- `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"` - Package plugin as `.pext`

## Contributions

We welcome all contributions! If you have an idea for a new feature or have found a bug, feel free to open an [issue](https://github.com/game-scrobbler/gs-playnite/issues) or submit a pull request.

To ensure consistency:
- Format your code with `dotnet format GsPlugin.sln` before submitting a PR
- All commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/) format (enforced by the commit-msg hook — see the Git Hooks section above)
- Run `powershell -ExecutionPolicy Bypass -File setup-hooks.ps1` once after cloning to install the hooks locally

## More

If you enjoy this plugin, help support [Playnite launcher on Patreon](https://www.patreon.com/playnite).
