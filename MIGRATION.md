# Playnite 11 Migration

This branch (`playnite11`) targets Playnite 11 / .NET 10. The `main` branch remains the Playnite 10 release.

When P11 reaches GA and P10 is deprecated, `playnite11` becomes `main`.

---

## What's done

### Infrastructure / project files (initial commit)

| File | Change |
|---|---|
| `GsPlugin.csproj` | Replaced old-style with SDK-style, `net10.0-windows`, `EnableDynamicLoading` |
| `GsPlugin.Tests/GsPlugin.Tests.csproj` | Updated to `net10.0-windows`, removed polyfill packages |
| `extension.toml` | Replaces `extension.yaml`; string id `GameScrobbler.GsPlugin` |
| `nuget.config` | Points to `nuget.playnite.link` for Playnite.SDK alpha packages |
| `Directory.Build.props` | Shared build settings, disables WinRT source generator |
| `global.json` | SDK version pinned to 10.0 |
| `Localization/Localization.cs` | `Loc` / `LocalizedString` wrappers for the Fluent API (Fluent migration not yet complete — XAML files still used) |
| `.github/workflows/build.yml` | Uses `dotnet build/test`, `.pext2` packaging, targets `playnite11` branch |

**Deleted:** `extension.yaml`, `packages.config`, `app.config`, `App.xaml`, `Properties/AssemblyInfo.cs`

### Source migration (completed — all 301 tests pass)

- `GsPlugin.cs`: `GenericPlugin` → `Plugin`; constructor injection → `InitializeAsync(InitializeArgs args)`; `Dispose()` → `async ValueTask DisposeAsync()`; `GetSidebarItems` → `GetAppViewItemDescriptors` + `GetAppViewItem`; `GetAppMenuItems` updated to `args.ItemId` string matching with `MenuItemImpl`
- `View/GsDashboardView.cs`: new `AppViewItem` subclass wrapping `MySidebarView`; `ActivateViewAsync` handles token refresh
- `View/GsPluginSettingsHandler.cs`: new `PluginSettingsHandler` subclass replacing the old view-model registration approach
- `Services/GsUriHandler.cs`: handler changed to `Func<PlayniteUriEventArgs, Task>`
- Game model renames applied across all files: `PluginId` → `LibraryId`, `GameId` → `LibraryGameId`, `Playtime` → `PlayTime`, `LastActivity` → `LastPlayedDate`, `Added` → `AddedDate`, `Modified` → `ModifiedDate`
- `Game.Id` treated as `string`; `Guid.TryParse` used where a `Guid` is required
- `Game.ReleaseDate` typed as `Playnite.PartialDate?` (class, not struct); `!= null` guards used
- `Services/GsNotificationService.cs`: `Application.Current.Dispatcher.Invoke()` removed; `NotificationType` → `NotificationSeverity`
- Polyfill packages and `AssemblyResolve` handler removed
- `ServicePointManager` usage removed (SYSLIB0014 on .NET 10)
- `InternalsVisibleTo` declared via `<AssemblyAttribute>` in `<ItemGroup>` (not `<PropertyGroup>`)
- `CA1822` added to `WarningsNotAsErrors` in Release build
- `System.Windows.MessageBox.Show()` used for Yes/No dialogs; result type qualified as `System.Windows.MessageBoxResult`
- `IDialogs.ShowMessageAsync` used for informational dialogs
- `UIIcon.FromFontIcon` / `UIIcon.FromBitmapFile` used for app-view icons
- `GsDataManager.IsOptedOut` check added in `FlushPendingScrobblesAsync`
- Test project updated: `Game.Id` as `string`, `PartialDate` assertions, `DateTimeOffset` comparisons

---

## What still needs porting

### 1 — Fluent localization migration

Currently the plugin still uses `GsLocalization.Get()` / `Format()` wrappers backed by XAML resource dictionaries (`Localization/*.xaml`). The `Localization/Localization.cs` Fluent wrapper exists but the migration from XAML → `.ftl` is not yet complete.

When ready:
1. Convert all 7 XAML locale files to `Localization/en_US.ftl` (and other locales)
2. Run `Toolbox.exe ftlgen "Localization/" "Localization/"` to generate `Localization/Localization.g.cs` with typed `Loc.*()` methods and `LocId.*` constants
3. Replace all `GsLocalization.Get(key, fallback)` / `GsLocalization.Format(key, fallback, args)` calls with `Loc.*(...)` equivalents
4. Delete `Infrastructure/GsLocalization.cs` and the XAML locale files
5. Commit the generated `Localization/Localization.g.cs`

### 2 — `Microsoft.Data.Sqlite` migration

`GsPlayniteAchievementsHelper.cs` still uses `System.Data.SQLite.Core` (which ships `SQLite.Interop.dll` native binaries). Replace with `Microsoft.Data.Sqlite` (NuGet). The API is nearly identical; only the connection string format and `using` namespaces change.

### 3 — Remaining `// TODO P11` items in source

Grep for `// TODO P11` comments in the source before release to catch any deferred items not covered above.

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
