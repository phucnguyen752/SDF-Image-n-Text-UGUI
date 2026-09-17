using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    /// <summary>GPU regressions for whole-string effects, including TMP fallback submeshes.</summary>
    public sealed class SdfTextTests
    {
        private const int Resolution = 256;
        private readonly List<TMP_FontAsset> generatedFonts = new List<TMP_FontAsset>();
        private Scene previewScene;
        private Camera camera;
        private Canvas canvas;
        private RenderTexture target;
        private TMP_FontAsset font;

        [OneTimeSetUp]
        public void RequireFontResources()
        {
            if (!Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF"))
                Assert.Ignore("Import TMP Essential Resources before running SdfTextTests.");
        }

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("GPU rendering is unavailable on the Null graphics device.");

            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            Assert.That(font, Is.Not.Null, "Import TMP Essential Resources before running the text render tests.");
            previewScene = EditorSceneManager.NewPreviewScene();
            camera = NewObject("Text Test Camera", typeof(Camera)).GetComponent<Camera>();
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
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            target.Create();
            camera.targetTexture = target;
            canvas = NewObject("Text Test Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Resolution, Resolution);
        }

        [TearDown]
        public void TearDown()
        {
            if (camera) camera.targetTexture = null;
            if (target)
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            foreach (TMP_FontAsset generated in generatedFonts)
            {
                if (generated.material) Object.DestroyImmediate(generated.material);
                foreach (Texture2D atlas in generated.atlasTextures)
                    if (atlas) Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(generated);
            }
            generatedFonts.Clear();
        }

        [Test]
        public void CloselySpacedGlyphs_ThickOutlineNeverPaintsOverAnyGlyphFace()
        {
            SdfText text = CreateText(canvas.transform, "AVAVA");
            Color[] face = Render();
            EnableEffects(text);
            Color[] effects = Render();
            SaveCapture("tmp-close-letters-face.png", face);
            SaveCapture("tmp-close-letters.png", effects);
            AssertFaceUnchanged(face, effects);
            Assert.That(CountExteriorEffect(face, effects), Is.GreaterThan(100),
                "The comparison must include a visible outline, not two face-only renders.");
        }

        [Test]
        public void ThickOutline_FillsTheExteriorStrokeWithoutHolesAtConcaveCorners()
        {
            SdfText text = CreateText(canvas.transform, "LE\nUP");
            text.font = CreateFont("LEVELUP", 256, 48, 2048);
            text.fontSize = 80;
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 0;
            text.lineSpacing = -12;
            text.rectTransform.sizeDelta = new Vector2(240, 220);
            Color[] face = Render();
            text.OutlineEnabled = true;
            text.OutlineWidth = 7;
            text.OutlineSoftness = 0.8f;
            text.OutlineColor = Color.cyan;
            Color[] outlined = Render();
            SaveCapture("tmp-thick-outline-face.png", face);
            SaveCapture("tmp-thick-outline.png", outlined);

            // Independently dilate opaque face pixels in Canvas space. The two-pixel
            // margin accommodates antialiasing and softness without using shader math.
            var expectedStroke = new bool[face.Length];
            const int radius = 5;
            for (int y = radius; y < Resolution - radius; y++)
            for (int x = radius; x < Resolution - radius; x++)
            {
                if (face[y * Resolution + x].a < 0.99f) continue;
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    if (dx * dx + dy * dy <= radius * radius)
                        expectedStroke[(y + dy) * Resolution + x + dx] = true;
            }

            int samples = 0, holes = 0;
            float minimumAlpha = 1;
            for (int i = 0; i < face.Length; i++)
            {
                if (!expectedStroke[i] || face[i].a > 0.01f) continue;
                samples++;
                minimumAlpha = Mathf.Min(minimumAlpha, outlined[i].a);
                if (outlined[i].a < 0.95f) holes++;
            }
            Assert.That(samples, Is.GreaterThan(100), "The comparison must cover the exterior outline.");
            Assert.That(holes, Is.Zero,
                "A seven-unit outline left holes within five pixels of an opaque glyph face. Minimum alpha: " + minimumAlpha);
        }

        [Test]
        public void FallbackFontSubmeshes_AllOutlinesRemainBehindAllGlyphFaces()
        {
            SdfText text = CreateText(canvas.transform, "AVAVA");
            TMP_FontAsset primary = CreateFont("A");
            TMP_FontAsset fallback = CreateFont("V");
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            text.font = primary;
            text.ForceMeshUpdate();
            Assert.That(text.textInfo.materialCount, Is.GreaterThanOrEqualTo(2),
                "The regression must actually draw a separate fallback font material.");
            Color[] face = Render();
            EnableEffects(text);
            Color[] effects = Render();
            SaveCapture("tmp-fallback-letters-face.png", face);
            SaveCapture("tmp-fallback-letters.png", effects);
            AssertFaceUnchanged(face, effects);
            Assert.That(CountExteriorEffect(face, effects), Is.GreaterThan(100));
        }

        [Test]
        public void LegacyOutlineSerialization_MigratesTheStyleAndKeepsScalarAliasesOnTheFirstEntry()
        {
            SdfText text = CreateText(canvas.transform, "O");
            using (var serialized = new SerializedObject(text))
            {
                serialized.FindProperty("sdfLayersMigrated").boolValue = false;
                serialized.FindProperty("sdfLayers").arraySize = 0;
                serialized.FindProperty("sdfOutlinesMigrated").boolValue = false;
                serialized.FindProperty("sdfOutlines").arraySize = 0;
                serialized.FindProperty("sdfOutlineEnabled").boolValue = false;
                serialized.FindProperty("sdfOutlineWidth").floatValue = 7.25f;
                serialized.FindProperty("sdfOutlineSoftness").floatValue = 1.5f;
                serialized.FindProperty("sdfOutlineColor").colorValue = new Color(0.2f, 0.4f, 0.6f, 0.75f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(text.Layers.Count, Is.EqualTo(2));
            SdfTextEffect legacy = text.Layers[0];
            Assert.That(legacy.Enabled, Is.False);
            Assert.That(legacy.Width, Is.EqualTo(7.25f));
            Assert.That(legacy.Softness, Is.EqualTo(1.5f));
            Assert.That(legacy.Color, Is.EqualTo(new Color(0.2f, 0.4f, 0.6f, 0.75f)));
            Assert.That(legacy.Offset, Is.EqualTo(Vector2.zero));
            Assert.That(legacy.Position, Is.EqualTo(SdfOutlinePosition.Underlay), "Existing layers must retain their filled silhouettes.");
            Assert.That(text.OutlineEnabled, Is.False, "Migration must preserve the legacy outline toggle.");
            Assert.That(text.Layers[1].Enabled, Is.False, "The legacy disabled shadow remains an editable back layer.");
            legacy.Enabled = false;
            text.OutlineWidth = 4;
            text.OutlineSoftness = 0.5f;
            text.OutlineColor = Color.yellow;
            text.OutlineOffset = new Vector2(3, -4);
            Assert.That(legacy.Enabled, Is.False, "A scalar style setter must not enable an existing disabled entry.");
            Assert.That(legacy.Width, Is.EqualTo(4));
            Assert.That(legacy.Softness, Is.EqualTo(0.5f));
            Assert.That(legacy.Color, Is.EqualTo(Color.yellow));
            Assert.That(legacy.Offset, Is.EqualTo(new Vector2(3, -4)));
            text.RefreshEffects();
            Assert.That(text.Layers.Count, Is.EqualTo(2), "Repeated refresh must not duplicate migrated styles.");
        }

        [TestCase(0f)]
        [TestCase(-3f)]
        public void ReleasedScalarStyles_MigrateToTwoLayersWithoutChangingTheirRenderedOutput(float shadowSpread)
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color outlineColor = new Color(0.9f, 0.1f, 0.1f, 1);
            Color shadowColor = new Color(0.1f, 0.1f, 0.8f, 0.8f);
            Vector2 shadowOffset = new Vector2(16, -7);
            text.OutlineEnabled = true;
            text.OutlineWidth = 6;
            text.OutlineSoftness = 0.5f;
            text.OutlineColor = outlineColor;
            text.ShadowEnabled = true;
            text.ShadowSpread = shadowSpread;
            text.ShadowBlur = 3;
            text.ShadowOffset = shadowOffset;
            text.ShadowColor = shadowColor;
            Color[] expected = Render();
            using (var serialized = new SerializedObject(text))
            {
                serialized.FindProperty("sdfLayersMigrated").boolValue = false;
                serialized.FindProperty("sdfLayers").arraySize = 0;
                serialized.FindProperty("sdfOutlinesMigrated").boolValue = false;
                serialized.FindProperty("sdfOutlines").arraySize = 0;
                serialized.FindProperty("sdfOutlineEnabled").boolValue = true;
                serialized.FindProperty("sdfOutlineWidth").floatValue = 6;
                serialized.FindProperty("sdfOutlineSoftness").floatValue = 0.5f;
                serialized.FindProperty("sdfOutlineColor").colorValue = outlineColor;
                serialized.FindProperty("sdfShadowEnabled").boolValue = true;
                serialized.FindProperty("sdfShadowSpread").floatValue = shadowSpread;
                serialized.FindProperty("sdfShadowBlur").floatValue = 3;
                serialized.FindProperty("sdfShadowOffset").vector2Value = shadowOffset;
                serialized.FindProperty("sdfShadowColor").colorValue = shadowColor;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(text.Layers.Count, Is.EqualTo(2));
            Assert.That(text.Layers[0].Color, Is.EqualTo(outlineColor));
            Assert.That(text.Layers[1].Enabled, Is.True);
            Assert.That(text.Layers[1].Width, Is.EqualTo(shadowSpread));
            Assert.That(text.Layers[1].Softness, Is.EqualTo(3));
            Assert.That(text.Layers[1].Offset, Is.EqualTo(shadowOffset));
            text.RefreshEffects();
            Assert.That(Render(), Is.EqualTo(expected), "Legacy outline/shadow migration must preserve the complete composition.");
        }

        [Test]
        public void ReleasedScalarAliases_KeepTheirLayerBindingsAfterReordering()
        {
            SdfText text = CreateText(canvas.transform, "O");
            SdfTextEffect outline = text.Layers[0], shadow = text.Layers[1];
            text.Layers.Add(Outline(Color.cyan, 4, new Vector2(2, 0)));
            text.Layers.Reverse();
            text.OutlineEnabled = true;
            text.OutlineWidth = 7;
            text.OutlineSoftness = 0.75f;
            text.OutlineColor = Color.red;
            text.OutlineOffset = new Vector2(3, -4);
            text.ShadowEnabled = true;
            text.ShadowSpread = -2;
            text.ShadowBlur = 5;
            text.ShadowColor = Color.blue;
            text.ShadowOffset = new Vector2(-6, -8);
            Assert.That(text.Layers.Count, Is.EqualTo(3));
            Assert.That(outline.Enabled, Is.True);
            Assert.That(outline.Width, Is.EqualTo(7));
            Assert.That(outline.Softness, Is.EqualTo(0.75f));
            Assert.That(outline.Color, Is.EqualTo(Color.red));
            Assert.That(outline.Offset, Is.EqualTo(new Vector2(3, -4)));
            Assert.That(shadow.Enabled, Is.True);
            Assert.That(shadow.Width, Is.EqualTo(-2));
            Assert.That(shadow.Softness, Is.EqualTo(5));
            Assert.That(shadow.Color, Is.EqualTo(Color.blue));
            Assert.That(shadow.Offset, Is.EqualTo(new Vector2(-6, -8)));
            Assert.That(text.Layers[0].Color, Is.EqualTo(Color.cyan), "Released aliases must not overwrite a reordered unrelated layer.");
            text.Layers.Clear();
            text.RefreshEffects();
            Assert.That(text.OutlineEnabled, Is.False);
            Assert.That(text.ShadowEnabled, Is.False);
            Assert.That(text.Layers, Is.Empty, "Reading compatibility aliases must not recreate deleted layers.");
        }

        [Test]
        public void ExistingOutlineStack_MigratesItsOrderAndOffsetsAndAppendsTheShadow()
        {
            SdfText text = CreateText(canvas.transform, "O");
            using (var serialized = new SerializedObject(text))
            {
                serialized.FindProperty("sdfLayersMigrated").boolValue = false;
                serialized.FindProperty("sdfLayers").arraySize = 0;
                serialized.FindProperty("sdfOutlinesMigrated").boolValue = true;
                serialized.FindProperty("sdfOutlineEnabled").boolValue = true;
                SerializedProperty previous = serialized.FindProperty("sdfOutlines");
                previous.arraySize = 2;
                for (int i = 0; i < previous.arraySize; i++)
                {
                    SerializedProperty entry = previous.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("enabled").boolValue = true;
                    entry.FindPropertyRelative("width").floatValue = i == 0 ? 3 : 8;
                    entry.FindPropertyRelative("softness").floatValue = 0.5f;
                    entry.FindPropertyRelative("color").colorValue = i == 0 ? Color.red : Color.blue;
                    entry.FindPropertyRelative("offset").vector2Value = new Vector2(i, -i);
                }
                serialized.FindProperty("sdfShadowEnabled").boolValue = false;
                serialized.FindProperty("sdfShadowSpread").floatValue = -2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(text.Layers.Count, Is.EqualTo(3));
            Assert.That(text.Layers[0].Width, Is.EqualTo(3));
            Assert.That(text.Layers[0].Color, Is.EqualTo(Color.red));
            Assert.That(text.Layers[1].Width, Is.EqualTo(8));
            Assert.That(text.Layers[1].Color, Is.EqualTo(Color.blue));
            Assert.That(text.Layers[1].Offset, Is.EqualTo(new Vector2(1, -1)));
            Assert.That(text.Layers[2].Enabled, Is.False);
            Assert.That(text.Layers[2].Width, Is.EqualTo(-2));
            text.RefreshEffects();
            Assert.That(text.Layers.Count, Is.EqualTo(3), "Refreshing a converted stack must not append duplicate shadows.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingEmptyOutlineStack_MigratesOnlyAnEnabledShadow(bool shadowEnabled)
        {
            SdfText text = CreateText(canvas.transform, "O");
            using (var serialized = new SerializedObject(text))
            {
                serialized.FindProperty("sdfLayersMigrated").boolValue = false;
                serialized.FindProperty("sdfLayers").arraySize = 0;
                serialized.FindProperty("sdfOutlinesMigrated").boolValue = true;
                serialized.FindProperty("sdfOutlines").arraySize = 0;
                serialized.FindProperty("sdfOutlineEnabled").boolValue = true;
                serialized.FindProperty("sdfShadowEnabled").boolValue = shadowEnabled;
                serialized.FindProperty("sdfShadowSpread").floatValue = -3;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            text.RefreshEffects();
            Assert.That(text.Layers.Count, Is.EqualTo(shadowEnabled ? 1 : 0));
            Assert.That(text.OutlineEnabled, Is.False, "An empty old outline stack must not acquire a primary outline.");
            Assert.That(text.ShadowEnabled, Is.EqualTo(shadowEnabled));
            if (shadowEnabled) Assert.That(text.Layers[0].Width, Is.EqualTo(-3));
            Assert.That(text.Layers.Count, Is.EqualTo(shadowEnabled ? 1 : 0), "Reading aliases must not repopulate missing styles.");
        }

        [Test]
        public void SignedLayerSpread_ZeroDrawsAnOffsetSilhouetteAndNegativeContractsIt()
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color[] face = Render();
            text.Layers.Clear();
            var effect = Outline(Color.blue, 0, new Vector2(40, -10));
            text.Layers.Add(effect);
            text.RefreshEffects();
            Color[] zero = Render();
            effect.Width = -3;
            text.RefreshEffects();
            Color[] negative = Render();
            Assert.That(effect.Width, Is.EqualTo(-3), "The unified layer API must preserve negative spread.");
            Assert.That(CountExteriorEffect(face, zero), Is.GreaterThan(100), "A zero-spread effect is a valid shadow silhouette.");
            Assert.That(CountExteriorEffect(face, negative), Is.LessThan(CountExteriorEffect(face, zero) - 10));
            SaveCapture("tmp-unified-layer-zero-spread.png", zero);
            SaveCapture("tmp-unified-layer-negative-spread.png", negative);
        }

        [TestCase(1f)]
        [TestCase(0.5f)]
        public void EffectLayerColor_MatchesTheInspectorSwatchAndAppliesOpacityOnce(float opacity)
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color[] face = Render();
            text.Layers.Clear();
            SdfTextEffect layer = Outline(Color.white, 6, new Vector2(48, 0));
            text.Layers.Add(layer);
            text.RefreshEffects();
            Color[] coverage = Render();
            Color inspectorColor = new Color(0.2f, 0.4f, 0.6f, opacity);
            layer.Color = inspectorColor;
            text.RefreshEffects();
            Color[] actual = Render();
            Color expected = QualitySettings.activeColorSpace == ColorSpace.Linear ? inspectorColor.linear : inspectorColor;
            SaveCapture("tmp-layer-color-swatch-" + Mathf.RoundToInt(opacity * 100) + ".png", actual);
            int samples = 0;
            for (int i = 0; i < face.Length; i++)
            {
                // One fully covered effect, away from glyph faces and overlapping fringes.
                if (face[i].a > 0.01f || coverage[i].a < 0.999f) continue;
                Assert.That(actual[i].r, Is.EqualTo(expected.r * opacity).Within(0.008f), "Effect red differs from the Inspector's sRGB swatch.");
                Assert.That(actual[i].g, Is.EqualTo(expected.g * opacity).Within(0.008f), "Effect green differs from the Inspector's sRGB swatch.");
                Assert.That(actual[i].b, Is.EqualTo(expected.b * opacity).Within(0.008f), "Effect blue differs from the Inspector's sRGB swatch.");
                Assert.That(actual[i].a, Is.EqualTo(opacity).Within(0.008f), "Opacity must be applied once.");
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(100), "The reference must provide opaque exterior effect samples.");
        }

        [Test]
        public void LegacyTextLayersWithoutPosition_PreserveSilhouettesAndSaveNewModes()
        {
            string folder = CreateSerializationFolder();
            GameObject loaded = null;
            try
            {
                SdfText text = CreateText(canvas.transform, "O");
                text.Layers.Clear();
                text.Layers.Add(Outline(Color.red, 0, new Vector2(20, 0)));
                text.Layers.Add(Outline(Color.blue, -3, new Vector2(-20, 0)));
                string path = folder + "/LegacyLayers.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(text.gameObject, path), Is.Not.Null);
                // Reproduce the released prefab schema, before position and underlay type existed.
                string yaml = File.ReadAllText(path);
                string legacy = System.Text.RegularExpressions.Regex.Replace(yaml, @"(?m)^    position: 3\r?\n", "");
                legacy = System.Text.RegularExpressions.Regex.Replace(legacy, @"(?m)^    underlayType: 0\r?\n", "");
                Assert.That(legacy, Is.Not.EqualTo(yaml));
                File.WriteAllText(path, legacy);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                loaded = PrefabUtility.LoadPrefabContents(path);
                var reloaded = loaded.GetComponent<SdfText>();
                Assert.That(reloaded.Layers.Count, Is.EqualTo(2));
                foreach (var layer in reloaded.Layers)
                {
                    Assert.That(layer.Position, Is.EqualTo(SdfOutlinePosition.Underlay));
                    Assert.That(layer.UnderlayType, Is.EqualTo(SdfTextUnderlayType.Normal));
                }
                Assert.That(reloaded.Layers[0].Width, Is.Zero);
                Assert.That(reloaded.Layers[1].Width, Is.EqualTo(-3));
                reloaded.Layers[0].Position = SdfOutlinePosition.Inner;
                reloaded.Layers[0].Width = 2;
                reloaded.Layers[0].UnderlayType = SdfTextUnderlayType.Normal;
                reloaded.Layers[1].UnderlayType = SdfTextUnderlayType.Inner;
                PrefabUtility.SaveAsPrefabAsset(loaded, path);
                PrefabUtility.UnloadPrefabContents(loaded);
                loaded = PrefabUtility.LoadPrefabContents(path);
                Assert.That(loaded.GetComponent<SdfText>().Layers[0].Position, Is.EqualTo(SdfOutlinePosition.Inner));
                Assert.That(loaded.GetComponent<SdfText>().Layers[0].UnderlayType, Is.EqualTo(SdfTextUnderlayType.Normal));
                Assert.That(loaded.GetComponent<SdfText>().Layers[1].UnderlayType, Is.EqualTo(SdfTextUnderlayType.Inner));
            }
            finally
            {
                if (loaded) PrefabUtility.UnloadPrefabContents(loaded);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void EmptyOutlineList_RemainsEmptyAfterPrefabSaveAndReload()
        {
            string folder = CreateSerializationFolder();
            GameObject loaded = null;
            try
            {
                SdfText text = CreateText(canvas.transform, "O");
                Color[] face = Render();
                text.Layers.Clear();
                text.EffectsEnabled = true;
                text.RefreshEffects();
                Assert.That(Render(), Is.EqualTo(face), "An empty list must leave only the glyph face.");
                string path = folder + "/Empty.prefab";
                Assert.That(PrefabUtility.SaveAsPrefabAsset(text.gameObject, path), Is.Not.Null);
                loaded = PrefabUtility.LoadPrefabContents(path);
                SdfText reloaded = loaded.GetComponent<SdfText>();
                reloaded.RefreshEffects();
                Assert.That(reloaded.Layers, Is.Empty);
                Assert.That(reloaded.OutlineWidth, Is.Zero);
                Assert.That(reloaded.OutlineColor, Is.EqualTo(Color.clear));
                Assert.That(reloaded.OutlineOffset, Is.EqualTo(Vector2.zero));
                Assert.That(reloaded.ShadowEnabled, Is.False);
                Assert.That(reloaded.ShadowSpread, Is.Zero);
                reloaded.enabled = false;
                reloaded.enabled = true;
                Assert.That(reloaded.Layers, Is.Empty, "OnEnable must not resurrect the legacy outline.");
            }
            finally
            {
                if (loaded) PrefabUtility.UnloadPrefabContents(loaded);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void LegacyPrefabVariantOverride_MigratesWithoutChangingTheBaseOrResurrectingAnEmptyList()
        {
            string folder = CreateSerializationFolder();
            GameObject instance = null, loaded = null;
            try
            {
                SdfText source = CreateText(canvas.transform, "O");
                source.OutlineWidth = 2;
                source.ShadowOffset = new Vector2(2, 13);
                string basePath = folder + "/Base.prefab", variantPath = folder + "/Variant.prefab";
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source.gameObject, basePath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
                SdfText variant = instance.GetComponent<SdfText>();
                using (var serialized = new SerializedObject(variant))
                {
                    serialized.FindProperty("sdfOutlineWidth").floatValue = 9;
                    serialized.FindProperty("sdfOutlineColor").colorValue = Color.magenta;
                    // Only X differs from the legacy default. Preserve the base's new layer Y.
                    serialized.FindProperty("sdfShadowOffset").vector2Value = new Vector2(11, -2);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(variant);
                Assert.That(PrefabUtility.SaveAsPrefabAsset(instance, variantPath), Is.Not.Null);
                loaded = PrefabUtility.LoadPrefabContents(variantPath);
                Assert.That(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath)),
                    Is.EqualTo(PrefabAssetType.Variant));
                SdfText reloaded = loaded.GetComponent<SdfText>();
                Assert.That(reloaded.Layers.Count, Is.EqualTo(2));
                Assert.That(reloaded.Layers[0].Width, Is.EqualTo(9));
                Assert.That(reloaded.Layers[0].Color, Is.EqualTo(Color.magenta));
                Assert.That(prefab.GetComponent<SdfText>().OutlineWidth, Is.EqualTo(2), "A variant must not change its base style.");
                Assert.That(reloaded.ShadowOffset, Is.EqualTo(new Vector2(11, 13)),
                    "An old X-only override must not overwrite the new layer Y inherited from the base.");
                Assert.That(prefab.GetComponent<SdfText>().ShadowOffset, Is.EqualTo(new Vector2(2, 13)));
                reloaded.Layers.Clear();
                reloaded.RefreshEffects();
                PrefabUtility.SaveAsPrefabAsset(loaded, variantPath);
                PrefabUtility.UnloadPrefabContents(loaded);
                loaded = PrefabUtility.LoadPrefabContents(variantPath);
                Assert.That(loaded.GetComponent<SdfText>().Layers, Is.Empty,
                    "An intentionally empty variant must not revive its legacy scalar override on reload.");
            }
            finally
            {
                if (loaded) PrefabUtility.UnloadPrefabContents(loaded);
                if (instance) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void MultipleOutlines_ReorderingChangesTheFrontStrokeWhileEveryGlyphFaceStaysInFront()
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color[] face = Render();
            var red = Outline(Color.red, 6, Vector2.zero);
            var blue = Outline(Color.blue, 6, Vector2.zero);
            text.Layers.Clear();
            text.Layers.Add(red);
            text.EffectsEnabled = true;
            text.RefreshEffects();
            Color[] singleOutline = Render();
            text.Layers.Add(blue);
            text.RefreshEffects();
            Color[] redFront = Render();
            text.Layers.Reverse();
            text.RefreshEffects();
            Color[] blueFront = Render();
            SaveCapture("tmp-multi-outline-red-front.png", redFront);
            SaveCapture("tmp-multi-outline-blue-front.png", blueFront);
            AssertFaceUnchanged(face, redFront);
            AssertFaceUnchanged(face, blueFront);
            int samples = 0;
            for (int i = 0; i < face.Length; i++)
            {
                // Composite alpha can become opaque where two antialiased fringes overlap.
                // Select pixels opaque in one isolated outline to test actual draw order.
                if (face[i].a > 0.01f || singleOutline[i].a < 0.999f) continue;
                Assert.That(redFront[i].r, Is.GreaterThan(0.98f));
                Assert.That(redFront[i].b, Is.LessThan(0.02f));
                Assert.That(blueFront[i].b, Is.GreaterThan(0.98f));
                Assert.That(blueFront[i].r, Is.LessThan(0.02f));
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(100));
            blue.Enabled = false;
            text.RefreshEffects();
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(100));
            text.EffectsEnabled = false;
            Assert.That(Render(), Is.EqualTo(face), "The master switch must hide every effect layer.");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void InnerLayers_PreserveGlyphInteriorsAndKeepListOrderAcrossFonts(bool useFallback, bool underlay)
        {
            SdfText text = CreateText(canvas.transform, "AO");
            text.fontSize = 128;
            text.characterSpacing = 0;
            if (useFallback)
            {
                TMP_FontAsset primary = CreateFont("A"), fallback = CreateFont("O");
                primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                text.font = primary;
            }
            Color[] face = Render();
            Assert.That(text.textInfo.materialCount, Is.EqualTo(useFallback ? 2 : 1));
            var red = Outline(Color.red, underlay ? -3 : 3, Vector2.zero);
            var blue = Outline(Color.blue, underlay ? -3 : 3, Vector2.zero);
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.green, 8, Vector2.zero));
            text.Layers.Add(red);
            text.Layers.Add(blue);
            text.RefreshEffects();
            AssertFaceUnchanged(face, Render());

            // Inner borders draw over the face even after an earlier below-text layer.
            if (underlay) blue.UnderlayType = SdfTextUnderlayType.Inner;
            else blue.Position = SdfOutlinePosition.Inner;
            text.RefreshEffects();
            Color[] singleInner = Render();
            Assert.That(CountColored(singleInner, Color.blue), Is.GreaterThan(30));
            if (underlay) red.UnderlayType = SdfTextUnderlayType.Inner;
            else red.Position = SdfOutlinePosition.Inner;
            text.RefreshEffects();
            Color[] redFront = Render();
            Assert.That(CountColored(redFront, Color.white), Is.GreaterThan(50), "Inner must retain the middle of each stroke.");
            SaveCapture("tmp-inner-red-front-" + useFallback + ".png", redFront);

            text.Layers.Reverse();
            text.RefreshEffects();
            Color[] blueFront = Render();
            int samples = 0;
            for (int i = 0; i < face.Length; i++)
            {
                if (singleInner[i].b < 0.99f || singleInner[i].r > 0.01f || singleInner[i].g > 0.01f) continue;
                Assert.That(redFront[i].r, Is.GreaterThan(0.98f));
                Assert.That(redFront[i].b, Is.LessThan(0.02f));
                Assert.That(blueFront[i].b, Is.GreaterThan(0.98f));
                Assert.That(blueFront[i].r, Is.LessThan(0.02f));
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(30));
            SaveCapture("tmp-inner-blue-front-" + useFallback + ".png", blueFront);
            blue.Enabled = false;
            text.RefreshEffects();
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(30));
            red.Position = SdfOutlinePosition.Underlay;
            red.UnderlayType = SdfTextUnderlayType.Normal;
            text.RefreshEffects();
            AssertFaceUnchanged(face, Render());
            text.EffectsEnabled = false;
            Assert.That(Render(), Is.EqualTo(face));
        }

        [TestCase(SdfOutlinePosition.Outer)]
        [TestCase(SdfOutlinePosition.Inner)]
        [TestCase(SdfOutlinePosition.Center)]
        [TestCase(SdfOutlinePosition.Underlay)]
        public void OutlinePosition_PlacesTheBandOnTheRequestedSideOfTheGlyph(SdfOutlinePosition position)
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.fontSize = 180;
            Color[] face = Render();
            text.Layers.Clear();
            var layer = new SdfTextEffect { Position = position, Width = 4, Color = Color.red };
            text.Layers.Add(layer);
            text.RefreshEffects();
            Color[] actual = Render();
            SaveCapture("tmp-position-" + position + ".png", actual);
            if (position == SdfOutlinePosition.Inner)
                Assert.That(CountExteriorEffect(face, actual), Is.Zero, "Inner must not expand outside the glyph.");
            else
                Assert.That(CountExteriorEffect(face, actual), Is.GreaterThan(50));

            if (position == SdfOutlinePosition.Inner || position == SdfOutlinePosition.Center)
            {
                int inside = 0;
                for (int i = 0; i < face.Length; i++)
                    if (face[i].a > 0.99f && actual[i].r > 0.98f && actual[i].g < 0.02f) inside++;
                Assert.That(inside, Is.GreaterThan(50), "The border must reach into the glyph face.");
                Assert.That(CountColored(actual, Color.white), Is.GreaterThan(100), "A border must leave the stroke center visible.");
            }
            else AssertFaceUnchanged(face, actual);

            if (position == SdfOutlinePosition.Inner)
            {
                layer.Softness = 4;
                text.RefreshEffects();
                Assert.That(CountExteriorEffect(face, Render()), Is.Zero, "Softness must not move Inner outside the glyph.");
            }
            if (position != SdfOutlinePosition.Underlay)
            {
                layer.Width = 0;
                text.RefreshEffects();
                Assert.That(Render(), Is.EqualTo(face), "A zero-width border must be invisible.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OffsetOutlineLayers_FollowRectMaskAndSourceCanvasGroupIndependently(bool center)
        {
            var mask = (RectTransform)NewObject("Offset outline clip", typeof(RectTransform),
                typeof(UnityEngine.UI.RectMask2D)).transform;
            mask.SetParent(canvas.transform, false);
            mask.sizeDelta = new Vector2(150, 100);
            SdfText text = CreateText(mask, "O");
            Color[] face = Render();
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, new Vector2(-60, 0)));
            text.Layers.Add(Outline(Color.blue, 4, new Vector2(60, 0)));
            if (center) text.Layers[1].Position = SdfOutlinePosition.Center;
            text.EffectsEnabled = true;
            text.RefreshEffects();
            Color[] opaque = Render();
            Assert.That(CountColored(opaque, Color.red), Is.GreaterThan(100));
            Assert.That(CountColored(opaque, Color.blue), Is.GreaterThan(100));
            for (int i = 0; i < opaque.Length; i++)
            {
                float x = i % Resolution + 0.5f - Resolution / 2f;
                if (opaque[i].r > 0.9f && opaque[i].b < 0.1f) Assert.That(x, Is.LessThan(-25));
                if (opaque[i].b > 0.9f && opaque[i].r < 0.1f) Assert.That(x, Is.GreaterThan(25));
                if (Mathf.Abs(x) > 76) Assert.That(opaque[i].a, Is.LessThan(0.02f), "Shifted outline escaped RectMask2D.");
            }
            CanvasGroup group = text.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.35f;
            Color[] faded = Render();
            int samples = 0;
            for (int i = 0; i < face.Length; i++)
            {
                if (face[i].a > 0.01f || opaque[i].a < 0.99f) continue;
                Assert.That(faded[i].a, Is.EqualTo(opaque[i].a * group.alpha).Within(0.025f));
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(100));
            SaveCapture("tmp-multi-outline-offset-mask.png", opaque);
            group.alpha = 0;
            Assert.That(CountVisible(Render()), Is.Zero);
        }

        [TestCase(14f, 8f, false, false, false)]
        [TestCase(-14f, -8f, true, false, false)]
        [TestCase(150f, 0f, false, false, false)]
        [TestCase(0f, 150f, true, false, false)]
        [TestCase(1000f, -1000f, false, false, false)]
        [TestCase(14f, 8f, true, true, false)]
        [TestCase(0f, 0f, false, false, true)]
        [TestCase(8f, -6f, false, false, true)]
        [TestCase(-8f, 6f, true, false, true)]
        [TestCase(1000f, -1000f, false, false, true)]
        [TestCase(8f, -6f, true, true, true)]
        public void InnerOffset_IsMaskedByTheOriginalGlyphIncludingHoles(float x, float y, bool transformed, bool fallbackFont, bool underlay)
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.fontSize = 160;
            if (fallbackFont)
            {
                TMP_FontAsset primary = CreateFont("A"), fallback = CreateFont("O");
                primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                text.font = primary;
                text.text = "AO";
                text.fontSize = 100;
                text.characterSpacing = 0;
            }
            if (transformed)
            {
                text.rectTransform.localScale = new Vector3(1.1f, 0.85f, 1);
                text.rectTransform.localRotation = Quaternion.Euler(0, 0, 17);
                text.fontStyle = FontStyles.Italic | FontStyles.Bold;
            }
            text.Layers.Clear();
            // Keep the reference's TMP atlas padding identical to the tested render.
            // Enabling effects changes padded italic quads slightly, even without a visible effect.
            var layer = new SdfTextEffect { Width = -10000, Color = Color.red };
            text.Layers.Add(layer);
            text.RefreshEffects();
            Color[] face = Render();
            Assert.That(CountColored(face, Color.white), Is.GreaterThan(100));
            if (fallbackFont) Assert.That(text.textInfo.materialCount, Is.EqualTo(2));
            layer.Position = underlay ? SdfOutlinePosition.Underlay : SdfOutlinePosition.Inner;
            layer.UnderlayType = underlay ? SdfTextUnderlayType.Inner : SdfTextUnderlayType.Normal;
            layer.Width = underlay ? -4 : 4;
            layer.Softness = 1;
            layer.Offset = new Vector2(x, y);
            text.RefreshEffects();
            Color[] actual = Render();
            SaveCapture("tmp-inner-masked-offset-" + x + "-" + y + "-underlay-" + underlay + ".png", actual);
            Assert.That(CountExteriorEffect(face, actual), Is.Zero,
                "Inner must stay inside the original glyph, including its holes, when Offset moves the border.");
            if (Mathf.Abs(x) < 20 && Mathf.Abs(y) < 20)
                Assert.That(CountColored(actual, Color.red), Is.GreaterThan(10), "The mask must keep the part intersecting the glyph.");
            else if (underlay)
            {
                int samples = 0;
                for (int i = 0; i < face.Length; i++)
                {
                    if (face[i].a < 0.999f) continue;
                    Assert.That(actual[i].r, Is.GreaterThan(0.98f));
                    Assert.That(actual[i].g, Is.LessThan(0.02f), "A distant shadow must cover the face, without neighboring atlas glyphs cutting holes in it.");
                    samples++;
                }
                Assert.That(samples, Is.GreaterThan(100));
            }
            else
            {
                Assert.That(CountColored(actual, Color.red), Is.Zero, "Large offsets must not sample neighboring glyphs in the atlas.");
                AssertFaceUnchanged(face, actual);
            }
        }

        [Test]
        public void UnderlayType_SwitchesBetweenNormalAndInnerWithoutLosingSettings()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.fontSize = 180;
            Color[] face = Render();
            var layer = Outline(Color.red, 0, new Vector2(8, 0));
            text.Layers.Add(layer);
            text.RefreshEffects();
            Color[] normal = Render();
            Assert.That(CountExteriorEffect(face, normal), Is.GreaterThan(100));
            AssertFaceUnchanged(face, normal);

            layer.UnderlayType = SdfTextUnderlayType.Inner;
            text.RefreshEffects();
            Color[] inner = Render();
            Assert.That(CountColored(inner, Color.red), Is.GreaterThan(100));
            Assert.That(CountColored(inner, Color.white), Is.GreaterThan(100));
            SaveCapture("tmp-underlay-inner.png", inner);

            layer.Enabled = false;
            text.RefreshEffects();
            Assert.That(Render(), Is.EqualTo(face), "The layer switch must hide this underlay.");
            Assert.That(layer.Offset, Is.EqualTo(new Vector2(8, 0)));
            Assert.That(layer.Color, Is.EqualTo(Color.red));
            layer.UnderlayType = SdfTextUnderlayType.Normal;
            layer.Enabled = true;
            text.RefreshEffects();
            Assert.That(Render(), Is.EqualTo(normal));

            layer.UnderlayType = SdfTextUnderlayType.Inner;
            layer.Position = SdfOutlinePosition.Inner;
            layer.Width = 4;
            text.RefreshEffects();
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(30), "Underlay Type must not change other Positions.");
        }

        [Test]
        public void InnerUnderlay_OffsetCastsDirectionalShadowAndSignedSpreadControlsItsSize()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.fontSize = 180;
            var layer = new SdfTextEffect { UnderlayType = SdfTextUnderlayType.Inner, Color = Color.red, Width = 0, Offset = new Vector2(8, 0) };
            text.Layers.Add(layer);
            text.RefreshEffects();
            Color[] rightOffset = Render();
            layer.Offset = new Vector2(-8, 0);
            text.RefreshEffects();
            Color[] leftOffset = Render();
            float rightSum = 0, leftSum = 0;
            int rightCount = 0, leftCount = 0;
            for (int i = 0; i < rightOffset.Length; i++)
            {
                if (rightOffset[i].a > 0.99f && rightOffset[i].g < 0.01f) { rightSum += i % Resolution; rightCount++; }
                if (leftOffset[i].a > 0.99f && leftOffset[i].g < 0.01f) { leftSum += i % Resolution; leftCount++; }
            }
            Assert.That(rightCount, Is.GreaterThan(100));
            Assert.That(leftCount, Is.GreaterThan(100));
            Assert.That(rightSum / rightCount, Is.LessThan(leftSum / leftCount - 5), "The inner shadow must fall on the opposite side of the shifted silhouette.");
            layer.Spread = 4;
            text.RefreshEffects();
            int smaller = CountColored(Render(), Color.red);
            layer.Spread = -4;
            text.RefreshEffects();
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(smaller + 100));
        }

        [Test]
        public void MultipleOutlines_GroupEveryFallbackMaterialByStyleAndFollowCustomFallbackGeometry()
        {
            SdfText text = CreateText(canvas.transform, "AVAVA");
            TMP_FontAsset primary = CreateFont("A"), fallback = CreateFont("V");
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            text.font = primary;
            Color[] face = Render();
            Assert.That(text.textInfo.materialCount, Is.EqualTo(2));
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 3, Vector2.zero));
            text.Layers.Add(Outline(Color.blue, 8, new Vector2(0, -2)));
            text.EffectsEnabled = true;
            text.ShadowEnabled = true;
            text.ShadowColor = Color.black;
            text.ShadowOffset = new Vector2(4, -5);
            text.RefreshEffects();
            Color[] effects = Render();
            AssertFaceUnchanged(face, effects);
            Assert.That(CountColored(effects, Color.red), Is.GreaterThan(50));
            Assert.That(CountColored(effects, Color.blue), Is.GreaterThan(50));
            SaveCapture("tmp-multi-outline-fallback.png", effects);
            var layers = new List<SdfTextLayer>();
            foreach (SdfTextLayer layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                if (layer.Owner == text && layer.canvasRenderer.GetMesh() && layer.canvasRenderer.GetMesh().vertexCount > 0)
                    layers.Add(layer);
            layers.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            Assert.That(layers.Count, Is.EqualTo(6));
            var styleColors = new List<Vector4>();
            for (int i = 0; i < layers.Count; i++)
            {
                layers[i].canvasRenderer.GetMesh().GetUVs(3, styleColors);
                Assert.That(styleColors[0],
                    Is.EqualTo((Vector4)(i < 2 ? Color.black : i < 4 ? Color.blue : Color.red)),
                    "All fallback materials of a rear style must render before the next front style.");
            }

            TMP_SubMeshUI sub = text.GetComponentInChildren<TMP_SubMeshUI>();
            Mesh replacement = Object.Instantiate(sub.canvasRenderer.GetMesh());
            try
            {
                Vector3[] moved = replacement.vertices;
                for (int i = 0; i < moved.Length; i++) moved[i].y += 12;
                replacement.vertices = moved;
                text.UpdateGeometry(replacement, 1);
                for (int frame = 0; frame < 2; frame++)
                {
                    Render();
                    int matched = 0;
                    foreach (SdfTextLayer layer in layers)
                    {
                        if (layer.mainTexture != fallback.material.mainTexture) continue;
                        CollectionAssert.AreEqual(sub.canvasRenderer.GetMesh().vertices, layer.canvasRenderer.GetMesh().vertices);
                        matched++;
                    }
                    Assert.That(matched, Is.EqualTo(3), "Both outlines and the shadow must follow the uploaded fallback mesh.");
                }
            }
            finally { text.ClearMesh(); Object.DestroyImmediate(replacement); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangingTextEmptyingAndDisabling_ClearsEveryEffectLayer(bool inner)
        {
            SdfText text = CreateText(canvas.transform, "AVAVA");
            EnableEffects(text);
            if (inner) text.Layers[0].Position = SdfOutlinePosition.Inner;
            Assert.That(CountVisible(Render()), Is.GreaterThan(200));
            text.ClearMesh();
            Assert.That(CountVisible(Render()), Is.Zero, "ClearMesh must clear the borrowed effect meshes too.");
            text.text = "AVA";
            Assert.That(CountVisible(Render()), Is.GreaterThan(100));
            text.text = string.Empty;
            Assert.That(CountVisible(Render()), Is.Zero, "Empty text must not retain an old shadow or outline mesh.");
            text.text = "O";
            Assert.That(CountVisible(Render()), Is.GreaterThan(100));
            text.enabled = false;
            Assert.That(CountVisible(Render()), Is.Zero, "Disabling the component must hide its sibling effects.");
            text.enabled = true;
            Assert.That(CountVisible(Render()), Is.GreaterThan(100));
            text.gameObject.SetActive(false);
            Assert.That(CountVisible(Render()), Is.Zero, "Deactivating the source must hide its sibling effects.");
        }

        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(1, true)]
        [TestCase(3, true)]
        public void CustomUpdateGeometry_EffectsKeepFollowingTheUploadedFaceMeshAcrossRenderUpdates(int outlineCount, bool inner)
        {
            SdfText text = CreateText(canvas.transform, "O");
            EnableEffects(text);
            if (inner)
            {
                text.Layers[0].Position = SdfOutlinePosition.Inner;
                text.Layers[0].Offset = new Vector2(5, 2);
            }
            for (int i = 1; i < outlineCount; i++) text.Layers.Add(Outline(Color.red, 3, Vector2.zero));
            text.RefreshEffects();
            Render();
            Mesh replacement = Object.Instantiate(text.canvasRenderer.GetMesh());
            try
            {
                Vector3[] vertices = replacement.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i].x += 70;
                replacement.vertices = vertices;
                text.UpdateGeometry(replacement, 0);
                for (int frame = 0; frame < 2; frame++)
                {
                    Color[] pixels = Render();
                    Assert.That(CountVisible(pixels), Is.GreaterThan(100));
                    for (int y = 0; y < Resolution; y++)
                    for (int x = 0; x < Resolution / 2 + 20; x++)
                        Assert.That(pixels[y * Resolution + x].a, Is.LessThan(0.02f),
                            "An effect reverted to the original textInfo mesh after UpdateGeometry.");

                    Vector3[] faceVertices = text.canvasRenderer.GetMesh().vertices;
                    int matched = 0;
                    foreach (SdfTextLayer layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                    {
                        if (layer.Owner != text) continue;
                        bool above = layer.transform.parent.GetSiblingIndex() > text.transform.GetSiblingIndex();
                        var expectedVertices = new List<Vector3>();
                        for (int i = text.Layers.Count - 1; i >= 0; i--)
                        {
                            var effect = text.Layers[i];
                            bool innerEffect = effect.Position == SdfOutlinePosition.Inner ||
                                effect.Position == SdfOutlinePosition.Underlay && effect.UnderlayType == SdfTextUnderlayType.Inner;
                            if ((innerEffect || effect.Position == SdfOutlinePosition.Center) != above) continue;
                            Vector3 offset = innerEffect ? Vector3.zero : (Vector3)effect.Offset;
                            foreach (Vector3 vertex in faceVertices) expectedVertices.Add(vertex + offset);
                            matched++;
                        }
                        // Each combined block must follow the replacement mesh, with its authored offset.
                        CollectionAssert.AreEqual(expectedVertices, layer.canvasRenderer.GetMesh().vertices);
                    }
                    Assert.That(matched, Is.EqualTo(outlineCount + 1));
                }
            }
            finally
            {
                text.ClearMesh();
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void TextEnteringRectMaskDuringMeshRebuild_UncullsEffectsWithoutRegisteringAnotherRebuild()
        {
            var mask = (RectTransform)NewObject("Late text clip", typeof(RectTransform),
                typeof(UnityEngine.UI.RectMask2D)).transform;
            mask.SetParent(canvas.transform, false);
            mask.sizeDelta = new Vector2(60, 100);
            SdfText text = CreateText(mask, "O");
            text.alignment = TextAlignmentOptions.Left;
            EnableEffects(text);
            Assert.That(CountVisible(Render()), Is.Zero, "The initial left-aligned glyph must lie outside the clip.");
            text.alignment = TextAlignmentOptions.Center;
            Assert.That(CountVisible(Render()), Is.GreaterThan(100));
            foreach (SdfTextLayer layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                Assert.That(layer.canvasRenderer.cull, Is.False, "Newly visible effects must uncull in the same rebuild.");
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void AncestorMask_ClipsBothTextAndExpandedEffects(bool stencil, bool center)
        {
            var mask = (RectTransform)NewObject("Text Mask", typeof(RectTransform)).transform;
            mask.SetParent(canvas.transform, false);
            mask.sizeDelta = new Vector2(40, 80);
            if (stencil)
            {
                mask.gameObject.AddComponent<UnityEngine.UI.Image>();
                mask.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            }
            else mask.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            SdfText text = CreateText(mask, "AVAVA");
            EnableEffects(text);
            text.OutlineWidth = 1;
            if (center) text.Layers[0].Position = SdfOutlinePosition.Center;
            text.ShadowEnabled = false;
            Color[] thin = Render();
            text.OutlineWidth = 8;
            Color[] pixels = Render();
            Assert.That(CountVisible(pixels), Is.GreaterThan(100));
            Assert.That(CountVisible(pixels), Is.GreaterThan(CountVisible(thin) + 10),
                "Changing effect style must also refresh a material derived for stencil masking.");
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
                if (Mathf.Abs(x + 0.5f - Resolution / 2f) > 22)
                    Assert.That(pixels[y * Resolution + x].a, Is.LessThan(0.02f),
                        "Effect leaked beyond the ancestor mask at " + x + "," + y);
        }

        [Test]
        public void CanvasGroupOnSource_FadesSiblingOutlineAndShadow()
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color[] face = Render();
            EnableEffects(text);
            text.ShadowEnabled = false;
            Color[] opaque = Render();
            CanvasGroup group = text.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.35f;
            Color[] faded = Render();
            int samples = 0;
            for (int i = 0; i < opaque.Length; i++)
            {
                if (face[i].a > 0.01f || opaque[i].a < 0.95f) continue;
                Assert.That(faded[i].a, Is.EqualTo(opaque[i].a * group.alpha).Within(0.045f),
                    "A CanvasGroup on the text itself must also fade its sibling effect.");
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(50));
            group.alpha = 0;
            text.ShadowEnabled = true;
            Assert.That(CountVisible(Render()), Is.Zero);
        }

        [Test]
        public void NegativeShadowSpread_ContractsTheShadowSilhouette()
        {
            SdfText text = CreateText(canvas.transform, "O");
            Color[] face = Render();
            text.ShadowEnabled = true;
            text.ShadowColor = Color.black;
            text.ShadowOffset = new Vector2(20, -12);
            text.ShadowBlur = 0;
            text.ShadowSpread = 0;
            Color[] normal = Render();
            text.ShadowSpread = -3;
            Color[] contracted = Render();
            Assert.That(CountExteriorEffect(face, normal), Is.GreaterThan(100));
            Assert.That(CountExteriorEffect(face, contracted),
                Is.LessThan(CountExteriorEffect(face, normal) - 10),
                "Negative spread must shrink the shadow, not clamp to zero.");
        }

        [Test]
        public void Inspector_EditsTheSavedFontMaterialInsteadOfTheRenderClone()
        {
            string folder = CreateSerializationFolder();
            var preset = new Material(font.material) { name = "Editable SDF Text Preset" };
            string path = folder + "/Preset.mat";
            AssetDatabase.CreateAsset(preset, path);
            UnityEditor.Editor inspector = null, renderInspector = null;
            try
            {
                SdfText text = CreateText(canvas.transform, "EDIT");
                text.fontSharedMaterial = preset;
                EnableEffects(text);
                Render();
                Material renderMaterial = text.canvasRenderer.GetMaterial();
                Assert.That(renderMaterial, Is.Not.EqualTo(preset));
                renderInspector = UnityEditor.Editor.CreateEditor(renderMaterial);
                var hidden = typeof(EditorUtility).GetMethod("IsHiddenInInspector", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(UnityEditor.Editor) }, null);
                Assert.That(hidden, Is.Not.Null);
                Assert.That(hidden.Invoke(null, new object[] { renderInspector }), Is.True,
                    "The temporary, read-only SDF Text Face material must not be presented as the editable font material.");

                inspector = UnityEditor.Editor.CreateEditor(text);
                var field = inspector.GetType().GetField("fontMaterialEditor", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                var materialEditor = (MaterialEditor)field.GetValue(inspector);
                Assert.That(materialEditor.target, Is.EqualTo(preset));
                Assert.That(materialEditor.target.hideFlags & HideFlags.NotEditable, Is.EqualTo(HideFlags.None));
                Assert.That(AssetDatabase.GetAssetPath(materialEditor.target), Is.EqualTo(path));

                Undo.IncrementCurrentGroup();
                materialEditor.RegisterPropertyChangeUndo("Edit SDF Text Face");
                preset.SetColor("_FaceColor", Color.green);
                preset.SetFloat("_FaceDilate", 0.15f);
                Undo.FlushUndoRecordObjects();
                TMPro_EventManager.ON_MATERIAL_PROPERTY_CHANGED(true, preset);
                Render();
                Assert.That(text.canvasRenderer.GetMaterial().GetColor("_FaceColor"), Is.EqualTo(Color.green));
                Assert.That(text.canvasRenderer.GetMaterial().GetFloat("_FaceDilate"), Is.EqualTo(0.15f).Within(0.000001f));
                Assert.That(text.fontSharedMaterial, Is.EqualTo(preset));
                Undo.PerformUndo();
                Render();
                Assert.That(preset.GetColor("_FaceColor"), Is.EqualTo(Color.white));
                Assert.That(text.canvasRenderer.GetMaterial().GetColor("_FaceColor"), Is.EqualTo(Color.white));
                Undo.PerformRedo();
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(path).GetFloat("_FaceDilate"), Is.EqualTo(0.15f).Within(0.000001f));
                Assert.That(AssetDatabase.LoadAssetAtPath<Material>(path).GetColor("_FaceColor"), Is.EqualTo(Color.green));
            }
            finally
            {
                if (inspector) Object.DestroyImmediate(inspector);
                if (renderInspector) Object.DestroyImmediate(renderInspector);
                Undo.ClearUndo(preset);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IdenticalStyles_ShareMaterialsAndChangingOneLabelDoesNotChangeTheOther(bool stencil)
        {
            Transform parent = canvas.transform;
            if (stencil)
            {
                var mask = (RectTransform)NewObject("Shared style mask", typeof(RectTransform),
                    typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask)).transform;
                mask.SetParent(parent, false);
                mask.sizeDelta = new Vector2(256, 200);
                mask.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                parent = mask;
            }
            SdfText first = CreateText(parent, "O"), second = CreateText(parent, "O");
            first.rectTransform.anchoredPosition = new Vector2(-60, 0);
            second.rectTransform.anchoredPosition = new Vector2(60, 0);
            foreach (var text in new[] { first, second })
            {
                text.fontSize = 80;
                text.Layers.Clear();
                text.Layers.Add(Outline(Color.red, 4, new Vector2(2, -1)));
                text.RefreshEffects();
            }
            Color[] before = Render();
            Material sharedFace = second.canvasRenderer.GetMaterial();
            SdfTextLayer firstLayer = OnlyEffect(first), secondLayer = OnlyEffect(second);
            Material sharedEffect = secondLayer.canvasRenderer.GetMaterial();
            Assert.That(first.canvasRenderer.GetMaterial(), Is.SameAs(sharedFace), "Matching faces must batch across labels.");
            Assert.That(firstLayer.canvasRenderer.GetMaterial(), Is.SameAs(sharedEffect), "Matching styles must batch, including stencil variants.");

            first.Layers[0].Color = Color.green;
            first.Layers[0].Width = 9;
            first.Layers[0].Position = SdfOutlinePosition.Inner;
            first.RefreshEffects();
            Color[] changed = Render();
            Assert.That(firstLayer.canvasRenderer.GetMaterial(), Is.SameAs(sharedEffect), "Different styles must still share a material.");
            Assert.That(secondLayer.canvasRenderer.GetMaterial(), Is.SameAs(sharedEffect));
            Assert.That(CountColored(changed, Color.green), Is.GreaterThan(10));
            for (int i = 0; i < before.Length; i++)
                if (i % Resolution >= Resolution / 2)
                    Assert.That(changed[i], Is.EqualTo(before[i]), "Editing one label must not mutate another label's shared style.");

            first.Layers[0].Color = Color.red;
            first.Layers[0].Width = 4;
            first.Layers[0].Position = SdfOutlinePosition.Underlay;
            first.RefreshEffects();
            Render();
            Assert.That(firstLayer.canvasRenderer.GetMaterial(), Is.SameAs(sharedEffect), "Restoring a style must rejoin its existing batch.");
            first.EffectsEnabled = second.EffectsEnabled = false;
            Render();
            Assert.That(first.canvasRenderer.GetMaterial(), Is.SameAs(second.canvasRenderer.GetMaterial()), "Disabling effects must keep faces batchable.");
        }

        [Test]
        public void SharedMaterials_StayAliveForOtherLabelsAndReleaseAfterTheLastOwner()
        {
            SdfText first = CreateText(canvas.transform, "A"), second = CreateText(canvas.transform, "O");
            foreach (var text in new[] { first, second })
            {
                text.Layers.Clear();
                text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
                text.RefreshEffects();
            }
            Render();
            Material face = first.canvasRenderer.GetMaterial(), effect = OnlyEffect(first).material;
            Assert.That(second.canvasRenderer.GetMaterial(), Is.SameAs(face));
            Assert.That(OnlyEffect(second).material, Is.SameAs(effect));
            Object.DestroyImmediate(first.gameObject);
            Assert.That(CountVisible(Render()), Is.GreaterThan(100));
            Assert.That(face && effect, Is.True, "Removing one user must keep shared materials alive.");
            Object.DestroyImmediate(second.gameObject);
            Assert.That(face == null && effect == null, Is.True, "Materials must be released with their last user.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SharedMaterials_FollowSourcePresetAnimationWithoutChangingMaterialIdentity(bool stencil)
        {
            var preset = new Material(font.material);
            try
            {
                Transform parent = canvas.transform;
                if (stencil)
                {
                    var mask = (RectTransform)NewObject("Animated preset mask", typeof(RectTransform),
                        typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Mask)).transform;
                    mask.SetParent(parent, false);
                    mask.sizeDelta = new Vector2(256, 200);
                    mask.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
                    parent = mask;
                }
                SdfText first = CreateText(parent, "A"), second = CreateText(parent, "O");
                foreach (var text in new[] { first, second })
                {
                    text.fontSharedMaterial = preset;
                    text.Layers.Clear();
                    text.Layers.Add(Outline(Color.red, 3, Vector2.zero));
                    text.RefreshEffects();
                }
                Render();
                Material face = first.canvasRenderer.GetMaterial(), effect = OnlyEffect(first).canvasRenderer.GetMaterial();
                preset.SetColor("_FaceColor", new Color(0, 1, 0, 0.7f));
                preset.SetFloat("_FaceDilate", 0.12f);
                Render(); // No text rebuild or material-change event, including same-frame edits.
                Assert.That(first.canvasRenderer.GetMaterial(), Is.SameAs(face));
                Assert.That(second.canvasRenderer.GetMaterial(), Is.SameAs(face));
                Assert.That(OnlyEffect(first).canvasRenderer.GetMaterial(), Is.SameAs(effect));
                Assert.That(effect.GetColor("_FaceColor"), Is.EqualTo(preset.GetColor("_FaceColor")));
                Assert.That(face.GetColor("_FaceColor"), Is.EqualTo(preset.GetColor("_FaceColor")));
                Assert.That(effect.GetFloat("_FaceDilate"), Is.EqualTo(0.12f));
                Assert.That(face.GetFloat("_FaceDilate"), Is.EqualTo(0.12f));
                Assert.That(effect.GetInt("_Stencil"), Is.EqualTo(stencil ? 1 : 0));
            }
            finally { Object.DestroyImmediate(preset); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CachedEffectGeometry_FollowsNativeTmpScaleUpdates(bool inner)
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(new SdfTextEffect { Position = inner ? SdfOutlinePosition.Inner : SdfOutlinePosition.Underlay, Width = 3, Offset = new Vector2(2, 1), Color = Color.red });
            text.RefreshEffects();
            Render();
            text.rectTransform.localScale = Vector3.one * 1.5f;
            Render();
            var faceUvs = new List<Vector4>();
            var effectUvs = new List<Vector4>();
            text.canvasRenderer.GetMesh().GetUVs(0, faceUvs);
            OnlyEffect(text).canvasRenderer.GetMesh().GetUVs(0, effectUvs);
            Assert.That(effectUvs.Count, Is.EqualTo(faceUvs.Count));
            for (int i = 0; i < faceUvs.Count; i++)
            {
                Assert.That(effectUvs[i].x, Is.EqualTo(faceUvs[i].x));
                Assert.That(effectUvs[i].y, Is.EqualTo(faceUvs[i].y));
                Assert.That(effectUvs[i].w, Is.EqualTo(faceUvs[i].w), "TMP's UV scale changes outside UpdateGeometry; effects must follow in the same render.");
            }
            text.CrossFadeAlpha(0.5f, 0, true);
            Render();
            Assert.That(OnlyEffect(text).canvasRenderer.GetColor().a, Is.EqualTo(text.canvasRenderer.GetColor().a));
        }

        [Test]
        public void CachedEffectIndices_FollowCustomGeometryWithUnchangedVertexAndIndexCounts()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 3, Vector2.zero));
            text.RefreshEffects();
            Render();
            Mesh replacement = Object.Instantiate(text.canvasRenderer.GetMesh());
            try
            {
                int[] original = replacement.GetIndices(0);
                for (int update = 0; update < 2; update++)
                {
                    Array.Reverse(original);
                    replacement.SetIndices(original, MeshTopology.Triangles, 0, false);
                    text.UpdateGeometry(replacement, 0);
                    Render();
                    CollectionAssert.AreEqual(original, OnlyEffect(text).canvasRenderer.GetMesh().GetIndices(0));
                }
            }
            finally { text.ClearMesh(); Object.DestroyImmediate(replacement); }
        }

        [Test]
        public void CachedPadding_RestoresNativeGeometryAfterTogglingEffectsAndExtraPadding()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.EffectsEnabled = false;
            Render();
            Vector3[] original = text.canvasRenderer.GetMesh().vertices;
            foreach (bool extra in new[] { true, false })
            {
                text.EffectsEnabled = true;
                text.extraPadding = extra;
                Render();
                text.EffectsEnabled = false;
                text.extraPadding = false;
                text.text = "A";
                Render();
                text.text = "O";
                Render();
                CollectionAssert.AreEqual(original, text.canvasRenderer.GetMesh().vertices,
                    "Cached padding must restore native geometry, without retaining the expanded effect border.");
            }
        }

        [Test]
        public void SingleAtlas_CombinesLayersPerSideAndClearsUnusedRenderers()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 3, Vector2.zero));
            text.Layers.Add(Outline(Color.blue, 6, new Vector2(2, -2)));
            text.RefreshEffects();
            Render();
            AssertActiveEffects(text, 1, 2);
            text.Layers[0].Position = SdfOutlinePosition.Inner;
            text.RefreshEffects();
            Render();
            AssertActiveEffects(text, 2, 2);
            text.Layers.RemoveAt(1);
            text.RefreshEffects();
            Render();
            AssertActiveEffects(text, 1, 1);
        }

        private void AssertActiveEffects(SdfText text, int expectedRenderers, int expectedCopies)
        {
            int renderers = 0, copies = 0;
            foreach (var layer in canvas.GetComponentsInChildren<SdfTextLayer>())
            {
                Mesh mesh = layer.canvasRenderer.GetMesh();
                if (layer.Owner != text || !mesh || mesh.vertexCount == 0) continue;
                renderers++;
                copies += mesh.vertexCount / text.canvasRenderer.GetMesh().vertexCount;
            }
            Assert.That(renderers, Is.EqualTo(expectedRenderers));
            Assert.That(copies, Is.EqualTo(expectedCopies), "Combining renderers must keep exactly one geometry block per effect.");
        }

        [Test]
        public void IdleFallbackFace_FollowsSourceMaterialAnimationWithoutAnExplicitRefresh()
        {
            TMP_FontAsset primary = CreateFont("A"), fallback = CreateFont("O");
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            SdfText text = CreateText(canvas.transform, "AO");
            text.font = primary;
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
            text.RefreshEffects();
            Render();
            TMP_SubMeshUI sub = text.GetComponentInChildren<TMP_SubMeshUI>();
            Assert.That(sub, Is.Not.Null);
            Material face = sub.canvasRenderer.GetMaterial();
            sub.sharedMaterial.SetColor("_FaceColor", Color.green);
            Render();
            Assert.That(sub.canvasRenderer.GetMaterial(), Is.SameAs(face));
            Assert.That(face.GetColor("_FaceColor"), Is.EqualTo(Color.green));
            foreach (var layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                if (layer.Owner == text && layer.mainTexture == sub.sharedMaterial.mainTexture)
                    Assert.That(layer.canvasRenderer.GetMaterial().GetColor("_FaceColor"), Is.EqualTo(Color.green));
        }

        [Test]
        public void IdleText_FollowsTransformAndRendererFadeWithoutAnExplicitRefresh()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, new Vector2(2, -3)));
            text.RefreshEffects();
            Render();
            Render();
            text.rectTransform.anchoredPosition = new Vector2(31, -12);
            text.rectTransform.localRotation = Quaternion.Euler(0, 0, 13);
            Color[] automatic = Render();
            text.RefreshEffects();
            CollectionAssert.AreEqual(automatic, Render(), "Transform changes must reach the effect before rendering.");
            text.CrossFadeAlpha(0.35f, 0, true);
            Render();
            Assert.That(OnlyEffect(text).canvasRenderer.GetColor().a, Is.EqualTo(0.35f).Within(0.0001f));
        }

        [Test]
        public void IdleText_FollowsAddedChangedAndRemovedCanvasGroup()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
            text.RefreshEffects();
            Color[] opaque = Render();
            var group = text.gameObject.AddComponent<CanvasGroup>();
            foreach (float alpha in new[] { 0.4f, 0.7f })
            {
                group.alpha = alpha;
                Color[] automatic = Render();
                text.RefreshEffects();
                CollectionAssert.AreEqual(automatic, Render(), "A CanvasGroup changed during idle must propagate without RefreshEffects.");
            }
            Object.DestroyImmediate(group);
            CollectionAssert.AreEqual(opaque, Render(), "Removing the source CanvasGroup must restore effect opacity.");
        }

        [Test]
        public void IdleText_RepairsSiblingOrderWhenAnotherGraphicMovesBetweenEffectAndFace()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
            text.RefreshEffects();
            Render();
            var other = NewObject("Reordered sibling", typeof(RectTransform));
            other.transform.SetParent(canvas.transform, false);
            other.transform.SetSiblingIndex(text.transform.GetSiblingIndex());
            Render();
            Assert.That(OnlyEffect(text).transform.parent.GetSiblingIndex(), Is.EqualTo(text.transform.GetSiblingIndex() - 1));
        }

        [Test]
        public void IdleText_DisablesEffectsWhenRectMaskIsAddedDirectlyToText()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
            text.RefreshEffects();
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(20));
            var mask = text.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            Assert.That(CountColored(Render(), Color.red), Is.Zero);
            Object.DestroyImmediate(mask);
            Assert.That(CountColored(Render(), Color.red), Is.GreaterThan(20));
        }

        [Test]
        public void IdleText_RecoversAfterSharedMaterialCacheReset()
        {
            SdfText text = CreateText(canvas.transform, "O");
            text.Layers.Clear();
            text.Layers.Add(Outline(Color.red, 4, Vector2.zero));
            text.RefreshEffects();
            Color[] before = Render();
            Type cache = typeof(SdfText).Assembly.GetType("SDFUI.SdfTextMaterials");
            cache.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            CollectionAssert.AreEqual(before, Render(), "Surviving labels must reacquire render materials after a cache reset.");
            Assert.That(text.canvasRenderer.GetMaterial(), Is.Not.Null);
            Assert.That(OnlyEffect(text).canvasRenderer.GetMaterial(), Is.Not.Null);
        }

        private SdfTextLayer OnlyEffect(SdfText owner)
        {
            foreach (var layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                if (layer.Owner == owner) return layer;
            Assert.Fail("Expected an effect renderer for the label.");
            return null;
        }

        [Test]
        public void Effects_PreserveSharedFontMaterialAndRenderImmediatelyBeforeSource()
        {
            string materialBefore = EditorJsonUtility.ToJson(font.material);
            SdfText text = CreateText(canvas.transform, "AVAVA");
            Vector2 preferred = text.GetPreferredValues();
            Vector2 rectangle = text.rectTransform.sizeDelta;
            EnableEffects(text);
            Render();
            Assert.That(text.GetPreferredValues(), Is.EqualTo(preferred));
            Assert.That(text.rectTransform.sizeDelta, Is.EqualTo(rectangle));
            SdfTextLayer[] layers = canvas.GetComponentsInChildren<SdfTextLayer>(true);
            Assert.That(layers.Length, Is.GreaterThan(0));
            foreach (SdfTextLayer layer in layers)
            {
                Assert.That(layer.Owner, Is.SameAs(text));
                Assert.That(layer.raycastTarget, Is.False);
                Transform root = layer.transform;
                while (root.parent != text.transform.parent && root.parent) root = root.parent;
                Assert.That(root.parent, Is.SameAs(text.transform.parent));
                Assert.That(root.GetSiblingIndex(), Is.EqualTo(text.transform.GetSiblingIndex() - 1));
            }
            text.OutlineWidth = 12;
            text.ShadowBlur = 8;
            text.RefreshEffects();
            Render();
            text.enabled = false;
            Assert.That(EditorJsonUtility.ToJson(font.material), Is.EqualTo(materialBefore),
                "Per-text styles must not modify the shared font material asset.");
        }

        private SdfText CreateText(Transform parent, string value)
        {
            var text = NewObject("SDF Text Test", typeof(RectTransform), typeof(SdfText)).GetComponent<SdfText>();
            text.transform.SetParent(parent, false);
            text.rectTransform.sizeDelta = new Vector2(240, 100);
            text.font = font;
            text.fontSize = 64;
            text.characterSpacing = -25;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.color = Color.white;
            text.text = value;
            text.OutlineEnabled = false;
            text.ShadowEnabled = false;
            return text;
        }

        private static SdfTextEffect Outline(Color color, float width, Vector2 offset) =>
            new SdfTextEffect { Enabled = true, Width = width, Softness = 0, Color = color, Offset = offset };

        private static int CountColored(Color[] pixels, Color color)
        {
            int count = 0;
            foreach (Color pixel in pixels)
                if (pixel.a > 0.95f && Mathf.Abs(pixel.r - color.r) < 0.05f &&
                    Mathf.Abs(pixel.g - color.g) < 0.05f && Mathf.Abs(pixel.b - color.b) < 0.05f) count++;
            return count;
        }

        private static string CreateSerializationFolder()
        {
            string name = "SdfTextSerializationTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            return "Assets/" + name;
        }

        private static void EnableEffects(SdfText text)
        {
            text.OutlineEnabled = true;
            text.OutlineWidth = 8;
            text.OutlineSoftness = 0;
            text.OutlineColor = new Color(0.05f, 0.02f, 0.02f, 1);
            text.ShadowEnabled = true;
            text.ShadowOffset = new Vector2(5, -5);
            text.ShadowBlur = 3;
            text.ShadowSpread = 2;
            text.ShadowColor = new Color(0.02f, 0.02f, 0.1f, 0.8f);
            text.RefreshEffects();
        }

        private TMP_FontAsset CreateFont(string characters, int samplingSize = 90, int padding = 20, int atlasSize = 512)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(
                AssetDatabase.GUIDToAssetPath("e3265ab4bf004d28a9537516768c1c75"));
            Assert.That(source, Is.Not.Null);
            TMP_FontAsset result = TMP_FontAsset.CreateFontAsset(source, samplingSize, padding,
                GlyphRenderMode.SDFAA, atlasSize, atlasSize);
            generatedFonts.Add(result);
            result.name = "SDF Test Font " + characters;
            Assert.That(result.TryAddCharacters(characters), Is.True);
            result.atlasPopulationMode = AtlasPopulationMode.Static;
            return result;
        }

        private Color[] Render()
        {
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline == null) camera.Render();
            else
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readback.Apply();
                foreach (SdfTextLayer layer in canvas.GetComponentsInChildren<SdfTextLayer>())
                {
                    Material material = layer.materialForRendering;
                    if (!material) continue;
                    Assert.That(material.shader.isSupported, Is.True);
                    foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                        Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
                }
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(readback);
            }
        }

        private GameObject NewObject(string name, params Type[] components)
        {
            var result = new GameObject(name, components);
            SceneManager.MoveGameObjectToScene(result, previewScene);
            return result;
        }

        private static void AssertFaceUnchanged(Color[] face, Color[] effects)
        {
            int samples = 0;
            for (int i = 0; i < face.Length; i++)
            {
                int x = i % Resolution, y = i / Resolution;
                if (x == 0 || y == 0 || x == Resolution - 1 || y == Resolution - 1) continue;
                // Exclude the antialiased fringe: changing TMP padding can move its sampling fractionally.
                bool interior = true;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Color pixel = face[i + dy * Resolution + dx];
                    interior &= pixel.a > 0.99f && pixel.r > 0.99f && pixel.g > 0.99f && pixel.b > 0.99f;
                }
                if (!interior) continue;
                Assert.That(effects[i].r, Is.GreaterThan(0.975f), "Outline or shadow covered a glyph face at pixel " + i);
                Assert.That(effects[i].g, Is.GreaterThan(0.975f), "Outline or shadow covered a glyph face at pixel " + i);
                Assert.That(effects[i].b, Is.GreaterThan(0.975f), "Outline or shadow covered a glyph face at pixel " + i);
                samples++;
            }
            Assert.That(samples, Is.GreaterThan(100), "The baseline must contain opaque white glyph interiors.");
        }

        private static int CountExteriorEffect(Color[] face, Color[] effects)
        {
            int count = 0;
            for (int i = 0; i < face.Length; i++)
                if (face[i].a < 0.01f && effects[i].a > 0.4f) count++;
            return count;
        }

        private static int CountVisible(Color[] pixels)
        {
            int count = 0;
            foreach (Color pixel in pixels) if (pixel.a > 0.02f) count++;
            return count;
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
    }
}
