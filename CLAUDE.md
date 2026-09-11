# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Game Scrobbler is a Playnite plugin that tracks game sessions and provides statistics visualization. It's the official Playnite plugin for GameScrobbler. It's built as a .NET Framework 4.6.2 C# project using the Playnite SDK.

## Build & Development Commands

- **Build solution**: `MSBuild.exe GsPlugin.sln -p:Configuration=Release -restore`
- **Restore NuGet packages**: `nuget restore GsPlugin.sln`
- **Format code**: `powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1`. Do not call `dotnet format GsPlugin.sln` directly: the solution's old-style WPF `.csproj` can only be loaded through a .NET Framework build host (`BuildHost-net472`) that the repo-pinned .NET 8 SDK does not ship, so the bare command dies with "The build host could not be found". `scripts/format-sdk.ps1` picks the newest installed SDK that has one and leaves the SDK pin alone.
- **Verify formatting**: same script; the pre-commit hook runs it for staged `.cs` files.
- **Run all tests**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --no-build --verbosity normal` (build with MSBuild first)
- **Run a single test**: `dotnet test GsPlugin.Tests/GsPlugin.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ClassName.MethodName"`
- **Setup git hooks**: `powershell -ExecutionPolicy Bypass -File scripts/setup-hooks.ps1`
- **Manual formatting**: `powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1`
- **Pack plugin**: `Playnite\Toolbox.exe pack "bin\Release" "PackingOutput"`

## Architecture Overview

### Project Structure
```
GsPlugin.cs              — Entry point (namespace: GsPlugin)
│
├── Api/                 — namespace: GsPlugin.Api
│   ├── ApiResult.cs         — Generic API result wrapper
│   ├── Dtos.cs              — All API request/response DTOs (namespace-level, not nested)
│   ├── GsApiClient.cs       — HTTP API client
│   ├── IGsApiClient.cs      — API client interface
│   └── GsCircuitBreaker.cs  — Circuit breaker with exponential backoff
│
├── Services/            — namespace: GsPlugin.Services
│   ├── GsScrobblingService.cs       — Game session tracking and library/achievement sync
│   ├── IAchievementProvider.cs      — Achievement provider interface
│   ├── AchievementProviderBase.cs   — Shared provider base (plugin id, IsInstalled, GetVersion, safe-read wrappers)
│   ├── GsAchievementAggregator.cs   — Multi-provider achievement aggregation
│   ├── GsSuccessStoryHelper.cs      — SuccessStory addon integration (direct JSON file reads)
│   ├── GsPlayniteAchievementsHelper.cs — Playnite Achievements addon integration (direct SQLite reads)
│   ├── GsAccountLinkingService.cs   — Account linking operations
│   ├── GsNotificationService.cs      — Server notification fetch and display
│   ├── GsUriHandler.cs              — Deep link processing
│   └── GsUpdateChecker.cs           — Plugin update checking
│
├── Models/              — namespace: GsPlugin.Models
│   ├── GsData.cs            — Persistent data (GsDataManager, GsTime, PendingScrobble)
│   ├── GsSyncHashIndex.cs   — Static facade over the two hash index stores (library, achievements)
│   ├── GsHashIndexStore.cs  — Per-half index store instance (load/migrate/save/replace/diff/clear)
│   ├── GsSnapshot.cs        — Legacy fat snapshot POCO types (deserialized once by GsSyncHashIndex during migration; no manager)
│   └── GsPluginSettings.cs  — Settings data model and view model
│
├── Infrastructure/      — namespace: GsPlugin.Infrastructure
│   ├── GsAtomicFile.cs      — Shared temp recovery + atomic JSON replace/retry helpers
│   ├── GsLocalization.cs    — XAML resource string lookup helper
│   ├── GsLogger.cs          — Logging wrapper
│   ├── GsTaskExtensions.cs  — LogFaults() fire-and-forget fault observer
│   ├── GsTelemetryConsent.cs — Consent gate + HTTP handler that drops post-withdrawal batches
│   ├── GsAssemblyIdentity.cs — Public-key-token comparison for the AssemblyResolve handler
│   ├── GsPostHog.cs         — PostHog product analytics
│   └── GsSentry.cs          — Sentry error tracking
│
├── View/                — namespace: GsPlugin.View
│   ├── GsPluginSettingsView.xaml/.cs — Settings UI
│   ├── GsConverters.cs               — StringToVisibilityConverter for binding-driven visibility
│   └── MySidebarView.xaml/.cs        — Sidebar with WebView2
│
├── scripts/             — PowerShell build/dev scripts
├── hooks/               — Git hook scripts
├── GsPlugin.Tests/      — xUnit test project (net462)
└── Properties/          — AssemblyInfo.cs
```

