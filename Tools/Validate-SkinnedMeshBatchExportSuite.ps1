param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\2022.3.53f1c1\Editor\Unity.exe",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ConfigPath = "Assets/EditorConfig/SkinnedMeshSimplification.json",
    [string]$LogDirectory = "Temp/batch-skinned-export-suite-logs",
    [string]$ReportDirectory = "Temp/batch-skinned-export-suite-reports",
    [string]$SummaryReportPath = "Temp/batch-skinned-export-suite-result.json"
)

$ErrorActionPreference = "Stop"

function Resolve-ProjectPath {
    param(
        [string]$BasePath,
        [string]$CandidatePath
    )

    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        throw "A required path argument was empty."
    }

    if ([System.IO.Path]::IsPathRooted($CandidatePath)) {
        return [System.IO.Path]::GetFullPath($CandidatePath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $CandidatePath))
}

function Get-ConfigEntries {
    param(
        [string]$ResolvedConfigPath
    )

    $config = Get-Content -Raw -Path $ResolvedConfigPath | ConvertFrom-Json
    if ($null -eq $config.entries) {
        throw "Config '$ResolvedConfigPath' does not contain an 'entries' array."
    }

    $entries = @($config.entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_.entryId) })
    if ($entries.Count -eq 0) {
        throw "Config '$ResolvedConfigPath' does not contain any valid entryId values."
    }

    return $entries
}

function New-SafeEntryToken {
    param(
        [string]$EntryId,
        [int]$Index
    )

    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($EntryId)
    if ([string]::IsNullOrWhiteSpace($leaf)) {
        $leaf = "entry$Index"
    }

    $safeLeaf = [regex]::Replace($leaf, '[^A-Za-z0-9_-]+', '_')
    if ([string]::IsNullOrWhiteSpace($safeLeaf)) {
        $safeLeaf = "entry$Index"
    }

    return ('{0:D2}_{1}' -f $Index, $safeLeaf)
}

function Write-SummaryReport {
    param(
        [hashtable]$Summary,
        [string]$ResolvedSummaryReportPath
    )

    $summaryDirectory = Split-Path -Parent $ResolvedSummaryReportPath
    if (-not (Test-Path $summaryDirectory)) {
        New-Item -ItemType Directory -Path $summaryDirectory | Out-Null
    }

    $Summary | ConvertTo-Json -Depth 8 | Set-Content -Path $ResolvedSummaryReportPath
}

function New-CoverageSummary {
    return [ordered]@{
        successLogFound = 0
        importValidationLogFound = 0
        restPoseValidationLogFound = 0
        animatedPoseValidationLogFound = 0
        neutralPoseValidationLogFound = 0
        highDeformationPoseValidationLogFound = 0
        outputReadWriteDisabled = 0
        stagingEmpty = 0
        outputUpdated = 0
    }
}

function Add-CoverageCount {
    param(
        $Coverage,
        $EntryReport,
        [string]$PropertyName
    )

    if ($null -eq $Coverage -or $null -eq $EntryReport -or [string]::IsNullOrWhiteSpace($PropertyName)) {
        return
    }

    $property = $EntryReport.PSObject.Properties[$PropertyName]
    if ($null -ne $property -and $property.Value) {
        $Coverage[$PropertyName]++
    }
}

$resolvedProjectPath = Resolve-ProjectPath -BasePath $PWD.Path -CandidatePath $ProjectPath
$resolvedConfigPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ConfigPath
$resolvedLogDirectory = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $LogDirectory
$resolvedReportDirectory = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ReportDirectory
$resolvedSummaryReportPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $SummaryReportPath
$singleEntryScriptPath = Join-Path $PSScriptRoot "Validate-SkinnedMeshBatchExport.ps1"
$pwshPath = (Get-Process -Id $PID).Path

if (-not (Test-Path $UnityPath)) {
    throw "Unity editor was not found at '$UnityPath'."
}

if (-not (Test-Path $resolvedProjectPath)) {
    throw "Project path '$resolvedProjectPath' does not exist."
}

if (-not (Test-Path $resolvedConfigPath)) {
    throw "Config path '$resolvedConfigPath' does not exist."
}

