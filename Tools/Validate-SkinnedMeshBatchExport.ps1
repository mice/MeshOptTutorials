param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\2022.3.53f1c1\Editor\Unity.exe",
    [string]$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ConfigPath = "Assets/EditorConfig/SkinnedMeshSimplification.json",
    [string]$EntryId = "Assets/res/raw/chars/Anim_1325/Export/Anim_1325.FBX",
    [string]$LogPath = "Temp/batch-skinned-export.log",
    [string]$ReportPath = "Temp/batch-skinned-export-result.json"
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

function Get-ConfigEntry {
    param(
        [string]$ResolvedConfigPath,
        [string]$TargetEntryId
    )

    $config = Get-Content -Raw -Path $ResolvedConfigPath | ConvertFrom-Json
    if ($null -eq $config.entries) {
        throw "Config '$ResolvedConfigPath' does not contain an 'entries' array."
    }

    $entry = $config.entries | Where-Object { $_.entryId -eq $TargetEntryId } | Select-Object -First 1
    if ($null -eq $entry) {
        throw "Config '$ResolvedConfigPath' does not contain entryId '$TargetEntryId'."
    }

    return $entry
}

function New-ResultObject {
    param(
        [string]$ResolvedProjectPath,
        [string]$ResolvedConfigPath,
        [string]$TargetEntryId,
        [string]$ResolvedLogPath,
        [string]$ResolvedReportPath,
        [string]$ResolvedOutputPath,
        [string]$ResolvedOutputMetaPath,
        $PreviousOutputTimestampUtc,
        [Nullable[int64]]$PreviousOutputLength
    )

    return [ordered]@{
        projectPath = $ResolvedProjectPath
        configPath = $ResolvedConfigPath
        entryId = $TargetEntryId
        logPath = $ResolvedLogPath
        reportPath = $ResolvedReportPath
        outputPath = $ResolvedOutputPath
        outputMetaPath = $ResolvedOutputMetaPath
        unityPath = $UnityPath
        unityExitCode = $null
        successLogFound = $false
        importValidationLogFound = $false
        restPoseValidationLogFound = $false
        animatedPoseValidationLogFound = $false
        neutralPoseValidationLogFound = $false
        highDeformationPoseValidationLogFound = $false
        outputExists = $false
        outputUpdated = $false
        outputLength = $null
        outputLastWriteTimeUtc = $null
        outputReadWriteDisabled = $false
        outputImporterReadable = $null
        previousOutputLastWriteTimeUtc = if ($null -ne $PreviousOutputTimestampUtc) { ([datetime]$PreviousOutputTimestampUtc).ToString("o") } else { $null }
        previousOutputLength = if ($PreviousOutputLength -ne $null) { [int64]$PreviousOutputLength } else { $null }
        stagingEmpty = $false
        succeeded = $false
        checkedAtUtc = [datetime]::UtcNow.ToString("o")
        failureReasons = New-Object System.Collections.Generic.List[string]
    }
}

