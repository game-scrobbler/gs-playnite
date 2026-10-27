<#
.SYNOPSIS
    Pester tests for scripts/release-highlights-helpers.ps1 and
    scripts/assert-release-highlights.ps1.

.DESCRIPTION
    The helpers are pure functions, so they are dot-sourced and called directly
    with hand-built Messages API responses and error records. The shapes match
    what Invoke-RestMethod produces under pwsh 7: an HttpResponseException whose
    Response.StatusCode is the HTTP status, with the API's JSON error body in
    ErrorDetails.Message.

    assert-release-highlights.ps1 ends with `exit`, so it is invoked with `&`,
    which ends only that script and sets $LASTEXITCODE. Running it in-process
    is what lets Pester measure its coverage.

    Run with: Invoke-Pester -Path scripts/release-highlights-helpers.Tests.ps1
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'release-highlights-helpers.ps1')
    $script:AssertScriptPath = Join-Path $PSScriptRoot 'assert-release-highlights.ps1'
    $script:Generic = 'Generic highlight.'

    function New-Response([string]$StopReason, [string[]]$Texts) {
        $content = @([PSCustomObject]@{ type = 'thinking'; thinking = '' })
        foreach ($t in $Texts) {
            $content += [PSCustomObject]@{ type = 'text'; text = $t }
        }
        [PSCustomObject]@{ stop_reason = $StopReason; content = $content }
    }

    function New-ErrorRecord([int]$Status, [string]$Body, [string]$Message) {
        $response = if ($Status) { [PSCustomObject]@{ StatusCode = [System.Net.HttpStatusCode]$Status } } else { $null }
        [PSCustomObject]@{
            Exception    = [PSCustomObject]@{ Message = $Message; Response = $response }
            ErrorDetails = if ($null -ne $Body) { [PSCustomObject]@{ Message = $Body } } else { $null }
        }
    }
}

Describe 'Get-HighlightsResult' {

    It 'returns the bullets, normalized to "* ", when the turn ended normally' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @("* One`n- Two`n* Three")) -GenericHighlight $script:Generic

        $result.Problem | Should -BeNullOrEmpty
        $result.Bullets | Should -Be @('* One', '* Two', '* Three')
    }

    It 'uses the generic highlight when the model answers with only the no-changes line' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @('NO_PLAYER_FACING_CHANGES')) -GenericHighlight $script:Generic

        $result.Problem | Should -BeNullOrEmpty
        $result.Bullets | Should -Be @('* Generic highlight.')
    }

    It 'prefers real bullets over the no-changes line when the model sends both' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @("* Real change`nNO_PLAYER_FACING_CHANGES")) -GenericHighlight $script:Generic

        $result.Problem | Should -BeNullOrEmpty
        $result.Bullets | Should -Be @('* Real change')
    }

    It 'matches the no-changes line case-sensitively' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @('no_player_facing_changes')) -GenericHighlight $script:Generic

        $result.Problem | Should -Be 'Model returned 0 bullet lines, expected 1-8'
    }

    It 'reports a problem when the answer has no text at all' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @()) -GenericHighlight $script:Generic

        $result.Problem | Should -Be 'Model returned 0 bullet lines, expected 1-8'
        $result.Bullets.Count | Should -Be 0
    }

    It 'rejects an answer cut off by max_tokens even when it holds bullets' {
        $result = Get-HighlightsResult -Response (New-Response 'max_tokens' @("* One`n* Two is cut of")) -GenericHighlight $script:Generic

        $result.Problem | Should -Be "Model stopped with stop_reason 'max_tokens' instead of 'end_turn'"
    }

    It 'reports a problem for more than 8 bullets' {
        $nine = (1..9 | ForEach-Object { "* Bullet $_" }) -join "`n"
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @($nine)) -GenericHighlight $script:Generic

        $result.Problem | Should -Be 'Model returned 9 bullet lines, expected 1-8'
    }

    It 'keeps the raw text so a failure can be logged' {
        $result = Get-HighlightsResult -Response (New-Response 'end_turn' @('There is nothing to say.')) -GenericHighlight $script:Generic

        $result.Text | Should -Be 'There is nothing to say.'
        $result.StopReason | Should -Be 'end_turn'
    }
}

