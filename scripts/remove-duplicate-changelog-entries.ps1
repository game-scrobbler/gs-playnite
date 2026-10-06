param(
    [Parameter(Mandatory = $false)]
    [string]$ChangelogFile = "CHANGELOG.md",

    [Parameter(Mandatory = $false)]
    [string]$ManifestFile = ".release-please-manifest.json"
)

# Removes repeated entries from the release version's CHANGELOG.md section. PR titles are
# kept in plain English so release-please never lists a change twice (see
# Remove-DuplicateChangelogEntries); this catches any conventional-style title that slips
# through. It needs the full history (fetch-depth: 0) to tell merge commits apart; without
# it, the first of each repeated entry is kept.

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/release-highlights-helpers.ps1"

$version = (Get-Content -Path $ManifestFile -Raw | ConvertFrom-Json).'.'
if (-not $version) {
    Write-Host "::warning::Could not read the release version from $ManifestFile - not checking for duplicate changelog entries"
    exit 0
}

$isMergeCommit = {
    param([string]$Sha)
    $parents = git rev-list --parents -n 1 $Sha 2>$null
    # Output is the commit followed by its parents, so a merge has three or more SHAs.
    return $LASTEXITCODE -eq 0 -and @("$parents".Trim() -split '\s+').Count -gt 2
}

$changelog = Get-Content -Path $ChangelogFile -Raw
$result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version $version -IsMergeCommit $isMergeCommit
if ($result.Removed.Count -eq 0) {
    Write-Host "No duplicate changelog entries for $version."
    exit 0
}

Set-Content -Path $ChangelogFile -Value $result.Changelog -NoNewline
Write-Host "Removed $($result.Removed.Count) duplicate changelog entries for $($version):"
$result.Removed | ForEach-Object { Write-Host "  $_" }
