# Shared helper for running "dotnet format" on this solution.
#
# The solution includes an old-style (non-SDK) WPF .csproj that targets .NET
# Framework. "dotnet format" can only load such a project through a .NET
# Framework build host (BuildHost-net472). The repo pins the .NET 8 SDK via
# global.json for reproducible builds/tests, but the 8.x SDK's bundled
# dotnet-format ships no working net472 build host, so a plain "dotnet format"
# crashes with "The build host could not be found". The .NET 10 SDK's build host
# loads the project correctly.
#
# These helpers locate the newest installed SDK that has a working build host and
# run "dotnet format" under it -- via a throwaway global.json in a temp folder --
# so formatting works without changing the SDK the repo pins for builds/tests.

function Get-GsFormatSdk {
    # Returns the newest installed SDK version whose dotnet-format ships a .NET
    # Framework build host, requiring major version >= 10. The 8.x SDK ships no
    # working net472 host and the 9.x host crashes loading this WPF project, so
    # neither qualifies. Returns $null when none qualifies -- callers should
    # surface a clear "install a newer SDK" message.
    $sdks = & dotnet --list-sdks 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $sdks) { return $null }

    $candidates = foreach ($line in $sdks) {
        # Each line looks like: "10.0.302 [C:\Program Files\dotnet\sdk]"
        if ($line -match '^(?<ver>\S+)\s+\[(?<dir>.+)\]$') {
            $ver = $Matches['ver']
            $dir = $Matches['dir']
            $major = [int]($ver.Split('.')[0])
            $host472 = Join-Path $dir (Join-Path $ver 'DotnetTools/dotnet-format/BuildHost-net472/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe')
            if ($major -ge 10 -and (Test-Path -LiteralPath $host472)) {
                [pscustomobject]@{
                    Version = $ver
                    # Parse the numeric prefix (drop any -prerelease tag) so
                    # versions compare semantically: 10.0.10 outranks 10.0.9.
                    Parsed  = [version](($ver -split '-')[0])
                }
            }
        }
    }
    if (-not $candidates) { return $null }

    return ($candidates | Sort-Object -Property Parsed -Descending | Select-Object -First 1).Version
}

function Invoke-GsDotnetFormat {
    # Runs "dotnet format <solution> <ExtraArgs>" under the given SDK. The SDK is
    # selected with a throwaway global.json in a temp directory we cd into, so the
    # repo's own global.json (pinned to .NET 8) is left untouched. Returns a
    # hashtable @{ ExitCode; Output }, where Output is an array of plain strings
    # (dotnet's stderr diagnostics are stringified to avoid PowerShell
    # NativeCommandError noise).
    param(
        [Parameter(Mandatory)][string]   $RepoRoot,
        [Parameter(Mandatory)][string]   $SdkVersion,
        [string[]]                        $ExtraArgs = @()
    )

    $sln = Join-Path $RepoRoot 'GsPlugin.sln'
    $sdkDir = Join-Path ([System.IO.Path]::GetTempPath()) 'gs-playnite-format-sdk'
    New-Item -ItemType Directory -Force -Path $sdkDir | Out-Null

    $globalJson = @"
{
  "sdk": {
    "version": "$SdkVersion",
    "rollForward": "disable",
    "allowPrerelease": true
  }
}
"@
    [System.IO.File]::WriteAllText((Join-Path $sdkDir 'global.json'), $globalJson)

    Push-Location $sdkDir
    try {
        # dotnet writes formatting diagnostics to stderr; with 2>&1 those become
        # error records. Force Continue (function-local) so stderr does not turn
        # into a terminating error, and stringify each record to plain text.
        $ErrorActionPreference = 'Continue'
        $output = & dotnet format $sln @ExtraArgs 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    return @{ ExitCode = $code; Output = $output }
}