### Service Dependency Graph
```
GsPlugin (entry point, IDisposable)
├── GsScrobblingService → GsApiClient → GsCircuitBreaker
│                       → GsAchievementAggregator → GsSuccessStoryHelper (JSON file reads)
│                       │                         → GsPlayniteAchievementsHelper (SQLite reads)
│                       → GsSyncHashIndex (per-item fingerprint baselines for diffs)
├── GsAccountLinkingService → GsApiClient
├── GsNotificationService → GsApiClient (fire-and-forget background)
├── GsUriHandler → GsAccountLinkingService
├── GsUpdateChecker
└── All services use GsDataManager for persistent state
```

### Library & Achievement Sync (v4 chunked full + v3/v2 diff)
- **Full sync (current plugin):** begin → chunk (≤500 items) → commit against `/api/playnite/v4/{library|achievements}/sync-full/*`. Never calls monolithic `/v3/library/sync-full` or `/v2/achievements/sync-full`.
- **Diff sync:** still `POST /v3/library/sync-diff` and `POST /v2/achievements/sync-diff`. Diffs compare live DTOs against the local fingerprint index (`GsSyncHashIndex`), not fat game/achievement snapshots.
- **Local baselines:** `gs_library_hashes.json` / `gs_achievement_hashes.json` store `{ playnite_id → fingerprint }` only. Library fingerprint = `playtime|play_count|normalized_last_activity|metadata_hash`. The local achievement fingerprint hashes each sorted `name + unlock state + rounded rarity` tuple, so rarity-only changes and same-count unlock swaps are detected. Global `LastLibraryHash` / `LastAchievementHash` in `gs_data.json` remain the server `result_snapshot_hash` / `base_snapshot_hash` values (`createLibraryHashV3` / `createAchievementHashV2` recipes — unchanged); do not substitute the richer local achievement recipe for this server contract.
- **Hashed values must serialize exactly as hashed.** `result_snapshot_hash` can only be recomputed from the payload, so anything feeding the hash has to go on the wire in its hashed form. `GameSyncDto`'s three date fields carry `[JsonConverter(typeof(CanonicalDateTimeConverter))]` for this reason: System.Text.Json preserves `DateTimeKind` and would otherwise emit `2025-02-19T14:51:26.897-08:00` for a value hashed as `2025-02-19T22:51:26Z`, leaving the recipient to infer a normalization the payload never states. Never add a `DateTime` to a hashed DTO without the converter, and land any recipe change in `GsPlugin.Tests/Fixtures/playnite-hash-vectors.json`, whose twin in the backend repo asserts the same digests.
- **Permanent rejections are not retried.** `PostV4Async` passes `isPermanent` to the circuit breaker so a 4xx (other than 408/429) returns immediately and does not count toward the failure threshold — retrying cannot change the answer, and letting it trip the breaker takes scrobbles down with it. Such a response still marks the service responsive, so it resolves a HalfOpen probe rather than leaving the breaker mid-probe.
- **v4 failures say which kind they are.** `UploadFullChunkedAsync` renders every begin/chunk/commit failure through `DescribeV4Failure`, because interpolating `response?.status` collapsed a response that never arrived and a response the server rejected into the same empty `status=`. A null response reports "no usable response" and defers to the HTTP status and truncated body that `PostJsonAsync` already logged; a response that arrived prints whichever of the server's `status`/`error`/`reason`/`message` it actually set. See gs-playnite#89, where three identical `Library v4 commit failed: status=` lines were the entire record of a days-long outage.
- **Commit order:** a `queued` response is admission, not completion. Poll for a terminal status before saving the index and then `Last*Hash`. Only an answer the server disowned blocks the baseline: a `failed`/`partial` status, or an admission carrying no job ID to poll. A job still processing when the bounded poll expires **does** commit. Refusing to meant a library whose server-side job outran the budget re-uploaded in full on every launch and never advanced `LastSyncAt`, which is the force-full-sync loop this step exists to prevent. No pending-job reconciliation is persisted. The captured install identity/generation fences both baseline writes against deletion or rotation during the request.
- This invariant is enforced in exactly one place: `GsScrobblingService.CommitSyncBaselineAsync(label, queueId, expectedInstallId, expectedGeneration, persistIndex, persistHashes)`. All four sync paths (library full/diff, achievements full/diff) route through it, so do not re-implement the confirm-then-index-then-hash sequence at a call site. `SkipOrRepairIndex` likewise owns the "hash matches but index count diverged" repair for all three paths.
- **Migration:** if the shipped legacy `gs_snapshot.json` exists, `GsSyncHashIndex.Initialize` derives fingerprints once, writes the compact files, and deletes the fat snapshot.
- **Crash recovery:** `GsAtomicFile` promotes a surviving `.tmp` when the destination is missing and performs JSON replacement with bounded retries for transient Windows file locks. Both `GsDataManager` and `GsSyncHashIndex` use it. See **Atomic File Writes** for the retry budget and why it is the size it is.
- **Self-heal:** if global hash matches but index entry count ≠ live allowed count → rewrite index from live (no re-upload). On `force-full-sync` from diff → clear index + global hash → chunked full with `bypassCooldown`.
- Monolithic v2/v3 full endpoints remain on the server for old plugin builds only.

