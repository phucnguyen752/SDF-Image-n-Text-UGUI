using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEditor.AssetImporters;
using Unity.Collections;

namespace SDFUI.Editor
{
    internal static class SdfTextureCompression
    {
        private static readonly MethodInfo DefaultFormat = typeof(TextureImporter).GetMethod("DefaultFormatFromTextureParameters",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(TextureImporterSettings), typeof(TextureImporterPlatformSettings), typeof(bool), typeof(bool), typeof(BuildTarget) }, null);

        internal static TextureFormat Format(TextureImporterPlatformSettings settings, BuildTarget target)
        {
            if (settings.format != TextureImporterFormat.Automatic) return (TextureFormat)settings.format;
            if (DefaultFormat == null) throw new NotSupportedException("Unity's automatic Sprite texture format resolver is unavailable.");
            var spriteSettings = new TextureImporterSettings();
            spriteSettings.ApplyTextureType(TextureImporterType.Sprite);
            var format = (TextureFormat)(TextureImporterFormat)DefaultFormat.Invoke(null, new object[] { spriteSettings, settings, true, false, target });
            // Unity's default-format helper resolves GPU formats, not their Crunch wrappers.
            if (settings.crunchedCompression && settings.textureCompression != TextureImporterCompression.Uncompressed)
            {
                if (target == BuildTarget.Android) return TextureFormat.ETC2_RGBA8Crunched;
                if (format == TextureFormat.BC7 || format == TextureFormat.DXT5) return TextureFormat.DXT5Crunched;
                if (format == TextureFormat.DXT1) return TextureFormat.DXT1Crunched;
                if (format == TextureFormat.ETC_RGB4) return TextureFormat.ETC_RGB4Crunched;
                if (format == TextureFormat.ETC2_RGBA8) return TextureFormat.ETC2_RGBA8Crunched;
            }
            return format;
        }

        internal static Vector2Int Size(int width, int height, TextureFormat format)
        {
            if (format == TextureFormat.PVRTC_RGB2 || format == TextureFormat.PVRTC_RGBA2
                || format == TextureFormat.PVRTC_RGB4 || format == TextureFormat.PVRTC_RGBA4)
            {
                int side = Mathf.Max(16, Mathf.NextPowerOfTwo(Mathf.Max(width, height)));
                if ((long)side * side > 16 * 1024 * 1024)
                    throw new InvalidOperationException("PVRTC square padding exceeds the SDF color texture budget. Reduce Max Size or choose another format.");
                return new Vector2Int(side, side);
            }
            int block = BlockSize(format);
            return new Vector2Int((width + block - 1) / block * block, (height + block - 1) / block * block);
        }

        internal static Texture2D Encode(Color32[] pixels, int width, int height,
            TextureImporterPlatformSettings platform, TextureFormat format, bool sRGB, Action<string> warning)
        {
            var generation = new TextureGenerationSettings(TextureImporterType.Sprite);
            generation.enablePostProcessor = false;
            generation.textureImporterSettings.readable = false;
            generation.textureImporterSettings.mipmapEnabled = false;
            generation.textureImporterSettings.sRGBTexture = sRGB;
            generation.textureImporterSettings.alphaIsTransparency = false; // RGB is already dilated by the SDF bake.
            generation.textureImporterSettings.filterMode = FilterMode.Bilinear;
            generation.sourceTextureInformation = new SourceTextureInformation { width = width, height = height, containsAlpha = true };
            generation.platformSettings = JsonUtility.FromJson<TextureImporterPlatformSettings>(JsonUtility.ToJson(platform));
            generation.platformSettings.overridden = true;
            generation.platformSettings.format = (TextureImporterFormat)format;
            // Max Size was applied before the distance bake. Do not rescale its padded color texture again.
            generation.platformSettings.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
            using (var buffer = new NativeArray<Color32>(pixels, Allocator.Temp))
            {
                var output = TextureGenerator.GenerateTexture(generation, buffer);
                if (output.thumbNail) UnityEngine.Object.DestroyImmediate(output.thumbNail);
                if (output.sprites != null)
                    foreach (var sprite in output.sprites) UnityEngine.Object.DestroyImmediate(sprite);
                if (output.importWarnings != null)
                    foreach (string message in output.importWarnings) warning(message);
                if (!output.texture) throw new InvalidOperationException("Unity could not encode the SDF color texture.");
                if (output.texture.width != width || output.texture.height != height)
                {
                    UnityEngine.Object.DestroyImmediate(output.texture);
                    throw new InvalidOperationException("The selected texture format resizes the SDF color data. Choose a different platform format.");
                }
                return output.texture;
            }
        }

