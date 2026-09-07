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

### Phase A — Metadata sync ✅ DONE

`MapGameToDto` now resolves all metadata via `ILibraryApi` collections. P11 key facts:
- `Game.*Ids` are `HashSet<string>?` (not `List<Guid>`)
- Developers and publishers both resolve through `lib.Companies.Get(id)?.Name`
- Lookup method: `ILibraryCollection<T>.Get(string id)` (not indexer)
- `ILibraryApi` is injected via `GsScrobblingService` constructor; `GsPlugin.cs` passes `args.Api.Library`

### Phase B — PlayCount ✅ DONE

`play_count = (int)(g.SessionIds?.Count ?? 0)` — `Game.PlayCount` is gone in P11; `SessionIds` is a `HashSet<string>`.

### Phase D — Plugin version lookup ✅ DONE (permanent null)

`GetVersion()` returns `null` in both achievement helpers. `PlayniteApi.Addons.Plugins` is gone in P11 and the version is optional server-side metadata.

---

### Phase C — Game stop detection ✅ DONE (alpha14)

The SDK shipped the real lifecycle callbacks, so the workaround is gone. `Plugin` now exposes
`OnGameStartingAsync`, `OnGameStartedAsync` and `OnGameStoppedAsync`, alongside
`OnApplicationShutdownAsync` and `OnLibraryUpdateFinishedAsync`.

`GsPlugin` overrides `OnGameStartingAsync` (matching the Playnite 10 behaviour of scrobbling on
launch rather than on process start) and `OnGameStoppedAsync`. Two shapes to know:

- `OnGameStoppedEventArgs` carries no `Game`; the game is at `args.StartingArgs.Game`.
- Elapsed seconds are at `args.StoppedArgs.SessionLength` (a `uint`).

`GsScrobblingService.OnGameStartAsync`/`OnGameStoppedAsync` therefore take a `Game` rather than the
event args, so the service stays independent of the SDK's event shape. The `SessionIds`-diffing
code in `OnGameCollectionChange` has been deleted; that handler now only kicks off a library sync
when games are added.

---

### Phase D — Plugin version lookup in achievement helpers

**Affected files:**
- `Services/GsSuccessStoryHelper.cs:127`
- `Services/GsPlayniteAchievementsHelper.cs:158`

Both `GetVersion()` methods return `null` because `PlayniteApi.Addons.Plugins` was removed in P11.

**Options (pick one when P11 GA docs are available):**
1. **Read `extension.toml`** from the target addon's data directory — but the format is TOML and the path isn't guaranteed.
2. **Use file version info** on the addon DLL (if the path is predictable):
   ```csharp
   var dllPath = Path.Combine(extensionsPath, addonGuid.ToString(), "SuccessStory.dll");
   return File.Exists(dllPath)
       ? System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath).FileVersion
       : null;
   ```
3. **Return `null` permanently** — the version is sent to the API as metadata only; `null` is already handled by the server.

Option 3 is the lowest-risk path and is already the current behaviour. Promote it to a permanent decision when P11 GA ships and the Addons API is confirmed absent.

---

### Phase E — `Microsoft.Data.Sqlite` migration ✅ DONE

`System.Data.SQLite.Core` → `Microsoft.Data.Sqlite 9.*` in both csproj files. `SQLiteConnection` → `SqliteConnection`, connection string `Mode=ReadOnly`, `SQLiteException` → `SqliteException`. Test project updated identically.

---

### Phase F — Fluent localization migration ✅ DONE

All `GsLocalization.Get()` / `Format()` call sites replaced:
- `GsPlugin.cs`, `GsAccountLinkingService.cs`, `GsUriHandler.cs`, `GsPluginSettingsView.xaml.cs` → `Loc.*()` typed methods
- `GsData.cs` (`FormatElapsed`/`FormatRemaining`), `GsPluginSettingsViewModel.cs` → inline C# (keeps `GsTimeTests` and settings ViewModel tests passing without a live `Loc.Api`)
- `Infrastructure/GsLocalization.cs` deleted
- `Loc.Api = args.Api;` wired in `InitializeAsync`

---

### Remaining `// TODO P11` items

None. Phase C, the last one, closed when alpha14 shipped the game lifecycle callbacks.

### Merged from `main` (Playnite 10)

The Playnite 10 line kept moving while this branch sat still, so `main` was merged in. Everything
that is not tied to the Playnite 10 SDK came across: the v4 chunked full sync and the
confirm-then-commit baseline rule, `GsSyncHashIndex`/`GsHashIndexStore` replacing the fat
`gs_snapshot.json` (and `GsSnapshotManager`, now deleted), `GsAtomicFile` crash recovery, the
per-game scrobble gates and persist-before-send queueing, telemetry consent gating and Sentry
scrubbing, the inert `_dataUnavailable` mode, the private WebView2 profile with its fail-closed
behaviour, the deep-link confirmation prompt, and the source-alias allowlist.

Three merge decisions worth knowing:

1. **The slim library DTO won.** `main` removed genres/platforms/companies/scores/release dates
   from `GameSyncDto` per ADR-011 (IGDB is the server-side source of truth). This branch still had
   the fat v2 shape, and it is now gone here too.
2. **The library hash recipe is `main`'s again.** This branch had dropped
   `achievement_count_unlocked`/`_total` from `ComputeGameMetadataHash`, which silently diverged
   from the server's `createLibraryHashV3`. They are back, always null.
3. **Achievements stay removed** (see CLAUDE.md), but every piece needed to switch them back on
   — API methods, hash-index half, hash recipes, DTO fields — was kept.

---

## SDK & tooling reference

| Item | Value |
|---|---|
| SDK NuGet package | `Playnite.SDK` `11.0.0-alpha14` (matches the Playnite **11.0.11.0** alpha; the app and package alpha numbers differ) |
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
