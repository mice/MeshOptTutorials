using Autodesk.Fbx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static partial class MeshEditorUtils
{
    private const string TrimFbxMenuPath = "Assets/skin/SimplifyToFBX";
    private const string TempStagingAssetDirectory = "Assets/raw_temp/models";
    private const string BatchEntryIdArgument = "-entryId";
    private const string BatchConfigPathArgument = "-configPath";
    private const string BatchReportPathArgument = "-reportPath";
    private const double CentimetersPerUnityUnit = 100.0;
    private const float AnimatedPoseValidationNormalizedTime = 0.5f;
    private const float DefaultAnimatedPoseMinDiagonalRatio = 0.5f;
    private const float DefaultAnimatedPoseMaxDiagonalRatio = 1.5f;
    private const float DefaultAnimatedPoseMaxCenterOffsetRatio = 0.25f;
    private const float HighDeformationAnimatedPoseMaxDiagonalRatio = 1.6f;
    private const float HighDeformationAnimatedPoseMaxCenterOffsetRatio = 0.3f;
    private const int ScreenSpaceDiffRenderResolution = 128;
    private static readonly string[] NeutralPoseValidationClipSuffixes = { "@Idle", "@Run" };
    private static readonly string[] HighDeformationPoseValidationClipSuffixes = { "@Attack", "@Hurt", "@Death" };

    [MenuItem(TrimFbxMenuPath)]
    private static void Editor_SimplifySkinMeshToFbx()
    {
        if (!TryGetSelectedMeshAsset(out var mesh, out var assetPath))
        {
            return;
        }

        if (!ValidateSkinnedMeshForProcessing(mesh, assetPath))
        {
            return;
        }

        if (!TryGetSourceSkinnedMeshRenderer(mesh, assetPath, out var renderer))
        {
            Debug.LogError($"FBX export requires a SkinnedMeshRenderer for '{assetPath}'.");
            return;
        }

        OpenSkinnedMeshSimplificationWindow(mesh, assetPath, renderer);
    }

    public static void Batch_SimplifySkinMeshToFbx()
    {
        var exitCode = 1;

        try
        {
            exitCode = TryExecuteBatchSkinnedMeshFbxExport() ? 0 : 1;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Batch skinned mesh FBX export failed with an unexpected exception: {ex}");
            exitCode = 1;
        }
        finally
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }

    public static void Batch_ComputeSkinnedMeshComparisonMetrics()
    {
        var exitCode = 1;

        try
        {
            exitCode = TryExecuteBatchSkinnedMeshComparisonMetrics() ? 0 : 1;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Batch skinned mesh comparison metric computation failed with an unexpected exception: {ex}");
            exitCode = 1;
        }
        finally
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }
    }

    private static bool TryExecuteBatchSkinnedMeshFbxExport()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        if (!TryGetCommandLineArgValue(commandLineArgs, BatchEntryIdArgument, out var entryId))
        {
            Debug.LogError($"Missing required batch argument '{BatchEntryIdArgument}'.");
            return false;
        }

        var configPath = TryGetCommandLineArgValue(commandLineArgs, BatchConfigPathArgument, out var configuredConfigPath)
            ? configuredConfigPath
            : SkinnedMeshSimplificationConfig.DefaultConfigAssetPath;

        CleanupStagedExternalSources();

        try
        {
            var config = SkinnedMeshSimplificationConfig.Load(configPath);
            var entry = config.FindEntry(entryId);
            if (entry == null)
            {
                Debug.LogError($"No skinned mesh simplification config entry was found for '{entryId}' in '{configPath}'.");
                return false;
            }

            if (!TryResolveWorkingAssetPath(entry, out var workingAssetPath))
            {
                return false;
            }

            if (!TryLoadMeshAssetAtPath(workingAssetPath, out var mesh))
            {
                return false;
            }

            if (!ValidateSkinnedMeshForProcessing(mesh, workingAssetPath))
            {
                return false;
            }

            if (!TryGetSourceSkinnedMeshRenderer(mesh, workingAssetPath, out var renderer))
            {
                Debug.LogError($"FBX export requires a SkinnedMeshRenderer for '{workingAssetPath}'.");
                return false;
            }

            if (!TryRunSkinnedMeshFbxExport(mesh, workingAssetPath, renderer, config, entry, persistConfigEntry: false, out var outputAssetPath))
            {
                return false;
            }

            if (!TryValidateImportedTrimmedFbxOutput(outputAssetPath, out var outputValidationSummary))
            {
                return false;
            }

            if (!TryValidateTrimmedFbxRestPose(workingAssetPath, mesh, outputAssetPath, out var restPoseValidationSummary))
            {
                return false;
            }

            if (!TryValidateTrimmedFbxRepresentativePoses(entry.entryId, mesh, outputAssetPath, out var representativePoseValidationSummary))
            {
                return false;
            }

            Debug.Log($"Batch exported trimmed FBX: '{outputAssetPath}'");
            Debug.Log($"Batch validated trimmed FBX import: {outputValidationSummary}");
            Debug.Log($"Batch validated trimmed FBX rest pose: {restPoseValidationSummary}");
            Debug.Log($"Batch validated trimmed FBX animated poses: {representativePoseValidationSummary}");
            return true;
        }
        finally
        {
            CleanupStagedExternalSources();
        }
    }

    private static bool TryExecuteBatchSkinnedMeshComparisonMetrics()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        if (!TryGetCommandLineArgValue(commandLineArgs, BatchEntryIdArgument, out var entryId))
        {
            Debug.LogError($"Missing required batch argument '{BatchEntryIdArgument}'.");
            return false;
        }

        if (!TryGetCommandLineArgValue(commandLineArgs, BatchReportPathArgument, out var reportPath))
        {
            Debug.LogError($"Missing required batch argument '{BatchReportPathArgument}'.");
            return false;
        }

        var configPath = TryGetCommandLineArgValue(commandLineArgs, BatchConfigPathArgument, out var configuredConfigPath)
            ? configuredConfigPath
            : SkinnedMeshSimplificationConfig.DefaultConfigAssetPath;

        var report = new SkinnedMeshComparisonMetricReport
        {
            entryId = entryId
        };

        try
        {
            var config = SkinnedMeshSimplificationConfig.Load(configPath);
            var entry = config.FindEntry(entryId);
            if (entry == null)
            {
                report.failureReasons.Add($"No skinned mesh simplification config entry was found for '{entryId}' in '{configPath}'.");
                return TryWriteComparisonMetricReport(reportPath, report);
            }

            report.outputAssetPath = ResolveTrimFbxOutputPath(entry.entryId, entry.outputPath);
            var comparisonSourceAssetPath = NormalizeAssetPath(entry.entryId);

            if (!TryResolveWorkingAssetPath(entry, out var workingAssetPath))
            {
                report.failureReasons.Add($"Failed to resolve working asset path for '{entryId}'.");
                return TryWriteComparisonMetricReport(reportPath, report);
            }

            var comparisonMeshAssetPath = entry.isExternalSource ? workingAssetPath : comparisonSourceAssetPath;
            if (!TryLoadMeshAssetWithoutLogging(comparisonMeshAssetPath, out var mesh))
            {
                comparisonMeshAssetPath = workingAssetPath;
            }

            if (!TryLoadMeshAssetAtPath(comparisonMeshAssetPath, out mesh))
            {
                report.failureReasons.Add($"Failed to load a Mesh asset from '{comparisonMeshAssetPath}'.");
                return TryWriteComparisonMetricReport(reportPath, report);
            }

            if (!ValidateSkinnedMeshForProcessing(mesh, comparisonMeshAssetPath))
            {
                report.failureReasons.Add($"Skinned mesh validation failed for '{comparisonMeshAssetPath}'. Check the Unity log for details.");
                return TryWriteComparisonMetricReport(reportPath, report);
            }

            if (!TryBuildComparisonMetricReport(comparisonSourceAssetPath, mesh, report.outputAssetPath, report))
            {
                return TryWriteComparisonMetricReport(reportPath, report);
            }

            return TryWriteComparisonMetricReport(reportPath, report);
        }
        catch (Exception ex)
        {
            report.failureReasons.Add($"Unexpected metric computation exception: {ex.Message}");
            return TryWriteComparisonMetricReport(reportPath, report);
        }
    }

    private static bool TryRunSkinnedMeshFbxExport(
        Mesh mesh,
        string assetPath,
        SkinnedMeshRenderer renderer,
        SkinnedMeshSimplificationConfig config,
        SkinnedMeshSimplificationConfig.Entry entry,
        bool persistConfigEntry,
        out string outputAssetPath)
    {
        var reductionPercent = Mathf.Clamp(entry?.reductionPercent ?? 50, 0, 100);
        outputAssetPath = ResolveTrimFbxOutputPath(assetPath, entry?.outputPath);
        var sourceFbxPath = ResolveSourceFbxPath(assetPath, entry);

        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);
        var simplifiedMesh = simpleMeshEditor.Simplify(reductionPercent);

        if (!TryExportTrimFbx(renderer, simplifiedMesh, sourceFbxPath, outputAssetPath))
        {
            return false;
        }

        AssetDatabase.Refresh();

        if (!persistConfigEntry)
        {
            return true;
        }

        var savedEntry = config.UpsertEntry(assetPath);
        savedEntry.outputPath = outputAssetPath;
        savedEntry.reductionPercent = reductionPercent;
        if (entry != null)
        {
            savedEntry.isExternalSource = entry.isExternalSource;
            savedEntry.externalSourcePath = entry.externalSourcePath;
        }

        return config.Save();
    }

    private static bool TryLoadMeshAssetAtPath(string assetPath, out Mesh mesh)
    {
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (mesh != null)
        {
            return true;
        }

        var meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();
        if (meshes.Length == 1)
        {
            mesh = meshes[0];
            return true;
        }

        if (meshes.Length > 1)
        {
            Debug.LogError($"Asset '{assetPath}' contains multiple meshes. Batch execution requires an asset path that resolves to a single Mesh.");
            return false;
        }

        Debug.LogError($"Failed to resolve a Mesh asset from '{assetPath}'.");
        return false;
    }

    private static bool TryLoadMeshAssetWithoutLogging(string assetPath, out Mesh mesh)
    {
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (mesh != null)
        {
            return true;
        }

        var meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();
        if (meshes.Length == 1)
        {
            mesh = meshes[0];
            return true;
        }

        return false;
    }

    private static bool TryResolveWorkingAssetPath(SkinnedMeshSimplificationConfig.Entry entry, out string assetPath)
    {
        assetPath = null;

        if (entry == null)
        {
            return false;
        }

        if (!entry.isExternalSource)
        {
            assetPath = NormalizeAssetPath(entry.entryId);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                Debug.LogError("Config entry is missing a valid entryId asset path.");
                return false;
            }

            return true;
        }

        return TryStageExternalSource(entry, out assetPath);
    }

    private static bool TryStageExternalSource(SkinnedMeshSimplificationConfig.Entry entry, out string stagedAssetPath)
    {
        stagedAssetPath = null;

        var sourceFilePath = ResolveExternalSourcePath(entry?.externalSourcePath);
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            Debug.LogError($"Configured external source FBX does not exist: '{entry?.externalSourcePath}'.");
            return false;
        }

        var stagingAbsoluteDirectory = AssetPathToAbsolutePath(TempStagingAssetDirectory);
        Directory.CreateDirectory(stagingAbsoluteDirectory);

        var stagedFileName = Path.GetFileName(sourceFilePath);
        var stagedAbsolutePath = Path.Combine(stagingAbsoluteDirectory, stagedFileName);
        File.Copy(sourceFilePath, stagedAbsolutePath, overwrite: true);

        stagedAssetPath = NormalizeAssetPath($"{TempStagingAssetDirectory}/{stagedFileName}");
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(stagedAssetPath, ImportAssetOptions.ForceSynchronousImport);
        if (!TryConfigureStagedModelImporter(stagedAssetPath))
        {
            return false;
        }

        return true;
    }

    private static bool TryConfigureStagedModelImporter(string stagedAssetPath)
    {
        var importer = AssetImporter.GetAtPath(stagedAssetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Failed to resolve a ModelImporter for staged asset '{stagedAssetPath}'.");
            return false;
        }

        var requiresReimport = false;
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            requiresReimport = true;
        }

        if (!requiresReimport)
        {
            return true;
        }

        importer.SaveAndReimport();
        return true;
    }

    private static void CleanupStagedExternalSources()
    {
        var stagingAbsoluteDirectory = AssetPathToAbsolutePath(TempStagingAssetDirectory);
        if (!Directory.Exists(stagingAbsoluteDirectory))
        {
            return;
        }

        foreach (var filePath in Directory.GetFiles(stagingAbsoluteDirectory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
        }

        foreach (var directoryPath in Directory.GetDirectories(stagingAbsoluteDirectory, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            Directory.Delete(directoryPath, recursive: false);
        }

        AssetDatabase.Refresh();
    }

    private static string ResolveExternalSourcePath(string configuredExternalSourcePath)
    {
        if (string.IsNullOrWhiteSpace(configuredExternalSourcePath))
        {
            return null;
        }

        var normalizedPath = configuredExternalSourcePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath))
        {
            return Path.GetFullPath(normalizedPath);
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var externalSourceRoot = Path.Combine(projectRoot, "ART_SVN");
        return Path.GetFullPath(Path.Combine(externalSourceRoot, normalizedPath));
    }

    private static bool TryGetCommandLineArgValue(string[] commandLineArgs, string argumentName, out string argumentValue)
    {
        argumentValue = null;
        if (commandLineArgs == null)
        {
            return false;
        }

        for (var i = 0; i < commandLineArgs.Length; i++)
        {
            var current = commandLineArgs[i];
            if (string.Equals(current, argumentName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandLineArgs.Length || string.IsNullOrWhiteSpace(commandLineArgs[i + 1]))
                {
                    return false;
                }

                argumentValue = commandLineArgs[i + 1];
                return true;
            }

            var prefix = argumentName + "=";
            if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && current.Length > prefix.Length)
            {
                argumentValue = current.Substring(prefix.Length);
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAssetPath(string assetPath)
    {
        return string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Replace('\\', '/');
    }

    private static bool TryGetSourceSkinnedMeshRenderer(Mesh mesh, string assetPath, out SkinnedMeshRenderer renderer)
    {
        renderer = null;

        var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (sourceRoot == null)
        {
            return false;
        }

        var renderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        foreach (var candidate in renderers)
        {
            if (candidate != null && candidate.sharedMesh == mesh)
            {
                renderer = candidate;
                return true;
            }
        }

        if (renderers.Length == 1)
        {
            renderer = renderers[0];
            return renderer != null;
        }

        return false;
    }

    private static string ResolveTrimFbxOutputPath(string assetPath, string configuredOutputPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredOutputPath))
        {
            return configuredOutputPath.Replace('\\', '/');
        }

        var directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        var fileName = Path.GetFileNameWithoutExtension(assetPath);
        return $"{directory}/{fileName}_trim.fbx";
    }

    private static string ResolveSourceFbxPath(string assetPath, SkinnedMeshSimplificationConfig.Entry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry?.externalSourcePath))
        {
            return entry.isExternalSource
                ? ResolveExternalSourcePath(entry.externalSourcePath)
                : entry.externalSourcePath.Replace('\\', '/');
        }

        return assetPath.Replace('\\', '/');
    }

    private static bool TryValidateImportedTrimmedFbxOutput(string assetPath, out string validationSummary)
    {
        validationSummary = null;

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogError("Trimmed FBX output validation failed because the output path was empty.");
            return false;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Trimmed FBX output validation failed because no ModelImporter was found for '{assetPath}'.");
            return false;
        }

        var outputRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (outputRoot == null)
        {
            Debug.LogError($"Trimmed FBX output validation failed because no imported root GameObject was found for '{assetPath}'.");
            return false;
        }

        var outputRenderer = outputRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.sharedMesh != null);
        if (outputRenderer == null)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' does not expose a SkinnedMeshRenderer with a shared mesh.");
            return false;
        }

        var outputMesh = outputRenderer.sharedMesh;
        if (outputMesh == null)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' resolved a renderer without a shared mesh.");
            return false;
        }

        if (outputMesh.subMeshCount != 1)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported {outputMesh.subMeshCount} submeshes instead of 1.");
            return false;
        }

        if (outputMesh.GetTopology(0) != MeshTopology.Triangles)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported topology {outputMesh.GetTopology(0)} instead of Triangles.");
            return false;
        }

        if (outputMesh.blendShapeCount > 0)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported {outputMesh.blendShapeCount} BlendShapes.");
            return false;
        }

        if (outputMesh.bindposes == null || outputMesh.bindposes.Length == 0)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported no bindposes.");
            return false;
        }

        if (outputRenderer.bones == null || outputRenderer.bones.Length == 0)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported no skinned bones.");
            return false;
        }

        if (outputMesh.boneWeights.Length != outputMesh.vertexCount)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported {outputMesh.boneWeights.Length} legacy bone weights for {outputMesh.vertexCount} vertices.");
            return false;
        }

        var bonesPerVertex = outputMesh.GetBonesPerVertex();
        try
        {
            if (bonesPerVertex.Length != outputMesh.vertexCount)
            {
                Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported {bonesPerVertex.Length} bone-weight entries for {outputMesh.vertexCount} vertices.");
                return false;
            }

            for (var i = 0; i < bonesPerVertex.Length; i++)
            {
                if (bonesPerVertex[i] > 4)
                {
                    Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' imported vertices with more than 4 bone influences.");
                    return false;
                }
            }
        }
        finally
        {
            if (bonesPerVertex.IsCreated)
            {
                bonesPerVertex.Dispose();
            }
        }

        if (importer.isReadable)
        {
            Debug.LogError($"Trimmed FBX output validation failed because '{assetPath}' is still imported with read/write enabled.");
            return false;
        }

        validationSummary = $"root='{outputRoot.name}', mesh='{outputMesh.name}', vertices={outputMesh.vertexCount}, bones={outputRenderer.bones.Length}, importerReadable={importer.isReadable}";
        return true;
    }

    private static bool TryValidateTrimmedFbxRestPose(string sourceAssetPath, Mesh sourceMesh, string outputAssetPath, out string validationSummary)
    {
        validationSummary = null;

        if (!TryInstantiateImportedModel(sourceAssetPath, out var sourceRootInstance))
        {
            Debug.LogError($"Trimmed FBX rest-pose validation failed because source model '{sourceAssetPath}' could not be instantiated.");
            return false;
        }

        if (!TryInstantiateImportedModel(outputAssetPath, out var outputRootInstance))
        {
            UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            Debug.LogError($"Trimmed FBX rest-pose validation failed because output model '{outputAssetPath}' could not be instantiated.");
            return false;
        }

        Mesh sourceBakedMesh = null;
        Mesh outputBakedMesh = null;
        try
        {
            sourceRootInstance.hideFlags = HideFlags.HideAndDontSave;
            outputRootInstance.hideFlags = HideFlags.HideAndDontSave;

            ResetValidationRootTransform(sourceRootInstance.transform);
            ResetValidationRootTransform(outputRootInstance.transform);

            if (!TryFindSkinnedRendererForValidation(sourceRootInstance, sourceMesh, out var sourceRenderer))
            {
                Debug.LogError($"Trimmed FBX rest-pose validation failed because no matching source SkinnedMeshRenderer was found for '{sourceAssetPath}'.");
                return false;
            }

            if (!TryFindSkinnedRendererForValidation(outputRootInstance, null, out var outputRenderer))
            {
                Debug.LogError($"Trimmed FBX rest-pose validation failed because no output SkinnedMeshRenderer was found for '{outputAssetPath}'.");
                return false;
            }

            sourceBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "SourceBakeValidationMesh" };
            outputBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "OutputBakeValidationMesh" };

            sourceRenderer.BakeMesh(sourceBakedMesh, true);
            outputRenderer.BakeMesh(outputBakedMesh, true);

            if (!TryValidateBakedMeshData(sourceBakedMesh, sourceAssetPath, out var sourceBakeSummary))
            {
                return false;
            }

            if (!TryValidateBakedMeshData(outputBakedMesh, outputAssetPath, out var outputBakeSummary))
            {
                return false;
            }

            var sourceBounds = sourceBakedMesh.bounds;
            var outputBounds = outputBakedMesh.bounds;
            var sourceDiagonal = Mathf.Max(sourceBounds.size.magnitude, 0.0001f);
            var outputDiagonal = outputBounds.size.magnitude;
            var diagonalRatio = outputDiagonal / sourceDiagonal;
            var normalizedCenterOffset = Vector3.Distance(sourceBounds.center, outputBounds.center) / sourceDiagonal;

            if (diagonalRatio < 0.5f || diagonalRatio > 1.5f)
            {
                Debug.LogError($"Trimmed FBX rest-pose validation failed because bounds diagonal ratio was {diagonalRatio:F3} for '{outputAssetPath}'.");
                return false;
            }

            if (normalizedCenterOffset > 0.25f)
            {
                Debug.LogError($"Trimmed FBX rest-pose validation failed because normalized center offset was {normalizedCenterOffset:F3} for '{outputAssetPath}'.");
                return false;
            }

            validationSummary = $"source={sourceBakeSummary}; output={outputBakeSummary}; diagonalRatio={diagonalRatio:F3}; centerOffsetRatio={normalizedCenterOffset:F3}";
            return true;
        }
        finally
        {
            if (sourceBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceBakedMesh);
            }

            if (outputBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(outputBakedMesh);
            }

            if (sourceRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            }

            if (outputRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(outputRootInstance);
            }
        }
    }

    private static bool TryValidateTrimmedFbxRepresentativePoses(string sourceAssetPath, Mesh sourceMesh, string outputAssetPath, out string validationSummary)
    {
        validationSummary = null;

        if (!TryValidateTrimmedFbxAnimatedPoseCategory(sourceAssetPath, sourceMesh, outputAssetPath, "neutral", NeutralPoseValidationClipSuffixes, out var neutralPoseSummary))
        {
            return false;
        }

        if (!TryValidateTrimmedFbxAnimatedPoseCategory(sourceAssetPath, sourceMesh, outputAssetPath, "highDeformation", HighDeformationPoseValidationClipSuffixes, out var highDeformationPoseSummary))
        {
            return false;
        }

        validationSummary = $"{neutralPoseSummary}; {highDeformationPoseSummary}";
        return true;
    }

    private static bool TryValidateTrimmedFbxAnimatedPoseCategory(string sourceAssetPath, Mesh sourceMesh, string outputAssetPath, string poseCategory, string[] clipSuffixes, out string validationSummary, bool logErrors = true)
    {
        validationSummary = null;

        if (!TryResolveAnimatedPoseValidationClip(sourceAssetPath, clipSuffixes, out var clipAssetPath, out var clip))
        {
            validationSummary = $"category='{poseCategory}', skipped=true, reason='no neighboring animation clip found'";
            return true;
        }

        var minDiagonalRatio = DefaultAnimatedPoseMinDiagonalRatio;
        var maxDiagonalRatio = poseCategory == "highDeformation" ? HighDeformationAnimatedPoseMaxDiagonalRatio : DefaultAnimatedPoseMaxDiagonalRatio;
        var maxCenterOffsetRatio = poseCategory == "highDeformation" ? HighDeformationAnimatedPoseMaxCenterOffsetRatio : DefaultAnimatedPoseMaxCenterOffsetRatio;

        if (!TryInstantiateImportedModel(sourceAssetPath, out var sourceRootInstance))
        {
            validationSummary = $"category='{poseCategory}', failed='source model could not be instantiated'";
            if (logErrors)
            {
                Debug.LogError($"Trimmed FBX animated-pose validation failed because source model '{sourceAssetPath}' could not be instantiated.");
            }
            return false;
        }

        if (!TryInstantiateImportedModel(outputAssetPath, out var outputRootInstance))
        {
            UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            validationSummary = $"category='{poseCategory}', failed='output model could not be instantiated'";
            if (logErrors)
            {
                Debug.LogError($"Trimmed FBX animated-pose validation failed because output model '{outputAssetPath}' could not be instantiated.");
            }
            return false;
        }

        Mesh sourceBakedMesh = null;
        Mesh outputBakedMesh = null;
        try
        {
            sourceRootInstance.hideFlags = HideFlags.HideAndDontSave;
            outputRootInstance.hideFlags = HideFlags.HideAndDontSave;

            ResetValidationRootTransform(sourceRootInstance.transform);
            ResetValidationRootTransform(outputRootInstance.transform);

            if (!TryFindSkinnedRendererForValidation(sourceRootInstance, sourceMesh, out var sourceRenderer))
            {
                validationSummary = $"category='{poseCategory}', failed='matching source SkinnedMeshRenderer was not found'";
                if (logErrors)
                {
                    Debug.LogError($"Trimmed FBX animated-pose validation failed because no matching source SkinnedMeshRenderer was found for '{sourceAssetPath}'.");
                }
                return false;
            }

            if (!TryFindSkinnedRendererForValidation(outputRootInstance, null, out var outputRenderer))
            {
                validationSummary = $"category='{poseCategory}', failed='output SkinnedMeshRenderer was not found'";
                if (logErrors)
                {
                    Debug.LogError($"Trimmed FBX animated-pose validation failed because no output SkinnedMeshRenderer was found for '{outputAssetPath}'.");
                }
                return false;
            }

            var sampleTime = Mathf.Clamp(clip.length * AnimatedPoseValidationNormalizedTime, 0f, Mathf.Max(clip.length, 0f));
            clip.SampleAnimation(sourceRootInstance, sampleTime);
            clip.SampleAnimation(outputRootInstance, sampleTime);

            sourceBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "SourceAnimatedBakeValidationMesh" };
            outputBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "OutputAnimatedBakeValidationMesh" };

            sourceRenderer.BakeMesh(sourceBakedMesh, true);
            outputRenderer.BakeMesh(outputBakedMesh, true);

            if (!TryValidateBakedMeshData(sourceBakedMesh, sourceAssetPath, out var sourceBakeSummary))
            {
                return false;
            }

            if (!TryValidateBakedMeshData(outputBakedMesh, outputAssetPath, out var outputBakeSummary))
            {
                return false;
            }

            var sourceBounds = sourceBakedMesh.bounds;
            var outputBounds = outputBakedMesh.bounds;
            var sourceDiagonal = Mathf.Max(sourceBounds.size.magnitude, 0.0001f);
            var outputDiagonal = outputBounds.size.magnitude;
            var diagonalRatio = outputDiagonal / sourceDiagonal;
            var normalizedCenterOffset = Vector3.Distance(sourceBounds.center, outputBounds.center) / sourceDiagonal;

            if (diagonalRatio < minDiagonalRatio || diagonalRatio > maxDiagonalRatio)
            {
                validationSummary = $"category='{poseCategory}', failed='bounds diagonal ratio {diagonalRatio:F3} outside [{minDiagonalRatio:F3}, {maxDiagonalRatio:F3}]'";
                if (logErrors)
                {
                    Debug.LogError($"Trimmed FBX animated-pose validation failed because bounds diagonal ratio was {diagonalRatio:F3} for '{outputAssetPath}'.");
                }
                return false;
            }

            if (normalizedCenterOffset > maxCenterOffsetRatio)
            {
                validationSummary = $"category='{poseCategory}', failed='center offset ratio {normalizedCenterOffset:F3} exceeded {maxCenterOffsetRatio:F3}'";
                if (logErrors)
                {
                    Debug.LogError($"Trimmed FBX animated-pose validation failed because normalized center offset was {normalizedCenterOffset:F3} for '{outputAssetPath}'.");
                }
                return false;
            }

            validationSummary = $"category='{poseCategory}', skipped=false, clip='{Path.GetFileName(clipAssetPath)}', clipName='{clip.name}', normalizedTime={AnimatedPoseValidationNormalizedTime:F3}, sampleTime={sampleTime:F3}, source={sourceBakeSummary}; output={outputBakeSummary}; diagonalRatio={diagonalRatio:F3}; centerOffsetRatio={normalizedCenterOffset:F3}";
            return true;
        }
        finally
        {
            if (sourceBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceBakedMesh);
            }

            if (outputBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(outputBakedMesh);
            }

            if (sourceRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            }

            if (outputRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(outputRootInstance);
            }
        }
    }

    private static bool TryResolveAnimatedPoseValidationClip(string sourceAssetPath, string[] clipSuffixes, out string clipAssetPath, out AnimationClip clip)
    {
        clipAssetPath = null;
        clip = null;

        if (string.IsNullOrWhiteSpace(sourceAssetPath) || clipSuffixes == null || clipSuffixes.Length == 0)
        {
            return false;
        }

        var normalizedSourceAssetPath = sourceAssetPath.Replace('\\', '/');
        var directory = Path.GetDirectoryName(normalizedSourceAssetPath)?.Replace('\\', '/');
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedSourceAssetPath);
        var extension = Path.GetExtension(normalizedSourceAssetPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileNameWithoutExtension) || string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        foreach (var clipSuffix in clipSuffixes)
        {
            var candidateAssetPath = $"{directory}/{fileNameWithoutExtension}{clipSuffix}{extension}";
            if (!TryLoadValidationAnimationClip(candidateAssetPath, out clip))
            {
                continue;
            }

            clipAssetPath = candidateAssetPath;
            return true;
        }

        return false;
    }

    private static bool TryLoadValidationAnimationClip(string clipAssetPath, out AnimationClip clip)
    {
        clip = null;
        if (string.IsNullOrWhiteSpace(clipAssetPath))
        {
            return false;
        }

        clip = AssetDatabase.LoadAllAssetsAtPath(clipAssetPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => candidate != null && !candidate.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

        return clip != null;
    }

    private static bool TryBuildComparisonMetricReport(string sourceAssetPath, Mesh sourceMesh, string outputAssetPath, SkinnedMeshComparisonMetricReport report, bool logErrors = true)
    {
        if (report == null)
        {
            return false;
        }

        report.outputAssetPath = outputAssetPath;
        report.representativePoses.Clear();
        report.globalWorstFrames.Clear();
        report.jointRegionWorstFrames.Clear();
        report.screenSpaceWorstFrames.Clear();
        report.comparisonStatus = "not_computed";
        report.hasJointRegionP95Error = false;
        report.hasGlobalP95Error = false;
        report.hasMaxError = false;
        report.hasScreenSpaceDiff = false;

        var poseReports = new[]
        {
            TryComputeRepresentativePoseMetric(sourceAssetPath, sourceMesh, outputAssetPath, "rest", null, useRestPose: true, out var restReport, logErrors) ? restReport : restReport,
            TryComputeRepresentativePoseMetric(sourceAssetPath, sourceMesh, outputAssetPath, "neutral", NeutralPoseValidationClipSuffixes, useRestPose: false, out var neutralReport, logErrors) ? neutralReport : neutralReport,
            TryComputeRepresentativePoseMetric(sourceAssetPath, sourceMesh, outputAssetPath, "highDeformation", HighDeformationPoseValidationClipSuffixes, useRestPose: false, out var highDeformationReport, logErrors) ? highDeformationReport : highDeformationReport
        };

        var overallSuccess = true;
        for (var i = 0; i < poseReports.Length; i++)
        {
            var poseReport = poseReports[i] ?? new SkinnedMeshComparisonPoseMetricReport
            {
                category = i == 0 ? "rest" : i == 1 ? "neutral" : "highDeformation",
                status = "not_computed"
            };

            report.representativePoses.Add(poseReport);
            if (string.Equals(poseReport.status, "fail", StringComparison.OrdinalIgnoreCase))
            {
                overallSuccess = false;
                if (!string.IsNullOrWhiteSpace(poseReport.details))
                {
                    report.failureReasons.Add($"{poseReport.category}: {poseReport.details}");
                }
            }

            if (poseReport.hasJointRegionP95Error)
            {
                if (!report.hasJointRegionP95Error || poseReport.jointRegionP95Error > report.jointRegionP95Error)
                {
                    report.hasJointRegionP95Error = true;
                    report.jointRegionP95Error = poseReport.jointRegionP95Error;
                    report.jointRegionWorstFrames.Clear();
                    report.jointRegionWorstFrames.Add(i);
                }
                else if (Mathf.Approximately(poseReport.jointRegionP95Error, report.jointRegionP95Error))
                {
                    report.jointRegionWorstFrames.Add(i);
                }
            }

            if (!poseReport.hasGlobalP95Error || !poseReport.hasMaxError)
            {
                goto ScreenSpaceAggregation;
            }

            if (!report.hasGlobalP95Error || poseReport.globalP95Error > report.globalP95Error)
            {
                report.hasGlobalP95Error = true;
                report.globalP95Error = poseReport.globalP95Error;
                report.globalWorstFrames.Clear();
                report.globalWorstFrames.Add(i);
            }
            else if (Mathf.Approximately(poseReport.globalP95Error, report.globalP95Error))
            {
                report.globalWorstFrames.Add(i);
            }

            if (!report.hasMaxError || poseReport.maxError > report.maxError)
            {
                report.hasMaxError = true;
                report.maxError = poseReport.maxError;
            }

        ScreenSpaceAggregation:
            if (!poseReport.hasScreenSpaceDiff)
            {
                continue;
            }

            if (!report.hasScreenSpaceDiff || poseReport.screenSpaceDiff > report.screenSpaceDiff)
            {
                report.hasScreenSpaceDiff = true;
                report.screenSpaceDiff = poseReport.screenSpaceDiff;
                report.screenSpaceWorstFrames.Clear();
                report.screenSpaceWorstFrames.Add(i);
            }
            else if (Mathf.Approximately(poseReport.screenSpaceDiff, report.screenSpaceDiff))
            {
                report.screenSpaceWorstFrames.Add(i);
            }
        }

        if (report.hasJointRegionP95Error || report.hasGlobalP95Error || report.hasMaxError || report.hasScreenSpaceDiff)
        {
            report.comparisonStatus = "partial";
        }

        return overallSuccess;
    }

    private static bool TryComputeRepresentativePoseMetric(
        string sourceAssetPath,
        Mesh sourceMesh,
        string outputAssetPath,
        string poseCategory,
        string[] clipSuffixes,
        bool useRestPose,
        out SkinnedMeshComparisonPoseMetricReport poseReport,
        bool logErrors)
    {
        poseReport = new SkinnedMeshComparisonPoseMetricReport
        {
            category = poseCategory,
            status = "not_computed"
        };

        string clipAssetPath = null;
        AnimationClip clip = null;
        if (!useRestPose && !TryResolveAnimatedPoseValidationClip(sourceAssetPath, clipSuffixes, out clipAssetPath, out clip))
        {
            poseReport.status = "skipped";
            poseReport.details = "no neighboring animation clip found";
            return true;
        }

        poseReport.clipAssetPath = clipAssetPath;
        poseReport.clipName = clip != null ? clip.name : null;

        if (!TryInstantiateImportedModel(sourceAssetPath, out var sourceRootInstance))
        {
            poseReport.status = "fail";
            poseReport.details = "source model could not be instantiated";
            if (logErrors)
            {
                Debug.LogError($"Skinned mesh comparison metric computation failed because source model '{sourceAssetPath}' could not be instantiated.");
            }
            return false;
        }

        if (!TryInstantiateImportedModel(outputAssetPath, out var outputRootInstance))
        {
            UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            poseReport.status = "fail";
            poseReport.details = "output model could not be instantiated";
            if (logErrors)
            {
                Debug.LogError($"Skinned mesh comparison metric computation failed because output model '{outputAssetPath}' could not be instantiated.");
            }
            return false;
        }

        Mesh sourceBakedMesh = null;
        Mesh outputBakedMesh = null;
        try
        {
            sourceRootInstance.hideFlags = HideFlags.HideAndDontSave;
            outputRootInstance.hideFlags = HideFlags.HideAndDontSave;

            ResetValidationRootTransform(sourceRootInstance.transform);
            ResetValidationRootTransform(outputRootInstance.transform);

            if (!TryFindSkinnedRendererForValidation(sourceRootInstance, sourceMesh, out var sourceRenderer))
            {
                poseReport.status = "fail";
                poseReport.details = "matching source SkinnedMeshRenderer was not found";
                return false;
            }

            if (!TryFindSkinnedRendererForValidation(outputRootInstance, null, out var outputRenderer))
            {
                poseReport.status = "fail";
                poseReport.details = "output SkinnedMeshRenderer was not found";
                return false;
            }

            if (clip != null)
            {
                var sampleTime = Mathf.Clamp(clip.length * AnimatedPoseValidationNormalizedTime, 0f, Mathf.Max(clip.length, 0f));
                clip.SampleAnimation(sourceRootInstance, sampleTime);
                clip.SampleAnimation(outputRootInstance, sampleTime);
            }

            sourceBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "SourceComparisonMetricMesh" };
            outputBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "OutputComparisonMetricMesh" };

            sourceRenderer.BakeMesh(sourceBakedMesh, true);
            outputRenderer.BakeMesh(outputBakedMesh, true);

            if (!TryValidateBakedMeshData(sourceBakedMesh, sourceAssetPath, out _))
            {
                poseReport.status = "fail";
                poseReport.details = "source baked mesh validation failed";
                return false;
            }

            if (!TryValidateBakedMeshData(outputBakedMesh, outputAssetPath, out _))
            {
                poseReport.status = "fail";
                poseReport.details = "output baked mesh validation failed";
                return false;
            }

            if (!TryComputeGlobalVertexDistanceMetrics(sourceBakedMesh, outputBakedMesh, out var globalP95Error, out var maxError, out var sampleCount))
            {
                poseReport.status = "fail";
                poseReport.details = "global vertex-distance metric computation failed";
                return false;
            }

            if (TryComputeJointRegionVertexDistanceMetrics(sourceMesh, sourceBakedMesh, outputBakedMesh, out var jointRegionP95Error, out var jointSampleCount))
            {
                poseReport.hasJointRegionP95Error = true;
                poseReport.jointRegionP95Error = jointRegionP95Error;
                poseReport.jointSampleCount = jointSampleCount;
            }

            if (TryComputeScreenSpaceDiffMetric(sourceBakedMesh, outputBakedMesh, out var screenSpaceDiff))
            {
                poseReport.hasScreenSpaceDiff = true;
                poseReport.screenSpaceDiff = screenSpaceDiff;
            }

            poseReport.status = "pass";
            poseReport.details = useRestPose ? "baked_rest_pose" : $"sampled clip '{clip.name}'";
            poseReport.hasGlobalP95Error = true;
            poseReport.globalP95Error = globalP95Error;
            poseReport.hasMaxError = true;
            poseReport.maxError = maxError;
            poseReport.sampleCount = sampleCount;
            return true;
        }
        finally
        {
            if (sourceBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceBakedMesh);
            }

            if (outputBakedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(outputBakedMesh);
            }

            if (sourceRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceRootInstance);
            }

            if (outputRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(outputRootInstance);
            }
        }
    }

    private static bool TryComputeJointRegionVertexDistanceMetrics(Mesh sourceMesh, Mesh sourceBakedMesh, Mesh outputBakedMesh, out float jointRegionP95Error, out int sampleCount)
    {
        jointRegionP95Error = 0f;
        sampleCount = 0;

        if (sourceMesh == null || sourceBakedMesh == null || outputBakedMesh == null)
        {
            return false;
        }

        var sourceVertices = sourceBakedMesh.vertices;
        var outputVertices = outputBakedMesh.vertices;
        var sourceBoneWeights = sourceMesh.boneWeights;
        if (sourceVertices == null || outputVertices == null || sourceBoneWeights == null)
        {
            return false;
        }

        if (sourceVertices.Length == 0 || outputVertices.Length == 0 || sourceBoneWeights.Length != sourceVertices.Length)
        {
            return false;
        }

        var jointRiskMask = BuildJointRiskVertexMask(sourceMesh, sourceBoneWeights);
        if (jointRiskMask == null || jointRiskMask.Length != sourceVertices.Length)
        {
            return false;
        }

        var normalizationBasis = Mathf.Max(sourceBakedMesh.bounds.size.magnitude, 0.0001f);
        var distances = new List<float>(sourceVertices.Length);
        for (var i = 0; i < sourceVertices.Length; i++)
        {
            if (!jointRiskMask[i])
            {
                continue;
            }

            var sourceVertex = sourceVertices[i];
            var minSquaredDistance = float.PositiveInfinity;
            for (var j = 0; j < outputVertices.Length; j++)
            {
                var squaredDistance = (sourceVertex - outputVertices[j]).sqrMagnitude;
                if (squaredDistance < minSquaredDistance)
                {
                    minSquaredDistance = squaredDistance;
                }
            }

            distances.Add(Mathf.Sqrt(minSquaredDistance) / normalizationBasis);
        }

        if (distances.Count == 0)
        {
            return false;
        }

        distances.Sort();
        sampleCount = distances.Count;
        var p95Index = Mathf.Clamp(Mathf.CeilToInt(sampleCount * 0.95f) - 1, 0, sampleCount - 1);
        jointRegionP95Error = distances[p95Index];
        return true;
    }

    private static bool TryComputeScreenSpaceDiffMetric(Mesh sourceBakedMesh, Mesh outputBakedMesh, out float screenSpaceDiff)
    {
        screenSpaceDiff = 0f;

        if (sourceBakedMesh == null || outputBakedMesh == null)
        {
            return false;
        }

        var renderBounds = sourceBakedMesh.bounds;
        renderBounds.Encapsulate(outputBakedMesh.bounds);
        if (renderBounds.size.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        if (!TryRenderMeshToPixelBuffer(sourceBakedMesh, renderBounds, out var sourcePixels) ||
            !TryRenderMeshToPixelBuffer(outputBakedMesh, renderBounds, out var outputPixels))
        {
            return false;
        }

        if (sourcePixels == null || outputPixels == null || sourcePixels.Length == 0 || sourcePixels.Length != outputPixels.Length)
        {
            return false;
        }

        double accumulatedDifference = 0d;
        for (var i = 0; i < sourcePixels.Length; i++)
        {
            var sourcePixel = sourcePixels[i];
            var outputPixel = outputPixels[i];
            accumulatedDifference += Math.Abs(sourcePixel.r - outputPixel.r);
            accumulatedDifference += Math.Abs(sourcePixel.g - outputPixel.g);
            accumulatedDifference += Math.Abs(sourcePixel.b - outputPixel.b);
        }

        screenSpaceDiff = (float)(accumulatedDifference / (sourcePixels.Length * 255d * 3d));
        return true;
    }

    private static bool TryRenderMeshToPixelBuffer(Mesh mesh, Bounds renderBounds, out Color32[] pixels)
    {
        pixels = null;

        if (mesh == null)
        {
            return false;
        }

        GameObject cameraObject = null;
        GameObject meshObject = null;
        GameObject keyLightObject = null;
        GameObject fillLightObject = null;
        RenderTexture renderTexture = null;
        Texture2D readbackTexture = null;
        Material renderMaterial = null;
        var previousActive = RenderTexture.active;

        try
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                return false;
            }

            renderMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (renderMaterial.HasProperty("_Color"))
            {
                renderMaterial.SetColor("_Color", Color.white);
            }

            meshObject = new GameObject("ScreenSpaceDiffMesh")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var previewRotation = Quaternion.Euler(15f, -30f, 0f);
            meshObject.transform.rotation = previewRotation;
            meshObject.transform.position = previewRotation * (-renderBounds.center);

            var meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = renderMaterial;

            cameraObject = new GameObject("ScreenSpaceDiffCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var camera = cameraObject.AddComponent<Camera>();
            renderTexture = new RenderTexture(ScreenSpaceDiffRenderResolution, ScreenSpaceDiffRenderResolution, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var radius = Mathf.Max(renderBounds.extents.magnitude, 0.01f);
            var distance = Mathf.Max(2.5f, radius * 3.0f);
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(0.18f, 0.19f, 0.22f, 1f);
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = distance * 6f;
            camera.targetTexture = renderTexture;
            camera.transform.position = new Vector3(0f, 0f, -distance);
            camera.transform.rotation = Quaternion.identity;

            keyLightObject = CreateScreenSpaceDiffLight(new Color(1f, 1f, 1f, 1f), 1.1f, Quaternion.Euler(40f, 40f, 0f));
            fillLightObject = CreateScreenSpaceDiffLight(new Color(1f, 1f, 1f, 1f), 0.7f, Quaternion.Euler(340f, 218f, 177f));

            camera.Render();

            RenderTexture.active = renderTexture;
            readbackTexture = new Texture2D(ScreenSpaceDiffRenderResolution, ScreenSpaceDiffRenderResolution, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            readbackTexture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            readbackTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            pixels = readbackTexture.GetPixels32();
            return pixels != null && pixels.Length > 0;
        }
        finally
        {
            RenderTexture.active = previousActive;

            if (readbackTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(readbackTexture);
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            if (meshObject != null)
            {
                UnityEngine.Object.DestroyImmediate(meshObject);
            }

            if (keyLightObject != null)
            {
                UnityEngine.Object.DestroyImmediate(keyLightObject);
            }

            if (fillLightObject != null)
            {
                UnityEngine.Object.DestroyImmediate(fillLightObject);
            }

            if (renderMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(renderMaterial);
            }
        }
    }

    private static GameObject CreateScreenSpaceDiffLight(Color color, float intensity, Quaternion rotation)
    {
        var lightObject = new GameObject("ScreenSpaceDiffLight")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        lightObject.transform.rotation = rotation;
        return lightObject;
    }

    private static bool[] BuildJointRiskVertexMask(Mesh sourceMesh, BoneWeight[] sourceBoneWeights)
    {
        if (sourceMesh == null || sourceBoneWeights == null || sourceBoneWeights.Length != sourceMesh.vertexCount)
        {
            return null;
        }

        var jointRiskMask = new bool[sourceBoneWeights.Length];
        for (var i = 0; i < sourceBoneWeights.Length; i++)
        {
            jointRiskMask[i] = IsJointRiskVertex(sourceBoneWeights[i]);
        }

        var triangles = sourceMesh.triangles;
        if (triangles == null || triangles.Length < 3)
        {
            return jointRiskMask;
        }

        for (var i = 0; i <= triangles.Length - 3; i += 3)
        {
            var index0 = triangles[i];
            var index1 = triangles[i + 1];
            var index2 = triangles[i + 2];
            if (index0 < 0 || index1 < 0 || index2 < 0 ||
                index0 >= sourceBoneWeights.Length || index1 >= sourceBoneWeights.Length || index2 >= sourceBoneWeights.Length)
            {
                continue;
            }

            var dominantBone0 = GetDominantBoneIndex(sourceBoneWeights[index0]);
            var dominantBone1 = GetDominantBoneIndex(sourceBoneWeights[index1]);
            var dominantBone2 = GetDominantBoneIndex(sourceBoneWeights[index2]);
            if (dominantBone0 == dominantBone1 && dominantBone1 == dominantBone2)
            {
                continue;
            }

            jointRiskMask[index0] = true;
            jointRiskMask[index1] = true;
            jointRiskMask[index2] = true;
        }

        return jointRiskMask;
    }

    private static bool IsJointRiskVertex(BoneWeight boneWeight)
    {
        const float secondInfluenceThreshold = 0.05f;
        const float thirdInfluenceThreshold = 0.025f;
        const float dominantInfluenceThreshold = 0.95f;

        var first = boneWeight.weight0;
        var second = boneWeight.weight1;
        var third = boneWeight.weight2;
        var fourth = boneWeight.weight3;

        SortBoneInfluencesDescending(ref first, ref second, ref third, ref fourth);
        return third >= thirdInfluenceThreshold || (second >= secondInfluenceThreshold && first <= dominantInfluenceThreshold);
    }

    private static int GetDominantBoneIndex(BoneWeight boneWeight)
    {
        var dominantBoneIndex = boneWeight.boneIndex0;
        var dominantWeight = boneWeight.weight0;

        if (boneWeight.weight1 > dominantWeight)
        {
            dominantBoneIndex = boneWeight.boneIndex1;
            dominantWeight = boneWeight.weight1;
        }

        if (boneWeight.weight2 > dominantWeight)
        {
            dominantBoneIndex = boneWeight.boneIndex2;
            dominantWeight = boneWeight.weight2;
        }

        if (boneWeight.weight3 > dominantWeight)
        {
            dominantBoneIndex = boneWeight.boneIndex3;
        }

        return dominantBoneIndex;
    }

    private static void SortBoneInfluencesDescending(ref float first, ref float second, ref float third, ref float fourth)
    {
        if (first < second)
        {
            (first, second) = (second, first);
        }

        if (second < third)
        {
            (second, third) = (third, second);
        }

        if (third < fourth)
        {
            (third, fourth) = (fourth, third);
        }

        if (first < second)
        {
            (first, second) = (second, first);
        }

        if (second < third)
        {
            (second, third) = (third, second);
        }

        if (first < second)
        {
            (first, second) = (second, first);
        }
    }

    private static bool TryComputeGlobalVertexDistanceMetrics(Mesh sourceBakedMesh, Mesh outputBakedMesh, out float globalP95Error, out float maxError, out int sampleCount)
    {
        globalP95Error = 0f;
        maxError = 0f;
        sampleCount = 0;

        if (sourceBakedMesh == null || outputBakedMesh == null)
        {
            return false;
        }

        var sourceVertices = sourceBakedMesh.vertices;
        var outputVertices = outputBakedMesh.vertices;
        if (sourceVertices == null || outputVertices == null || sourceVertices.Length == 0 || outputVertices.Length == 0)
        {
            return false;
        }

        var normalizationBasis = Mathf.Max(sourceBakedMesh.bounds.size.magnitude, 0.0001f);
        var distances = new float[sourceVertices.Length];
        for (var i = 0; i < sourceVertices.Length; i++)
        {
            var sourceVertex = sourceVertices[i];
            var minSquaredDistance = float.PositiveInfinity;
            for (var j = 0; j < outputVertices.Length; j++)
            {
                var squaredDistance = (sourceVertex - outputVertices[j]).sqrMagnitude;
                if (squaredDistance < minSquaredDistance)
                {
                    minSquaredDistance = squaredDistance;
                }
            }

            distances[i] = Mathf.Sqrt(minSquaredDistance) / normalizationBasis;
        }

        Array.Sort(distances);
        sampleCount = distances.Length;
        var p95Index = Mathf.Clamp(Mathf.CeilToInt(sampleCount * 0.95f) - 1, 0, sampleCount - 1);
        globalP95Error = distances[p95Index];
        maxError = distances[sampleCount - 1];
        return true;
    }

    private static bool TryWriteComparisonMetricReport(string reportPath, SkinnedMeshComparisonMetricReport report)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || report == null)
        {
            return false;
        }

        try
        {
            var absoluteReportPath = Path.IsPathRooted(reportPath)
                ? Path.GetFullPath(reportPath)
                : Path.GetFullPath(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), reportPath));

            var directoryPath = Path.GetDirectoryName(absoluteReportPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(absoluteReportPath, JsonUtility.ToJson(report, prettyPrint: true));
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to write skinned mesh comparison metric report '{reportPath}': {ex.Message}");
            return false;
        }
    }

    private static bool TryInstantiateImportedModel(string assetPath, out GameObject instance)
    {
        instance = null;

        var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (sourceRoot == null)
        {
            return false;
        }

        instance = PrefabUtility.InstantiatePrefab(sourceRoot) as GameObject;
        return instance != null;
    }

    private static void ResetValidationRootTransform(Transform transform)
    {
        if (transform == null)
        {
            return;
        }

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private static bool TryFindSkinnedRendererForValidation(GameObject root, Mesh expectedMesh, out SkinnedMeshRenderer renderer)
    {
        renderer = null;
        if (root == null)
        {
            return false;
        }

        var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        if (expectedMesh != null)
        {
            foreach (var candidate in renderers)
            {
                if (candidate != null && candidate.sharedMesh != null && candidate.sharedMesh.name == expectedMesh.name)
                {
                    renderer = candidate;
                    return true;
                }
            }
        }

        renderer = renderers.FirstOrDefault(candidate => candidate != null && candidate.sharedMesh != null);
        return renderer != null;
    }

    private static bool TryValidateBakedMeshData(Mesh bakedMesh, string assetPath, out string validationSummary)
    {
        validationSummary = null;

        if (bakedMesh == null)
        {
            Debug.LogError($"Trimmed FBX rest-pose validation failed because no baked mesh was produced for '{assetPath}'.");
            return false;
        }

        if (bakedMesh.vertexCount == 0)
        {
            Debug.LogError($"Trimmed FBX rest-pose validation failed because baked mesh for '{assetPath}' has zero vertices.");
            return false;
        }

        var vertices = bakedMesh.vertices;
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
            {
                Debug.LogError($"Trimmed FBX rest-pose validation failed because baked mesh for '{assetPath}' contains non-finite vertex data.");
                return false;
            }
        }

        var bounds = bakedMesh.bounds;
        if (!float.IsFinite(bounds.center.x) || !float.IsFinite(bounds.center.y) || !float.IsFinite(bounds.center.z) ||
            !float.IsFinite(bounds.size.x) || !float.IsFinite(bounds.size.y) || !float.IsFinite(bounds.size.z))
        {
            Debug.LogError($"Trimmed FBX rest-pose validation failed because baked bounds for '{assetPath}' are non-finite.");
            return false;
        }

        if (bounds.size.sqrMagnitude <= 0f)
        {
            Debug.LogError($"Trimmed FBX rest-pose validation failed because baked bounds for '{assetPath}' are degenerate.");
            return false;
        }

        validationSummary = $"mesh='{bakedMesh.name}', vertices={bakedMesh.vertexCount}, diagonal={bounds.size.magnitude:F3}";
        return true;
    }

    private static bool TryExportTrimFbx(SkinnedMeshRenderer sourceRenderer, Mesh simplifiedMesh, string sourceFbxPath, string outputAssetPath)
    {
        var outputFilePath = AssetPathToAbsolutePath(outputAssetPath);
        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using (var manager = FbxManager.Create())
        {
            if (manager == null)
            {
                Debug.LogError("Failed to create FBX manager.");
                return false;
            }

            var ioSettings = FbxIOSettings.Create(manager, Globals.IOSROOT);
            manager.SetIOSettings(ioSettings);

            FbxExportUnitSettings unitSettings;
            try
            {
                unitSettings = LoadSourceFbxUnitSettings(manager, ioSettings, sourceFbxPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to read source FBX unit from '{sourceFbxPath}': {ex.Message}");
                return false;
            }

            using (unitSettings)
            using (var scene = FbxScene.Create(manager, Path.GetFileNameWithoutExtension(outputAssetPath)))
            {
                if (scene == null)
                {
                    Debug.LogError("Failed to create FBX scene.");
                    return false;
                }

                scene.GetGlobalSettings().SetAxisSystem(FbxAxisSystem.OpenGL);
                scene.GetGlobalSettings().SetSystemUnit(unitSettings.SystemUnit);

                var skeletonNodes = BuildSkeletonNodes(scene, sourceRenderer, unitSettings);
                var meshNode = CreateMeshNode(scene, sourceRenderer, simplifiedMesh, skeletonNodes, unitSettings);
                if (meshNode == null)
                {
                    return false;
                }

                ApplyMaterials(scene, meshNode, sourceRenderer.sharedMaterials);
                ApplySkin(scene, meshNode, simplifiedMesh, sourceRenderer, skeletonNodes);
                AddBindPose(scene, meshNode, skeletonNodes);

                using (var exporter = FbxExporter.Create(manager, "TrimFbxExporter"))
                {
                    if (exporter == null)
                    {
                        Debug.LogError("Failed to create FBX exporter.");
                        return false;
                    }

                    var writerId = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
                    if (writerId < 0)
                    {
                        writerId = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX ascii (*.fbx)");
                    }

                    if (!exporter.Initialize(outputFilePath, writerId, ioSettings))
                    {
                        Debug.LogError($"Failed to initialize FBX exporter for '{outputFilePath}': {exporter.GetStatus().GetErrorString()}");
                        return false;
                    }

                    if (!exporter.Export(scene))
                    {
                        Debug.LogError($"Failed to export FBX '{outputFilePath}': {exporter.GetStatus().GetErrorString()}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static FbxExportUnitSettings LoadSourceFbxUnitSettings(FbxManager manager, FbxIOSettings ioSettings, string sourceFbxPath)
    {
        var sourceFilePath = AssetPathToAbsolutePath(sourceFbxPath);
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException($"Source FBX file does not exist: {sourceFilePath}", sourceFilePath);
        }

        using (var sourceScene = FbxScene.Create(manager, "SourceUnitScene"))
        using (var importer = FbxImporter.Create(manager, "SourceUnitImporter"))
        {
            if (sourceScene == null)
            {
                throw new InvalidOperationException("Failed to create source FBX scene for unit detection.");
            }

            if (importer == null)
            {
                throw new InvalidOperationException("Failed to create source FBX importer for unit detection.");
            }

            if (!importer.Initialize(sourceFilePath, -1, ioSettings))
            {
                throw new InvalidOperationException($"Failed to initialize FBX importer for '{sourceFilePath}': {importer.GetStatus().GetErrorString()}");
            }

            if (!importer.Import(sourceScene))
            {
                throw new InvalidOperationException($"Failed to import source FBX '{sourceFilePath}' for unit detection: {importer.GetStatus().GetErrorString()}");
            }

            var sourceUnit = sourceScene.GetGlobalSettings().GetSystemUnit();
            try
            {
                return FbxExportUnitSettings.FromSource(sourceUnit, sourceFilePath);
            }
            finally
            {
                sourceUnit?.Dispose();
            }
        }
    }

    private static Dictionary<Transform, FbxNode> BuildSkeletonNodes(FbxScene scene, SkinnedMeshRenderer renderer, FbxExportUnitSettings unitSettings)
    {
        var nodes = new Dictionary<Transform, FbxNode>();
        var boneSet = new HashSet<Transform>(renderer.bones.Where(t => t != null));

        if (renderer.rootBone != null)
        {
            boneSet.Add(renderer.rootBone);
        }

        var root = renderer.transform.root;
        var pending = new Stack<Transform>();
        pending.Push(root);
        pending.Push(renderer.transform);

        foreach (var bone in renderer.bones)
        {
            if (bone != null)
            {
                pending.Push(bone);
            }
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current == null || nodes.ContainsKey(current))
            {
                continue;
            }

            var node = FbxNode.Create(scene, current.name);
            if (node == null)
            {
                continue;
            }

            if (current == renderer.transform)
            {
                node.SetNodeAttribute(FbxNull.Create(scene, current.name + "_Null"));
            }
            else if (boneSet.Contains(current))
            {
                var skeleton = FbxSkeleton.Create(scene, current.name);
                skeleton.SetSkeletonType(current.parent != null && boneSet.Contains(current.parent)
                    ? FbxSkeleton.EType.eLimbNode
                    : FbxSkeleton.EType.eRoot);
                node.SetNodeAttribute(skeleton);
            }
            else
            {
                node.SetNodeAttribute(FbxNull.Create(scene, current.name));
            }

            node.LclTranslation.Set(ToFbxTranslation(current.localPosition, unitSettings));
            node.LclRotation.Set(new FbxDouble3(current.localEulerAngles.x, current.localEulerAngles.y, current.localEulerAngles.z));
            node.LclScaling.Set(new FbxDouble3(current.localScale.x, current.localScale.y, current.localScale.z));

            nodes[current] = node;

            if (current.parent != null)
            {
                pending.Push(current.parent);
            }
        }

        foreach (var pair in nodes)
        {
            var transform = pair.Key;
            var node = pair.Value;
            if (transform.parent != null && nodes.TryGetValue(transform.parent, out var parentNode))
            {
                parentNode.AddChild(node);
            }
            else
            {
                scene.GetRootNode().AddChild(node);
            }
        }

        return nodes;
    }

    private static FbxNode CreateMeshNode(FbxScene scene, SkinnedMeshRenderer renderer, Mesh simplifiedMesh, Dictionary<Transform, FbxNode> nodes, FbxExportUnitSettings unitSettings)
    {
        if (!nodes.TryGetValue(renderer.transform, out var meshNode))
        {
            meshNode = FbxNode.Create(scene, renderer.name);
            if (meshNode == null)
            {
                Debug.LogError("Failed to create FBX mesh node.");
                return null;
            }

            meshNode.LclTranslation.Set(ToFbxTranslation(renderer.transform.localPosition, unitSettings));
            meshNode.LclRotation.Set(new FbxDouble3(renderer.transform.localEulerAngles.x, renderer.transform.localEulerAngles.y, renderer.transform.localEulerAngles.z));
            meshNode.LclScaling.Set(new FbxDouble3(renderer.transform.localScale.x, renderer.transform.localScale.y, renderer.transform.localScale.z));
            scene.GetRootNode().AddChild(meshNode);
        }

        var fbxMesh = FbxMesh.Create(scene, simplifiedMesh.name);
        if (fbxMesh == null)
        {
            Debug.LogError("Failed to create FBX mesh.");
            return null;
        }

        meshNode.SetNodeAttribute(fbxMesh);
        fbxMesh.InitControlPoints(simplifiedMesh.vertexCount);

        var vertices = simplifiedMesh.vertices;
        for (var i = 0; i < vertices.Length; i++)
        {
            fbxMesh.SetControlPointAt(ToFbxPosition(vertices[i], unitSettings), i);
        }

        var triangles = simplifiedMesh.GetIndices(0);
        for (var i = 0; i < triangles.Length; i += 3)
        {
            fbxMesh.BeginPolygon();
            fbxMesh.AddPolygon(triangles[i]);
            fbxMesh.AddPolygon(triangles[i + 1]);
            fbxMesh.AddPolygon(triangles[i + 2]);
            fbxMesh.EndPolygon();
        }

        var layer = fbxMesh.GetLayer(0);
        if (layer == null)
        {
            fbxMesh.CreateLayer();
            layer = fbxMesh.GetLayer(0);
        }

        if (simplifiedMesh.normals.Length == simplifiedMesh.vertexCount)
        {
            using (var normalElement = FbxLayerElementNormal.Create(fbxMesh, "Normals"))
            {
                normalElement.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
                normalElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                var directArray = normalElement.GetDirectArray();
                for (var i = 0; i < triangles.Length; i++)
                {
                    directArray.Add(ToFbxDirection(simplifiedMesh.normals[triangles[i]]));
                }
                layer.SetNormals(normalElement);
            }
        }

        if (simplifiedMesh.tangents.Length == simplifiedMesh.vertexCount)
        {
            using (var tangentElement = FbxLayerElementTangent.Create(fbxMesh, "Tangents"))
            {
                tangentElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                tangentElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                var directArray = tangentElement.GetDirectArray();
                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
                {
                    var tangent = simplifiedMesh.tangents[i];
                    directArray.Add(new FbxVector4(tangent.x, tangent.y, tangent.z, tangent.w));
                }
                layer.SetTangents(tangentElement);
            }
        }

        if (simplifiedMesh.colors32.Length == simplifiedMesh.vertexCount)
        {
            using (var colorElement = FbxLayerElementVertexColor.Create(fbxMesh, "VertexColors"))
            {
                colorElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                colorElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                var directArray = colorElement.GetDirectArray();
                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
                {
                    var color = simplifiedMesh.colors32[i];
                    directArray.Add(new FbxColor(color.r / 255.0, color.g / 255.0, color.b / 255.0, color.a / 255.0));
                }
                layer.SetVertexColors(colorElement);
            }
        }

        if (simplifiedMesh.uv.Length == simplifiedMesh.vertexCount)
        {
            using (var uvElement = FbxLayerElementUV.Create(fbxMesh, "UVSet"))
            {
                uvElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
                uvElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                var directArray = uvElement.GetDirectArray();
                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
                {
                    var uv = simplifiedMesh.uv[i];
                    directArray.Add(new FbxVector2(uv.x, uv.y));
                }
                layer.SetUVs(uvElement, FbxLayerElement.EType.eTextureDiffuse);
            }
        }

        return meshNode;
    }

    private static void ApplyMaterials(FbxScene scene, FbxNode meshNode, Material[] materials)
    {
        if (materials == null || materials.Length == 0)
        {
            return;
        }

        foreach (var material in materials)
        {
            var materialName = material != null && !string.IsNullOrWhiteSpace(material.name) ? material.name : "Material";
            var fbxMaterial = FbxSurfacePhong.Create(scene, materialName);
            if (fbxMaterial == null)
            {
                continue;
            }

            meshNode.AddMaterial(fbxMaterial);
        }
    }

    private static void ApplySkin(FbxScene scene, FbxNode meshNode, Mesh simplifiedMesh, SkinnedMeshRenderer renderer, Dictionary<Transform, FbxNode> nodes)
    {
        var bones = renderer.bones.Where(t => t != null).ToArray();
        if (bones.Length == 0)
        {
            return;
        }

        var boneWeights = simplifiedMesh.GetAllBoneWeights();
        var bonesPerVertex = simplifiedMesh.GetBonesPerVertex();
        try
        {
            var clusters = new List<FbxCluster>(bones.Length);
            var clusterByBoneIndex = new Dictionary<int, FbxCluster>(bones.Length);
            for (var i = 0; i < bones.Length; i++)
            {
                if (!nodes.TryGetValue(bones[i], out var boneNode))
                {
                    continue;
                }

                var cluster = FbxCluster.Create(scene, bones[i].name + "_Cluster");
                cluster.SetLink(boneNode);
                cluster.SetLinkMode(FbxCluster.ELinkMode.eTotalOne);
                cluster.SetTransformMatrix(meshNode.EvaluateGlobalTransform());
                cluster.SetTransformLinkMatrix(boneNode.EvaluateGlobalTransform());
                clusters.Add(cluster);
                clusterByBoneIndex[i] = cluster;
            }

            var weightOffset = 0;
            for (var vertexIndex = 0; vertexIndex < bonesPerVertex.Length; vertexIndex++)
            {
                var influenceCount = bonesPerVertex[vertexIndex];
                for (var influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++)
                {
                    var weight = boneWeights[weightOffset++];
                    if (weight.boneIndex < 0 || weight.weight <= 0f || !clusterByBoneIndex.TryGetValue(weight.boneIndex, out var cluster))
                    {
                        continue;
                    }

                    cluster.AddControlPointIndex(vertexIndex, weight.weight);
                }
            }

            if (clusters.Count == 0)
            {
                return;
            }

            var skin = FbxSkin.Create(scene, "Skin");
            foreach (var cluster in clusters)
            {
                skin.AddCluster(cluster);
            }

            meshNode.GetMesh().AddDeformer(skin);
        }
        finally
        {
            if (boneWeights.IsCreated)
            {
                boneWeights.Dispose();
            }

            if (bonesPerVertex.IsCreated)
            {
                bonesPerVertex.Dispose();
            }
        }
    }

    private static void AddBindPose(FbxScene scene, FbxNode meshNode, Dictionary<Transform, FbxNode> nodes)
    {
        var pose = FbxPose.Create(scene, "BindPose");
        pose.SetIsBindPose(true);

        foreach (var pair in nodes)
        {
            var node = pair.Value;
            var matrix = new FbxMatrix(node.EvaluateGlobalTransform());
            pose.Add(node, matrix);
        }

        pose.Add(meshNode, new FbxMatrix(meshNode.EvaluateGlobalTransform()));
        scene.AddPose(pose);
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }

    private static FbxVector4 ToFbxPosition(Vector3 value, FbxExportUnitSettings unitSettings)
    {
        return new FbxVector4(value.x * unitSettings.FbxUnitsPerUnityUnit, value.y * unitSettings.FbxUnitsPerUnityUnit, value.z * unitSettings.FbxUnitsPerUnityUnit);
    }

    private static FbxVector4 ToFbxDirection(Vector3 value)
    {
        return new FbxVector4(value.x, value.y, value.z);
    }

    private static FbxDouble3 ToFbxTranslation(Vector3 value, FbxExportUnitSettings unitSettings)
    {
        return new FbxDouble3(value.x * unitSettings.FbxUnitsPerUnityUnit, value.y * unitSettings.FbxUnitsPerUnityUnit, value.z * unitSettings.FbxUnitsPerUnityUnit);
    }

    private static void OpenSkinnedMeshSimplificationWindow(Mesh mesh, string assetPath, SkinnedMeshRenderer renderer)
    {
        SkinnedMeshSimplificationWindow.Open(mesh, assetPath, renderer);
    }

    private sealed class SkinnedMeshSimplificationWindow : EditorWindow
    {
        private const float PreviewHeight = 240f;

        private enum ValidationSeverity
        {
            Pass,
            Warning,
            Fail
        }

        private readonly struct ValidationSnapshot
        {
            public ValidationSnapshot(ValidationSeverity severity, string summary, string detail)
            {
                Severity = severity;
                Summary = summary;
                Detail = detail;
            }

            public ValidationSeverity Severity { get; }
            public string Summary { get; }
            public string Detail { get; }
        }

        [SerializeField] private Mesh sourceMesh;
        [SerializeField] private string sourceAssetPath;
        [SerializeField] private int reductionPercent = 50;
        [SerializeField] private string outputAssetPath;
        [SerializeField] private bool isExternalSource;
        [SerializeField] private string externalSourcePath;
        [SerializeField] private string statusMessage;
        [SerializeField] private MessageType statusType = MessageType.Info;

        private Mesh simplifiedMesh;
        private SkinnedMeshRenderer sourceRenderer;
        private PreviewRenderUtility previewUtility;
        private Material previewMaterial;
        private Vector2 scrollPosition;
        private bool validationSnapshotsDirty = true;
        private string validatedOutputAssetPath;
        private ValidationSnapshot sourceMeshValidationSnapshot;
        private ValidationSnapshot previewMeshValidationSnapshot;
        private ValidationSnapshot outputImportValidationSnapshot;
        private ValidationSnapshot outputMeshValidationSnapshot;
        private ValidationSnapshot restPoseValidationSnapshot;
        private ValidationSnapshot neutralPoseValidationSnapshot;
        private ValidationSnapshot highDeformationPoseValidationSnapshot;
        private SkinnedMeshComparisonMetricReport comparisonMetricReport;

        public static void Open(Mesh mesh, string assetPath, SkinnedMeshRenderer renderer)
        {
            var window = GetWindow<SkinnedMeshSimplificationWindow>(utility: false, title: "Skin Simplify To FBX");
            window.minSize = new Vector2(840f, 620f);
            window.Initialize(mesh, assetPath, renderer);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EnsurePreviewResources();
            InvalidateValidationSnapshots();

            if (sourceMesh != null && simplifiedMesh == null)
            {
                RebuildSimplifiedMesh();
            }
        }

        private void OnDisable()
        {
            DestroyImmediatePreviewMesh();

            if (previewUtility != null)
            {
                previewUtility.Cleanup();
                previewUtility = null;
            }

            if (previewMaterial != null)
            {
                DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }
        }

        private void Initialize(Mesh mesh, string assetPath, SkinnedMeshRenderer renderer)
        {
            sourceMesh = mesh;
            sourceAssetPath = assetPath;
            sourceRenderer = renderer;

            var config = SkinnedMeshSimplificationConfig.Load();
            var entry = config.FindEntry(assetPath);
            reductionPercent = Mathf.Clamp(entry?.reductionPercent ?? 50, 0, 100);
            outputAssetPath = ResolveTrimFbxOutputPath(assetPath, entry?.outputPath);
            isExternalSource = entry?.isExternalSource ?? false;
            externalSourcePath = entry?.externalSourcePath;
            SetStatus("Adjust reductionPercent, review the side-by-side preview, then save or export.", MessageType.Info);

            InvalidateValidationSnapshots();
            RebuildSimplifiedMesh();
        }

        private void OnGUI()
        {
            if (sourceMesh == null || string.IsNullOrWhiteSpace(sourceAssetPath))
            {
                EditorGUILayout.HelpBox("Select a supported skinned mesh asset from the Project window, then reopen the tool.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawSelectionInfo();
            EditorGUILayout.Space();
            DrawAuthoringControls();
            EditorGUILayout.Space();
            DrawMetrics();
            EditorGUILayout.Space();
            DrawPreviews();
            EditorGUILayout.Space();
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectionInfo()
        {
            EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Source Mesh", sourceMesh, typeof(Mesh), false);
                EditorGUILayout.TextField("Entry Id", sourceAssetPath);
                if (isExternalSource)
                {
                    EditorGUILayout.Toggle("External Source", true);
                    EditorGUILayout.TextField("External Source Path", externalSourcePath ?? string.Empty);
                }
            }
        }

        private void DrawAuthoringControls()
        {
            EditorGUILayout.LabelField("Authoring", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var nextReductionPercent = EditorGUILayout.IntSlider("Reduction Percent", reductionPercent, 0, 100);
            var nextOutputAssetPath = EditorGUILayout.TextField("Output Path", GetEffectiveOutputAssetPath());
            if (EditorGUI.EndChangeCheck())
            {
                reductionPercent = nextReductionPercent;
                outputAssetPath = nextOutputAssetPath;
                RebuildSimplifiedMesh();
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }
        }

        private void DrawMetrics()
        {
            EditorGUILayout.LabelField("Core Metrics", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var originalVertexCount = sourceMesh.vertexCount;
                var simplifiedVertexCount = simplifiedMesh != null ? simplifiedMesh.vertexCount : 0;
                var originalTriangleCount = GetTriangleCount(sourceMesh);
                var simplifiedTriangleCount = simplifiedMesh != null ? GetTriangleCount(simplifiedMesh) : 0;

                DrawMetricComparisonHeader();
                DrawMetricComparisonRow("Vertices", originalVertexCount, simplifiedMesh != null ? simplifiedVertexCount.ToString() : "n/a", simplifiedMesh != null ? FormatCountDelta(simplifiedVertexCount - originalVertexCount, originalVertexCount) : "n/a");
                DrawMetricComparisonRow("Triangles", originalTriangleCount, simplifiedMesh != null ? simplifiedTriangleCount.ToString() : "n/a", simplifiedMesh != null ? FormatCountDelta(simplifiedTriangleCount - originalTriangleCount, originalTriangleCount) : "n/a");
                DrawMetricComparisonRow("Reduction Target", "100%", $"{Mathf.Clamp(reductionPercent, 0, 100)}%", $"-{Mathf.Clamp(100 - reductionPercent, 0, 100)}% input");

                EditorGUILayout.Space(6f);
                DrawMetricRow("Output Target", GetEffectiveOutputAssetPath());
                EditorGUILayout.Space(8f);
                DrawComparisonMetrics();
                EditorGUILayout.Space(8f);
                DrawValidationFlags();
            }
        }

        private void DrawComparisonMetrics()
        {
            EditorGUILayout.LabelField("Comparison Metrics", EditorStyles.miniBoldLabel);

            RefreshValidationSnapshots();

            DrawMetricRow("Metric Status", FormatComparisonStatus(comparisonMetricReport), GetMetricDetail(comparisonMetricReport));
            DrawMetricRow("Joint Region P95", FormatMetricValue(comparisonMetricReport?.hasJointRegionP95Error ?? false, comparisonMetricReport != null ? comparisonMetricReport.jointRegionP95Error : 0f), FormatWorstPoseList(comparisonMetricReport?.jointRegionWorstFrames));
            DrawMetricRow("Global P95", FormatMetricValue(comparisonMetricReport?.hasGlobalP95Error ?? false, comparisonMetricReport != null ? comparisonMetricReport.globalP95Error : 0f), FormatWorstPoseList(comparisonMetricReport?.globalWorstFrames));
            DrawMetricRow("Screen Diff", FormatMetricValue(comparisonMetricReport?.hasScreenSpaceDiff ?? false, comparisonMetricReport != null ? comparisonMetricReport.screenSpaceDiff : 0f), FormatWorstPoseList(comparisonMetricReport?.screenSpaceWorstFrames));
            DrawMetricRow("Max Error", FormatMetricValue(comparisonMetricReport?.hasMaxError ?? false, comparisonMetricReport != null ? comparisonMetricReport.maxError : 0f), FormatRepresentativePoseCoverage(comparisonMetricReport));
        }

        private void DrawValidationFlags()
        {
            EditorGUILayout.LabelField("Validation Flags", EditorStyles.miniBoldLabel);

            RefreshValidationSnapshots();

            DrawValidationRow("Source Mesh", sourceMeshValidationSnapshot);
            DrawValidationRow("Preview Mesh", previewMeshValidationSnapshot);
            DrawValidationRow("Output Import", outputImportValidationSnapshot);
            DrawValidationRow("Output Mesh", outputMeshValidationSnapshot);
            DrawValidationRow("Rest Pose", restPoseValidationSnapshot);
            DrawValidationRow("Neutral Pose", neutralPoseValidationSnapshot);
            DrawValidationRow("High-Deform", highDeformationPoseValidationSnapshot);
        }

        private void DrawMetricComparisonHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(string.Empty, EditorStyles.miniBoldLabel, GUILayout.Width(140f));
                EditorGUILayout.LabelField("Original", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                EditorGUILayout.LabelField("Simplified", EditorStyles.miniBoldLabel, GUILayout.Width(110f));
                EditorGUILayout.LabelField("Delta", EditorStyles.miniBoldLabel);
            }
        }

        private void DrawMetricComparisonRow(string label, int originalValue, string simplifiedValue, string deltaValue)
        {
            DrawMetricComparisonRow(label, originalValue.ToString(), simplifiedValue, deltaValue);
        }

        private void DrawMetricComparisonRow(string label, string originalValue, string simplifiedValue, string deltaValue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(label, GUILayout.Width(140f));
                EditorGUILayout.SelectableLabel(originalValue, EditorStyles.textField, GUILayout.Width(110f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(simplifiedValue, EditorStyles.textField, GUILayout.Width(110f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(deltaValue, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawValidationRow(string label, ValidationSnapshot snapshot)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(label, GUILayout.Width(140f));
                EditorGUILayout.SelectableLabel(snapshot.Summary, EditorStyles.textField, GUILayout.Width(110f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(snapshot.Detail, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawMetricRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(160f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawMetricRow(string label, string value, string detail)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(4f);
                EditorGUILayout.LabelField(label, GUILayout.Width(140f));
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Width(110f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.SelectableLabel(detail, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawPreviews()
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var previewRect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            var spacing = 10f;
            var columnWidth = Mathf.Max(0f, (previewRect.width - spacing) * 0.5f);
            var originalRect = new Rect(previewRect.x, previewRect.y, columnWidth, previewRect.height);
            var simplifiedRect = new Rect(previewRect.x + columnWidth + spacing, previewRect.y, columnWidth, previewRect.height);

            DrawMeshPreview(originalRect, sourceMesh, "Original");
            DrawMeshPreview(simplifiedRect, simplifiedMesh, $"Simplified ({reductionPercent}%)");
        }

        private void DrawActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Simplified Preview", GUILayout.Height(28f)))
                {
                    RebuildSimplifiedMesh();
                }

                if (GUILayout.Button("Save Config", GUILayout.Height(28f)))
                {
                    TrySaveCurrentConfig();
                }

                if (GUILayout.Button("Export Trimmed FBX", GUILayout.Height(28f)))
                {
                    ExportCurrentSelectionToFbx();
                }
            }
        }

        private void RebuildSimplifiedMesh()
        {
            DestroyImmediatePreviewMesh();
            InvalidateValidationSnapshots();

            if (sourceMesh == null)
            {
                return;
            }

            try
            {
                var simplifier = new SkinMeshOpt();
                simplifier.Init(sourceMesh);
                simplifiedMesh = simplifier.Simplify(reductionPercent);
                if (simplifiedMesh != null)
                {
                    simplifiedMesh.hideFlags = HideFlags.HideAndDontSave;
                    simplifiedMesh.name = sourceMesh.name + $"_preview_{reductionPercent}";
                }

                SetStatus($"Preview updated for reductionPercent={reductionPercent}.", MessageType.Info);
            }
            catch (Exception ex)
            {
                simplifiedMesh = null;
                SetStatus($"Failed to rebuild simplified preview: {ex.Message}", MessageType.Error);
            }

            Repaint();
        }

        private void ExportCurrentSelectionToFbx()
        {
            if (!TryResolveSourceRenderer())
            {
                SetStatus($"Failed to resolve a SkinnedMeshRenderer for '{sourceAssetPath}'.", MessageType.Error);
                return;
            }

            if (!TrySaveCurrentConfig(silentSuccess: true))
            {
                return;
            }

            var config = SkinnedMeshSimplificationConfig.Load();
            var entry = config.FindEntry(sourceAssetPath) ?? BuildCurrentEntry();

            CleanupStagedExternalSources();
            try
            {
                if (!TryRunSkinnedMeshFbxExport(sourceMesh, sourceAssetPath, sourceRenderer, config, entry, persistConfigEntry: false, out var exportedAssetPath))
                {
                    SetStatus("Trimmed FBX export failed. Check the Console for details.", MessageType.Error);
                    return;
                }

                AssetDatabase.ImportAsset(exportedAssetPath, ImportAssetOptions.ForceSynchronousImport);
                var exportedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportedAssetPath);
                if (exportedAsset != null)
                {
                    EditorGUIUtility.PingObject(exportedAsset);
                }

                InvalidateValidationSnapshots();
                SetStatus($"Exported trimmed FBX: '{exportedAssetPath}'", MessageType.Info);
                Repaint();
            }
            finally
            {
                CleanupStagedExternalSources();
            }
        }

        private bool TrySaveCurrentConfig(bool silentSuccess = false)
        {
            var config = SkinnedMeshSimplificationConfig.Load();
            var savedEntry = config.UpsertEntry(sourceAssetPath);
            var currentEntry = BuildCurrentEntry();
            savedEntry.isExternalSource = currentEntry.isExternalSource;
            savedEntry.externalSourcePath = currentEntry.externalSourcePath;
            savedEntry.outputPath = currentEntry.outputPath;
            savedEntry.reductionPercent = currentEntry.reductionPercent;

            if (!config.Save())
            {
                SetStatus("Failed to save the skinned mesh simplification config. Check the Console for details.", MessageType.Error);
                return false;
            }

            if (!silentSuccess)
            {
                SetStatus($"Saved config entry for '{sourceAssetPath}'.", MessageType.Info);
            }

            return true;
        }

        private SkinnedMeshSimplificationConfig.Entry BuildCurrentEntry()
        {
            return new SkinnedMeshSimplificationConfig.Entry
            {
                entryId = sourceAssetPath,
                isExternalSource = isExternalSource,
                externalSourcePath = externalSourcePath,
                outputPath = GetEffectiveOutputAssetPath(),
                reductionPercent = reductionPercent
            };
        }

        private string GetEffectiveOutputAssetPath()
        {
            return ResolveTrimFbxOutputPath(sourceAssetPath, outputAssetPath);
        }

        private bool TryResolveSourceRenderer()
        {
            if (sourceRenderer != null)
            {
                return true;
            }

            return TryGetSourceSkinnedMeshRenderer(sourceMesh, sourceAssetPath, out sourceRenderer);
        }

        private void InvalidateValidationSnapshots()
        {
            validationSnapshotsDirty = true;
            validatedOutputAssetPath = null;
        }

        private void RefreshValidationSnapshots()
        {
            var effectiveOutputAssetPath = GetEffectiveOutputAssetPath();
            if (!validationSnapshotsDirty && string.Equals(validatedOutputAssetPath, effectiveOutputAssetPath, StringComparison.Ordinal))
            {
                return;
            }

            validatedOutputAssetPath = effectiveOutputAssetPath;
            sourceMeshValidationSnapshot = EvaluateMeshValidation(sourceMesh, sourceAssetPath, requireSkinData: true);
            previewMeshValidationSnapshot = EvaluateMeshValidation(simplifiedMesh, "Preview Mesh", requireSkinData: true);
            outputImportValidationSnapshot = EvaluateOutputImportValidation(effectiveOutputAssetPath);
            outputMeshValidationSnapshot = EvaluateOutputMeshValidation(effectiveOutputAssetPath);
            restPoseValidationSnapshot = EvaluateOutputRestPoseValidation(effectiveOutputAssetPath, outputImportValidationSnapshot, outputMeshValidationSnapshot);
            neutralPoseValidationSnapshot = EvaluateOutputAnimatedPoseValidation(effectiveOutputAssetPath, "neutral", NeutralPoseValidationClipSuffixes, outputImportValidationSnapshot, outputMeshValidationSnapshot);
            highDeformationPoseValidationSnapshot = EvaluateOutputAnimatedPoseValidation(effectiveOutputAssetPath, "highDeformation", HighDeformationPoseValidationClipSuffixes, outputImportValidationSnapshot, outputMeshValidationSnapshot);
            comparisonMetricReport = EvaluateOutputComparisonMetricReport(effectiveOutputAssetPath, outputImportValidationSnapshot, outputMeshValidationSnapshot);
            validationSnapshotsDirty = false;
        }

        private SkinnedMeshComparisonMetricReport EvaluateOutputComparisonMetricReport(string assetPath, ValidationSnapshot importSnapshot, ValidationSnapshot meshSnapshot)
        {
            var report = new SkinnedMeshComparisonMetricReport
            {
                outputAssetPath = assetPath,
                comparisonStatus = "not_computed"
            };

            if (sourceMesh == null)
            {
                report.failureReasons.Add("Source mesh is unavailable for comparison metrics.");
                return report;
            }

            if (importSnapshot.Severity != ValidationSeverity.Pass)
            {
                report.failureReasons.Add("Export the trimmed FBX once to evaluate comparison metrics.");
                return report;
            }

            if (meshSnapshot.Severity == ValidationSeverity.Fail)
            {
                report.failureReasons.Add("Fix the imported output mesh validation before evaluating comparison metrics.");
                return report;
            }

            TryBuildComparisonMetricReport(sourceAssetPath, sourceMesh, assetPath, report, logErrors: false);
            return report;
        }

        private ValidationSnapshot EvaluateOutputRestPoseValidation(string assetPath, ValidationSnapshot importSnapshot, ValidationSnapshot meshSnapshot)
        {
            if (sourceMesh == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Missing", "Source mesh is unavailable for rest-pose validation.");
            }

            if (importSnapshot.Severity != ValidationSeverity.Pass)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Pending", "Export the trimmed FBX once to evaluate the rest pose.");
            }

            if (meshSnapshot.Severity == ValidationSeverity.Fail)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Blocked", "Fix the imported output mesh validation before evaluating the rest pose.");
            }

            if (!TryValidateTrimmedFbxRestPose(sourceAssetPath, sourceMesh, assetPath, out var validationSummary))
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Rest-pose validation failed. Check the Console for details.");
            }

            return new ValidationSnapshot(ValidationSeverity.Pass, "Pass", validationSummary);
        }

        private ValidationSnapshot EvaluateOutputAnimatedPoseValidation(string assetPath, string poseCategory, string[] clipSuffixes, ValidationSnapshot importSnapshot, ValidationSnapshot meshSnapshot)
        {
            if (sourceMesh == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Missing", $"Source mesh is unavailable for {poseCategory} validation.");
            }

            if (importSnapshot.Severity != ValidationSeverity.Pass)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Pending", $"Export the trimmed FBX once to evaluate the {poseCategory} pose.");
            }

            if (meshSnapshot.Severity == ValidationSeverity.Fail)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Blocked", $"Fix the imported output mesh validation before evaluating the {poseCategory} pose.");
            }

            if (!TryValidateTrimmedFbxAnimatedPoseCategory(sourceAssetPath, sourceMesh, assetPath, poseCategory, clipSuffixes, out var validationSummary, logErrors: false))
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", validationSummary ?? $"{poseCategory} pose validation failed.");
            }

            if (validationSummary.Contains("skipped=true", StringComparison.Ordinal))
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Skipped", validationSummary);
            }

            return new ValidationSnapshot(ValidationSeverity.Pass, "Pass", validationSummary);
        }

        private ValidationSnapshot EvaluateOutputImportValidation(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Missing", "Output path is empty.");
            }

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Not Exported", "No imported output asset exists yet at the current output path.");
            }

            var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (sourceRoot == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Imported", "Output exists, but no import root GameObject was resolved yet.");
            }

            return new ValidationSnapshot(ValidationSeverity.Pass, "Imported", $"Resolved import root '{sourceRoot.name}'.");
        }

        private ValidationSnapshot EvaluateOutputMeshValidation(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Missing", "Output path is empty.");
            }

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Pending", "Export the trimmed FBX once to evaluate the imported output mesh.");
            }

            var outputRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (outputRoot == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Imported", "Output exists, but no import root GameObject was resolved yet.");
            }

            var outputRenderer = outputRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(candidate => candidate != null && candidate.sharedMesh != null);
            if (outputRenderer == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Imported output does not expose a SkinnedMeshRenderer with a mesh.");
            }

            var outputMesh = outputRenderer.sharedMesh;
            if (outputMesh == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Imported output renderer has no shared mesh.");
            }

            if (outputMesh.subMeshCount != 1)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Expected 1 submesh, found {outputMesh.subMeshCount}.");
            }

            if (outputMesh.GetTopology(0) != MeshTopology.Triangles)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Topology is {outputMesh.GetTopology(0)} instead of Triangles.");
            }

            if (outputMesh.blendShapeCount > 0)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"BlendShapes are present ({outputMesh.blendShapeCount}).");
            }

            if (outputMesh.bindposes == null || outputMesh.bindposes.Length == 0)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Bindposes are missing.");
            }

            if (importer.isReadable)
            {
                return new ValidationSnapshot(ValidationSeverity.Warning, "Readable", "Imported output is still read/write enabled. Final trimmed output should disable read/write.");
            }

            return new ValidationSnapshot(ValidationSeverity.Pass, "Ready", "Imported output is skinned and read/write is disabled for the final asset.");
        }

        private static ValidationSnapshot EvaluateMeshValidation(Mesh mesh, string assetLabel, bool requireSkinData)
        {
            if (mesh == null)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Missing", $"{assetLabel} could not be resolved.");
            }

            if (mesh.subMeshCount != 1)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Expected 1 submesh, found {mesh.subMeshCount}.");
            }

            if (!mesh.isReadable)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Mesh read/write is disabled.");
            }

            if (mesh.GetTopology(0) != MeshTopology.Triangles)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Topology is {mesh.GetTopology(0)} instead of Triangles.");
            }

            if (mesh.blendShapeCount > 0)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"BlendShapes are present ({mesh.blendShapeCount}).");
            }

            if (!requireSkinData)
            {
                return new ValidationSnapshot(ValidationSeverity.Pass, "Valid", "Mesh satisfies the current non-skin validation rules.");
            }

            if (mesh.bindposes == null || mesh.bindposes.Length == 0)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Bindposes are missing.");
            }

            var bonesPerVertex = mesh.GetBonesPerVertex();
            try
            {
                if (bonesPerVertex.Length != mesh.vertexCount)
                {
                    return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Bone-weight entries {bonesPerVertex.Length} do not match vertex count {mesh.vertexCount}.");
                }

                for (var i = 0; i < bonesPerVertex.Length; i++)
                {
                    if (bonesPerVertex[i] > 4)
                    {
                        return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", "Some vertices exceed the 4-weight limit.");
                    }
                }
            }
            finally
            {
                if (bonesPerVertex.IsCreated)
                {
                    bonesPerVertex.Dispose();
                }
            }

            if (mesh.boneWeights.Length != mesh.vertexCount)
            {
                return new ValidationSnapshot(ValidationSeverity.Fail, "Invalid", $"Legacy bone weights {mesh.boneWeights.Length} do not match vertex count {mesh.vertexCount}.");
            }

            return new ValidationSnapshot(ValidationSeverity.Pass, "Valid", "Mesh satisfies the current skinned-mesh validation rules.");
        }

        private static bool TryLoadMeshAssetWithoutLogging(string assetPath, out Mesh mesh)
        {
            mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (mesh != null)
            {
                return true;
            }

            var meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();
            if (meshes.Length == 1)
            {
                mesh = meshes[0];
                return true;
            }

            return false;
        }

        private void DrawMeshPreview(Rect rect, Mesh mesh, string label)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            var headerRect = new Rect(rect.x + 8f, rect.y + 6f, Mathf.Max(0f, rect.width - 16f), 18f);
            EditorGUI.LabelField(headerRect, label, EditorStyles.boldLabel);

            var contentRect = new Rect(rect.x + 6f, rect.y + 28f, Mathf.Max(0f, rect.width - 12f), Mathf.Max(0f, rect.height - 34f));
            if (contentRect.width < 2f || contentRect.height < 2f)
            {
                return;
            }

            if (mesh == null)
            {
                EditorGUI.HelpBox(contentRect, "Preview mesh is not available.", MessageType.Info);
                return;
            }

            EnsurePreviewResources();
            previewUtility.BeginPreview(contentRect, GUIStyle.none);

            var bounds = mesh.bounds;
            var radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            var distance = Mathf.Max(2.5f, radius * 3.0f);
            var previewMatrix = Matrix4x4.Rotate(Quaternion.Euler(15f, -30f, 0f)) * Matrix4x4.Translate(-bounds.center);

            previewUtility.camera.clearFlags = CameraClearFlags.Color;
            previewUtility.camera.backgroundColor = new Color(0.18f, 0.19f, 0.22f, 1f);
            previewUtility.camera.fieldOfView = 30f;
            previewUtility.camera.nearClipPlane = 0.01f;
            previewUtility.camera.farClipPlane = distance * 6f;
            previewUtility.camera.transform.position = new Vector3(0f, 0f, -distance);
            previewUtility.camera.transform.rotation = Quaternion.identity;

            previewUtility.lights[0].intensity = 1.1f;
            previewUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            previewUtility.lights[1].intensity = 0.7f;
            previewUtility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            previewUtility.ambientColor = new Color(0.35f, 0.35f, 0.35f, 1f);

            previewUtility.DrawMesh(mesh, previewMatrix, previewMaterial, 0);
            previewUtility.camera.Render();

            var previewTexture = previewUtility.EndPreview();
            GUI.DrawTexture(contentRect, previewTexture, ScaleMode.StretchToFill, false);
        }

        private void EnsurePreviewResources()
        {
            if (previewUtility == null)
            {
                previewUtility = new PreviewRenderUtility();
            }

            if (previewMaterial == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                previewMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        private void DestroyImmediatePreviewMesh()
        {
            if (simplifiedMesh != null)
            {
                DestroyImmediate(simplifiedMesh);
                simplifiedMesh = null;
            }
        }

        private void SetStatus(string message, MessageType messageType)
        {
            statusMessage = message;
            statusType = messageType;
        }

        private static int GetTriangleCount(Mesh mesh)
        {
            if (mesh == null || mesh.subMeshCount == 0)
            {
                return 0;
            }

            return mesh.GetIndices(0).Length / 3;
        }

        private static float GetTriangleRatio(Mesh originalMesh, Mesh reducedMesh)
        {
            var originalTriangles = GetTriangleCount(originalMesh);
            if (originalTriangles <= 0)
            {
                return 0f;
            }

            return (float)GetTriangleCount(reducedMesh) / originalTriangles;
        }

        private static string FormatCountDelta(int deltaValue, int originalValue)
        {
            if (originalValue <= 0)
            {
                return deltaValue.ToString();
            }

            var ratio = (float)deltaValue / originalValue;
            return $"{deltaValue:+#;-#;0} ({ratio:P1})";
        }

        private static string FormatMetricValue(bool hasValue, float value)
        {
            return hasValue ? value.ToString("F4") : "n/a";
        }

        private static string FormatComparisonStatus(SkinnedMeshComparisonMetricReport report)
        {
            if (report == null)
            {
                return "n/a";
            }

            return string.IsNullOrWhiteSpace(report.comparisonStatus) ? "not_computed" : report.comparisonStatus;
        }

        private static string GetMetricDetail(SkinnedMeshComparisonMetricReport report)
        {
            if (report == null)
            {
                return "Comparison metrics are unavailable.";
            }

            if (!string.IsNullOrWhiteSpace(report.normalizationBasis))
            {
                if (report.failureReasons != null && report.failureReasons.Count > 0)
                {
                    return $"{report.normalizationBasis}; {report.failureReasons[0]}";
                }

                return report.normalizationBasis;
            }

            return report.failureReasons != null && report.failureReasons.Count > 0
                ? report.failureReasons[0]
                : "Comparison metrics are pending.";
        }

        private static string FormatWorstPoseList(List<int> worstFrames)
        {
            if (worstFrames == null || worstFrames.Count == 0)
            {
                return "Worst pose: n/a";
            }

            var labels = worstFrames
                .Select(GetRepresentativePoseLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct()
                .ToArray();
            if (labels.Length == 0)
            {
                return "Worst pose: n/a";
            }

            return "Worst pose: " + string.Join(", ", labels);
        }

        private static string FormatRepresentativePoseCoverage(SkinnedMeshComparisonMetricReport report)
        {
            if (report?.representativePoses == null || report.representativePoses.Count == 0)
            {
                return "Pose coverage: n/a";
            }

            var parts = report.representativePoses
                .Select(pose => $"{GetRepresentativePoseDisplayName(pose?.category)}={pose?.status ?? "n/a"}")
                .ToArray();
            return "Pose coverage: " + string.Join(", ", parts);
        }

        private static string GetRepresentativePoseLabel(int poseIndex)
        {
            return poseIndex switch
            {
                0 => "Rest",
                1 => "Neutral",
                2 => "High-Deform",
                _ => null
            };
        }

        private static string GetRepresentativePoseDisplayName(string category)
        {
            return category switch
            {
                "rest" => "Rest",
                "neutral" => "Neutral",
                "highDeformation" => "High-Deform",
                _ => string.IsNullOrWhiteSpace(category) ? "Unknown" : category
            };
        }
    }

    private sealed class FbxExportUnitSettings : IDisposable
    {
        public FbxSystemUnit SystemUnit { get; }
        public double FbxUnitsPerUnityUnit { get; }

        private FbxExportUnitSettings(FbxSystemUnit systemUnit, double fbxUnitsPerUnityUnit)
        {
            SystemUnit = systemUnit;
            FbxUnitsPerUnityUnit = fbxUnitsPerUnityUnit;
        }

        public static FbxExportUnitSettings FromSource(FbxSystemUnit sourceUnit, string sourceFilePath)
        {
            if (sourceUnit == null)
            {
                throw new InvalidOperationException($"Source FBX '{sourceFilePath}' does not define a readable system unit.");
            }

            var sourceScaleFactor = sourceUnit.GetScaleFactor();
            var sourceMultiplier = sourceUnit.GetMultiplier();
            if (sourceScaleFactor <= 0.0)
            {
                throw new InvalidOperationException($"Source FBX '{sourceFilePath}' has an invalid system unit scale factor: {sourceScaleFactor}.");
            }

            var exportUnit = new FbxSystemUnit(sourceScaleFactor, sourceMultiplier);
            var fbxUnitsPerUnityUnit = CentimetersPerUnityUnit / sourceScaleFactor;
            return new FbxExportUnitSettings(exportUnit, fbxUnitsPerUnityUnit);
        }

        public void Dispose()
        {
            SystemUnit?.Dispose();
        }
    }
}

[Serializable]
internal sealed class SkinnedMeshSimplificationConfig
{
    internal const string DefaultConfigAssetPath = "Assets/EditorConfig/SkinnedMeshSimplification.json";

    [Serializable]
    internal sealed class Entry
    {
        public string entryId;
        public bool isExternalSource;
        public string externalSourcePath;
        public string outputPath;
        public int reductionPercent = 50;
    }

    [Serializable]
    private sealed class Root
    {
        public List<Entry> entries = new List<Entry>();
    }

    public List<Entry> entries = new List<Entry>();

    public static SkinnedMeshSimplificationConfig Load(string configAssetPath = null)
    {
        var config = new SkinnedMeshSimplificationConfig();
        var normalizedConfigPath = NormalizeConfigPath(configAssetPath);
        var filePath = AssetPathToAbsolutePath(normalizedConfigPath);
        if (!File.Exists(filePath))
        {
            return config;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var root = JsonUtility.FromJson<Root>(json);
            if (root?.entries != null)
            {
                config.entries = root.entries;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to read skinned mesh simplification config '{normalizedConfigPath}': {ex.Message}");
        }

        return config;
    }

    public bool Save(string configAssetPath = null)
    {
        try
        {
            var normalizedConfigPath = NormalizeConfigPath(configAssetPath);
            var filePath = AssetPathToAbsolutePath(normalizedConfigPath);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var root = new Root
            {
                entries = (entries ?? new List<Entry>())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry?.entryId))
                    .Select(NormalizeEntry)
                    .OrderBy(entry => entry.entryId, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            File.WriteAllText(filePath, JsonUtility.ToJson(root, prettyPrint: true));

            if (normalizedConfigPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(normalizedConfigPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to write skinned mesh simplification config '{NormalizeConfigPath(configAssetPath)}': {ex.Message}");
            return false;
        }
    }

    public Entry FindEntry(string assetPath)
    {
        if (entries == null)
        {
            return null;
        }

        var normalized = assetPath.Replace('\\', '/');
        return entries.FirstOrDefault(entry => string.Equals(NormalizeEntryId(entry?.entryId), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public Entry UpsertEntry(string entryId)
    {
        entries ??= new List<Entry>();

        var normalizedEntryId = NormalizeEntryId(entryId);
        var existing = entries.FirstOrDefault(entry => string.Equals(NormalizeEntryId(entry?.entryId), normalizedEntryId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.entryId = normalizedEntryId;
            return existing;
        }

        var entry = new Entry
        {
            entryId = normalizedEntryId
        };
        entries.Add(entry);
        return entry;
    }

    private static Entry NormalizeEntry(Entry source)
    {
        return new Entry
        {
            entryId = NormalizeEntryId(source.entryId),
            isExternalSource = source.isExternalSource,
            externalSourcePath = NormalizeStoredPath(source.externalSourcePath),
            outputPath = NormalizeStoredPath(source.outputPath),
            reductionPercent = Mathf.Clamp(source.reductionPercent, 0, 100)
        };
    }

    private static string NormalizeEntryId(string entryId)
    {
        return string.IsNullOrWhiteSpace(entryId) ? null : entryId.Replace('\\', '/');
    }

    private static string NormalizeStoredPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/');
    }

    private static string NormalizeConfigPath(string configAssetPath)
    {
        return string.IsNullOrWhiteSpace(configAssetPath) ? DefaultConfigAssetPath : configAssetPath.Replace('\\', '/');
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized))
        {
            return Path.GetFullPath(normalized);
        }

        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }
}

[Serializable]
internal sealed class SkinnedMeshComparisonMetricReport
{
    public string entryId;
    public string outputAssetPath;
    public string comparisonStatus = "not_computed";
    public string normalizationBasis = "mesh_bounds_diagonal";
    public bool hasJointRegionP95Error;
    public float jointRegionP95Error;
    public List<int> jointRegionWorstFrames = new List<int>();
    public bool hasGlobalP95Error;
    public float globalP95Error;
    public bool hasMaxError;
    public float maxError;
    public bool hasScreenSpaceDiff;
    public float screenSpaceDiff;
    public List<int> screenSpaceWorstFrames = new List<int>();
    public List<int> globalWorstFrames = new List<int>();
    public List<string> failureReasons = new List<string>();
    public List<SkinnedMeshComparisonPoseMetricReport> representativePoses = new List<SkinnedMeshComparisonPoseMetricReport>();
}

[Serializable]
internal sealed class SkinnedMeshComparisonPoseMetricReport
{
    public string category;
    public string status;
    public string clipAssetPath;
    public string clipName;
    public string details;
    public bool hasJointRegionP95Error;
    public float jointRegionP95Error;
    public int jointSampleCount;
    public bool hasGlobalP95Error;
    public float globalP95Error;
    public bool hasMaxError;
    public float maxError;
    public bool hasScreenSpaceDiff;
    public float screenSpaceDiff;
    public int sampleCount;
}
