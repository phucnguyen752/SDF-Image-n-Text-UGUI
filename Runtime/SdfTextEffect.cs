using System;
using UnityEngine;

namespace SDFUI
{
    /// <summary>One text effect, with signed spread, softness and offset in Canvas local units.</summary>
    [Serializable]
    public sealed class SdfTextEffect
    {
        [SerializeField] private bool enabled = true;
        // Existing text layers are filled silhouettes; retain that look when loading older data.
        [SerializeField] private SdfOutlinePosition position = SdfOutlinePosition.Underlay;
        [SerializeField] private SdfTextUnderlayType underlayType = SdfTextUnderlayType.Normal;
        [SerializeField] private float width = 2;
        [SerializeField, Min(0)] private float softness;
        [SerializeField] private Color color = UnityEngine.Color.black;
        [SerializeField] private Vector2 offset;
        [SerializeField, HideInInspector] private SdfTextEffectRole legacyRole;

        public bool Enabled { get => enabled; set => enabled = value; }
        public SdfOutlinePosition Position { get => position; set => position = value >= SdfOutlinePosition.Outer && value <= SdfOutlinePosition.Underlay ? value : SdfOutlinePosition.Underlay; }
        public SdfTextUnderlayType UnderlayType { get => underlayType; set => underlayType = value == SdfTextUnderlayType.Inner ? value : SdfTextUnderlayType.Normal; }
        public float Width { get => width; set => width = Finite(value); }
        public float Spread { get => Width; set => Width = value; }
        public float Softness { get => softness; set => softness = Positive(value); }
        public Color Color { get => color; set => color = value; }
        public Vector2 Offset { get => offset; set => offset = new Vector2(Finite(value.x), Finite(value.y)); }

        internal SdfTextEffectRole LegacyRole { get => legacyRole; set => legacyRole = value; }
        internal bool IsInner => position == SdfOutlinePosition.Inner ||
            (position == SdfOutlinePosition.Underlay && underlayType == SdfTextUnderlayType.Inner);
        internal bool DrawAboveText => IsInner || position == SdfOutlinePosition.Center;
        internal bool HasInnerOffset => IsInner && (offset.x != 0 || offset.y != 0);
        internal bool IsVisible => enabled && color.a > 0 && (position == SdfOutlinePosition.Underlay || width > 0);

        internal void Sanitize()
        {
            width = Finite(width);
            softness = Positive(softness);
            Offset = offset;
            Position = position;
            UnderlayType = underlayType;
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        private static float Positive(float value) => Mathf.Max(0, Finite(value));
    }

    public enum SdfTextUnderlayType { Normal, Inner }

    internal enum SdfTextEffectRole { None, Outline, Shadow }
}
