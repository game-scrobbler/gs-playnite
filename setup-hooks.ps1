# Setup script for Git hooks on Windows
# Copies hook scripts from hooks/ directory into .git/hooks/
Write-Host "Setting up Git hooks for GS Playnite..." -ForegroundColor Cyan

# Check if we're in a Git repository
if (-not (Test-Path ".git")) {
    Write-Host "Error: Not in a Git repository. Run from repository root." -ForegroundColor Red
    exit 1
}

# Check if hooks source directory exists
if (-not (Test-Path "hooks")) {
    Write-Host "Error: hooks/ directory not found. Run from repository root." -ForegroundColor Red
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

# Install hooks by copying from hooks/ to .git/hooks/
$hookFiles = @("pre-commit", "pre-commit.ps1", "commit-msg", "commit-msg.ps1")
$installedCount = 0

foreach ($hookFile in $hookFiles) {
    $source = "hooks/$hookFile"
    $dest = ".git/hooks/$hookFile"

    if (Test-Path $source) {
        Copy-Item -Path $source -Destination $dest -Force
        Write-Host "OK Installed $hookFile" -ForegroundColor Green
        $installedCount++
    } else {
        Write-Host "WARN Source file not found: $source" -ForegroundColor Yellow
    }
}

if ($installedCount -eq 0) {
    Write-Host "ERROR No hooks were installed." -ForegroundColor Red
    exit 1
}

# Configure Git settings
Write-Host "Configuring Git..." -ForegroundColor Yellow
git config core.autocrlf false
Write-Host "OK Configured Git line endings" -ForegroundColor Green

Write-Host "" -ForegroundColor White
Write-Host "Successfully installed $installedCount hook(s)!" -ForegroundColor Green
Write-Host "" -ForegroundColor White
Write-Host "Installed hooks:" -ForegroundColor White
Write-Host "  1. Pre-commit hook:" -ForegroundColor Cyan
Write-Host "     - Verifies C# formatting before commit (does NOT auto-stage)" -ForegroundColor Gray
Write-Host "     - Run 'format-code.ps1' to fix formatting issues" -ForegroundColor Gray
Write-Host "" -ForegroundColor White
Write-Host "  2. Commit-msg hook:" -ForegroundColor Cyan
Write-Host "     - Validates commit messages follow Conventional Commits" -ForegroundColor Gray
Write-Host "     - Enforces proper format: <type>[scope]: <description>" -ForegroundColor Gray
Write-Host "" -ForegroundColor White

Write-Host "Valid commit message examples:" -ForegroundColor Yellow
Write-Host "  feat: add new statistics dashboard" -ForegroundColor Gray
Write-Host "  fix: resolve dependency version conflicts" -ForegroundColor Gray
Write-Host "  docs: update README with commit guidelines" -ForegroundColor Gray
Write-Host "  feat!: redesign API (breaking change)" -ForegroundColor Gray
Write-Host "" -ForegroundColor White

Write-Host "Next commit will validate formatting and message format!" -ForegroundColor Green
