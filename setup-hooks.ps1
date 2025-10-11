# Setup script for Git hooks on Windows
Write-Host "Setting up Git hooks for GS Playnite..." -ForegroundColor Cyan

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

# Check if pre-commit hooks are installed
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

# Check if commit-msg hooks are installed
if (Test-Path ".git/hooks/commit-msg.ps1") {
    Write-Host "OK Commit-msg PowerShell hook exists" -ForegroundColor Green
} else {
    Write-Host "WARN Commit-msg PowerShell hook not found." -ForegroundColor Yellow
}

if (Test-Path ".git/hooks/commit-msg") {
    Write-Host "OK Commit-msg shell hook exists" -ForegroundColor Green
} else {
    Write-Host "WARN Commit-msg shell hook not found." -ForegroundColor Yellow
}

# Configure Git settings
Write-Host "Configuring Git..." -ForegroundColor Yellow
git config core.autocrlf false
Write-Host "OK Configured Git line endings" -ForegroundColor Green

Write-Host "Git hooks setup complete!" -ForegroundColor Green
Write-Host "" -ForegroundColor White
Write-Host "Installed hooks:" -ForegroundColor White
Write-Host "  1. Pre-commit hook:" -ForegroundColor Cyan
Write-Host "     - Checks C# formatting before commit" -ForegroundColor Gray
Write-Host "     - Warns about unstaged changes" -ForegroundColor Gray
Write-Host "" -ForegroundColor White
Write-Host "  2. Commit-msg hook:" -ForegroundColor Cyan
Write-Host "     - Validates commit messages follow Conventional Commits" -ForegroundColor Gray
Write-Host "     - Enforces proper format: <type>[scope]: <description>" -ForegroundColor Gray
Write-Host "     - Ensures Release Please can auto-version correctly" -ForegroundColor Gray
Write-Host "" -ForegroundColor White

Write-Host "Valid commit message examples:" -ForegroundColor Yellow
Write-Host "  feat: add new statistics dashboard" -ForegroundColor Gray
Write-Host "  fix: resolve dependency version conflicts" -ForegroundColor Gray
Write-Host "  docs: update README with commit guidelines" -ForegroundColor Gray
Write-Host "  feat!: redesign API (breaking change)" -ForegroundColor Gray
Write-Host "" -ForegroundColor White

Write-Host "To test hooks manually:" -ForegroundColor Cyan
Write-Host "  Pre-commit:  powershell -File .git/hooks/pre-commit.ps1" -ForegroundColor White
Write-Host "  Commit-msg:  powershell -File .git/hooks/commit-msg.ps1 .git/COMMIT_EDITMSG" -ForegroundColor White
Write-Host "" -ForegroundColor White

Write-Host "Next commit will validate formatting and message format!" -ForegroundColor Green
