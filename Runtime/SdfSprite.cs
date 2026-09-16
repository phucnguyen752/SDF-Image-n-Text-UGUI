using UnityEngine;

namespace SDFUI
{
    /// <summary>A sprite and its padded, signed distance field baked by the Editor.</summary>
    public sealed class SdfSprite : ScriptableObject
    {
#if UNITY_EDITOR
        // Editor rebake metadata; runtime lookup uses the descriptor attached to Image.sprite.
        [SerializeField] private Sprite sourceSprite;
        [SerializeField] private string bakeFingerprint;
#endif
        [SerializeField] private Texture2D colorTexture;
        [SerializeField] private Texture2D distanceTexture;
        [SerializeField] private bool normalizedDistance;
        [SerializeField] private Vector2Int sourceSize;
        [SerializeField] private Vector2 nativeSize;
        [SerializeField] private Vector4 border;
        [SerializeField] private Vector2 pivot;
        [SerializeField] private float pixelsPerUnit = 100;
        [SerializeField] private int padding;
        [SerializeField] private float distanceRange;
        [SerializeField] private float alphaThreshold = 0.5f;

        /// <summary>Original rebake source in the Editor; null in players, which use the baked textures.</summary>
        public Sprite SourceSprite
        {
            get
            {
#if UNITY_EDITOR
                return sourceSprite;
#else
                return null;
#endif
            }
        }

        /// <summary>Editor identity of the cached bake generation attached to the source Sprite.</summary>
        public string BakeFingerprint
        {
            get
            {
#if UNITY_EDITOR
                return bakeFingerprint;
#else
                return string.Empty;
#endif
            }
        }
        public Texture2D ColorTexture => colorTexture;
        public Texture2D DistanceTexture => distanceTexture;
        public bool NormalizedDistance => normalizedDistance;
        /// <summary>Converts a distance texture sample into signed source pixels, including older RHalf bakes.</summary>
        public Vector2 DistanceDecode => normalizedDistance ? new Vector2(2 * distanceRange, -distanceRange) : new Vector2(1, 0);
        public Vector2Int SourceSize => sourceSize;
        /// <summary>Original sprite size in sprite units, before applying Canvas reference pixels per unit.</summary>
        public Vector2 NativeSize => nativeSize.x > 0 && nativeSize.y > 0
            && !float.IsInfinity(nativeSize.x) && !float.IsInfinity(nativeSize.y)
            ? nativeSize : (Vector2)sourceSize / pixelsPerUnit;
        public Vector4 Border => border;
        public Vector2 Pivot => pivot;
        public float PixelsPerUnit => pixelsPerUnit;
        public int Padding => padding;
        /// <summary>Maximum encoded distance in source pixels; positive values are inside.</summary>
        public float DistanceRange => distanceRange;
        public float AlphaThreshold => alphaThreshold;

        /// <summary>Finds valid baked data attached to a Unity Sprite by the texture importer.</summary>
        public static SdfSprite FromSprite(Sprite source)
        {
            if (!source) return null;
            var count = source.GetScriptableObjectsCount();
            if (count == 0) return null;
            // Called on source changes or enable/import, not each frame. Avoid a cache retaining source assets.
            var attached = new ScriptableObject[count];
            var retrieved = source.GetScriptableObjects(attached);
            for (var i = 0; i < retrieved; i++)
                if (attached[i] is SdfSprite baked && baked.IsValid)
                    return baked;
            return null;
        }

        public bool IsValid
        {
            get
            {
                if (!colorTexture || !distanceTexture || sourceSize.x <= 0 || sourceSize.y <= 0
                    || padding < 0 || !(distanceRange > 0) || float.IsInfinity(distanceRange)
                    || !(pixelsPerUnit > 0) || float.IsInfinity(pixelsPerUnit)) return false;
                int width = sourceSize.x + padding * 2, height = sourceSize.y + padding * 2;
                int square = Mathf.Max(16, Mathf.NextPowerOfTwo(Mathf.Max(width, height)));
                return distanceTexture.width == (normalizedDistance ? Mathf.NextPowerOfTwo(width) : width)
                    && distanceTexture.height == (normalizedDistance ? Mathf.NextPowerOfTwo(height) : height)
                    // Each texture has independent storage padding, without changing the sprite's domain.
                    && colorTexture.width >= width && colorTexture.height >= height
                    && colorTexture.width <= Mathf.Max(width + 11, square)
                    && colorTexture.height <= Mathf.Max(height + 11, square);
            }
        }

#if UNITY_EDITOR
        public void Initialize(Sprite source, Texture2D color, Texture2D distance, Vector2Int size,
            Vector4 sourceBorder, Vector2 normalizedPivot, float ppu, int texturePadding, float range,
            float threshold = 0.5f, string fingerprint = "", bool normalized = false)
        {
            sourceSprite = source;
            bakeFingerprint = fingerprint;
            colorTexture = color;
            distanceTexture = distance;
            normalizedDistance = normalized;
            sourceSize = size;
            nativeSize = source ? source.rect.size / source.pixelsPerUnit : (Vector2)size / ppu;
            border = sourceBorder;
            pivot = normalizedPivot;
            pixelsPerUnit = ppu;
            padding = texturePadding;
            distanceRange = range;
            alphaThreshold = threshold;
        }
#endif
    }
}
