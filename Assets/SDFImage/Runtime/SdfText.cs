using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SDFUI
{
    /// <summary>TMP text with all glyph effects behind all glyph faces. Sizes use Canvas local units.</summary>
    [ExecuteAlways, AddComponentMenu("UI/SDF Text")]
    public sealed class SdfText : TextMeshProUGUI
    {
        [SerializeField] private bool sdfEffectsEnabled = true;
        [SerializeField] private List<SdfTextEffect> sdfLayers = new List<SdfTextEffect>();
        [SerializeField, HideInInspector] private bool sdfLayersMigrated;

        // Keep legacy fields serialized so existing scenes and prefab overrides can migrate.
        [SerializeField, HideInInspector] private bool sdfOutlineEnabled = true;
        [SerializeField, HideInInspector] private List<SdfTextEffect> sdfOutlines = new List<SdfTextEffect>();
        [SerializeField, HideInInspector] private bool sdfOutlinesMigrated;
        [SerializeField, HideInInspector] private float sdfOutlineWidth = 2;
        [SerializeField, HideInInspector] private float sdfOutlineSoftness;
        [SerializeField, HideInInspector] private Color sdfOutlineColor = Color.black;
        [SerializeField, HideInInspector] private Vector2 sdfLegacyOutlineSize;
        [SerializeField, HideInInspector] private Color sdfLegacyOutlineColor;
        [SerializeField, HideInInspector] private bool sdfLegacyOutlineEnabled;
        [SerializeField, HideInInspector] private bool sdfShadowEnabled;
        [SerializeField, HideInInspector] private Vector2 sdfShadowOffset = new Vector2(2, -2);
        [SerializeField, HideInInspector] private float sdfShadowBlur = 2;
        [SerializeField, HideInInspector] private float sdfShadowSpread;
        [SerializeField, HideInInspector] private Color sdfShadowColor = new Color(0, 0, 0, 0.3f);
        [SerializeField, HideInInspector] private bool sdfLegacyShadowEnabled;
        [SerializeField, HideInInspector] private Vector4 sdfLegacyShadowSize;
        [SerializeField, HideInInspector] private Color sdfLegacyShadowColor;

        private RectTransform effectRoot;
        private CanvasGroup effectGroup;
        // Unity hot reload must restore ownership together with effectRoot.
        private List<SdfTextLayer> effectLayers = new List<SdfTextLayer>();
        private readonly List<CanvasGroup> ownGroups = new List<CanvasGroup>();
        private readonly List<RectMask2D> clipMasks = new List<RectMask2D>();
        private Material faceSource, faceStencil, faceMaterial;
        private bool syncing;
        private bool meshCleared;
        private static Shader effectShader;

        public bool EffectsEnabled { get => sdfEffectsEnabled; set { if (sdfEffectsEnabled == value) return; sdfEffectsEnabled = value; RefreshEffects(); } }
        /// <summary>Frontmost effect first. Call RefreshEffects after changing the list or its entries.</summary>
        public List<SdfTextEffect> Layers { get { MigrateLayers(); return sdfLayers; } }

        // Released scalar APIs follow their original layers even when the list is reordered.
        public bool OutlineEnabled { get => LegacyLayer(SdfTextEffectRole.Outline, false)?.Enabled ?? false; set { LegacyLayer(SdfTextEffectRole.Outline, true).Enabled = value; RefreshEffects(); } }
        public float OutlineWidth { get => LegacyLayer(SdfTextEffectRole.Outline, false)?.Width ?? 0; set { LegacyLayer(SdfTextEffectRole.Outline, true).Width = Positive(value); RefreshEffects(); } }
        public float OutlineSoftness { get => LegacyLayer(SdfTextEffectRole.Outline, false)?.Softness ?? 0; set { LegacyLayer(SdfTextEffectRole.Outline, true).Softness = value; RefreshEffects(); } }
        public Color OutlineColor { get => LegacyLayer(SdfTextEffectRole.Outline, false)?.Color ?? Color.clear; set { LegacyLayer(SdfTextEffectRole.Outline, true).Color = value; RefreshEffects(); } }
        public Vector2 OutlineOffset { get => LegacyLayer(SdfTextEffectRole.Outline, false)?.Offset ?? Vector2.zero; set { LegacyLayer(SdfTextEffectRole.Outline, true).Offset = value; RefreshEffects(); } }
        public bool ShadowEnabled { get => LegacyLayer(SdfTextEffectRole.Shadow, false)?.Enabled ?? false; set { LegacyLayer(SdfTextEffectRole.Shadow, true).Enabled = value; RefreshEffects(); } }
        public Vector2 ShadowOffset { get => LegacyLayer(SdfTextEffectRole.Shadow, false)?.Offset ?? Vector2.zero; set { LegacyLayer(SdfTextEffectRole.Shadow, true).Offset = value; RefreshEffects(); } }
        public float ShadowBlur { get => LegacyLayer(SdfTextEffectRole.Shadow, false)?.Softness ?? 0; set { LegacyLayer(SdfTextEffectRole.Shadow, true).Softness = value; RefreshEffects(); } }
        public float ShadowSpread { get => LegacyLayer(SdfTextEffectRole.Shadow, false)?.Width ?? 0; set { LegacyLayer(SdfTextEffectRole.Shadow, true).Width = value; RefreshEffects(); } }
        public Color ShadowColor { get => LegacyLayer(SdfTextEffectRole.Shadow, false)?.Color ?? Color.clear; set { LegacyLayer(SdfTextEffectRole.Shadow, true).Color = value; RefreshEffects(); } }

        // These components change the draw/mask domain of the text itself. A parent is supported.
        public bool EffectsSupported => !GetComponent<Canvas>() && !GetComponent<Mask>() && !GetComponent<RectMask2D>();
        private bool HasEffects
        {
            get
            {
                if (sdfEffectsEnabled)
                    foreach (var layer in Layers)
                        if (layer != null && layer.IsVisible) return true;
                return false;
            }
        }

        protected override void OnEnable()
        {
            MigrateLayers();
            base.OnEnable();
            if (effectRoot)
            {
                // Recover all owned graphics across hot reload, including older pool layouts.
                effectLayers.Clear();
                for (int i = 0; i < effectRoot.childCount; i++)
                {
                    var layer = effectRoot.GetChild(i).GetComponent<SdfTextLayer>();
                    if (layer) effectLayers.Add(layer);
                }
                effectRoot.gameObject.SetActive(true);
            }
            Canvas.preWillRenderCanvases += SyncEffects;
            RefreshEffects();
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= SyncEffects;
            if (effectRoot) effectRoot.gameObject.SetActive(false);
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            Canvas.preWillRenderCanvases -= SyncEffects;
            if (effectRoot)
            {
                var root = effectRoot.gameObject;
                effectRoot = null;
#if UNITY_EDITOR
                // Scene teardown may already be destroying this sibling. Wait until that
                // operation completes before removing a root left by component removal/Undo.
                if (!Application.isPlaying)
                    UnityEditor.EditorApplication.delayCall += () => { if (root) DestroyImmediate(root); };
                else
#endif
                    Destroy(root);
            }
            Release(faceMaterial);
            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            MigrateLayers();
            foreach (var layer in sdfLayers) layer?.Sanitize();
            base.OnValidate();
            RefreshEffects();
        }
#endif

        public void RefreshEffects()
        {
            MigrateLayers();
            havePropertiesChanged = true;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            Material modified = base.GetModifiedMaterial(baseMaterial);
            if (!isActiveAndEnabled || !EffectsSupported || !IsDistanceField(modified)) return modified;
            faceSource = baseMaterial;
            faceStencil = modified;
            return FaceOnly(faceSource, ref faceMaterial, faceStencil);
        }

        protected override void GenerateTextMesh()
        {
            meshCleared = false;
            bool propertiesChanged = m_havePropertiesChanged;
            base.UpdateMeshPadding();
            m_havePropertiesChanged = propertiesChanged;
            // Padding affects geometry only; TMP still owns advances, wrapping and preferred size.
            // Use the available atlas border, never sample across adjacent glyph atlas rectangles.
            if (HasEffects && EffectsSupported)
            {
                m_padding = Mathf.Max(m_padding, AtlasPadding(m_sharedMaterial));
                for (int i = 1; i < m_subTextObjects.Length; i++)
                    if (m_subTextObjects[i])
                        m_subTextObjects[i].padding = Mathf.Max(m_subTextObjects[i].padding,
                            AtlasPadding(m_subTextObjects[i].sharedMaterial));
            }
            base.GenerateTextMesh();
            SyncEffects();
        }

        public override void ClearMesh()
        {
            base.ClearMesh();
            meshCleared = true;
            ClearEffects();
        }

        public override void UpdateVertexData(TMP_VertexDataUpdateFlags flags)
        {
            base.UpdateVertexData(flags);
            meshCleared = false;
            SyncEffects();
        }

        public override void UpdateVertexData()
        {
            base.UpdateVertexData();
            meshCleared = false;
            SyncEffects();
        }

        public override void UpdateGeometry(Mesh mesh, int index)
        {
            base.UpdateGeometry(mesh, index);
            meshCleared = false;
            SyncEffects();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            if (effectRoot && transform.parent) effectRoot.SetParent(transform.parent, false);
            RefreshEffects();
        }

        private void SyncEffects()
        {
            if (syncing || !this) return;
            syncing = true;
            try
            {
                // Keep render-only clones in sync with animated material properties.
                if (faceSource && faceMaterial) FaceOnly(faceSource, ref faceMaterial, faceStencil);
                for (int i = 1; i < m_subTextObjects.Length; i++)
                {
                    var sub = m_subTextObjects[i];
                    if (!sub) continue;
                    var modifier = sub.GetComponent<SdfTextFaceMaterial>();
                    if (!modifier && EffectsSupported)
                    {
                        modifier = sub.gameObject.AddComponent<SdfTextFaceMaterial>();
                        modifier.hideFlags = HideFlags.HideInInspector | HideFlags.DontSave;
                    }
                    if (modifier && modifier.Owner != this)
                    {
                        modifier.Owner = this;
                        sub.SetMaterialDirty();
                    }
                }

                bool visible = !meshCleared && isActiveAndEnabled && HasEffects && EffectsSupported && canvas
                    && transform.parent && textInfo != null && textInfo.characterCount > 0;
                if (!visible)
                {
                    ClearEffects();
                    return;
                }
                if (!effectShader) effectShader = Resources.Load<Shader>("SDFTextEffect");
                if (!effectShader) return;
                EnsureRoot();
                SyncTransform();
                effectRoot.gameObject.SetActive(true);

                int count = textInfo.materialCount;
                int layerCount = sdfLayers.Count;
                while (effectLayers.Count < count * layerCount)
                    effectLayers.Add(CreateLayer("Effect"));

                for (int i = 0; i < count; i++)
                {
                    var info = textInfo.meshInfo[i];
                    Material source = i == 0 ? fontSharedMaterial
                        : i < m_subTextObjects.Length && m_subTextObjects[i] ? m_subTextObjects[i].sharedMaterial : null;
                    bool active = info.vertexCount > 0 && IsDistanceField(source);
                    // Match the actual face renderer, including a mesh supplied via UpdateGeometry.
                    Mesh renderedMesh = i == 0 ? canvasRenderer.GetMesh()
                        : i < m_subTextObjects.Length && m_subTextObjects[i] ? m_subTextObjects[i].canvasRenderer.GetMesh() : null;
                    for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    {
                        var style = sdfLayers[layerIndex];
                        var layer = effectLayers[layerIndex * count + i];
                        layer.Configure(this, renderedMesh, source, effectShader,
                            style?.Color ?? Color.clear, style?.Width ?? 0, style?.Softness ?? 0,
                            style?.Offset ?? Vector2.zero, active && style != null && style.IsVisible);
                    }
                }
                // Group by style across every fallback material: the last list item is behind
                // the first. Every effect remains behind the complete native face hierarchy.
                int sibling = 0;
                for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
                    for (int i = 0; i < count; i++)
                        effectLayers[layerIndex * count + i].transform.SetSiblingIndex(sibling++);
                for (int i = count * layerCount; i < effectLayers.Count; i++) effectLayers[i].Clear();
                // Fallback layers may first appear after the Canvas clipping phase.
                if (CanvasUpdateRegistry.IsRebuildingGraphics())
                {
                    effectRoot.GetComponentsInParent(false, clipMasks);
                    foreach (var mask in clipMasks) if (mask.isActiveAndEnabled) mask.PerformClipping();
                }
            }
            finally { syncing = false; }
        }

        private void ClearEffects()
        {
            // TMP may clear text during a Canvas rebuild. Unbinding meshes is safe there;
            // disabling Graphics would unregister them from the active rebuild queue.
            foreach (var layer in effectLayers) if (layer) layer.Clear();
        }

        private SdfTextEffect LegacyLayer(SdfTextEffectRole role, bool create)
        {
            MigrateLayers();
            var layer = FindLegacyLayer(role);
            if (layer != null || !create) return layer;
            layer = CreateLegacyLayer(role);
            if (role == SdfTextEffectRole.Outline) sdfLayers.Insert(0, layer);
            else sdfLayers.Add(layer);
            return layer;
        }

        private SdfTextEffect FindLegacyLayer(SdfTextEffectRole role)
        {
            foreach (var layer in sdfLayers)
                if (layer != null && layer.LegacyRole == role) return layer;
            return null;
        }

        private SdfTextEffect CreateLegacyLayer(SdfTextEffectRole role)
        {
            return role == SdfTextEffectRole.Shadow
                ? new SdfTextEffect
                {
                    Enabled = sdfShadowEnabled, Width = sdfShadowSpread, Softness = sdfShadowBlur,
                    Color = sdfShadowColor, Offset = sdfShadowOffset, LegacyRole = role
                }
                : new SdfTextEffect
                {
                    Enabled = sdfOutlineEnabled, Width = sdfOutlineWidth, Softness = sdfOutlineSoftness,
                    Color = sdfOutlineColor, LegacyRole = role
                };
        }

        private void MigrateLayers()
        {
            sdfOutlineWidth = Positive(sdfOutlineWidth);
            sdfOutlineSoftness = Positive(sdfOutlineSoftness);
            sdfShadowBlur = Positive(sdfShadowBlur);
            sdfShadowSpread = Finite(sdfShadowSpread);
            sdfShadowOffset = new Vector2(Finite(sdfShadowOffset.x), Finite(sdfShadowOffset.y));
            if (sdfLayers == null) sdfLayers = new List<SdfTextEffect>();
            if (!sdfLayersMigrated)
            {
                if (sdfOutlines == null) sdfOutlines = new List<SdfTextEffect>();
                if (!sdfOutlinesMigrated && sdfOutlines.Count == 0)
                    sdfOutlines.Add(CreateLegacyLayer(SdfTextEffectRole.Outline));
                else if (sdfOutlinesMigrated && sdfOutlines.Count > 0 && sdfOutlines[0] != null)
                    ApplyLegacyOutlineSize(sdfOutlines[0]);
                if (sdfLayers.Count == 0)
                {
                    for (int i = 0; i < sdfOutlines.Count; i++)
                    {
                        var outline = sdfOutlines[i];
                        if (outline == null) { sdfLayers.Add(null); continue; }
                        sdfLayers.Add(new SdfTextEffect
                        {
                            Enabled = sdfOutlineEnabled && outline.Enabled, Width = outline.Width,
                            Softness = outline.Softness, Color = outline.Color, Offset = outline.Offset,
                            LegacyRole = i == 0 ? SdfTextEffectRole.Outline : SdfTextEffectRole.None
                        });
                    }
                    // An intentionally cleared outline list with no enabled shadow stays empty.
                    // Its hidden shadow settings remain available to the compatibility setters.
                    if (sdfOutlines.Count > 0 || sdfShadowEnabled)
                        sdfLayers.Add(CreateLegacyLayer(SdfTextEffectRole.Shadow));
                }
                sdfOutlinesMigrated = true;
                sdfLayersMigrated = true;
            }
            else
            {
                // Variants of a migrated base prefab can still override released scalar fields.
                // Apply only changed legacy channels, leaving new list edits and a cleared list intact.
                var outline = FindLegacyLayer(SdfTextEffectRole.Outline);
                if (outline != null)
                {
                    ApplyLegacyOutlineSize(outline);
                    if (sdfOutlineEnabled != sdfLegacyOutlineEnabled) outline.Enabled = sdfOutlineEnabled;
                }
                var shadow = FindLegacyLayer(SdfTextEffectRole.Shadow);
                if (shadow != null)
                {
                    if (sdfShadowEnabled != sdfLegacyShadowEnabled) shadow.Enabled = sdfShadowEnabled;
                    if (sdfShadowOffset.x != sdfLegacyShadowSize.x || sdfShadowOffset.y != sdfLegacyShadowSize.y)
                        shadow.Offset = new Vector2(
                            sdfShadowOffset.x != sdfLegacyShadowSize.x ? sdfShadowOffset.x : shadow.Offset.x,
                            sdfShadowOffset.y != sdfLegacyShadowSize.y ? sdfShadowOffset.y : shadow.Offset.y);
                    if (sdfShadowBlur != sdfLegacyShadowSize.z) shadow.Softness = sdfShadowBlur;
                    if (sdfShadowSpread != sdfLegacyShadowSize.w) shadow.Width = sdfShadowSpread;
                    if (!sdfShadowColor.Equals(sdfLegacyShadowColor)) shadow.Color = sdfShadowColor;
                }
            }
            sdfLegacyOutlineSize = new Vector2(sdfOutlineWidth, sdfOutlineSoftness);
            sdfLegacyOutlineColor = sdfOutlineColor;
            sdfLegacyOutlineEnabled = sdfOutlineEnabled;
            sdfLegacyShadowEnabled = sdfShadowEnabled;
            sdfLegacyShadowSize = new Vector4(sdfShadowOffset.x, sdfShadowOffset.y, sdfShadowBlur, sdfShadowSpread);
            sdfLegacyShadowColor = sdfShadowColor;
        }

        private void ApplyLegacyOutlineSize(SdfTextEffect outline)
        {
            if (sdfOutlineWidth != sdfLegacyOutlineSize.x) outline.Width = sdfOutlineWidth;
            if (sdfOutlineSoftness != sdfLegacyOutlineSize.y) outline.Softness = sdfOutlineSoftness;
            if (!sdfOutlineColor.Equals(sdfLegacyOutlineColor)) outline.Color = sdfOutlineColor;
        }

        private void EnsureRoot()
        {
            if (effectRoot) return;
            var root = new GameObject("SDF Text Effects", typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            effectRoot = (RectTransform)root.transform;
            effectRoot.SetParent(transform.parent, false);
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            effectGroup = root.GetComponent<CanvasGroup>();
            effectGroup.blocksRaycasts = false;
            effectGroup.interactable = false;
            // The sibling may have been destroyed together with an old parent.
            effectLayers.Clear();
        }

        private SdfTextLayer CreateLayer(string layerName)
        {
            var child = new GameObject(layerName, typeof(RectTransform), typeof(CanvasRenderer));
            child.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            child.transform.SetParent(effectRoot, false);
            return child.AddComponent<SdfTextLayer>();
        }

        private void SyncTransform()
        {
            if (effectRoot.parent != transform.parent) effectRoot.SetParent(transform.parent, false);
            var source = rectTransform;
            effectRoot.anchorMin = source.anchorMin;
            effectRoot.anchorMax = source.anchorMax;
            effectRoot.pivot = source.pivot;
            effectRoot.sizeDelta = source.sizeDelta;
            effectRoot.anchoredPosition3D = source.anchoredPosition3D;
            effectRoot.localRotation = source.localRotation;
            effectRoot.localScale = source.localScale;
            effectRoot.gameObject.layer = gameObject.layer;
            int sourceIndex = transform.GetSiblingIndex(), rootIndex = effectRoot.GetSiblingIndex();
            int targetIndex = rootIndex < sourceIndex ? sourceIndex - 1 : sourceIndex;
            if (rootIndex != targetIndex) effectRoot.SetSiblingIndex(targetIndex);

            GetComponents(ownGroups);
            float alpha = 1;
            bool ignoreParents = false;
            foreach (var group in ownGroups)
                if (group.isActiveAndEnabled) { alpha *= group.alpha; ignoreParents |= group.ignoreParentGroups; }
            effectGroup.alpha = alpha;
            effectGroup.ignoreParentGroups = ignoreParents;
        }

        internal static bool IsDistanceField(Material value) => value && value.HasProperty("_GradientScale")
            && value.HasProperty("_WeightNormal") && value.HasProperty("_MainTex");

        private static float AtlasPadding(Material value) => IsDistanceField(value)
            ? Mathf.Max(0, value.GetFloat("_GradientScale") - 1) : 0;

        internal static Material FaceOnly(Material source, ref Material instance, Material stencil = null)
        {
            if (!instance || instance.shader != source.shader)
            {
                Release(instance);
                instance = new Material(source) { name = "SDF Text Face", hideFlags = HideFlags.HideAndDontSave };
            }
            CopyRenderProperties(source, instance, stencil);
            instance.SetFloat("_OutlineWidth", 0);
            instance.SetFloat("_OutlineSoftness", 0);
            instance.DisableKeyword("OUTLINE_ON");
            instance.DisableKeyword("UNDERLAY_ON");
            instance.DisableKeyword("UNDERLAY_INNER");
            instance.DisableKeyword("GLOW_ON");
            return instance;
        }

        // uGUI caches stencil variants by material identity. Refresh their style/atlas while
        // retaining stencil state so material animation remains correct inside a Mask.
        internal static void CopyRenderProperties(Material source, Material destination, Material stencil)
        {
            if (!stencil || stencil == source) { destination.CopyPropertiesFromMaterial(source); return; }
            int comparison = stencil.GetInt("_StencilComp"), reference = stencil.GetInt("_Stencil");
            int operation = stencil.GetInt("_StencilOp"), read = stencil.GetInt("_StencilReadMask");
            int write = stencil.GetInt("_StencilWriteMask"), colorMask = stencil.GetInt("_ColorMask");
            bool alphaClip = stencil.IsKeywordEnabled("UNITY_UI_ALPHACLIP");
            destination.CopyPropertiesFromMaterial(source);
            destination.SetInt("_StencilComp", comparison);
            destination.SetInt("_Stencil", reference);
            destination.SetInt("_StencilOp", operation);
            destination.SetInt("_StencilReadMask", read);
            destination.SetInt("_StencilWriteMask", write);
            destination.SetInt("_ColorMask", colorMask);
            if (alphaClip) destination.EnableKeyword("UNITY_UI_ALPHACLIP");
        }

        internal static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        private static float Positive(float value) => Mathf.Max(0, Finite(value));
    }
}
