<#
.SYNOPSIS
    Pester tests for changelog selection and lossless installer YAML updates.

.DESCRIPTION
    The script has two hard, non-parameterized dependencies on its own
    location ($PSScriptRoot):
      - it imports "..\powershell-yaml\powershell-yaml.psd1"
      - it reads/writes "..\installer_manifest.yaml"
    Each test copies the script and the real powershell-yaml module into an
    isolated temp workspace. CI checks out that module at the repository root.
    For local runs, set GS_TEST_YAML_MODULE_PATH to powershell-yaml.psd1 from
    a checkout, or install the module. Tests perform no network requests.
    Every output is parsed by the real parser, including the second input
    when testing repeated updates. JSON seed fixtures are valid YAML too.

    The updater runs in a child Windows PowerShell process on Windows, matching
    the release workflow; elsewhere it uses pwsh. This also isolates module
    imports between updates.

    Run with: Invoke-Pester -Path scripts/update-installer-manifest.Tests.ps1
#>

BeforeAll {
    $script:RealScriptPath = Join-Path $PSScriptRoot 'update-installer-manifest.ps1'
    $script:YamlModulePath = $env:GS_TEST_YAML_MODULE_PATH
    if (-not $script:YamlModulePath) {
        $script:YamlModulePath = Join-Path $PSScriptRoot '..\powershell-yaml\powershell-yaml.psd1'
    }
    if (-not (Test-Path -LiteralPath $script:YamlModulePath)) {
        $script:YamlModulePath = (Get-Module -ListAvailable powershell-yaml | Select-Object -First 1).Path
    }
    if (-not $script:YamlModulePath) {
        throw 'Real YAML tests require powershell-yaml. Set GS_TEST_YAML_MODULE_PATH or install the module.'
    }
    Import-Module $script:YamlModulePath -Force -ErrorAction Stop
    $script:ScriptHost = if ($env:OS -eq 'Windows_NT') { 'powershell.exe' } else { 'pwsh' }

    function New-TestWorkspace {
        $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path $workspace -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $workspace 'scripts') -Force | Out-Null
        Copy-Item -Path $script:RealScriptPath -Destination (Join-Path $workspace 'scripts\update-installer-manifest.ps1')
        Copy-Item -LiteralPath (Split-Path -Parent $script:YamlModulePath) `
            -Destination (Join-Path $workspace 'powershell-yaml') -Recurse

        return $workspace
    }

    function Invoke-ManifestScript {
        param(
            [Parameter(Mandatory)] [string]$Workspace,
            [Parameter(Mandatory)] [string]$Version,
            [Parameter(Mandatory)] [string]$TagName,
            [Parameter(Mandatory)] [string]$ChangelogFile,
            [string]$ExistingManifestJsonPath
        )

        $scriptPath = Join-Path $Workspace 'scripts\update-installer-manifest.ps1'
        $resultYamlPath = Join-Path $Workspace 'installer_manifest.yaml'
        if ($ExistingManifestJsonPath) {
            Copy-Item -LiteralPath $ExistingManifestJsonPath -Destination $resultYamlPath -Force
        }
        $output = & $script:ScriptHost -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath `
            -Version $Version -TagName $TagName -ChangelogFile $ChangelogFile 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Manifest updater failed with exit code ${LASTEXITCODE}: $output"
        }
        $manifestRaw = [System.IO.File]::ReadAllText($resultYamlPath)
        [PSCustomObject]@{
            Output      = $output
            ManifestRaw = $manifestRaw
            Manifest    = ($manifestRaw | ConvertFrom-Yaml)
        }
    }

    function New-ExistingManifestFixture {
        param([string]$Workspace, [string]$Version = '1.0.0')

        $fixturePath = Join-Path $Workspace 'existing-manifest.json'
        $fixture = [ordered]@{
            AddonId  = '32975fed-6915-4dd3-a230-030cdc5265ae'
            Packages = @(
                [ordered]@{
                    Version             = $Version
                    RequiredApiVersion  = '6.12.0'
                    ReleaseDate         = '2026-01-01'
                    PackageUrl          = 'https://example.com/old.pext'
                    Changelog           = @('old entry one', 'old entry two')
                }
            )
        }
        ($fixture | ConvertTo-Json -Depth 5) | Set-Content -Path $fixturePath
        return $fixturePath
    }
}

