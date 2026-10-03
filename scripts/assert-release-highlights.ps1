param(
    [Parameter(Mandatory = $false)]
    [string]$ChangelogFile = "CHANGELOG.md",

    [Parameter(Mandatory = $false)]
    [string]$ManifestFile = ".release-please-manifest.json"
)

# Fails when the release version's CHANGELOG.md entry has no Highlights. The generator
# is best-effort and exits 0 whatever goes wrong (API failure, exhausted credit, an
# unusable answer), so this is the step that turns a missing section into a failed
# check. Without Highlights, Playnite shows the raw commit bullets.

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/release-highlights-helpers.ps1"

$version = (Get-Content -Path $ManifestFile -Raw | ConvertFrom-Json).'.'
if (-not $version) {
    Write-Host "::error::Could not read the release version from $ManifestFile"
    exit 1
}

$changelog = Get-Content -Path $ChangelogFile -Raw
if (Test-HighlightsPresent -Changelog $changelog -Version $version) {
    Write-Host "$ChangelogFile has Highlights for $version."
    exit 0
}

Write-Host "::error::$ChangelogFile has no Highlights for $version, so Playnite would show the raw commit bullets. The 'Generate release highlights' step says why. Fix the cause and re-run this job, or add a '### Highlights' section to the release PR by hand."
exit 1
