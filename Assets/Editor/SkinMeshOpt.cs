using MeshOptimizer;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public struct SimpleSkinData : IEquatable<SimpleSkinData>
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Tangent;
    public Color32 Color;
    public Vector2 UV;
    public BoneWeight Bone;
    public int VertexByte;

    public bool Equals(SimpleSkinData other)
    {
        return Position == other.Position
            && Normal == other.Normal
            && Tangent == other.Tangent
            && Color.Equals(other.Color)
            && UV == other.UV
            && Bone == other.Bone
            && VertexByte == other.VertexByte;
    }

    public override bool Equals(object obj)
    {
        return obj is SimpleSkinData other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Position, Normal, Tangent, Color, UV, Bone, VertexByte).GetHashCode();
    }
}

public class SkinMeshOpt : IMeshOpt
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
        sizeOfElement = (uint)UnsafeUtility.SizeOf<SimpleSkinData>();
        hasNormals = mesh.normals.Length == mesh.vertexCount;
        hasTangents = mesh.tangents.Length == mesh.vertexCount;
        hasColors = mesh.colors32.Length == mesh.vertexCount;
        hasUv0 = mesh.uv.Length == mesh.vertexCount;
    }

    public Mesh Optimize()
    {
        var originVertex = SkinMeshVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), (uint)UnsafeUtility.SizeOf<SimpleSkinData>());
        var originWeight = GetBoneWeights(mesh);
        var newBoneWeights = MakeBoneWeight(originVertex, newVertex, originWeight);
        var newMesh = ToMesh(newVertex, newIndics, newBoneWeights);
        return newMesh;
    }

    public Mesh Simplify(int percent,float target_error = 0.01f)
    {
        var originVertex = SkinMeshVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), (uint)UnsafeUtility.SizeOf<SimpleSkinData>());

        var newSimpleIndics = MeshOperations.Simplify(newIndics, newVertex, sizeOfElement, (uint)(newIndics.Length * percent / 100.0f), target_error, 0, out var error);
        var originWeight = GetBoneWeights(mesh);
        var newBoneWeights = MakeBoneWeight(originVertex, newVertex, originWeight);

        var newMesh = ToMesh(newVertex, newSimpleIndics, newBoneWeights);
        return newMesh;
    }

    public Mesh MergeSimplified(int[] percents, float[] target_errors)
    {
        var normalizedPercents = NormalizeSimplifyPercents(percents);
        var originVertex = SkinMeshVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), (uint)UnsafeUtility.SizeOf<SimpleSkinData>());

        var originWeight = GetBoneWeights(mesh);
        var newBoneWeights = MakeBoneWeight(originVertex, newVertex, originWeight);
        var mergedIndices = BuildSimplifiedIndexBuffers(newVertex, newIndics, normalizedPercents, target_errors);

        return ToMesh(newVertex, mergedIndices, newBoneWeights);
    }

    public Mesh MergeLOD()
    {
        throw new NotSupportedException("LOD merge is only implemented for static meshes.");
    }

    private Mesh ToMesh(SimpleSkinData[] newVertex, uint[] newIndics, List<BoneWeight1> newBoneWeights)
    {
        var newMesh = new Mesh
        {
            name = mesh.name,
            indexFormat = mesh.indexFormat
        };
        newMesh.SetVertices(newVertex.Select(t => t.Position).ToArray());
        if (hasNormals)
        {
            newMesh.SetNormals(newVertex.Select(t => t.Normal).ToList());
        }

        if (hasTangents)
        {
            newMesh.SetTangents(newVertex.Select(t => t.Tangent).ToList());
        }

        if (hasColors)
        {
            newMesh.SetColors(newVertex.Select(t => t.Color).ToList());
        }

        if (hasUv0)
        {
            newMesh.SetUVs(0, newVertex.Select(t => t.UV).ToArray());
        }

        var tmpVet = new NativeArray<byte>(newVertex.Length, Allocator.Temp);
        tmpVet.CopyFrom(newVertex.Select(t => (byte)t.VertexByte).ToArray());

        var tmpVet2 = new NativeArray<BoneWeight1>(newBoneWeights.Count, Allocator.Temp);
        tmpVet2.CopyFrom(newBoneWeights.ToArray());
        newMesh.SetBoneWeights(tmpVet, tmpVet2);

        newMesh.bindposes = mesh.bindposes;
        newMesh.SetIndices(newIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        if (!hasNormals)
        {
            newMesh.RecalculateNormals();
        }
        newMesh.bounds = mesh.bounds;
        tmpVet.Dispose();
        tmpVet2.Dispose();
        return newMesh;
    }

    private Mesh ToMesh(SimpleSkinData[] newVertex, IReadOnlyList<uint[]> subMeshIndices, List<BoneWeight1> newBoneWeights)
    {
        var newMesh = new Mesh
        {
            name = mesh.name,
            indexFormat = mesh.indexFormat
        };
        newMesh.SetVertices(newVertex.Select(t => t.Position).ToArray());
        if (hasNormals)
        {
            newMesh.SetNormals(newVertex.Select(t => t.Normal).ToList());
        }

        if (hasTangents)
        {
            newMesh.SetTangents(newVertex.Select(t => t.Tangent).ToList());
        }

        if (hasColors)
        {
            newMesh.SetColors(newVertex.Select(t => t.Color).ToList());
        }

        if (hasUv0)
        {
            newMesh.SetUVs(0, newVertex.Select(t => t.UV).ToArray());
        }

        var tmpVet = new NativeArray<byte>(newVertex.Length, Allocator.Temp);
        tmpVet.CopyFrom(newVertex.Select(t => (byte)t.VertexByte).ToArray());

        var tmpVet2 = new NativeArray<BoneWeight1>(newBoneWeights.Count, Allocator.Temp);
        tmpVet2.CopyFrom(newBoneWeights.ToArray());
        newMesh.SetBoneWeights(tmpVet, tmpVet2);

        newMesh.bindposes = mesh.bindposes;
        newMesh.subMeshCount = subMeshIndices.Count;
        for (var subMeshIndex = 0; subMeshIndex < subMeshIndices.Count; subMeshIndex++)
        {
            newMesh.SetIndices(subMeshIndices[subMeshIndex].Select(t => (int)t).ToArray(), MeshTopology.Triangles, subMeshIndex);
        }
        if (!hasNormals)
        {
            newMesh.RecalculateNormals();
        }
        newMesh.bounds = mesh.bounds;
        tmpVet.Dispose();
        tmpVet2.Dispose();
        return newMesh;
    }

    private static List<BoneWeight1> MakeBoneWeight(List<SimpleSkinData> originVertex, SimpleSkinData[] newVertex, List<BoneWeight1> originWeight)
    {
        var vertexSet = new HashSet<SimpleSkinData>(originVertex);
        if (vertexSet.Count != originVertex.Count)
        {
            UnityEngine.Debug.Log($"Error:originVertex Count:{originVertex.Count}=>{vertexSet.Count}");
        }

        var indexStartList = new int[originVertex.Count];
        for (int i = 0, j = 0; i < originVertex.Count; i++)
        {
            indexStartList[i] = j;
            var vertex = originVertex[i];
            if (vertex.VertexByte > 0)
            {
                j += vertex.VertexByte;
            }
        }
        var newBoneWeights = new List<BoneWeight1>();
        var tmpArray = new int[newVertex.Length];
        for (int i = 0; i < newVertex.Length; i++)
        {
            var tIndex = originVertex.IndexOf(newVertex[i]);
            tmpArray[i] = tIndex;
            if (tmpArray[i] != -1)
            {
                var startIndex = indexStartList[tIndex];
                var totalWeight = 0.0f;
                for (int j = 0; j < newVertex[i].VertexByte; j++)
                {
                    newBoneWeights.Add(originWeight[startIndex + j]);
                    totalWeight += originWeight[startIndex + j].weight;
                }
                Debug.Assert(Mathf.Approximately(1f, totalWeight));
            }
        }

        return newBoneWeights;
    }

    private static List<BoneWeight1> GetBoneWeights(Mesh mesh)
    {
        var boneWeights = mesh.GetAllBoneWeights();
        try
        {
            return boneWeights.ToList();
        }
        finally
        {
            if (boneWeights.IsCreated)
            {
                boneWeights.Dispose();
            }
        }
    }

    public static List<SimpleSkinData> SkinMeshVertex(Mesh mesh)
    {
        var vert = mesh.vertices;
        var normals = mesh.normals;
        var tangents = mesh.tangents;
        var colors = mesh.colors32;
        var uvs = mesh.uv;
        var bytes = mesh.GetBonesPerVertex();
        var bone = mesh.boneWeights;
        try
        {
            UnityEngine.Debug.Assert(vert != null);
            UnityEngine.Debug.Assert(vert.Length == bytes.Length);
            UnityEngine.Debug.Assert(vert.Length == bone.Length);

            var output = new List<SimpleSkinData>(vert.Length);
            for (int i = 0; i < vert.Length; i++)
            {
                output.Add(new SimpleSkinData()
                {
                    Position = vert[i],
                    Normal = normals.Length == vert.Length ? normals[i] : default,
                    Tangent = tangents.Length == vert.Length ? tangents[i] : default,
                    Color = colors.Length == vert.Length ? colors[i] : new Color32(255, 255, 255, 255),
                    UV = uvs.Length == vert.Length ? uvs[i] : default,
                    VertexByte = bytes[i],
                    Bone = bone[i]
                });
            }

            return output;
        }
        finally
        {
            if (bytes.IsCreated)
            {
                bytes.Dispose();
            }
        }
    }

    private uint[][] BuildSimplifiedIndexBuffers(SimpleSkinData[] vertices, uint[] sourceIndices, IReadOnlyList<int> percents,IReadOnlyList<float> target_errors)
    {
        var mergedIndices = new uint[percents.Count][];
        for (var index = 0; index < percents.Count; index++)
        {
            var percent = percents[index];
            float error_target = target_errors!=null && target_errors.Count > index ? target_errors[index] : 0.01f;
            mergedIndices[index] = MeshOperations.Simplify(sourceIndices, vertices, sizeOfElement, (uint)(sourceIndices.Length * percent / 100.0f), error_target, 0, out var error);
        }

        return mergedIndices;
    }

    private static int[] NormalizeSimplifyPercents(int[] percents)
    {
        if (percents == null || percents.Length == 0)
        {
            throw new ArgumentException("At least one simplify percent must be provided.", nameof(percents));
        }

        var normalizedPercents = new int[percents.Length];
        for (var index = 0; index < percents.Length; index++)
        {
            var percent = percents[index];
            if (percent <= 0 || percent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(percents), $"Simplify percent must be between 1 and 100. Received {percent}.");
            }

            normalizedPercents[index] = percent;
        }

        return normalizedPercents;
    }
}