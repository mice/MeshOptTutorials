using MeshOptimizer;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class SimpleMeshOpt : IMeshOpt
{
    public Mesh mesh;
    private uint sizeOfElement;
    public void Init(Mesh mesh)
    {
        this.mesh = mesh;
        sizeOfElement = (uint)UnsafeUtility.SizeOf<Vector3>();
    }

    public Mesh Optimize()
    {
        var originVertex = MeshPosVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), sizeOfElement);

        var newMesh = new Mesh();
        newMesh.SetVertices(newVertex);
        newMesh.SetIndices(newIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }

    public Mesh Simplify(int percent)
    {
        var originVertex = MeshPosVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), sizeOfElement);
        var newSimpleIndics = MeshOperations.Simplify(newIndics, newVertex, sizeOfElement, (uint)(newIndics.Length * percent / 100.0f), 0.01f, 0, out var error);
        var newMesh = new Mesh();

        newMesh.SetVertices(newVertex);
        newMesh.SetIndices(newSimpleIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        return newMesh;
    }

    private static List<Vector3> MeshPosVertex(Mesh mesh)
    {
        var vertices = mesh.vertices;
        return new List<Vector3>(vertices);
    }
}