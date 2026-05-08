using UnityEngine;

public class ShaderLODSet : MonoBehaviour
{
    public int lod;

    [ContextMenu("apply")]
    private void Apply()
    {
        var meshRenderer = GetComponent<Renderer>();
        if (meshRenderer?.sharedMaterial?.shader != null)
        {
            meshRenderer.sharedMaterial.shader.maximumLOD = lod;
        }
    }
}
