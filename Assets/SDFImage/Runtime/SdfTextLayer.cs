using UnityEngine;
using UnityEngine.UI;

namespace SDFUI
{
    /// <summary>A render-only graphic borrowing the face renderer's mesh; it never destroys that mesh.</summary>
    [AddComponentMenu("")]
    public sealed class SdfTextLayer : MaskableGraphic
    {
        public SdfText Owner { get; private set; }
        private Mesh borrowedMesh;
        private Material effectMaterial;
        private readonly Vector3[] corners = new Vector3[4];

        public override Texture mainTexture => effectMaterial ? effectMaterial.mainTexture : s_WhiteTexture;
        public override Material material { get => effectMaterial; set { } }

        internal void Configure(SdfText owner, Mesh mesh, Material source, Shader shader,
            Color color, float width, float softness, Vector2 offset, bool visible)
        {
            Owner = owner;
            if (!visible) { Clear(); return; }
            gameObject.layer = owner.gameObject.layer;
            raycastTarget = false;
            if (maskable != owner.maskable) { maskable = owner.maskable; RecalculateClipping(); RecalculateMasking(); }
            if (!effectMaterial) effectMaterial = new Material(shader) { name = "SDF Text Effect", hideFlags = HideFlags.HideAndDontSave };
            effectMaterial.CopyPropertiesFromMaterial(source);
            effectMaterial.shaderKeywords = System.Array.Empty<string>();
            // Inspector colors are sRGB; this non-HDR material property expects working-space RGB.
            effectMaterial.SetColor("_EffectColor", QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color);
            effectMaterial.SetFloat("_EffectWidth", width);
            effectMaterial.SetFloat("_EffectSoftness", softness);
            effectMaterial.SetInt("_StencilComp", 8);
            effectMaterial.SetInt("_Stencil", 0);
            effectMaterial.SetInt("_StencilOp", 0);
            effectMaterial.SetInt("_StencilReadMask", 255);
            effectMaterial.SetInt("_StencilWriteMask", 255);
            effectMaterial.SetInt("_ColorMask", 15);

            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = owner.rectTransform.pivot;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = offset;
            UseMesh(mesh);
            UpdateMaterial();
            canvasRenderer.SetColor(owner.canvasRenderer.GetColor());
        }

        internal void UseMesh(Mesh value)
        {
            borrowedMesh = value;
            if (isActiveAndEnabled) UpdateGeometry();
        }

        internal void Clear()
        {
            borrowedMesh = null;
            canvasRenderer.SetMesh(null);
        }

        protected override void UpdateGeometry() => canvasRenderer.SetMesh(borrowedMesh);

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            Material modified = base.GetModifiedMaterial(baseMaterial);
            if (modified && modified != baseMaterial)
                SdfText.CopyRenderProperties(baseMaterial, modified, modified);
            return modified;
        }

        // TMP can generate fallback meshes during a Canvas rebuild; upload them directly then.
        public override void SetVerticesDirty()
        {
            if (CanvasUpdateRegistry.IsRebuildingGraphics()) UpdateGeometry(); else base.SetVerticesDirty();
        }

        public override void SetMaterialDirty()
        {
            if (CanvasUpdateRegistry.IsRebuildingGraphics()) UpdateMaterial(); else base.SetMaterialDirty();
        }

        public override void OnCullingChanged()
        {
            if (!CanvasUpdateRegistry.IsRebuildingGraphics()) { base.OnCullingChanged(); return; }
            if (!canvasRenderer.cull) { UpdateGeometry(); UpdateMaterial(); }
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            if (!borrowedMesh || !canvas) { base.Cull(clipRect, validRect); return; }
            Bounds bounds = borrowedMesh.bounds;
            if (effectMaterial) bounds.center += new Vector3(effectMaterial.GetFloat("_VertexOffsetX"), effectMaterial.GetFloat("_VertexOffsetY"), 0);
            corners[0] = bounds.min;
            corners[1] = new Vector3(bounds.min.x, bounds.max.y, bounds.center.z);
            corners[2] = bounds.max;
            corners[3] = new Vector3(bounds.max.x, bounds.min.y, bounds.center.z);
            Matrix4x4 matrix = canvas.rootCanvas.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue), maximum = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 p = matrix.MultiplyPoint3x4(corners[i]);
                minimum = Vector2.Min(minimum, p);
                maximum = Vector2.Max(maximum, p);
            }
            bool culled = !validRect || !clipRect.Overlaps(Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y), true);
            if (canvasRenderer.cull == culled) return;
            canvasRenderer.cull = culled;
            onCullStateChanged.Invoke(culled);
            OnCullingChanged();
        }

        protected override void OnDestroy()
        {
            SdfText.Release(effectMaterial);
            base.OnDestroy();
        }
    }
}