Describe 'update-installer-manifest.ps1' {

    BeforeEach {
        $script:Workspace = New-TestWorkspace
        $script:ChangelogPath = Join-Path $script:Workspace 'CHANGELOG.md'
    }

    AfterEach {
        $cleanupPath = [System.IO.Path]::GetFullPath($script:Workspace)
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        if (-not $cleanupPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove test workspace outside the temp directory: $cleanupPath"
        }
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
    }

    Context 'when a "### Highlights" section exists for the version' {
        It 'uses only the Highlights bullets, ignoring the raw Features/Bug Fixes bullets' {
            $changelog = @'
## [2.0.0] (2026-05-01)

### Highlights

* Track sessions more reliably across restarts
* Faster startup and smoother syncing

### Features

* refactor internal sync loop
* add debug logging

### Bug Fixes

* fix crash on startup

## [1.9.0] (2026-04-01)
* older
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $result.Output.Contains('Using curated Highlights section for changelog entries') | Should -BeTrue
            $result.ManifestRaw.Contains('Track sessions more reliably across restarts') | Should -BeTrue
            $result.ManifestRaw.Contains('Faster startup and smoother syncing') | Should -BeTrue
            $result.ManifestRaw.Contains('refactor internal sync loop') | Should -BeFalse
            $result.ManifestRaw.Contains('add debug logging') | Should -BeFalse
            $result.ManifestRaw.Contains('fix crash on startup') | Should -BeFalse
        }

        It 'prepends the new version ahead of the existing (unrelated) version entries' {
            $changelog = @'
## [2.0.0] (2026-05-01)

### Highlights

* A shiny new highlight
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $versionLines = @([regex]::Matches($result.ManifestRaw, '- Version: (\S+)') | ForEach-Object { $_.Groups[1].Value })
            $versionLines[0] | Should -Be '2.0.0'
            $versionLines | Should -Contain '1.9.0'
            $result.ManifestRaw.Contains('old entry one') | Should -BeTrue
        }
    }

    Context 'when no "### Highlights" section exists for the version' {
        It 'falls back to the raw Features/Bug Fixes bullets' {
            $changelog = @'
## [3.0.0] (2026-06-01)

### Features

* [abc1234](https://example.com/abc1234) add cool feature
* **Fixed**: something else

### Bug Fixes

* fix crash on startup
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '2.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '3.0.0' -TagName 'GsPlugin-v3.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $result.Output.Contains('Using curated Highlights section for changelog entries') | Should -BeFalse
            $result.ManifestRaw.Contains('add cool feature') | Should -BeTrue
            $result.ManifestRaw.Contains('Fixed: something else') | Should -BeTrue
            $result.ManifestRaw.Contains('fix crash on startup') | Should -BeTrue
        }
    }

    Context 'when the "### Highlights" heading is present but has no bullets before the next heading' {
        It 'documents current regex behavior: it falls through and picks up the following section''s bullets' {
            # This is a known quirk of the "### Highlights\s*\n(.*?)(?=\n###|\n## |$)"
            # lookahead: when Highlights has zero bullets, backtracking
            # consumes the blank-line boundary before the next heading, so
            # the capture group ends up including the *next* section's text
            # instead of being empty. This test pins down that actual
            # behavior as a regression guard, not as a statement that it is
            # necessarily the ideal behavior.
            $changelog = @'
## [2.0.1] (2026-05-02)

### Highlights

### Features

* some feature
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '2.0.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.1' -TagName 'GsPlugin-v2.0.1' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $result.ManifestRaw.Contains('some feature') | Should -BeTrue
        }
    }

    Context 'when the changelog has no section at all for the version' {
        It 'falls back to a generic "Release version X" entry' {
            $changelog = @'
## [1.9.0] (2026-04-01)
* older
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '4.0.0' -TagName 'GsPlugin-v4.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $result.ManifestRaw.Contains('Release version 4.0.0') | Should -BeTrue
        }
    }

    Context 'when the version already exists in the manifest' {
        It 'updates that entry in place instead of adding a duplicate' {
            $changelog = @'
## [1.9.0] (2026-04-01)

### Highlights

* Updated highlight for an existing version
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '1.9.0' -TagName 'GsPlugin-v1.9.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $result.Output.Contains('already exists in manifest, updating') | Should -BeTrue
            $versionMatches = [regex]::Matches($result.ManifestRaw, '- Version: 1\.9\.0')
            $versionMatches.Count | Should -Be 1
            $result.ManifestRaw.Contains('Updated highlight for an existing version') | Should -BeTrue
            $result.ManifestRaw.Contains('old entry one') | Should -BeFalse
        }
    }

    Context 'YAML string round trips' {
        It 'preserves quotes, paths, YAML-looking values, and Unicode across repeated updates' {
            $entries = @(
                'Fixed "Delete My Data" sometimes getting stuck.',
                'Support imports from C:\Games: shared library',
                'Keep the literal sequence \n and a trailing backslash\',
                "Your friend's [library] #1: ready & waiting",
                'true', 'null', '123', '2026-09-05',
                'Faster café launches — بازی‌ها 🎮'
            )
            $changelog = "## [2.0.0] (2026-05-01)`n`n### Highlights`n`n" +
                (($entries | ForEach-Object { "* $_" }) -join "`n")
            [System.IO.File]::WriteAllText($script:ChangelogPath, $changelog)
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'

            $first = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest
            $second = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath

            $second.ManifestRaw | Should -BeExactly $first.ManifestRaw
            $second.Manifest.Packages.Count | Should -Be 2
            $actual = @($second.Manifest.Packages[0].Changelog)
            $actual.Count | Should -Be $entries.Count
            for ($i = 0; $i -lt $entries.Count; $i++) {
                $actual[$i] | Should -BeOfType [string]
                $actual[$i] | Should -BeExactly $entries[$i]
            }
        }

        It 'preserves older entries containing quotes, control characters, and backslashes' {
            $oldEntries = @(
                'Already fixed "Delete My Data".',
                'Paths: C:\Games\new\ and \\server\games',
                "A tab`tand a newline`nare kept.",
                'A backslash immediately before a quote: \"'
            )
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '1.9.0'
            $fixture = Get-Content -LiteralPath $existingManifest -Raw | ConvertFrom-Json
            $fixture.Packages[0].Changelog = $oldEntries
            [System.IO.File]::WriteAllText($existingManifest, ($fixture | ConvertTo-Json -Depth 5))
            [System.IO.File]::WriteAllText($script:ChangelogPath, "## [2.0.0]`n`n### Highlights`n`n* New release")

            $first = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest
            $second = Invoke-ManifestScript -Workspace $script:Workspace -Version '2.0.0' -TagName 'GsPlugin-v2.0.0' `
                -ChangelogFile $script:ChangelogPath

            $second.ManifestRaw | Should -BeExactly $first.ManifestRaw
            $actual = @($second.Manifest.Packages[1].Changelog)
            $actual.Count | Should -Be $oldEntries.Count
            for ($i = 0; $i -lt $oldEntries.Count; $i++) {
                $actual[$i] | Should -BeExactly $oldEntries[$i]
            }
        }

        It 'keeps the repaired historical highlights identical to the reviewed changelog' {
            $manifest = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\installer_manifest.yaml')) | ConvertFrom-Yaml
            $changelog = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\CHANGELOG.md'))
            foreach ($version in @('2.8.1', '2.8.0', '2.7.0')) {
                $section = [regex]::Match($changelog, "## \[$([regex]::Escape($version))\].*?\n(.*?)(?=\n## \[|$)", 'Singleline').Groups[1].Value
                $highlights = [regex]::Match($section, '### Highlights\s*\n(.*?)(?=\n###|$)', 'Singleline').Groups[1].Value
                $expected = @([regex]::Matches($highlights, '^\* (.+)', 'Multiline') | ForEach-Object { $_.Groups[1].Value.TrimEnd() })
                $package = $manifest.Packages | Where-Object { $_.Version -eq $version }
                $actual = @($package.Changelog)
                $actual.Count | Should -Be $expected.Count
                for ($i = 0; $i -lt $expected.Count; $i++) {
                    $actual[$i] | Should -BeExactly $expected[$i]
                }
            }
        }
    }

    Context 'no leftover marketing-note placeholder' {
        It 'never injects any entry beyond what was parsed from the changelog' {
            $changelog = @'
## [5.0.0] (2026-07-01)

### Highlights

* Only entry
'@
            Set-Content -Path $script:ChangelogPath -Value $changelog
            $existingManifest = New-ExistingManifestFixture -Workspace $script:Workspace -Version '4.9.0'

            $result = Invoke-ManifestScript -Workspace $script:Workspace -Version '5.0.0' -TagName 'GsPlugin-v5.0.0' `
                -ChangelogFile $script:ChangelogPath -ExistingManifestJsonPath $existingManifest

            $newEntryBlock = [regex]::Match($result.ManifestRaw, '- Version: 5\.0\.0.*?(?=\n  - Version:|\z)', 'Singleline').Value
            # Changelog bullets are emitted with a 6-space indent ("      - entry"),
            # distinct from the 2-space "  - Version: ..." entry header line -
            # match only the former so the header line isn't mistaken for a bullet.
            # Wrapped in @() so a single match isn't unwrapped into a bare string
            # (which would make [0] index into its first character instead).
            $changelogLines = @([regex]::Matches($newEntryBlock, '^ {6}-\s+(.+)$', 'Multiline') | ForEach-Object { $_.Groups[1].Value.Trim() })

            $changelogLines.Count | Should -Be 1
            $result.Manifest.Packages[0].Changelog[0] | Should -Be 'Only entry'
        }
    }
}