### Install Token Authentication
- `GsPlugin.OnApplicationStarted()` starts `EnsureInstallTokenAsync()` in parallel with refresh/update work, then awaits it before the first v4 sync because v4 is strictly token-authenticated.
- `EnsureInstallTokenAsync()` retries up to 3 times with exponential backoff (2 s, 4 s) for transient network errors; a non-null result (success or known error code) breaks the loop immediately.
- Each install registers with the server via `/api/playnite/v2/register`, receiving a per-install token stored in `GsData.InstallToken`.
- Authenticated write calls use the shared `PostJsonAsync()` path, which adds the `x-playnite-token` header when `InstallToken` is present. `RequestDeleteMyData()` and `GetDashboardToken()` also attach this header explicitly.
- v4 full-sync DTOs carry no body identity; begin requests require `InstallToken`, and the server resolves the install exclusively from `x-playnite-token`. Legacy request DTOs still use `InstallIdForBody` where supported.
- If registration cannot produce a token, the v4 client fails closed before opening a sync session; it must not fall back to `user_id` because the backend's v4 routes use strict token middleware.
- Pending scrobble DTOs still keep whatever `user_id` they were queued with, so old queued items can replay without depending on the current `InstallIdForBody` value.
- If `/v2/register` returns `PLAYNITE_TOKEN_ALREADY_REGISTERED`, the plugin treats the local token as lost, rotates to a fresh `InstallID`, clears identity-bound state, resets the hash index, and immediately re-registers under the new identity.
- `RotateInstallId()` clears token, linked user, sessions, pending scrobbles, sync hashes, cooldowns, and integration-account hashes before calling `GsSyncHashIndex.Reset()`.
- `SetInstallTokenIfActive()` atomically checks opt-out status before persisting the token, preventing races with `PerformOptOut()`.
- Deletion requests require a valid `InstallToken`; the server resolves install identity from the `x-playnite-token` header. No `user_id` is sent in the body. `DeleteDataRes.rateLimited` is set when the server returns HTTP 429.
- `GetDashboardToken()` sends a POST request with a dashboard context object (`plugin_version`, flags, preferences) in the body. The server stores this context alongside the token and returns it tamper-proof when the frontend resolves the token — eliminating the need for client-side URL query params. If the token fetch fails for a registered install, the dashboard fails closed instead of falling back to `user_id`.
- `IdentityGeneration` is incremented on fresh-install `InstallID` creation, on `RotateInstallId()`, and on `PerformOptOut()`. `GsSyncHashIndex` stamps this generation into the hash-index files and discards indexes whose generation no longer matches current data.

### Server Notifications
- `GsNotificationService` fetches notifications from `GET /api/playnite/v2/notifications` at startup and displays them in Playnite's native notification tray.
- Runs as fire-and-forget via `FetchNotificationsAfterTokenAsync()` which awaits `EnsureInstallTokenAsync()` first, ensuring the install token is available before fetching. Never blocks the startup critical path.
- Auth: `x-playnite-token` header only — no `user_id`/`install_id` fallback.
- `GetNotifications()` in `GsApiClient` intentionally bypasses the shared circuit breaker so notification failures cannot affect core sync/scrobble paths.
- UI thread safety: notifications are collected on the background thread, then marshaled onto `Application.Current.Dispatcher.Invoke()` for `Notifications.Add()` calls. The dispatcher invoke is wrapped in try/catch so a dispatcher fault does not surface as a false Sentry error.
- `GsDataManager.GetShownNotificationIds()` returns a lock-protected snapshot; `RecordShownNotifications()` atomically appends and persists under `_lock`, preventing cross-thread races with concurrent startup writes.
- `ShownNotificationIds` is capped at 100 entries and cleared on `RotateInstallId()` alongside other identity-bound state.
- Action URL handling: `gs://settings` opens plugin settings via `OpenPluginSettings(Id)`, `gs://addons` opens the add-ons dialog, `https://` URLs are opened in the browser but only for trusted hosts (`gamescrobbler.com`, `playnite.link`). Plain `http://` and untrusted hosts are rejected.
- Two user-facing settings (`ShowUpdateNotifications`, `ShowImportantNotifications`) control whether update and server notifications appear. Both default to `true` and are synced to `GsData` via `GsPluginSettingsViewModel.EndEdit()` and `LoadExistingSettings()`.

