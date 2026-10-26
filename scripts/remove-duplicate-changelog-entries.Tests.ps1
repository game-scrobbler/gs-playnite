<#
.SYNOPSIS
    Pester tests for Remove-DuplicateChangelogEntries (release-highlights-helpers.ps1)
    and scripts/remove-duplicate-changelog-entries.ps1.

.DESCRIPTION
    The fixtures mirror what release-please writes for a PR merged with a merge
    commit whose title matches its commit: the same entry twice, once linked to the
    merge commit and once to the commit. The function takes a merge check as a
    script block; the script's check calls `git rev-list --parents`, which is
    mocked here. The script runs in-process with `&` so Pester measures its
    coverage.

    Run with: Invoke-Pester -Path scripts/remove-duplicate-changelog-entries.Tests.ps1
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'release-highlights-helpers.ps1')
    $script:ScriptPath = Join-Path $PSScriptRoot 'remove-duplicate-changelog-entries.ps1'

    $script:MergeSha = '1c6edf485b49f4edffd01959e30a12d683406d2d'
    $script:CommitSha = '8ee3d53a4024018bf8a7b0aca05bd817d516678e'
    $script:OtherSha = '2b70317f60b2bea6793bcb0e4f4450f0c856ef3d'

    function New-Entry([string]$Text, [string]$Sha) {
        "* $Text ([$($Sha.Substring(0, 7))](https://github.com/game-scrobbler/gs-playnite/commit/$Sha))"
    }

    $script:IsMerge = { param($Sha) $Sha -eq $script:MergeSha }
    $script:NeverMerge = { param($Sha) $false }

    $script:Duplicated = @(
        '# Changelog',
        '',
        '## [2.9.1](https://example.com) (2026-10-03)',
        '',
        '### Bug Fixes',
        '',
        (New-Entry '**telemetry:** fail closed when the scrub times out' $script:OtherSha),
        (New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha),
        (New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:CommitSha),
        '',
        '## [2.9.0](https://example.com) (2026-09-19)',
        '',
        '### Bug Fixes',
        '',
        (New-Entry '**scrobble:** older duplicate' $script:MergeSha),
        (New-Entry '**scrobble:** older duplicate' $script:CommitSha),
        ''
    ) -join "`n"
}

Describe 'Remove-DuplicateChangelogEntries' {

    It 'keeps the entry for the commit and drops the one for the merge commit' {
        $result = Remove-DuplicateChangelogEntries -Changelog $script:Duplicated -Version '2.9.1' -IsMergeCommit $script:IsMerge

        $result.Removed | Should -Be @(New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha)
        $result.Changelog.Contains((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:CommitSha)) | Should -BeTrue
        $result.Changelog.Contains((New-Entry '**telemetry:** fail closed when the scrub times out' $script:OtherSha)) | Should -BeTrue
    }

    It 'keeps the commit entry even when it comes first' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Bug Fixes', '',
            (New-Entry 'thing' $script:CommitSha),
            (New-Entry 'thing' $script:MergeSha)
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit $script:IsMerge

        $result.Removed | Should -Be @(New-Entry 'thing' $script:MergeSha)
    }

    It 'matches an entry that also closes an issue, and keeps that one' {
        $closing = (New-Entry 'thing' $script:CommitSha) + ', closes [#84](https://github.com/game-scrobbler/gs-playnite/issues/84)'
        $changelog = @(
            '## [1.0.0](x)', '', '### Bug Fixes', '',
            (New-Entry 'thing' $script:MergeSha),
            $closing
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit $script:IsMerge

        $result.Removed | Should -Be @(New-Entry 'thing' $script:MergeSha)
        $result.Changelog.Contains($closing) | Should -BeTrue
    }

    It 'leaves other versions alone' {
        $result = Remove-DuplicateChangelogEntries -Changelog $script:Duplicated -Version '2.9.1' -IsMergeCommit $script:IsMerge

        $older = $result.Changelog.Substring($result.Changelog.IndexOf('## [2.9.0]'))
        $older.Contains((New-Entry '**scrobble:** older duplicate' $script:MergeSha)) | Should -BeTrue
        $older.Contains((New-Entry '**scrobble:** older duplicate' $script:CommitSha)) | Should -BeTrue
    }

    It 'changes nothing but the removed line' {
        $result = Remove-DuplicateChangelogEntries -Changelog $script:Duplicated -Version '2.9.1' -IsMergeCommit $script:IsMerge

        $expected = $script:Duplicated.Replace((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha) + "`n", '')
        $result.Changelog | Should -BeExactly $expected
    }

    It 'preserves CRLF line endings' {
        $crlf = $script:Duplicated.Replace("`n", "`r`n")

        $result = Remove-DuplicateChangelogEntries -Changelog $crlf -Version '2.9.1' -IsMergeCommit $script:IsMerge

        $result.Removed.Count | Should -Be 1
        $result.Changelog | Should -BeExactly $crlf.Replace((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha) + "`r`n", '')
    }

    It 'keeps the first entry when none of the repeats is a merge commit' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Bug Fixes', '',
            (New-Entry 'same' $script:CommitSha),
            (New-Entry 'same' $script:OtherSha)
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit $script:NeverMerge

        $result.Removed | Should -Be @(New-Entry 'same' $script:OtherSha)
    }

    It 'keeps the first entry when every repeat is a merge commit' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Bug Fixes', '',
            (New-Entry 'same' $script:CommitSha),
            (New-Entry 'same' $script:OtherSha)
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit { param($Sha) $true }

        $result.Removed | Should -Be @(New-Entry 'same' $script:OtherSha)
    }

    It 'does not treat the same text in different subsections as a repeat' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Features', '',
            (New-Entry 'same' $script:CommitSha),
            '', '### Bug Fixes', '',
            (New-Entry 'same' $script:MergeSha)
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit $script:IsMerge

        $result.Removed.Count | Should -Be 0
        $result.Changelog | Should -BeExactly $changelog
    }

    It 'ignores bullets without a commit link, such as Highlights' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Highlights', '',
            '* Same sentence.', '* Same sentence.'
        ) -join "`n"

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit $script:IsMerge

        $result.Removed.Count | Should -Be 0
    }

    It 'falls back to the short SHA when the link has no full SHA' {
        $changelog = @(
            '## [1.0.0](x)', '', '### Bug Fixes', '',
            '* thing ([1c6edf4](https://example.com/one))',
            '* thing ([8ee3d53](https://example.com/two))'
        ) -join "`n"
        $seen = [System.Collections.Generic.List[string]]::new()

        $result = Remove-DuplicateChangelogEntries -Changelog $changelog -Version '1.0.0' -IsMergeCommit { param($Sha) $seen.Add($Sha); $Sha -eq '1c6edf4' }

        $seen | Should -Contain '1c6edf4'
        $result.Removed | Should -Be @('* thing ([1c6edf4](https://example.com/one))')
    }

    It 'returns the changelog unchanged when the version has no section' {
        $result = Remove-DuplicateChangelogEntries -Changelog $script:Duplicated -Version '9.9.9' -IsMergeCommit $script:IsMerge

        $result.Removed.Count | Should -Be 0
        $result.Changelog | Should -BeExactly $script:Duplicated
    }
}

