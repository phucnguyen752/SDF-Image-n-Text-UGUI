using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    public sealed class SdfImageTests
    {
        private GameObject canvasObject;
        private SdfSprite sprite;
        private Texture2D colorTexture;
        private Texture2D distanceTexture;
        private readonly List<Object> temporaryAssets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("SDF Test Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<Canvas>().referencePixelsPerUnit = 100;
            colorTexture = new Texture2D(64, 64);
            distanceTexture = new Texture2D(64, 64);
            sprite = ScriptableObject.CreateInstance<SdfSprite>();
            sprite.Initialize(null, colorTexture, distanceTexture, new Vector2Int(32, 32),
                new Vector4(4, 6, 8, 10), new Vector2(0.5f, 0.5f), 100, 16, 16);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(canvasObject);
            for (int i = temporaryAssets.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(temporaryAssets[i]);
            temporaryAssets.Clear();
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(colorTexture);
            Object.DestroyImmediate(distanceTexture);
        }

        [Test]
        public void StandardImageSourceProperty_ResolvesAttachedSdfWithOneComponent()
        {
            SdfImage component = CreateImage("Native Image API", assignBaked: false);
            Image image = component;
            Texture2D sourceTexture = CreateTemporaryTexture(32, 32);
            Sprite source = CreateSourceSprite(sourceTexture, new Rect(0, 0, 32, 32), sprite);

            image.sprite = source;

            Assert.That(component.SourceSprite, Is.EqualTo(source));
            AssertResolvedData(component, sprite);
            Assert.That(component.GetComponents<Image>().Length, Is.EqualTo(1));
            Assert.That(component.GetComponents<MonoBehaviour>().Length, Is.EqualTo(1),
                "Source assignment and rendering must be handled by the SdfImage itself.");
        }

        [Test]
        public void SpriteAndOverrideSwaps_OnOneSourceTexture_NeverKeepStaleSdfData()
        {
            SdfImage component = CreateImage("Sprite Swaps", assignBaked: false);
            Image image = component;
            Texture2D sourceTexture = CreateTemporaryTexture(96, 32);
            SdfSprite secondData = CreateTemporarySdf();
            Sprite first = CreateSourceSprite(sourceTexture, new Rect(0, 0, 32, 32), sprite);
            Sprite second = CreateSourceSprite(sourceTexture, new Rect(32, 0, 32, 32), secondData);
            Sprite unbaked = CreateSourceSprite(sourceTexture, new Rect(64, 0, 32, 32));

            image.sprite = first;
            AssertResolvedData(component, sprite);
            // Equal sprite dimensions and a shared RGBA texture exercise Image's
            // material-update optimization while the attached SDF textures differ.
            image.sprite = second;
            AssertResolvedData(component, secondData);
            image.overrideSprite = first;
            AssertResolvedData(component, sprite);
            image.sprite = unbaked;
            Assert.That(component.SourceSprite, Is.EqualTo(first), "The active override still takes precedence.");
            AssertResolvedData(component, sprite);

            image.overrideSprite = null;
            Assert.That(component.SourceSprite, Is.EqualTo(unbaked));
            Assert.That(component.SdfData, Is.Null);
            Assert.That(component.mainTexture, Is.EqualTo(sourceTexture));
            Assert.That(component.materialForRendering.shader, Is.EqualTo(Graphic.defaultGraphicMaterial.shader));

            image.sprite = first;
            AssertResolvedData(component, sprite);
        }

        [Test]
        public void AnimationCallbacks_ReuseAttachmentsUntilTheSourceChanges()
        {
            SdfImage image = CreateImage("Animated Image", assignBaked: false);
            Texture2D sourceTexture = CreateTemporaryTexture(64, 32);
            Sprite first = CreateSourceSprite(sourceTexture, new Rect(0, 0, 32, 32), sprite);
            SdfSprite secondData = CreateTemporarySdf();
            Sprite second = CreateSourceSprite(sourceTexture, new Rect(32, 0, 32, 32), secondData);
            image.sprite = first;
            var animationApplied = (System.Action)typeof(SdfImage)
                .GetMethod("OnDidApplyAnimationProperties", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate(typeof(System.Action), image);

            // Warm uGUI's dirty-registration pools before measuring repeated animation callbacks.
            for (int i = 0; i < 32; i++) animationApplied();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) animationApplied();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero,
                "Animating tint/effects with the same source must not allocate a Sprite attachment array each frame.");
            AssertResolvedData(image, sprite);

            // Animator writes serialized fields directly instead of calling Image.sprite's setter.
            typeof(Image).GetField("m_Sprite", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(image, second);
            animationApplied();
            Assert.That(image.SourceSprite, Is.EqualTo(second));
            AssertResolvedData(image, secondData);
        }

        [TestCase(Image.Type.Simple, 100f, 1f)]
        [TestCase(Image.Type.Sliced, 100f, 1f)]
        [TestCase(Image.Type.Sliced, 200f, 2f)]
        public void SourceImageLayout_MatchesStandardImageBeforeBakeAfterBakeAndWithEffectsDisabled(
            Image.Type type, float referencePixelsPerUnit, float multiplier)
        {
            canvasObject.GetComponent<Canvas>().referencePixelsPerUnit = referencePixelsPerUnit;
            var source = Sprite.Create(CreateTemporaryTexture(32, 32), new Rect(0, 0, 32, 32),
                new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(4, 6, 8, 10));
            temporaryAssets.Add(source);
            SdfImage image = CreateImage("Source Layout", assignBaked: false);
            image.sprite = source;
            image.type = type;
            image.pixelsPerUnitMultiplier = multiplier;

            var standardObject = new GameObject("Layout Reference", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            standardObject.transform.SetParent(canvasObject.transform, false);
            var standard = standardObject.GetComponent<Image>();
            standard.sprite = source;
            standard.type = type;
            standard.pixelsPerUnitMultiplier = multiplier;
            AssertStandardLayout(image, standard);

            sprite.Initialize(source, colorTexture, distanceTexture, new Vector2Int(32, 32), source.border,
                new Vector2(0.5f, 0.5f), 100, 16, 16);
            Assert.That(source.AddScriptableObject(sprite), Is.True);
            image.RefreshSdf();
            AssertResolvedData(image, sprite);
            AssertStandardLayout(image, standard);

            image.OutlineEnabled = false;
            image.ShadowEnabled = false;
            AssertStandardLayout(image, standard);
        }

        [Test]
        public void SourceWithoutSdf_UsesTheNormalImageMaterialAndFilledGeometry()
        {
            SdfImage component = CreateImage("Unbaked Fallback", assignBaked: false);
            Image image = component;
            Texture2D sourceTexture = CreateTemporaryTexture(32, 16);
            Sprite source = CreateSourceSprite(sourceTexture, new Rect(0, 0, 32, 16));
            image.sprite = source;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = 0.25f;

            var standardObject = new GameObject("Standard Image Reference", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            standardObject.transform.SetParent(canvasObject.transform, false);
            var standard = standardObject.GetComponent<Image>();
            standard.rectTransform.sizeDelta = image.rectTransform.sizeDelta;
            standard.sprite = source;
            standard.type = image.type;
            standard.fillMethod = image.fillMethod;
            standard.fillAmount = image.fillAmount;

            Assert.That(component.SdfData, Is.Null);
            Assert.That(component.mainTexture, Is.EqualTo(standard.mainTexture));
            Assert.That(component.materialForRendering.shader, Is.EqualTo(standard.materialForRendering.shader));
            Mesh actual = BuildMesh(component);
            Mesh expected = BuildMesh(standard);
            try
            {
                Assert.That(actual.vertexCount, Is.GreaterThan(0), "Missing SDF data must not make an ordinary Sprite disappear.");
                Assert.That(actual.vertices, Is.EqualTo(expected.vertices));
                Assert.That(actual.uv, Is.EqualTo(expected.uv));
                Assert.That(actual.triangles, Is.EqualTo(expected.triangles));
            }
            finally
            {
                Object.DestroyImmediate(actual);
                Object.DestroyImmediate(expected);
            }
        }

        [Test]
        public void EffectToggles_DisableTheirRenderingAndRetainAllStyleValues()
        {
            SdfImage component = CreateImage("Effect Toggles", assignBaked: false);
            Image image = component;
            Texture2D sourceTexture = CreateTemporaryTexture(32, 32);
            image.sprite = CreateSourceSprite(sourceTexture, new Rect(0, 0, 32, 32), sprite);
            Color outlineColor = new Color(1, 0.2f, 0.1f, 0.8f);
            Color shadowColor = new Color(0.2f, 0.3f, 0.4f, 0.7f);
            component.OutlineWidth = 6;
            component.OutlineSoftness = 1;
            component.OutlineColor = outlineColor;
            Assert.That(component.OutlineUseTextureColor, Is.False);
            Assert.That(component.OutlineTextureColorIntensity, Is.EqualTo(1));
            component.OutlineUseTextureColor = true;
            component.OutlineTextureColorIntensity = float.NaN;
            Assert.That(component.OutlineTextureColorIntensity, Is.Zero);
            component.OutlineTextureColorIntensity = -1;
            Assert.That(component.OutlineTextureColorIntensity, Is.Zero);
            component.OutlineTextureColorIntensity = 1.5f;
            component.ShadowOffset = new Vector2(4, -3);
            component.ShadowBlur = 4;
            component.ShadowSpread = 2;
            component.ShadowColor = shadowColor;
            Assert.That(component.OutlineEnabled, Is.True);
            Assert.That(component.ShadowEnabled, Is.True);

            component.OutlineEnabled = false;
            Material shadowOnly = component.materialForRendering;
            Assert.That(shadowOnly.GetVector("_Outline").x <= 0 || shadowOnly.GetColor("_OutlineColor").a <= 0, Is.True);
            Assert.That(shadowOnly.GetColor("_ShadowColor").a, Is.EqualTo(shadowColor.a));

            component.OutlineEnabled = true;
            component.ShadowEnabled = false;
            Material outlineOnly = component.materialForRendering;
            Assert.That(outlineOnly.GetVector("_Outline").x, Is.EqualTo(6));
            Assert.That(outlineOnly.GetColor("_ShadowColor").a, Is.Zero);

            component.OutlineEnabled = false;
            Assert.That(component.mainTexture, Is.EqualTo(sourceTexture), "With both effects off, the source renders as a normal Image.");
            Assert.That(component.materialForRendering.shader, Is.EqualTo(Graphic.defaultGraphicMaterial.shader));
            Assert.That(component.OutlineWidth, Is.EqualTo(6));
            Assert.That(component.OutlineSoftness, Is.EqualTo(1));
            Assert.That(component.OutlineColor, Is.EqualTo(outlineColor));
            Assert.That(component.OutlineUseTextureColor, Is.True);
            Assert.That(component.OutlineTextureColorIntensity, Is.EqualTo(1.5f));
            Assert.That(component.ShadowOffset, Is.EqualTo(new Vector2(4, -3)));
            Assert.That(component.ShadowBlur, Is.EqualTo(4));
            Assert.That(component.ShadowSpread, Is.EqualTo(2));
            Assert.That(component.ShadowColor, Is.EqualTo(shadowColor));

            component.OutlineEnabled = true;
            component.ShadowEnabled = true;
            Material restored = component.materialForRendering;
            Assert.That(restored.GetVector("_Outline"), Is.EqualTo(new Vector4(6, 1, 0, 0)));
            Assert.That(restored.GetVector("_OutlineTextureColor"), Is.EqualTo(new Vector4(1, 1.5f, 0, 0)));
            // Allow material color round-trip rounding; the stored component style above remains exact.
            Color restoredColor = restored.GetColor("_ShadowColor");
            Assert.That(restoredColor.r, Is.EqualTo(shadowColor.r).Within(0.000001f));
            Assert.That(restoredColor.g, Is.EqualTo(shadowColor.g).Within(0.000001f));
            Assert.That(restoredColor.b, Is.EqualTo(shadowColor.b).Within(0.000001f));
            Assert.That(restoredColor.a, Is.EqualTo(shadowColor.a).Within(0.000001f));
            Assert.That(restored.GetVector("_Shadow"), Is.EqualTo(new Vector4(4, -3, 4, 2)));
        }

        [Test]
        public void ImagesKeepIndependentMaterials_AndReleaseOnlyTheirOwnOnDisable()
        {
            SdfImage first = CreateImage("First");
            SdfImage second = CreateImage("Second");
            first.OutlineWidth = 4;
            second.OutlineWidth = 11;

            Material firstMaterial = first.materialForRendering;
            Material secondMaterial = second.materialForRendering;
            Assert.That(firstMaterial, Is.Not.Null);
            Assert.That(secondMaterial, Is.Not.SameAs(firstMaterial));
            Assert.That(firstMaterial.GetVector("_Outline").x, Is.EqualTo(4));
            Assert.That(secondMaterial.GetVector("_Outline").x, Is.EqualTo(11));

            first.gameObject.SetActive(false);
            Assert.That(!firstMaterial, Is.True, "Disabled image should release its material in Edit Mode.");
            Assert.That((bool)secondMaterial, Is.True);
            Assert.That(second.materialForRendering.GetVector("_Outline").x, Is.EqualTo(11));
        }

        [Test]
        public void CompatibleImages_ShareMaterialDespitePositionAndGraphicTint_AndReleaseLastOwner()
        {
            var first = CreateImage("First shared");
            var second = CreateImage("Second shared");
            second.rectTransform.anchoredPosition = new Vector2(80, 20);
            second.color = new Color(0.2f, 0.6f, 0.9f, 0.5f);
            var shared = first.materialForRendering;
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            first.gameObject.SetActive(false);
            Assert.That((bool)shared, Is.True, "Another visible owner must retain the material.");
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            first.gameObject.SetActive(true);
            Assert.That(first.materialForRendering, Is.SameAs(shared));
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            Assert.That(!shared, Is.True, "The final owner must release the material.");
        }

        [Test]
        public void SharedImageStyle_DetachesAndRejoinsWithoutChangingOtherOwners()
        {
            var first = CreateImage("Changing shared");
            var second = CreateImage("Unchanged shared");
            var shared = first.materialForRendering;
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            first.OutlineWidth = 7;
            var changed = first.materialForRendering;
            Assert.That(changed, Is.Not.SameAs(shared));
            Assert.That(shared.GetVector("_Outline").x, Is.EqualTo(2));
            Assert.That(changed.GetVector("_Outline").x, Is.EqualTo(7));
            first.OutlineWidth = 2;
            Assert.That(first.materialForRendering, Is.SameAs(shared));
            first.Layers[0].Color = Color.green;
            first.RefreshEffects();
            Assert.That(first.materialForRendering, Is.Not.SameAs(shared));
            Assert.That(second.materialForRendering.GetColor("_OutlineColor"), Is.EqualTo(Color.black));
            first.gameObject.SetActive(false);
            second.gameObject.SetActive(false);
            Assert.That(!shared && !changed, Is.True, "All live and spare materials must be released with their owners.");
        }

        [Test]
        public void MaterialSharing_SeparatesDrawingRectsSlicingAndTextures()
        {
            var first = CreateImage("Common source");
            var second = CreateImage("Different mapping");
            var shared = first.materialForRendering;
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            var size = second.rectTransform.sizeDelta;
            second.rectTransform.sizeDelta = size + new Vector2(40, 10);
            Assert.That(second.materialForRendering, Is.Not.SameAs(shared));
            second.rectTransform.sizeDelta = size;
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            second.type = Image.Type.Sliced;
            Assert.That(second.materialForRendering, Is.Not.SameAs(shared));
            second.type = Image.Type.Simple;
            Assert.That(second.materialForRendering, Is.SameAs(shared));
            second.Sprite = CreateTemporarySdf();
            Assert.That(second.materialForRendering, Is.Not.SameAs(shared));
            Assert.That(first.materialForRendering.GetTexture("_SdfTex"), Is.SameAs(distanceTexture));
        }

        [Test]
        public void SharedMaskedImages_KeepStencilAndSiblingStyleWhenOneChanges()
        {
            var maskObject = new GameObject("Shared mask", typeof(RectTransform), typeof(Image), typeof(Mask));
            maskObject.transform.SetParent(canvasObject.transform, false);
            var first = CreateImage("Masked first", maskObject.transform);
            var second = CreateImage("Masked second", maskObject.transform);
            var masked = first.materialForRendering;
            Assert.That(second.materialForRendering, Is.SameAs(masked));
            float stencil = masked.GetFloat("_Stencil");
            Assert.That(stencil, Is.GreaterThan(0));
            first.OutlineWidth = 8;
            first.OutlineColor = Color.magenta;
            var changed = first.materialForRendering;
            Assert.That(changed, Is.Not.SameAs(masked));
            Assert.That(changed.GetFloat("_Stencil"), Is.EqualTo(stencil));
            Assert.That(changed.GetColor("_OutlineColor"), Is.EqualTo(Color.magenta));
            Assert.That(second.materialForRendering.GetVector("_Outline").x, Is.EqualTo(2));
            first.gameObject.SetActive(false);
            Assert.That((bool)masked, Is.True);
        }

        [TestCase(1, false)]
        [TestCase(5, false)]
        [TestCase(1, true)]
        public void SharedGroupAnimation_ReusesMaterialsWithoutManagedAllocationsAfterWarmup(int groups, bool masked)
        {
            Transform parent = canvasObject.transform;
            if (masked)
            {
                var mask = new GameObject("Animated group mask", typeof(RectTransform), typeof(Image), typeof(Mask));
                mask.transform.SetParent(parent, false);
                parent = mask.transform;
            }
            var first = new SdfImage[groups];
            var second = new SdfImage[groups];
            var rendered = new Material[groups];
            for (int i = 0; i < groups; i++)
            {
                first[i] = CreateImage("Animated first", parent);
                second[i] = CreateImage("Animated second", parent);
                first[i].OutlineColor = second[i].OutlineColor = Color.HSVToRGB(i / (float)groups, 1, 1);
            }
            void Apply(float width)
            {
                for (int i = 0; i < groups; i++)
                {
                    first[i].OutlineWidth = width;
                    rendered[i] = first[i].materialForRendering;
                }
                for (int i = 0; i < groups; i++)
                {
                    second[i].OutlineWidth = width;
                    if (rendered[i] != second[i].materialForRendering)
                        throw new System.InvalidOperationException("Animated group failed to share.");
                }
            }
            for (int i = 0; i < 32; i++) Apply(2 + i % 3);
            var materials = new HashSet<Material>();
            for (int i = 0; i < 12; i++)
            {
                Apply(2 + i % 3);
                foreach (var material in rendered) materials.Add(material);
            }
            Assert.That(materials.Count, Is.LessThanOrEqualTo(2 * groups), "Animated groups should reuse their render states.");
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Apply(2 + i % 3);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Warmed-up material sharing should not allocate each style update.");
        }
        [Test]
        public void Layers_MigrateOldFieldsAndKeepCompatibilityRolesAfterReorderUndoAndSerialization()
        {
            SdfImage image = CreateImage("Layer migration");
            var serialized = new SerializedObject(image);
            serialized.FindProperty("sdfLayersMigrated").boolValue = false;
            serialized.FindProperty("sdfLayers").ClearArray();
            serialized.FindProperty("outlineWidth").floatValue = 7;
            serialized.FindProperty("outlineColor").colorValue = Color.magenta;
            serialized.FindProperty("outlineUseTextureColor").boolValue = true;
            serialized.FindProperty("outlineTextureColorIntensity").floatValue = 0.5f;
            serialized.FindProperty("shadowOffset").vector2Value = new Vector2(11, -5);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            image.RefreshEffects();
            Assert.That(image.Layers.Count, Is.EqualTo(2));
            Assert.That(image.Layers[0].Width, Is.EqualTo(7));
            Assert.That(image.Layers[0].UseTextureColor, Is.True);
            Assert.That(image.Layers[0].TextureColorIntensity, Is.EqualTo(0.5f));
            Assert.That(image.Layers[1].Position, Is.EqualTo(SdfOutlinePosition.Underlay));
            Assert.That(image.Layers[1].Offset, Is.EqualTo(new Vector2(11, -5)));

            Undo.IncrementCurrentGroup();
            serialized.Update();
            serialized.FindProperty("sdfLayers").MoveArrayElement(0, 1);
            serialized.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Assert.That(image.Layers[0].Position, Is.EqualTo(SdfOutlinePosition.Underlay));
            Undo.PerformUndo();
            Assert.That(image.Layers[0].Position, Is.EqualTo(SdfOutlinePosition.Outer));
            Undo.PerformRedo();
            image.OutlineWidth = 9;
            Assert.That(image.Layers[1].Width, Is.EqualTo(9), "The scalar API must follow the migrated outline after reordering.");
            Assert.That(image.Layers[0].Width, Is.Zero);

            // A legacy prefab override/animation channel must update only that channel.
            serialized.Update();
            serialized.FindProperty("outlineSoftness").floatValue = 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            image.RefreshEffects();
            Assert.That(image.Layers[1].Width, Is.EqualTo(9));
            Assert.That(image.Layers[1].Softness, Is.EqualTo(3));
            var copy = CreateImage("Layer copy");
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(image), copy);
            copy.RefreshEffects();
            Assert.That(copy.Layers[1].Width, Is.EqualTo(9));
            Assert.That(copy.Layers[1].Color, Is.EqualTo(Color.magenta));
            image.Layers.Clear();
            image.RefreshEffects();
            image.enabled = false;
            image.enabled = true;
            Assert.That(image.Layers, Is.Empty, "An intentionally empty list must remain empty.");
            Undo.ClearUndo(image);
        }

        [Test]
        public void Layers_ExpandOneQuadForOffsetsAndClearUniformsWhenDisabled()
        {
            SdfImage image = CreateImage("Layer bounds");
            image.rectTransform.sizeDelta = new Vector2(64, 64);
            image.Layers.Clear();
            image.Layers.Add(new SdfImageEffect { Width = 5, Offset = new Vector2(60, -40) });
            image.Layers.Add(new SdfImageEffect { Width = 8, Offset = new Vector2(-30, 20), Color = Color.red });
            image.RefreshEffects();
            var mesh = BuildMesh(image);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(4), "Additional layers must not add overlapping geometry or child Graphics.");
                Assert.That(mesh.bounds.max.x, Is.GreaterThanOrEqualTo(97));
                Assert.That(mesh.bounds.min.y, Is.LessThanOrEqualTo(-77));
                Assert.That(image.materialForRendering.GetInt("_LayerCount"), Is.EqualTo(2));
                Assert.That(image.transform.childCount, Is.Zero);
                image.EffectsEnabled = false;
                Assert.That(image.materialForRendering.GetInt("_LayerCount"), Is.Zero);
                image.EffectsEnabled = true;
                Assert.That(image.materialForRendering.GetInt("_LayerCount"), Is.EqualTo(2));
                image.Layers.RemoveAt(0);
                image.RefreshEffects();
                Assert.That(image.materialForRendering.GetInt("_LayerCount"), Is.EqualTo(1));
                Assert.That(image.materialForRendering.GetVectorArray("_LayerSizes")[0].x, Is.EqualTo(-30));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void StencilMaterial_ReceivesStyleChangesAfterItsFirstCreation()
        {
            var maskObject = new GameObject("Mask", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            maskObject.transform.SetParent(canvasObject.transform, false);
            SdfImage child = CreateImage("Masked Child", maskObject.transform);
            child.OutlineWidth = 4;
            Material initial = child.materialForRendering;
            Assert.That(initial, Is.Not.SameAs(child.material));
            Assert.That(initial.GetFloat("_Stencil"), Is.GreaterThan(0));

            child.OutlineWidth = 11;
            child.OutlineColor = Color.magenta;
            child.OutlineUseTextureColor = true;
            child.OutlineTextureColorIntensity = 0.5f;
            Material changed = child.materialForRendering;

            Assert.That(changed.GetVector("_Outline").x, Is.EqualTo(11));
            Assert.That(changed.GetColor("_OutlineColor"), Is.EqualTo(Color.magenta));
            Assert.That(changed.GetVector("_OutlineTextureColor"), Is.EqualTo(new Vector4(1, 0.5f, 0, 0)));
        }

        [Test]
        public void OutlineExpandsRenderedMesh_WithoutExpandingClickableBounds()
        {
            SdfImage image = CreateImage("Outlined");
            image.OutlineWidth = 12;
            image.ShadowColor = Color.clear;
            image.rectTransform.sizeDelta = new Vector2(64, 64);
            var mesh = new Mesh();
            try
            {
                using (var vertices = new VertexHelper())
                {
                    typeof(SdfImage).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                            null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(image, new object[] { vertices });
                    vertices.FillMesh(mesh);
                }
                Rect content = image.rectTransform.rect;
                Assert.That(mesh.bounds.min.x, Is.LessThanOrEqualTo(content.xMin - 12));
                Assert.That(mesh.bounds.max.x, Is.GreaterThanOrEqualTo(content.xMax + 12));
                Assert.That(mesh.bounds.min.y, Is.LessThanOrEqualTo(content.yMin - 12));
                Assert.That(mesh.bounds.max.y, Is.GreaterThanOrEqualTo(content.yMax + 12));
                Vector2 center = RectTransformUtility.WorldToScreenPoint(null,
                    image.rectTransform.TransformPoint(content.center));
                Vector2 outline = RectTransformUtility.WorldToScreenPoint(null,
                    image.rectTransform.TransformPoint(new Vector2(content.xMax + 6, content.center.y)));
                Assert.That(image.Raycast(center, null), Is.True);
                Assert.That(image.Raycast(outline, null), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SlicedLayout_UsesSpriteBordersAndCanvasPixelsPerUnit()
        {
            SdfImage image = CreateImage("Sliced");
            canvasObject.GetComponent<Canvas>().referencePixelsPerUnit = 200;
            image.Type = SdfImageType.Sliced;

            Assert.That(image.minWidth, Is.EqualTo(24));
            Assert.That(image.minHeight, Is.EqualTo(32));
            Assert.That(image.preferredWidth, Is.EqualTo(64));
            Assert.That(image.preferredHeight, Is.EqualTo(64));
            image.SetNativeSize();
            Assert.That(image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(64, 64)));

            // Borders that cannot fit must shrink together, preserving each pair's proportions.
            image.rectTransform.sizeDelta = new Vector2(12, 16);
            Vector4 border = image.materialForRendering.GetVector("_LocalBorder");
            Assert.That(border, Is.EqualTo(new Vector4(4, 6, 8, 10)));
            image.Type = SdfImageType.Simple;
            Assert.That(image.minWidth, Is.Zero);
            Assert.That(image.minHeight, Is.Zero);
        }

        [Test]
        public void CompressedSlicedBorders_KeepOutlineWhenTheCenterCollapses()
        {
            SdfImage image = CreateImage("Compressed Sliced");
            image.Type = SdfImageType.Sliced;
            image.OutlineWidth = 3;
            image.ShadowBlur = 4;
            image.rectTransform.sizeDelta = new Vector2(6, 8);

            Material rendered = image.materialForRendering;
            Assert.That(rendered.GetVector("_LocalBorder"), Is.EqualTo(new Vector4(2, 3, 4, 5)));
            Assert.That(rendered.GetVector("_Outline").x, Is.EqualTo(3),
                "Removing the center must not force the visible borders' effect budget to zero.");
            Assert.That(rendered.GetVector("_Shadow").z, Is.EqualTo(4));
        }

        [TestCase(100f)]
        [TestCase(200f)]
        public void ThinDownsampledSprite_PreservesOriginalNativeSizeAspectAndSliceBorders(float referencePixelsPerUnit)
        {
            var originalTexture = new Texture2D(1024, 3, TextureFormat.RGBA32, false);
            var original = Sprite.Create(originalTexture, new Rect(0, 0, 1024, 3), new Vector2(0.5f, 0.5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(16, 1, 16, 1));
            try
            {
                Object.DestroyImmediate(colorTexture);
                Object.DestroyImmediate(distanceTexture);
                colorTexture = new Texture2D(80, 17);
                distanceTexture = new Texture2D(80, 17);
                // A 1/16 bake rounds the original three rows to one; that must not turn native height into 16.
                sprite.Initialize(original, colorTexture, distanceTexture, new Vector2Int(64, 1),
                    new Vector4(1, 1f / 3, 1, 1f / 3), new Vector2(0.5f, 0.5f), 6.25f, 8, 8);
                canvasObject.GetComponent<Canvas>().referencePixelsPerUnit = referencePixelsPerUnit;
                SdfImage image = CreateImage("Thin Downsampled");
                float canvasScale = referencePixelsPerUnit / 100;

                Assert.That(sprite.NativeSize.x, Is.EqualTo(10.24f).Within(0.0001f));
                Assert.That(sprite.NativeSize.y, Is.EqualTo(0.03f).Within(0.0001f));
                Assert.That(sprite.SourceSize, Is.EqualTo(new Vector2Int(64, 1)), "Shader metadata stays in baked pixels.");
                Assert.That(image.preferredWidth, Is.EqualTo(1024 * canvasScale).Within(0.001f));
                Assert.That(image.preferredHeight, Is.EqualTo(3 * canvasScale).Within(0.001f));
                image.SetNativeSize();
                Assert.That(image.rectTransform.sizeDelta.x, Is.EqualTo(1024 * canvasScale).Within(0.001f));
                Assert.That(image.rectTransform.sizeDelta.y, Is.EqualTo(3 * canvasScale).Within(0.001f));

                image.PreserveAspect = true;
                image.rectTransform.sizeDelta = Vector2.one * (1024 * canvasScale);
                Vector4 drawing = image.materialForRendering.GetVector("_ImageRect");
                Assert.That(drawing.z, Is.EqualTo(1024 * canvasScale).Within(0.001f));
                Assert.That(drawing.w, Is.EqualTo(3 * canvasScale).Within(0.001f));

                image.Type = SdfImageType.Sliced;
                image.rectTransform.sizeDelta = new Vector2(1024, 30) * canvasScale;
                Vector4 localBorder = image.materialForRendering.GetVector("_LocalBorder");
                Assert.That(localBorder.x, Is.EqualTo(16 * canvasScale).Within(0.001f));
                Assert.That(localBorder.z, Is.EqualTo(16 * canvasScale).Within(0.001f));
                Assert.That(localBorder.y, Is.EqualTo(canvasScale).Within(0.001f));
                Assert.That(localBorder.w, Is.EqualTo(canvasScale).Within(0.001f));
                Assert.That(image.minWidth, Is.EqualTo(32 * canvasScale).Within(0.001f));
                Assert.That(image.minHeight, Is.EqualTo(2 * canvasScale).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(originalTexture);
            }
        }

        [Test]
        public void LegacyImageSettings_MigrateWhenSourceIsSeededBeforeEnable()
        {
            SdfImage image = CreateImage("Disabled Legacy Image");
            image.enabled = false;
            Sprite original = CreateSourceSprite(CreateTemporaryTexture(32, 32), new Rect(0, 0, 32, 32));
            sprite.Initialize(original, colorTexture, distanceTexture, new Vector2Int(32, 32),
                new Vector4(4, 6, 8, 10), new Vector2(0.5f, 0.5f), 100, 16, 16);
            var serialized = new UnityEditor.SerializedObject(image);
            serialized.FindProperty("legacyImageType").enumValueIndex = (int)SdfImageType.Sliced;
            serialized.FindProperty("legacyPreserveAspect").boolValue = true;
            serialized.FindProperty("migratedImageSettings").boolValue = false;
            serialized.FindProperty("legacyBakedBinding").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // An old helper can assign the standard source while the Image is still disabled.
            // Its inherited setter marks geometry dirty before OnEnable performs migration.
            ((Image)image).sprite = original;
            image.enabled = true;

            Assert.That(image.SourceSprite, Is.EqualTo(original));
            Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
            Assert.That(image.preserveAspect, Is.True);
            Assert.That(image.SdfData, Is.EqualTo(sprite), "An unattached legacy bake must survive source seeding.");

            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.RefreshSdf();
            Assert.That(image.type, Is.EqualTo(Image.Type.Simple), "Migration runs once, so later Image edits remain authoritative.");
            Assert.That(image.preserveAspect, Is.False);
        }

        private SdfImage CreateImage(string name, Transform parent = null, bool assignBaked = true)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SdfImage));
            imageObject.transform.SetParent(parent ? parent : canvasObject.transform, false);
            var image = imageObject.GetComponent<SdfImage>();
            if (assignBaked)
                image.Sprite = sprite;
            image.rectTransform.sizeDelta = new Vector2(64, 64);
            return image;
        }

        private Texture2D CreateTemporaryTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            temporaryAssets.Add(texture);
            return texture;
        }

        private Sprite CreateSourceSprite(Texture2D texture, Rect rectangle, SdfSprite attached = null)
        {
            Sprite source = Sprite.Create(texture, rectangle, new Vector2(0.5f, 0.5f), 100);
            temporaryAssets.Add(source);
            if (attached)
                Assert.That(source.AddScriptableObject(attached), Is.True);
            return source;
        }

        private SdfSprite CreateTemporarySdf()
        {
            Texture2D color = CreateTemporaryTexture(64, 64);
            Texture2D distance = CreateTemporaryTexture(64, 64);
            var data = ScriptableObject.CreateInstance<SdfSprite>();
            temporaryAssets.Add(data);
            data.Initialize(null, color, distance, new Vector2Int(32, 32), Vector4.zero,
                new Vector2(0.5f, 0.5f), 100, 16, 16);
            return data;
        }

        private static void AssertResolvedData(SdfImage component, SdfSprite expected)
        {
            Material rendered = component.materialForRendering;
            Assert.That(component.SdfData, Is.EqualTo(expected));
            Assert.That(component.mainTexture, Is.EqualTo(expected.ColorTexture));
            Assert.That(rendered.GetTexture("_SdfTex"), Is.EqualTo(expected.DistanceTexture));
        }

        private static void AssertStandardLayout(SdfImage image, Image standard)
        {
            Assert.That(image.minWidth, Is.EqualTo(standard.minWidth));
            Assert.That(image.minHeight, Is.EqualTo(standard.minHeight));
            Assert.That(image.preferredWidth, Is.EqualTo(standard.preferredWidth));
            Assert.That(image.preferredHeight, Is.EqualTo(standard.preferredHeight));
        }

        private static Mesh BuildMesh(Graphic graphic)
        {
            var mesh = new Mesh();
            using (var vertices = new VertexHelper())
            {
                graphic.GetType().GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null)
                    .Invoke(graphic, new object[] { vertices });
                vertices.FillMesh(mesh);
            }
            return mesh;
        }
    }
}
