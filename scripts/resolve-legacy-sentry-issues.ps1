<#
.SYNOPSIS
    Resolves the 25 legacy Sentry issues (all on releases 2.5.0 / 0.5.0 / 0.4.0) that are
    already addressed in the current GsPlugin@2.6.0 code base.

.DESCRIPTION
    Every currently-unresolved gs-playnite issue was confirmed (2026-07-14) to be:
      * on an OLD plugin release (none on 2.6.0), and
      * already fixed / filtered by the shipping 2.6.0 code, OR an exception thrown by a
        DIFFERENT Playnite plugin (QuickSearch/Lucene, Lacro59 backgroundchanger, LiteDB, etc.)
        that the strengthened GsSentry beforeSend filter now drops.

    Marks them "resolved in GsPlugin@2.6.0" so lingering events from un-upgraded users do NOT
    reopen them. Requires a Sentry auth token with event:write (or project:write) scope.

.NOTES
    The read-only token in ~/.sentryclirc is NOT sufficient (it 403s on writes).
    Create a write-scoped token at https://game-scrobbler.sentry.io/settings/account/api/auth-tokens/
    then:  $env:SENTRY_WRITE_TOKEN = "sntryu_..."; ./scripts/resolve-legacy-sentry-issues.ps1
#>

$ErrorActionPreference = 'Stop'
$token = $env:SENTRY_WRITE_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Set `$env:SENTRY_WRITE_TOKEN to a Sentry token with event:write scope first."
}

$org     = 'game-scrobbler'
$project = 'gs-playnite'
$host    = 'de.sentry.io'   # EU region
$release = 'GsPlugin@2.6.0'
$headers = @{ Authorization = "Bearer $token" }

# Pull all currently-unresolved issue ids (short window; the SDK only accepts '', '24h', '14d').
$issues = Invoke-RestMethod -Headers $headers -Method Get `
    -Uri "https://$host/api/0/projects/$org/$project/issues/?query=is%3Aunresolved&statsPeriod=14d&limit=100"

if (-not $issues -or $issues.Count -eq 0) {
    Write-Host "No unresolved issues. Already at zero."
    return
}

Write-Host "Resolving $($issues.Count) issue(s) in $release ..."
$body = @{ status = 'resolved'; statusDetails = @{ inRelease = $release } } | ConvertTo-Json -Compress

foreach ($i in $issues) {
    $uri = "https://$host/api/0/organizations/$org/issues/$($i.id)/"
    Invoke-RestMethod -Headers $headers -Method Put -ContentType 'application/json' -Body $body -Uri $uri | Out-Null
    Write-Host ("  resolved {0,-16} {1}" -f $i.shortId, $i.title)
}

Start-Sleep -Seconds 2
$remaining = Invoke-RestMethod -Headers $headers -Method Get `
    -Uri "https://$host/api/0/projects/$org/$project/issues/?query=is%3Aunresolved&statsPeriod=14d&limit=100"
Write-Host ""
Write-Host "Unresolved remaining: $($remaining.Count)"
if ($remaining.Count -gt 0) {
    Write-Host "If any reopened due to release-package mismatch on bare 0.4.0/0.5.0 events, re-run"
    Write-Host "or archive them (status=ignored) instead:"
    $remaining | ForEach-Object { Write-Host "  $($_.shortId) $($_.title)" }
}
