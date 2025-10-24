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
Import-Module "$PSScriptRoot\powershell-yaml\powershell-yaml.psd1" -Force

$manifestPath = "$PSScriptRoot\installer_manifest.yaml"
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

# Write the updated manifest back to file
$yamlContent = ConvertTo-Yaml -Data $manifest -Options EmitDefaults

# Fix YAML formatting issues
$yamlContent = $yamlContent -replace "Packages:\r?\n- ", "Packages:`n  - "
$yamlContent = $yamlContent -replace "\r?\n  Version:", "`n`n  - Version:"
$yamlContent = $yamlContent -replace "\r?\n- Version:", "`n  - Version:"

# Ensure proper indentation for all fields
$lines = $yamlContent -split "`n"
$formattedLines = @()
$inPackages = $false

foreach ($line in $lines) {
    if ($line -match "^Packages:") {
        $inPackages = $true
        $formattedLines += $line
    }
    elseif ($inPackages -and $line -match "^\s*- Version:") {
        $formattedLines += "  $($line.TrimStart())"
    }
    elseif ($inPackages -and $line -match "^\s*(RequiredApiVersion|ReleaseDate|PackageUrl|Changelog):") {
        $formattedLines += "    $($line.TrimStart())"
    }
    elseif ($inPackages -and $line -match "^\s*-\s+") {
        $formattedLines += "      $($line.TrimStart())"
    }
    else {
        $formattedLines += $line
    }
}

$yamlContent = $formattedLines -join "`n"

# Write to file
Set-Content -Path $manifestPath -Value $yamlContent -NoNewline

Write-Host "Successfully updated $manifestPath"
Write-Host ""
Write-Host "Updated manifest content:"
Get-Content -Path $manifestPath | Select-Object -First 20
