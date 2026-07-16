<#
.SYNOPSIS
    Pester tests for scripts/update-installer-manifest.ps1, focused on the
    changes introduced in this PR: preferring a curated "### Highlights"
    changelog section over the raw Features/Bug Fixes bullets (falling back
    to the raw bullets when no Highlights section exists), and the removal of
    the dead "marketing note" placeholder.

.DESCRIPTION
    The script has two hard, non-parameterized dependencies on its own
    location ($PSScriptRoot):
      - it imports "..\powershell-yaml\powershell-yaml.psd1"
      - it reads/writes "..\installer_manifest.yaml"
    To test it without ever touching the real repository files (and without a
    network dependency on the real powershell-yaml module, which is
    unrelated to the logic this PR changes), each test copies the script into
    an isolated temp workspace that reproduces this relative layout, along
    with a minimal stub "powershell-yaml" module. The stub's ConvertFrom-Yaml
    ignores the piped YAML text and instead returns a fixture object supplied
    via the PESTER_STUB_MANIFEST_JSON environment variable, so tests can
    control the "existing manifest" input precisely without a real YAML
    parser. The script never calls ConvertTo-Yaml (it builds YAML manually),
    so that's the only function the stub needs to provide.

    The script contains no `exit` calls, so it is safe to invoke directly
    (via the call operator) in-process.

    Run with: Invoke-Pester -Path scripts/update-installer-manifest.Tests.ps1
#>

BeforeAll {
    $script:RealScriptPath = Join-Path $PSScriptRoot 'update-installer-manifest.ps1'

    function New-TestWorkspace {
        $workspace = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path $workspace -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $workspace 'scripts') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $workspace 'powershell-yaml') -Force | Out-Null

        Copy-Item -Path $script:RealScriptPath -Destination (Join-Path $workspace 'scripts\update-installer-manifest.ps1')

        # The real installer_manifest.yaml content is irrelevant: the stub
        # ConvertFrom-Yaml below ignores it. The file just needs to exist so
        # Get-Content does not throw.
        Set-Content -Path (Join-Path $workspace 'installer_manifest.yaml') -Value 'placeholder' -NoNewline

        $stubModule = @'
function ConvertFrom-Yaml {
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline = $true)][string]$InputObject)
    process {
        # Test stub: real YAML parsing is intentionally not exercised here.
        # The fixture "existing manifest" object is supplied out-of-band via
        # PESTER_STUB_MANIFEST_JSON so tests can control it precisely.
        Get-Content -Path $env:PESTER_STUB_MANIFEST_JSON -Raw | ConvertFrom-Json
    }
}
Export-ModuleMember -Function ConvertFrom-Yaml
'@
        Set-Content -Path (Join-Path $workspace 'powershell-yaml\powershell-yaml.psm1') -Value $stubModule

        $stubManifest = @'
@{
    ModuleVersion = '1.0.0'
    RootModule = 'powershell-yaml.psm1'
    FunctionsToExport = @('ConvertFrom-Yaml')
}
'@
        Set-Content -Path (Join-Path $workspace 'powershell-yaml\powershell-yaml.psd1') -Value $stubManifest

        return $workspace
    }

    function Invoke-ManifestScript {
        param(
            [Parameter(Mandatory)] [string]$Workspace,
            [Parameter(Mandatory)] [string]$Version,
            [Parameter(Mandatory)] [string]$TagName,
            [Parameter(Mandatory)] [string]$ChangelogFile,
            [Parameter(Mandatory)] [string]$ExistingManifestJsonPath
        )

        $scriptPath = Join-Path $Workspace 'scripts\update-installer-manifest.ps1'
        $previous = [Environment]::GetEnvironmentVariable('PESTER_STUB_MANIFEST_JSON')
        [Environment]::SetEnvironmentVariable('PESTER_STUB_MANIFEST_JSON', $ExistingManifestJsonPath)

        try {
            # *>&1 merges all streams (including the Information stream that
            # Write-Host writes to) into the success stream so Out-String
            # actually captures the script's Write-Host output.
            $output = & $scriptPath -Version $Version -TagName $TagName -ChangelogFile $ChangelogFile *>&1 | Out-String
        }
        finally {
            [Environment]::SetEnvironmentVariable('PESTER_STUB_MANIFEST_JSON', $previous)
        }

        $resultYamlPath = Join-Path $Workspace 'installer_manifest.yaml'
        [PSCustomObject]@{
            Output      = $output
            ManifestRaw = (Get-Content -Path $resultYamlPath -Raw)
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
        Remove-Item -Path $script:Workspace -Recurse -Force -ErrorAction SilentlyContinue
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
            $changelogLines[0] | Should -Be 'Only entry'
        }
    }
}