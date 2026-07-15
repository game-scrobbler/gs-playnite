# PowerShell Pre-commit hook for .NET code formatting
# This script runs before each commit to ensure code is properly formatted
# It does NOT auto-stage changes -developers must review and stage manually.

Write-Host "Running pre-commit formatting check..." -ForegroundColor Yellow

# Check if dotnet is available
$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: dotnet CLI not found. Please install .NET SDK." -ForegroundColor Red
    exit 1
}

Write-Host "Using .NET SDK version: $dotnetVersion" -ForegroundColor Green

# Get list of staged C# files
$stagedFiles = @(git diff --cached --name-only --diff-filter=ACM | Where-Object { $_ -match '\.(cs|vb)$' })

if ($stagedFiles.Count -eq 0) {
    Write-Host "No C# files to format." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($stagedFiles.Count) C# file(s) to check:" -ForegroundColor Cyan
$stagedFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }

# The solution's old-style WPF .csproj can only be loaded by "dotnet format"
# through a .NET Framework build host, which the repo-pinned .NET 8 SDK does not
# ship. format-sdk.ps1 runs the real "dotnet format" under the newest installed
# SDK that has a working build host, leaving the repo's SDK pin untouched.
# Git runs hooks from the repository root, so this working-tree path resolves.
. ./scripts/format-sdk.ps1

$sdk = Get-GsFormatSdk
if (-not $sdk) {
    Write-Host "" -ForegroundColor White
    Write-Host "ERROR: No installed .NET SDK can format this solution." -ForegroundColor Red
    Write-Host "dotnet format needs a .NET Framework build host (BuildHost-net472)" -ForegroundColor Yellow
    Write-Host "that the pinned .NET 8 SDK does not ship. Install the .NET 10 SDK:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    exit 1
}

Write-Host "Checking code formatting (dotnet format via SDK $sdk)..." -ForegroundColor Cyan
$repoRoot = (Get-Location).Path
$result = Invoke-GsDotnetFormat -RepoRoot $repoRoot -SdkVersion $sdk -ExtraArgs @('--verify-no-changes', '--verbosity', 'quiet')

# A capable SDK should not crash, but fail loudly rather than skip if it does.
$joined = ($result.Output -join "`n")
if ($joined -match 'Unhandled exception|build host could not be found|TypeInitializationException') {
    Write-Host "" -ForegroundColor White
    Write-Host "ERROR: dotnet format could not load the solution (build host failure)." -ForegroundColor Red
    Write-Host "Install the .NET 10 SDK, then commit again:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    exit 1
}

# dotnet format verifies the whole solution; restrict the pass/fail decision to
# the staged files so pre-existing formatting debt elsewhere does not block this
# commit. Match each "<path>(line,col): error RULE:" line against the staged set.
$stagedSet = @{}
foreach ($f in $stagedFiles) { $stagedSet[($f -replace '\\', '/').ToLowerInvariant()] = $true }
$repoPrefix = (($repoRoot -replace '\\', '/').ToLowerInvariant().TrimEnd('/')) + '/'

$offending = foreach ($line in $result.Output) {
    if ($line -match '^(?<path>.+?)\(\d+,\d+\):\s+error ') {
        $p = ($Matches['path'] -replace '\\', '/').ToLowerInvariant()
        if ($p.StartsWith($repoPrefix)) { $p = $p.Substring($repoPrefix.Length) }
        if ($stagedSet.ContainsKey($p)) { $line }
    }
}

if ($offending) {
    Write-Host "" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  CODE FORMATTING ISSUES DETECTED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "" -ForegroundColor White
    Write-Host ($offending -join [Environment]::NewLine) -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Please fix formatting before committing:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File scripts/format-code.ps1" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Then review the changes, stage them, and commit again." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor White
    exit 1
}

Write-Host "Code formatting check passed!" -ForegroundColor Green
exit 0
