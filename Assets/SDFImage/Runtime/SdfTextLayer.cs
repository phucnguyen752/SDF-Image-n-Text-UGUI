using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace SDFUI
{
    /// <summary>A render-only graphic copying TMP geometry and storing effect style in its vertices.</summary>
    [AddComponentMenu("")]
    public sealed class SdfTextLayer : MaskableGraphic
    {
        public SdfText Owner { get; private set; }
        private Mesh borrowedMesh;
        private Mesh styledMesh;
        private bool meshLayoutInitialized;
        // Mesh construction runs synchronously on Unity's main thread. Reuse scratch
        // buffers across layers instead of retaining a managed geometry copy per layer.
        private static readonly List<Vector3> positions = new List<Vector3>();
        private static readonly List<Vector3> normals = new List<Vector3>();
        private static readonly List<Color32> colors = new List<Color32>();
        private static readonly List<Vector4> atlasUvs = new List<Vector4>();
        private static readonly List<int> indices = new List<int>();
        private static readonly List<Vertex> vertices = new List<Vertex>();
        private static readonly VertexAttributeDescriptor[] vertexLayout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4)
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            internal Vector3 position, normal;
            internal Color32 color;
            internal Vector4 atlas, bounds, style, effectColor;
        }
        private struct Style
        {
            internal Color color;
            internal Vector4 parameters;
            internal Vector2 offset;
            internal float mode;
            internal bool inner;

            internal Style(SdfTextEffect value)
            {
                color = QualitySettings.activeColorSpace == ColorSpace.Linear ? value.Color.linear : value.Color;
                inner = value.IsInner;
                offset = value.Offset;
                parameters = new Vector4(value.Width, value.Softness, inner ? offset.x : 0, inner ? offset.y : 0);
                mode = (float)value.Position + (value.Position == SdfOutlinePosition.Underlay ? (float)value.UnderlayType : 0);
            }

            internal bool Equals(Style other) => color.Equals(other.color) && parameters.Equals(other.parameters)
                && offset.Equals(other.offset) && mode == other.mode && inner == other.inner;
        }

        private readonly List<Style> styles = new List<Style>();
        private readonly List<int> sourceIndices = new List<int>();
        private int indexedVertexCount, indexedStyleCount;
        private static readonly List<Style> pendingStyles = new List<Style>();
        private static readonly List<int> combinedIndices = new List<int>();
        private bool merged;
        private Material effectMaterial;
        private SdfTextMaterials.Entry materialEntry;
        private int materialRevision;
        private int meshVersion = -1;
        private readonly Vector3[] corners = new Vector3[4];

        public override Texture mainTexture => effectMaterial ? effectMaterial.mainTexture : s_WhiteTexture;
        public override Material material { get => effectMaterial; set { } }

        internal void Configure(SdfText owner, Mesh mesh, Material source, Shader shader,
            SdfTextEffect singleStyle, bool combine, bool aboveText, bool visible, int geometryVersion)
        {
            Owner = owner;
            if (!visible || !mesh) { Clear(); return; }
            pendingStyles.Clear();
            if (combine)
            {
                // A combined mesh retains whole-layer order: all glyphs of each rear
                // effect precede all glyphs of the next effect, including shifted quads.
                var layers = owner.RenderLayers;
                for (int i = layers.Count - 1; i >= 0; i--)
                    if (layers[i] != null && layers[i].IsVisible && layers[i].DrawAboveText == aboveText)
                        pendingStyles.Add(new Style(layers[i]));
            }
            else if (singleStyle != null && singleStyle.IsVisible) pendingStyles.Add(new Style(singleStyle));
            if (pendingStyles.Count == 0) { Clear(); return; }
            bool styleChanged = merged != combine || styles.Count != pendingStyles.Count;
            for (int i = 0; !styleChanged && i < styles.Count; i++)
                styleChanged = !styles[i].Equals(pendingStyles[i]);
            if (styleChanged)
            {
                styles.Clear();
                styles.AddRange(pendingStyles);
            }
            merged = combine;
            gameObject.layer = owner.gameObject.layer;
            raycastTarget = false;
            if (maskable != owner.maskable) { maskable = owner.maskable; RecalculateClipping(); RecalculateMasking(); }
            if (materialEntry == null && effectMaterial) SdfText.Release(effectMaterial);
            Material previous = effectMaterial;
            effectMaterial = SdfTextMaterials.Effect(source, shader, ref materialEntry);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.pivot = owner.rectTransform.pivot;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = combine || styles[0].inner ? Vector2.zero : styles[0].offset;
            bool geometryChanged = borrowedMesh != mesh || meshVersion != geometryVersion || !styledMesh;
            if (geometryChanged || styleChanged)
            {
                PrepareMesh(mesh, geometryChanged || styleChanged);
                borrowedMesh = mesh;
                meshVersion = geometryVersion;
                UpdateGeometry();
            }
            if (previous != effectMaterial || materialRevision != materialEntry.revision)
            {
                UpdateMaterial();
                materialRevision = materialEntry.revision;
            }
            canvasRenderer.SetColor(owner.canvasRenderer.GetColor());
        }

        internal void RefreshSharedMaterial()
        {
            if (materialEntry == null || !effectMaterial || materialRevision == materialEntry.revision) return;
            UpdateMaterial();
            materialRevision = materialEntry.revision;
        }

        internal void SyncColor(Color color) => canvasRenderer.SetColor(color);

        private void PrepareMesh(Mesh source, bool updateIndices)
        {
            if (!styledMesh)
            {
                styledMesh = new Mesh { name = "SDF Text Layer", hideFlags = HideFlags.HideAndDontSave };
                styledMesh.MarkDynamic();
            }
            int sourceCount = source.vertexCount, count = sourceCount * styles.Count;
            IndexFormat format = count > ushort.MaxValue ? IndexFormat.UInt32 : source.indexFormat;
            if (!meshLayoutInitialized || styledMesh.vertexCount != count || styledMesh.indexFormat != format)
            {
                styledMesh.Clear();
                styledMesh.indexFormat = format;
                styledMesh.SetVertexBufferParams(count, vertexLayout);
                meshLayoutInitialized = true;
                updateIndices = true;
            }
            source.GetVertices(positions);
            source.GetNormals(normals);
            source.GetColors(colors);
            source.GetUVs(0, atlasUvs);
            vertices.Clear();
            Bounds sourceBounds = source.bounds, totalBounds = sourceBounds;
            for (int layerIndex = 0; layerIndex < styles.Count; layerIndex++)
            {
                Style style = styles[layerIndex];
                Vector3 shift = merged && !style.inner ? (Vector3)style.offset : Vector3.zero;
                Bounds bounds = sourceBounds;
                bounds.center += shift;
                if (layerIndex == 0) totalBounds = bounds; else totalBounds.Encapsulate(bounds);
                for (int start = 0; start < sourceCount; start += 4)
                {
                    int end = Mathf.Min(start + 4, sourceCount);
                    Vector4 firstUv = start < atlasUvs.Count ? atlasUvs[start] : Vector4.zero;
                    Vector4 glyphBounds = new Vector4(firstUv.x, firstUv.y, firstUv.x, firstUv.y);
                    if (style.inner && style.offset != Vector2.zero)
                        for (int i = start + 1; i < end && i < atlasUvs.Count; i++)
                        {
                            glyphBounds.x = Mathf.Min(glyphBounds.x, atlasUvs[i].x);
                            glyphBounds.y = Mathf.Min(glyphBounds.y, atlasUvs[i].y);
                            glyphBounds.z = Mathf.Max(glyphBounds.z, atlasUvs[i].x);
                            glyphBounds.w = Mathf.Max(glyphBounds.w, atlasUvs[i].y);
                        }
                    for (int i = start; i < end; i++)
                    {
                        Vector4 uv = i < atlasUvs.Count ? atlasUvs[i] : Vector4.zero;
                        // The effect shader does not need TMP's packed UV0.z.
                        uv.z = style.mode;
                        vertices.Add(new Vertex
                        {
                            position = positions[i] + shift, normal = i < normals.Count ? normals[i] : Vector3.back,
                            color = i < colors.Count ? colors[i] : (Color32)Color.white,
                            atlas = uv, bounds = glyphBounds, style = style.parameters, effectColor = style.color
                        });
                    }
                }
            }
            // One upload per side of a single-atlas label, regardless of layer count.
            styledMesh.SetVertexBufferData(vertices, 0, 0, count, 0, MeshUpdateFlags.DontRecalculateBounds);
            if (updateIndices)
            {
                source.GetIndices(indices, 0);
                bool changed = indexedVertexCount != sourceCount || indexedStyleCount != styles.Count ||
                    sourceIndices.Count != indices.Count || styledMesh.GetIndexCount(0) != (uint)(indices.Count * styles.Count);
                for (int i = 0; !changed && i < indices.Count; i++) changed = sourceIndices[i] != indices[i];
                if (changed)
                {
                    sourceIndices.Clear();
                    sourceIndices.AddRange(indices);
                    indexedVertexCount = sourceCount;
                    indexedStyleCount = styles.Count;
                    combinedIndices.Clear();
                    for (int layerIndex = 0; layerIndex < styles.Count; layerIndex++)
                    {
                        int vertexOffset = layerIndex * sourceCount;
                        foreach (int index in indices) combinedIndices.Add(index + vertexOffset);
                    }
                    styledMesh.SetIndices(combinedIndices, MeshTopology.Triangles, 0, false);
                }
            }
            styledMesh.bounds = totalBounds;
        }
        internal void Clear()
        {
            if (ReferenceEquals(borrowedMesh, null)) return;
            borrowedMesh = null;
            meshVersion = -1;
            canvasRenderer.SetMesh(null);
        }

        protected override void UpdateGeometry() => canvasRenderer.SetMesh(borrowedMesh ? styledMesh : null);

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
            Bounds bounds = styledMesh ? styledMesh.bounds : borrowedMesh.bounds;
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

        protected override void OnDisable()
        {
            base.OnDisable();
            SdfTextMaterials.Release(ref materialEntry);
            effectMaterial = null;
            meshVersion = -1;
        }

        protected override void OnDestroy()
        {
            SdfTextMaterials.Release(ref materialEntry);
            SdfText.Release(styledMesh);
            base.OnDestroy();
        }
    }
}
