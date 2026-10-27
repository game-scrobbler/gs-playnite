param(
    [Parameter(Mandatory = $false)]
    [string]$ChangelogFile = "CHANGELOG.md",

    [Parameter(Mandatory = $false)]
    [string]$ManifestFile = ".release-please-manifest.json",

    [Parameter(Mandatory = $false)]
    [string]$Model = "claude-sonnet-5",

    [Parameter(Mandatory = $false)]
    [string]$TagPrefix = "GsPlugin-v",

    [Parameter(Mandatory = $false)]
    [int]$MaxDiffChars = 60000,

    # Thinking is on by default for this model and counts toward max_tokens, so the
    # cap has to leave room for it before the answer. Only generated tokens are billed.
    [Parameter(Mandatory = $false)]
    [int]$MaxTokens = 16000,

    # Used as the only bullet when the model reports that nothing in the release
    # changes what a player notices.
    [Parameter(Mandatory = $false)]
    [string]$GenericHighlight = "Bug fixes and under-the-hood improvements for a smoother experience.",

    # Skips git + API calls and inserts canned bullets. For local testing of the
    # changelog insertion/idempotency logic only.
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/release-highlights-helpers.ps1"

# Highlights are best-effort: any failure warns and exits 0, so this step never fails
# the job. The workflow's next check (assert-release-highlights.ps1) fails it instead
# when the section is missing. If the release PR merged without Highlights anyway,
# update-installer-manifest.ps1 would fall back to the raw changelog bullets.
function Write-Skip([string]$Message) {
    Write-Host "::warning::$Message - skipping highlights generation (installer manifest will fall back to raw changelog bullets)"
}

$version = (Get-Content -Path $ManifestFile -Raw | ConvertFrom-Json).'.'
if (-not $version) {
    Write-Skip "Could not read version from $ManifestFile"
    exit 0
}
Write-Host "Generating release highlights for version $version"

$changelog = Get-Content -Path $ChangelogFile -Raw
$escapedVersion = [regex]::Escape($version)

$headingMatch = [regex]::Match($changelog, "(?m)^## \[$escapedVersion\][^\r\n]*")
if (-not $headingMatch.Success) {
    Write-Skip "No '## [$version]' heading found in $ChangelogFile"
    exit 0
}

$sectionMatch = [regex]::Match($changelog, "## \[$escapedVersion\].*?\n(.*?)(?=\n## \[|$)", [System.Text.RegularExpressions.RegexOptions]::Singleline)
if ($sectionMatch.Success -and $sectionMatch.Value -match '### Highlights') {
    Write-Host "Highlights already present for $version, nothing to do."
    exit 0
}

if ($DryRun) {
    $bullets = @("* Dry-run highlight one", "* Dry-run highlight two")
}
else {
    if (-not $env:ANTHROPIC_API_KEY) {
        Write-Skip "ANTHROPIC_API_KEY is not set"
        exit 0
    }

    # The release-please PR branch is based on main; the release content is
    # everything on main since the previous release tag.
    $prevTag = git tag --list "$TagPrefix*" --sort=-v:refname | Select-Object -First 1
    $null = git rev-parse --verify "origin/main" 2>$null
    $target = if ($LASTEXITCODE -eq 0) { "origin/main" } else { "HEAD" }
    $range = if ($prevTag) { "$prevTag..$target" } else { $target }
    Write-Host "Summarizing commit range: $range"

    $excludePaths = @(":(exclude)CHANGELOG.md", ":(exclude)installer_manifest.yaml", ":(exclude).release-please-manifest.json")
    $commitLog = (git log $range --no-merges --pretty=format:"%s%n%b---") -join "`n"
    $diffStat = (git diff $range --stat -- . $excludePaths) -join "`n"
    $diff = (git diff $range -- . $excludePaths) -join "`n"
    if ($diff.Length -gt $MaxDiffChars) {
        $diff = $diff.Substring(0, $MaxDiffChars) + "`n[diff truncated]"
    }

    $systemPrompt = "You write the 'What's New' notes for Game Scrobbler, a Playnite (desktop game library manager) extension that automatically tracks play time and achievements and shows gaming stats on gamescrobbler.com. Your readers are gamers glancing at an update popup: they don't know programming, and they don't care how the plugin works internally - only what changes for them."

    $userPrompt = @"
Below are the commits and code changes going into Game Scrobbler release $version. Write the "Highlights" bullets that players read before updating.

Rules:
- Output ONLY bullet lines, each starting with "* ". No headings, intro, or closing text.
- 3 to 5 bullets, each a single sentence under 120 characters. Lead with the most exciting change; new features before fixes.
- Describe the benefit the player notices, never the mechanism of the change.
  - Bad: "Improved handling of transient save errors during library sync."
  - Good: "Your play stats no longer get lost when your PC or connection hiccups."
- Address the reader as "you"/"your" where it reads naturally.
- Every word must make sense to a non-programmer. Never use words like: sync hash, snapshot, session, token, API, server, endpoint, DTO, retry, error handling, thread, timer, JSON, plugin ID.
- Omit purely internal changes (refactors, CI, tests, logging, telemetry) entirely. Do NOT write a filler bullet like "various behind-the-scenes improvements".
- If nothing in this release changes what a player notices, output exactly this one line and nothing else: $($script:NoPlayerFacingChangesSentinel)
- Merge related commits into one bullet.
- Never claim a feature or fix that is not supported by the commits and diff below.

Commits:
$commitLog

Diff stat:
$diffStat

Diff (may be truncated):
$diff
"@

    try {
        $body = @{
            model      = $Model
            max_tokens = $MaxTokens
            system     = $systemPrompt
            messages   = @(@{ role = "user"; content = $userPrompt })
        } | ConvertTo-Json -Depth 6
        $response = Invoke-RestMethod -Uri "https://api.anthropic.com/v1/messages" -Method Post -Body $body -TimeoutSec 300 -Headers @{
            "x-api-key"         = $env:ANTHROPIC_API_KEY
            "anthropic-version" = "2023-06-01"
            "content-type"      = "application/json"
        }
    }
    catch {
        Write-Skip "Anthropic API call failed: $(Format-AnthropicFailure -ErrorRecord $_)"
        exit 0
    }

    Write-Host "Model response: stop_reason=$($response.stop_reason), input_tokens=$($response.usage.input_tokens), output_tokens=$($response.usage.output_tokens)"
    $result = Get-HighlightsResult -Response $response -GenericHighlight $GenericHighlight
    if ($result.Problem) {
        Write-Skip $result.Problem
        Write-Host "Model text:"
        Write-Host $(if ($result.Text) { $result.Text } else { "<none>" })
        exit 0
    }
    $bullets = $result.Bullets
}

$highlightsBlock = "### Highlights`n`n" + ($bullets -join "`n")
$insertPos = $headingMatch.Index + $headingMatch.Length
$newChangelog = $changelog.Substring(0, $insertPos) + "`n`n`n" + $highlightsBlock + $changelog.Substring($insertPos)
Set-Content -Path $ChangelogFile -Value $newChangelog -NoNewline

Write-Host "Inserted $($bullets.Count) highlight bullet(s) for $version into $($ChangelogFile):"
$bullets | ForEach-Object { Write-Host "  $_" }
