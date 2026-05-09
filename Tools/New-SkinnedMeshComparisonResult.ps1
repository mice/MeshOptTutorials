param(
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$BenchmarkSetPath = "Assets/EditorConfig/SkinnedMeshBenchmarkSet_V1.json",
    [string]$SuiteSummaryPath = "Temp/batch-skinned-export-suite-result.json",
    [string]$OutputPath = "Temp/skinned-mesh-comparison-result-v1.json",
    [string]$ComparisonSetId = "skinned-mesh-comparison-seed-v1"
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

function Resolve-ExistingPreferredAssetPath {
    param(
        [string]$ResolvedProjectPath,
        $CandidateAssetPaths
    )

    if ($null -eq $CandidateAssetPaths) {
        return $null
    }

    foreach ($candidateAssetPath in $CandidateAssetPaths) {
        if ([string]::IsNullOrWhiteSpace($candidateAssetPath)) {
            continue
        }

        $resolvedCandidatePath = Resolve-ProjectPath -BasePath $ResolvedProjectPath -CandidatePath $candidateAssetPath
        if (Test-Path $resolvedCandidatePath) {
            return $candidateAssetPath
        }
    }

    return $null
}

function Convert-ToPoseResult {
    param(
        [string]$Status,
        $ClipAssetPath,
        [string]$Details
    )

    $normalizedClipAssetPath = if ([string]::IsNullOrWhiteSpace($ClipAssetPath)) {
        $null
    }
    else {
        $ClipAssetPath
    }

    $clipName = if ([string]::IsNullOrWhiteSpace($normalizedClipAssetPath)) {
        $null
    }
    else {
        [System.IO.Path]::GetFileNameWithoutExtension($normalizedClipAssetPath)
    }

    return [ordered]@{
        status = $Status
        clipAssetPath = $normalizedClipAssetPath
        clipName = $clipName
        details = $Details
    }
}

function Get-PoseStatus {
    param(
        $SuiteEntry,
        [string]$PropertyName
    )

    if ($null -eq $SuiteEntry) {
        return 'not_computed'
    }

    $property = $SuiteEntry.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return 'not_computed'
    }

    if ($property.Value) {
        return 'pass'
    }

    return 'fail'
}

function New-MetricCoverage {
    return [ordered]@{
        jointRegionP95Error = 0
        globalP95Error = 0
        maxError = 0
        screenSpaceDiff = 0
    }
}

$resolvedProjectPath = Resolve-ProjectPath -BasePath $PWD.Path -CandidatePath $ProjectPath
$resolvedBenchmarkSetPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $BenchmarkSetPath
$resolvedSuiteSummaryPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $SuiteSummaryPath
$resolvedOutputPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $OutputPath

if (-not (Test-Path $resolvedBenchmarkSetPath)) {
    throw "Benchmark set path '$resolvedBenchmarkSetPath' does not exist."
}

if (-not (Test-Path $resolvedSuiteSummaryPath)) {
    throw "Suite summary path '$resolvedSuiteSummaryPath' does not exist."
}

$benchmarkSet = Get-Content -Raw -Path $resolvedBenchmarkSetPath | ConvertFrom-Json
$suiteSummary = Get-Content -Raw -Path $resolvedSuiteSummaryPath | ConvertFrom-Json

if ($null -eq $benchmarkSet.entries -or $null -eq $suiteSummary.entries) {
    throw "Benchmark set or suite summary is missing an 'entries' array."
}

$comparisonEntries = New-Object System.Collections.Generic.List[object]

