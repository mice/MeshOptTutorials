param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\2022.3.53f1c1\Editor\Unity.exe",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ConfigPath = "Assets/EditorConfig/SkinnedMeshSimplification.json",
    [string]$ComparisonResultPath = "Temp/skinned-mesh-comparison-result-v1.json",
    [string]$LogDirectory = "Temp/skinned-mesh-comparison-metric-logs",
    [string]$ReportDirectory = "Temp/skinned-mesh-comparison-metric-reports"
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

function Set-PoseFromMetricReport {
    param(
        $PoseObject,
        $PoseMetric
    )

    if ($null -eq $PoseObject -or $null -eq $PoseMetric) {
        return
    }

    $PoseObject.status = $PoseMetric.status
    $PoseObject.clipAssetPath = if ([string]::IsNullOrWhiteSpace($PoseMetric.clipAssetPath)) { $null } else { $PoseMetric.clipAssetPath }
    $PoseObject.clipName = if ([string]::IsNullOrWhiteSpace($PoseMetric.clipName)) { $null } else { $PoseMetric.clipName }
    $PoseObject.details = if ([string]::IsNullOrWhiteSpace($PoseMetric.details)) { $null } else { $PoseMetric.details }
}

$resolvedProjectPath = Resolve-ProjectPath -BasePath $PWD.Path -CandidatePath $ProjectPath
$resolvedConfigPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ConfigPath
$resolvedComparisonResultPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ComparisonResultPath
$resolvedLogDirectory = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $LogDirectory
$resolvedReportDirectory = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ReportDirectory

if (-not (Test-Path $UnityPath)) {
    throw "Unity editor was not found at '$UnityPath'."
}

if (-not (Test-Path $resolvedConfigPath)) {
    throw "Config path '$resolvedConfigPath' does not exist."
}

if (-not (Test-Path $resolvedComparisonResultPath)) {
    throw "Comparison result path '$resolvedComparisonResultPath' does not exist."
}

New-Item -ItemType Directory -Force -Path $resolvedLogDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $resolvedReportDirectory | Out-Null

$comparisonResult = Get-Content -Raw -Path $resolvedComparisonResultPath | ConvertFrom-Json
if ($null -eq $comparisonResult.entries) {
    throw "Comparison result '$resolvedComparisonResultPath' does not contain an 'entries' array."
}

$metricCoverage = [ordered]@{
    jointRegionP95Error = 0
    globalP95Error = 0
    maxError = 0
    screenSpaceDiff = 0
}

$computedEntries = 0

for ($index = 0; $index -lt $comparisonResult.entries.Count; $index++) {
    $entry = $comparisonResult.entries[$index]
    $token = New-SafeEntryToken -EntryId $entry.entryId -Index ($index + 1)
    $entryLogPath = Join-Path $resolvedLogDirectory "$token.log"
    $entryReportPath = Join-Path $resolvedReportDirectory "$token.json"

    Remove-Item -Path $entryLogPath -ErrorAction SilentlyContinue
    Remove-Item -Path $entryReportPath -ErrorAction SilentlyContinue

    $unityArguments = @(
        '-batchmode',
        '-quit',
        '-projectPath', $resolvedProjectPath,
        '-executeMethod', 'MeshEditorUtils.Batch_ComputeSkinnedMeshComparisonMetrics',
        '-entryId', $entry.entryId,
        '-configPath', $resolvedConfigPath,
        '-reportPath', $entryReportPath,
        '-logFile', $entryLogPath
    )

    $unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -PassThru -Wait
    if (-not (Test-Path $entryReportPath)) {
        throw "Metric report was not produced for '$($entry.entryId)': $entryReportPath"
    }

    $metricReport = Get-Content -Raw -Path $entryReportPath | ConvertFrom-Json

    $entry.suiteEntryReportPath = if ([string]::IsNullOrWhiteSpace($entry.suiteEntryReportPath)) { $null } else { $entry.suiteEntryReportPath }
    $entry.comparison.status = $metricReport.comparisonStatus
    $entry.comparison.normalizationBasis = $metricReport.normalizationBasis
    $entry.comparison.metrics.jointRegionP95Error = if ($metricReport.hasJointRegionP95Error) { $metricReport.jointRegionP95Error } else { $null }
    $entry.comparison.metrics.globalP95Error = if ($metricReport.hasGlobalP95Error) { $metricReport.globalP95Error } else { $null }
    $entry.comparison.metrics.maxError = if ($metricReport.hasMaxError) { $metricReport.maxError } else { $null }
    $entry.comparison.metrics.screenSpaceDiff = if ($metricReport.hasScreenSpaceDiff) { $metricReport.screenSpaceDiff } else { $null }
    $entry.comparison.worstFrames.jointRegion = @($metricReport.jointRegionWorstFrames)
    $entry.comparison.worstFrames.global = @($metricReport.globalWorstFrames)

    $restPoseMetric = $metricReport.representativePoses | Where-Object { $_.category -eq 'rest' } | Select-Object -First 1
    $neutralPoseMetric = $metricReport.representativePoses | Where-Object { $_.category -eq 'neutral' } | Select-Object -First 1
    $highDeformationPoseMetric = $metricReport.representativePoses | Where-Object { $_.category -eq 'highDeformation' } | Select-Object -First 1
    Set-PoseFromMetricReport -PoseObject $entry.representativePoses.rest -PoseMetric $restPoseMetric
    Set-PoseFromMetricReport -PoseObject $entry.representativePoses.neutral -PoseMetric $neutralPoseMetric
    Set-PoseFromMetricReport -PoseObject $entry.representativePoses.highDeformation -PoseMetric $highDeformationPoseMetric

    if ($entry.comparison.status -ne 'not_computed') {
        $computedEntries++
    }

    if ($entry.comparison.metrics.jointRegionP95Error -ne $null) {
        $metricCoverage.jointRegionP95Error++
    }

    if ($entry.comparison.metrics.globalP95Error -ne $null) {
        $metricCoverage.globalP95Error++
    }

    if ($entry.comparison.metrics.maxError -ne $null) {
        $metricCoverage.maxError++
    }

    if ($entry.comparison.metrics.screenSpaceDiff -ne $null) {
        $metricCoverage.screenSpaceDiff++
    }

    if ($unityProcess.ExitCode -ne 0) {
        Write-Warning "Unity exited with code $($unityProcess.ExitCode) while computing metrics for '$($entry.entryId)'. The metric report has still been merged."
    }
}

$comparisonResult.aggregate.computedEntries = $computedEntries
$comparisonResult.aggregate.metricCoverage.jointRegionP95Error = $metricCoverage.jointRegionP95Error
$comparisonResult.aggregate.metricCoverage.globalP95Error = $metricCoverage.globalP95Error
$comparisonResult.aggregate.metricCoverage.maxError = $metricCoverage.maxError
$comparisonResult.aggregate.metricCoverage.screenSpaceDiff = $metricCoverage.screenSpaceDiff
$comparisonResult.generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
$comparisonResult.generator.tool = 'Tools/Update-SkinnedMeshComparisonMetrics.ps1'
$comparisonResult.generator.toolVersion = $null

$comparisonResult | ConvertTo-Json -Depth 12 | Set-Content -Path $resolvedComparisonResultPath

Write-Host "Skinned mesh comparison metrics updated."
Write-Host "Comparison result: $resolvedComparisonResultPath"
exit 0