using System.Collections.Generic;
using UnityEngine;

namespace SDFUI
{
    public sealed partial class SdfImage
    {
        // Keep in sync with the shader arrays. All layers composite before the Graphic's alpha.
        public const int MaxEffectLayers = 16;
        [SerializeField] private bool sdfEffectsEnabled = true;
        [SerializeField] private List<SdfImageEffect> sdfLayers = new List<SdfImageEffect>();
        [SerializeField, HideInInspector] private bool sdfLayersMigrated;
        [SerializeField, HideInInspector] private LegacyStyle sdfLegacyStyle;
        private readonly Vector4[] layerSizes = new Vector4[MaxEffectLayers];
        private readonly Vector4[] layerColors = new Vector4[MaxEffectLayers];
        private readonly Vector4[] layerModes = new Vector4[MaxEffectLayers];

        public bool EffectsEnabled { get => sdfEffectsEnabled; set { if (sdfEffectsEnabled == value) return; sdfEffectsEnabled = value; RefreshEffects(); } }
        /// <summary>Frontmost effect first, up to MaxEffectLayers. Call RefreshEffects after editing entries or order.</summary>
        public List<SdfImageEffect> Layers { get { MigrateLayers(); return sdfLayers; } }
        public void RefreshEffects()
        {
            MigrateLayers();
            foreach (var layer in sdfLayers) layer?.Sanitize();
            SetAllDirty();
        }

        // Released APIs follow their original layers after reordering. Removed layers stay removed
        // until a compatibility setter explicitly recreates one.
        public bool OutlineEnabled { get => LegacyLayer(1, false)?.Enabled ?? false; set { LegacyLayer(1, true).Enabled = value; RefreshEffects(); } }
        public bool ShadowEnabled { get => LegacyLayer(2, false)?.Enabled ?? false; set { LegacyLayer(2, true).Enabled = value; RefreshEffects(); } }
        public float OutlineWidth { get => LegacyLayer(1, false)?.Width ?? 0; set { LegacyLayer(1, true).Width = Positive(value); RefreshEffects(); } }
        public float OutlineSoftness { get => LegacyLayer(1, false)?.Softness ?? 0; set { LegacyLayer(1, true).Softness = value; RefreshEffects(); } }
        public Color OutlineColor { get => LegacyLayer(1, false)?.Color ?? Color.clear; set { LegacyLayer(1, true).Color = value; RefreshEffects(); } }
        public bool OutlineUseTextureColor { get => LegacyLayer(1, false)?.UseTextureColor ?? false; set { LegacyLayer(1, true).UseTextureColor = value; RefreshEffects(); } }
        public float OutlineTextureColorIntensity { get => LegacyLayer(1, false)?.TextureColorIntensity ?? 1; set { LegacyLayer(1, true).TextureColorIntensity = value; RefreshEffects(); } }
        public SdfOutlinePosition OutlinePosition { get => LegacyLayer(1, false)?.Position ?? SdfOutlinePosition.Outer; set { LegacyLayer(1, true).Position = ValidPosition(value); RefreshEffects(); } }
        public Vector2 OutlineOffset { get => LegacyLayer(1, false)?.Offset ?? Vector2.zero; set { LegacyLayer(1, true).Offset = value; RefreshEffects(); } }
        public Color ShadowColor { get => LegacyLayer(2, false)?.Color ?? Color.clear; set { LegacyLayer(2, true).Color = value; RefreshEffects(); } }
        public Vector2 ShadowOffset { get => LegacyLayer(2, false)?.Offset ?? Vector2.zero; set { LegacyLayer(2, true).Offset = value; RefreshEffects(); } }
        public float ShadowBlur { get => LegacyLayer(2, false)?.Softness ?? 0; set { LegacyLayer(2, true).Softness = value; RefreshEffects(); } }
        public float ShadowSpread { get => LegacyLayer(2, false)?.Width ?? 0; set { LegacyLayer(2, true).Width = value; RefreshEffects(); } }

        private bool HasEnabledLayers
        {
            get
            {
                if (!sdfEffectsEnabled) return false;
                var layers = Layers;
                for (int i = 0; i < Mathf.Min(layers.Count, MaxEffectLayers); i++)
                    if (layers[i] != null && layers[i].Enabled) return true;
                return false;
            }
        }

        private SdfImageEffect FindLegacyLayer(int role)
        {
            foreach (var layer in sdfLayers)
                if (layer != null && layer.LegacyRole == role) return layer;
            return null;
        }

        private SdfImageEffect LegacyLayer(int role, bool create)
        {
            MigrateLayers();
            var layer = FindLegacyLayer(role);
            if (layer != null || !create) return layer;
            layer = CreateLegacyLayer(role);
            // Keep an explicitly requested compatibility layer inside the rendered capacity.
            sdfLayers.Insert(Mathf.Min(role == 1 ? 0 : sdfLayers.Count, MaxEffectLayers - 1), layer);
            return layer;
        }

