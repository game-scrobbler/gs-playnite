<#
.SYNOPSIS
    Pester tests for scripts/generate-release-highlights.ps1.

.DESCRIPTION
    The script under test communicates its control flow via top-level `exit`
    statements (best-effort/idempotent design - see CLAUDE.md "Release
    Highlights"). Because `exit` inside a dot-sourced or `&`-invoked script
    would terminate the whole PowerShell/Pester host process, every scenario
    here launches the script as a real child `pwsh -File` process and asserts
    on its exit code, stdout, and any resulting file changes. This mirrors how
    the script is actually invoked in CI (release-highlights.yml).

    The Anthropic API call itself is intentionally not exercised - there is no
    live API key in a test environment, and the call happens inside a
    separate process so it cannot be mocked. The `-DryRun` switch exists in
    the script specifically "for local testing of the changelog
    insertion/idempotency logic only", so these tests lean on it to cover the
    insertion/idempotency behavior that would otherwise be untestable.

    Run with: Invoke-Pester -Path scripts/generate-release-highlights.Tests.ps1
#>

BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot 'generate-release-highlights.ps1'

    function Invoke-HighlightsScript {
        param(
            [Parameter(Mandatory)] [string]$ChangelogFile,
            [Parameter(Mandatory)] [string]$ManifestFile,
            [switch]$DryRun,
            [string]$Model,
            [hashtable]$EnvOverrides = @{}
        )

        $pwshArgs = @(
            '-NoProfile', '-NonInteractive', '-File', $script:ScriptPath,
            '-ChangelogFile', $ChangelogFile,
            '-ManifestFile', $ManifestFile
        )
        if ($DryRun) { $pwshArgs += '-DryRun' }
        if ($Model) { $pwshArgs += @('-Model', $Model) }

        # Snapshot and override environment variables for the child process.
        # ANTHROPIC_API_KEY is always explicitly controlled so tests never
        # depend on whatever is ambient in the host environment.
        $keysToControl = @('ANTHROPIC_API_KEY') + @($EnvOverrides.Keys) | Select-Object -Unique
        $previous = @{}
        foreach ($key in $keysToControl) {
            $previous[$key] = [Environment]::GetEnvironmentVariable($key)
            $newValue = if ($EnvOverrides.ContainsKey($key)) { $EnvOverrides[$key] } else { $null }
            [Environment]::SetEnvironmentVariable($key, $newValue)
        }

        try {
            $output = & pwsh @pwshArgs 2>&1
            $exitCode = $LASTEXITCODE
        }
        finally {
            foreach ($key in $previous.Keys) {
                [Environment]::SetEnvironmentVariable($key, $previous[$key])
            }
        }

        [PSCustomObject]@{
            ExitCode = $exitCode
            Output   = ($output | Out-String)
        }
    }
}