if (-not (Test-Path $singleEntryScriptPath)) {
    throw "Single-entry validation script was not found at '$singleEntryScriptPath'."
}

if (-not (Test-Path $pwshPath)) {
    throw "Could not resolve the current PowerShell host executable."
}

New-Item -ItemType Directory -Force -Path $resolvedLogDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedReportDirectory | Out-Null

$entries = Get-ConfigEntries -ResolvedConfigPath $resolvedConfigPath
$summary = [ordered]@{
    projectPath = $resolvedProjectPath
    configPath = $resolvedConfigPath
    unityPath = $UnityPath
    suiteScriptPath = $MyInvocation.MyCommand.Path
    singleEntryScriptPath = $singleEntryScriptPath
    checkedAtUtc = [datetime]::UtcNow.ToString('o')
    totalEntries = $entries.Count
    passedEntries = 0
    failedEntries = 0
    succeeded = $false
    coverage = New-CoverageSummary
    entries = New-Object System.Collections.Generic.List[object]
}

for ($index = 0; $index -lt $entries.Count; $index++) {
    $entry = $entries[$index]
    $token = New-SafeEntryToken -EntryId $entry.entryId -Index ($index + 1)
    $entryLogPath = Join-Path $resolvedLogDirectory "$token.log"
    $entryReportPath = Join-Path $resolvedReportDirectory "$token.json"

    $entryArguments = @(
        '-NoProfile',
        '-File', $singleEntryScriptPath,
        '-UnityPath', $UnityPath,
        '-ProjectPath', $resolvedProjectPath,
        '-ConfigPath', $resolvedConfigPath,
        '-EntryId', $entry.entryId,
        '-LogPath', $entryLogPath,
        '-ReportPath', $entryReportPath
    )

    & $pwshPath @entryArguments
    $processExitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
    $entryReport = if (Test-Path $entryReportPath) {
        Get-Content -Raw -Path $entryReportPath | ConvertFrom-Json
    }
    else {
        [pscustomobject]@{
            entryId = $entry.entryId
            succeeded = $false
            unityExitCode = $processExitCode
            failureReasons = @("Entry report was not produced: $entryReportPath")
            logPath = $entryLogPath
            reportPath = $entryReportPath
        }
    }

    $summary.entries.Add([pscustomobject]@{
        entryId = $entry.entryId
        reductionPercent = $entry.reductionPercent
        isExternalSource = [bool]$entry.isExternalSource
        externalSourcePath = $entry.externalSourcePath
        outputPath = $entry.outputPath
        logPath = $entryLogPath
        reportPath = $entryReportPath
        processExitCode = $processExitCode
        outputUpdated = $entryReport.outputUpdated
        outputLength = $entryReport.outputLength
        outputReadWriteDisabled = $entryReport.outputReadWriteDisabled
        importValidationLogFound = $entryReport.importValidationLogFound
        restPoseValidationLogFound = $entryReport.restPoseValidationLogFound
        neutralPoseValidationLogFound = $entryReport.neutralPoseValidationLogFound
        highDeformationPoseValidationLogFound = $entryReport.highDeformationPoseValidationLogFound
        stagingEmpty = $entryReport.stagingEmpty
        result = $entryReport
    }) | Out-Null

    if ($entryReport.succeeded) {
        $summary.passedEntries++
    }
    else {
        $summary.failedEntries++
    }

    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'successLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'importValidationLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'restPoseValidationLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'animatedPoseValidationLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'neutralPoseValidationLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'highDeformationPoseValidationLogFound'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'outputReadWriteDisabled'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'stagingEmpty'
    Add-CoverageCount -Coverage $summary.coverage -EntryReport $entryReport -PropertyName 'outputUpdated'
}

$summary.succeeded = ($summary.failedEntries -eq 0)
Write-SummaryReport -Summary $summary -ResolvedSummaryReportPath $resolvedSummaryReportPath

if (-not $summary.succeeded) {
    Write-Host "Skinned mesh batch export suite validation failed."
    Write-Host "Summary: $resolvedSummaryReportPath"
    exit 1
}

Write-Host "Skinned mesh batch export suite validation passed."
Write-Host "Summary: $resolvedSummaryReportPath"
exit 0