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

# Get all C# files in the project
$csFiles = Get-ChildItem -Recurse -Include "*.cs" -Exclude "**/bin/**", "**/obj/**", "**/packages/**" | Where-Object { $_.FullName -notmatch "(bin|obj|packages)" }

if ($csFiles.Count -eq 0) {
    Write-Host "No C# files found to format." -ForegroundColor Yellow
    exit 0
}

Write-Host "Found $($csFiles.Count) C# files to format" -ForegroundColor Green

# Run dotnet format on the solution with progress indication
Write-Host "Running 'dotnet format .\GsPlugin.sln'..." -ForegroundColor Cyan
Write-Host "This may take a moment..." -ForegroundColor Gray

try {
    # Run the format command and capture output
    $output = dotnet format .\GsPlugin.sln --verbosity normal 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Code formatting completed successfully!" -ForegroundColor Green
        if ($output) {
            Write-Host "Output:" -ForegroundColor Gray
            Write-Host $output -ForegroundColor Gray
        }
    } else {
        Write-Host "Code formatting completed with warnings:" -ForegroundColor Yellow
        Write-Host $output -ForegroundColor Yellow
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
