using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SDFUI
{
    public enum SdfImageType { Simple, Sliced }
    public enum SdfOutlinePosition { Outer, Inner, Center, Underlay }

    /// <summary>uGUI image with a baked SDF outline and shadow. Effect sizes use Canvas local units.</summary>
    [AddComponentMenu("UI/SDF Image")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed partial class SdfImage : UnityEngine.UI.Image
    {
        [SerializeField, HideInInspector, FormerlySerializedAs("sprite")] private SdfSprite bakedSprite;
        [SerializeField, HideInInspector, FormerlySerializedAs("imageType")] private SdfImageType legacyImageType;
        [SerializeField, HideInInspector, FormerlySerializedAs("preserveAspect")] private bool legacyPreserveAspect;
        [SerializeField, HideInInspector] private bool migratedImageSettings;
        [SerializeField, HideInInspector] private bool outlineEnabled = true;
        [SerializeField, HideInInspector] private bool shadowEnabled = true;
        [SerializeField, HideInInspector, Min(0)] private float outlineWidth = 2;
        [SerializeField, HideInInspector, Min(0)] private float outlineSoftness;
        [SerializeField, HideInInspector] private Color outlineColor = Color.black;
        [SerializeField, HideInInspector] private bool outlineUseTextureColor;
        [SerializeField, HideInInspector, Min(0)] private float outlineTextureColorIntensity = 1;
        [SerializeField, HideInInspector] private SdfOutlinePosition outlinePosition;
        [SerializeField, HideInInspector] private Color shadowColor = new Color(0, 0, 0, 0.3f);
        [SerializeField, HideInInspector] private Vector2 shadowOffset = new Vector2(2, -2);
        [SerializeField, HideInInspector, Min(0)] private float shadowBlur = 3;
        [SerializeField, HideInInspector] private float shadowSpread;

        private Material ownedMaterial;
        private static Shader sdfShader;
        private UnityEngine.Sprite resolvedSource;
        private bool sourceResolved;
        [SerializeField, HideInInspector] private bool legacyBakedBinding;
        private bool resolvingSource;
        private bool suppressResolve;

#if UNITY_EDITOR
        public static event System.Action<SdfImage> ChangedEditor;
#endif

        /// <summary>The standard Image source, including its temporary override sprite.</summary>
        public UnityEngine.Sprite SourceSprite => base.overrideSprite;
        public SdfSprite SdfData { get { ResolveSource(false); return bakedSprite; } }
        /// <summary>Compatibility API for existing baked-data references. Prefer Image.sprite for new code.</summary>
        public SdfSprite Sprite
        {
            get => SdfData;
            set
            {
                if (bakedSprite == value) return;
                bakedSprite = value;
                legacyBakedBinding = value != null;
                resolvedSource = SourceSprite;
                sourceResolved = true;
                SetAllDirty();
            }
        }

        private SdfImageType ImageType => type == UnityEngine.UI.Image.Type.Sliced ? SdfImageType.Sliced : SdfImageType.Simple;
        public new SdfImageType Type { get => ImageType; set => type = value == SdfImageType.Sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple; }
        public bool PreserveAspect { get => preserveAspect; set => preserveAspect = value; }
        public override Texture mainTexture { get { ResolveSource(false); return UsesSdf ? bakedSprite.ColorTexture : base.mainTexture; } }
        private bool HasSprite => bakedSprite && bakedSprite.IsValid;
        private bool UsesSdf => HasSprite
            && (type == UnityEngine.UI.Image.Type.Simple || (type == UnityEngine.UI.Image.Type.Sliced && fillCenter))
            && (HasEnabledLayers || !SourceSprite);

        public void RefreshSdf()
        {
            MigrateImageSettings();
            ResolveSource(true);
            SetAllDirty();
        }

        private void ResolveSource(bool force)
        {
            if (resolvingSource || suppressResolve) return;
            MigrateImageSettings();
            var current = SourceSprite;
            if (!force && sourceResolved && current == resolvedSource) return;
            bool changed = sourceResolved && current != resolvedSource;
            resolvingSource = true;
            if (changed) legacyBakedBinding = false;
            var attached = SdfSprite.FromSprite(current);
            bakedSprite = attached ? attached : legacyBakedBinding ? bakedSprite : null;
            resolvedSource = current;
            sourceResolved = true;
            resolvingSource = false;
#if UNITY_EDITOR
            if (changed) ChangedEditor?.Invoke(this);
#endif
        }

        private void MigrateImageSettings()
        {
            if (migratedImageSettings) return;
            migratedImageSettings = true;
            if (!bakedSprite) return;
            legacyBakedBinding = true;
            // A legacy helper may seed Image.sprite before this runs. Migrate style independently,
            // and defer resolution while the inherited setters mark geometry/material dirty.
            bool previousSuppression = suppressResolve;
            suppressResolve = true;
            type = legacyImageType == SdfImageType.Sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            preserveAspect = legacyPreserveAspect;
#if UNITY_EDITOR
            // Only old baked-only components seed the new standard source field.
            if (!SourceSprite && bakedSprite.SourceSprite)
                base.sprite = bakedSprite.SourceSprite;
#endif
            resolvedSource = SourceSprite;
            sourceResolved = true;
            suppressResolve = previousSuppression;
        }

        public override void SetVerticesDirty()
        {
            ResolveSource(false);
            base.SetVerticesDirty();
            // Image skips material dirtiness for sprites sharing a texture, but their SDFs are different.
            base.SetMaterialDirty();
        }

        public override void SetMaterialDirty()
        {
            ResolveSource(false);
            base.SetMaterialDirty();
        }

        // Every image owns its material. uGUI stencil derivatives can therefore be updated safely.
        public override Material material
        {
            get { ResolveSource(false); return UsesSdf ? GetSdfMaterial() : base.material; }
            set
            {
                if (m_Material == value) return;
                base.material = value;
            }
        }

        public override Material defaultMaterial => UsesSdf ? GetSdfMaterial() : base.defaultMaterial;

        public override Material materialForRendering
        {
            get
            {
                var result = base.materialForRendering;
                if (!UsesSdf) return result;
                ApplyProperties(result);
                // A Mask creates its stencil/pop materials after this Graphic's modifier.
                for (var i = 0; i < canvasRenderer.popMaterialCount; i++)
                    ApplyProperties(canvasRenderer.GetPopMaterial(i));
                return result;
            }
        }

        private Material GetSdfMaterial()
        {
            if (ownedMaterial)
            {
                ApplyProperties(ownedMaterial);
                return ownedMaterial;
            }
            if (!sdfShader) sdfShader = Resources.Load<Shader>("SDFImage");
            if (!sdfShader) return null;
            ownedMaterial = new Material(sdfShader)
            {
                name = "SDF Image (Instance)",
                hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector
            };
            ApplyProperties(ownedMaterial);
            return ownedMaterial;
        }

        protected override void OnEnable()
        {
            Sanitize();
            MigrateImageSettings();
            ResolveSource(true);
            base.OnEnable();
#if UNITY_EDITOR
            ChangedEditor?.Invoke(this);
#endif
        }

        private void ReleaseMaterial()
        {
            UnityEngine.UI.StencilMaterial.Remove(m_MaskMaterial);
            m_MaskMaterial = null;
            if (!ownedMaterial) return;
            if (Application.isPlaying) Destroy(ownedMaterial);
            else DestroyImmediate(ownedMaterial);
            ownedMaterial = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseMaterial();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseMaterial();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetMaterialDirty();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            SetAllDirty();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            Sanitize();
            // Tint/effect animation keeps the attachment cache; an animated Sprite change still resolves.
            ResolveSource(false);
            base.OnDidApplyAnimationProperties();
            SetAllDirty();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            Sanitize();
            suppressResolve = true;
            base.OnValidate();
            suppressResolve = false;
            // Inspector effect/tint edits only dirty rendering. Defer attachment resolution
            // to the Editor service only when the serialized source actually changed.
            if (!sourceResolved || SourceSprite != resolvedSource) ChangedEditor?.Invoke(this);
        }
#endif

        private void Sanitize()
        {
            outlineWidth = Positive(outlineWidth);
            outlineSoftness = Positive(outlineSoftness);
            outlineColor = SafeColor(outlineColor);
            outlineTextureColorIntensity = Positive(outlineTextureColorIntensity);
            shadowColor = SafeColor(shadowColor);
            shadowOffset = new Vector2(Finite(shadowOffset.x), Finite(shadowOffset.y));
            shadowBlur = Positive(shadowBlur);
            shadowSpread = Finite(shadowSpread);
            outlinePosition = ValidPosition(outlinePosition);
            MigrateLayers();
            foreach (var layer in sdfLayers) layer?.Sanitize();
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value, -100000, 100000);
        private static float Positive(float value) => Mathf.Max(0, Finite(value));
        private static Color SafeColor(Color value) => new Color(Mathf.Clamp01(Finite(value.r)), Mathf.Clamp01(Finite(value.g)), Mathf.Clamp01(Finite(value.b)), Mathf.Clamp01(Finite(value.a)));
        private static SdfOutlinePosition ValidPosition(SdfOutlinePosition value) => value >= SdfOutlinePosition.Outer && value <= SdfOutlinePosition.Underlay ? value : SdfOutlinePosition.Outer;

        private Vector2 NativeSize => HasSprite ? bakedSprite.NativeSize * (canvas ? canvas.referencePixelsPerUnit : 100) : Vector2.zero;

        private Rect DrawingRect()
        {
            var rect = GetPixelAdjustedRect();
            if (!HasSprite || ImageType != SdfImageType.Simple || !preserveAspect || rect.width <= 0 || rect.height <= 0) return rect;
            var nativeSize = NativeSize;
            var aspect = nativeSize.x / nativeSize.y;
            if (rect.width / rect.height > aspect)
            {
                var width = rect.height * aspect;
                rect.x += (rect.width - width) * rectTransform.pivot.x;
                rect.width = width;
            }
            else
            {
                var height = rect.width / aspect;
                rect.y += (rect.height - height) * rectTransform.pivot.y;
                rect.height = height;
            }
            return rect;
        }

        private Vector4 LocalBorder(Rect rect)
        {
            if (ImageType != SdfImageType.Sliced || !HasSprite) return Vector4.zero;
            var nativeSize = NativeSize;
            var border = bakedSprite.Border;
            var multiplier = Mathf.Max(0.01f, pixelsPerUnitMultiplier);
            var scaleX = nativeSize.x / bakedSprite.SourceSize.x / multiplier;
            var scaleY = nativeSize.y / bakedSprite.SourceSize.y / multiplier;
            border.x *= scaleX;
            border.z *= scaleX;
            border.y *= scaleY;
            border.w *= scaleY;
            if (border.x + border.z > rect.width && border.x + border.z > 0)
            {
                var ratio = Mathf.Max(0, rect.width) / (border.x + border.z);
                border.x *= ratio;
                border.z *= ratio;
            }
            if (border.y + border.w > rect.height && border.y + border.w > 0)
            {
                var ratio = Mathf.Max(0, rect.height) / (border.y + border.w);
                border.y *= ratio;
                border.w *= ratio;
            }
            return border;
        }

        private float MinimumScale(Rect rect, Vector4 localBorder)
        {
            var size = bakedSprite.SourceSize;
            if (ImageType != SdfImageType.Sliced) return Mathf.Min(rect.width / size.x, rect.height / size.y);
            var border = bakedSprite.Border;
            var scale = float.MaxValue;
            IncludeScale(ref scale, localBorder.x, border.x);
            IncludeScale(ref scale, localBorder.z, border.z);
            IncludeScale(ref scale, localBorder.y, border.y);
            IncludeScale(ref scale, localBorder.w, border.w);
            var centerWidth = rect.width - localBorder.x - localBorder.z;
            var centerHeight = rect.height - localBorder.y - localBorder.w;
            // Compressed borders remove the center from the mesh; its zero scale must not disable effects.
            if (centerWidth > 0.0001f)
                IncludeScale(ref scale, centerWidth, size.x - border.x - border.z);
            if (centerHeight > 0.0001f)
                IncludeScale(ref scale, centerHeight, size.y - border.y - border.w);
            return Mathf.Max(0, scale == float.MaxValue ? 0 : scale);
        }

        private static void IncludeScale(ref float scale, float local, float source)
        {
            if (source > 0.0001f) scale = Mathf.Min(scale, Mathf.Max(0, local) / source);
        }

        private Vector4 EffectSettings(Rect rect, Vector4 border)
        {
            // Keep released material properties available for tooling; rendering uses the layer arrays.
            var budget = EffectBudget(rect, border);
            var width = OutlineEnabled ? Mathf.Min(Positive(OutlineWidth), budget) : 0;
            var softness = OutlineEnabled ? Mathf.Min(Positive(OutlineSoftness), Mathf.Max(0, budget - width) * 2) : 0;
            var spread = ShadowEnabled ? Mathf.Clamp(Finite(ShadowSpread), -budget, budget) : 0;
            var blur = ShadowEnabled ? Mathf.Min(Positive(ShadowBlur), Mathf.Max(0, budget - Mathf.Abs(spread)) * 2) : 0;
            return new Vector4(width, softness, blur, spread);
        }

        private Rect ExpandedRect()
        {
            var rect = DrawingRect();
            if (!HasSprite || rect.width <= 0 || rect.height <= 0) return rect;
            var min = rect.min;
            var max = rect.max;
            float budget = EffectBudget(rect, LocalBorder(rect));
            var layers = Layers;
            if (sdfEffectsEnabled)
                for (int i = 0; i < Mathf.Min(layers.Count, MaxEffectLayers); i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.IsVisible) continue;
                    Vector4 style = LayerSettings(layer, budget);
                    float outer = layer.Position == SdfOutlinePosition.Inner ? 0
                        : Mathf.Max(0, style.z) * (layer.Position == SdfOutlinePosition.Center ? 0.5f : 1) + style.w * 0.5f;
                    Vector2 offset = new Vector2(style.x, style.y);
                    min = Vector2.Min(min, rect.min + offset - Vector2.one * outer);
                    max = Vector2.Max(max, rect.max + offset + Vector2.one * outer);
                }
            return Rect.MinMaxRect(min.x - 1, min.y - 1, max.x + 1, max.y + 1);
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            ResolveSource(false);
            if (!UsesSdf)
            {
                base.OnPopulateMesh(vertices);
                return;
            }
            vertices.Clear();
            if (!HasSprite || !GetSdfMaterial()) return;
            var drawing = DrawingRect();
            if (drawing.width <= 0 || drawing.height <= 0) return;
            var rect = ExpandedRect();
            // Canvas batching transforms POSITION into Canvas space; UV0 keeps image-local coordinates intact.
            vertices.AddVert(new Vector3(rect.xMin, rect.yMin), color, new Vector2(rect.xMin, rect.yMin));
            vertices.AddVert(new Vector3(rect.xMin, rect.yMax), color, new Vector2(rect.xMin, rect.yMax));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMax), color, new Vector2(rect.xMax, rect.yMax));
            vertices.AddVert(new Vector3(rect.xMax, rect.yMin), color, new Vector2(rect.xMax, rect.yMin));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
        }

        private void ApplyProperties(Material target)
        {
            if (!target || !target.HasProperty("_SdfTex")) return;
            target.SetFloat("_HasSprite", HasSprite ? 1 : 0);
            if (!HasSprite) return;
            var rect = DrawingRect();
            var border = LocalBorder(rect);
            var settings = EffectSettings(rect, border);
            target.SetTexture("_MainTex", bakedSprite.ColorTexture);
            target.SetTexture("_SdfTex", bakedSprite.DistanceTexture);
            Vector2 decode = bakedSprite.DistanceDecode;
            target.SetVector("_SdfDecode", new Vector4(decode.x, decode.y, 0, 0));
            target.SetVector("_SourceSize", new Vector4(bakedSprite.SourceSize.x, bakedSprite.SourceSize.y, bakedSprite.Padding, bakedSprite.DistanceRange));
            target.SetVector("_ImageRect", new Vector4(rect.x, rect.y, Mathf.Max(0.0001f, rect.width), Mathf.Max(0.0001f, rect.height)));
            target.SetVector("_SourceBorder", ImageType == SdfImageType.Sliced ? bakedSprite.Border : Vector4.zero);
            target.SetVector("_LocalBorder", border);
            ApplyLayers(target, rect, border);
            target.SetVector("_Outline", new Vector4(settings.x, settings.y, (float)OutlinePosition, 0));
            target.SetColor("_OutlineColor", OutlineEnabled ? OutlineColor : Color.clear);
            target.SetVector("_OutlineTextureColor", new Vector4(OutlineUseTextureColor ? 1 : 0, OutlineTextureColorIntensity, 0, 0));
            target.SetColor("_ShadowColor", ShadowEnabled ? ShadowColor : Color.clear);
            target.SetVector("_Shadow", new Vector4(ShadowOffset.x, ShadowOffset.y, settings.z, settings.w));
        }

        public override bool Raycast(Vector2 screenPoint, Camera eventCamera)
        {
            if (!UsesSdf) return base.Raycast(screenPoint, eventCamera);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local)
                && rectTransform.rect.Contains(local) && base.Raycast(screenPoint, eventCamera);
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            // Include the expanded effects when RectMask2D decides whether the whole Graphic is culled.
            if (!canvas || !UsesSdf) { base.Cull(clipRect, validRect); return; }
            var rect = ExpandedRect();
            var toRoot = canvas.rootCanvas.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 4; i++)
            {
                Vector2 point = toRoot.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? rect.xMin : rect.xMax, (i & 2) == 0 ? rect.yMin : rect.yMax));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            var shouldCull = !validRect || !clipRect.Overlaps(Rect.MinMaxRect(min.x, min.y, max.x, max.y), true);
            if (canvasRenderer.cull == shouldCull) return;
            canvasRenderer.cull = shouldCull;
            onCullStateChanged.Invoke(shouldCull);
            OnCullingChanged();
        }

        public override void SetNativeSize()
        {
            ResolveSource(false);
            if (!UsesSdf) { base.SetNativeSize(); return; }
            rectTransform.anchorMax = rectTransform.anchorMin;
            rectTransform.sizeDelta = NativeSize;
            SetAllDirty();
        }

        // Source-backed images keep the standard Image layout contract when effects turn on or off.
        // The baked-only compatibility API retains its original native-size/border layout.
        public override float minWidth => !SourceSprite && UsesSdf && ImageType == SdfImageType.Sliced
            ? (bakedSprite.Border.x + bakedSprite.Border.z) * NativeSize.x / bakedSprite.SourceSize.x / Mathf.Max(0.01f, pixelsPerUnitMultiplier) : base.minWidth;
        public override float minHeight => !SourceSprite && UsesSdf && ImageType == SdfImageType.Sliced
            ? (bakedSprite.Border.y + bakedSprite.Border.w) * NativeSize.y / bakedSprite.SourceSize.y / Mathf.Max(0.01f, pixelsPerUnitMultiplier) : base.minHeight;
        public override float preferredWidth => !SourceSprite && UsesSdf ? NativeSize.x : base.preferredWidth;
        public override float preferredHeight => !SourceSprite && UsesSdf ? NativeSize.y : base.preferredHeight;

        protected override void UpdateMaterial()
        {
            ResolveSource(false);
            canvasRenderer.SetAlphaTexture(null);
            if (!UsesSdf)
            {
                base.UpdateMaterial();
                return;
            }
            if (!IsActive()) return;
            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(materialForRendering, 0);
            canvasRenderer.SetTexture(bakedSprite.ColorTexture);
        }
    }
}
