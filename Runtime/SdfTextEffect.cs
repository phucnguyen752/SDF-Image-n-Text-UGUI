using System;
using UnityEngine;

namespace SDFUI
{
    /// <summary>One text effect, with signed spread, softness and offset in Canvas local units.</summary>
    [Serializable]
    public sealed class SdfTextEffect
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private float width = 2;
        [SerializeField, Min(0)] private float softness;
        [SerializeField] private Color color = UnityEngine.Color.black;
        [SerializeField] private Vector2 offset;
        [SerializeField, HideInInspector] private SdfTextEffectRole legacyRole;

        public bool Enabled { get => enabled; set => enabled = value; }
        public float Width { get => width; set => width = Finite(value); }
        public float Spread { get => Width; set => Width = value; }
        public float Softness { get => softness; set => softness = Positive(value); }
        public Color Color { get => color; set => color = value; }
        public Vector2 Offset { get => offset; set => offset = new Vector2(Finite(value.x), Finite(value.y)); }

        internal SdfTextEffectRole LegacyRole { get => legacyRole; set => legacyRole = value; }
        internal bool IsVisible => enabled && color.a > 0;

        internal void Sanitize()
        {
            width = Finite(width);
            softness = Positive(softness);
            Offset = offset;
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
        private static float Positive(float value) => Mathf.Max(0, Finite(value));
    }

    internal enum SdfTextEffectRole { None, Outline, Shadow }
}