        internal static Texture2D EncodeDistance(SdfBakeData data, TextureImporterPlatformSettings platform,
            BuildTarget target, Action<string> warning)
        {
            int sourceWidth = data.width + data.padding * 2, sourceHeight = data.height + data.padding * 2;
            int width = Mathf.NextPowerOfTwo(sourceWidth), height = Mathf.NextPowerOfTwo(sourceHeight);
            SdfDistanceTransform.ValidateDimensions(width, height);
            TextureFormat format = BuildPipeline.GetBuildTargetGroup(target) == BuildTargetGroup.Standalone
                ? TextureFormat.BC4
                : target == BuildTarget.Android || target == BuildTarget.iOS || target == BuildTarget.tvOS
                    ? TextureFormat.EAC_R : TextureFormat.R8;
            var generation = new TextureGenerationSettings(TextureImporterType.SingleChannel);
            generation.enablePostProcessor = false;
            var importer = generation.textureImporterSettings;
            importer.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.readable = false;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            generation.sourceTextureInformation = new SourceTextureInformation { width = width, height = height, containsAlpha = false };
            generation.platformSettings = JsonUtility.FromJson<TextureImporterPlatformSettings>(JsonUtility.ToJson(platform));
            generation.platformSettings.overridden = true;
            generation.platformSettings.format = (TextureImporterFormat)format;
            generation.platformSettings.maxTextureSize = Mathf.Max(width, height);
            generation.platformSettings.textureCompression = format == TextureFormat.R8
                ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            generation.platformSettings.crunchedCompression = false;
            generation.platformSettings.allowsAlphaSplitting = false;
            var pixels = new NativeArray<Color32>(width * height, Allocator.Temp);
            try
            {
                // Zero represents -range, safely outside the shape. Extend only right/top;
                // keep every original texel and the existing padding at exactly the same position.
                for (int y = 0; y < sourceHeight; y++)
                    for (int x = 0; x < sourceWidth; x++)
                    {
                        float distance = Mathf.HalfToFloat(data.distanceHalf[y * sourceWidth + x]);
                        byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(distance / (2 * data.range) + 0.5f) * 255);
                        pixels[y * width + x] = new Color32(value, value, value, 255);
                    }
                var output = TextureGenerator.GenerateTexture(generation, pixels);
                if (output.thumbNail) UnityEngine.Object.DestroyImmediate(output.thumbNail);
                if (output.importWarnings != null)
                    foreach (string message in output.importWarnings) warning(message);
                if (!output.texture) throw new InvalidOperationException("Unity could not encode the SDF distance texture.");
                if (output.texture.width != width || output.texture.height != height)
                {
                    UnityEngine.Object.DestroyImmediate(output.texture);
                    throw new InvalidOperationException("The distance encoder resized the SDF. Source texels must remain unchanged.");
                }
                return output.texture;
            }
            finally { pixels.Dispose(); }
        }

        private static int BlockSize(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1Crunched:
                case TextureFormat.DXT5Crunched:
                case TextureFormat.ETC_RGB4Crunched:
                case TextureFormat.ETC2_RGBA8Crunched: return 4;
                case TextureFormat.ASTC_5x5: return 5;
                case TextureFormat.ASTC_6x6: return 6;
                case TextureFormat.ASTC_8x8: return 8;
                case TextureFormat.ASTC_10x10: return 10;
                case TextureFormat.ASTC_12x12: return 12;
                default:
                    var graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(format, false);
                    return Mathf.Max(1, (int)UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(graphicsFormat));
            }
        }
    }
}
