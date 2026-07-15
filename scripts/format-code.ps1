# Manual code formatting script
# Run this script to format your code before committing

Write-Host "Formatting C# code..." -ForegroundColor Cyan

# Check if dotnet is available
$dotnetVersion = dotnet --version 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: dotnet CLI not found. Please install .NET SDK." -ForegroundColor Red
    exit 1
}

Write-Host "Using .NET SDK version: $dotnetVersion" -ForegroundColor Green

# The solution's old-style WPF .csproj can only be loaded by "dotnet format"
# through a .NET Framework build host, which the repo-pinned .NET 8 SDK does not
# ship. format-sdk.ps1 runs the real "dotnet format" under the newest installed
# SDK that has a working build host, leaving the repo's SDK pin untouched.
. "$PSScriptRoot/format-sdk.ps1"

$sdk = Get-GsFormatSdk
if (-not $sdk) {
    Write-Host "Error: No installed .NET SDK can format this solution." -ForegroundColor Red
    Write-Host "dotnet format needs a .NET Framework build host (BuildHost-net472) that the" -ForegroundColor Yellow
    Write-Host "pinned .NET 8 SDK does not ship. Install the .NET 10 SDK:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Gray
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot

# Run dotnet format (fix mode -no --verify-no-changes) on the whole solution.
Write-Host "Running 'dotnet format GsPlugin.sln' via SDK $sdk..." -ForegroundColor Cyan
Write-Host "This may take a moment..." -ForegroundColor Gray

try {
    $result = Invoke-GsDotnetFormat -RepoRoot $repoRoot -SdkVersion $sdk -ExtraArgs @('--verbosity', 'normal')
    $output = $result.Output

    $joined = ($output -join "`n")
    if ($joined -match 'Unhandled exception|build host could not be found|TypeInitializationException') {
        Write-Host "Error: dotnet format could not load the solution (build host failure)." -ForegroundColor Red
        Write-Host "Install the .NET 10 SDK, then try again." -ForegroundColor Yellow
        exit 1
    }

    if ($result.ExitCode -eq 0) {
        Write-Host "Code formatting completed successfully!" -ForegroundColor Green
        if ($output) {
            Write-Host "Output:" -ForegroundColor Gray
            Write-Host ($output -join [Environment]::NewLine) -ForegroundColor Gray
        }
    } else {
        Write-Host "Code formatting failed (dotnet format exit $($result.ExitCode)):" -ForegroundColor Red
        Write-Host ($output -join [Environment]::NewLine) -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "Error running dotnet format: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "You may need to format files manually or check for syntax errors." -ForegroundColor Yellow
    exit 1
}

# Check if there are any changes after formatting
$changes = git status --porcelain
if ($changes) {
    Write-Host "" -ForegroundColor White
    Write-Host "Files were modified during formatting:" -ForegroundColor Cyan
    git status --short
    Write-Host "" -ForegroundColor White
    Write-Host "Run 'git add .' to stage the formatted changes" -ForegroundColor Green
    Write-Host "Then run 'git commit' to commit your changes" -ForegroundColor Green
} else {
    Write-Host "No formatting changes were needed." -ForegroundColor Green
}

Write-Host "" -ForegroundColor White
Write-Host "Formatting complete!" -ForegroundColor Green
