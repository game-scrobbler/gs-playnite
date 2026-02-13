# PowerShell Pre-commit hook for .NET code formatting
# This script runs before each commit to ensure code is properly formatted
# It does NOT auto-stage changes — developers must review and stage manually.

Write-Host "Running pre-commit formatting check..." -ForegroundColor Yellow

# Check if dotnet is available
$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: dotnet CLI not found. Please install .NET SDK." -ForegroundColor Red
    exit 1
}

Write-Host "Using .NET SDK version: $dotnetVersion" -ForegroundColor Green

# Get list of staged C# files
$stagedFiles = git diff --cached --name-only --diff-filter=ACM | Where-Object { $_ -match '\.(cs|vb)$' }

if ($stagedFiles.Count -eq 0) {
    Write-Host "No C# files to format." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($stagedFiles.Count) C# file(s) to check:" -ForegroundColor Cyan
$stagedFiles | ForEach-Object { Write-Host "  - $_" -ForegroundColor Gray }

# Run dotnet format in verify mode — does not modify files
Write-Host "Checking code formatting..." -ForegroundColor Cyan
$formatResult = dotnet format GsPlugin.sln --verify-no-changes --verbosity quiet 2>&1
$formatExitCode = $LASTEXITCODE

if ($formatExitCode -ne 0) {
    Write-Host "" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  CODE FORMATTING ISSUES DETECTED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "" -ForegroundColor White
    Write-Host "Please fix formatting before committing:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File format-code.ps1" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Then review the changes, stage them, and commit again." -ForegroundColor Yellow
    Write-Host "" -ForegroundColor White
    exit 1
}

Write-Host "Code formatting check passed!" -ForegroundColor Green
exit 0
