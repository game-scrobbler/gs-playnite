param(
    # Pester's JaCoCo report (CodeCoverage.OutputFormat = 'JaCoCo').
    [Parameter(Mandatory = $true)]
    [string]$JaCoCoPath,

    # Where to write the SonarQube generic coverage report (sonar.coverageReportPaths).
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    # The directory Pester's package names are relative to: the parent of the
    # CodeCoverage.Path directories, which is the repository root in CI.
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot
)

# SonarQube Cloud has no PowerShell coverage importer, so Pester's JaCoCo line data is
# rewritten in the generic coverage format, which works for any indexed file. A line
# counts as covered when any of its commands ran (ci > 0). Paths are written absolute
# because the scanner resolves relative ones against a module root that SonarScanner
# for .NET chooses itself.

$ErrorActionPreference = "Stop"

$settings = [System.Xml.XmlReaderSettings]::new()
# Pester writes a DOCTYPE that points at report.dtd, which does not exist.
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
$reader = [System.Xml.XmlReader]::Create((Resolve-Path -LiteralPath $JaCoCoPath).ProviderPath, $settings)
try {
    $report = [System.Xml.XmlDocument]::new()
    $report.Load($reader)
}
finally {
    $reader.Dispose()
}

$root = (Resolve-Path -LiteralPath $SourceRoot).ProviderPath
# path -> (line number -> covered); a file or line listed twice is covered if any entry is.
$files = [ordered]@{}
foreach ($package in $report.SelectNodes("/report/package")) {
    foreach ($sourceFile in $package.SelectNodes("sourcefile")) {
        $path = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $root $package.GetAttribute("name")) $sourceFile.GetAttribute("name")))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Coverage names '$path', which does not exist under $root"
        }
        if (-not $files.Contains($path)) {
            $files[$path] = [System.Collections.Generic.SortedDictionary[int, bool]]::new()
        }
        foreach ($line in $sourceFile.SelectNodes("line")) {
            $number = [int]$line.GetAttribute("nr")
            if ($number -lt 1) {
                throw "Coverage for '$path' has line number $number"
            }
            $covered = [int]$line.GetAttribute("ci") -gt 0
            $files[$path][$number] = $covered -or ($files[$path].ContainsKey($number) -and $files[$path][$number])
        }
    }
}

if ($files.Count -eq 0) {
    throw "$JaCoCoPath has no source files, so there is no coverage to report"
}

$writerSettings = [System.Xml.XmlWriterSettings]::new()
$writerSettings.Indent = $true
$writerSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
$writer = [System.Xml.XmlWriter]::Create([System.IO.Path]::GetFullPath($OutputPath), $writerSettings)
try {
    $writer.WriteStartElement("coverage")
    $writer.WriteAttributeString("version", "1")
    foreach ($path in $files.Keys) {
        $writer.WriteStartElement("file")
        $writer.WriteAttributeString("path", $path)
        foreach ($entry in $files[$path].GetEnumerator()) {
            $writer.WriteStartElement("lineToCover")
            $writer.WriteAttributeString("lineNumber", [string]$entry.Key)
            $writer.WriteAttributeString("covered", $(if ($entry.Value) { "true" } else { "false" }))
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
}
finally {
    $writer.Dispose()
}

$lineCount = ($files.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
$coveredCount = ($files.Values | ForEach-Object { @($_.Values | Where-Object { $_ }).Count } | Measure-Object -Sum).Sum
Write-Host "Wrote $($OutputPath): $($files.Count) files, $coveredCount of $lineCount lines covered."
