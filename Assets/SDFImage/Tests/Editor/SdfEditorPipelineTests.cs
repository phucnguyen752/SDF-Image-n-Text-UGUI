using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SDFUI.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    public sealed class SdfEditorPipelineTests
    {
        private const string Marker = "\n[SDF_IMAGE_V1]\n";
        private const string EndMarker = "\n[/SDF_IMAGE_V1]\n";
        private string folder, sourcePath, cacheDirectory;
        private EditorBuildSettingsScene[] originalScenes;
        private Object[] originalPreloaded;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/SDFImagePipelineTest_" + Guid.NewGuid().ToString("N");
            sourcePath = folder + "/Source.png";
            originalScenes = EditorBuildSettings.scenes;
            originalPreloaded = PlayerSettings.GetPreloadedAssets();
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[256];
                for (int y = 4; y < 12; y++)
                for (int x = 4; x < 12; x++) pixels[y * 16 + x] = new Color32(255, 220, 80, 255);
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(sourcePath, texture.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            cacheDirectory = Path.Combine(SdfBakeCache.CacheDirectory, AssetDatabase.AssetPathToGUID(sourcePath));
        }

        [TearDown]
        public void TearDown()
        {
            SdfBakeQueue.Cancel(sourcePath);
            EditorBuildSettings.scenes = originalScenes;
            PlayerSettings.SetPreloadedAssets(originalPreloaded);
            var importer = AssetImporter.GetAtPath(sourcePath);
            if (importer) Undo.ClearUndo(importer);
            AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void LegacySettings_DefaultToAutomaticCompression_AndRoundTripOptOut()
        {
            SetUserData(Marker + "{\"enabled\":false,\"maxSize\":512,\"padding\":32,\"range\":32,\"alphaThreshold\":0.5}" + EndMarker);
            var settings = SdfTextureSettings.Get(sourcePath);
            Assert.That(settings.colorCompression, Is.EqualTo(SdfColorCompression.Automatic));
            Assert.That(settings.compressDistance, Is.True, "Older metadata should receive the compressed-distance default.");
            settings.colorCompression = SdfColorCompression.Uncompressed;
            settings.compressDistance = false;
            SdfTextureSettings.Set(sourcePath, settings);
            Assert.That(SdfTextureSettings.Get(sourcePath).colorCompression, Is.EqualTo(SdfColorCompression.Uncompressed));
            Assert.That(SdfTextureSettings.Get(sourcePath).compressDistance, Is.False);
        }

        [Test]
        public void InactivePlatformOverrides_DoNotInvalidateTheCurrentBake()
        {
            var settings = SdfTextureSettings.Get(sourcePath);
            SdfTextureSettings.Set(sourcePath, settings);
            string before = SdfTextureSettings.Fingerprint(sourcePath);
            SdfPlatformSettings inactive = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? settings.ios : settings.android;
            inactive.overridden = true;
            inactive.maxSize = 64;
            inactive.colorCompression = SdfColorCompression.Uncompressed;
            inactive.compressionQuality = SdfCompressionQuality.Fast;
            SdfTextureSettings.Set(sourcePath, settings);
            Assert.That(SdfTextureSettings.Fingerprint(sourcePath), Is.EqualTo(before));
            var stored = SdfTextureSettings.Get(sourcePath);
            SdfPlatformSettings saved = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? stored.ios : stored.android;
            Assert.That(saved.overridden, Is.True);
            Assert.That(saved.maxSize, Is.EqualTo(64));
            Assert.That(saved.compressionQuality, Is.EqualTo(SdfCompressionQuality.Fast));
        }

        [Test]
        public void PlatformSettings_SupportImporterUndoAndRedo()
        {
            var settings = SdfTextureSettings.Get(sourcePath);
            SdfTextureSettings.Set(sourcePath, settings);
            Undo.IncrementCurrentGroup();
            settings.android.overridden = true;
            settings.android.maxSize = 128;
            settings.android.compressionQuality = SdfCompressionQuality.Fast;
            SdfTextureSettings.Set(sourcePath, settings);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(SdfTextureSettings.Get(sourcePath).android.overridden, Is.False);
            Undo.PerformRedo();
            var restored = SdfTextureSettings.Get(sourcePath).android;
            Assert.That(restored.overridden, Is.True);
            Assert.That(restored.maxSize, Is.EqualTo(128));
            Assert.That(restored.compressionQuality, Is.EqualTo(SdfCompressionQuality.Fast));
        }

        [Test]
        public void NativeTexturePlatforms_PreserveAllFieldsAndSupportUndoRedo()
        {
            var settings = SdfTextureSettings.Get(sourcePath);
            SdfTextureSettings.Set(sourcePath, settings);
            string before = SdfTextureSettings.Fingerprint(sourcePath);
            Undo.IncrementCurrentGroup();
            settings.texturePlatforms.Add(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 2048,
                resizeAlgorithm = TextureResizeAlgorithm.Mitchell, format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.CompressedLQ, compressionQuality = 73,
                crunchedCompression = true, allowsAlphaSplitting = true,
                androidETC2FallbackOverride = AndroidETC2FallbackOverride.Quality16Bit
            });
            SdfTextureSettings.Set(sourcePath, settings);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                Assert.That(SdfTextureSettings.Fingerprint(sourcePath), Is.EqualTo(before));
            Assert.That(JsonUtility.ToJson(SdfTextureSettings.Get(sourcePath).texturePlatforms[0]),
                Is.EqualTo(JsonUtility.ToJson(settings.texturePlatforms[0])));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(SdfTextureSettings.Get(sourcePath).texturePlatforms, Is.Empty);
            Undo.PerformRedo();
            Assert.That(JsonUtility.ToJson(SdfTextureSettings.Get(sourcePath).texturePlatforms[0]),
                Is.EqualTo(JsonUtility.ToJson(settings.texturePlatforms[0])));
        }

        [Test]
        public void Settings_LegacyJsonWithEscapedBraces_PreservesForeignPrefixAndSuffix()
        {
            const string prefix = "{\"otherImporter\":{\"value\":17}}";
            const string suffix = "\n[another tool]\n{\"text\":\"} {\",\"enabled\":true}";
            const string json = "{\"enabled\":false,\"padding\":12,\"range\":8,\"note\":\"escaped \\\" } { \\\\ end\",\"nested\":{\"text\":\"}\"}}";
            SetUserData(prefix + Marker + json + suffix);

            var settings = SdfTextureSettings.Get(sourcePath);
            Assert.That(settings.padding, Is.EqualTo(12));
            Assert.That(settings.enabled, Is.False);
            settings.padding = 24;
            SdfTextureSettings.Set(sourcePath, settings);
            string updated = AssetImporter.GetAtPath(sourcePath).userData;
            Assert.That(updated, Does.StartWith(prefix + Marker));
            Assert.That(updated, Does.EndWith(EndMarker + suffix));
            Assert.That(SdfTextureSettings.Get(sourcePath).padding, Is.EqualTo(24));

            string fingerprint = SdfTextureSettings.Fingerprint(sourcePath);
            SetUserData(updated + "\nmore unrelated metadata");
            Assert.That(SdfTextureSettings.Fingerprint(sourcePath), Is.EqualTo(fingerprint),
                "Other importer metadata must not change the SDF inputs or trigger a new bake.");
            SdfTextureSettings.Set(sourcePath, settings);
            Assert.That(AssetImporter.GetAtPath(sourcePath).userData, Is.EqualTo(updated + "\nmore unrelated metadata"));
        }

        [Test]
        public void Settings_DamagedTerminatedBlock_IsDisabledAndCanBeReplacedWithoutLosingSuffix()
        {
            const string prefix = "foreign prefix";
            const string suffix = "foreign suffix {still intact}";
            SetUserData(prefix + Marker + "{\"enabled\":true" + EndMarker + suffix);
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);

            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { padding = 20 });
            string updated = AssetImporter.GetAtPath(sourcePath).userData;
            Assert.That(updated, Does.StartWith(prefix + Marker));
            Assert.That(updated, Does.EndWith(EndMarker + suffix));
            Assert.That(SdfTextureSettings.Get(sourcePath).padding, Is.EqualTo(20));
        }

        [TestCase("not JSON")]
        [TestCase("{\"enabled\":true")]
        [TestCase("{\"enabled\":truX}")]
        public void Settings_DamagedUnterminatedBlock_IsPreservedWhenWritingAReplacement(string damaged)
        {
            string previous = "foreign prefix" + Marker + damaged + "\nforeign suffix";
            SetUserData(previous);
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { padding = 20 });
            Assert.That(AssetImporter.GetAtPath(sourcePath).userData, Does.StartWith(previous + Marker));
            Assert.That(SdfTextureSettings.Get(sourcePath).padding, Is.EqualTo(20));
        }

        [Test]
        public void Settings_OverlongBlock_IsDisabledWithoutDiscardingUnownedText()
        {
            string previous = Marker + "{\"enabled\":true,\"note\":\"" + new string('x', 20000) + "\"}\nforeign suffix";
            SetUserData(previous);
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { padding = 20 });
            Assert.That(AssetImporter.GetAtPath(sourcePath).userData, Does.StartWith(previous + Marker));
            Assert.That(SdfTextureSettings.Get(sourcePath).padding, Is.EqualTo(20));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BuildCheck_ValidatesUnbakedSpriteDependenciesOutsideBuildScenes(bool preload)
        {
            EditorBuildSettings.scenes = Array.Empty<EditorBuildSettingsScene>();
            PlayerSettings.SetPreloadedAssets(Array.Empty<Object>());
            string effectPath = folder + "/Effect.prefab";
            var effect = new GameObject("Effect", typeof(RectTransform), typeof(SdfImage));
            GameObject effectPrefab;
            try
            {
                effect.GetComponent<SdfImage>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                effectPrefab = PrefabUtility.SaveAsPrefabAsset(effect, effectPath);
            }
            finally { Object.DestroyImmediate(effect); }

            if (preload) PlayerSettings.SetPreloadedAssets(new Object[] { effectPrefab });
            else
            {
                AssetDatabase.CreateFolder(folder, "Resources");
                var root = new GameObject("Runtime Loaded Root");
                try
                {
                    var nested = (GameObject)PrefabUtility.InstantiatePrefab(effectPrefab);
                    nested.transform.SetParent(root.transform, false);
                    PrefabUtility.SaveAsPrefabAsset(root, folder + "/Resources/Root.prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }

            var check = new SdfBuildCheck();
            Assert.DoesNotThrow(() => check.OnPreprocessBuild(null), "Ordinary Image fallback must remain buildable.");
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { enabled = true, padding = 8, range = 8 });
            var failure = Assert.Throws<BuildFailedException>(() => check.OnPreprocessBuild(null));
            Assert.That(failure.Message, Does.Contain(sourcePath));
        }

        [UnityTest]
        public IEnumerator CacheHit_RestoresCurrentLatestPointerWithoutReimportingOrRewritingItAgain()
        {
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Baking requires asynchronous GPU readback.");
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { enabled = true, padding = 8, range = 8 });
            string fingerprint = SdfTextureSettings.Fingerprint(sourcePath);
            yield return WaitUntilReady(fingerprint);
            var source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            var attached = SdfSprite.FromSprite(source);
            Assert.That(attached, Is.Not.Null);
            string latest = Path.Combine(cacheDirectory, "latest.txt");

            // Simulate a different accepted generation that was canceled before source reimport.
            File.WriteAllText(latest, "another-approved-generation");
            Assert.That(SdfBakeCache.TryRead(sourcePath, source, fingerprint, out _), Is.False);
            int completions = 0;
            void Completed(string path) { if (path == sourcePath) completions++; }
            SdfBakeQueue.Completed += Completed;
            try
            {
                SdfBakeQueue.Enqueue(sourcePath);
                yield return WaitUntilReady(fingerprint);
                Assert.That(SdfBakeCache.TryRead(sourcePath, source, fingerprint, out _), Is.True);
                Assert.That(SdfSprite.FromSprite(source), Is.SameAs(attached));
                Assert.That(completions, Is.Zero, "An already attached cache hit must only restore the pointer.");

                File.SetLastWriteTimeUtc(latest, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
                DateTime written = File.GetLastWriteTimeUtc(latest);
                SdfBakeQueue.Enqueue(sourcePath);
                yield return WaitUntilReady(fingerprint);
                Assert.That(File.GetLastWriteTimeUtc(latest), Is.EqualTo(written), "Repeated cache hits must not rewrite latest.txt.");
            }
            finally { SdfBakeQueue.Completed -= Completed; }
        }

        [Test]
        public void BuildCheck_EditorOnlyResourcesPrefab_DoesNotBlockThePlayerBuild()
        {
            EditorBuildSettings.scenes = Array.Empty<EditorBuildSettingsScene>();
            PlayerSettings.SetPreloadedAssets(Array.Empty<Object>());
            AssetDatabase.CreateFolder(folder, "Editor");
            AssetDatabase.CreateFolder(folder + "/Editor", "Resources");
            var preview = new GameObject("Editor Preview", typeof(RectTransform), typeof(SdfImage));
            try
            {
                preview.GetComponent<SdfImage>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                PrefabUtility.SaveAsPrefabAsset(preview, folder + "/Editor/Resources/Preview.prefab");
            }
            finally { Object.DestroyImmediate(preview); }
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { enabled = true, padding = 8, range = 8 });
            Assert.That(SdfSprite.FromSprite(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath)), Is.Null);
            Assert.DoesNotThrow(() => new SdfBuildCheck().OnPreprocessBuild(null));
        }

        [UnityTest]
        public IEnumerator DisabledWarmCache_RestoresMissingDependencyAndStableArtifactsWithoutBaking()
        {
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("The initial bake requires asynchronous GPU readback.");
            SdfTextureSettings.Set(sourcePath, new SdfTextureSettings { enabled = true, padding = 8, range = 8 });
            string fingerprint = SdfTextureSettings.Fingerprint(sourcePath);
            yield return WaitUntilReady(fingerprint);
            var source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            var baked = SdfSprite.FromSprite(source);
            string[] previousIds = { Identity(baked), Identity(baked.ColorTexture), Identity(baked.DistanceTexture) };
            string dependency = "SDFImage/Bake/" + AssetDatabase.AssetPathToGUID(sourcePath);
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = false;
            SdfTextureSettings.Set(sourcePath, settings);

            int completions = 0;
            void Completed(string path) { if (path == sourcePath) completions++; }
            SdfBakeQueue.Completed += Completed;
            try
            {
                // A fresh Editor may load cache before its custom dependency registration is restored.
                AssetDatabase.RegisterCustomDependency(dependency, default);
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                double settle = EditorApplication.timeSinceStartup + 0.75;
                while (EditorApplication.timeSinceStartup < settle) yield return null;
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                baked = SdfSprite.FromSprite(source);
                Assert.That(baked, Is.Not.Null, "Last-good cache must be restored even when generation is disabled.");
                Assert.That(baked.BakeFingerprint, Is.EqualTo(fingerprint));
                Assert.That(new[] { Identity(baked), Identity(baked.ColorTexture), Identity(baked.DistanceTexture) }, Is.EqualTo(previousIds));
                Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
                Assert.That(completions, Is.Zero, "Restoring cached artifacts must not start a bake or publish a new generation.");
            }
            finally { SdfBakeQueue.Completed -= Completed; }
        }

        private static string Identity(Object asset)
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id), Is.True);
            return guid + ":" + id;
        }

        private IEnumerator WaitUntilReady(string fingerprint)
        {
            double deadline = EditorApplication.timeSinceStartup + 30;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                var baked = SdfSprite.FromSprite(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
                if (baked && baked.BakeFingerprint == fingerprint && SdfBakeQueue.GetStatus(sourcePath) == "Ready") yield break;
                if (SdfBakeQueue.GetStatus(sourcePath).StartsWith("Error:", StringComparison.Ordinal)) break;
                yield return null;
            }
            Assert.Fail("SDF did not become ready: " + SdfBakeQueue.GetStatus(sourcePath));
        }

        private void SetUserData(string value)
        {
            var importer = AssetImporter.GetAtPath(sourcePath);
            importer.userData = value;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(sourcePath);
        }
    }
}