### Pending Scrobble Flush
- Starts and stops are persisted before waiting for HTTP. Per-game gates serialize live requests. Each gate is reference counted and retired only when its last handler releases it, so the map does not grow for the life of the process without a handler that already took a gate losing its exclusion to a fresh one. The semaphore is deliberately never disposed: it owns no unmanaged handle, and disposing it under a handler that had already taken it would surface as an ObjectDisposedException on a live scrobble. Process-local queue claims prevent replay from duplicating live requests. `PeekPendingScrobbles()` blocks only the games that actually hold a claim, not the whole queue: ordering is per game, and cutting the queue at the first claimed item stalled every other game behind one in-flight request. Claims disappear on restart.
- Successful starts atomically attach their session ID to the matching queued finish, stopping at the next start for that game. Accepted starts without a session ID retain the pending marker when no finish exists. Finish completion clears only a matching active session. Failed queue writes roll back local transitions.
- Shutdown persists all active and pending-start finishes in one identity-checked write before the first network await.
- `_flushInFlight` Interlocked guard prevents concurrent flush invocations (circuit recovery + periodic timer + startup can overlap).
- Failed items stay queued with an incremented `FlushAttempts` counter. A blocked circuit consumes no attempt; an expired open circuit permits a recovery probe. A failed send stops the pass, and dropping a start after five attempts also drops its paired finish before the next same-game start.
- A periodic 5-minute timer (`_pendingFlushTimer`) retries queued scrobbles independently of circuit breaker recovery. Disposed in `Dispose()`.

### Startup Flow
- Plugin refresh (`RefreshAllowedPluginsAsync`) and update check (`CheckForUpdateAsync`) run in parallel via `Task.WhenAll` — they are independent network calls.
- Pending scrobble flush is fire-and-forget so library sync starts immediately; the periodic timer catches remaining items.
- First-run detection: when `LastSyncAt` is null and `InstallToken` is empty, progress notifications guide the user through initial setup.
- `startup_completed` PostHog event captures elapsed time and sync result for startup performance tracking.

### Allowed Library Sources
- `GsAllowedPlugins.IsAllowed(Game)` is the single predicate for deciding whether a Playnite game can sync, scrobble, or contribute achievement counts.
- Official Playnite library plugin GUIDs are accepted directly after `RefreshAllowedPluginsAsync` loads the backend allowlist.
- OSS/forked library plugins can still sync when `Game.Source.Name` contains a recognized supported source fragment such as `GOG OSS`, `Legendary`, `Epic Games`, or `Amazon Games`.
- `Guid.Empty` plugin IDs are always rejected. They represent manual/custom games and should not be sent.
- Keep DTOs source-aware: send the raw `plugin_id` plus `source_name = g.Source?.Name`; the backend canonicalizes recognized fork GUIDs to official plugin IDs before writing rows.

### Sidebar Dashboard
- `MySidebarView(IGsApiClient apiClient, string userDataFolder = null)`. Plugin version and flags are sent server-side via the dashboard token POST body. `userDataFolder` is a private WebView2 profile inside the plugin's data folder; the default profile is derived from the host process and shared with every other extension hosting a WebView2, which would expose the dashboard's authenticated cookies and the `access_token` in its URL history. If that profile cannot be created the view fails closed and shows an error instead of silently using the shared one.
- Dashboard URL passes only `theme` as a query param (cosmetic, needed for instant rendering); all other context is tamper-proof via the token.
- Auto-refreshes the dashboard token when the sidebar becomes visible after 8+ minutes (tokens have a 10-minute TTL).
- Handles `gs:refresh-token` postMessage from the frontend for manual retry when the session expires.

### Theme Integration (Desktop & Fullscreen)
- The dashboard is exposed as a theme-embeddable custom element so it works in **Fullscreen mode**, which has no sidebar. The sidebar (`GetSidebarItems`) and the Extensions menu (`GetMainMenuItems`) are Desktop-only surfaces.
- Registered in the `GsPlugin` constructor via `AddCustomElementSupport(SourceName = "GameScrobbler", ElementList = ["Dashboard"])`. Theme developers embed it with `<ContentControl x:Name="GameScrobbler_Dashboard" />` in either a Desktop or Fullscreen theme.
- `GsPlugin.GetGameViewControl(GetGameViewControlArgs)` returns a fresh `MySidebarView(_apiClient)` when `args.Name == "Dashboard"`; returns `null` for unknown names or when opted out. `args.Mode` distinguishes `Desktop`/`Fullscreen` if mode-specific controls are ever needed. The same WebView2 dashboard is reused for all surfaces.
- The returned `MySidebarView` self-disposes on `Unloaded`, so Playnite creating/destroying the control on theme reloads or view changes is safe.