Describe 'remove-duplicate-changelog-entries.ps1' {

    BeforeEach {
        $script:TestDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path $script:TestDir -Force | Out-Null
        $script:ChangelogPath = Join-Path $script:TestDir 'CHANGELOG.md'
        $script:ManifestPath = Join-Path $script:TestDir '.release-please-manifest.json'
        Set-Content -Path $script:ManifestPath -Value '{ ".": "2.9.1" }' -NoNewline
        Set-Content -Path $script:ChangelogPath -Value $script:Duplicated -NoNewline

        # git rev-list --parents -n 1 <sha>: the commit, then its parents. The mock body
        # does not see this file's $script: variables, so the merge SHA is spelled out.
        Mock git {
            $global:LASTEXITCODE = 0
            $sha = $args[-1]
            if ($sha -eq '1c6edf485b49f4edffd01959e30a12d683406d2d') { "$sha aaaa bbbb" } else { "$sha aaaa" }
        }
    }

    AfterEach {
        Remove-Item -Path $script:TestDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'removes the merge commit entry from the release version and reports it' {
        $global:LASTEXITCODE = 0
        $output = & $script:ScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-String

        $LASTEXITCODE | Should -Be 0
        $output.Contains('Removed 1 duplicate changelog entries for 2.9.1') | Should -BeTrue
        $updated = Get-Content -Path $script:ChangelogPath -Raw
        $updated.Contains((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha)) | Should -BeFalse
        $updated.Contains((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:CommitSha)) | Should -BeTrue
        Should -Invoke git -ParameterFilter { $args -contains '--parents' }
    }

    It 'is idempotent' {
        & $script:ScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-Null
        $afterFirst = Get-Content -Path $script:ChangelogPath -Raw

        $global:LASTEXITCODE = 0
        $output = & $script:ScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-String

        $LASTEXITCODE | Should -Be 0
        $output.Contains('No duplicate changelog entries for 2.9.1') | Should -BeTrue
        (Get-Content -Path $script:ChangelogPath -Raw) | Should -BeExactly $afterFirst
    }

    It 'keeps the first entry when git cannot read the commits' {
        Mock git { $global:LASTEXITCODE = 128 }

        & $script:ScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-Null

        $updated = Get-Content -Path $script:ChangelogPath -Raw
        $updated.Contains((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:MergeSha)) | Should -BeTrue
        $updated.Contains((New-Entry '**telemetry:** keep non-Playnite hosts off the production DSN' $script:CommitSha)) | Should -BeFalse
    }

    It 'warns and exits 0 when the version cannot be read' {
        Set-Content -Path $script:ManifestPath -Value '{}' -NoNewline

        $global:LASTEXITCODE = 0
        $output = & $script:ScriptPath -ChangelogFile $script:ChangelogPath -ManifestFile $script:ManifestPath *>&1 | Out-String

        $LASTEXITCODE | Should -Be 0
        $output.Contains('::warning::Could not read the release version') | Should -BeTrue
        (Get-Content -Path $script:ChangelogPath -Raw) | Should -BeExactly $script:Duplicated
    }
}