Describe 'generate-release-highlights.ps1' {

    BeforeEach {
        $script:TestDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path $script:TestDir -Force | Out-Null
        $script:ChangelogPath = Join-Path $script:TestDir 'CHANGELOG.md'
        $script:ManifestPath = Join-Path $script:TestDir '.release-please-manifest.json'
    }

    AfterEach {
        Remove-Item -Path $script:TestDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Context 'when the version cannot be determined' {
        It 'skips gracefully (exit 0) and leaves the changelog untouched when the manifest has no "." key' {
            Set-Content -Path $script:ManifestPath -Value '{}' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n`n### Features`n* something" -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('Could not read version') | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }

        It 'fails hard (non-zero exit) when the manifest file does not exist at all' {
            # Unlike a malformed-but-present manifest, a missing file makes
            # Get-Content throw a terminating error (ErrorActionPreference =
            # Stop), which is not caught anywhere before the version check.
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n* something" -NoNewline
            $missingManifest = Join-Path $script:TestDir 'does-not-exist.json'

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $missingManifest

            $result.ExitCode | Should -Not -Be 0
        }
    }

    Context 'when the changelog has no matching version heading' {
        It 'skips gracefully (exit 0) and leaves the changelog untouched' {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "9.9.9" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n`n### Features`n* something" -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath

            $result.ExitCode | Should -Be 0
            $result.Output.Contains("No '## [9.9.9]' heading found") | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }
    }

    Context 'when Highlights already exist for the version' {
        It 'is idempotent: exits 0, logs the reason, and makes no file changes' {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.0.0" }' -NoNewline
            $original = "## [1.0.0] (2026-01-01)`n`n### Highlights`n`n* Already here`n`n### Features`n* something"
            Set-Content -Path $script:ChangelogPath -Value $original -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('Highlights already present') | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw) | Should -Be $original
        }
    }

    Context 'when ANTHROPIC_API_KEY is not set (non-dry-run path)' {
        It 'skips gracefully without attempting an API call and leaves the changelog untouched' {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.0.0" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n`n### Features`n* something" -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('ANTHROPIC_API_KEY is not set') | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }
    }

    Context '-DryRun' {
        It 'inserts the two canned highlight bullets under the matched version heading' {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.2.3" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.2.3] (2026-02-02)`n`n### Features`n* did a thing`n`n## [1.2.2] (2026-01-01)`n* older" -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun

            $result.ExitCode | Should -Be 0
            $updated = Get-Content -Path $script:ChangelogPath -Raw
            $updated.Contains('### Highlights') | Should -BeTrue
            $updated.Contains('* Dry-run highlight one') | Should -BeTrue
            $updated.Contains('* Dry-run highlight two') | Should -BeTrue
            # Inserted before the version's own Features section.
            $updated.IndexOf('### Highlights') | Should -BeLessThan $updated.IndexOf('### Features')
            # The older version's section is left completely untouched.
            $updated.Contains('## [1.2.2] (2026-01-01)`n* older'.Replace('`n', "`n")) | Should -BeTrue
        }

        It 'places the Highlights block immediately after the heading line, preserving the rest of the file' {
            $original = "## [1.2.3] (2026-02-02)`n`n### Features`n* did a thing"
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.2.3" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value $original -NoNewline

            Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun | Out-Null

            $updated = Get-Content -Path $script:ChangelogPath -Raw

            $headingLine = '## [1.2.3] (2026-02-02)'
            $remainder = $original.Substring($headingLine.Length)
            $expectedBullets = "* Dry-run highlight one`n* Dry-run highlight two"
            $expected = $headingLine + "`n`n`n" + "### Highlights`n`n$expectedBullets" + $remainder

            $updated | Should -Be $expected
        }

        It 'is idempotent across repeated runs' {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.2.3" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.2.3] (2026-02-02)`n`n### Features`n* did a thing" -NoNewline

            Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun | Out-Null
            $afterFirstRun = Get-Content -Path $script:ChangelogPath -Raw

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun
            $afterSecondRun = Get-Content -Path $script:ChangelogPath -Raw

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('Highlights already present') | Should -BeTrue
            $afterSecondRun | Should -Be $afterFirstRun
        }
    }

    Context 'version regex escaping' {
        It 'does not treat "." in the version as a regex wildcard when matching the heading' {
            # If the version were interpolated into the heading regex without
            # [regex]::Escape, "1.0.0" would match a heading using any
            # character in place of the dots (e.g. "1x0x0"). It must not.
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.0.0" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1x0x0] (2026-01-01)`n* something" -NoNewline

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -DryRun

            $result.ExitCode | Should -Be 0
            $result.Output.Contains("No '## [1.0.0]' heading found") | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }
    }
}

Describe 'generate-release-highlights.ps1 parameter defaults' {

    BeforeAll {
        $tokens = $null
        $errors = $null
        $script:ScriptAst = [System.Management.Automation.Language.Parser]::ParseFile(
            $script:ScriptPath, [ref]$tokens, [ref]$errors)
        $script:ScriptParams = $script:ScriptAst.ParamBlock.Parameters

        function Get-DefaultValue([string]$Name) {
            $param = $script:ScriptParams | Where-Object { $_.Name.VariablePath.UserPath -eq $Name }
            return $param.DefaultValue.Value
        }
    }

    It 'defaults ChangelogFile to CHANGELOG.md' {
        Get-DefaultValue 'ChangelogFile' | Should -Be 'CHANGELOG.md'
    }

    It 'defaults ManifestFile to .release-please-manifest.json' {
        Get-DefaultValue 'ManifestFile' | Should -Be '.release-please-manifest.json'
    }

    It 'defaults Model to claude-sonnet-5' {
        Get-DefaultValue 'Model' | Should -Be 'claude-sonnet-5'
    }

    It 'defaults TagPrefix to GsPlugin-v' {
        Get-DefaultValue 'TagPrefix' | Should -Be 'GsPlugin-v'
    }

    It 'defaults MaxDiffChars to 60000' {
        Get-DefaultValue 'MaxDiffChars' | Should -Be 60000
    }
}