#Requires -Version 7.0
<#
.SYNOPSIS
    Fails the build when line coverage is below the agreed thresholds (#8).

.DESCRIPTION
    Reads the Cobertura report merged by ReportGenerator and checks two gates:
      - global line coverage of every instrumented assembly (-MinGlobalLine);
      - aggregated line coverage of the core layers, i.e. assemblies whose name matches
        -CorePattern (Domain and Application by default) (-MinCoreLine).
    The core gate is skipped while the core layers hold no coverable line (empty module skeletons).
    The measured values are printed and appended to the GitHub job summary when available.

.EXAMPLE
    ./scripts/ci/coverage-gate.ps1 -CoberturaPath coverage/Cobertura.xml -MinGlobalLine 60 -MinCoreLine 80
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CoberturaPath,

    [ValidateRange(0, 100)]
    [double] $MinGlobalLine = 60,

    [ValidateRange(0, 100)]
    [double] $MinCoreLine = 80,

    [string] $CorePattern = '\.(Domain|Application)$'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoberturaPath -PathType Leaf)) {
    Write-Output "::error::Coverage report not found: $CoberturaPath"
    exit 1
}

# The report declares an external DTD: never process it (no network access, no entity expansion).
$settings = [System.Xml.XmlReaderSettings]::new()
$settings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
$settings.XmlResolver = $null
$reader = [System.Xml.XmlReader]::Create((Resolve-Path -LiteralPath $CoberturaPath).Path, $settings)
try {
    $doc = [System.Xml.XmlDocument]::new()
    $doc.XmlResolver = $null
    $doc.Load($reader)
}
finally {
    $reader.Dispose()
}

function Get-Rate([int] $covered, [int] $valid) {
    if ($valid -eq 0) { return $null }
    return [math]::Round(100.0 * $covered / $valid, 2)
}

$rows = foreach ($package in $doc.SelectNodes('/coverage/packages/package')) {
    # Class-level lines only: method-level lines are duplicates of the same source lines.
    $lines = $package.SelectNodes('classes/class/lines/line')
    $covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    [pscustomobject]@{
        Assembly = $package.name
        Covered  = $covered
        Valid    = $lines.Count
        IsCore   = $package.name -match $CorePattern
    }
}

$globalCovered = ($rows | Measure-Object Covered -Sum).Sum
$globalValid = ($rows | Measure-Object Valid -Sum).Sum
$core = @($rows | Where-Object IsCore)
$coreCovered = ($core | Measure-Object Covered -Sum).Sum
$coreValid = ($core | Measure-Object Valid -Sum).Sum

$globalRate = Get-Rate $globalCovered $globalValid
$coreRate = Get-Rate $coreCovered $coreValid

$failures = [System.Collections.Generic.List[string]]::new()
if ($null -eq $globalRate) {
    $failures.Add('No coverable line found: coverage was not collected.')
}
elseif ($globalRate -lt $MinGlobalLine) {
    $failures.Add("Global line coverage $globalRate% is below $MinGlobalLine%.")
}
if ($null -ne $coreRate -and $coreRate -lt $MinCoreLine) {
    $failures.Add("Domain/Application line coverage $coreRate% is below $MinCoreLine%.")
}

$coreText = if ($null -eq $coreRate) { 'n/a (no coverable line yet)' } else { "$coreRate% ($coreCovered/$coreValid)" }
$summary = @(
    '### Coverage gate'
    ''
    '| Gate | Measured | Threshold | Result |'
    '| --- | --- | --- | --- |'
    "| Global line coverage | $globalRate% ($globalCovered/$globalValid) | $MinGlobalLine% | $(if ($null -ne $globalRate -and $globalRate -ge $MinGlobalLine) { 'pass' } else { 'FAIL' }) |"
    "| Domain/Application line coverage | $coreText | $MinCoreLine% | $(if ($null -eq $coreRate) { 'skipped' } elseif ($coreRate -ge $MinCoreLine) { 'pass' } else { 'FAIL' }) |"
    ''
    '| Assembly | Layer | Line coverage |'
    '| --- | --- | --- |'
    foreach ($row in $rows | Sort-Object Assembly) {
        $rate = Get-Rate $row.Covered $row.Valid
        "| $($row.Assembly) | $(if ($row.IsCore) { 'core' } else { 'other' }) | $(if ($null -eq $rate) { 'n/a' } else { "$rate% ($($row.Covered)/$($row.Valid))" }) |"
    }
)

$summary | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    $summary | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output "::error::$failure" }
    exit 1
}
Write-Output 'Coverage gate passed.'