function Write-ResultFile {
    param(
        [hashtable]$Result,
        [string]$ResolvedReportPath
    )

    $reportDirectory = Split-Path -Parent $ResolvedReportPath
    if (-not (Test-Path $reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory | Out-Null
    }

    $Result | ConvertTo-Json -Depth 5 | Set-Content -Path $ResolvedReportPath
}

$resolvedProjectPath = Resolve-ProjectPath -BasePath $PWD.Path -CandidatePath $ProjectPath
$resolvedConfigPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ConfigPath
$resolvedLogPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $LogPath
$resolvedReportPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $ReportPath

if (-not (Test-Path $UnityPath)) {
    throw "Unity editor was not found at '$UnityPath'."
}

if (-not (Test-Path $resolvedProjectPath)) {
    throw "Project path '$resolvedProjectPath' does not exist."
}

if (-not (Test-Path $resolvedConfigPath)) {
    throw "Config path '$resolvedConfigPath' does not exist."
}

$entry = Get-ConfigEntry -ResolvedConfigPath $resolvedConfigPath -TargetEntryId $EntryId

if ([string]::IsNullOrWhiteSpace($entry.outputPath)) {
    throw "Config entry '$EntryId' is missing outputPath."
}

$resolvedOutputPath = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath $entry.outputPath
$resolvedOutputMetaPath = "$resolvedOutputPath.meta"
$previousOutput = Get-Item -Path $resolvedOutputPath -ErrorAction SilentlyContinue
$previousOutputTimestampUtc = if ($null -ne $previousOutput) { $previousOutput.LastWriteTimeUtc } else { $null }
$previousOutputLength = if ($null -ne $previousOutput) { [int64]$previousOutput.Length } else { $null }

$result = New-ResultObject `
    -ResolvedProjectPath $resolvedProjectPath `
    -ResolvedConfigPath $resolvedConfigPath `
    -TargetEntryId $EntryId `
    -ResolvedLogPath $resolvedLogPath `
    -ResolvedReportPath $resolvedReportPath `
    -ResolvedOutputPath $resolvedOutputPath `
    -ResolvedOutputMetaPath $resolvedOutputMetaPath `
    -PreviousOutputTimestampUtc $previousOutputTimestampUtc `
    -PreviousOutputLength $previousOutputLength

$logDirectory = Split-Path -Parent $resolvedLogPath
if (-not (Test-Path $logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
}

Remove-Item -Path $resolvedLogPath -ErrorAction SilentlyContinue

$unityArguments = @(
    "-batchmode",
    "-quit",
    "-projectPath", $resolvedProjectPath,
    "-executeMethod", "MeshEditorUtils.Batch_SimplifySkinMeshToFbx",
    "-entryId", $EntryId,
    "-configPath", $ConfigPath,
    "-logFile", $resolvedLogPath
)

$unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $unityArguments -PassThru -Wait
$result.unityExitCode = $unityProcess.ExitCode

$logContent = if (Test-Path $resolvedLogPath) { Get-Content -Raw -Path $resolvedLogPath } else { "" }
$expectedSuccessLine = "Batch exported trimmed FBX: '$($entry.outputPath)'"
$expectedImportValidationPrefix = "Batch validated trimmed FBX import:"
$expectedRestPoseValidationPrefix = "Batch validated trimmed FBX rest pose:"
$expectedAnimatedPoseValidationPrefix = "Batch validated trimmed FBX animated poses:"
$expectedNeutralPoseValidationToken = "category='neutral', skipped=false"
$expectedHighDeformationPoseValidationToken = "category='highDeformation', skipped=false"
$result.successLogFound = $logContent.Contains($expectedSuccessLine)
if (-not $result.successLogFound) {
    $result.failureReasons.Add("Missing success log line: $expectedSuccessLine")
}

$result.importValidationLogFound = $logContent.Contains($expectedImportValidationPrefix)
if (-not $result.importValidationLogFound) {
    $result.failureReasons.Add("Missing imported-output validation log line: $expectedImportValidationPrefix")
}

$result.restPoseValidationLogFound = $logContent.Contains($expectedRestPoseValidationPrefix)
if (-not $result.restPoseValidationLogFound) {
    $result.failureReasons.Add("Missing rest-pose validation log line: $expectedRestPoseValidationPrefix")
}

$result.animatedPoseValidationLogFound = $logContent.Contains($expectedAnimatedPoseValidationPrefix)
if (-not $result.animatedPoseValidationLogFound) {
    $result.failureReasons.Add("Missing animated-pose validation log line: $expectedAnimatedPoseValidationPrefix")
}

$result.neutralPoseValidationLogFound = $logContent.Contains($expectedNeutralPoseValidationToken)
if (-not $result.neutralPoseValidationLogFound) {
    $result.failureReasons.Add("Missing neutral-pose validation token: $expectedNeutralPoseValidationToken")
}

$result.highDeformationPoseValidationLogFound = $logContent.Contains($expectedHighDeformationPoseValidationToken)
if (-not $result.highDeformationPoseValidationLogFound) {
    $result.failureReasons.Add("Missing high-deformation validation token: $expectedHighDeformationPoseValidationToken")
}

$outputFile = Get-Item -Path $resolvedOutputPath -ErrorAction SilentlyContinue
$result.outputExists = $null -ne $outputFile
if (-not $result.outputExists) {
    $result.failureReasons.Add("Expected output file was not found: $resolvedOutputPath")
}
else {
    $result.outputLength = [int64]$outputFile.Length
    $result.outputLastWriteTimeUtc = $outputFile.LastWriteTimeUtc.ToString("o")
    $result.outputUpdated = ($null -eq $previousOutput) -or ($outputFile.LastWriteTimeUtc -gt $previousOutputTimestampUtc)

    if (-not $result.outputUpdated) {
        $result.failureReasons.Add("Output file timestamp did not advance: $resolvedOutputPath")
    }
}

$outputMetaFile = Get-Item -Path $resolvedOutputMetaPath -ErrorAction SilentlyContinue
if ($null -eq $outputMetaFile) {
    $result.failureReasons.Add("Expected output meta file was not found: $resolvedOutputMetaPath")
}
else {
    $metaContent = Get-Content -Raw -Path $resolvedOutputMetaPath
    $readableMatch = [regex]::Match($metaContent, '(?m)^\s*isReadable:\s*(\d+)\s*$')
    if (-not $readableMatch.Success) {
        $result.failureReasons.Add("Could not determine output importer readability from: $resolvedOutputMetaPath")
    }
    else {
        $isReadableValue = $readableMatch.Groups[1].Value
        $result.outputImporterReadable = ($isReadableValue -ne '0')
        $result.outputReadWriteDisabled = ($isReadableValue -eq '0')

        if (-not $result.outputReadWriteDisabled) {
            $result.failureReasons.Add("Output importer is still read/write enabled: $resolvedOutputMetaPath")
        }
    }
}

$stagingDirectory = Resolve-ProjectPath -BasePath $resolvedProjectPath -CandidatePath "Assets/raw_temp/models"
$stagedItems = Get-ChildItem -Path $stagingDirectory -Force -ErrorAction SilentlyContinue
$result.stagingEmpty = ($null -eq $stagedItems) -or ($stagedItems.Count -eq 0)
if (-not $result.stagingEmpty) {
    $result.failureReasons.Add("Staging directory was not cleaned: $stagingDirectory")
}

if ($result.unityExitCode -ne 0) {
    $result.failureReasons.Add("Unity exited with code $($result.unityExitCode).")
}

$result.succeeded = ($result.failureReasons.Count -eq 0)
Write-ResultFile -Result $result -ResolvedReportPath $resolvedReportPath

if (-not $result.succeeded) {
    Write-Host "Skinned mesh batch export validation failed."
    $result.failureReasons | ForEach-Object { Write-Host "- $_" }
    Write-Host "Report: $resolvedReportPath"
    exit 1
}

Write-Host "Skinned mesh batch export validation passed."
Write-Host "Output: $resolvedOutputPath"
Write-Host "Report: $resolvedReportPath"
exit 0