using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using SDFUI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    /// <summary>Actual GPU rendering in a disposable scene, including uGUI clipping and compositing.</summary>
    public sealed class SdfRenderingTests
    {
        private const int Resolution = 128;
        private string folder;
        private string sourcePath;
        private Scene previewScene;
        private Camera camera;
        private Canvas canvas;
        private RenderTexture target;
        private SdfSprite sprite;
        private Texture2D editedColor;
        private SdfSprite editedSprite;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("GPU rendering is unavailable on the Null graphics device.");

            folder = "Assets/SDFImageRenderTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            sprite = null;
            sourcePath = CreateSquareSource();
            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            settings.maxSize = 512;
            settings.padding = 16;
            settings.range = 16;
            SdfTextureSettings.Set(sourcePath, settings);
            double deadline = EditorApplication.timeSinceStartup + 30;
            while (!sprite && EditorApplication.timeSinceStartup < deadline)
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                    if (asset is SdfSprite embedded && embedded.IsValid)
                        sprite = embedded;
                if (!sprite)
                    yield return null;
            }
            Assert.That(sprite, Is.Not.Null, "Render fixture auto-bake did not finish: " + SdfBakeQueue.GetStatus(sourcePath));

            previewScene = EditorSceneManager.NewPreviewScene();
            var cameraObject = NewObject("SDF Render Test Camera", typeof(Camera));
            camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.scene = previewScene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(previewScene);
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = Resolution * 0.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = false;

            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "SDF Test Render",
                antiAliasing = 1,
                hideFlags = HideFlags.HideAndDontSave
            };
            target.Create();
            camera.targetTexture = target;

            var canvasObject = NewObject("SDF Render Test Canvas", typeof(RectTransform), typeof(Canvas));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.referencePixelsPerUnit = 100;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Resolution, Resolution);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(sourcePath))
                SdfBakeQueue.Cancel(sourcePath);
            if (camera)
                camera.targetTexture = null;
            if (target)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
            if (previewScene.IsValid())
                EditorSceneManager.ClosePreviewScene(previewScene);
            if (editedSprite) Object.DestroyImmediate(editedSprite);
            if (editedColor) Object.DestroyImmediate(editedColor);
            if (!string.IsNullOrEmpty(folder))
                AssetDatabase.DeleteAsset(folder);
        }

        [UnityTest]
        public IEnumerator BlockPadding_KeepsColorAlignedWithDistanceInSimpleAndSlicedImages()
        {
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.padding = 9;
            settings.range = 9;
            SdfTextureSettings.Set(sourcePath, settings);
            double deadline = EditorApplication.timeSinceStartup + 30;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                sprite = SdfSprite.FromSprite(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
                if (sprite && sprite.Padding == 9) break;
                yield return null;
            }
            Assert.That(sprite.Padding, Is.EqualTo(9));
            Assert.That(sprite.DistanceTexture.width, Is.EqualTo(64));
            SdfImage image = CreateImage(canvas.transform);
            image.OutlineWidth = 5;
            image.OutlineColor = Color.red;
            foreach (SdfImageType type in new[] { SdfImageType.Simple, SdfImageType.Sliced })
            {
                sprite.Initialize(sprite.SourceSprite, sprite.ColorTexture, sprite.DistanceTexture, sprite.SourceSize,
                    type == SdfImageType.Sliced ? new Vector4(10, 10, 10, 10) : Vector4.zero,
                    sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, sprite.AlphaThreshold, normalized: sprite.NormalizedDistance);
                image.Type = type;
                image.rectTransform.sizeDelta = type == SdfImageType.Sliced ? new Vector2(76, 60) : new Vector2(64, 64);
                image.RefreshSdf();
                Color[] compressed = Render();
                Texture2D compressedColor = sprite.ColorTexture;
                Texture2D copy = SdfTestTextureReadback.Copy(compressedColor);
                var exactSize = new Texture2D(50, 50, TextureFormat.RGBA32, false, true);
                try
                {
                    exactSize.SetPixels(copy.GetPixels(0, 0, 50, 50));
                    exactSize.Apply(false, false);
                    sprite.Initialize(sprite.SourceSprite, exactSize, sprite.DistanceTexture, sprite.SourceSize,
                        sprite.Border, sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, sprite.AlphaThreshold, normalized: sprite.NormalizedDistance);
                    image.RefreshSdf();
                    Color[] reference = Render();
                    float maxError = 0;
                    for (int i = 0; i < reference.Length; i++)
                        maxError = Mathf.Max(maxError, Mathf.Abs(reference[i].g - compressed[i].g));
                    Assert.That(maxError, Is.LessThan(0.015f), "Block alignment must not shift or stretch the artwork.");
                }
                finally
                {
                    sprite.Initialize(sprite.SourceSprite, compressedColor, sprite.DistanceTexture, sprite.SourceSize,
                        sprite.Border, sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, sprite.AlphaThreshold, normalized: sprite.NormalizedDistance);
                    Object.DestroyImmediate(exactSize);
                    Object.DestroyImmediate(copy);
                }
            }
        }

        [Test]
        public void OuterAndInnerOutlines_AppearOnTheirRespectiveSidesOfTheSourceEdge()
        {
            SdfImage image = CreateImage(canvas.transform);
            image.OutlineWidth = 6;
            image.OutlineColor = Color.red;
            image.OutlinePosition = SdfOutlinePosition.Outer;
            Color[] outer = Render();
            AssertGreen(Average(outer, -6, -6, 12, 12), "Outer outline preserves the green center.");
            AssertRed(Average(outer, 18, -6, 3, 12), "Outer outline must render beyond the source's x=16 edge.");
            AssertGreen(Average(outer, 11, -6, 3, 12), "Outer outline does not paint inside the source.");

            image.OutlinePosition = SdfOutlinePosition.Inner;
            Color[] inner = Render();
            AssertGreen(Average(inner, -6, -6, 12, 12), "Inner outline preserves the center.");
            AssertRed(Average(inner, 11, -6, 3, 12), "Inner outline paints inside the source edge.");
            Assert.That(Average(inner, 18, -6, 3, 12).a, Is.LessThan(0.05f),
                "Inner outline must not leave a colored exterior ring.");
        }

        [UnityTest]
        public IEnumerator TextureColoredOutline_FollowsBakedEdgeColorsAndScalesOnlyTheOutline()
        {
            string previousFingerprint = sprite.BakeFingerprint;
            sourcePath = CreateSquareSource(splitColors: true);
            double deadline = EditorApplication.timeSinceStartup + 30;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                sprite = SdfSprite.FromSprite(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
                if (sprite && sprite.BakeFingerprint != previousFingerprint) break;
                yield return null;
            }
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sprite.BakeFingerprint, Is.Not.EqualTo(previousFingerprint), "The multicolor source must finish baking.");
            SdfImage image = CreateImage(canvas.transform);
            Color[] sourceOnly = Render();
            Color leftColor = Average(sourceOnly, -10, -4, 4, 8);
            Color rightColor = Average(sourceOnly, 6, -4, 4, 8);
            Assert.That(leftColor.r, Is.GreaterThan(leftColor.b * 2));
            Assert.That(rightColor.b, Is.GreaterThan(rightColor.r * 2));

            image.OutlineWidth = 6;
            image.OutlineColor = Color.black;
            Color[] solid = Render();
            image.OutlineUseTextureColor = true;
            foreach (SdfOutlinePosition position in new[] { SdfOutlinePosition.Outer, SdfOutlinePosition.Inner, SdfOutlinePosition.Center })
            {
                image.OutlinePosition = position;
                int sampleX = position == SdfOutlinePosition.Outer ? 19 : position == SdfOutlinePosition.Inner ? 12 : 17;
                foreach (float intensity in new[] { 0f, 0.5f, 1f, 2f })
                {
                    image.OutlineTextureColorIntensity = intensity;
                    Color[] rendered = Render();
                    AssertColor(Average(rendered, -sampleX - 1, -4, 1, 8),
                        new Color(leftColor.r * intensity, leftColor.g * intensity, leftColor.b * intensity, 1),
                        position + " left outline must extend the left source color, even where texture alpha is zero.");
                    AssertColor(Average(rendered, sampleX, -4, 1, 8),
                        new Color(rightColor.r * intensity, rightColor.g * intensity, rightColor.b * intensity, 1),
                        position + " right outline must extend the right source color.");
                    AssertColor(Average(rendered, -6, -4, 2, 8), Average(sourceOnly, -6, -4, 2, 8), "Intensity must not change the face.");
                    if (position == SdfOutlinePosition.Outer && intensity == 1)
                        SaveCapture("outline-texture-color.png", rendered);
                }
            }

            image.OutlinePosition = SdfOutlinePosition.Outer;
            image.OutlineUseTextureColor = false;
            Color[] restored = Render();
            Assert.That(restored, Is.EqualTo(solid), "Turning texture color off must restore the solid-color rendering.");
            SaveCapture("outline-solid-color.png", restored);
        }

        [Test]
        public void TextureColoredOutline_PreservesOpacityTintAndClippingOnSlicedImages()
        {
            sprite.Initialize(sprite.SourceSprite, sprite.ColorTexture, sprite.DistanceTexture, sprite.SourceSize,
                new Vector4(4, 4, 4, 4), sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, normalized: sprite.NormalizedDistance);
            RectTransform clip = CreateMask("Texture Outline Clip", canvas.transform, new Vector2(100, 16), Vector2.zero, false);
            clip.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
            SdfImage image = CreateImage(clip);
            image.Type = SdfImageType.Sliced;
            image.color = new Color(0.2f, 0.4f, 0.6f, 0.5f);
            Color faceBefore = Average(Render(), -4, -4, 8, 8);
            image.OutlineWidth = 6;
            image.OutlineColor = new Color(1, 0, 1, 0.5f);
            image.OutlineUseTextureColor = true;
            image.OutlineTextureColorIntensity = 0.5f;
            Color[] dimmed = Render();
            AssertColor(Average(dimmed, 21, -4, 2, 8), new Color(0, 0.05f, 0, 0.1f),
                "Texture RGB ignores Image/Outline RGB tint, while outline opacity and CanvasGroup alpha apply once.");
            AssertColor(Average(dimmed, -4, -4, 8, 8), faceBefore, "The tinted face must stay unchanged.");
            Assert.That(Average(dimmed, 21, 11, 2, 3).a, Is.LessThan(0.01f), "RectMask2D must clip the colored outline.");

            image.OutlineTextureColorIntensity = 2;
            Color[] brightened = Render();
            AssertColor(Average(brightened, 21, -4, 2, 8), new Color(0, 0.2f, 0, 0.1f),
                "Runtime intensity changes must increase only RGB, including on the existing clipped material.");
        }

        [TestCase(SdfOutlinePosition.Outer)]
        [TestCase(SdfOutlinePosition.Center)]
        public void WhiteOutline_WithSourceAntialiasingDifferentFromSdfCoverage_HasNoDarkJoin(SdfOutlinePosition position)
        {
            PrepareWhiteAntialiasedSource();
            SdfImage image = CreateImage(canvas.transform);
            image.OutlineWidth = 6;
            image.OutlineColor = Color.white;
            image.OutlinePosition = position;

            camera.backgroundColor = Color.black;
            Color[] onBlack = Render();
            Color rightJoin = Average(onBlack, 15, -6, 1, 12);
            Color leftJoin = Average(onBlack, -16, -6, 1, 12);
            Assert.That(Mathf.Min(rightJoin.r, rightJoin.g, rightJoin.b), Is.GreaterThan(0.98f),
                "A white source and white outline must not reveal a dark seam inside their combined coverage.");
            Assert.That(Mathf.Min(leftJoin.r, leftJoin.g, leftJoin.b), Is.GreaterThan(0.98f));

            // A transparent black target exposes composite alpha independently
            // from an opaque background, which would always report alpha=1.
            camera.backgroundColor = Color.clear;
            Color[] transparent = Render();
            Assert.That(Average(transparent, 15, -6, 1, 12).a, Is.GreaterThan(0.98f));
            Assert.That(Average(transparent, -16, -6, 1, 12).a, Is.GreaterThan(0.98f));
            Color interior = Average(transparent, -2, -2, 4, 4);
            Assert.That(interior.a, Is.EqualTo(128f / 255).Within(0.015f),
                "Repairing the source edge must not fill intentional transparency deep inside the artwork.");
            Assert.That(interior.r, Is.EqualTo(128f / 255).Within(0.015f));
        }

        [TestCase(0f, 1f)]
        [TestCase(6f, 0f)]
        public void InactiveWhiteOutline_PreservesTheSourcePartialAlpha(float width, float outlineAlpha)
        {
            PrepareWhiteAntialiasedSource();
            SdfImage image = CreateImage(canvas.transform);
            image.OutlineWidth = 0;
            camera.backgroundColor = Color.clear;
            Color[] baseline = Render();
            Color expectedEdge = Average(baseline, 15, -6, 1, 12);
            Assert.That(expectedEdge.a, Is.InRange(0.3f, 0.6f), "Fixture must contain a partial-alpha edge pixel.");

            image.OutlineWidth = width;
            image.OutlineColor = new Color(1, 1, 1, outlineAlpha);
            Color[] actual = Render();
            Color edge = Average(actual, 15, -6, 1, 12);
            Assert.That(edge.a, Is.EqualTo(expectedEdge.a).Within(0.015f));
            Assert.That(edge.r, Is.EqualTo(expectedEdge.r).Within(0.015f));
            Assert.That(Average(actual, -2, -2, 4, 4).a, Is.EqualTo(128f / 255).Within(0.015f));
        }

        [TestCase(SdfImageType.Simple)]
        [TestCase(SdfImageType.Sliced)]
        public void StretchedWhiteOutline_PreservesTransparencyBeyondTheSourceEdgeFootprint(SdfImageType type)
        {
            PrepareWhiteAntialiasedSource();
            if (type == SdfImageType.Sliced)
                sprite.Initialize(sprite.SourceSprite, sprite.ColorTexture, sprite.DistanceTexture, sprite.SourceSize,
                    new Vector4(4, 4, 4, 4), sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, normalized: sprite.NormalizedDistance);
            SdfImage image = CreateImage(canvas.transform);
            image.Type = type;
            image.rectTransform.sizeDelta = new Vector2(1024, 64);
            image.OutlineColor = Color.white;
            Color baseline = Average(Render(), -8, -3, 16, 1);
            Assert.That(baseline.a, Is.EqualTo(128f / 255).Within(0.015f),
                "The sample lies in the intentionally translucent center, several source pixels from the contour.");

            image.OutlineWidth = 6;
            Color[] outlined = Render();
            Color interior = Average(outlined, -8, -3, 16, 1);
            Assert.That(interior.a, Is.EqualTo(baseline.a).Within(0.015f),
                "Stretching the horizontal axis must not widen the alpha repair around a horizontal source edge.");
            Assert.That(interior.r, Is.EqualTo(baseline.r).Within(0.015f));
            // The horizontal contour is at local y=16 (Simple) or y=18.67 (Sliced).
            Assert.That(Average(outlined, -8, 21, 16, 1).a, Is.GreaterThan(0.9f),
                "The exterior outline must still render while interior transparency is preserved.");
        }

        [Test]
        public void ShadowOffset_MovesTheVisibleBlueSilhouetteToTheRight()
        {
            SdfImage image = CreateImage(canvas.transform);
            image.OutlineWidth = 0;
            image.ShadowColor = Color.blue;
            image.ShadowOffset = new Vector2(20, 0);
            image.ShadowBlur = 0;
            image.ShadowSpread = 0;

            Color[] pixels = Render();
            AssertGreen(Average(pixels, -6, -6, 12, 12), "Opaque fill must remain above its shadow.");
            AssertBlue(Average(pixels, 26, -6, 6, 12), "A positive X shadow offset must render on the right.");
            Assert.That(Average(pixels, -32, -6, 6, 12).a, Is.LessThan(0.05f),
                "The shadow must not appear at the mirrored offset.");
        }

        [Test]
        public void MultipleImageLayers_OrderOffsetAndTextureColorAreIndependent()
        {
            SdfImage image = CreateImage(canvas.transform);
            image.Layers.Clear();
            var front = new SdfImageEffect { Width = 4, Color = Color.red };
            var back = new SdfImageEffect { Width = 8, Color = Color.blue };
            image.Layers.Add(front);
            image.Layers.Add(back);
            image.Layers.Add(new SdfImageEffect { Width = 0, Position = SdfOutlinePosition.Underlay,
                Offset = new Vector2(20, 0), Color = Color.yellow });
            image.RefreshEffects();
            var first = Render();
            AssertGreen(Average(first, -6, -6, 12, 12), "All exterior layers stay behind the sprite.");
            AssertRed(Average(first, 17, -5, 2, 10), "The front outline wins in the overlap.");
            AssertBlue(Average(first, 21, -5, 2, 10), "The wider rear outline remains visible outside the front outline.");
            Color shifted = Average(first, 29, -5, 3, 10);
            AssertColor(shifted, QualitySettings.activeColorSpace == ColorSpace.Linear ? Color.yellow.linear : Color.yellow,
                "The shifted underlay uses its own color.");
            SaveCapture("image-multiple-layers.png", first);

            front.UseTextureColor = true;
            front.TextureColorIntensity = 0.4f;
            image.RefreshEffects();
            var textured = Render();
            Color ring = Average(textured, 17, -5, 2, 10);
            Assert.That(ring.g, Is.EqualTo(0.4f).Within(0.04f));
            Assert.That(ring.r, Is.LessThan(0.02f));
            AssertBlue(Average(textured, 21, -5, 2, 10), "Texture color must not leak into another layer.");

            image.Layers[0] = back;
            image.Layers[1] = front;
            image.RefreshEffects();
            AssertBlue(Average(Render(), 17, -5, 2, 10), "Changing list order changes the visible overlapping layer.");
            back.Enabled = false;
            image.RefreshEffects();
            Assert.That(Average(Render(), 17, -5, 2, 10).g, Is.EqualTo(0.4f).Within(0.04f));
        }

        [TestCase(3)]
        [TestCase(SdfImage.MaxEffectLayers)]
        public void MultipleImageLayers_FadeOnceAndUseWorkingSpaceColors(int count)
        {
            canvas.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            SdfImage image = CreateImage(canvas.transform);
            image.color = new Color(1, 1, 1, 0.8f);
            image.Layers.Clear();
            Color expected = new Color(0.25f, 0.5f, 0.75f, 1);
            for (int i = 0; i < count; i++)
                image.Layers.Add(new SdfImageEffect { Width = 6, Color = i == 0 ? expected : Color.red });
            image.RefreshEffects();
            Color actual = Average(Render(), 18, -5, 2, 10);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) expected = expected.linear;
            Assert.That(actual.a, Is.EqualTo(0.4f).Within(0.02f));
            Assert.That(actual.r, Is.EqualTo(expected.r * 0.4f).Within(0.02f));
            Assert.That(actual.g, Is.EqualTo(expected.g * 0.4f).Within(0.02f));
            Assert.That(actual.b, Is.EqualTo(expected.b * 0.4f).Within(0.02f));
            Assert.That(image.materialForRendering.GetInt("_LayerCount"), Is.EqualTo(count));
            Assert.That(image.canvasRenderer.materialCount, Is.EqualTo(1));
        }

        [Test]
        public void CanvasGroupAndGraphicAlpha_FadeTheCompletedCompositeOnce()
        {
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.5f;
            SdfImage image = CreateImage(canvas.transform);
            image.color = new Color(1, 1, 1, 0.8f);
            image.OutlineWidth = 6;
            image.OutlineColor = Color.red;
            image.ShadowColor = Color.blue;
            image.ShadowOffset = new Vector2(20, 0);
            image.ShadowBlur = 0;

            Color[] pixels = Render();
            Assert.That(Average(pixels, -6, -6, 12, 12).a, Is.EqualTo(0.4f).Within(0.04f), "Fill alpha");
            Assert.That(Average(pixels, 18, -6, 3, 12).a, Is.EqualTo(0.4f).Within(0.04f),
                "Outline above an overlapping shadow must fade once, without leaking shadow alpha.");
            Assert.That(Average(pixels, 28, -6, 4, 12).a, Is.EqualTo(0.4f).Within(0.04f), "Shadow alpha");
        }

        [Test]
        public void TranslatedRectMask2D_ClipsFillAndEffectsInTheCorrectCoordinates()
        {
            RectTransform mask = CreateMask("Rect Clip", canvas.transform, new Vector2(28, 64),
                new Vector2(-8, 0), false);
            SdfImage image = CreateImage(mask);
            image.rectTransform.anchoredPosition = new Vector2(8, 0);
            image.OutlineWidth = 6;
            image.OutlineColor = Color.red;

            Color[] pixels = Render();
            AssertGreen(Average(pixels, -10, -6, 8, 12), "Fill inside the translated clip rectangle.");
            AssertRed(Average(pixels, -21, -6, 3, 12), "Outline inside the clip rectangle must remain visible.");
            Assert.That(Average(pixels, 10, -6, 3, 12).a, Is.LessThan(0.05f), "Fill outside the right clip boundary.");
            Assert.That(Average(pixels, 18, -6, 3, 12).a, Is.LessThan(0.05f), "Outline outside the right clip boundary.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StencilMasks_ClipTranslatedAndNestedBounds(bool nested)
        {
            RectTransform horizontal = CreateMask("Horizontal Stencil", canvas.transform, new Vector2(28, 64),
                new Vector2(-8, 0), true);
            RectTransform parent = nested
                ? CreateMask("Vertical Stencil", horizontal, new Vector2(128, 20), new Vector2(8, -6), true)
                : horizontal;
            SdfImage image = CreateImage(parent);
            image.rectTransform.anchoredPosition = nested ? new Vector2(0, 6) : new Vector2(8, 0);
            image.OutlineWidth = 6;
            image.OutlineColor = Color.red;

            Color[] pixels = Render();
            AssertGreen(Average(pixels, -10, -10, 8, 8), "Fill inside both stencil masks.");
            AssertRed(Average(pixels, -21, -10, 3, 8), "Outline inside both stencil masks.");
            Assert.That(Average(pixels, 10, -10, 3, 8).a, Is.LessThan(0.05f), "First stencil clips the right side.");
            if (nested)
                Assert.That(Average(pixels, -10, 8, 8, 4).a, Is.LessThan(0.05f), "Second stencil clips the top side.");
            else
                AssertGreen(Average(pixels, -10, 8, 8, 4), "A single horizontal mask preserves the top fill.");
        }

        [Test]
        public void SdfImageAsMask_ClipsToItsSilhouetteAndUpdatesItsStencilMaterials()
        {
            SdfImage maskImage = CreateImage(canvas.transform);
            maskImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var childObject = NewObject("Blue Masked Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            childObject.transform.SetParent(maskImage.transform, false);
            ((RectTransform)childObject.transform).sizeDelta = new Vector2(100, 100);
            childObject.GetComponent<Image>().color = Color.blue;

            Color[] initial = Render();
            AssertBlue(Average(initial, -6, -6, 12, 12), "Child must be visible inside the opaque source silhouette.");
            Assert.That(Average(initial, 18, -6, 3, 12).a, Is.LessThan(0.05f),
                "Transparent source pixels must not write the mask stencil.");

            maskImage.OutlineColor = Color.red;
            maskImage.OutlineWidth = 6;
            Color[] changed = Render();
            AssertBlue(Average(changed, 18, -6, 3, 12),
                "The updated SDF outline must enlarge the mask silhouette, while the mask graphic stays hidden.");
            Assert.That(Average(changed, 28, -6, 3, 12).a, Is.LessThan(0.05f),
                "Child pixels outside the complete SDF silhouette must remain clipped.");
            Assert.That(maskImage.canvasRenderer.popMaterialCount, Is.GreaterThan(0));
            Material popMaterial = maskImage.canvasRenderer.GetPopMaterial(0);
            Assert.That(popMaterial.GetVector("_Outline").x, Is.EqualTo(6),
                "Stencil cleanup must use the same updated silhouette as the draw material.");
            Assert.That(popMaterial.IsKeywordEnabled("UNITY_UI_ALPHACLIP"), Is.True,
                "Stencil cleanup must retain alpha clipping on transparent SDF pixels.");
        }

        [Test]
        public void SharedCanvas_WithBackgroundAndTransformedImages_PreservesEachImagesLocalSampling()
        {
            var background = NewObject("Standard UI Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(canvas.transform, false);
            ((RectTransform)background.transform).sizeDelta = new Vector2(Resolution, Resolution);
            background.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1);

            SdfImage translated = CreateImage(canvas.transform);
            translated.name = "Translated SDF Image";
            translated.rectTransform.anchoredPosition = new Vector2(-30, -12);

            var transformedParent = NewObject("Scaled And Rotated Parent", typeof(RectTransform));
            transformedParent.transform.SetParent(canvas.transform, false);
            var parentRectangle = (RectTransform)transformedParent.transform;
            parentRectangle.sizeDelta = new Vector2(64, 64);
            parentRectangle.anchoredPosition = new Vector2(30, 16);
            parentRectangle.localScale = new Vector3(0.8f, 1.1f, 1);
            parentRectangle.localRotation = Quaternion.Euler(0, 0, 15);
            SdfImage transformed = CreateImage(parentRectangle);
            transformed.name = "Transformed SDF Image";

            foreach (SdfImage image in new[] { translated, transformed })
            {
                image.rectTransform.sizeDelta = new Vector2(48, 48);
                image.OutlineWidth = 4;
                image.OutlineColor = Color.red;
                image.ShadowColor = Color.blue;
                image.ShadowOffset = new Vector2(10, 0);
                image.ShadowBlur = 0;
            }

            // Canvas batching can transform POSITION into Canvas space. Artwork
            // sampling must still use each Graphic's own local coordinates.
            Color[] pixels = Render();
            foreach (SdfImage image in new[] { translated, transformed })
            {
                AssertGreen(SampleLocal(pixels, image.rectTransform, Vector2.zero),
                    image.name + " fill must stay centered after Canvas batching and transforms.");
                AssertRed(SampleLocal(pixels, image.rectTransform, new Vector2(14, 0)),
                    image.name + " outline must remain at the source contour after transforms.");
                AssertBlue(SampleLocal(pixels, image.rectTransform, new Vector2(19, 0)),
                    image.name + " shadow offset must follow the transformed Graphic's local axes.");
            }
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            var result = new GameObject(name, components) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(result, previewScene);
            return result;
        }

        private void PrepareWhiteAntialiasedSource()
        {
            // Mutate a disposable copy; imported textures intentionally have no CPU-readable data.
            editedColor = SdfTestTextureReadback.Copy(sprite.ColorTexture);
            editedSprite = ScriptableObject.CreateInstance<SdfSprite>();
            editedSprite.Initialize(sprite.SourceSprite, editedColor, sprite.DistanceTexture, sprite.SourceSize,
                sprite.Border, sprite.Pivot, sprite.PixelsPerUnit, sprite.Padding, sprite.DistanceRange, sprite.AlphaThreshold, normalized: sprite.NormalizedDistance);
            sprite = editedSprite;
            // The last inside texel is only 60% covered but still exceeds the
            // baker's 50% threshold, so the existing binary SDF remains correct.
            // This isolates the mismatch between RGBA alpha and SDF coverage.
            Color32[] pixels = sprite.ColorTexture.GetPixels32();
            int width = sprite.ColorTexture.width;
            for (int i = 0; i < pixels.Length; i++)
            {
                int x = i % width - sprite.Padding;
                int y = i / width - sprite.Padding;
                byte alpha = pixels[i].a;
                if ((x == 8 || x == 23) && y >= 8 && y < 24)
                    alpha = 153;
                if (x >= 14 && x < 18 && y >= 14 && y < 18)
                    alpha = 128;
                pixels[i] = new Color32(255, 255, 255, alpha);
            }
            sprite.ColorTexture.SetPixels32(pixels);
            sprite.ColorTexture.Apply(false, false);
        }

        private SdfImage CreateImage(Transform parent)
        {
            var imageObject = NewObject("SDF Render Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(SdfImage));
            imageObject.transform.SetParent(parent, false);
            var image = imageObject.GetComponent<SdfImage>();
            image.rectTransform.sizeDelta = new Vector2(64, 64);
            image.Sprite = sprite;
            image.OutlineWidth = 0;
            image.ShadowColor = Color.clear;
            image.ShadowBlur = 0;
            return image;
        }

        private RectTransform CreateMask(string name, Transform parent, Vector2 size, Vector2 position, bool stencil)
        {
            var maskObject = NewObject(name, typeof(RectTransform));
            maskObject.transform.SetParent(parent, false);
            var rectangle = (RectTransform)maskObject.transform;
            rectangle.sizeDelta = size;
            rectangle.anchoredPosition = position;
            if (stencil)
            {
                maskObject.AddComponent<Image>();
                maskObject.AddComponent<Mask>().showMaskGraphic = false;
            }
            else
            {
                maskObject.AddComponent<RectMask2D>();
            }
            return rectangle;
        }

        private Color[] Render()
        {
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                camera.Render();
            }
            else
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True,
                    "The active render pipeline must support an isolated StandardRequest.");
                RenderPipeline.SubmitRenderRequest(camera, request);
            }

            var previous = RenderTexture.active;
            var readback = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readback.Apply();
                Shader shader = Resources.Load<Shader>("SDFImage");
                Assert.That(shader, Is.Not.Null);
                Assert.That(shader.isSupported, Is.True);
                var errors = new StringBuilder();
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity.ToString() == "Error")
                        errors.AppendLine(message.message);
                Assert.That(errors.Length, Is.Zero, errors.ToString());
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }

        private string CreateSquareSource(bool splitColors = false)
        {
            string path = folder + "/Square.png";
            var source = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[32 * 32];
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    bool inside = x >= 8 && x < 24 && y >= 8 && y < 24;
                    pixels[y * 32 + x] = !inside ? new Color32(0, 0, 0, 0)
                        : !splitColors ? new Color32(0, 255, 0, 255)
                        : x < 16 ? new Color32(120, 48, 24, 255) : new Color32(24, 72, 120, 255);
                }
                source.SetPixels32(pixels);
                source.Apply();
                File.WriteAllBytes(path, source.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return path;
        }

        private static void SaveCapture(string filename, Color[] pixels)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                texture.SetPixels(pixels);
                texture.Apply();
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Build/Validation"));
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, filename), texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void AssertColor(Color actual, Color expected, string context)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), context + " (red)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), context + " (green)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), context + " (blue)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), context + " (alpha)");
        }

        private static Color Average(Color[] pixels, int x, int y, int width, int height)
        {
            Color sum = Color.clear;
            for (int row = y + Resolution / 2; row < y + Resolution / 2 + height; row++)
            for (int column = x + Resolution / 2; column < x + Resolution / 2 + width; column++)
                sum += pixels[row * Resolution + column];
            return sum / (width * height);
        }

        private Color SampleLocal(Color[] pixels, RectTransform rectangle, Vector2 local)
        {
            Vector3 viewport = camera.WorldToViewportPoint(rectangle.TransformPoint(local));
            int x = Mathf.Clamp(Mathf.FloorToInt(viewport.x * Resolution), 0, Resolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(viewport.y * Resolution), 0, Resolution - 1);
            return pixels[y * Resolution + x];
        }

        private static void AssertGreen(Color color, string context)
        {
            Assert.That(color.g, Is.GreaterThan(0.8f), context);
            Assert.That(color.r + color.b, Is.LessThan(0.15f), context);
            Assert.That(color.a, Is.GreaterThan(0.9f), context);
        }

        private static void AssertRed(Color color, string context)
        {
            Assert.That(color.r, Is.GreaterThan(0.8f), context);
            Assert.That(color.g + color.b, Is.LessThan(0.15f), context);
            Assert.That(color.a, Is.GreaterThan(0.9f), context);
        }

        private static void AssertBlue(Color color, string context)
        {
            Assert.That(color.b, Is.GreaterThan(0.8f), context);
            Assert.That(color.r + color.g, Is.LessThan(0.15f), context);
            Assert.That(color.a, Is.GreaterThan(0.9f), context);
        }
    }
}
