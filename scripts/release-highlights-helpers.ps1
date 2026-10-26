# Functions shared by generate-release-highlights.ps1 and assert-release-highlights.ps1.
# Dot-source this file; it defines functions only and has no side effects.

# The single line the model is told to answer with when a release changes nothing a
# player notices. The script swaps it for a generic bullet instead of failing.
$script:NoPlayerFacingChangesSentinel = "NO_PLAYER_FACING_CHANGES"

# Turns a Messages API response into Highlights bullets. Problem is null when the
# bullets are usable, and otherwise says why they are not.
function Get-HighlightsResult {
    param(
        [Parameter(Mandatory = $true)] $Response,
        [Parameter(Mandatory = $true)] [string]$GenericHighlight
    )

    $stopReason = $Response.stop_reason
    $text = (@($Response.content) | Where-Object { $_.type -eq "text" } | ForEach-Object { $_.text }) -join "`n"
    $lines = @($text -split "\r?\n" | ForEach-Object { $_.Trim() })
    $bullets = @($lines |
        Where-Object { $_ -match "^[\*\-]\s+\S" } |
        ForEach-Object { "* " + ($_ -replace "^[\*\-]\s+", "") })

    $problem = $null
    if ($stopReason -ne "end_turn") {
        # A cut-off answer can end mid-bullet, so anything but a finished turn is unusable.
        $problem = "Model stopped with stop_reason '$stopReason' instead of 'end_turn'"
    }
    elseif ($bullets.Count -eq 0 -and $lines -ccontains $script:NoPlayerFacingChangesSentinel) {
        $bullets = @("* $GenericHighlight")
    }
    elseif ($bullets.Count -lt 1 -or $bullets.Count -gt 8) {
        $problem = "Model returned $($bullets.Count) bullet lines, expected 1-8"
    }

    [PSCustomObject]@{
        Bullets    = $bullets
        Problem    = $problem
        StopReason = $stopReason
        Text       = $text
    }
}

# Describes a failed Invoke-RestMethod call. The exception message only carries the
# HTTP status line; the API's own error type and message (for example a billing_error,
# or the 400 a spend limit returns) are in the response body.
function Format-AnthropicFailure {
    param([Parameter(Mandatory = $true)] $ErrorRecord)

    $status = $null
    $response = $ErrorRecord.Exception.Response
    if ($response) {
        $status = [int]$response.StatusCode
    }

    $apiError = $null
    $requestId = $null
    $body = if ($ErrorRecord.ErrorDetails) { $ErrorRecord.ErrorDetails.Message } else { $null }
    if ($body) {
        try {
            $parsed = $body | ConvertFrom-Json
            $apiError = $parsed.error
            $requestId = $parsed.request_id
        }
        catch {
            $apiError = $null
        }
    }

    if ($apiError -and $apiError.type) {
        $described = "HTTP $($status) $($apiError.type): $($apiError.message)"
        if ($requestId) {
            $described += " (request_id $requestId)"
        }
        return $described
    }
    if ($status) {
        return "HTTP $($status): $($ErrorRecord.Exception.Message)"
    }
    return $ErrorRecord.Exception.Message
}

# True when the changelog section for $Version has a "### Highlights" heading with at
# least one bullet under it.
function Test-HighlightsPresent {
    param(
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string]$Changelog,
        [Parameter(Mandatory = $true)] [string]$Version
    )

    # (?m) makes ^ match at line starts, which also makes $ match at every line end,
    # so the section and block ends use \z (end of text) instead.
    $escapedVersion = [regex]::Escape($Version)
    $section = [regex]::Match($Changelog, "(?m)^## \[$escapedVersion\][^\n]*\n(.*?)(?=\n## \[|\z)", [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $section.Success) {
        return $false
    }

    $highlights = [regex]::Match($section.Groups[1].Value, "(?m)^### Highlights[ \t]*\r?\n(.*?)(?=\n###|\z)", [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $highlights.Success) {
        return $false
    }
    return [regex]::IsMatch($highlights.Groups[1].Value, "(?m)^\s*\*\s+\S")
}

# Removes repeated entries from one release's CHANGELOG section. release-please lists a
# change twice when a PR lands as a merge commit whose title reads like one of its
# commits: GitHub puts the PR title in the merge commit's body, and release-please parses
# that as a second change. Entries repeat only within one subsection, since both carry
# the same type. The kept entry is the one whose commit is not a merge, so its link points
# at the change itself. $IsMergeCommit takes a SHA and returns whether it is a merge.
function Remove-DuplicateChangelogEntries {
    param(
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string]$Changelog,
        [Parameter(Mandatory = $true)] [string]$Version,
        [Parameter(Mandatory = $true)] [scriptblock]$IsMergeCommit
    )

    $lines = $Changelog -split "\n"
    $escapedVersion = [regex]::Escape($Version)
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "^## \[$escapedVersion\]") {
            $start = $i
            break
        }
    }
    if ($start -lt 0) {
        return [PSCustomObject]@{ Changelog = $Changelog; Removed = @() }
    }

    # "* text ([abc1234](https://.../commit/<full sha>))", sometimes followed by
    # ", closes [#84](...)" when the commit closes an issue. Only the text before the
    # link is compared: the merge commit's copy never carries the "closes" part.
    $entryPattern = '^\* (?<text>.+?) \(\[(?<short>[0-9a-f]{7,40})\]\((?<url>[^)]*)\)\)'
    $groups = [ordered]@{}
    $subsection = ""
    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^## \[') {
            break
        }
        if ($lines[$i] -match '^### ') {
            $subsection = $lines[$i].Trim()
            continue
        }
        $entry = [regex]::Match($lines[$i], $entryPattern)
        if (-not $entry.Success) {
            continue
        }
        $sha = $entry.Groups["short"].Value
        $fullSha = [regex]::Match($entry.Groups["url"].Value, '[0-9a-f]{40}$')
        if ($fullSha.Success) {
            $sha = $fullSha.Value
        }
        $key = "$subsection`n$($entry.Groups['text'].Value)"
        if (-not $groups.Contains($key)) {
            $groups[$key] = [System.Collections.Generic.List[object]]::new()
        }
        $groups[$key].Add([PSCustomObject]@{ Index = $i; Sha = $sha })
    }

    $drop = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($group in $groups.Values) {
        if ($group.Count -lt 2) {
            continue
        }
        $keep = $group | Where-Object { -not (& $IsMergeCommit $_.Sha) } | Select-Object -First 1
        if (-not $keep) {
            $keep = $group[0]
        }
        foreach ($item in $group) {
            if ($item.Index -ne $keep.Index) {
                $null = $drop.Add($item.Index)
            }
        }
    }

    $removed = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($drop.Contains($i)) { $lines[$i].TrimEnd("`r") } })
    $kept = for ($i = 0; $i -lt $lines.Count; $i++) { if (-not $drop.Contains($i)) { $lines[$i] } }
    [PSCustomObject]@{
        Changelog = (@($kept) -join "`n")
        Removed   = $removed
    }
}
