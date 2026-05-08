using MeshOptimizer;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public struct SimpleMeshData : IEquatable<SimpleMeshData>
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Color32 Color;
    public Vector2 UV;

    public bool Equals(SimpleMeshData other)
    {
        return Position == other.Position
            && Normal == other.Normal
            && Tangent == other.Tangent
            && Color.Equals(other.Color)
            && UV == other.UV;
    }

    public override bool Equals(object obj)
    {
        return obj is SimpleMeshData other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Position, Normal, Tangent, Color, UV).GetHashCode();
    }
}

public class SimpleMeshOpt : IMeshOpt
{
    public Mesh mesh;
    private uint sizeOfElement;
    private bool hasNormals;
    private bool hasTangents;
    private bool hasColors;
    private bool hasUv0;

    public void Init(Mesh mesh)
    {
        this.mesh = mesh;
        sizeOfElement = (uint)UnsafeUtility.SizeOf<SimpleMeshData>();
        hasNormals = mesh.normals.Length == mesh.vertexCount;
        hasTangents = mesh.tangents.Length == mesh.vertexCount;
        hasColors = mesh.colors32.Length == mesh.vertexCount;
        hasUv0 = mesh.uv.Length == mesh.vertexCount;
    }

    public Mesh Optimize()
    {
        var originVertex = MeshVertexData(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), sizeOfElement);
        return ToMesh(newVertex, newIndics);
    }

    public Mesh Simplify(int percent)
    {
        var originVertex = MeshVertexData(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), sizeOfElement);
        var newSimpleIndics = MeshOperations.Simplify(newIndics, newVertex, sizeOfElement, (uint)(newIndics.Length * percent / 100.0f), 0.01f, 0, out var error);
        return ToMesh(newVertex, newSimpleIndics);
    }


    public Mesh MergeLOD()
    {
        var originVertex = MeshVertexData(mesh);
        var indics = mesh.GetIndices(0).Select(t => (uint)t).ToArray();
        const int percent = 25;
        var newSimpleIndics = MeshOperations.Simplify(indics, originVertex.ToArray(), sizeOfElement, (uint)(indics.Length * percent / 100.0f), 0.01f, 0, out var error);
        return ToLodMesh(originVertex, indics, newSimpleIndics);
    }

    private Mesh ToLodMesh(IReadOnlyList<SimpleMeshData> vertices, uint[] originIndics, uint[] newSimpleIndics)
    {
        var newMesh = new Mesh
        {
            name = mesh.name,
            indexFormat = mesh.indexFormat
        };
        ApplyVertexData(newMesh, vertices);
        newMesh.subMeshCount = 2;
        newMesh.SetIndices(originIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles,0);
        newMesh.SetIndices(newSimpleIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 1);
        if (!hasNormals)
        {
            newMesh.RecalculateNormals();
        }
        newMesh.bounds = mesh.bounds;
        return newMesh;
    }

    private Mesh ToMesh(SimpleMeshData[] vertices, uint[] indices)
    {
        var newMesh = new Mesh
        {
            name = mesh.name,
            indexFormat = mesh.indexFormat
        };
        ApplyVertexData(newMesh, vertices);
        newMesh.SetIndices(indices.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        if (!hasNormals)
        {
            newMesh.RecalculateNormals();
        }
        newMesh.bounds = mesh.bounds;
        return newMesh;
    }

    private void ApplyVertexData(Mesh newMesh, IReadOnlyList<SimpleMeshData> vertices)
    {
        newMesh.SetVertices(vertices.Select(t => t.Position).ToList());
        if (hasNormals)
        {
            newMesh.SetNormals(vertices.Select(t => t.Normal).ToList());
        }

        if (hasTangents)
        {
            newMesh.SetTangents(vertices.Select(t => t.Tangent).ToList());
        }

        if (hasColors)
        {
            newMesh.SetColors(vertices.Select(t => t.Color).ToList());
        }

        if (hasUv0)
        {
            newMesh.SetUVs(0, vertices.Select(t => t.UV).ToList());
        }
    }

    private static List<SimpleMeshData> MeshVertexData(Mesh mesh)
    {
        var vertexCount = mesh.vertexCount;
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var tangents = mesh.tangents;
        var colors = mesh.colors32;
        var uvs = mesh.uv;
        var output = new List<SimpleMeshData>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            output.Add(new SimpleMeshData
            {
                Position = vertices[i],
                Normal = normals.Length == vertexCount ? normals[i] : default,
                Tangent = tangents.Length == vertexCount ? tangents[i] : default,
                Color = colors.Length == vertexCount ? colors[i] : new Color32(255, 255, 255, 255),
                UV = uvs.Length == vertexCount ? uvs[i] : default
            });
        }

        return output;
    }
}
