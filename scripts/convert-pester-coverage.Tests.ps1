<#
.SYNOPSIS
    Pester tests for scripts/convert-pester-coverage.ps1.

.DESCRIPTION
    Each test writes a small JaCoCo report in the shape Pester produces
    (package name relative to the source root, bare sourcefile names, a
    DOCTYPE pointing at a missing report.dtd) next to real source files, runs
    the converter in-process with `&`, and parses the generic coverage XML it
    writes.

    Run with: Invoke-Pester -Path scripts/convert-pester-coverage.Tests.ps1
#>

BeforeAll {
    $script:ConverterPath = Join-Path $PSScriptRoot 'convert-pester-coverage.ps1'

    function New-JaCoCo([string]$Packages) {
        @"
<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<!DOCTYPE report PUBLIC "-//JACOCO//DTD Report 1.1//EN" "report.dtd"[]>
<report name="Pester">
$Packages
</report>
"@
    }

    function Get-CoveredLines($Coverage, [string]$Path) {
        $file = @($Coverage.coverage.file) | Where-Object { $_.path -eq $Path }
        # Keyed by line number. An [ordered] dictionary would read an int index as a position.
        $result = [System.Collections.Generic.SortedDictionary[int, string]]::new()
        foreach ($line in @($file.lineToCover)) {
            $result[[int]$line.lineNumber] = $line.covered
        }
        return , $result
    }
}

Describe 'convert-pester-coverage.ps1' {

    BeforeEach {
        $script:Root = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid())
        New-Item -ItemType Directory -Path (Join-Path $script:Root 'scripts') -Force | Out-Null
        Set-Content -Path (Join-Path $script:Root 'scripts/a.ps1') -Value 'one'
        Set-Content -Path (Join-Path $script:Root 'scripts/b c.ps1') -Value 'two'
        $script:JaCoCoPath = Join-Path $script:Root 'jacoco.xml'
        $script:OutputPath = Join-Path $script:Root 'out/sonar.xml'
        New-Item -ItemType Directory -Path (Join-Path $script:Root 'out') -Force | Out-Null
        $script:FileA = [System.IO.Path]::GetFullPath((Join-Path $script:Root 'scripts/a.ps1'))
        $script:FileB = [System.IO.Path]::GetFullPath((Join-Path $script:Root 'scripts/b c.ps1'))
    }

    AfterEach {
        Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'writes absolute paths and marks a line covered when any command on it ran' {
        Set-Content -Path $script:JaCoCoPath -Value (New-JaCoCo @'
  <package name="scripts">
    <sourcefile name="a.ps1">
      <line nr="2" mi="0" ci="1" mb="0" cb="0" />
      <line nr="3" mi="1" ci="1" mb="0" cb="0" />
      <line nr="5" mi="2" ci="0" mb="0" cb="0" />
    </sourcefile>
    <sourcefile name="b c.ps1">
      <line nr="1" mi="1" ci="0" mb="0" cb="0" />
    </sourcefile>
  </package>
'@)

        $output = & $script:ConverterPath -JaCoCoPath $script:JaCoCoPath -OutputPath $script:OutputPath -SourceRoot $script:Root *>&1 | Out-String

        [xml]$coverage = Get-Content -LiteralPath $script:OutputPath -Raw
        $coverage.coverage.version | Should -Be '1'
        @($coverage.coverage.file).Count | Should -Be 2
        $a = Get-CoveredLines $coverage $script:FileA
        $a.Keys | Should -Be @(2, 3, 5)
        $a[2] | Should -Be 'true'
        $a[3] | Should -Be 'true'
        $a[5] | Should -Be 'false'
        (Get-CoveredLines $coverage $script:FileB)[1] | Should -Be 'false'
        $output.Contains('2 files, 2 of 4 lines covered') | Should -BeTrue
    }

    It 'merges a file listed twice, keeping a line covered if either entry covered it' {
        Set-Content -Path $script:JaCoCoPath -Value (New-JaCoCo @'
  <package name="scripts">
    <sourcefile name="a.ps1">
      <line nr="4" mi="0" ci="1" mb="0" cb="0" />
      <line nr="6" mi="1" ci="0" mb="0" cb="0" />
    </sourcefile>
  </package>
  <package name="scripts">
    <sourcefile name="a.ps1">
      <line nr="4" mi="1" ci="0" mb="0" cb="0" />
      <line nr="6" mi="0" ci="2" mb="0" cb="0" />
    </sourcefile>
  </package>
'@)

        & $script:ConverterPath -JaCoCoPath $script:JaCoCoPath -OutputPath $script:OutputPath -SourceRoot $script:Root *>&1 | Out-Null

        [xml]$coverage = Get-Content -LiteralPath $script:OutputPath -Raw
        @($coverage.coverage.file).Count | Should -Be 1
        $a = Get-CoveredLines $coverage $script:FileA
        $a[4] | Should -Be 'true'
        $a[6] | Should -Be 'true'
    }

    It 'fails when the report names a file that does not exist under the source root' {
        Set-Content -Path $script:JaCoCoPath -Value (New-JaCoCo @'
  <package name="scripts">
    <sourcefile name="missing.ps1">
      <line nr="1" mi="0" ci="1" mb="0" cb="0" />
    </sourcefile>
  </package>
'@)

        { & $script:ConverterPath -JaCoCoPath $script:JaCoCoPath -OutputPath $script:OutputPath -SourceRoot $script:Root } |
            Should -Throw '*missing.ps1*does not exist*'
    }

    It 'fails when the report has no source files' {
        Set-Content -Path $script:JaCoCoPath -Value (New-JaCoCo '')

        { & $script:ConverterPath -JaCoCoPath $script:JaCoCoPath -OutputPath $script:OutputPath -SourceRoot $script:Root } |
            Should -Throw '*no source files*'
    }

    It 'fails on a non-positive line number' {
        Set-Content -Path $script:JaCoCoPath -Value (New-JaCoCo @'
  <package name="scripts">
    <sourcefile name="a.ps1">
      <line nr="0" mi="0" ci="1" mb="0" cb="0" />
    </sourcefile>
  </package>
'@)

        { & $script:ConverterPath -JaCoCoPath $script:JaCoCoPath -OutputPath $script:OutputPath -SourceRoot $script:Root } |
            Should -Throw '*line number 0*'
    }
}
