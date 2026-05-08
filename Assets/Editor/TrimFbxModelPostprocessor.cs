using System;
using UnityEditor;

public sealed class TrimFbxModelPostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.EndsWith("_trim.fbx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (assetImporter is ModelImporter modelImporter)
        {
            modelImporter.importNormals = ModelImporterNormals.Import;
            modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
        }
    }
}
