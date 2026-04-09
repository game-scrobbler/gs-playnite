# Playnite 11 Migration

This branch (`playnite11`) targets Playnite 11 / .NET 10. The `main` branch remains the Playnite 10 release.

When P11 reaches GA and P10 is deprecated, `playnite11` becomes `main`.

---

## What's already done (this commit)

| File | Change |
|---|---|
| `GsPlugin.csproj` | Replaced old-style with SDK-style, `net10.0-windows`, `EnableDynamicLoading` |
| `GsPlugin.Tests/GsPlugin.Tests.csproj` | Updated to `net10.0-windows`, removed polyfill packages |
| `extension.toml` | Replaces `extension.yaml`; string id `GameScrobbler.GsPlugin` |
| `nuget.config` | Points to `nuget.playnite.link` for Playnite.SDK alpha packages |
| `Directory.Build.props` | Shared build settings, disables WinRT source generator |
| `global.json` | SDK version pinned to 10.0 |
| `Localization/en_US.ftl` | Replaces all 7 XAML locale files; all 117 keys converted to Fluent format |
| `Localization/Localization.cs` | `Loc` / `LocalizedString` wrappers for the Fluent API |
| `.github/workflows/build.yml` | Uses `dotnet build/test`, `.pext2` packaging, targets `playnite11` branch |

**Deleted:** `extension.yaml`, `packages.config`, `app.config`, `App.xaml`, `Properties/AssemblyInfo.cs`, `Localization/*.xaml`

---

## What still needs porting (source files)

The `.cs` source files are still P10. They will not compile against the P11 SDK until these changes are made.

### 1 — `GsPlugin.cs` (full rewrite)

| P10 | P11 |
|---|---|
| `class GsPlugin : GenericPlugin` | `class GsPlugin : Plugin` |
| `public override Guid Id` | `public const string Id = "GameScrobbler.GsPlugin"` |
| `public GsPlugin(IPlayniteAPI api) : base(api)` | `public GsPlugin()` (parameterless) |
| All init in constructor | `public override async Task InitializeAsync(InitializeArgs args)` — `PlayniteApi = args.Api` |
| `async void OnGameStarting(...)` | `async Task` override — name TBD from P11 SDK |
| `async void OnGameStopped(...)` | `async Task` override |
| `async void OnApplicationStarted(...)` | `async Task InitializeAsync` + `OnApplicationStartupAsync` |
| `void Dispose()` | `async ValueTask DisposeAsync()` |
| `GenericPluginProperties { HasSettings = true }` | `GetSettingsHandlerAsync` returning `PluginSettingsHandler` |
| `GetSidebarItems()` → `SiderbarItemType.View` | `GetAppViewItemDescriptors` + `AppViewItem` (auto-generates sidebar icon) |

```csharp
// New shell — fill in service initialization
public class GsPlugin : Plugin
{
    private static readonly ILogger logger = LogManager.GetLogger();
    public const string Id = "GameScrobbler.GsPlugin";
    public static IPlayniteApi PlayniteApi { get; private set; } = null!;

    public GsPlugin() { }

    public override async Task InitializeAsync(InitializeArgs args)
    {
        PlayniteApi = args.Api;
        Loc.Api = args.Api;
        // TODO: initialize GsDataManager, GsSnapshotManager, GsSentry, services
    }

    public override async ValueTask DisposeAsync()
    {
        // TODO: dispose timer, services
    }

    public override async Task<PluginSettingsHandler?> GetSettingsHandlerAsync(GetSettingsHandlerArgs args)
        => new GsPluginSettingsHandler(this);

    public override ICollection<AppViewItemDescriptor>? GetAppViewItemDescriptors(GetAppViewItemDescriptorsArgs args)
        => [new AppViewItemDescriptor("gs.dashboard", "Game Scrobbler",
                i => UIIcon.FromFontIcon("...", Playnite.Fonts.NerdFont),
                i => UIIcon.FromFontIcon("...", Playnite.Fonts.NerdFont))];

    public override AppViewItem? GetAppViewItem(GetAppViewItemsArgs args)
    {
        if (args.ViewId == "gs.dashboard") return new GsDashboardView();
        return null;
    }
}
```

### 2 — `View/MySidebarView` → `GsDashboardView.cs`

Create new `GsDashboardView : AppViewItem`. The existing XAML + WebView code moves into the `View` property. `ActivateViewAsync` replaces the "sidebar became visible" token refresh trigger.

```csharp
public class GsDashboardView : AppViewItem
{
    public GsDashboardView() => View = new MySidebarView();
    public override async Task ActivateViewAsync(ActivateViewAsyncArgs args) { /* refresh token */ }
    public override async Task DeactivateViewAsync(DeactivateViewAsyncArgs args) { }
}
```

### 3 — `View/GsPluginSettingsViewModel.cs` → `GsPluginSettingsHandler.cs`

