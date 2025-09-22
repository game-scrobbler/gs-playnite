# Setup script for pre-commit hook on Windows
Write-Host "Setting up pre-commit hook for GS Playnite..." -ForegroundColor Cyan

# Check if we're in a Git repository
if (-not (Test-Path ".git")) {
    Write-Host "Error: Not in a Git repository. Run from repository root." -ForegroundColor Red
    exit 1
}

# Check if dotnet is available
$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "OK .NET SDK found: $dotnetVersion" -ForegroundColor Green
} else {
    Write-Host "ERROR .NET SDK not found. Please install .NET SDK first." -ForegroundColor Red
    exit 1
}

# Ensure hooks directory exists
$hooksDir = ".git/hooks"
if (-not (Test-Path $hooksDir)) {
    New-Item -ItemType Directory -Path $hooksDir -Force | Out-Null
    Write-Host "OK Created hooks directory" -ForegroundColor Green
}

# Check if hooks are already installed
if (Test-Path ".git/hooks/pre-commit.ps1") {
    Write-Host "OK Pre-commit PowerShell hook exists" -ForegroundColor Green
} else {
    Write-Host "WARN Pre-commit PowerShell hook not found." -ForegroundColor Yellow
}

if (Test-Path ".git/hooks/pre-commit") {
    Write-Host "OK Pre-commit shell hook exists" -ForegroundColor Green
} else {
    Write-Host "WARN Pre-commit shell hook not found." -ForegroundColor Yellow
}

# Configure Git settings
Write-Host "Configuring Git..." -ForegroundColor Yellow
git config core.autocrlf false
Write-Host "OK Configured Git line endings" -ForegroundColor Green

Write-Host "Pre-commit hook setup complete!" -ForegroundColor Green
Write-Host "The hook will:" -ForegroundColor White
Write-Host "  - Run dotnet format before each commit" -ForegroundColor Gray
Write-Host "  - Check for C# formatting issues" -ForegroundColor Gray
Write-Host "  - Prevent commits with formatting problems" -ForegroundColor Gray
Write-Host "  - Auto-apply formatting and ask you to re-commit" -ForegroundColor Gray

Write-Host "To test the hook manually, run:" -ForegroundColor Cyan
Write-Host "  powershell -File .git/hooks/pre-commit.ps1" -ForegroundColor White

Write-Host "Next time you commit, formatting will be applied automatically!" -ForegroundColor Green
