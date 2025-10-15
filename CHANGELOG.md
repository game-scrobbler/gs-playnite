# Changelog

## [0.8.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.7.0...GsPlugin-v0.8.0) (2025-10-15)


### Features

* **api:** add support for asynchronous operations ([c50d466](https://github.com/game-scrobbler/gs-playnite/commit/c50d466aa12ebdcbcb9a71155aee1074ad57fc6a))

## [0.7.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.6.2...GsPlugin-v0.7.0) (2025-10-12)


### ⚠ BREAKING CHANGES

* **git-hooks:** Commit messages must now follow the Conventional Commits specification to ensure proper versioning and release notes.

### Features

* **api:** add support for Git operations and API URLs ([8fd2cd9](https://github.com/game-scrobbler/gs-playnite/commit/8fd2cd9ecaff4561b9f95bb522d3d9379a1091db))
* **git-hooks:** enhance hooks and add automated versioning ([257c47e](https://github.com/game-scrobbler/gs-playnite/commit/257c47eeddaf8bf50941b903083ff2e307d18c8c))

## [0.6.2](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.6.2...GsPlugin-v0.6.2) (2025-10-02)


### Features

* add favicon to project ([f5ce14c](https://github.com/game-scrobbler/gs-playnite/commit/f5ce14c6ee26e62d12bb2cbec5efa0e901367d14))
* add permissions ([db7a643](https://github.com/game-scrobbler/gs-playnite/commit/db7a643f6f4890ea8294218ca9be3615394e82aa))
* add permissions ([6f486b3](https://github.com/game-scrobbler/gs-playnite/commit/6f486b37b4a35bd65a5d5374f1ebb86dfe6c07ef))
* add pirvacy controls to plugin settings ([3e0d8f9](https://github.com/game-scrobbler/gs-playnite/commit/3e0d8f99ceb6d07256f9691401d68d4c27d0182d))
* add ShowNonBlockingNotification for visiblity in development ([7228c4c](https://github.com/game-scrobbler/gs-playnite/commit/7228c4cd680c5d413bc3a8216b7d7a2cd1f13abf))
* added dark mode and cleaned the code ([952dc26](https://github.com/game-scrobbler/gs-playnite/commit/952dc26634abaa3891ecaee45f1c8aa78eded166))
* added sentry logs and ver to the web view link ([c9d3650](https://github.com/game-scrobbler/gs-playnite/commit/c9d365081132933d251c54dd6909fe4566f0cf31))
* added sentry package(not implemented) and resolved conflicts. ([e1e7adc](https://github.com/game-scrobbler/gs-playnite/commit/e1e7adc65e41397bc4db73c27e6a7dbdd0bfb662))
* api fully implement and GUID ([1b99895](https://github.com/game-scrobbler/gs-playnite/commit/1b99895548f6f0f0ef09fe7723b24aa370620821))
* init commit ([0a002fc](https://github.com/game-scrobbler/gs-playnite/commit/0a002fcd88e799ca70d61ab61d7cf71b9fd187cf))
* persistence session_id and gamestop on playnite closed ([9a27eaf](https://github.com/game-scrobbler/gs-playnite/commit/9a27eafa2ddefcb6c002406c9d530ba7df842712))
* release 0.5 ([04bbadb](https://github.com/game-scrobbler/gs-playnite/commit/04bbadb5d87354685669b099e15fe30b386b13b1))
* update manifests and icon ([cf1199d](https://github.com/game-scrobbler/gs-playnite/commit/cf1199deaa22fa2317396e406b94c6d0335ffa1f))
* updated setting so user can copy their ID ([bdf5a02](https://github.com/game-scrobbler/gs-playnite/commit/bdf5a025798170b2aaf695fc29fe540c0ea87188))
* updated the Ifarame ([bf610bc](https://github.com/game-scrobbler/gs-playnite/commit/bf610bc0eb403d975aabf37ca488d79c6b556517))
* v0.6.0 - Major reliability and robustness improvements ([de6ad19](https://github.com/game-scrobbler/gs-playnite/commit/de6ad1918a1ef37442b4599354acb180dbb206ad))


### Bug Fixes

* Add pre-commit hook for automatic code formatting ([b3ba718](https://github.com/game-scrobbler/gs-playnite/commit/b3ba71825a8677a7889ed0a8a37f1ea787f47d05))
* added one log for onAppStart ([fe1de34](https://github.com/game-scrobbler/gs-playnite/commit/fe1de344d25aa47aed9b64c7a5ee905c5803c9c8))
* added sync on app exit ([3131cd0](https://github.com/game-scrobbler/gs-playnite/commit/3131cd0d2b432f79007238a73105542dc48a985b))
* address concerns of migrating user_id ([8003277](https://github.com/game-scrobbler/gs-playnite/commit/8003277bdfdeda5d84d0527f7e02b36203ff1909))
* change release draft status ([aa82c2d](https://github.com/game-scrobbler/gs-playnite/commit/aa82c2dea8bee719a20fb59193d3320e934635b2))
* changed to Utf8Json ([63afa08](https://github.com/game-scrobbler/gs-playnite/commit/63afa08d883b40b06b9fefb125909c11f39d1a93))
* code cleanup ([a24b6a5](https://github.com/game-scrobbler/gs-playnite/commit/a24b6a5e20eb2cd07ec8ba1ae98b3c893ac0cf83))
* debug removed ([c8bf5ac](https://github.com/game-scrobbler/gs-playnite/commit/c8bf5ac4001b90649ec0effe0f7848822b086201))
* disableSentryFlag is a bool ([484ab3e](https://github.com/game-scrobbler/gs-playnite/commit/484ab3e8af1d9cc3783a6ecd23a4a5970f4238b3))
* do not call libSync in app stopped ([10065bb](https://github.com/game-scrobbler/gs-playnite/commit/10065bb056906104ce533144883afa3825ff5c07))
* dummy release for ci fix ([7ab7d58](https://github.com/game-scrobbler/gs-playnite/commit/7ab7d58e6f57e767135c06d200c65eaa99d8cfef))
* dump version ([70a636a](https://github.com/game-scrobbler/gs-playnite/commit/70a636ab2dbe957c95e968e6a3eef2ae23998d8f))
* fix version number ([598df0d](https://github.com/game-scrobbler/gs-playnite/commit/598df0de3fccb4b4991e4b229fe2fbaaf97a70ee))
* made function static ([9a88599](https://github.com/game-scrobbler/gs-playnite/commit/9a8859926efff7c7d6ece75b56c3623334b9a638))
* made session ID persistence ([96363fc](https://github.com/game-scrobbler/gs-playnite/commit/96363fc42c0bf313fd9f04537415f3eccfdac9a3))
* release please config typo ([96decf2](https://github.com/game-scrobbler/gs-playnite/commit/96decf2e1c10436a4d40519f134628683a0e2aa0))
* release process of pext file ([80bfd34](https://github.com/game-scrobbler/gs-playnite/commit/80bfd345141d8a4cc94b45aa716f56647d216df4))
* release process of pext file ([65d97a3](https://github.com/game-scrobbler/gs-playnite/commit/65d97a350b4d52935945c097284e342e8b0f449c))
* remove extra lines ([eed04ef](https://github.com/game-scrobbler/gs-playnite/commit/eed04efcb94fd155f3fd8864faf99b2c8f8487ae))
* remove ignored files ([aa8faa1](https://github.com/game-scrobbler/gs-playnite/commit/aa8faa10a426bcfbce62a36c19269781899037ae))
* remove Links from addon manifest ([92a570a](https://github.com/game-scrobbler/gs-playnite/commit/92a570aa1cf09868275149e956759488b6408fbc))
* removed all logs ([60fb3e6](https://github.com/game-scrobbler/gs-playnite/commit/60fb3e63978b0d6ba6305b80d11299a80a784c74))
* removed whitespace ([2177749](https://github.com/game-scrobbler/gs-playnite/commit/217774927263b999c14f30b601559494fe93109b))
* set different sentry Environment for development and production ([608de0a](https://github.com/game-scrobbler/gs-playnite/commit/608de0a2c6a62c6bcc9a08a8e3e05169b10f4aca))
* start api wrong tag ([91b0c24](https://github.com/game-scrobbler/gs-playnite/commit/91b0c243bbec236ad730ca0604ed8dc3be8bae4c))
* sync api ([4197c40](https://github.com/game-scrobbler/gs-playnite/commit/4197c408d40e5352da62f4645f6e2dd50b9b79d7))
* the data is now sync when the game library is updated ([59ed862](https://github.com/game-scrobbler/gs-playnite/commit/59ed8622f343ae7a3fd58aeb63c287e678b57631))
* the data is now sync when the game library is updated ([c274b2e](https://github.com/game-scrobbler/gs-playnite/commit/c274b2e8fa28a34af29f9065257ca8185c96dd98))
* type of manifest should be Generic not GenericPlugin ([0b5ed04](https://github.com/game-scrobbler/gs-playnite/commit/0b5ed043f8c4d0482f44c7209c425ec981d8e103))
* update gitignore ([ef17cc9](https://github.com/game-scrobbler/gs-playnite/commit/ef17cc9654ceaa31974f6ab9cee252e80843a718))
* wrong session_id model ([d0914c1](https://github.com/game-scrobbler/gs-playnite/commit/d0914c1a88d680d7ee31e95336c06cd7b1108d37))

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

## [0.3.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.3.0...GsPlugin-v0.3.0) (2025-02-10)


### Features

* add favicon to project ([f5ce14c](https://github.com/game-scrobbler/gs-playnite/commit/f5ce14c6ee26e62d12bb2cbec5efa0e901367d14))
* add permissions ([6f486b3](https://github.com/game-scrobbler/gs-playnite/commit/6f486b37b4a35bd65a5d5374f1ebb86dfe6c07ef))
* added sentry logs and ver to the web view link ([c9d3650](https://github.com/game-scrobbler/gs-playnite/commit/c9d365081132933d251c54dd6909fe4566f0cf31))
* added sentry package(not implemented) and resolved conflicts. ([e1e7adc](https://github.com/game-scrobbler/gs-playnite/commit/e1e7adc65e41397bc4db73c27e6a7dbdd0bfb662))
* api fully implement and GUID ([1b99895](https://github.com/game-scrobbler/gs-playnite/commit/1b99895548f6f0f0ef09fe7723b24aa370620821))
* init commit ([0a002fc](https://github.com/game-scrobbler/gs-playnite/commit/0a002fcd88e799ca70d61ab61d7cf71b9fd187cf))
* update manifests and icon ([cf1199d](https://github.com/game-scrobbler/gs-playnite/commit/cf1199deaa22fa2317396e406b94c6d0335ffa1f))
* updated the Ifarame ([bf610bc](https://github.com/game-scrobbler/gs-playnite/commit/bf610bc0eb403d975aabf37ca488d79c6b556517))


### Bug Fixes

* added one log for onAppStart ([fe1de34](https://github.com/game-scrobbler/gs-playnite/commit/fe1de344d25aa47aed9b64c7a5ee905c5803c9c8))
* added sync on app exit ([3131cd0](https://github.com/game-scrobbler/gs-playnite/commit/3131cd0d2b432f79007238a73105542dc48a985b))
* change release draft status ([aa82c2d](https://github.com/game-scrobbler/gs-playnite/commit/aa82c2dea8bee719a20fb59193d3320e934635b2))
* changed to Utf8Json ([63afa08](https://github.com/game-scrobbler/gs-playnite/commit/63afa08d883b40b06b9fefb125909c11f39d1a93))
* dummy release for ci fix ([7ab7d58](https://github.com/game-scrobbler/gs-playnite/commit/7ab7d58e6f57e767135c06d200c65eaa99d8cfef))
* release please config typo ([96decf2](https://github.com/game-scrobbler/gs-playnite/commit/96decf2e1c10436a4d40519f134628683a0e2aa0))
* release process of pext file ([65d97a3](https://github.com/game-scrobbler/gs-playnite/commit/65d97a350b4d52935945c097284e342e8b0f449c))
* remove extra lines ([eed04ef](https://github.com/game-scrobbler/gs-playnite/commit/eed04efcb94fd155f3fd8864faf99b2c8f8487ae))
* remove ignored files ([aa8faa1](https://github.com/game-scrobbler/gs-playnite/commit/aa8faa10a426bcfbce62a36c19269781899037ae))
* remove Links from addon manifest ([92a570a](https://github.com/game-scrobbler/gs-playnite/commit/92a570aa1cf09868275149e956759488b6408fbc))
* removed all logs ([60fb3e6](https://github.com/game-scrobbler/gs-playnite/commit/60fb3e63978b0d6ba6305b80d11299a80a784c74))
* start api wrong tag ([91b0c24](https://github.com/game-scrobbler/gs-playnite/commit/91b0c243bbec236ad730ca0604ed8dc3be8bae4c))
* sync api ([4197c40](https://github.com/game-scrobbler/gs-playnite/commit/4197c408d40e5352da62f4645f6e2dd50b9b79d7))
* the data is now sync when the game library is updated ([c274b2e](https://github.com/game-scrobbler/gs-playnite/commit/c274b2e8fa28a34af29f9065257ca8185c96dd98))
* type of manifest should be Generic not GenericPlugin ([0b5ed04](https://github.com/game-scrobbler/gs-playnite/commit/0b5ed043f8c4d0482f44c7209c425ec981d8e103))
* update gitignore ([ef17cc9](https://github.com/game-scrobbler/gs-playnite/commit/ef17cc9654ceaa31974f6ab9cee252e80843a718))

## [0.3.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.2.0...GsPlugin-v0.3.0) (2025-02-10)


### Features

* add favicon to project ([f5ce14c](https://github.com/game-scrobbler/gs-playnite/commit/f5ce14c6ee26e62d12bb2cbec5efa0e901367d14))
* added sentry logs and ver to the web view link ([c9d3650](https://github.com/game-scrobbler/gs-playnite/commit/c9d365081132933d251c54dd6909fe4566f0cf31))
* added sentry package(not implemented) and resolved conflicts. ([e1e7adc](https://github.com/game-scrobbler/gs-playnite/commit/e1e7adc65e41397bc4db73c27e6a7dbdd0bfb662))


### Bug Fixes

* added sync on app exit ([3131cd0](https://github.com/game-scrobbler/gs-playnite/commit/3131cd0d2b432f79007238a73105542dc48a985b))
* remove extra lines ([eed04ef](https://github.com/game-scrobbler/gs-playnite/commit/eed04efcb94fd155f3fd8864faf99b2c8f8487ae))
* the data is now sync when the game library is updated ([c274b2e](https://github.com/game-scrobbler/gs-playnite/commit/c274b2e8fa28a34af29f9065257ca8185c96dd98))

## [0.2.0](https://github.com/game-scrobbler/gs-playnite/compare/GsPlugin-v0.2.0...GsPlugin-v0.2.0) (2025-02-06)


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
