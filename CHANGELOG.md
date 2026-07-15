# Changelog

## [2.7.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.6.0...GsPlugin-v2.7.0) (2026-07-15)


### Features

* **settings:** rebrand new dashboard toggle as beta channel ([b18ca26](https://github.com/game-scrobbler/gs-playnite/commit/b18ca2661441d73538b7d23e61abb63f9574238b))
* **sync:** replace fat snapshots with chunked hash-index sync ([0b98019](https://github.com/game-scrobbler/gs-playnite/commit/0b9801923663908f1959050b1a16ba877d504b65))
* **sync:** split v4 full-sync chunks by payload byte size ([3d5633e](https://github.com/game-scrobbler/gs-playnite/commit/3d5633e4aa3fde53f919e8a07d6a52f4366adaa5))


### Bug Fixes

* **telemetry:** harden Sentry lifecycle in Playnite's shared process ([0cd6dbb](https://github.com/game-scrobbler/gs-playnite/commit/0cd6dbb76d48993f99ba2da93cbd5d592f004ede))

## [2.6.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.5.0...GsPlugin-v2.6.0) (2026-07-11)


### Highlights

* Games from launchers like GOG OSS, Legendary, and Amazon Games now count toward your stats instead of being skipped.
* Playing two games at once? Each one's playtime is now tracked correctly.
* Safer account linking: Playnite now asks for your confirmation before connecting to a link you opened in the browser.
* Turning scrobbling back on after opting out now works properly again.
* Your library and achievements stay up to date more dependably, even when your connection hiccups.


### Features

* **observability:** report sync, flush and token-parse failures to Sentry ([57531e3](https://github.com/game-scrobbler/gs-playnite/commit/57531e3200721afad9dcbdb3fac4bdb4368f7762))
* **playnite:** allow source-aware library plugins ([7130ba2](https://github.com/game-scrobbler/gs-playnite/commit/7130ba29585ceaf1cc48eaf897c7da8a0ce89882))
* **scrobbling:** track multiple concurrent game sessions per game ID ([67543d0](https://github.com/game-scrobbler/gs-playnite/commit/67543d04d7f951068af6696e5b4866fc7d451de0))
* **sync:** migrate library sync to the slim v3 DTO and endpoints ([ea24584](https://github.com/game-scrobbler/gs-playnite/commit/ea24584b8cb0e01bfc99b75a0a1fb5707d27431d))
* **sync:** send result_snapshot_hash on achievement diff sync ([aa9fe0a](https://github.com/game-scrobbler/gs-playnite/commit/aa9fe0a3f3130d68e477c992fb86e85ae9b842be))
* **sync:** send result_snapshot_hash on v3 diff sync ([0b5f92a](https://github.com/game-scrobbler/gs-playnite/commit/0b5f92a50ad9aaa8660710fbf4e33582119f9b13))
* wire OptBackIn to server-side /v2/opt-in endpoint ([8f8a959](https://github.com/game-scrobbler/gs-playnite/commit/8f8a9596a1cb9f3bc8b7157a10dcbe3fa6752b85))


### Bug Fixes

* **api:** don't overwrite process-wide TLS SecurityProtocol setting ([d67eb0d](https://github.com/game-scrobbler/gs-playnite/commit/d67eb0d4af55923823e545cf21165d2f17de277f))
* **data:** increment PendingScrobble.FlushAttempts under GsDataManager lock ([a58e268](https://github.com/game-scrobbler/gs-playnite/commit/a58e2684164d615cb09e7421effe994dc235ca1c))
* **data:** retry GsData save on transient IOException ([43a373d](https://github.com/game-scrobbler/gs-playnite/commit/43a373d2194d061514094197586fb36ba0f933a1))
* **security:** require confirmation before deep-link account linking ([da64c0d](https://github.com/game-scrobbler/gs-playnite/commit/da64c0d1beee96a3d088134fdf51e51cb1afa5a2))
* **startup:** prevent orphaned flush timer when Dispose races startup ([dab7ca8](https://github.com/game-scrobbler/gs-playnite/commit/dab7ca803127e5df5d2b7a3068b47f482267f311))
* stop sending "queued" string as session_id in async scrobble start ([f03f764](https://github.com/game-scrobbler/gs-playnite/commit/f03f764c2e88f38dde119a23c4046383ee61ab7e))
* store null instead of \"queued\" as session_id in pending finish scrobbles ([e6c7129](https://github.com/game-scrobbler/gs-playnite/commit/e6c71296cf0d7100cbde6058c56cb15659a33f40))
* **sync:** apply library snapshot diff before committing hash baseline ([7beb594](https://github.com/game-scrobbler/gs-playnite/commit/7beb594fa24ebb2c19f5f2933e011a835716d0e2))
* **tests:** implement RequestOptIn in MockGsApiClient ([766047f](https://github.com/game-scrobbler/gs-playnite/commit/766047f541342e0cd2dcc2550068d68ca9213130))

## [2.5.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.4.0...GsPlugin-v2.5.0) (2026-03-27)


### Features

* add localization for pt-BR, de-DE, fr-FR, zh-CN, hi-IN ([563f67b](https://github.com/game-scrobbler/gs-playnite/commit/563f67b8d95486b2a48e39121c6215be4ddc29bd))
* add localization infrastructure and linking strings ([1d24ef1](https://github.com/game-scrobbler/gs-playnite/commit/1d24ef1652ce389618b623ef3de8ae31c7a14dc1))
* add Russian localization (thanks to [[@god](https://github.com/god)OFslaves](https://github.com/godOFslaves)) ([544f538](https://github.com/game-scrobbler/gs-playnite/commit/544f538552fe00d16f790c41b8b3cc077f7f6e12))
* improve token expiry UX and add account disconnect ([f01d8a5](https://github.com/game-scrobbler/gs-playnite/commit/f01d8a5df565d345fdbb68f7b51d3a9ee09de4ec))
* localize all remaining hardcoded UI strings for Russian support ([962284b](https://github.com/game-scrobbler/gs-playnite/commit/962284b0c0eb14091a45e8608457222eb0dcabc5))


### Bug Fixes

* clean up leading spaces in Russian localization ([8378ee1](https://github.com/game-scrobbler/gs-playnite/commit/8378ee1f4f5299cca85c5cd76ea43433d61cf7c2))

## [2.4.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.3.3...GsPlugin-v2.4.0) (2026-03-25)


### Features

* add diagnostic logging for achievement provider resolution ([c97ee7c](https://github.com/game-scrobbler/gs-playnite/commit/c97ee7c55da2e3e04fd11902b3967664afb6431e))


### Bug Fixes

* enforce thread-safe mutations and bound retry recursion ([16b0a3a](https://github.com/game-scrobbler/gs-playnite/commit/16b0a3a879f35be1b76d0e4a923b479d41884d0a))
* improve token expiry error with actionable guidance and downgrade Sentry level ([9f7924c](https://github.com/game-scrobbler/gs-playnite/commit/9f7924ca007086f31e32e0168ff9173b41f445ec))
* make circuit breaker detect HTTP server errors ([cc654c5](https://github.com/game-scrobbler/gs-playnite/commit/cc654c545639c91bba1d4d7551bf981955796bab))
* restrict WebView external URLs and surface dropped scrobbles ([c03b7cc](https://github.com/game-scrobbler/gs-playnite/commit/c03b7cc41f313887f2767382e8e0cd12a4587513))
* use atomic file writes for persistent state ([c2eb8df](https://github.com/game-scrobbler/gs-playnite/commit/c2eb8df9c170c38b7979155d111405ab24db9ee6))

## [2.3.3](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.3.2...GsPlugin-v2.3.3) (2026-03-24)


### Bug Fixes

* inherit Playnite theme TextBox style to fix non-editable token input ([c5cbf67](https://github.com/game-scrobbler/gs-playnite/commit/c5cbf6747e5abefae1ed4563477da3676a6a499d))

## [2.3.2](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.3.1...GsPlugin-v2.3.2) (2026-03-24)


### Bug Fixes

* parse verify error responses instead of treating 400 as network error ([02646ad](https://github.com/game-scrobbler/gs-playnite/commit/02646ad34748ea113b26a856bd82aeaf021e8823))

## [2.3.1](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.3.0...GsPlugin-v2.3.1) (2026-03-24)


### Bug Fixes

* address high-priority Sentry errors and fix CI for windows-2022 ([bbf3e51](https://github.com/game-scrobbler/gs-playnite/commit/bbf3e51d462bb55143a78147e4f5489fb88c4889))

## [2.3.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.2.0...GsPlugin-v2.3.0) (2026-03-21)


### Features

* add opt-out and delete my data support ([88a04ef](https://github.com/game-scrobbler/gs-playnite/commit/88a04efdaacd8592cd139943716b02d64d703992))
* add opt-out, delete my data, and fix update notification ([d559a6f](https://github.com/game-scrobbler/gs-playnite/commit/d559a6f94021f3dc2a49e7076220bb076c24090e))
* add per-install token authentication and identity management ([a75cb9e](https://github.com/game-scrobbler/gs-playnite/commit/a75cb9ec280d40489108ceaecd4bce980cde2273))
* add server notification system with settings controls ([9b519c7](https://github.com/game-scrobbler/gs-playnite/commit/9b519c705f30730c3eb6ce8141638e9a0f8bb35c))
* add sidebar dashboard auto-refresh with token expiry detection ([a17ab59](https://github.com/game-scrobbler/gs-playnite/commit/a17ab59598c77af85497564ccec70ad6573cc94c))
* crash-safe pending scrobble flush with periodic retry and startup improvements ([8a20107](https://github.com/game-scrobbler/gs-playnite/commit/8a20107ce3cb37b8de50adddf4079a18cb40371e))
* overhaul settings view with localized sections and diagnostics ([52281b9](https://github.com/game-scrobbler/gs-playnite/commit/52281b91320f8c06db2978223e31a336d430b340))


### Bug Fixes

* add MutateAndSave for atomic data mutations and library sync guard ([715058c](https://github.com/game-scrobbler/gs-playnite/commit/715058ca8bff727202c55cfbe2ea88d8d592b444))
* add System.Net.Http reference to test project for HttpClient tests ([db89bb3](https://github.com/game-scrobbler/gs-playnite/commit/db89bb35e82e016e4b9be8c508ed01c08d67d17f))
* harden achievement providers and infrastructure resilience ([b56ed1c](https://github.com/game-scrobbler/gs-playnite/commit/b56ed1c4c15841dbd7def401715530329775cd7f))
* harden WebView2 security and remove dead code ([cb415ee](https://github.com/game-scrobbler/gs-playnite/commit/cb415ee64bf454c34eccd95c4661ea6c87293a8e))
* open Add-ons dialog instead of plugin settings on update notification click ([05d9889](https://github.com/game-scrobbler/gs-playnite/commit/05d98895357cd7d78b9d280d0e56d5058d17bc18))
* remove missing package urls and add changelogs to installer manifest ([bfb4b20](https://github.com/game-scrobbler/gs-playnite/commit/bfb4b200007641e3501d478ed601fa478d77a457))
* revert debug API URL and restore permissive pre-commit hook ([4be29b2](https://github.com/game-scrobbler/gs-playnite/commit/4be29b2ecf692a22a533c2ff050793d5a90fc41e))
* use theme-aware TextBrush for library sync status text ([77b9378](https://github.com/game-scrobbler/gs-playnite/commit/77b9378ea2596c024782443dd90d91096e7e0a12))


### Performance Improvements

* cache reflection lookups and replace debug MessageBox with logging ([86e5fe2](https://github.com/game-scrobbler/gs-playnite/commit/86e5fe28406e1b9841ded51ccf19dc22b448b6be))

## [2.2.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.1.1...GsPlugin-v2.2.0) (2026-03-10)


### Features

* add PostHog analytics alongside Sentry ([82b0090](https://github.com/game-scrobbler/gs-playnite/commit/82b00902e901ce194828f4e2a1c1b39420571d6a))
* embed YouTube video in README.md ([748fb2d](https://github.com/game-scrobbler/gs-playnite/commit/748fb2dab6a5c3e9e152f9db8d18f7dde5d12149))
* send integration account identities during library sync ([43bdf5a](https://github.com/game-scrobbler/gs-playnite/commit/43bdf5a5c54742b9b9c472207efa3c42ecd12be8))


### Bug Fixes

* deduplicate achievements by name in diff sync path ([043f163](https://github.com/game-scrobbler/gs-playnite/commit/043f163f9f2e8690db8430b0c15d7926ccabd0fe))
* deduplicate achievements by name in full sync path ([a9ebc48](https://github.com/game-scrobbler/gs-playnite/commit/a9ebc487a0f52d828d84a701c42c5ed5ec6eeff3))
* improve Sentry exception filtering and handle missing data directory ([45ab138](https://github.com/game-scrobbler/gs-playnite/commit/45ab1384d60130ca99b9d1e50e785473959305db))
* wrap all SentrySdk calls in try/catch for graceful fallback ([063aa19](https://github.com/game-scrobbler/gs-playnite/commit/063aa19d00eaa4f06ef7b81724cf120ce3776527))

## [2.1.1](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.1.0...GsPlugin-v2.1.1) (2026-02-24)


### Bug Fixes

* add click action to update notification to open plugin settings ([f9e8998](https://github.com/game-scrobbler/gs-playnite/commit/f9e899851517bbb5199c091e6dbd201fb7125e3d))

## [2.1.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.0.1...GsPlugin-v2.1.0) (2026-02-24)


### Features

* add Playnite Achievements plugin support alongside SuccessStory ([526b876](https://github.com/game-scrobbler/gs-playnite/commit/526b8761c9e4b92f1507fdccfe1990a6d65ebbc4))

## [2.0.1](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v2.0.0...GsPlugin-v2.0.1) (2026-02-24)


### Bug Fixes

* correct $PSScriptRoot paths in installer manifest script after move to scripts/ ([e1ef7ce](https://github.com/game-scrobbler/gs-playnite/commit/e1ef7cec92e09738e924e62af8ba5f8e0071990a))
* prevent JSON crash on non-JSON responses, add gzip compression, and filter third-party Sentry errors ([344a843](https://github.com/game-scrobbler/gs-playnite/commit/344a8439c1554aa7629ccf43af10c4fd528493ee))

## [2.0.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v1.1.0...GsPlugin-v2.0.0) (2026-02-21)


### ⚠ BREAKING CHANGES

* All public types have moved to sub-namespaces. Code referencing GsPlugin.GsApiClient must now use GsPlugin.Api.GsApiClient, GsPlugin.GsData must use GsPlugin.Models.GsData, etc.

### Features

* add 'Link Account on Website' button to settings UI ([6637801](https://github.com/game-scrobbler/gs-playnite/commit/6637801727dc922657883f5a4398e333db5f5a33))
* add achievement hash tracking, remove legacy v1 sync, and update docs ([c649b54](https://github.com/game-scrobbler/gs-playnite/commit/c649b54c2626d7c326fde43c1d26eb148efc732a))
* add achievement sync via SuccessStory plugin and v2/sync endpoint ([2b44254](https://github.com/game-scrobbler/gs-playnite/commit/2b4425444e1d8bfb94f585972d81258af41a7a3e))
* add diff-based library and achievement sync with snapshot state ([c7d6a6b](https://github.com/game-scrobbler/gs-playnite/commit/c7d6a6b1accef10335009c3f873cb3ab953e98b8))
* add Extensions top-menu with Open Dashboard, Sync Library Now, and Open Settings ([88920fa](https://github.com/game-scrobbler/gs-playnite/commit/88920faa37681f702b03c88cf58c178479a5f929))
* add in-app update notification via GitHub releases API ([7f3de64](https://github.com/game-scrobbler/gs-playnite/commit/7f3de641276c73707597c385db743ccaed38966b))
* add offline queue with retry on circuit breaker recovery ([7cba211](https://github.com/game-scrobbler/gs-playnite/commit/7cba2114db1b557f5754f52a4f7310d6e7cab280))
* extend GameSyncDto with scores, release year, dates, and user flags ([aa7f2ee](https://github.com/game-scrobbler/gs-playnite/commit/aa7f2eee6dc427f8a851a5f958f8da5e5f9e72d9))
* handle sync cooldown from server and enforce client-side guard ([53392bf](https://github.com/game-scrobbler/gs-playnite/commit/53392bff1642e6da29c49e070a720d36491a6457))
* show last sync status in settings UI ([943a342](https://github.com/game-scrobbler/gs-playnite/commit/943a3422bef2aef0e7f4df738fc17a2e6ce9f995))
* show SuccessStory install status in settings UI ([467036a](https://github.com/game-scrobbler/gs-playnite/commit/467036acffde7cabeefd49a1273dfa525cf7c182))


### Bug Fixes

* re-queue failed pending scrobbles, pair orphaned sessions, and resolve static binding ([699ca0c](https://github.com/game-scrobbler/gs-playnite/commit/699ca0c68d5ab9f88251b08ff48590f75d55142e))
* remove duplicate LibraryFullSyncReq_CanBeConstructed test method ([2642e50](https://github.com/game-scrobbler/gs-playnite/commit/2642e50af00ffb3964e82f92e9e34f150c561f52))


### Performance Improvements

* skip sync when library hash is unchanged between sessions ([24dc390](https://github.com/game-scrobbler/gs-playnite/commit/24dc390a9fc1f838bd4a7dad96cb6d6ebdb4fc5e))


### Code Refactoring

* reorganize root files into Api, Services, Models, Infrastructure subfolders ([059c6d5](https://github.com/game-scrobbler/gs-playnite/commit/059c6d572e08c6735949ad66440e870e719b70b8))

## [1.1.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v1.0.0...GsPlugin-v1.1.0) (2026-02-13)


### Features

* add dynamic client-side plugin filtering for library sync and scrobbling ([d58910b](https://github.com/game-scrobbler/gs-playnite/commit/d58910b9522a6597f465a78127d02950f414e581))
* add new dashboard toggle and scrobbling disabled warning to settings UI ([dc6703a](https://github.com/game-scrobbler/gs-playnite/commit/dc6703abe85e9dbaaec70c171b42fb0b0daa8518))

## [1.0.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.11.0...GsPlugin-v1.0.0) (2025-10-25)


### ⚠ BREAKING CHANGES

* This marks the first stable release of GsPlugin with all core features tested and production-ready. The plugin now has stable scrobbling, library sync, and account linking functionality.

### Features

* mark plugin as stable for 1.0.0 release ([b95f93e](https://github.com/game-scrobbler/gs-playnite/commit/b95f93ef241fdc7954dba8f0b4cfeae05c902c83))

## [0.11.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.10.3...GsPlugin-v0.11.0) (2025-10-25)


### ⚠ BREAKING CHANGES

* Library sync now sends all game data as-is without filtering image URLs. Previously attempted to filter out local paths and keep only web URLs, but this caused issues with Playnite UI display and was not needed for the API integration.

### Features

* simplify library sync by removing image URL enrichment ([0ffb20f](https://github.com/game-scrobbler/gs-playnite/commit/0ffb20f486338f3416b666256f5e2c6a45f9b2ed))
* **ui:** update plugin icon and sidebar icon with new brand design ([944659b](https://github.com/game-scrobbler/gs-playnite/commit/944659bedb54c130a28d887fd72d10af2d4ead09))

## [0.10.3](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.10.2...GsPlugin-v0.10.3) (2025-10-24)


### Bug Fixes

* **ci:** improve PowerShell script to handle changelog parsing edge cases ([e4405a6](https://github.com/game-scrobbler/gs-playnite/commit/e4405a6c3685bc2788b11a5cf1e90c9a50665dfa))

## [0.10.2](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.10.1...GsPlugin-v0.10.2) (2025-10-24)


### Bug Fixes

* **ci:** fix YAML generation in update-installer-manifest script ([6e8e36b](https://github.com/game-scrobbler/gs-playnite/commit/6e8e36be7ba53538048773cc6a4d6314a648f543))

## [0.10.1](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.10.0...GsPlugin-v0.10.1) (2025-10-24)


### Bug Fixes

* **ci:** automate installer_manifest.yaml updates and add missing versions ([0c8f750](https://github.com/game-scrobbler/gs-playnite/commit/0c8f7501da61fc23f4647bbe15096a330c3b2441))
* improve performance and error handling across multiple components ([1168593](https://github.com/game-scrobbler/gs-playnite/commit/116859303d2003b3c18d0884e7b8dea9011457df))

## [0.10.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.9.0...GsPlugin-v0.10.0) (2025-10-16)


### Features

* **sentry:** add user control for error reporting and scrobbling tracking ([e055acb](https://github.com/game-scrobbler/gs-playnite/commit/e055acb6b388c61c5eef1915acf6f0505fe1a98c))

## [0.9.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.8.0...GsPlugin-v0.9.0) (2025-10-16)


### Features

* **ci:** integrate Sentry release management in GitHub Actions ([226e7a0](https://github.com/game-scrobbler/gs-playnite/commit/226e7a066b51b206acc658de56399451a2c3c9ce))

## [0.8.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.7.0...GsPlugin-v0.8.0) (2025-10-15)


### Features

* **api:** add support for asynchronous operations ([c50d466](https://github.com/game-scrobbler/gs-playnite/commit/c50d466aa12ebdcbcb9a71155aee1074ad57fc6a))

## [0.7.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.6.2...GsPlugin-v0.7.0) (2025-10-12)


### ⚠ BREAKING CHANGES

* **git-hooks:** Commit messages must now follow the Conventional Commits specification to ensure proper versioning and release notes.

### Features

* **api:** add support for Git operations and API URLs ([8fd2cd9](https://github.com/game-scrobbler/gs-playnite/commit/8fd2cd9ecaff4561b9f95bb522d3d9379a1091db))
* **git-hooks:** enhance hooks and add automated versioning ([257c47e](https://github.com/game-scrobbler/gs-playnite/commit/257c47eeddaf8bf50941b903083ff2e307d18c8c))

## [0.6.2](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.6.1...GsPlugin-v0.6.2) (2025-10-02)


### Bug Fixes

* **dependencies**: Fix System.Text.Json version compatibility with Sentry 5.15.1
  - Downgraded System.Text.Json from 9.0.9 to 6.0.10 to match Sentry requirements
  - Downgraded System.Text.Encodings.Web from 9.0.9 to 6.0.0 for compatibility
  - Updated System.Memory reference from 4.0.5.0 to 4.0.1.2 to match actual package version
  - Fixed FileNotFoundException errors during plugin initialization
  - Updated all assembly binding redirects to match installed package versions

## [0.6.1](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.6.0...GsPlugin-v0.6.1) (2025-09-24)


### Bug Fixes

* Add pre-commit hook for automatic code formatting ([b3ba718](https://github.com/game-scrobbler/gs-playnite/commit/b3ba71825a8677a7889ed0a8a37f1ea787f47d05))
* dump version ([70a636a](https://github.com/game-scrobbler/gs-playnite/commit/70a636ab2dbe957c95e968e6a3eef2ae23998d8f))

## [0.6.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.5.0...GsPlugin-v0.6.0) (2025-09-22)

### 🚀 Major Features

* **Circuit Breaker Pattern**: Implemented advanced fault tolerance with automatic failure detection and recovery
* **Exponential Backoff Retry Logic**: Added intelligent retry mechanism with jitter to prevent thundering herd problems
* **Global Exception Protection**: Enhanced UnobservedTaskException handling to prevent application crashes
* **Enhanced Logging**: Added contextual logging with game IDs, session IDs, and detailed error information

### 🛡️ Reliability Improvements

* **API Input Validation**: Comprehensive null and empty string validation across all API endpoints
* **JSON Deserialization Safety**: Added safe JSON parsing with error recovery and detailed logging
* **Session Management**: Enhanced null safety checks in game session tracking
* **Error Context**: Improved error messages with game context for better debugging

### 📦 Dependencies

* **Sentry**: Updated from 5.1.0 to 5.15.1 for improved error tracking and performance
* **PlayniteSDK**: Updated from 6.11.0 to 6.12.0 for latest platform features
* **System Libraries**: Updated all System.* packages to latest stable versions for better compatibility
* **Microsoft.Web.WebView2**: Updated to 1.0.3485.44 for enhanced web view functionality

### 🔧 Technical Improvements

* **Assembly Binding**: Updated all assembly redirects for latest dependency versions
* **Build Configuration**: Enhanced MSBuild configuration with proper NuGet package management
* **Code Quality**: Added comprehensive code analysis with Microsoft.CodeAnalysis.NetAnalyzers 9.0.0

### 🐛 Bug Fixes

* **UnobservedTaskException**: Fixed crash issues by implementing proper task exception observation
* **Null Reference Exceptions**: Resolved null reference issues in game session handling
* **API Response Parsing**: Fixed argument null exceptions in API response deserialization
* **Memory Leaks**: Improved resource cleanup and disposal patterns

### 📚 Documentation

* **README Updates**: Comprehensive documentation of new reliability features and architecture
* **Code Comments**: Enhanced inline documentation for better maintainability
* **Architecture Diagrams**: Updated project structure to reflect new components

## [0.5.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.4.0...GsPlugin-v0.5.0) (2025-06-12)


### Features

* release 0.5 ([04bbadb](https://github.com/game-scrobbler/gs-playnite/commit/04bbadb5d87354685669b099e15fe30b386b13b1))

## [0.4.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.3.0...GsPlugin-v0.4.0) (2025-02-19)


### Features

* add Privacy Controls to plugin settings ([3e0d8f9](https://github.com/game-scrobbler/gs-playnite/commit/3e0d8f99ceb6d07256f9691401d68d4c27d0182d))
* add ShowNonBlockingNotification for visibility in development ([7228c4c](https://github.com/game-scrobbler/gs-playnite/commit/7228c4cd680c5d413bc3a8216b7d7a2cd1f13abf))
* added dark mode ([952dc26](https://github.com/game-scrobbler/gs-playnite/commit/952dc26634abaa3891ecaee45f1c8aa78eded166))
* updated setting so user can copy their ID ([bdf5a02](https://github.com/game-scrobbler/gs-playnite/commit/bdf5a025798170b2aaf695fc29fe540c0ea87188))


### Bug Fixes

* address concerns of migrating user_id ([8003277](https://github.com/game-scrobbler/gs-playnite/commit/8003277bdfdeda5d84d0527f7e02b36203ff1909))
* set different sentry Environment for development and production ([608de0a](https://github.com/game-scrobbler/gs-playnite/commit/608de0a2c6a62c6bcc9a08a8e3e05169b10f4aca))
* fix wrong session_id model ([d0914c1](https://github.com/game-scrobbler/gs-playnite/commit/d0914c1a88d680d7ee31e95336c06cd7b1108d37))

## [0.3.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.2.0...GsPlugin-v0.3.0) (2025-02-10)


### Features

* add favicon to project ([f5ce14c](https://github.com/game-scrobbler/gs-playnite/commit/f5ce14c6ee26e62d12bb2cbec5efa0e901367d14))
* added sentry logs and ver to the web view link ([c9d3650](https://github.com/game-scrobbler/gs-playnite/commit/c9d365081132933d251c54dd6909fe4566f0cf31))
* added sentry package(not implemented) and resolved conflicts. ([e1e7adc](https://github.com/game-scrobbler/gs-playnite/commit/e1e7adc65e41397bc4db73c27e6a7dbdd0bfb662))


### Bug Fixes

* added sync on app exit ([3131cd0](https://github.com/game-scrobbler/gs-playnite/commit/3131cd0d2b432f79007238a73105542dc48a985b))
* remove extra lines ([eed04ef](https://github.com/game-scrobbler/gs-playnite/commit/eed04efcb94fd155f3fd8864faf99b2c8f8487ae))
* the data is now sync when the game library is updated ([c274b2e](https://github.com/game-scrobbler/gs-playnite/commit/c274b2e8fa28a34af29f9065257ca8185c96dd98))

## [0.2.0](https://github.com/game-scrobbler/gs-playnite/releases/tag/GsPlugin-v0.2.0) (2025-02-06)


### Features

* add permissions ([6f486b3](https://github.com/game-scrobbler/gs-playnite/commit/6f486b37b4a35bd65a5d5374f1ebb86dfe6c07ef))
* api fully implement and GUID ([1b99895](https://github.com/game-scrobbler/gs-playnite/commit/1b99895548f6f0f0ef09fe7723b24aa370620821))
* init commit ([0a002fc](https://github.com/game-scrobbler/gs-playnite/commit/0a002fcd88e799ca70d61ab61d7cf71b9fd187cf))
* update manifests and icon ([cf1199d](https://github.com/game-scrobbler/gs-playnite/commit/cf1199deaa22fa2317396e406b94c6d0335ffa1f))
* updated the Ifarame ([bf610bc](https://github.com/game-scrobbler/gs-playnite/commit/bf610bc0eb403d975aabf37ca488d79c6b556517))


### Bug Fixes

* added one log for onAppStart ([fe1de34](https://github.com/game-scrobbler/gs-playnite/commit/fe1de344d25aa47aed9b64c7a5ee905c5803c9c8))
* change release draft status ([aa82c2d](https://github.com/game-scrobbler/gs-playnite/commit/aa82c2dea8bee719a20fb59193d3320e934635b2))
* changed to Utf8Json ([63afa08](https://github.com/game-scrobbler/gs-playnite/commit/63afa08d883b40b06b9fefb125909c11f39d1a93))
* dummy release for ci fix ([7ab7d58](https://github.com/game-scrobbler/gs-playnite/commit/7ab7d58e6f57e767135c06d200c65eaa99d8cfef))
* release please config typo ([96decf2](https://github.com/game-scrobbler/gs-playnite/commit/96decf2e1c10436a4d40519f134628683a0e2aa0))
* release process of pext file ([65d97a3](https://github.com/game-scrobbler/gs-playnite/commit/65d97a350b4d52935945c097284e342e8b0f449c))
* remove ignored files ([aa8faa1](https://github.com/game-scrobbler/gs-playnite/commit/aa8faa10a426bcfbce62a36c19269781899037ae))
* remove Links from addon manifest ([92a570a](https://github.com/game-scrobbler/gs-playnite/commit/92a570aa1cf09868275149e956759488b6408fbc))
* removed all logs ([60fb3e6](https://github.com/game-scrobbler/gs-playnite/commit/60fb3e63978b0d6ba6305b80d11299a80a784c74))
* start api wrong tag ([91b0c24](https://github.com/game-scrobbler/gs-playnite/commit/91b0c243bbec236ad730ca0604ed8dc3be8bae4c))
* sync api ([4197c40](https://github.com/game-scrobbler/gs-playnite/commit/4197c408d40e5352da62f4645f6e2dd50b9b79d7))
* type of manifest should be Generic not GenericPlugin ([0b5ed04](https://github.com/game-scrobbler/gs-playnite/commit/0b5ed043f8c4d0482f44c7209c425ec981d8e103))
* update gitignore ([ef17cc9](https://github.com/game-scrobbler/gs-playnite/commit/ef17cc9654ceaa31974f6ab9cee252e80843a718))
