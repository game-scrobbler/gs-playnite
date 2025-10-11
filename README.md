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
- 🛡️ **Robust Error Handling**: Advanced fault tolerance with circuit breaker pattern and retry logic
- 🔍 **Comprehensive Logging**: Detailed logging with context for better debugging and monitoring

## Services

- **GsPlugin** acts as the central orchestrator with comprehensive exception handling, receiving events from Playnite SDK and delegating to appropriate services
- **GsScrobblingService** communicates with **GsApiClient** for session start/stop operations with enhanced null safety and logging
- **GsApiClient** provides fault-tolerant HTTP communication using **GsCircuitBreaker** for resilience and retry logic
- **GsCircuitBreaker** implements circuit breaker pattern with exponential backoff for API call protection
- **GsAccountLinkingService** uses **GsApiClient** for token verification and user validation
- **GsUriHandler** delegates account linking to **GsAccountLinkingService** when processing deep links
- All services use **GsDataManager** for persistent state management
- **GsLogger** and **GsSentry** provide cross-cutting logging, error tracking, and global exception protection
- Settings UI components use two-way data binding with **GsPluginSettings**
- Event-driven updates propagate status changes between services and UI components

## Project Structure

```md
gs-playnite/
├── GsPlugin.cs                       # Main plugin entry point
├── GsData.cs                         # Persistent data models and manager
├── GsApiClient.cs                    # HTTP API communication layer
├── GsCircuitBreaker.cs               # Circuit breaker pattern with retry logic
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

- **GsPlugin.cs** - Main plugin entry point, orchestrates all services and handles Playnite lifecycle events with comprehensive exception handling
- **GsApiClient.cs** - HTTP API layer for GameScrobbler communication with circuit breaker protection, input validation, and retry logic
- **GsCircuitBreaker.cs** - Implements circuit breaker pattern with exponential backoff retry logic for API resilience
- **GsAccountLinkingService.cs** - Manages account linking between Playnite and GameScrobbler
- **GsScrobblingService.cs** - Tracks game sessions (start/stop events) with enhanced logging and null safety checks
- **GsUriHandler.cs** - Processes deep links (`playnite://gamescrobbler/...`) for automatic account linking

## Data Management

- **GsData.cs** - Persistent data models and manager, handles installation ID and session state
- **GsPluginSettings.cs** - Plugin settings data model with UI binding support

## UI Components

- **GsPluginSettingsView.xaml/.cs** - Main settings interface with account linking UI and two-way data binding
- **MySidebarView.xaml/.cs** - Sidebar integration displaying web view with user statistics

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
- **setup-hooks.ps1** - Configures the Git hooks system for Windows
- **.git/hooks/pre-commit.ps1** - PowerShell pre-commit hook for code formatting validation
- **format-code.ps1** - Manual code formatting script for developers

#### 2. Commit-msg Hook (Conventional Commits)
- **.git/hooks/commit-msg.ps1** - PowerShell hook that validates commit messages
- **.git/hooks/commit-msg** - Shell wrapper for Git integration
- Enforces [Conventional Commits](https://www.conventionalcommits.org/) format
- Ensures Release Please can properly auto-version the project

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

- `dotnet format .\GsPlugin.sln` - Format code
- `dotnet format .\GsPlugin.sln --verify-no-changes` - Verify formatting

## Contributions

We welcome all contributions! If you have an idea for a new feature or have found a bug, feel free to open an issue or submit a pull request.

To ensure consistency, please format your code with `dotnet format GsPlugin.sln` before submitting a PR.

## More

If you enjoy this plugin, help support [Playnite launcher on Patreon](https://www.patreon.com/playnite).