### Achievement Provider Architecture
Achievement data comes from two optional addons via an aggregator pattern:
- `IAchievementProvider` — common interface (`GetCounts`, `GetAchievements`, `IsInstalled`)
- `IReliableAchievementProvider.ReadAchievements` reports successful reads (including confirmed empty results) separately from unavailable data. The aggregator preserves failures from the preferred provider rather than substituting a possibly staler fallback. The two sync paths then differ deliberately: **full** sync aborts, because it replaces the server-side baseline wholesale and a snapshot missing a game it could not read would delete that game's achievements; **diff** sync records the unreadable game and keeps scanning, marking it current so nothing can delete achievements the plugin merely failed to read, then defers the whole upload. Deferring is required, not optional: the result hash is built from the games that were readable, so uploading it would hand the server a baseline describing a snapshot it does not have and the next diff would fail hash validation. Deferring costs one sync cycle; a partial diff costs a forced full sync.
- `AchievementProviderBase` — abstract base both providers derive from. Owns the plugin `Guid`, `IsPluginLoaded`, `IsInstalled` (delegating to an abstract `HasLocalData`), the single `GetVersion()` implementation, and the `SafeRead`/`SafeReadValue` wrappers that log and return null on failure. Subclasses contain only their real read logic. `LogPrefix` is derived from `GetType().Name`, so each provider keeps its own log prefix without duplicating the literal.
- A full achievement sync with nothing to send commits an empty baseline locally, without a network round trip, only on an install that has never synced achievements. With a prior baseline "empty" means the tracked games went away, so the empty snapshot has to be uploaded and acknowledged; committing locally would leave the server serving achievements it was never told to drop.
- `GsSuccessStoryHelper` — reads SuccessStory's per-game JSON files from `{ExtensionsDataPath}/{pluginGuid}/SuccessStory/{gameId}.json` (priority 1)
- `GsPlayniteAchievementsHelper` — reads Playnite Achievements' SQLite database at `{ExtensionsDataPath}/{pluginGuid}/achievement_cache.db` via `System.Data.SQLite` in read-only mode (priority 2)
- `GsAchievementAggregator` — iterates providers in order; first with data wins. Skips `(0, 0)` results to allow fallback.
- `PluginVersionHelper` — reads version from `extension.yaml` next to the plugin DLL; shared by both providers for `GetVersion()`.
- `IsInstalled` checks data directory/file existence on disk, not plugin presence in `_api.Addons.Plugins`.
- `System.Data.SQLite.Core` NuGet package ships native `SQLite.Interop.dll` (x86/x64) via build targets.

### Settings UI & Localization
- All user-facing strings are localized via XAML resource dictionaries in `Localization/` and accessed from C# via `GsLocalization.Get()`/`Format()` in `Infrastructure/GsLocalization.cs`.
- Playnite auto-discovers locale files by naming convention (`Localization/{locale}.xaml`). The `en_US.xaml` is the fallback; locale-specific files override it.
- Supported locales: `en_US` (English, default), `ru_RU` (Russian), `pt_BR` (Portuguese), `de_DE` (German), `fr_FR` (French), `zh_CN` (Chinese Simplified), `hi_IN` (Hindi).
- All locale files must have the same set of keys (currently 126). When adding a new key, add it to **all 7 files**. `LocalizationKeyParityTests` enforces this: it fails with the missing/extra key names per locale, and also rejects duplicate keys within a file.
- `GsLocalization.Get(key, fallback)` looks up from `Application.Current.Resources`; returns the fallback when no WPF app is running (e.g., in tests). `GsLocalization.Format(key, fallback, args)` wraps `string.Format()` on the resolved template.
- For format strings with English pluralization (e.g., elapsed time), the code-behind fallback uses inline plural logic so tests see `"5 minutes ago"` while the XAML template is used at runtime for non-English locales (e.g., `"{0} мин. назад"`).
- Settings view uses localized strings from `Localization/en_US.xaml` resource dictionary, organized into card-based sections.
- `GsDataManager.DiagnosticsStateChanged` event fires (outside the lock) when install-token or pending-scrobble state changes; the settings UI subscribes for live status updates.
- `GsPluginSettingsViewModel` exposes diagnostic properties: `IsInstallTokenActive`, `PendingScrobbleCount`, `HasPendingScrobbles`.

