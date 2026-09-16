using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SDFUI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    public sealed class SdfInspectorTests
    {
        private string folder;
        private string sourcePath;
        private Scene previewScene;
        private SdfImage component;
        private UnityEditor.Editor inspector;
        private int completedCount;

        [Test]
        public void SourceInspector_KeepsUnityTextureImporterAndInstallsSdfSectionOnce()
        {
            var sourceImporter = AssetImporter.GetAtPath(sourcePath);
            inspector = UnityEditor.Editor.CreateEditor(sourceImporter);
            Assert.That(inspector.GetType().FullName, Is.EqualTo("UnityEditor.TextureImporterInspector"));
            var header = typeof(SdfSpriteEditor).Assembly.GetType("SDFUI.Editor.SdfTextureHeader", true);
            var install = header.GetMethod("TryInstallSourceSection", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(install.Invoke(null, new object[] { inspector }), Is.True,
                "The supported Editor must insert SDF into the native body rather than the preview header.");
            var sections = (IDictionary)inspector.GetType().GetField("m_GUIElementMethods", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(inspector);
            object spriteKey = null;
            foreach (DictionaryEntry entry in sections)
                if (entry.Key.ToString() == "Sprite") { spriteKey = entry.Key; break; }
            var first = (Delegate)sections[spriteKey];
            Assert.That(first.Target.GetType().DeclaringType, Is.EqualTo(header));
            Assert.That(install.Invoke(null, new object[] { inspector }), Is.True);
            Assert.That(sections[spriteKey], Is.SameAs(first), "Repainting must not wrap the Sprite section again.");
        }

        [Test]
        public void PlatformTabs_UseUnityNativeTextureImporterUI()
        {
            // A compile-only check cannot detect a renamed internal Unity UI entry point.
            var type = typeof(SdfSpriteEditor).Assembly.GetType("SDFUI.Editor.SdfPlatformSettingsGUI", true);
            object platformUI = Activator.CreateInstance(type);
            var native = type.GetProperty("UsesNativeTabs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(native.GetValue(platformUI), Is.True,
                "Supported Unity versions should use the original texture-importer tabs, not the compatibility fallback.");
        }

        [Test]
        public void CompressionInspector_UsesDetachedNativeModelsWithoutEditingTheSource()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            string before = EditorJsonUtility.ToJson(importer);
            var type = typeof(SdfSpriteEditor).Assembly.GetType("SDFUI.Editor.SdfPlatformSettingsGUI", true);
            var ui = (IDisposable)Activator.CreateInstance(type, new object[] { importer });
            try
            {
                Assert.That(type.GetProperty("UsesNativeSettings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui), Is.True);
                var models = (IList)type.GetField("platforms", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ui);
                Assert.That(models.Count, Is.GreaterThan(1));
                foreach (var platform in models)
                {
                    var model = platform.GetType().GetProperty("model").GetValue(platform);
                    var nativeSettings = (TextureImporterPlatformSettings)model.GetType().GetProperty("platformTextureSettings").GetValue(model);
                    nativeSettings.overridden = true;
                    nativeSettings.maxTextureSize = 128;
                    nativeSettings.format = TextureImporterFormat.DXT5Crunched;
                    nativeSettings.compressionQuality = 73;
                    Assert.That(model.GetType().GetProperty("platformTextureSettingsProp").GetValue(model), Is.Null,
                        "Native compression controls must be detached from the source SerializedObject.");
                }
            }
            finally { ui.Dispose(); }
            Assert.That(EditorJsonUtility.ToJson(importer), Is.EqualTo(before));
        }

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/SDFImageInspectorTest_" + Guid.NewGuid().ToString("N");
            sourcePath = folder + "/Source.png";
            completedCount = 0;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            CreateSource();
            previewScene = EditorSceneManager.NewPreviewScene();
            var imageObject = new GameObject("Inspector Test Image", typeof(RectTransform), typeof(SdfImage))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(imageObject, previewScene);
            component = imageObject.GetComponent<SdfImage>();
            SdfBakeQueue.Completed += OnCompleted;
        }

        [TearDown]
        public void TearDown()
        {
            SdfBakeQueue.Completed -= OnCompleted;
            if (!string.IsNullOrEmpty(sourcePath))
                SdfBakeQueue.Cancel(sourcePath);
            if (inspector)
                Object.DestroyImmediate(inspector);
            if (component)
                Undo.ClearUndo(component);
            if (!string.IsNullOrEmpty(sourcePath))
            {
                var importer = AssetImporter.GetAtPath(sourcePath);
                if (importer)
                    Undo.ClearUndo(importer);
            }
            if (previewScene.IsValid())
                EditorSceneManager.ClosePreviewScene(previewScene);
            if (!string.IsNullOrEmpty(folder))
                AssetDatabase.DeleteAsset(folder);
        }

        [UnityTest]
        public IEnumerator CustomInspector_SourceAssignmentUndoAndGenerate_UseTheSameImageComponent()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("Generation requires an Editor graphics device with asynchronous GPU readback.");

            inspector = UnityEditor.Editor.CreateEditor(component);
            Assert.That(inspector.GetType().FullName, Is.EqualTo("SDFUI.Editor.SdfImageEditor"));
            Assert.That(inspector.GetType().BaseType.FullName, Is.EqualTo("UnityEditor.UI.ImageEditor"));
            SerializedObject serialized = inspector.serializedObject;
            SerializedProperty sourceProperty = serialized.FindProperty("m_Sprite");
            Assert.That(sourceProperty, Is.Not.Null);
            Assert.That(sourceProperty.propertyType, Is.EqualTo(SerializedPropertyType.ObjectReference));
            Assert.That(serialized.FindProperty("outlineEnabled").propertyType, Is.EqualTo(SerializedPropertyType.Boolean));
            Assert.That(serialized.FindProperty("shadowEnabled").propertyType, Is.EqualTo(SerializedPropertyType.Boolean));

            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Assign Inspector Test Source");
            int sourceUndoGroup = Undo.GetCurrentGroup();
            serialized.Update();
            sourceProperty.objectReferenceValue = source;
            Assert.That(serialized.ApplyModifiedProperties(), Is.True);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(sourceUndoGroup);
            Assert.That(component.sprite, Is.EqualTo(source));

            Undo.PerformUndo();
            Assert.That(component.sprite, Is.Null, "The standard serialized Image source must participate in Inspector Undo.");
            Undo.PerformRedo();
            Assert.That(component.sprite, Is.EqualTo(source));
            serialized.Update();
            yield return Settle();
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            Assert.That(completedCount, Is.Zero);
            Assert.That(component.SdfData, Is.Null, "Assigning a source alone must not start generation.");

            MethodInfo generate = inspector.GetType().GetMethod("ChangeGeneration", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null);
            Assert.That(generate, Is.Not.Null);
            generate.Invoke(inspector, new object[] { true });
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.True);
            double deadline = EditorApplication.timeSinceStartup + 30;
            while ((!component.SdfData || !component.SdfData.IsValid || completedCount == 0)
                && EditorApplication.timeSinceStartup < deadline)
                yield return null;

            Assert.That(component.SdfData, Is.Not.Null, "Inspector generation did not bind its result: " + SdfBakeQueue.GetStatus(sourcePath));
            Assert.That(component.SdfData.IsValid, Is.True);
            source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            Assert.That(component.SourceSprite, Is.EqualTo(source));
            Assert.That(component.SdfData, Is.EqualTo(SdfSprite.FromSprite(source)));
            Assert.That(AssetDatabase.GetAssetPath(component.SdfData), Is.EqualTo(sourcePath));
            Assert.That(component.GetComponent<SdfAutoBake>(), Is.Null);
            Assert.That(component.GetComponents<MonoBehaviour>().Length, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(folder, "*.asset"), Is.Empty);
            Object.DestroyImmediate(inspector);
            inspector = UnityEditor.Editor.CreateEditor(component.SdfData);
            Assert.That(inspector, Is.TypeOf<SdfSpriteEditor>());
            Assert.That(inspector.HasPreviewGUI(), Is.True);
            Texture2D preview = inspector.RenderStaticPreview(sourcePath, AssetDatabase.LoadAllAssetsAtPath(sourcePath), 96, 96);
            try
            {
                Assert.That(preview, Is.Not.Null, "The selectable SDF subasset must have a Project thumbnail.");
                Assert.That(preview.width, Is.EqualTo(96));
                Assert.That(preview.GetPixel(48, 48).a, Is.GreaterThan(0.9f));
            }
            finally { if (preview) Object.DestroyImmediate(preview); }
        }

        [UnityTest]
        public IEnumerator LegacyBinding_AfterSourceIsCleared_DoesNotReseedDuringRefreshEnableOrCleanupUndo()
        {
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            var binding = component.gameObject.AddComponent<SdfAutoBake>();
            var legacyFields = new SerializedObject(binding);
            legacyFields.FindProperty("source").objectReferenceValue = source;
            legacyFields.FindProperty("sourceSeeded").boolValue = false;
            legacyFields.ApplyModifiedPropertiesWithoutUndo();
            SdfSourceImporter.RefreshTarget(binding);
            yield return Settle();
            Assert.That(component.sprite, Is.EqualTo(source), "An old serialized binding must seed an initially empty Image once.");

            var imageFields = new SerializedObject(component);
            imageFields.FindProperty("m_Sprite").objectReferenceValue = null;
            imageFields.ApplyModifiedPropertiesWithoutUndo();
            SdfSourceImporter.RefreshTarget(binding);
            yield return Settle();
            Assert.That(component.SourceSprite, Is.Null, "Deferred legacy refresh must respect a source the user cleared.");
            Assert.That(binding.Source, Is.EqualTo(source), "The obsolete source still exists, so the migration guard is being exercised.");

            binding.enabled = false;
            binding.enabled = true;
            yield return Settle();
            Assert.That(component.SourceSprite, Is.Null, "Re-enabling the compatibility component must not restore its old source.");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Remove Inspector Test Legacy Binding");
            int cleanupUndoGroup = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(binding);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(cleanupUndoGroup);
            Assert.That(component.GetComponent<SdfAutoBake>(), Is.Null);
            Undo.PerformUndo();
            binding = component.GetComponent<SdfAutoBake>();
            Assert.That(binding, Is.Not.Null);
            SdfSourceImporter.RefreshTarget(binding);
            yield return Settle();
            Assert.That(component.SourceSprite, Is.Null, "Undoing legacy cleanup must preserve the cleared authoritative Image source.");
            Undo.PerformRedo();
            Assert.That(component.GetComponent<SdfAutoBake>(), Is.Null);
            Assert.That(component.SourceSprite, Is.Null);
            Assert.That(completedCount, Is.Zero);
        }

        private static IEnumerator Settle()
        {
            double until = EditorApplication.timeSinceStartup + 0.2;
            while (EditorApplication.timeSinceStartup < until)
                yield return null;
        }

        private void CreateSource()
        {
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[32 * 32];
                for (int y = 8; y < 24; y++)
                for (int x = 8; x < 24; x++)
                    pixels[y * 32 + x] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(sourcePath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private void OnCompleted(string path)
        {
            if (path == sourcePath)
                completedCount++;
        }
    }
}