Describe 'Format-AnthropicFailure' {

    It 'reports the status, error type, message and request id from the API error body' {
        $body = '{"type":"error","error":{"type":"billing_error","message":"Payment method declined."},"request_id":"req_123"}'
        $record = New-ErrorRecord 402 $body 'Response status code does not indicate success: 402 (Payment Required).'

        Format-AnthropicFailure -ErrorRecord $record | Should -Be 'HTTP 402 billing_error: Payment method declined. (request_id req_123)'
    }

    It 'shows a spend-limit 400 by its message rather than as a bare Bad Request' {
        $body = '{"type":"error","error":{"type":"invalid_request_error","message":"You have reached your specified API usage limits."}}'
        $record = New-ErrorRecord 400 $body 'Response status code does not indicate success: 400 (Bad Request).'

        Format-AnthropicFailure -ErrorRecord $record | Should -Be 'HTTP 400 invalid_request_error: You have reached your specified API usage limits.'
    }

    It 'falls back to the status line when the body is not the API error JSON' {
        $record = New-ErrorRecord 502 '<html>Bad gateway</html>' 'Response status code does not indicate success: 502 (Bad Gateway).'

        Format-AnthropicFailure -ErrorRecord $record | Should -Be 'HTTP 502: Response status code does not indicate success: 502 (Bad Gateway).'
    }

    It 'falls back to the exception message when no response arrived' {
        $record = New-ErrorRecord 0 $null 'No such host is known. (api.anthropic.com:443)'

        Format-AnthropicFailure -ErrorRecord $record | Should -Be 'No such host is known. (api.anthropic.com:443)'
    }
}

Describe 'Test-HighlightsPresent' {

    It 'is true when the version section has a Highlights heading with a bullet' {
        $changelog = "# Changelog`n`n## [1.2.0](x) (2026-01-02)`n`n`n### Highlights`n`n* Nice thing`n`n`n### Features`n`n* feat`n`n## [1.1.0](x) (2026-01-01)`n`n* old"

        Test-HighlightsPresent -Changelog $changelog -Version '1.2.0' | Should -BeTrue
    }

    It 'handles CRLF line endings' {
        $changelog = "## [1.2.0](x)`r`n`r`n### Highlights`r`n`r`n* Nice thing`r`n`r`n### Features`r`n* feat"

        Test-HighlightsPresent -Changelog $changelog -Version '1.2.0' | Should -BeTrue
    }

    It 'is false when the version section has no Highlights heading' {
        $changelog = "## [1.2.0](x)`n`n### Bug Fixes`n`n* fix`n"

        Test-HighlightsPresent -Changelog $changelog -Version '1.2.0' | Should -BeFalse
    }

    It 'is false when the Highlights heading has no bullets before the next section' {
        $changelog = "## [1.2.0](x)`n`n### Highlights`n`n`n### Bug Fixes`n`n* fix`n"

        Test-HighlightsPresent -Changelog $changelog -Version '1.2.0' | Should -BeFalse
    }

    It 'does not count Highlights that belong to an older version' {
        $changelog = "## [1.2.0](x)`n`n### Bug Fixes`n`n* fix`n`n## [1.1.0](x)`n`n### Highlights`n`n* Old highlight`n"

        Test-HighlightsPresent -Changelog $changelog -Version '1.2.0' | Should -BeFalse
    }

    It 'is false when the version has no section' {
        Test-HighlightsPresent -Changelog "## [1.1.0](x)`n`n### Highlights`n`n* Old`n" -Version '1.2.0' | Should -BeFalse
    }

    It 'does not treat "." in the version as a regex wildcard' {
        Test-HighlightsPresent -Changelog "## [1x2x0](x)`n`n### Highlights`n`n* Thing`n" -Version '1.2.0' | Should -BeFalse
    }
}

Describe 'assert-release-highlights.ps1' {

    BeforeEach {
        $script:TestDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path $script:TestDir -Force | Out-Null
        $script:ChangelogPath = Join-Path $script:TestDir 'CHANGELOG.md'
        $script:ManifestPath = Join-Path $script:TestDir '.release-please-manifest.json'
        Set-Content -Path $script:ManifestPath -Value '{ ".": "1.2.0" }' -NoNewline
    }

    AfterEach {
        Remove-Item -Path $script:TestDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    BeforeAll {
        function Invoke-AssertScript {
            $global:LASTEXITCODE = 0
            & $script:AssertScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-String
        }
    }

    It 'exits 0 when the release has Highlights' {
        Set-Content -Path $script:ChangelogPath -Value "## [1.2.0](x)`n`n### Highlights`n`n* Thing`n" -NoNewline

        $output = Invoke-AssertScript

        $LASTEXITCODE | Should -Be 0
        $output.Contains('has Highlights for 1.2.0') | Should -BeTrue
    }

    It 'exits 1 with an error annotation when the release has no Highlights' {
        Set-Content -Path $script:ChangelogPath -Value "## [1.2.0](x)`n`n### Bug Fixes`n`n* fix`n" -NoNewline

        $output = Invoke-AssertScript

        $LASTEXITCODE | Should -Be 1
        $output.Contains('::error::') | Should -BeTrue
        $output.Contains('no Highlights for 1.2.0') | Should -BeTrue
    }

    It 'exits 1 when the version cannot be read' {
        Set-Content -Path $script:ManifestPath -Value '{}' -NoNewline
        Set-Content -Path $script:ChangelogPath -Value "## [1.2.0](x)`n`n### Highlights`n`n* Thing`n" -NoNewline

        $output = Invoke-AssertScript

        $LASTEXITCODE | Should -Be 1
        $output.Contains('Could not read the release version') | Should -BeTrue
    }
}
