using MeshOptimizer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

public  static partial class MeshEditorUtils
{
    [MenuItem("Assets/skin/(danger)OptimAndReplace")]
    private static void Editor_ConvSkinMeshReplace()
    {
        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            OptSkinMeshFileReplace(AssetDatabase.GetAssetPath(mesh));
        }
    }

    [MenuItem("Assets/skin/convert")]
    private static void Editor_ConvSkinMesh()
    {

        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            OptSkinMeshFile(AssetDatabase.GetAssetPath(mesh));
        }
    }


    [MenuItem("Assets/skin/SimplifyMesh")]
    private static void Editor_SimpleSkinMesh()
    {
        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            SimplifySkinMeshFile(AssetDatabase.GetAssetPath(mesh));
        }
    }

    private static void OptSkinMeshFile(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_skin_fixed.mesh");
    }

    private static void OptSkinMeshFileReplace(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();

        mesh.Clear();
        mesh.SetVertices(newMesh.vertices.ToList());
        mesh.SetIndices(newMesh.GetIndices(0).Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.Refresh();
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_skin_fixed.mesh");
    }

    

    private static void SimplifySkinMeshFile(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        var simpleMeshEditor = new SkinMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.Simplify(75);
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_075.mesh");

        var newMesh2 = simpleMeshEditor.Simplify(50);
        AssetDatabase.CreateAsset(newMesh2, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_050.mesh");

        var newMesh3 = simpleMeshEditor.Simplify(25);
        AssetDatabase.CreateAsset(newMesh3, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_025.mesh");
    }
   

    public static List<SimpleSkinData> SkinMeshVertex(Mesh mesh)
    {
        var vert = mesh.vertices;
        var uvs = mesh.uv;
        var bytes = mesh.GetBonesPerVertex();
        var bone = mesh.boneWeights;
        UnityEngine.Debug.Assert(vert != null);
        UnityEngine.Debug.Assert(vert.Length == uvs.Length);
        UnityEngine.Debug.Assert(vert.Length == bytes.Length);
        UnityEngine.Debug.Assert(vert.Length == bone.Length);

        var output = new List<SimpleSkinData>();
        for (int i = 0; i < vert.Length; i++)
        {
            output.Add(new SimpleSkinData()
            {
                Position = vert[i],
                UV = uvs[i],
                VertexByte = bytes[i],
                Bone = bone[i]
            });
        }
        return output;
    }
}
