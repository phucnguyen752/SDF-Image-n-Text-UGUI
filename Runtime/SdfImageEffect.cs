using System;
using UnityEngine;

namespace SDFUI
{
    /// <summary>One image effect. Sizes and offsets use Canvas local units.</summary>
    [Serializable]
    public sealed class SdfImageEffect
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private float width = 2;
        [SerializeField, Min(0)] private float softness;
        [SerializeField] private Color color = UnityEngine.Color.black;
        [SerializeField] private Vector2 offset;
        [SerializeField] private SdfOutlinePosition position;
        [SerializeField] private bool useTextureColor;
        [SerializeField, Min(0)] private float textureColorIntensity = 1;
        [SerializeField, HideInInspector] private int legacyRole;

        public bool Enabled { get => enabled; set => enabled = value; }
        public float Width { get => width; set => width = Finite(value); }
        public float Spread { get => Width; set => Width = value; }
        public float Softness { get => softness; set => softness = Mathf.Max(0, Finite(value)); }
        public Color Color { get => color; set => color = new Color(Channel(value.r), Channel(value.g), Channel(value.b), Channel(value.a)); }
        public Vector2 Offset { get => offset; set => offset = new Vector2(Finite(value.x), Finite(value.y)); }
        public SdfOutlinePosition Position { get => position; set => position = value >= SdfOutlinePosition.Outer && value <= SdfOutlinePosition.Underlay ? value : SdfOutlinePosition.Outer; }
        public bool UseTextureColor { get => useTextureColor; set => useTextureColor = value; }
        public float TextureColorIntensity { get => textureColorIntensity; set => textureColorIntensity = Mathf.Max(0, Finite(value)); }

        internal int LegacyRole { get => legacyRole; set => legacyRole = value; }
        internal bool IsVisible => enabled && color.a > 0 && (position == SdfOutlinePosition.Underlay || width > 0);
        internal void Sanitize()
        {
            Width = width;
            Softness = softness;
            Color = color;
            Offset = offset;
            Position = position;
            TextureColorIntensity = textureColorIntensity;
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value, -100000, 100000);
        private static float Channel(float value) => Mathf.Clamp01(Finite(value));
    }
}