```csharp
public class GsPluginSettingsHandler : PluginSettingsHandler
{
    public override FrameworkElement GetEditView(GetSettingsViewArgs args) => new GsPluginSettingsView { DataContext = this };
    public override async Task BeginEditAsync(BeginEditArgs args) { }
    public override async Task EndEditAsync(EndEditArgs args) { /* save to PlayniteApi.UserDataDir */ }
    public override async Task CancelEditAsync(CancelEditArgs args) { }
    public override async Task<ICollection<string>> VerifySettingsAsync(VerifySettingsArgs args) => [];
}
```

### 4 — `Services/GsUriHandler.cs`

- URI scheme: `playnite://` → `playnite11://`
- Handler: `Action<PlayniteUriEventArgs>` → `Func<PlayniteUriEventArgs, Task>`

```csharp
api.UriHandler.RegisterSource("gs", async args => { ... });
```

### 5 — Game model property renames (grep and replace across all files)

| Old | New | Files affected |
|---|---|---|
| `game.PluginId` | `game.LibraryId` | `GsScrobblingService`, `GsAllowedPlugins`, `GsIntegrationAccountReader` |
| `game.GameId` | `game.LibraryGameId` | `GsScrobblingService`, DTOs |
| `game.Playtime` | `game.PlayTime` | `GsScrobblingService` |
| `game.LastActivity` | `game.LastPlayedDate` | snapshot/sync logic |
| `game.Added` | `game.AddedDate` | sync |
| `game.Modified` | `game.ModifiedDate` | sync |
| `DateTime` fields | `DateTimeOffset` | `GsData`, `GsSnapshot`, sync comparisons |

### 6 — `Infrastructure/GsLocalization.cs`

Replace `Application.Current.Resources[key]` lookups with `Loc.GetString(key)`. The `Loc` class is already in `Localization/Localization.cs`. Run `Toolbox.exe ftlgen` to generate the typed `LocId` constants:

```
Toolbox.exe ftlgen "Localization/" "Localization/"
```

This generates `Localization/Localization.g.cs` with `Loc.*()` methods and `LocId.*` constants. Commit this generated file.

### 7 — `Services/GsNotificationService.cs`

Remove `Application.Current.Dispatcher.Invoke()` wrapping — notifications no longer need explicit UI-thread marshalling in P11.

```csharp
// P10 (remove)
Application.Current.Dispatcher.Invoke(() => PlayniteApi.Notifications.Add(...));

// P11
PlayniteApi.Notifications.Add(new NotificationMessage("id", "text", NotificationSeverity.Info));
```

`NotificationType` → `NotificationSeverity` (enum renamed).

### 8 — Remaining polyfill removals

The following are now built into .NET 10 — remove any explicit usage or imports:
- `Microsoft.Bcl.*` packages
- `System.Threading.Tasks.Extensions`
- `System.ValueTuple`
- Old `AssemblyResolve` handler in `GsPlugin`'s static constructor — delete entirely
- `app.config` binding redirects — deleted

### 9 — `GsPlayniteAchievementsHelper.cs`

Replace `Stub.System.Data.SQLite.Core.NetFramework` with `Microsoft.Data.Sqlite` (NuGet). The SQLite API is nearly identical; connection string and `using` namespaces change.

### 10 — Test project

- Remove SDK mocks that are no longer needed (`IPlayniteAPI` is now properly mockable)
- Update `DateTime` assertions to `DateTimeOffset`
- Update any `Game.PluginId` / `Game.Playtime` references in test data builders

---

## SDK & tooling reference

| Item | Value |
|---|---|
| SDK NuGet package | `Playnite.SDK` `11.0.0-alpha6` |
| NuGet feed | `https://nuget.playnite.link/v3/index.json` |
| Target framework | `net10.0-windows` |
| Extension manifest | `extension.toml` (TOML) |
| Extension package | `.pext2` |
| URI scheme | `playnite11://` |
| Localization format | Project Fluent (`.ftl`) via Linguini |
| Localization code-gen | `Toolbox.exe ftlgen "Localization/" "Localization/"` |
| Plugin base class | `Plugin` (no more `GenericPlugin`) |
| Plugin ID in code | `public const string Id = "GameScrobbler.GsPlugin"` |
| IPlayniteAPI access | `args.Api` in `InitializeAsync(InitializeArgs args)` |
| Dispose | `async ValueTask DisposeAsync()` |
| UI thread dispatch | `UIDispatcher.Invoke(...)` |
| Settings | `PluginSettingsHandler` returned from `GetSettingsHandlerAsync` |
| Sidebar view | `AppViewItemDescriptor` + `AppViewItem` (sidebar icon auto-generated) |
| Manual view switch | `PlayniteApi.MainView.SwitchToViewAsync("gs.dashboard")` |
| WebView (login flows) | `PlayniteApi.WebView.CreateView(...)` / `CreateOffscreenView(...)` |
