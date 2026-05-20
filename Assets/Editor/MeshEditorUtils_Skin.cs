using UnityEditor;
using UnityEngine;

public static partial class MeshEditorUtils
{
    [MenuItem("Assets/skin/(danger)OptimAndReplace")]
    private static void Editor_ConvSkinMeshReplace()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateSkinnedMeshForProcessing(mesh, path) &&
            EnsureReplaceSupported(path))
        {
            OptSkinMeshFileReplace(mesh, path);
        }
    }

    [MenuItem("Assets/skin/convert")]
    private static void Editor_ConvSkinMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateSkinnedMeshForProcessing(mesh, path))
        {
            OptSkinMeshFile(mesh, path);
        }
    }

    [MenuItem("Assets/skin/SimplifyMesh")]
    private static void Editor_SimpleSkinMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateSkinnedMeshForProcessing(mesh, path))
        {
            SimplifySkinMeshFile(mesh, path);
        }
    }

    [MenuItem("Assets/skin/MergeSimplifiedMesh")]
    private static void Editor_MergeSimplifiedSkinMesh()
    {
        if (TryGetSelectedMeshAsset(out var mesh, out var path) &&
            ValidateSkinnedMeshForProcessing(mesh, path))
        {
            MergeSimplifiedSkinMeshFile(mesh, path);
        }
    }

    private static void OptSkinMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_skin_fixed"));
    }

    private static void OptSkinMeshFileReplace(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();

        CopyMesh(mesh, newMesh);
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void SimplifySkinMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.Simplify(75);
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_075"));

        var newMesh2 = simpleMeshEditor.Simplify(50);
        AssetDatabase.CreateAsset(newMesh2, BuildGeneratedMeshPath(path, "_050"));

        var newMesh3 = simpleMeshEditor.Simplify(25);
        AssetDatabase.CreateAsset(newMesh3, BuildGeneratedMeshPath(path, "_025"));
    }

    private static void MergeSimplifiedSkinMeshFile(Mesh mesh, string path)
    {
        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.MergeSimplified(DefaultMergedSimplifyPercents, null);
        AssetDatabase.CreateAsset(newMesh, BuildGeneratedMeshPath(path, "_skin_lod_804520"));
    }

    private static bool ValidateSkinnedMeshForProcessing(Mesh mesh, string assetPath)
    {
        if (!ValidateMeshForProcessing(mesh, assetPath))
        {
            return false;
        }

        if (mesh.bindposes == null || mesh.bindposes.Length == 0)
        {
            Debug.LogError($"Skin tools require a skinned mesh with bindposes. '{assetPath}' has no bindposes.");
            return false;
        }

        var bonesPerVertex = mesh.GetBonesPerVertex();
        try
        {
            if (bonesPerVertex.Length != mesh.vertexCount)
            {
                Debug.LogError($"Skin tools require valid bone weights for every vertex. '{assetPath}' returned {bonesPerVertex.Length} entries for {mesh.vertexCount} vertices.");
                return false;
            }

            for (var i = 0; i < bonesPerVertex.Length; i++)
            {
                if (bonesPerVertex[i] > 4)
                {
                    Debug.LogError($"Skin tools currently support at most 4 bone influences per vertex. '{assetPath}' contains vertices with more than 4 influences.");
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

        if (mesh.boneWeights.Length != mesh.vertexCount)
        {
            Debug.LogError($"Skin tools require legacy bone weights for every vertex. '{assetPath}' returned {mesh.boneWeights.Length} legacy weights for {mesh.vertexCount} vertices.");
            return false;
        }

        return true;
    }
}