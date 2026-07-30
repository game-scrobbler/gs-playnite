# AGENTS.md

General build, test, format, and architecture guidance for this repository lives in
[`CLAUDE.md`](CLAUDE.md) and [`README.md`](README.md). Read those first — this file only
adds notes specific to the Cursor Cloud (Linux) agent environment.

## Cursor Cloud specific instructions

Cursor Cloud agents run on **Linux**, but this project is a **Windows-only .NET Framework
4.6.2 WPF Playnite plugin**. That platform mismatch is the single most important thing to
understand before trying to build, test, or run anything here.

### What the environment already has (installed in the VM snapshot)

- **.NET SDK 8.0.x** (`dotnet`) — pinned by `global.json`; used for `dotnet restore` and the
  SDK-style test project.
- **Mono 6.8** — provides the .NET Framework runtime/reference framework list on Linux.
- **`nuget`** — a wrapper at `/usr/local/bin/nuget` that runs `mono /opt/nuget/nuget.exe`;
  needed to restore the `packages.config`-based main project (`dotnet restore` alone does
  **not** restore `packages.config` projects).
- **PowerShell 7 (`pwsh`) + Pester** — for the repo's PowerShell scripts and their tests.

The startup update script only restores dependencies (`nuget restore` + `dotnet restore`).
The toolchain above is baked into the snapshot, not reinstalled on every run.

### What can and cannot run on Linux

- **Cannot build the plugin (`GsPlugin.csproj`) on Linux.** It is an old-style WPF project;
  markup compilation (`Microsoft.WinFX.targets` / `PresentationBuildTasks`) requires the
  Windows-only `PresentationCore`/`PresentationFramework` assemblies. A build here fails with
  `error MC6000`. This is expected — the authoritative build is Windows MSBuild (see
  `CLAUDE.md` and `.github/workflows/build.yml`, which runs on `windows-2022`).
- **Cannot run the xUnit suite (`GsPlugin.Tests`) on Linux.** The test project has a
  `ProjectReference` to the WPF plugin, so it transitively hits the same WPF build wall.
  Run it on Windows with `dotnet test ... --no-build` after an MSBuild build.
- **`nuget restore GsPlugin.sln` and `dotnet restore GsPlugin.Tests/GsPlugin.Tests.csproj`
  both succeed on Linux.**
- **`dotnet format` does NOT truly verify formatting on Linux.** It exits 0 but only logs
  "Required references did not load" because it cannot load the WPF project. The pre-commit
  hook (`hooks/pre-commit`) already detects this and skips on non-Windows; the authoritative
  formatting check runs on Windows (`scripts/format-code.ps1`). Do not treat a green
  `dotnet format` on Linux as a real formatting pass.
- **The PowerShell script tests DO run on Linux** and are the one runnable automated suite
  here:
  `pwsh -NoProfile -Command "Invoke-Pester -Path scripts/update-installer-manifest.Tests.ps1, scripts/generate-release-highlights.Tests.ps1 -Output Detailed"`
- **Cannot run/pack the plugin on Linux.** Running it requires the Playnite desktop app +
  WebView2 on Windows; packing uses the Windows `Toolbox.exe`.

### Practical guidance for changes

- For edits to plugin C# code (`Api/`, `Services/`, `Models/`, `Infrastructure/`, `View/`),
  you can read/analyze and restore dependencies on Linux, but you cannot compile or run the
  xUnit tests here — call out that build/test verification must happen on Windows.
- For edits to the PowerShell tooling under `scripts/`, add/extend the Pester tests and run
  them with `pwsh`/`Invoke-Pester` as shown above — that fully works on Linux.
