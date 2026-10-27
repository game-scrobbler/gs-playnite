<#
.SYNOPSIS
    Pester tests for scripts/generate-release-highlights.ps1.

.DESCRIPTION
    The script under test communicates its control flow via top-level `exit`
    statements (best-effort/idempotent design - see CLAUDE.md "Release
    Highlights"). It runs in-process with `&`: `exit` in a script invoked that
    way ends only that script and sets $LASTEXITCODE (it is dot-sourcing that
    would end the caller). In-process runs are what let Pester measure the
    script's coverage for SonarQube Cloud and let these tests mock `git` and
    `Invoke-RestMethod`, so the API path is exercised without a network or a
    key. A terminating error, which CI reports as a failed step, surfaces here
    as a thrown exception rather than a non-zero exit code.

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

        $scriptArgs = @{
            ChangelogFile = $ChangelogFile
            ManifestFile  = $ManifestFile
        }
        if ($DryRun) { $scriptArgs.DryRun = $true }
        if ($Model) { $scriptArgs.Model = $Model }

        # Snapshot and override environment variables for the script.
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
            # A script that ends without `exit` leaves $LASTEXITCODE alone, so
            # start from 0 rather than whatever the previous test left.
            $global:LASTEXITCODE = 0
            $output = & $script:ScriptPath @scriptArgs *>&1
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

        It 'fails hard (terminating error) when the manifest file does not exist at all' {
            # Unlike a malformed-but-present manifest, a missing file makes
            # Get-Content throw a terminating error (ErrorActionPreference =
            # Stop), which is not caught anywhere before the version check.
            # In CI that fails the step.
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n* something" -NoNewline
            $missingManifest = Join-Path $script:TestDir 'does-not-exist.json'

            { Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $missingManifest } | Should -Throw
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

    Context 'calling the API (git and Invoke-RestMethod mocked)' {
        BeforeAll {
            function New-ApiResponse([string]$StopReason, [string]$Text) {
                [PSCustomObject]@{
                    stop_reason = $StopReason
                    content     = @(
                        [PSCustomObject]@{ type = 'thinking'; thinking = '' },
                        [PSCustomObject]@{ type = 'text'; text = $Text }
                    )
                    usage       = [PSCustomObject]@{ input_tokens = 1200; output_tokens = 340 }
                }
            }

            function New-ApiError([int]$Status, [string]$Body) {
                $message = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode]$Status)
                $exception = [Microsoft.PowerShell.Commands.HttpResponseException]::new("Response status code does not indicate success: $Status.", $message)
                $record = [System.Management.Automation.ErrorRecord]::new($exception, 'WebCmdletWebResponseException', 'InvalidOperation', $null)
                $record.ErrorDetails = [System.Management.Automation.ErrorDetails]::new($Body)
                return $record
            }
        }

        BeforeEach {
            Set-Content -Path $script:ManifestPath -Value '{ ".": "1.0.0" }' -NoNewline
            Set-Content -Path $script:ChangelogPath -Value "## [1.0.0] (2026-01-01)`n`n### Bug Fixes`n* fix" -NoNewline
            Mock git { $global:LASTEXITCODE = 0; '' }
        }

        It 'inserts the model bullets and sends a request with room for thinking and the no-changes instruction' {
            Mock Invoke-RestMethod { New-ApiResponse 'end_turn' "* First thing`n* Second thing" }

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -EnvOverrides @{ ANTHROPIC_API_KEY = 'test-key' }

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('stop_reason=end_turn, input_tokens=1200, output_tokens=340') | Should -BeTrue
            $updated = Get-Content -Path $script:ChangelogPath -Raw
            $updated.Contains("### Highlights`n`n* First thing`n* Second thing") | Should -BeTrue
            Should -Invoke Invoke-RestMethod -Times 1 -Exactly -ParameterFilter {
                $request = $Body | ConvertFrom-Json
                $request.max_tokens -eq 16000 -and $request.messages[0].content.Contains('NO_PLAYER_FACING_CHANGES') -and $TimeoutSec -eq 300
            }
        }

        It 'writes the generic highlight when the model reports no player-facing changes' {
            Mock Invoke-RestMethod { New-ApiResponse 'end_turn' 'NO_PLAYER_FACING_CHANGES' }

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -EnvOverrides @{ ANTHROPIC_API_KEY = 'test-key' }

            $result.ExitCode | Should -Be 0
            (Get-Content -Path $script:ChangelogPath -Raw).Contains("### Highlights`n`n* Bug fixes and under-the-hood improvements for a smoother experience.") | Should -BeTrue
        }

        It 'skips with the reason and the model text when the answer has no bullets' {
            Mock Invoke-RestMethod { New-ApiResponse 'end_turn' 'There are no changes worth mentioning.' }

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -EnvOverrides @{ ANTHROPIC_API_KEY = 'test-key' }

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('::warning::Model returned 0 bullet lines, expected 1-8') | Should -BeTrue
            $result.Output.Contains('There are no changes worth mentioning.') | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }

        It 'skips an answer cut off by max_tokens' {
            Mock Invoke-RestMethod { New-ApiResponse 'max_tokens' "* First thing`n* Second thi" }

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -EnvOverrides @{ ANTHROPIC_API_KEY = 'test-key' }

            $result.ExitCode | Should -Be 0
            $result.Output.Contains("stop_reason 'max_tokens'") | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
        }

        It 'reports the API error type and message when the call fails' {
            Mock Invoke-RestMethod {
                throw (New-ApiError 402 '{"type":"error","error":{"type":"billing_error","message":"Your credit balance is too low."},"request_id":"req_9"}')
            }

            $result = Invoke-HighlightsScript -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath -EnvOverrides @{ ANTHROPIC_API_KEY = 'test-key' }

            $result.ExitCode | Should -Be 0
            $result.Output.Contains('::warning::Anthropic API call failed: HTTP 402 billing_error: Your credit balance is too low. (request_id req_9)') | Should -BeTrue
            (Get-Content -Path $script:ChangelogPath -Raw).Contains('### Highlights') | Should -BeFalse
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

    It 'defaults MaxTokens to 16000, leaving room for thinking before the answer' {
        Get-DefaultValue 'MaxTokens' | Should -Be 16000
    }

    It 'defaults GenericHighlight to the no-player-facing-changes sentence' {
        Get-DefaultValue 'GenericHighlight' | Should -Be 'Bug fixes and under-the-hood improvements for a smoother experience.'
    }
}