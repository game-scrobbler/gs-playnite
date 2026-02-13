# PowerShell commit-msg hook for Conventional Commits validation
# This script validates that commit messages follow the Conventional Commits specification
# See: https://www.conventionalcommits.org/

param(
    [Parameter(Mandatory=$true)]
    [string]$CommitMsgFile
)

Write-Host "Validating commit message format..." -ForegroundColor Yellow

# Read the commit message
if (-not (Test-Path $CommitMsgFile)) {
    Write-Host "Error: Commit message file not found: $CommitMsgFile" -ForegroundColor Red
    exit 1
}

$commitMsg = Get-Content $CommitMsgFile -Raw

# Skip validation for merge commits
if ($commitMsg -match "^Merge") {
    Write-Host "Merge commit detected, skipping validation." -ForegroundColor Green
    exit 0
}

# Skip validation for revert commits
if ($commitMsg -match "^Revert") {
    Write-Host "Revert commit detected, skipping validation." -ForegroundColor Green
    exit 0
}

# Conventional Commits pattern
# Format: <type>[optional scope]: <description>
# Optional: body and footer with BREAKING CHANGE
$conventionalCommitPattern = '^(build|chore|ci|docs|feat|fix|perf|refactor|revert|style|test)(\(.+\))?(!)?:\s.+'

# Check if commit message matches the pattern
if ($commitMsg -notmatch $conventionalCommitPattern) {
    Write-Host "" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  COMMIT MESSAGE DOES NOT FOLLOW" -ForegroundColor Red
    Write-Host "  CONVENTIONAL COMMITS FORMAT" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "" -ForegroundColor White
    Write-Host "Your commit message:" -ForegroundColor Yellow
    Write-Host "$commitMsg" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Valid format: <type>[optional scope]: <description>" -ForegroundColor Cyan
    Write-Host "" -ForegroundColor White
    Write-Host "Examples:" -ForegroundColor Cyan
    Write-Host "  feat: add new statistics dashboard" -ForegroundColor Gray
    Write-Host "  fix: resolve dependency version conflicts" -ForegroundColor Gray
    Write-Host "  fix(api): correct session tracking bug" -ForegroundColor Gray
    Write-Host "  feat!: redesign API interface" -ForegroundColor Gray
    Write-Host "  docs: update README with version info" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Valid types:" -ForegroundColor Cyan
    Write-Host "  feat:     A new feature (MINOR version bump)" -ForegroundColor Gray
    Write-Host "  fix:      A bug fix (PATCH version bump)" -ForegroundColor Gray
    Write-Host "  docs:     Documentation only changes" -ForegroundColor Gray
    Write-Host "  style:    Code style changes (formatting, etc.)" -ForegroundColor Gray
    Write-Host "  refactor: Code refactoring (no feature/fix)" -ForegroundColor Gray
    Write-Host "  perf:     Performance improvements" -ForegroundColor Gray
    Write-Host "  test:     Adding or updating tests" -ForegroundColor Gray
    Write-Host "  build:    Build system or dependencies" -ForegroundColor Gray
    Write-Host "  ci:       CI/CD configuration changes" -ForegroundColor Gray
    Write-Host "  chore:    Other changes (no production code)" -ForegroundColor Gray
    Write-Host "  revert:   Revert a previous commit" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "Breaking changes:" -ForegroundColor Cyan
    Write-Host "  Add '!' after type or include 'BREAKING CHANGE:' in footer" -ForegroundColor Gray
    Write-Host "  Example: feat!: redesign API (MAJOR version bump)" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "To bypass this check (NOT RECOMMENDED):" -ForegroundColor Yellow
    Write-Host "  git commit --no-verify" -ForegroundColor Gray
    Write-Host "" -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Red

    exit 1
}

# Additional validation: Check for proper capitalization and punctuation
$firstLine = ($commitMsg -split "`n")[0]
$description = ($firstLine -split ':\s+', 2)[1]

if ($description) {
    # Check if description starts with uppercase (should be lowercase)
    if ($description -match '^[A-Z]') {
        Write-Host "" -ForegroundColor White
        Write-Host "Warning: Commit description should start with lowercase letter" -ForegroundColor Yellow
        Write-Host "Current: $description" -ForegroundColor Gray
        Write-Host "" -ForegroundColor White
    }

    # Check if description ends with period (shouldn't)
    if ($description.TrimEnd() -match '\.$') {
        Write-Host "" -ForegroundColor White
        Write-Host "Warning: Commit description should not end with a period" -ForegroundColor Yellow
        Write-Host "Current: $description" -ForegroundColor Gray
        Write-Host "" -ForegroundColor White
    }
}

Write-Host "Commit message format is valid!" -ForegroundColor Green
exit 0