### Test Project
- **GsPlugin.Tests/** — xUnit test project (SDK-style .csproj, net462)
- Test classes: `AccountLinkingConcurrencyTests`, `AccountLinkingResponseTests`, `AchievementItemTests`, `AssemblyResolveDriftTests`, `CultureInvarianceTests`, `ExpectedLinkingRejectionTests`, `GsAchievementAggregatorTests`, `GsAllowedPluginsTests`, `GsApiClientHttpTests`, `GsApiClientValidationTests`, `GsAssemblyIdentityTests`, `GsAtomicFileRetryTests`, `GsCircuitBreakerTests`, `GsDataManagerTests`, `GsDataRecoveryTests`, `GsDataTests`, `GsFlushAndPairingTests`, `GsMetadataHashTests`, `GsPendingScrobbleStateTests`, `GsPluginSettingsViewModelTests`, `GsScrobblingServiceHashTests`, `GsScrobblingServiceReliabilityTests`, `GsSelfContainedFinishTests`, `GsSyncHashIndexTests`, `GsTelemetryTests`, `GsTimeTests`, `GsV4ChunkedSyncClientTests`, `HashContractTests`, `LinkingResultTests`, `LocalizationKeyParityTests`, `PlayniteAchievementsSqliteTests`, `ScrobbleStartFailureTests`, `SuccessStoryFileReaderTests`, `ValidateTokenTests`. Class names, not file names: `AchievementProviderTests.cs` holds `AchievementItemTests` and `GsAchievementAggregatorTests`, `GsSyncHashIndexTests.cs` holds both `GsSyncHashIndexTests` (migration, fingerprint, clearing) and `GsV4ChunkedSyncClientTests` (the v4 begin/chunk/commit/abort driver and its failure rendering), and `AccountLinkingResponseTests.cs` holds both `AccountLinkingResponseTests` and `ExpectedLinkingRejectionTests`.
- **Parallelization is already disabled** assembly-wide (`[assembly: CollectionBehavior(DisableTestParallelization = true)]` in `AssemblyInfo.cs`), and the classes that mutate the static singletons additionally share the `StaticManagerTests` collection (`DisableParallelization = true`). Tests run sequentially, so a flake is not a race between collections.
- A flake that lands on a *different, unrelated* test each full run, and passes when that test is run alone, is environmental rather than ordering: the suite's thousands of real disk writes mean any assertion about persisted state can lose a race with an antivirus or indexer scan. That was the cause of exactly this symptom, fixed by widening the `GsAtomicFile` retry budget. Do not chase it by relaxing assertions.
- To hunt a rare one, drive the real code out of `bin/Release` by reflection in a standalone loop of thousands of iterations. A full-suite loop cannot find a 1-in-1000 event in reasonable time: 15 consecutive full runs stayed green while the underlying defect was still present.
- `TempPluginDir` is the shared temp-directory fixture (`Create`, `CreateWithDataManager`, `CreateWithHashIndex`, `CreateWithDataManagerAndHashIndex`). Use it instead of hand-rolling `Path.GetTempPath()` setup so directories are always cleaned up. The factory variants exist because `GsDataManager` and `GsSyncHashIndex` are static singletons and several tests deliberately depend on ambient identity generation rather than re-initializing.
- `GsDataManagerTests` and `GsDataTests` include coverage for install-token persistence, `IdentityGeneration`, `RotateInstallId()`, `SetInstallTokenIfActive()`, `InstallIdForBody`, opt-out token clearing, and `RecordShownNotifications()`/`GetShownNotificationIds()` thread-safe notification state.

## Build Environment

- Targets .NET Framework 4.6.2 (old-style .csproj — requires Visual Studio MSBuild, not `dotnet build`)
- XAML code-gen (WPF `PresentationBuildTasks`) requires the full `MSBuild.exe` from VS Build Tools or a full VS install; `dotnet msbuild` does **not** generate `.g.cs` files for old-style WPF projects, so View code-behind will fail to compile without it
- Test project uses SDK-style .csproj and can be built/run with `dotnet test`
- API endpoints: All builds (Debug and Release) use the production URL `api.gamescrobbler.com`
- When upgrading NuGet packages, only upgrade to versions that explicitly ship a `net462` (or `net461`/`net45`) lib folder. Do not rely on netstandard2.0 fallbacks for core runtime packages.
- `dotnet format` runs through `scripts/format-sdk.ps1`, which requires a **GA** .NET 10 SDK. Roslyn's .NET Framework build host resolves MSBuild through MSBuildLocator, which picks the highest-versioned VS instance; older or prerelease SDKs fail there with a `TypeInitializationException` on `Microsoft.Build.Shared.XMakeElements`. The script's `major >= 10` check passes for a `10.0.100-rc` that still fails, so confirm the SDK is GA before blaming the repo.
- If VS-hosted MSBuild fails in several unrelated ways at once (`MSB4276` for `Microsoft.NET.Sdk`, `CS0012` netstandard errors from the WPF temp project), check `vswhere -all -prerelease -format json` for `isComplete: false` first. An interrupted VS install leaves packages marked selected with their files missing, which is enough for MSBuildLocator to select that instance and not enough for it to work. `vs_installer repair` is the fix; adding the missing component is a no-op because it is already selected.
- **Clean the Release output before packing.** `MSBuild -t:Clean` only removes outputs of the current configuration, so a `net10.0-windows` directory left in `bin/Release` by Playnite 11 branch work survives it, and `Toolbox.exe pack` puts the whole thing in the `.pext`: a second `GsPlugin.dll` of a different version and target framework, plus `extension.toml` and `.ftl` locale files. It added 4.67 MB to a 7.74 MB package when this was last hit. CI never sees it because it checks out fresh.

## Important Notes

### Thread-Safe Data Mutations
- Use `GsDataManager.MutateAndSave(d => { ... })` instead of directly modifying `GsDataManager.Data` fields followed by `GsDataManager.Save()`. The `MutateAndSave` method acquires the lock, executes the action, and persists atomically — preventing concurrent threads from interleaving mutations.
- Direct field access via `GsDataManager.Data` is still available for reads, but all write-then-save sequences should use `MutateAndSave`. Identity-bound asynchronous completions use `TryMutateIfActiveIdentity`; its persistence failure restores the prior state.
- Initialization retries transient read failures (contended handles only, since a `JsonException` reparses identical bytes and is therefore not retried) and re-attempts `.tmp` recovery on each attempt. It throws if existing data remains unreadable. Only a missing file creates a fresh installation; an unreadable one is never overwritten, because consent lives in it.
- `GsPlugin`'s constructor catches that throw and enters an inert mode (`_dataUnavailable`): the extension still constructs, every event handler and UI entry point returns early, no services are built, nothing is sent, and a Playnite notification names the file so the user can repair or move it. Letting the exception escape the constructor left the extension unloadable, with settings, opt-out and "Delete My Data" all unreachable.
- `GsDataManager.IsActiveIdentity(installId, generation)` is the single definition of "is this response still for the live install". `TryMutateIfActiveIdentity`, `QueueSessionFinishesAndClearActive`, the scrobbling service's post-await rechecks and the account-linking service all resolve to it; do not re-derive the predicate.
- Saves are buffered by default. `SaveInternal(durable: true)` forces a physical disk flush and is reserved for state replay cannot rebuild: opt-out, opt-in, install-token storage, install-ID rotation, fresh-install creation, and `CompletePendingStart`. That last one qualifies because it removes the queued start and records the active session in its place, and startup replays `PendingScrobbles` without ever reconstructing `ActiveSessionsByGameId`. Routine queue mutations must not pay a hardware commit while holding the process-wide data lock.

### Atomic File Writes
- `GsAtomicFile.WithRetry` gives a transient Windows sharing violation six attempts, backing off 20, 40, 80, 160, 320 ms (~620 ms). Both the temp-file open and the `File.Replace` go through it.
- The budget is not arbitrary and must not be trimmed. At the previous three attempts over 75 ms, driving the shipped `WriteJson` through "a save forced to fail, then the save that has to succeed" lost the race 4 times in 4000 with `Unable to remove the file to be replaced`. `GsAtomicFileRetryTests` pins it: one test asserts a lower bound on elapsed time through the real writer, the rest exercise `WithRetry` against an injected operation.
- Test the retry against an injected operation, never by timing a real file handle. A timed version of that test passed against the old 75 ms policy while taking 600 ms, because which of the two threads wins a given moment is not something a test can schedule.
- Only `IOException` is retried. Anything else is a real fault and must surface on the first attempt.
- The final attempt's exception propagates by design, so `GsDataManager.PersistMutation` can roll the mutation back. Note that `MutateAndSave` and `Save` discard `SaveInternal`'s bool, so a genuinely failed write leaves memory and disk diverged with only a Sentry report. The retry budget narrows that window; it does not close it.

### Sentry Issue Identity
- A `CaptureMessage` string is the issue's identity. Never interpolate a game name, a server-supplied message, an install ID or any other per-user value into it: each distinct value becomes its own Sentry issue, which hides the real rate of the one failure mode. This has been fixed twice, for the scrobble-start path (`ScrobbleStartFailure`) and for account linking (`GsAccountLinkingService.LinkingFailureMessage`).
- Use a constant message plus an explicit `fingerprint`, and put the variable context in `extras` or a breadcrumb.
- An expected outcome is not an issue. A rejection that reflects the user's own state gets a breadcrumb, not a capture: expired/invalid link tokens, and the verify endpoint's 409 "already linked to another account" (`GsAccountLinkingService.IsExpectedLinkingRejection`). That 409 carries no `errorCode`, so `TokenVerificationRes.statusCode` (filled in by the client, `[JsonIgnore]`, never sent by the server) is the only reliable discriminator.

### API DTOs
- All API request/response DTOs live in `Api/Dtos.cs` at namespace level (`GsPlugin.Api`), not nested inside `GsApiClient`. Reference them directly (e.g., `new ScrobbleStartReq { ... }`) — no `GsApiClient.` prefix needed.

### Code Formatting
All code must be formatted before commits, via `powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1`. Do not invoke `dotnet format` directly; see **Build & Development Commands** for why the bare command cannot load this solution. The pre-commit hook runs the same script over staged `.cs` files and fails if anything is unformatted.

### Git Hooks
Hook scripts in `hooks/` are installed to `.git/hooks/` via `scripts/setup-hooks.ps1`:
- **pre-commit**: Verifies code formatting on staged `.cs` files
- **commit-msg**: Validates conventional commit message format (`feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert`)

**Never use `--no-verify` when pushing or committing.** Git hooks enforce formatting and commit message standards; bypassing them is not allowed.

### Playnite Plugin Hosting Constraints
- Playnite loads plugins in its own AppDomain and **ignores plugin-level `app.config` binding redirects**. Assembly version mismatches must be resolved at runtime via the `AppDomain.CurrentDomain.AssemblyResolve` handler in `GsPlugin`'s static constructor.
- When upgrading a NuGet package version, the plugin's dependencies (e.g., Sentry) may still reference the old assembly version. The `AssemblyResolve` handler in `GsPlugin.cs` resolves only DLLs shipped in the plugin output directory and refuses Playnite assemblies; never broaden it into a process-wide arbitrary loader because all extensions share the AppDomain.
- `GsAssemblyIdentity.CanServe` decides that handler's version policy: same major is served, across a major only the identities in `KnownCrossMajorReferences` (what our own package set declares, e.g. Sentry 6.1.0 asking for System.Text.Json 8.0.0.5 against the 9.0.0.9 we ship). Refusing those is what made 2.8.3 unloadable. `ResolveEventArgs.RequestingAssembly` cannot be used to tell our own dependency apart from a foreign one: it is null for every bind that matters, verified against the real DLLs. `AssemblyResolveDriftTests` reads the actual build output, so a package upgrade that introduces new skew fails the tests instead of shipping an extension that cannot load; when it fails, add the identity it names or realign the package versions.
- After building, the extension folder in `%APPDATA%\Playnite\Extensions\<plugin-guid>\` must contain the updated DLLs. Stale DLLs from a previous version will cause `FileNotFoundException` at runtime.
- `GsSentry` methods (`CaptureException`, `CaptureMessage`, `AddBreadcrumb`) use `GsDataManager.DataOrNull` instead of `GsDataManager.Data` to avoid a circular crash when called during `GsDataManager.Initialize()` before `_data` is assigned.
- All `SentrySdk` calls are wrapped in try/catch so the plugin continues working if the Sentry SDK is unavailable (e.g., expired account). `GsApiClient` similarly falls back to a plain `HttpClient` if `SentryHttpMessageHandler` throws.
- `MaxBreadcrumbs` is capped at 50 (default 100) to reduce per-session memory overhead.
- Continuous profiling and failed-request capture are disabled to avoid background-worker shutdown hangs and cross-extension `HttpStatusCodeRange` version conflicts. Trace sampling is 10% when telemetry is enabled.
- Telemetry starts after saved preferences are loaded. Settings save, deletion, and opt-in reconcile SDK lifetimes. Consent handlers check every outgoing batch, including automatic sessions; a retired SDK lifetime stays revoked even after preferences are re-enabled.
- `GsSentry.EnsureGlobalExceptionHandlers()` installs the `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers unconditionally from `ApplyPreferences()`, *before* the consent branch. They are crash-safety, not telemetry (the unobserved-task handler calls `SetObserved()` on plugin-origin faults, and only those: the event is process-wide, so observing another extension's fault would override whatever escalation its owner configured), so a privacy preference must not decide whether they exist. The consent gate still decides whether anything is reported.
- Plugin disposal calls `GsSentry.Shutdown()`, which disposes only its owned initialization handle with a two-second shutdown timeout. It does not globally flush or close another addon's newer hub. `GsSentry.ReleaseGlobalExceptionHandlers()` detaches the handlers, and only disposal calls it.
- `scripts/resolve-legacy-sentry-issues.ps1` is the maintenance helper for resolving legacy Sentry issues; use `-WhatIf` before applying changes.

### Playnite SDK Type Gotchas
- `Game.Playtime` and `Game.PlayCount` are `ulong` — cast explicitly to `long`/`int` when assigning to DTO fields (no implicit conversion).
- `Game.CompletionStatusId` defaults to `Guid.Empty` (not `null`) when unset — guard with `g.CompletionStatusId != Guid.Empty` before calling `.ToString()`.
- `Game.CompletionStatus` is a user-defined named object (not an enum) with a `.Name` string property; access null-safely (`g.CompletionStatus?.Name`).
- Adding a new `.cs` file requires a `<Compile Include="Folder\FileName.cs" />` entry in `GsPlugin.csproj` (old-style non-SDK project — files are not auto-included). Place files in the appropriate namespace folder (`Api/`, `Services/`, `Models/`, `Infrastructure/`, `View/`).
- New `.cs` files written with LF line endings will fail the formatting check; run `scripts/format-code.ps1` to auto-correct to CRLF.

### Release Highlights (User-Facing Changelog)
- Playnite shows users the `Changelog` entries from `installer_manifest.yaml`, not CHANGELOG.md. CHANGELOG.md stays technical (for developers); the Playnite-facing text is curated separately.
- `.github/workflows/release-highlights.yml` runs on release-please PR branches (`release-please--*`): `scripts/generate-release-highlights.ps1` calls the Anthropic API (repo secret `ANTHROPIC_API_KEY`, model `claude-sonnet-5`) with the commits and diff since the last release tag, and inserts a `### Highlights` section under the new version heading in CHANGELOG.md, committed back to the release PR.
- Highlights are reviewed/edited in the release PR like any other change — edit the bullets there before merging to change what users see in Playnite.
- The script is idempotent (no-ops when `### Highlights` already exists for the version, which also breaks the push→synchronize workflow loop) and best-effort: missing API key, API failure, or bad output warns and exits 0 so the release PR is never blocked.
- `scripts/update-installer-manifest.ps1` prefers `### Highlights` bullets for the manifest; when absent it falls back to the raw Features/Bug Fixes bullets.

### Sentry Release Management
- Runtime: Plugin reports version as `GsPlugin@X.Y.Z` from AssemblyInfo
- CI/CD: GitHub Actions creates Sentry releases, uploads portable PDB files (`--type=portablepdb`), and associates commits
- release-please keeps versions synchronized across `AssemblyInfo.cs`, `extension.yaml`, and manifests
- Only runs when release-please creates a GitHub release (conditional on `${{ steps.release.outputs.release_created }}`)
