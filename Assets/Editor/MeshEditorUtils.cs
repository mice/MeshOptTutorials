using MeshOptimizer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public interface IMeshOpt
{
    void Init(Mesh mesh);
    Mesh Simplify(int percent, float target_error = 0.01f);
    Mesh Optimize();
    Mesh MergeSimplified(int[] percents, float[] target_errors);

    Mesh MergeLOD();
}

public static partial class MeshEditorUtils
{
    private static readonly int[] DefaultMergedSimplifyPercents = { 80, 45, 20 };
    private static readonly float[] DefaultMergedSimplifyTargetErrors = { 0.01f, 0.01f, 0.01f };

    [MenuItem("Assets/mesh/(danger)OptimAndReplace")]
    private static void Editor_ConvMeshReplace()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateMeshForProcessing(mesh, path) &&
            EnsureReplaceSupported(path))
        {
            OptMeshFileReplace(mesh, path);
        }
    }

    [MenuItem("Assets/mesh/convert")]
    private static void Editor_ConvMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateMeshForProcessing(mesh, path))
        {
            OptMeshFile(mesh, path);
        }
    }

    [MenuItem("Assets/mesh/SimplifyMesh")]
    private static void Editor_SimpleMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateMeshForProcessing(mesh, path))
        {
            SimplifyMeshFile(mesh, path);
        }
    }

    [MenuItem("Assets/mesh/Shadow")]
    private static void Editor_MakeShadowMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateMeshForProcessing(mesh, path))
        {
            ShadowMeshFile(mesh, path);
        }
    }

    [MenuItem("Assets/mesh/MergeSimplifiedMesh")]
    private static void Editor_MergeSimplifiedMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateMeshForProcessing(mesh, path))
        {
            MergeSimplifiedMeshFile(mesh, path);
        }
    }

    private static void ShadowMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.MergeLOD();
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_lod"));
    }

    private static void MergeSimplifiedMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.MergeSimplified(DefaultMergedSimplifyPercents, null);
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_lod_804520"));
    }

    private static void SimplifyMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.Simplify(75);
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_075"));

        var newMesh2 = simpleMeshEditor.Simplify(50);
        AssetDatabase.CreateAsset(newMesh2, BuildGeneratedMeshPath(path, "_050"));

        var newMesh3 = simpleMeshEditor.Simplify(25);
        AssetDatabase.CreateAsset(newMesh3, BuildGeneratedMeshPath(path, "_025"));
    }

    private static void OptMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_fixed"));
    }

    private static void OptMeshFileReplace(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        CopyMesh(mesh, newMesh);
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static (T[] vertex, uint[] indices) OptMeshData<T>(Mesh mesh, T[] originList, uint sizeOfT) where T : struct
    {
        var vertices = new List<T>(originList);
        var indics = mesh.GetIndices(0).Select(t => (uint)t).ToArray();

        (var newVertex, var newIndics) = MeshOperations.Reindex(vertices.ToArray(), indics, sizeOfT);

        MeshOperations.OptimizeCache(newIndics, newVertex.Length);
        MeshOperations.OptimizeOverdraw(newIndics, newVertex, sizeOfT, 1.2f);
        MeshOperations.OptimizeVertexFetch(newIndics, newVertex, sizeOfT);
        return (newVertex, newIndics);
    }

    private static bool TryGetSelectedMeshAsset(out Mesh mesh, out string assetPath)
    {
        mesh = Selection.activeObject as Mesh;
        if (mesh != null)
        {
            assetPath = AssetDatabase.GetAssetPath(mesh);
            return true;
        }

        assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("Select a Mesh asset in the Project window.");
            return false;
        }

        var meshes = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToArray();
        if (meshes.Length == 1)
        {
            mesh = meshes[0];
            return true;
        }

        if (meshes.Length > 1)
        {
            Debug.LogError($"Asset '{assetPath}' contains multiple meshes. Select the specific Mesh sub-asset instead.");
            return false;
        }

        Debug.LogError("Select a Mesh asset in the Project window.");
        return false;
    }

    private static bool ValidateMeshForProcessing(Mesh mesh, string assetPath)
    {
        if (mesh == null || string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("Failed to resolve the selected mesh asset.");
            return false;
        }

        if (mesh.subMeshCount != 1)
        {
            Debug.LogError($"Only single-submesh meshes are supported. '{assetPath}' has {mesh.subMeshCount} submeshes.");
            return false;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError($"Mesh read/write must be enabled before running these tools. '{assetPath}' is not readable.");
            return false;
        }

        if (mesh.GetTopology(0) != MeshTopology.Triangles)
        {
            Debug.LogError($"Only triangle meshes are supported. '{assetPath}' uses {mesh.GetTopology(0)}.");
            return false;
        }

        if (mesh.blendShapeCount > 0)
        {
            Debug.LogError($"Meshes with blend shapes are not supported yet. '{assetPath}' has {mesh.blendShapeCount} blend shapes.");
            return false;
        }

        return true;
    }

    private static bool EnsureReplaceSupported(string assetPath)
    {
        if (!string.Equals(Path.GetExtension(assetPath), ".mesh", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"OptimAndReplace only supports native .mesh assets. Use convert to create a new mesh for '{assetPath}'.");
            return false;
        }

        return true;
    }

    private static string BuildGeneratedMeshPath(string sourcePath, string suffix)
    {
        var directory = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return AssetDatabase.GenerateUniqueAssetPath($"{directory}/{fileName}{suffix}.mesh");
    }

    private static void CopyMesh(Mesh destination, Mesh source)
    {
        destination.Clear();
        destination.name = source.name;
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;

        if (source.tangents.Length == source.vertexCount)
        {
            destination.tangents = source.tangents;
        }

        if (source.colors32.Length == source.vertexCount)
        {
            destination.colors32 = source.colors32;
        }

        for (var channel = 0; channel < 8; channel++)
        {
            var uvs = new List<Vector4>();
            source.GetUVs(channel, uvs);
            if (uvs.Count > 0)
            {
                destination.SetUVs(channel, uvs);
            }
        }

        if (source.bindposes.Length > 0)
        {
            destination.bindposes = source.bindposes;
        }

        var bonesPerVertex = source.GetBonesPerVertex();
        var boneWeights = source.GetAllBoneWeights();
        try
        {
            if (bonesPerVertex.Length == source.vertexCount && boneWeights.Length > 0)
            {
                destination.SetBoneWeights(bonesPerVertex, boneWeights);
            }
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

        destination.subMeshCount = source.subMeshCount;
        for (var subMeshIndex = 0; subMeshIndex < source.subMeshCount; subMeshIndex++)
        {
            destination.SetIndices(source.GetIndices(subMeshIndex), source.GetTopology(subMeshIndex), subMeshIndex, false);
        }

        if (source.normals.Length == source.vertexCount)
        {
            destination.normals = source.normals;
        }
        else
        {
            destination.RecalculateNormals();
        }

        destination.bounds = source.bounds;
    }
}