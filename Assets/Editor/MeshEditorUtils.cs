using MeshOptimizer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public interface IMeshOpt
{
    void Init(Mesh mesh);
    Mesh Simplify(int percent);
    Mesh Optimize();
}

public static partial class MeshEditorUtils
{
    [MenuItem("Assets/mesh/(danger)OptimAndReplace")]
    private static void Editor_ConvMeshReplace()
    {
        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            OptMeshFileReplace(AssetDatabase.GetAssetPath(mesh));
        }
    }

    [MenuItem("Assets/mesh/convert")]
    private static void Editor_ConvMesh()
    {

        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            OptMeshFile(AssetDatabase.GetAssetPath(mesh));
        }
    }

    [MenuItem("Assets/mesh/SimplifyMesh")]
    private static void Editor_SimpleMesh()
    {
        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            SimplifyMeshFile(AssetDatabase.GetAssetPath(mesh));
        }
    }

    private static void SimplifyMeshFile(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);

        var newMesh = simpleMeshEditor.Simplify(75);
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_075.mesh");

        var newMesh2 = simpleMeshEditor.Simplify(50);
        AssetDatabase.CreateAsset(newMesh2, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_050.mesh");

        var newMesh3 = simpleMeshEditor.Simplify(25);
        AssetDatabase.CreateAsset(newMesh3, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_025.mesh");
    }

    private static void OptMeshFile(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_fixed.mesh");
    }

    private static void OptMeshFileReplace(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);

        var simpleMeshEditor = new SimpleMeshOpt();
        simpleMeshEditor.Init(mesh);
        var newMesh = simpleMeshEditor.Optimize();
        mesh.Clear();
        mesh.SetVertices(newMesh.vertices.ToList());
        mesh.SetIndices(newMesh.GetIndices(0).Select(t=>(int)t).ToArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.Refresh();
    }


    public static (T[] vertex, uint[] indices) OptMeshData<T>(Mesh mesh, T[] originList ,uint sizeOfT) where T : struct
    {
        var vertices = new List<T>(originList);
        var indics = mesh.GetIndices(0).Select(t => (uint)t).ToArray();

        //remapVertexBuff
        //remapIndicsBuff
        (var newVertex, var newIndics) = MeshOperations.Reindex(vertices.ToArray(), indics, sizeOfT);

        MeshOperations.OptimizeCache(newIndics, newVertex.Length);
        MeshOperations.OptimizeOverdraw(newIndics, newVertex, sizeOfT, 1.2f);
        MeshOperations.OptimizeVertexFetch(newIndics, newVertex, sizeOfT);
        return (newVertex, newIndics);
    }
}
