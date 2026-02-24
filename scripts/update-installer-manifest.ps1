param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [Parameter(Mandatory=$true)]
    [string]$TagName,

    [Parameter(Mandatory=$false)]
    [string]$RequiredApiVersion = "6.12.0",

    [Parameter(Mandatory=$false)]
    [string]$ChangelogFile = "CHANGELOG.md"
)

# Import powershell-yaml module
Import-Module "$PSScriptRoot\..\powershell-yaml\powershell-yaml.psd1" -Force

$manifestPath = "$PSScriptRoot\..\installer_manifest.yaml"
$releaseDate = Get-Date -Format "yyyy-MM-dd"
$packageUrl = "https://github.com/game-scrobbler/gs-playnite/releases/download/$TagName/$TagName.pext"

Write-Host "Updating installer_manifest.yaml with version $Version"
Write-Host "Tag: $TagName"
Write-Host "Package URL: $packageUrl"
Write-Host "Release Date: $releaseDate"

# Read the current manifest
$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Yaml

# Extract changelog entries for this version from CHANGELOG.md
$changelogContent = Get-Content -Path $ChangelogFile -Raw
$versionPattern = "## \[$Version\].*?\n(.*?)(?=\n## \[|$)"
$changelogMatch = [regex]::Match($changelogContent, $versionPattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)

$changelogEntries = @()
if ($changelogMatch.Success) {
    $versionChangelog = $changelogMatch.Groups[1].Value
    # Extract bullet points from Features and Bug Fixes sections
    $bulletPattern = "^\s*\*\s+(.+?)$"
    $matches = [regex]::Matches($versionChangelog, $bulletPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)

    foreach ($match in $matches) {
        $line = $match.Groups[1].Value.Trim()
        # Clean up the line - remove commit links and extra formatting
        $line = $line -replace '\[([a-f0-9]+)\]\([^\)]+\)', ''
        $line = $line -replace '\*\*([^*]+)\*\*:', '$1:'
        $line = $line.Trim()
        if ($line -and $line -notmatch '^###') {
            $changelogEntries += $line
        }
    }
}

# If no changelog entries found, add a generic one
if ($changelogEntries.Count -eq 0) {
    $changelogEntries = @("Release version $Version")
}

Write-Host "Changelog entries:"
$changelogEntries | ForEach-Object { Write-Host "  - $_" }

# Create new package entry
$newPackage = [ordered]@{
    "Version" = $Version
    "RequiredApiVersion" = $RequiredApiVersion
    "ReleaseDate" = $releaseDate
    "PackageUrl" = $packageUrl
    "Changelog" = $changelogEntries
}

# Check if this version already exists
$existingVersionIndex = -1
for ($i = 0; $i -lt $manifest.Packages.Count; $i++) {
    if ($manifest.Packages[$i].Version -eq $Version) {
        $existingVersionIndex = $i
        break
    }
}

if ($existingVersionIndex -ge 0) {
    Write-Host "Version $Version already exists in manifest, updating..."
    $manifest.Packages[$existingVersionIndex] = $newPackage
} else {
    Write-Host "Adding new version $Version to manifest..."
    # Add at the beginning of the packages array
    $manifest.Packages = @($newPackage) + $manifest.Packages
}

# Generate clean YAML manually instead of relying on ConvertTo-Yaml
# This ensures proper formatting and avoids library quirks
$yamlLines = @()
$yamlLines += "AddonId: $($manifest.AddonId)"
$yamlLines += ""
$yamlLines += "Packages:"

foreach ($package in $manifest.Packages) {
    $yamlLines += "  - Version: $($package.Version)"
    $yamlLines += "    RequiredApiVersion: $($package.RequiredApiVersion)"
    $yamlLines += "    ReleaseDate: $($package.ReleaseDate)"
    $yamlLines += "    PackageUrl: $($package.PackageUrl)"
    $yamlLines += "    Changelog:"

    # Handle changelog entries - ensure we have an array
    $changelogEntries = @()
    if ($package.Changelog -is [System.Collections.IEnumerable] -and $package.Changelog -isnot [string]) {
        # It's already an array or list
        $changelogEntries = @($package.Changelog)
    } elseif ($package.Changelog) {
        # It's a single item or unknown type, wrap in array
        $changelogEntries = @($package.Changelog)
    }

    foreach ($entry in $changelogEntries) {
        # Skip if entry is null, empty, or a hashtable/object
        if (-not $entry -or $entry -is [hashtable] -or $entry -is [System.Collections.IDictionary]) {
            continue
        }

        # Convert to string if it's not already
        $entryStr = $entry.ToString()

        # Skip empty strings
        if ([string]::IsNullOrWhiteSpace($entryStr)) {
            continue
        }

        # Remove trailing " ()" artifacts from changelog parsing
        $entryStr = $entryStr -replace '\s*\(\)\s*$', ''

        # Escape quotes in the entry
        $escapedEntry = $entryStr -replace '"', '\"'

        # Quote entries that contain special YAML characters
        if ($escapedEntry -match '[:\[\]{}@&*#?|<>%`]|^-|\s+$') {
            $yamlLines += "      - `"$escapedEntry`""
        } else {
            $yamlLines += "      - $escapedEntry"
        }
    }
}

$yamlContent = $yamlLines -join "`n"

# Write to file
Set-Content -Path $manifestPath -Value $yamlContent -NoNewline

Write-Host "Successfully updated $manifestPath"
Write-Host ""
Write-Host "Updated manifest content:"
Get-Content -Path $manifestPath | Select-Object -First 20
