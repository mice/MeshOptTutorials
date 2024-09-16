using MeshOptimizer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;


public struct SimpleSkinData : IEquatable<SimpleSkinData>, IEqualityComparer<SimpleSkinData>
{
    public Vector3 Position;
    public Vector2 UV;
    public BoneWeight Bone;
    public int VertexByte;

    public bool Equals(SimpleSkinData other)
    {
        return Position == other.Position
            && UV == other.UV
            && Bone == other.Bone
            && VertexByte == other.VertexByte;
    }

    public bool Equals(SimpleSkinData x, SimpleSkinData y)
    {
        return x.Position == y.Position
            && x.UV == y.UV
            && x.Bone == y.Bone
            && x.VertexByte == y.VertexByte;
    }

    public int GetHashCode(SimpleSkinData obj)
    {
        return (obj.Position, obj.UV, obj.Bone, obj.VertexByte).GetHashCode();
    }
}

public class SkinMeshOpt : IMeshOpt
{
    public Mesh mesh;
    private uint sizeOfElement;

    public void Init(Mesh mesh)
    {
        this.mesh = mesh;
        sizeOfElement = (uint)UnsafeUtility.SizeOf<SimpleSkinData>();
    }

    public Mesh Optimize()
    {
        var originVertex = SkinMeshVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), (uint)UnsafeUtility.SizeOf<SimpleSkinData>());


        List<BoneWeight1> originWeight = mesh.GetAllBoneWeights().ToList();
        var newBoneWeights = MakeBoneWeight(originVertex, newVertex, originWeight);
        var newMesh = ToMesh(this.mesh, newVertex, newIndics, newBoneWeights);
        return newMesh;
    }

    public Mesh Simplify(int percent)
    {
        var originVertex = SkinMeshVertex(mesh);
        (var newVertex, var newIndics) = MeshEditorUtils.OptMeshData(mesh, originVertex.ToArray(), (uint)UnsafeUtility.SizeOf<SimpleSkinData>());

        var newSimpleIndics = MeshOperations.Simplify(newIndics, newVertex, sizeOfElement, (uint)(newIndics.Length * percent / 100.0f), 0.01f, 0, out var error);
        List<BoneWeight1> originWeight = mesh.GetAllBoneWeights().ToList();
        var newBoneWeights = MakeBoneWeight(originVertex, newVertex, originWeight);

        var newMesh = ToMesh(this.mesh, newVertex, newSimpleIndics, newBoneWeights);
        return newMesh;
    }

    private static Mesh ToMesh(Mesh originMesh,SimpleSkinData[] newVertex, uint[] newIndics,List<BoneWeight1> newBoneWeights)
    {
        var newMesh = new Mesh();
        newMesh.SetVertices(newVertex.Select(t => t.Position).ToArray());
        newMesh.SetUVs(0, newVertex.Select(t => t.UV).ToArray());
        var tmpVet = new NativeArray<byte>(newVertex.Length, Allocator.Temp);
        tmpVet.CopyFrom(newVertex.Select(t => (byte)t.VertexByte).ToArray());

        var tmpVet2 = new NativeArray<BoneWeight1>(newBoneWeights.Count, Allocator.Temp);
        tmpVet2.CopyFrom(newBoneWeights.ToArray());
        newMesh.SetBoneWeights(tmpVet, tmpVet2);
        newMesh.boneWeights = newVertex.Select(t => t.Bone).ToArray();

        newMesh.bindposes = originMesh.bindposes;
        newMesh.SetIndices(newIndics.Select(t => (int)t).ToArray(), MeshTopology.Triangles, 0);
        newMesh.RecalculateNormals();
        tmpVet.Dispose();
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
            tmpArray[i] = originVertex.IndexOf(newVertex[i]);
            if (tmpArray[i] != -1)
            {
                var startIndex = indexStartList[i];
                var totalWeight = 0.0f;
                for (int j = 0; j < originVertex[i].VertexByte; j++)
                {
                    newBoneWeights.Add(originWeight[startIndex + j]);
                    totalWeight += originWeight[startIndex + j].weight;
                }
                Debug.Assert(Mathf.Approximately(1f, totalWeight));
            }
        }

        return newBoneWeights;
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
