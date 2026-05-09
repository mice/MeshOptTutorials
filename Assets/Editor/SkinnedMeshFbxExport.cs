commit 82172fd3156409918c62f7992e4491b0e33e80ed
Author: lu.shenglin <mice_003@163.com>
Date:   Fri May 8 12:43:30 2026 +0800

    测试导出fbx

diff --git a/Assets/Editor/SkinnedMeshFbxExport.cs b/Assets/Editor/SkinnedMeshFbxExport.cs
new file mode 100644
index 0000000..c377e4f
--- /dev/null
+++ b/Assets/Editor/SkinnedMeshFbxExport.cs
@@ -0,0 +1,686 @@
+using Autodesk.Fbx;
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Linq;
+using Unity.Collections;
+using UnityEditor;
+using UnityEngine;
+
+public static partial class MeshEditorUtils
+{
+    private const string TrimFbxMenuPath = "Assets/skin/SimplifyToFBX";
+    private const double CentimetersPerUnityUnit = 100.0;
+
+    [MenuItem(TrimFbxMenuPath)]
+    private static void Editor_SimplifySkinMeshToFbx()
+    {
+        if (!TryGetSelectedMeshAsset(out var mesh, out var assetPath))
+        {
+            return;
+        }
+
+        if (!ValidateSkinnedMeshForProcessing(mesh, assetPath))
+        {
+            return;
+        }
+
+        if (!TryGetSourceSkinnedMeshRenderer(mesh, assetPath, out var renderer))
+        {
+            Debug.LogError($"FBX export requires a SkinnedMeshRenderer for '{assetPath}'.");
+            return;
+        }
+
+        var config = SkinnedMeshSimplificationConfig.Load();
+        var entry = config.FindEntry(assetPath);
+        var reductionPercent = entry?.reductionPercent ?? 50;
+        var outputAssetPath = ResolveTrimFbxOutputPath(assetPath, entry?.outputPath);
+        var sourceFbxPath = ResolveSourceFbxPath(assetPath, entry?.externalSourcePath);
+
+        var simpleMeshEditor = new SkinMeshOpt();
+        simpleMeshEditor.Init(mesh);
+        var simplifiedMesh = simpleMeshEditor.Simplify(reductionPercent);
+
+        if (!TryExportTrimFbx(renderer, simplifiedMesh, sourceFbxPath, outputAssetPath))
+        {
+            return;
+        }
+
+        AssetDatabase.Refresh();
+        Debug.Log($"Exported trimmed FBX: '{outputAssetPath}'");
+    }
+
+    private static bool TryGetSourceSkinnedMeshRenderer(Mesh mesh, string assetPath, out SkinnedMeshRenderer renderer)
+    {
+        renderer = null;
+
+        var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
+        if (sourceRoot == null)
+        {
+            return false;
+        }
+
+        var renderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
+        if (renderers == null || renderers.Length == 0)
+        {
+            return false;
+        }
+
+        foreach (var candidate in renderers)
+        {
+            if (candidate != null && candidate.sharedMesh == mesh)
+            {
+                renderer = candidate;
+                return true;
+            }
+        }
+
+        if (renderers.Length == 1)
+        {
+            renderer = renderers[0];
+            return renderer != null;
+        }
+
+        return false;
+    }
+
+    private static string ResolveTrimFbxOutputPath(string assetPath, string configuredOutputPath)
+    {
+        if (!string.IsNullOrWhiteSpace(configuredOutputPath))
+        {
+            return configuredOutputPath.Replace('\\', '/');
+        }
+
+        var directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
+        var fileName = Path.GetFileNameWithoutExtension(assetPath);
+        return $"{directory}/{fileName}_trim.fbx";
+    }
+
+    private static string ResolveSourceFbxPath(string assetPath, string configuredExternalSourcePath)
+    {
+        if (!string.IsNullOrWhiteSpace(configuredExternalSourcePath))
+        {
+            return configuredExternalSourcePath.Replace('\\', '/');
+        }
+
+        return assetPath.Replace('\\', '/');
+    }
+
+    private static bool TryExportTrimFbx(SkinnedMeshRenderer sourceRenderer, Mesh simplifiedMesh, string sourceFbxPath, string outputAssetPath)
+    {
+        var outputFilePath = AssetPathToAbsolutePath(outputAssetPath);
+        var outputDirectory = Path.GetDirectoryName(outputFilePath);
+        if (!string.IsNullOrEmpty(outputDirectory))
+        {
+            Directory.CreateDirectory(outputDirectory);
+        }
+
+        using (var manager = FbxManager.Create())
+        {
+            if (manager == null)
+            {
+                Debug.LogError("Failed to create FBX manager.");
+                return false;
+            }
+
+            var ioSettings = FbxIOSettings.Create(manager, Globals.IOSROOT);
+            manager.SetIOSettings(ioSettings);
+
+            FbxExportUnitSettings unitSettings;
+            try
+            {
+                unitSettings = LoadSourceFbxUnitSettings(manager, ioSettings, sourceFbxPath);
+            }
+            catch (Exception ex)
+            {
+                Debug.LogError($"Failed to read source FBX unit from '{sourceFbxPath}': {ex.Message}");
+                return false;
+            }
+
+            using (unitSettings)
+            using (var scene = FbxScene.Create(manager, Path.GetFileNameWithoutExtension(outputAssetPath)))
+            {
+                if (scene == null)
+                {
+                    Debug.LogError("Failed to create FBX scene.");
+                    return false;
+                }
+
+                scene.GetGlobalSettings().SetAxisSystem(FbxAxisSystem.OpenGL);
+                scene.GetGlobalSettings().SetSystemUnit(unitSettings.SystemUnit);
+
+                var skeletonNodes = BuildSkeletonNodes(scene, sourceRenderer, unitSettings);
+                var meshNode = CreateMeshNode(scene, sourceRenderer, simplifiedMesh, skeletonNodes, unitSettings);
+                if (meshNode == null)
+                {
+                    return false;
+                }
+
+                ApplyMaterials(scene, meshNode, sourceRenderer.sharedMaterials);
+                ApplySkin(scene, meshNode, simplifiedMesh, sourceRenderer, skeletonNodes);
+                AddBindPose(scene, meshNode, skeletonNodes);
+
+                using (var exporter = FbxExporter.Create(manager, "TrimFbxExporter"))
+                {
+                    if (exporter == null)
+                    {
+                        Debug.LogError("Failed to create FBX exporter.");
+                        return false;
+                    }
+
+                    var writerId = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX binary (*.fbx)");
+                    if (writerId < 0)
+                    {
+                        writerId = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX ascii (*.fbx)");
+                    }
+
+                    if (!exporter.Initialize(outputFilePath, writerId, ioSettings))
+                    {
+                        Debug.LogError($"Failed to initialize FBX exporter for '{outputFilePath}': {exporter.GetStatus().GetErrorString()}");
+                        return false;
+                    }
+
+                    if (!exporter.Export(scene))
+                    {
+                        Debug.LogError($"Failed to export FBX '{outputFilePath}': {exporter.GetStatus().GetErrorString()}");
+                        return false;
+                    }
+                }
+            }
+        }
+
+        return true;
+    }
+
+    private static FbxExportUnitSettings LoadSourceFbxUnitSettings(FbxManager manager, FbxIOSettings ioSettings, string sourceFbxPath)
+    {
+        var sourceFilePath = AssetPathToAbsolutePath(sourceFbxPath);
+        if (!File.Exists(sourceFilePath))
+        {
+            throw new FileNotFoundException($"Source FBX file does not exist: {sourceFilePath}", sourceFilePath);
+        }
+
+        using (var sourceScene = FbxScene.Create(manager, "SourceUnitScene"))
+        using (var importer = FbxImporter.Create(manager, "SourceUnitImporter"))
+        {
+            if (sourceScene == null)
+            {
+                throw new InvalidOperationException("Failed to create source FBX scene for unit detection.");
+            }
+
+            if (importer == null)
+            {
+                throw new InvalidOperationException("Failed to create source FBX importer for unit detection.");
+            }
+
+            if (!importer.Initialize(sourceFilePath, -1, ioSettings))
+            {
+                throw new InvalidOperationException($"Failed to initialize FBX importer for '{sourceFilePath}': {importer.GetStatus().GetErrorString()}");
+            }
+
+            if (!importer.Import(sourceScene))
+            {
+                throw new InvalidOperationException($"Failed to import source FBX '{sourceFilePath}' for unit detection: {importer.GetStatus().GetErrorString()}");
+            }
+
+            var sourceUnit = sourceScene.GetGlobalSettings().GetSystemUnit();
+            try
+            {
+                return FbxExportUnitSettings.FromSource(sourceUnit, sourceFilePath);
+            }
+            finally
+            {
+                sourceUnit?.Dispose();
+            }
+        }
+    }
+
+    private static Dictionary<Transform, FbxNode> BuildSkeletonNodes(FbxScene scene, SkinnedMeshRenderer renderer, FbxExportUnitSettings unitSettings)
+    {
+        var nodes = new Dictionary<Transform, FbxNode>();
+        var boneSet = new HashSet<Transform>(renderer.bones.Where(t => t != null));
+
+        if (renderer.rootBone != null)
+        {
+            boneSet.Add(renderer.rootBone);
+        }
+
+        var root = renderer.transform.root;
+        var pending = new Stack<Transform>();
+        pending.Push(root);
+        pending.Push(renderer.transform);
+
+        foreach (var bone in renderer.bones)
+        {
+            if (bone != null)
+            {
+                pending.Push(bone);
+            }
+        }
+
+        while (pending.Count > 0)
+        {
+            var current = pending.Pop();
+            if (current == null || nodes.ContainsKey(current))
+            {
+                continue;
+            }
+
+            var node = FbxNode.Create(scene, current.name);
+            if (node == null)
+            {
+                continue;
+            }
+
+            if (current == renderer.transform)
+            {
+                node.SetNodeAttribute(FbxNull.Create(scene, current.name + "_Null"));
+            }
+            else if (boneSet.Contains(current))
+            {
+                var skeleton = FbxSkeleton.Create(scene, current.name);
+                skeleton.SetSkeletonType(current.parent != null && boneSet.Contains(current.parent)
+                    ? FbxSkeleton.EType.eLimbNode
+                    : FbxSkeleton.EType.eRoot);
+                node.SetNodeAttribute(skeleton);
+            }
+            else
+            {
+                node.SetNodeAttribute(FbxNull.Create(scene, current.name));
+            }
+
+            node.LclTranslation.Set(ToFbxTranslation(current.localPosition, unitSettings));
+            node.LclRotation.Set(new FbxDouble3(current.localEulerAngles.x, current.localEulerAngles.y, current.localEulerAngles.z));
+            node.LclScaling.Set(new FbxDouble3(current.localScale.x, current.localScale.y, current.localScale.z));
+
+            nodes[current] = node;
+
+            if (current.parent != null)
+            {
+                pending.Push(current.parent);
+            }
+        }
+
+        foreach (var pair in nodes)
+        {
+            var transform = pair.Key;
+            var node = pair.Value;
+            if (transform.parent != null && nodes.TryGetValue(transform.parent, out var parentNode))
+            {
+                parentNode.AddChild(node);
+            }
+            else
+            {
+                scene.GetRootNode().AddChild(node);
+            }
+        }
+
+        return nodes;
+    }
+
+    private static FbxNode CreateMeshNode(FbxScene scene, SkinnedMeshRenderer renderer, Mesh simplifiedMesh, Dictionary<Transform, FbxNode> nodes, FbxExportUnitSettings unitSettings)
+    {
+        if (!nodes.TryGetValue(renderer.transform, out var meshNode))
+        {
+            meshNode = FbxNode.Create(scene, renderer.name);
+            if (meshNode == null)
+            {
+                Debug.LogError("Failed to create FBX mesh node.");
+                return null;
+            }
+
+            meshNode.LclTranslation.Set(ToFbxTranslation(renderer.transform.localPosition, unitSettings));
+            meshNode.LclRotation.Set(new FbxDouble3(renderer.transform.localEulerAngles.x, renderer.transform.localEulerAngles.y, renderer.transform.localEulerAngles.z));
+            meshNode.LclScaling.Set(new FbxDouble3(renderer.transform.localScale.x, renderer.transform.localScale.y, renderer.transform.localScale.z));
+            scene.GetRootNode().AddChild(meshNode);
+        }
+
+        var fbxMesh = FbxMesh.Create(scene, simplifiedMesh.name);
+        if (fbxMesh == null)
+        {
+            Debug.LogError("Failed to create FBX mesh.");
+            return null;
+        }
+
+        meshNode.SetNodeAttribute(fbxMesh);
+        fbxMesh.InitControlPoints(simplifiedMesh.vertexCount);
+
+        var vertices = simplifiedMesh.vertices;
+        for (var i = 0; i < vertices.Length; i++)
+        {
+            fbxMesh.SetControlPointAt(ToFbxPosition(vertices[i], unitSettings), i);
+        }
+
+        var triangles = simplifiedMesh.GetIndices(0);
+        for (var i = 0; i < triangles.Length; i += 3)
+        {
+            fbxMesh.BeginPolygon();
+            fbxMesh.AddPolygon(triangles[i]);
+            fbxMesh.AddPolygon(triangles[i + 1]);
+            fbxMesh.AddPolygon(triangles[i + 2]);
+            fbxMesh.EndPolygon();
+        }
+
+        var layer = fbxMesh.GetLayer(0);
+        if (layer == null)
+        {
+            fbxMesh.CreateLayer();
+            layer = fbxMesh.GetLayer(0);
+        }
+
+        if (simplifiedMesh.normals.Length == simplifiedMesh.vertexCount)
+        {
+            using (var normalElement = FbxLayerElementNormal.Create(fbxMesh, "Normals"))
+            {
+                normalElement.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
+                normalElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
+                var directArray = normalElement.GetDirectArray();
+                for (var i = 0; i < triangles.Length; i++)
+                {
+                    directArray.Add(ToFbxDirection(simplifiedMesh.normals[triangles[i]]));
+                }
+                layer.SetNormals(normalElement);
+            }
+        }
+
+        if (simplifiedMesh.tangents.Length == simplifiedMesh.vertexCount)
+        {
+            using (var tangentElement = FbxLayerElementTangent.Create(fbxMesh, "Tangents"))
+            {
+                tangentElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
+                tangentElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
+                var directArray = tangentElement.GetDirectArray();
+                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
+                {
+                    var tangent = simplifiedMesh.tangents[i];
+                    directArray.Add(new FbxVector4(tangent.x, tangent.y, tangent.z, tangent.w));
+                }
+                layer.SetTangents(tangentElement);
+            }
+        }
+
+        if (simplifiedMesh.colors32.Length == simplifiedMesh.vertexCount)
+        {
+            using (var colorElement = FbxLayerElementVertexColor.Create(fbxMesh, "VertexColors"))
+            {
+                colorElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
+                colorElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
+                var directArray = colorElement.GetDirectArray();
+                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
+                {
+                    var color = simplifiedMesh.colors32[i];
+                    directArray.Add(new FbxColor(color.r / 255.0, color.g / 255.0, color.b / 255.0, color.a / 255.0));
+                }
+                layer.SetVertexColors(colorElement);
+            }
+        }
+
+        if (simplifiedMesh.uv.Length == simplifiedMesh.vertexCount)
+        {
+            using (var uvElement = FbxLayerElementUV.Create(fbxMesh, "UVSet"))
+            {
+                uvElement.SetMappingMode(FbxLayerElement.EMappingMode.eByControlPoint);
+                uvElement.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
+                var directArray = uvElement.GetDirectArray();
+                for (var i = 0; i < simplifiedMesh.vertexCount; i++)
+                {
+                    var uv = simplifiedMesh.uv[i];
+                    directArray.Add(new FbxVector2(uv.x, uv.y));
+                }
+                layer.SetUVs(uvElement, FbxLayerElement.EType.eTextureDiffuse);
+            }
+        }
+
+        return meshNode;
+    }
+
+    private static void ApplyMaterials(FbxScene scene, FbxNode meshNode, Material[] materials)
+    {
+        if (materials == null || materials.Length == 0)
+        {
+            return;
+        }
+
+        foreach (var material in materials)
+        {
+            var materialName = material != null && !string.IsNullOrWhiteSpace(material.name) ? material.name : "Material";
+            var fbxMaterial = FbxSurfacePhong.Create(scene, materialName);
+            if (fbxMaterial == null)
+            {
+                continue;
+            }
+
+            meshNode.AddMaterial(fbxMaterial);
+        }
+    }
+
+    private static void ApplySkin(FbxScene scene, FbxNode meshNode, Mesh simplifiedMesh, SkinnedMeshRenderer renderer, Dictionary<Transform, FbxNode> nodes)
+    {
+        var bones = renderer.bones.Where(t => t != null).ToArray();
+        if (bones.Length == 0)
+        {
+            return;
+        }
+
+        var boneWeights = simplifiedMesh.GetAllBoneWeights();
+        var bonesPerVertex = simplifiedMesh.GetBonesPerVertex();
+        try
+        {
+            var clusters = new List<FbxCluster>(bones.Length);
+            var clusterByBoneIndex = new Dictionary<int, FbxCluster>(bones.Length);
+            for (var i = 0; i < bones.Length; i++)
+            {
+                if (!nodes.TryGetValue(bones[i], out var boneNode))
+                {
+                    continue;
+                }
+
+                var cluster = FbxCluster.Create(scene, bones[i].name + "_Cluster");
+                cluster.SetLink(boneNode);
+                cluster.SetLinkMode(FbxCluster.ELinkMode.eTotalOne);
+                cluster.SetTransformMatrix(meshNode.EvaluateGlobalTransform());
+                cluster.SetTransformLinkMatrix(boneNode.EvaluateGlobalTransform());
+                clusters.Add(cluster);
+                clusterByBoneIndex[i] = cluster;
+            }
+
+            var weightOffset = 0;
+            for (var vertexIndex = 0; vertexIndex < bonesPerVertex.Length; vertexIndex++)
+            {
+                var influenceCount = bonesPerVertex[vertexIndex];
+                for (var influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++)
+                {
+                    var weight = boneWeights[weightOffset++];
+                    if (weight.boneIndex < 0 || weight.weight <= 0f || !clusterByBoneIndex.TryGetValue(weight.boneIndex, out var cluster))
+                    {
+                        continue;
+                    }
+
+                    cluster.AddControlPointIndex(vertexIndex, weight.weight);
+                }
+            }
+
+            if (clusters.Count == 0)
+            {
+                return;
+            }
+
+            var skin = FbxSkin.Create(scene, "Skin");
+            foreach (var cluster in clusters)
+            {
+                skin.AddCluster(cluster);
+            }
+
+            meshNode.GetMesh().AddDeformer(skin);
+        }
+        finally
+        {
+            if (boneWeights.IsCreated)
+            {
+                boneWeights.Dispose();
+            }
+
+            if (bonesPerVertex.IsCreated)
+            {
+                bonesPerVertex.Dispose();
+            }
+        }
+    }
+
+    private static void AddBindPose(FbxScene scene, FbxNode meshNode, Dictionary<Transform, FbxNode> nodes)
+    {
+        var pose = FbxPose.Create(scene, "BindPose");
+        pose.SetIsBindPose(true);
+
+        foreach (var pair in nodes)
+        {
+            var node = pair.Value;
+            var matrix = new FbxMatrix(node.EvaluateGlobalTransform());
+            pose.Add(node, matrix);
+        }
+
+        pose.Add(meshNode, new FbxMatrix(meshNode.EvaluateGlobalTransform()));
+        scene.AddPose(pose);
+    }
+
+    private static string AssetPathToAbsolutePath(string assetPath)
+    {
+        var normalized = assetPath.Replace('\\', '/');
+        if (Path.IsPathRooted(normalized))
+        {
+            return Path.GetFullPath(normalized);
+        }
+
+        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
+        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
+    }
+
+    private static FbxVector4 ToFbxPosition(Vector3 value, FbxExportUnitSettings unitSettings)
+    {
+        return new FbxVector4(value.x * unitSettings.FbxUnitsPerUnityUnit, value.y * unitSettings.FbxUnitsPerUnityUnit, value.z * unitSettings.FbxUnitsPerUnityUnit);
+    }
+
+    private static FbxVector4 ToFbxDirection(Vector3 value)
+    {
+        return new FbxVector4(value.x, value.y, value.z);
+    }
+
+    private static FbxDouble3 ToFbxTranslation(Vector3 value, FbxExportUnitSettings unitSettings)
+    {
+        return new FbxDouble3(value.x * unitSettings.FbxUnitsPerUnityUnit, value.y * unitSettings.FbxUnitsPerUnityUnit, value.z * unitSettings.FbxUnitsPerUnityUnit);
+    }
+
+    private sealed class FbxExportUnitSettings : IDisposable
+    {
+        public FbxSystemUnit SystemUnit { get; }
+        public double FbxUnitsPerUnityUnit { get; }
+
+        private FbxExportUnitSettings(FbxSystemUnit systemUnit, double fbxUnitsPerUnityUnit)
+        {
+            SystemUnit = systemUnit;
+            FbxUnitsPerUnityUnit = fbxUnitsPerUnityUnit;
+        }
+
+        public static FbxExportUnitSettings FromSource(FbxSystemUnit sourceUnit, string sourceFilePath)
+        {
+            if (sourceUnit == null)
+            {
+                throw new InvalidOperationException($"Source FBX '{sourceFilePath}' does not define a readable system unit.");
+            }
+
+            var sourceScaleFactor = sourceUnit.GetScaleFactor();
+            var sourceMultiplier = sourceUnit.GetMultiplier();
+            if (sourceScaleFactor <= 0.0)
+            {
+                throw new InvalidOperationException($"Source FBX '{sourceFilePath}' has an invalid system unit scale factor: {sourceScaleFactor}.");
+            }
+
+            var exportUnit = new FbxSystemUnit(sourceScaleFactor, sourceMultiplier);
+            var fbxUnitsPerUnityUnit = CentimetersPerUnityUnit / sourceScaleFactor;
+            return new FbxExportUnitSettings(exportUnit, fbxUnitsPerUnityUnit);
+        }
+
+        public void Dispose()
+        {
+            SystemUnit?.Dispose();
+        }
+    }
+}
+
+[Serializable]
+internal sealed class SkinnedMeshSimplificationConfig
+{
+    private const string ConfigAssetPath = "Assets/EditorConfig/SkinnedMeshSimplification.json";
+
+    [Serializable]
+    internal sealed class Entry
+    {
+        public string entryId;
+        public bool isExternalSource;
+        public string externalSourcePath;
+        public string outputPath;
+        public int reductionPercent = 50;
+    }
+
+    [Serializable]
+    private sealed class Root
+    {
+        public List<Entry> entries = new List<Entry>();
+    }
+
+    public List<Entry> entries = new List<Entry>();
+
+    public static SkinnedMeshSimplificationConfig Load()
+    {
+        var config = new SkinnedMeshSimplificationConfig();
+        var filePath = AssetPathToAbsolutePath(ConfigAssetPath);
+        if (!File.Exists(filePath))
+        {
+            return config;
+        }
+
+        try
+        {
+            var json = File.ReadAllText(filePath);
+            var root = JsonUtility.FromJson<Root>(json);
+            if (root?.entries != null)
+            {
+                config.entries = root.entries;
+            }
+        }
+        catch (Exception ex)
+        {
+            Debug.LogError($"Failed to read skinned mesh simplification config '{ConfigAssetPath}': {ex.Message}");
+        }
+
+        return config;
+    }
+
+    public Entry FindEntry(string assetPath)
+    {
+        if (entries == null)
+        {
+            return null;
+        }
+
+        var normalized = assetPath.Replace('\\', '/');
+        return entries.FirstOrDefault(entry => string.Equals(NormalizeEntryId(entry?.entryId), normalized, StringComparison.OrdinalIgnoreCase));
+    }
+
+    private static string NormalizeEntryId(string entryId)
+    {
+        return string.IsNullOrWhiteSpace(entryId) ? null : entryId.Replace('\\', '/');
+    }
+
+    private static string AssetPathToAbsolutePath(string assetPath)
+    {
+        var normalized = assetPath.Replace('\\', '/');
+        if (Path.IsPathRooted(normalized))
+        {
+            return Path.GetFullPath(normalized);
+        }
+
+        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
+        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
+    }
+}
