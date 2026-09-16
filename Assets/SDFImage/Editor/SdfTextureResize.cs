using UnityEngine;
using UnityEditor;

namespace SDFUI.Editor
{
    internal static class SdfTextureResize
    {
        internal static void Blit(Texture2D source, Rect rect, RenderTexture target, TextureResizeAlgorithm algorithm, bool sRGB)
        {
            if (algorithm == TextureResizeAlgorithm.Bilinear && source.filterMode == FilterMode.Bilinear
                || rect.width == target.width && rect.height == target.height)
            {
                Graphics.Blit(source, target, new Vector2(rect.width / source.width, rect.height / source.height),
                    new Vector2(rect.x / source.width, rect.y / source.height));
                return;
            }
            var shader = Shader.Find("Hidden/SDFUI/TextureResize");
            if (!shader) throw new System.InvalidOperationException("The SDF texture resize shader is missing.");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture intermediate = null;
            try
            {
                material.SetVector("_SourceRect", new Vector4(rect.x, rect.y, rect.width, rect.height));
                if (algorithm == TextureResizeAlgorithm.Bilinear)
                {
                    Graphics.Blit(source, target, material, 0);
                    return;
                }
                // Separable Mitchell filtering covers the full downsample footprint. Only
                // the resized result is read back to the CPU for distance generation.
                intermediate = RenderTexture.GetTemporary(target.width, Mathf.RoundToInt(rect.height), 0,
                    RenderTextureFormat.ARGB32, sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                material.SetVector("_Filter", new Vector4(1, 0, Mathf.Max(1, rect.width / target.width), 0));
                Graphics.Blit(source, intermediate, material, 1);
                material.SetVector("_SourceRect", new Vector4(0, 0, intermediate.width, intermediate.height));
                material.SetVector("_Filter", new Vector4(0, 1, Mathf.Max(1, rect.height / target.height), 0));
                Graphics.Blit(intermediate, target, material, 1);
            }
            finally
            {
                if (intermediate) RenderTexture.ReleaseTemporary(intermediate);
                Object.DestroyImmediate(material);
            }
        }
    }
}