foreach ($benchmarkEntry in $benchmarkSet.entries) {
    $suiteEntry = @($suiteSummary.entries | Where-Object { $_.entryId -eq $benchmarkEntry.entryId } | Select-Object -First 1)
    if ($suiteEntry.Count -eq 0) {
        throw "Suite summary does not contain benchmark entry '$($benchmarkEntry.entryId)'."
    }

    $suiteEntry = $suiteEntry[0]
    $neutralClipAssetPath = Resolve-ExistingPreferredAssetPath -ResolvedProjectPath $resolvedProjectPath -CandidateAssetPaths $benchmarkEntry.requiredClipAssets.neutral
    $highDeformationClipAssetPath = Resolve-ExistingPreferredAssetPath -ResolvedProjectPath $resolvedProjectPath -CandidateAssetPaths $benchmarkEntry.requiredClipAssets.highDeformation

    $comparisonEntries.Add([ordered]@{
        entryId = $benchmarkEntry.entryId
        reductionPercent = [int]$suiteEntry.reductionPercent
        sourceProfile = if ([string]::IsNullOrWhiteSpace($benchmarkEntry.sourceProfile)) { 'unknown' } else { $benchmarkEntry.sourceProfile }
        externalSourcePath = if ([string]::IsNullOrWhiteSpace($suiteEntry.externalSourcePath)) { $null } else { $suiteEntry.externalSourcePath }
        outputAssetPath = $suiteEntry.outputPath
        suiteEntryReportPath = $suiteEntry.reportPath
        gate = [ordered]@{
            successLogFound = [bool]$suiteEntry.result.successLogFound
            importValidationLogFound = [bool]$suiteEntry.importValidationLogFound
            restPoseValidationLogFound = [bool]$suiteEntry.restPoseValidationLogFound
            neutralPoseValidationLogFound = [bool]$suiteEntry.neutralPoseValidationLogFound
            highDeformationPoseValidationLogFound = [bool]$suiteEntry.highDeformationPoseValidationLogFound
            outputReadWriteDisabled = [bool]$suiteEntry.outputReadWriteDisabled
            stagingEmpty = [bool]$suiteEntry.stagingEmpty
            outputUpdated = [bool]$suiteEntry.outputUpdated
            failureReasons = @($suiteEntry.result.failureReasons)
        }
        representativePoses = [ordered]@{
            rest = Convert-ToPoseResult -Status (Get-PoseStatus -SuiteEntry $suiteEntry -PropertyName 'restPoseValidationLogFound') -ClipAssetPath $null -Details 'baked_rest_pose'
            neutral = Convert-ToPoseResult -Status (Get-PoseStatus -SuiteEntry $suiteEntry -PropertyName 'neutralPoseValidationLogFound') -ClipAssetPath $neutralClipAssetPath -Details 'selected from benchmark neutral clip priority list'
            highDeformation = Convert-ToPoseResult -Status (Get-PoseStatus -SuiteEntry $suiteEntry -PropertyName 'highDeformationPoseValidationLogFound') -ClipAssetPath $highDeformationClipAssetPath -Details 'selected from benchmark high-deformation clip priority list'
        }
        comparison = [ordered]@{
            status = 'not_computed'
            normalizationBasis = $benchmarkSet.normalization
            metrics = [ordered]@{
                jointRegionP95Error = $null
                globalP95Error = $null
                maxError = $null
                screenSpaceDiff = $null
            }
            worstFrames = [ordered]@{
                jointRegion = @()
                global = @()
            }
            artifacts = [ordered]@{
                sideBySideRenderPath = $null
                errorHeatmapPath = $null
                perFrameCurvePath = $null
                worstFrameManifestPath = $null
            }
        }
    }) | Out-Null
}

$comparisonResult = [ordered]@{
    schemaVersion = 'v1'
    comparisonSetId = $ComparisonSetId
    benchmarkSetId = $benchmarkSet.benchmarkSetId
    generatedAtUtc = [datetime]::UtcNow.ToString('o')
    generator = [ordered]@{
        tool = 'Tools/New-SkinnedMeshComparisonResult.ps1'
        toolVersion = $null
        unityVersion = '2022.3.53f1c1'
        suiteSummaryPath = $resolvedSuiteSummaryPath
    }
    normalization = $benchmarkSet.normalization
    aggregate = [ordered]@{
        totalEntries = $comparisonEntries.Count
        computedEntries = 0
        gateCoverage = [ordered]@{
            successLogFound = [int]$suiteSummary.coverage.successLogFound
            importValidationLogFound = [int]$suiteSummary.coverage.importValidationLogFound
            restPoseValidationLogFound = [int]$suiteSummary.coverage.restPoseValidationLogFound
            neutralPoseValidationLogFound = [int]$suiteSummary.coverage.neutralPoseValidationLogFound
            highDeformationPoseValidationLogFound = [int]$suiteSummary.coverage.highDeformationPoseValidationLogFound
            outputReadWriteDisabled = [int]$suiteSummary.coverage.outputReadWriteDisabled
            stagingEmpty = [int]$suiteSummary.coverage.stagingEmpty
            outputUpdated = [int]$suiteSummary.coverage.outputUpdated
        }
        metricCoverage = New-MetricCoverage
    }
    entries = $comparisonEntries
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$comparisonResult | ConvertTo-Json -Depth 10 | Set-Content -Path $resolvedOutputPath

Write-Host "Skinned mesh comparison result generated."
Write-Host "Output: $resolvedOutputPath"
exit 0