        private SdfImageEffect CreateLegacyLayer(int role) => role == 1
            ? new SdfImageEffect { Enabled = outlineEnabled, Width = outlineWidth, Softness = outlineSoftness,
                Color = outlineColor, Position = outlinePosition, UseTextureColor = outlineUseTextureColor,
                TextureColorIntensity = outlineTextureColorIntensity, LegacyRole = role }
            : new SdfImageEffect { Enabled = shadowEnabled, Width = shadowSpread, Softness = shadowBlur,
                Color = shadowColor, Offset = shadowOffset, Position = SdfOutlinePosition.Underlay, LegacyRole = role };

        [System.Serializable]
        private struct LegacyStyle
        {
            public bool outlineEnabled, shadowEnabled, textureColor;
            public Vector4 outline; // width, softness, texture intensity, position
            public Vector4 shadow; // offset x/y, blur, spread
            public Color outlineColor, shadowColor;
        }

        private void MigrateLayers()
        {
            sdfLayers ??= new List<SdfImageEffect>();
            if (!sdfLayersMigrated)
            {
                if (sdfLayers.Count == 0)
                {
                    sdfLayers.Add(CreateLegacyLayer(1));
                    sdfLayers.Add(CreateLegacyLayer(2));
                }
                sdfLayersMigrated = true;
            }
            else
            {
                // Preserve old animation bindings and prefab overrides without overwriting list edits.
                var outline = FindLegacyLayer(1);
                if (outline != null)
                {
                    if (outlineEnabled != sdfLegacyStyle.outlineEnabled) outline.Enabled = outlineEnabled;
                    if (outlineWidth != sdfLegacyStyle.outline.x) outline.Width = Positive(outlineWidth);
                    if (outlineSoftness != sdfLegacyStyle.outline.y) outline.Softness = outlineSoftness;
                    if (outlineTextureColorIntensity != sdfLegacyStyle.outline.z) outline.TextureColorIntensity = outlineTextureColorIntensity;
                    if ((float)outlinePosition != sdfLegacyStyle.outline.w) outline.Position = outlinePosition;
                    if (outlineUseTextureColor != sdfLegacyStyle.textureColor) outline.UseTextureColor = outlineUseTextureColor;
                    if (!outlineColor.Equals(sdfLegacyStyle.outlineColor)) outline.Color = outlineColor;
                }
                var shadow = FindLegacyLayer(2);
                if (shadow != null)
                {
                    if (shadowEnabled != sdfLegacyStyle.shadowEnabled) shadow.Enabled = shadowEnabled;
                    if (shadowOffset.x != sdfLegacyStyle.shadow.x || shadowOffset.y != sdfLegacyStyle.shadow.y)
                        shadow.Offset = new Vector2(shadowOffset.x != sdfLegacyStyle.shadow.x ? shadowOffset.x : shadow.Offset.x,
                            shadowOffset.y != sdfLegacyStyle.shadow.y ? shadowOffset.y : shadow.Offset.y);
                    if (shadowBlur != sdfLegacyStyle.shadow.z) shadow.Softness = shadowBlur;
                    if (shadowSpread != sdfLegacyStyle.shadow.w) shadow.Width = shadowSpread;
                    if (!shadowColor.Equals(sdfLegacyStyle.shadowColor)) shadow.Color = shadowColor;
                }
            }
            sdfLegacyStyle = new LegacyStyle
            {
                outlineEnabled = outlineEnabled, shadowEnabled = shadowEnabled, textureColor = outlineUseTextureColor,
                outline = new Vector4(outlineWidth, outlineSoftness, outlineTextureColorIntensity, (float)outlinePosition),
                shadow = new Vector4(shadowOffset.x, shadowOffset.y, shadowBlur, shadowSpread),
                outlineColor = outlineColor, shadowColor = shadowColor
            };
        }

        private float EffectBudget(Rect rect, Vector4 border) =>
            Mathf.Max(0, Mathf.Min(bakedSprite.Padding, bakedSprite.DistanceRange) - 2) * MinimumScale(rect, border);

        private static Vector4 LayerSettings(SdfImageEffect layer, float budget)
        {
            float spread = layer.Position == SdfOutlinePosition.Underlay
                ? Mathf.Clamp(Finite(layer.Width), -budget, budget) : Mathf.Min(Positive(layer.Width), budget);
            float softness = Mathf.Min(Positive(layer.Softness), Mathf.Max(0, budget - Mathf.Abs(spread)) * 2);
            return new Vector4(Finite(layer.Offset.x), Finite(layer.Offset.y), spread, softness);
        }

        private int PrepareLayers(Rect rect, Vector4 border)
        {
            int count = 0;
            float budget = EffectBudget(rect, border);
            var layers = Layers;
            if (sdfEffectsEnabled)
                for (int i = 0; i < Mathf.Min(layers.Count, MaxEffectLayers); i++)
                {
                    var layer = layers[i];
                    if (layer == null || !layer.IsVisible) continue;
                    layerSizes[count] = LayerSettings(layer, budget);
                    Color tint = SafeColor(layer.Color);
                    layerColors[count] = QualitySettings.activeColorSpace == ColorSpace.Linear ? tint.linear : tint;
                    layerModes[count] = new Vector4((float)layer.Position, layer.UseTextureColor ? 1 : 0,
                        Positive(layer.TextureColorIntensity), 0);
                    count++;
                }
            return count;
        }
    }
}
