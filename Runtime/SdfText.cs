using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SDFUI
{
    /// <summary>TMP text with effect layers below or above all glyph faces. Sizes use Canvas local units.</summary>
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
        private RectTransform aboveEffectRoot;
        private CanvasGroup aboveEffectGroup;
        // Unity hot reload must restore ownership together with the effect roots.
        private List<SdfTextLayer> effectLayers = new List<SdfTextLayer>();
        // Retain ownership of older mesh copies across an Editor hot reload.
        private List<Mesh> innerMeshes = new List<Mesh>();
        private readonly List<CanvasGroup> ownGroups = new List<CanvasGroup>();
        private readonly List<RectMask2D> clipMasks = new List<RectMask2D>();
        private Material faceSource, faceStencil, faceMaterial;
        private SdfTextMaterials.Entry faceEntry;
        private bool syncing;
        private bool generatingMesh;
        private bool effectsDirty = true;
        private bool effectsVisible;
        private GameObject cachedObject;
        private Matrix4x4 lastTransform;
        private Color lastRendererColor;
        private int lastLayer, lastSibling, lastMaterialRevision, lastComponentCount;
        private float lastGroupAlpha = 1;
        private bool lastIgnoreGroups;
        private int geometryVersion;
        private float lastLossyScaleY;
        private bool meshCleared;
        private readonly List<PaddingState> paddingCache = new List<PaddingState>();
        private bool paddingExtra, paddingBold;
        private static Shader effectShader;

        private struct PaddingState
        {
            internal Material source;
            internal int crc;
            internal float padding;
        }

        internal List<SdfTextEffect> RenderLayers => sdfLayers;

        public bool EffectsEnabled { get => sdfEffectsEnabled; set { if (sdfEffectsEnabled == value) return; sdfEffectsEnabled = value; RefreshEffects(); } }
        /// <summary>Frontmost effect first within each side of the text. Call RefreshEffects after editing.</summary>
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
            cachedObject = gameObject;
            SdfTextMaterials.WatchChanges();
            MigrateLayers();
            base.OnEnable();
            paddingCache.Clear();
            foreach (var mesh in innerMeshes) Release(mesh);
            innerMeshes.Clear();
            // Recover all owned graphics across hot reload, including older pool layouts.
            effectLayers.Clear();
            RecoverLayers(effectRoot);
            RecoverLayers(aboveEffectRoot);
            Canvas.preWillRenderCanvases += CheckEffects;
            Canvas.willRenderCanvases += SyncScale;
            geometryVersion++;
            RefreshEffects();
        }

        protected override void OnDisable()
        {
            Canvas.preWillRenderCanvases -= CheckEffects;
            Canvas.willRenderCanvases -= SyncScale;
            if (effectRoot) effectRoot.gameObject.SetActive(false);
            if (aboveEffectRoot) aboveEffectRoot.gameObject.SetActive(false);
            base.OnDisable();
            SdfTextMaterials.Release(ref faceEntry);
            faceMaterial = faceSource = faceStencil = null;
        }

        protected override void OnDestroy()
        {
            Canvas.preWillRenderCanvases -= CheckEffects;
            Canvas.willRenderCanvases -= SyncScale;
            DestroyRoot(ref effectRoot);
            DestroyRoot(ref aboveEffectRoot);
            foreach (var mesh in innerMeshes) Release(mesh);
            innerMeshes.Clear();
            SdfTextMaterials.Release(ref faceEntry);
            base.OnDestroy();
        }

        private void RecoverLayers(RectTransform root)
        {
            if (!root) return;
            for (int i = 0; i < root.childCount; i++)
            {
                var layer = root.GetChild(i).GetComponent<SdfTextLayer>();
                if (layer) effectLayers.Add(layer);
            }
            root.gameObject.SetActive(true);
        }

        private static void DestroyRoot(ref RectTransform effectRoot)
        {
            if (!effectRoot) return;
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

        public override void SetVerticesDirty()
        {
            effectsDirty = true;
            base.SetVerticesDirty();
        }

        public override void SetMaterialDirty()
        {
            effectsDirty = true;
            base.SetMaterialDirty();
        }

        protected override void OnCanvasGroupChanged()
        {
            base.OnCanvasGroupChanged();
            effectsDirty = true;
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            effectsDirty = true;
        }

        public override void RecalculateClipping()
        {
            effectsDirty = true;
            base.RecalculateClipping();
        }

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            Material modified = base.GetModifiedMaterial(baseMaterial);
            if (!isActiveAndEnabled || !EffectsSupported || !IsDistanceField(modified)) return modified;
            faceSource = baseMaterial;
            faceStencil = modified;
            return FaceOnly(faceSource, ref faceMaterial, ref faceEntry, faceStencil);
        }

        protected override void GenerateTextMesh()
        {
            meshCleared = false;
            bool propertiesChanged = m_havePropertiesChanged;
            RestoreNativePadding();
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
            generatingMesh = true;
            try { base.GenerateTextMesh(); }
            finally { generatingMesh = false; }
            geometryVersion++;
            lastLossyScaleY = rectTransform.lossyScale.y;
            SyncEffects();
        }

        private void RestoreNativePadding()
        {
            int count = Mathf.Max(1, m_subTextObjects.Length);
            bool changed = checkPaddingRequired || paddingCache.Count != count ||
                paddingExtra != m_enableExtraPadding || paddingBold != m_isUsingBold;
            for (int i = 0; !changed && i < count; i++)
            {
                Material source = i == 0 ? m_sharedMaterial : m_subTextObjects[i] ? m_subTextObjects[i].sharedMaterial : null;
                changed = paddingCache[i].source != source || paddingCache[i].crc != (source ? source.ComputeCRC() : 0);
            }
            if (changed)
            {
                // TMP reads shader keywords when calculating padding. Only do that when
                // the preset/bold/extra-padding inputs change, not on every counter update.
                base.UpdateMeshPadding();
                paddingCache.Clear();
                for (int i = 0; i < count; i++)
                {
                    Material source = i == 0 ? m_sharedMaterial : m_subTextObjects[i] ? m_subTextObjects[i].sharedMaterial : null;
                    paddingCache.Add(new PaddingState
                    {
                        source = source, crc = source ? source.ComputeCRC() : 0,
                        padding = i == 0 ? m_padding : m_subTextObjects[i] ? m_subTextObjects[i].padding : 0
                    });
                }
                paddingExtra = m_enableExtraPadding;
                paddingBold = m_isUsingBold;
            }
            else
            {
                m_padding = paddingCache[0].padding;
                for (int i = 1; i < count; i++)
                    if (m_subTextObjects[i]) m_subTextObjects[i].padding = paddingCache[i].padding;
            }
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
            geometryVersion++;
            meshCleared = false;
            SyncEffects();
        }

        public override void UpdateVertexData()
        {
            base.UpdateVertexData();
            geometryVersion++;
            meshCleared = false;
            SyncEffects();
        }

        public override void UpdateGeometry(Mesh mesh, int index)
        {
            base.UpdateGeometry(mesh, index);
            geometryVersion++;
            meshCleared = false;
            SyncEffects();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            if (effectRoot && transform.parent) effectRoot.SetParent(transform.parent, false);
            if (aboveEffectRoot && transform.parent) aboveEffectRoot.SetParent(transform.parent, false);
            RefreshEffects();
        }

        private void SyncEffects()
        {
            if (syncing || generatingMesh || !this) return;
            if (!cachedObject) cachedObject = gameObject;
            syncing = true;
            effectsDirty = false;
            try
            {
                // Keep render-only clones in sync with animated material properties.
                if (faceSource && faceMaterial) FaceOnly(faceSource, ref faceMaterial, ref faceEntry, faceStencil);
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
                effectsVisible = visible;
                if (!visible)
                {
                    ClearEffects();
                    return;
                }
                if (!effectShader) effectShader = Resources.Load<Shader>("SDFTextEffect");
                if (!effectShader) return;
                const AdditionalCanvasShaderChannels channels = AdditionalCanvasShaderChannels.TexCoord1 |
                    AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3 |
                    AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                if ((canvas.additionalShaderChannels & channels) != channels)
                    canvas.additionalShaderChannels |= channels;
                EnsureRoot(ref effectRoot, ref effectGroup, "SDF Text Effects");
                EnsureRoot(ref aboveEffectRoot, ref aboveEffectGroup, "SDF Text Effects (Above)");
                GetComponents(ownGroups);
                SyncTransform(effectRoot, effectGroup, false);
                SyncTransform(aboveEffectRoot, aboveEffectGroup, true);

                int count = textInfo.materialCount;
                int layerCount = sdfLayers.Count;
                if (count == 1)
                {
                    // A single atlas can merge all effects on each side without changing
                    // layer order. Multi-atlas text keeps ordered graphics across fonts.
                    int used = 0;
                    Mesh renderedMesh = canvasRenderer.GetMesh();
                    bool active = textInfo.meshInfo[0].vertexCount > 0 && IsDistanceField(fontSharedMaterial);
                    for (int side = 0; side < 2; side++)
                    {
                        bool above = side == 1, any = false;
                        foreach (var style in sdfLayers)
                            if (style != null && style.IsVisible && style.DrawAboveText == above) { any = true; break; }
                        if (!any) continue;
                        if (effectLayers.Count <= used) effectLayers.Add(CreateLayer("Effects"));
                        var layer = effectLayers[used++];
                        var root = above ? aboveEffectRoot : effectRoot;
                        if (layer.transform.parent != root) layer.transform.SetParent(root, false);
                        if (layer.transform.GetSiblingIndex() != 0) layer.transform.SetSiblingIndex(0);
                        layer.Configure(this, renderedMesh, fontSharedMaterial, effectShader,
                            null, true, above, active, geometryVersion);
                    }
                    for (int i = used; i < effectLayers.Count; i++) effectLayers[i].Clear();
                    SyncClipping();
                    return;
                }
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
                        var root = style != null && style.DrawAboveText ? aboveEffectRoot : effectRoot;
                        if (layer.transform.parent != root) layer.transform.SetParent(root, false);
                        layer.Configure(this, renderedMesh, source, effectShader, style, false,
                            style != null && style.DrawAboveText, active && style != null && style.IsVisible, geometryVersion);
                    }
                }
                // Keep list order across every fallback material, separately on each side
                // of the complete native face hierarchy. Index zero is frontmost in its group.
                int belowSibling = 0, aboveSibling = 0;
                for (int layerIndex = layerCount - 1; layerIndex >= 0; layerIndex--)
                    for (int i = 0; i < count; i++)
                    {
                        var layerTransform = effectLayers[layerIndex * count + i].transform;
                        int sibling = sdfLayers[layerIndex] != null && sdfLayers[layerIndex].DrawAboveText
                            ? aboveSibling++ : belowSibling++;
                        if (layerTransform.GetSiblingIndex() != sibling) layerTransform.SetSiblingIndex(sibling);
                    }
                for (int i = count * layerCount; i < effectLayers.Count; i++) effectLayers[i].Clear();
                SyncClipping();
            }
            finally
            {
                lastMaterialRevision = SdfTextMaterials.Revision;
                lastComponentCount = cachedObject.GetComponentCount();
                if (effectsVisible)
                {
                    lastTransform = rectTransform.localToWorldMatrix;
                    lastRendererColor = canvasRenderer.GetColor();
                    lastLayer = cachedObject.layer;
                    lastSibling = transform.GetSiblingIndex();
                }
                syncing = false;
            }
        }

        private void CheckEffects()
        {
            if (syncing || generatingMesh || !isActiveAndEnabled) return;
            // A component count check catches masks / CanvasGroups added at runtime
            // without repeating GetComponent searches for every unchanged label.
            if (effectsDirty || lastComponentCount != cachedObject.GetComponentCount()) { SyncEffects(); return; }
            if (lastMaterialRevision != SdfTextMaterials.Revision)
            {
                if (faceEntry != null && !faceEntry.material)
                {
                    // Entering Play Mode with domain/scene reload disabled can reset the
                    // shared cache while these labels and their native renderers survive.
                    RefreshEffects();
                    for (int i = 1; i < m_subTextObjects.Length; i++)
                        if (m_subTextObjects[i]) m_subTextObjects[i].SetMaterialDirty();
                    SyncEffects();
                    return;
                }
                foreach (var layer in effectLayers) if (layer) layer.RefreshSharedMaterial();
                lastMaterialRevision = SdfTextMaterials.Revision;
            }
            if (!effectsVisible) return;
            if (!effectRoot || !aboveEffectRoot || !lastTransform.Equals(rectTransform.localToWorldMatrix) ||
                lastLayer != cachedObject.layer || lastSibling != rectTransform.GetSiblingIndex() ||
                effectRoot.GetSiblingIndex() != lastSibling - 1 || aboveEffectRoot.GetSiblingIndex() != lastSibling + 1)
            {
                SyncEffects();
                return;
            }
            Color rendererColor = canvasRenderer.GetColor();
            if (!lastRendererColor.Equals(rendererColor))
            {
                foreach (var layer in effectLayers) if (layer) layer.SyncColor(rendererColor);
                lastRendererColor = rendererColor;
            }
            SyncGroups();
        }

        private void SyncGroups()
        {
            float alpha = 1;
            bool ignoreParents = false;
            foreach (var group in ownGroups)
                if (group && group.isActiveAndEnabled) { alpha *= group.alpha; ignoreParents |= group.ignoreParentGroups; }
            if (lastGroupAlpha == alpha && lastIgnoreGroups == ignoreParents) return;
            lastGroupAlpha = alpha;
            lastIgnoreGroups = ignoreParents;
            if (effectGroup) { effectGroup.alpha = alpha; effectGroup.ignoreParentGroups = ignoreParents; }
            if (aboveEffectGroup) { aboveEffectGroup.alpha = alpha; aboveEffectGroup.ignoreParentGroups = ignoreParents; }
        }

        private void SyncClipping()
        {
            // Fallback layers may first appear after the Canvas clipping phase.
            if (!CanvasUpdateRegistry.IsRebuildingGraphics()) return;
            effectRoot.GetComponentsInParent(false, clipMasks);
            foreach (var mask in clipMasks) if (mask.isActiveAndEnabled) mask.PerformClipping();
        }

        private void ClearEffects()
        {
            // TMP may clear text during a Canvas rebuild. Unbinding meshes is safe there;
            // disabling Graphics would unregister them from the active rebuild queue.
            foreach (var layer in effectLayers) if (layer) layer.Clear();
        }

        private void SyncScale()
        {
            // TMP can update UV0.w directly during willRenderCanvases, without calling
            // UpdateGeometry. Upload that scale change after TMP's own callback has run.
            float scale = rectTransform.lossyScale.y;
            if (lastLossyScaleY == scale) return;
            lastLossyScaleY = scale;
            geometryVersion++;
            SyncEffects();
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

        private void EnsureRoot(ref RectTransform effectRoot, ref CanvasGroup effectGroup, string name)
        {
            if (effectRoot) return;
            var root = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasGroup));
            root.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            effectRoot = (RectTransform)root.transform;
            effectRoot.SetParent(transform.parent, false);
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            effectGroup = root.GetComponent<CanvasGroup>();
            effectGroup.blocksRaycasts = false;
            effectGroup.interactable = false;
            // The sibling may have been destroyed together with an old parent.
            effectLayers.RemoveAll(layer => !layer);
        }

        private SdfTextLayer CreateLayer(string layerName)
        {
            var child = new GameObject(layerName, typeof(RectTransform), typeof(CanvasRenderer));
            child.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            child.transform.SetParent(effectRoot, false);
            return child.AddComponent<SdfTextLayer>();
        }

        private void SyncTransform(RectTransform effectRoot, CanvasGroup effectGroup, bool aboveText)
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
            int targetIndex = aboveText
                ? (rootIndex < sourceIndex ? sourceIndex : sourceIndex + 1)
                : (rootIndex < sourceIndex ? sourceIndex - 1 : sourceIndex);
            if (rootIndex != targetIndex) effectRoot.SetSiblingIndex(targetIndex);

            float alpha = 1;
            bool ignoreParents = false;
            foreach (var group in ownGroups)
                if (group.isActiveAndEnabled) { alpha *= group.alpha; ignoreParents |= group.ignoreParentGroups; }
            effectGroup.alpha = alpha;
            effectGroup.ignoreParentGroups = ignoreParents;
            effectRoot.gameObject.SetActive(true);
        }

        internal static bool IsDistanceField(Material value) => value && value.HasProperty("_GradientScale")
            && value.HasProperty("_WeightNormal") && value.HasProperty("_MainTex");

        private static float AtlasPadding(Material value) => IsDistanceField(value)
            ? Mathf.Max(0, value.GetFloat("_GradientScale") - 1) : 0;

        internal static Material FaceOnly(Material source, ref Material instance, ref SdfTextMaterials.Entry entry, Material stencil = null)
        {
            // Release an owned copy left by a hot reload from the older implementation.
            if (entry == null && instance) Release(instance);
            instance = SdfTextMaterials.Face(source, stencil, ref entry);
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
