using MeshOptimizer;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MeshEditorUtils
{

    [MenuItem("Assets/convert")]
    private static void Editor_ConvMesh()
    {

        var mesh = Selection.gameObjects.Where(t => (AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GetAssetPath(t)) != null)).FirstOrDefault();
        if (mesh != null)
        {
            OptMeshFile(AssetDatabase.GetAssetPath(mesh));
        }
    }

    [MenuItem("Assets/SimplifyMesh")]
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
        (var newVertex, var newIndics) = OptMeshData(mesh);
        var newMesh = SimplifyMesh(newVertex,newIndics, 75);
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_075.mesh");

        var newMesh2 = SimplifyMesh(newVertex, newIndics, 50);
        AssetDatabase.CreateAsset(newMesh2, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_050.mesh");

        var newMesh3 = SimplifyMesh(newVertex, newIndics, 25);
        AssetDatabase.CreateAsset(newMesh3, System.IO.Path.GetDirectoryName(path) + $"\\{fileName}_025.mesh");
    }

    private static Mesh SimplifyMesh(Vector3[] vertex, uint[] indices, int percent)
    {
        var newSimpleIndics = MeshOperations.Simplify(indices, vertex, sizeof(float) * 3, (uint)(indices.Length * percent / 100.0f), 0.01f, 0, out var error);
        var newMesh = new Mesh();

        newMesh.SetVertices(vertex);
        newMesh.SetIndices(newSimpleIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }

    private static Mesh SimplifyMesh(Mesh mesh, int percent)
    {
        (var newVertex, var newIndics) = OptMeshData(mesh);


        var newSimpleIndics = MeshOperations.Simplify(newIndics, newVertex, sizeof(float) * 3, (uint)(newIndics.Length * percent / 100.0f), 0.01f, 0, out var error);
        var newMesh = new Mesh();

        newMesh.SetVertices(newVertex);
        newMesh.SetIndices(newSimpleIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }

    private static void OptMeshFile(string path)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        var newMesh = OptMesh2(mesh);
        AssetDatabase.CreateAsset(newMesh, System.IO.Path.GetDirectoryName(path) + $"\\ {fileName} _fixed.mesh");
    }

    public static Mesh OptMesh2(Mesh mesh)
    {
        (var newVertex, var newIndics) = OptMeshData(mesh);

        var newMesh = new Mesh();
        newMesh.SetVertices(newVertex);
        newMesh.SetIndices(newIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }

    public static (Vector3[] vertex, uint[] indices) OptMeshData(Mesh mesh)
    {
        var vertices = new List<Vector3>();
        mesh.GetVertices(vertices);
        var indics = mesh.GetIndices(0).Select(t => (uint)t).ToArray();

        //remapVertexBuff
        //remapIndicsBuff
        (var newVertex, var newIndics) = MeshOperations.Reindex(vertices.ToArray(), indics, (uint)(sizeof(float) * 3));

        MeshOperations.OptimizeCache(newIndics, newVertex.Length);
        MeshOperations.OptimizeOverdraw(newIndics, newVertex, sizeof(float) * 3, 1.2f);
        MeshOperations.OptimizeVertexFetch(newIndics, newVertex, sizeof(float) * 3);
        return (newVertex, newIndics);
    }

    public static Mesh OptMesh(Mesh mesh)
    {
        var vetex = new List<Vector3>();
        mesh.GetVertices(vetex);
        var indics = mesh.GetIndices(0).Select(t => (uint)t).ToArray();

        var result = MeshOperations.Optimize<Vector3>(vetex.ToArray(), indics, (uint)(sizeof(float) * 3));

        var newMesh = new Mesh();
        newMesh.SetVertices(result.Item1);
        newMesh.SetIndices(result.Item2.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }
}
