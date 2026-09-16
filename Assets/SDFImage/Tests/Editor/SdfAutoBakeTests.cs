using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SDFUI.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    public sealed class SdfAutoBakeTests
    {
        private string folder;
        private string sourcePath;
        private int completedCount;
        private int importedCount;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/SDFImageAutoTest_" + Guid.NewGuid().ToString("N");
            sourcePath = folder + "/Source.png";
            completedCount = 0;
            importedCount = 0;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            SdfBakeQueue.Completed += OnCompleted;
            SdfAutoBakeImportCounter.Imported += OnImported;
        }

        [TearDown]
        public void TearDown()
        {
            SdfBakeQueue.Completed -= OnCompleted;
            SdfAutoBakeImportCounter.Imported -= OnImported;
            if (!string.IsNullOrEmpty(sourcePath))
                SdfBakeQueue.Cancel(sourcePath);
            if (!string.IsNullOrEmpty(folder))
                AssetDatabase.DeleteAsset(folder);
        }

        [UnityTest]
        public IEnumerator ImageLayerEditsUndoAndEnable_KeepReadyWithoutQueueingOrReimporting()
        {
            CreateSource();
            Enable(8);
            yield return WaitForBake(1, result => result.Padding == 8);
            var go = new GameObject("SDF Layer Edits", typeof(RectTransform), typeof(SdfImage));
            var image = go.GetComponent<SdfImage>();
            var statuses = new HashSet<string>();
            void CaptureStatus() => statuses.Add(SdfBakeQueue.GetStatus(sourcePath));
            try
            {
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                yield return Settle();
                int imports = importedCount;
                int completions = completedCount;
                SdfSprite data = image.SdfData;
                Texture distance = data.DistanceTexture;
                EditorApplication.update += CaptureStatus;

                using (var serialized = new SerializedObject(image))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Undo.IncrementCurrentGroup();
                        serialized.Update();
                        var layer = serialized.FindProperty("sdfLayers").GetArrayElementAtIndex(0);
                        layer.FindPropertyRelative("width").floatValue = 3 + i;
                        layer.FindPropertyRelative("softness").floatValue = i * 0.1f;
                        layer.FindPropertyRelative("offset").vector2Value = new Vector2(i, -i);
                        layer.FindPropertyRelative("color").colorValue = Color.Lerp(Color.red, Color.blue, i / 8f);
                        layer.FindPropertyRelative("useTextureColor").boolValue = i % 2 == 0;
                        Assert.That(serialized.ApplyModifiedProperties(), Is.True);
                        image.RefreshEffects();
                        Undo.FlushUndoRecordObjects();
                        yield return null;
                    }
                }
                yield return Settle();
                Undo.PerformUndo();
                yield return Settle();
                Undo.PerformRedo();
                yield return Settle();
                image.enabled = false;
                image.enabled = true;
                SdfSourceImporter.RefreshTarget(image);
                yield return Settle();

                Assert.That(completedCount, Is.EqualTo(completions), "Layer edits must not rebake the distance map.");
                Assert.That(importedCount, Is.EqualTo(imports), "Layer edits must not reimport the source.");
                Assert.That(image.SdfData, Is.SameAs(data));
                Assert.That(image.SdfData.DistanceTexture, Is.SameAs(distance));
                Assert.That(statuses, Is.EquivalentTo(new[] { "Ready" }),
                    "Ready must stay stable while editing layers, undoing, refreshing or enabling the image.");
            }
            finally
            {
                EditorApplication.update -= CaptureStatus;
                Undo.ClearUndo(image);
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator ImageSourceRefresh_StillSchedulesMissingAndStaleBakes()
        {
            CreateSource();
            Enable(8);
            SdfBakeQueue.Cancel(sourcePath);
            yield return Settle();
            // No imported SDF yet; assigning the source must still schedule its enabled bake.
            Assert.That(FindEmbedded(), Is.Null);
            var go = new GameObject("SDF Source Refresh", typeof(RectTransform), typeof(SdfImage));
            var image = go.GetComponent<SdfImage>();
            try
            {
                using (var serialized = new SerializedObject(image))
                {
                    serialized.FindProperty("m_Sprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                yield return WaitForBake(1, result => result.Padding == 8);
                yield return Settle();
                Assert.That(image.SdfData.Padding, Is.EqualTo(8));

                Enable(12);
                SdfBakeQueue.Cancel(sourcePath);
                yield return Settle();
                Assert.That(image.SdfData.Padding, Is.EqualTo(8), "Keep the previous result until the replacement is ready.");
                SdfSourceImporter.RefreshTarget(image);
                yield return WaitForBake(2, result => result.Padding == 12);
                yield return Settle();
                Assert.That(image.SdfData.Padding, Is.EqualTo(12));
                Assert.That(image.SdfData.BakeFingerprint, Is.EqualTo(SdfTextureSettings.Fingerprint(sourcePath)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator OrdinarySpriteImport_IsDisabledByDefaultAndDoesNotScheduleSdfWork()
        {
            CreateSource();
            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            Assert.That(settings.enabled, Is.False);
            Assert.That(settings.maxSize, Is.EqualTo(512));
            Assert.That(settings.colorCompression, Is.EqualTo(SdfColorCompression.Automatic));
            int importsAfterSetup = importedCount;
            yield return Settle();

            Assert.That(FindEmbedded(), Is.Null);
            Assert.That(completedCount, Is.Zero);
            Assert.That(importedCount, Is.EqualTo(importsAfterSetup), "An ordinary import must not start an import loop.");
            Assert.That(Directory.GetFiles(folder, "*.asset"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator EnablingAndRebaking_EmbedsAllResultsInSourceWithStableObjectIds()
        {
            CreateSource();
            byte[] originalPng = File.ReadAllBytes(sourcePath);
            string importerBefore = ImporterSettingsJson();
            string sourceSpriteId = StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
            Enable(8);
            yield return WaitForBake(1, result => result.Padding == 8);

            SdfSprite first = FindEmbedded();
            Assert.That(first.DistanceTexture.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.None), "SDF must be selectable below its source in Project.");
            Assert.That(AssetDatabase.LoadAllAssetRepresentationsAtPath(sourcePath), Does.Contain(first.DistanceTexture));
            string mainId = StableId(first);
            string colorId = StableId(first.ColorTexture);
            string distanceId = StableId(first.DistanceTexture);
            AssertSharedSourcePath(first);

            Enable(12);
            yield return WaitForBake(2, result => result.Padding == 12);
            SdfSprite second = FindEmbedded();
            AssertSharedSourcePath(second);
            Assert.That(StableId(second), Is.EqualTo(mainId));
            Assert.That(StableId(second.ColorTexture), Is.EqualTo(colorId));
            Assert.That(StableId(second.DistanceTexture), Is.EqualTo(distanceId));
            Assert.That(StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath)), Is.EqualTo(sourceSpriteId));
            Assert.That(second.ColorTexture.width, Is.EqualTo(56));
            Assert.That(second.DistanceTexture.width, Is.EqualTo(56));
            Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(originalPng));
            Assert.That(ImporterSettingsJson(), Is.EqualTo(importerBefore), "SDF opt-in must preserve source texture settings.");
            Assert.That(Directory.GetFiles(folder, "*.asset"), Is.Empty, "No separate baked asset should be written.");
        }

        [UnityTest]
        public IEnumerator Clear_RemovesGeneratedAssetsWithoutChangingSource_AndSupportsUndoRedoAndGenerate()
        {
            CreateSource();
            byte[] original = File.ReadAllBytes(sourcePath);
            string sourceSettings = ImporterSettingsJson();
            string sourceId = StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
            string textureId = StableId(AssetDatabase.LoadMainAssetAtPath(sourcePath));
            Enable(8);
            yield return WaitForBake(1, result => result.Padding == 8);
            string sdfId = StableId(FindEmbedded());
            string colorId = StableId(FindEmbedded().ColorTexture);
            string distanceId = StableId(FindEmbedded().DistanceTexture);

            // Undo must restore a retained bake even when Auto Update was already disabled.
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = false;
            SdfTextureSettings.Set(sourcePath, settings);
            Undo.FlushUndoRecordObjects();
            Undo.ClearUndo(AssetImporter.GetAtPath(sourcePath));
            Undo.IncrementCurrentGroup();
            SdfTextureSettings.Clear(sourcePath);
            yield return Settle();
            Assert.That(FindEmbedded(), Is.Null);
            Assert.That(SdfSprite.FromSprite(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath)), Is.Null);
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            Assert.That(SdfTextureSettings.Get(sourcePath).padding, Is.EqualTo(8));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(sourcePath).Length, Is.EqualTo(2),
                "Clear must remove both generated textures and the descriptor, leaving the source texture and Sprite.");
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            yield return Settle();
            Assert.That(FindEmbedded(), Is.Null, "A warm cache must not republish a cleared result.");
            Assert.That(completedCount, Is.EqualTo(1));

            Undo.PerformUndo();
            yield return Settle();
            Assert.That(FindEmbedded(), Is.Not.Null, "Undo must restore the SDF without requiring Auto Update.");
            Assert.That(StableId(FindEmbedded()), Is.EqualTo(sdfId));
            Undo.PerformRedo();
            yield return Settle();
            Assert.That(FindEmbedded(), Is.Null);

            settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, result => result.Padding == 8);
            Assert.That(StableId(FindEmbedded()), Is.EqualTo(sdfId));
            Assert.That(StableId(FindEmbedded().ColorTexture), Is.EqualTo(colorId));
            Assert.That(StableId(FindEmbedded().DistanceTexture), Is.EqualTo(distanceId));
            Assert.That(StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath)), Is.EqualTo(sourceId));
            Assert.That(StableId(AssetDatabase.LoadMainAssetAtPath(sourcePath)), Is.EqualTo(textureId));
            Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(original));
            Assert.That(ImporterSettingsJson(), Is.EqualTo(sourceSettings));
            Undo.ClearUndo(AssetImporter.GetAtPath(sourcePath));
        }

        [UnityTest]
        public IEnumerator Clear_DuringActiveRebake_CannotPublishLateResults()
        {
            CreateSource(512);
            Enable(8);
            yield return WaitForBake(1, result => result.Padding == 8);
            Enable(12);
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!SdfBakeQueue.GetStatus(sourcePath).StartsWith("Baking ", StringComparison.Ordinal)
                && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(SdfBakeQueue.GetStatus(sourcePath), Does.StartWith("Baking "));
            SdfTextureSettings.Clear(sourcePath);
            yield return Settle();
            SdfBakeQueue.Enqueue(sourcePath);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            yield return Settle();
            Assert.That(FindEmbedded(), Is.Null);
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            Assert.That(completedCount, Is.EqualTo(1), "An in-flight worker must not republish after Clear.");
            Undo.ClearUndo(AssetImporter.GetAtPath(sourcePath));
        }

        [UnityTest]
        public IEnumerator DisablingBeforeQueueCompletion_CancelsPendingWorkAndPreventsManualEnqueue()
        {
            CreateSource();
            Enable(8);
            SdfTextureSettings disabled = SdfTextureSettings.Get(sourcePath);
            disabled.enabled = false;
            SdfTextureSettings.Set(sourcePath, disabled);
            SdfBakeQueue.Enqueue(sourcePath);
            yield return Settle();

            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.False);
            Assert.That(FindEmbedded(), Is.Null);
            Assert.That(completedCount, Is.Zero, "Canceled or disabled sources must not publish completed results.");
        }

        [UnityTest]
        public IEnumerator Default512Bake_CanCancelWhileActiveAndKeepsTheEditorUpdating()
        {
            CreateSource(512);
            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            Assert.That(settings.maxSize, Is.EqualTo(512));
            settings.enabled = true;
            SdfTextureSettings.Set(sourcePath, settings);
            Assert.That(completedCount, Is.Zero, "Opting in must return before the asynchronous bake completes.");

            double startDeadline = EditorApplication.timeSinceStartup + 10;
            while (!SdfBakeQueue.GetStatus(sourcePath).StartsWith("Baking ", StringComparison.Ordinal)
                && EditorApplication.timeSinceStartup < startDeadline)
                yield return null;
            Assert.That(SdfBakeQueue.GetStatus(sourcePath), Does.StartWith("Baking "),
                "The test must cancel an active GPU/worker job, not only an item still queued.");

            var cancelTimer = System.Diagnostics.Stopwatch.StartNew();
            SdfBakeQueue.Cancel(sourcePath);
            cancelTimer.Stop();
            yield return Settle();
            Assert.That(SdfTextureSettings.Get(sourcePath).enabled, Is.True);
            Assert.That(completedCount, Is.Zero, "An explicit cancellation must not automatically restart the same active job.");
            Assert.That(FindEmbedded(), Is.Null);

            int editorUpdates = 0;
            double previousUpdate = EditorApplication.timeSinceStartup;
            double maximumUpdateGap = 0;
            void CountEditorUpdate()
            {
                double now = EditorApplication.timeSinceStartup;
                maximumUpdateGap = Math.Max(maximumUpdateGap, now - previousUpdate);
                previousUpdate = now;
                editorUpdates++;
            }

            EditorApplication.update += CountEditorUpdate;
            var bakeTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                settings.enabled = true;
                SdfTextureSettings.Set(sourcePath, settings);
                Assert.That(completedCount, Is.Zero);
                yield return WaitForBake(1, result => result.SourceSize == new Vector2Int(512, 512));
                bakeTimer.Stop();
                Assert.That(editorUpdates, Is.GreaterThanOrEqualTo(2),
                    "The Editor must continue processing updates while the default-size bake runs.");
                Assert.That(FindEmbedded().ColorTexture.width, Is.EqualTo(576));
                TestContext.Out.WriteLine(FormattableString.Invariant(
                    $"SDF512_TIMING total_ms={bakeTimer.Elapsed.TotalMilliseconds:F1} max_update_gap_ms={maximumUpdateGap * 1000:F1} editor_updates={editorUpdates} cancel_call_ms={cancelTimer.Elapsed.TotalMilliseconds:F1}"));
            }
            finally
            {
                EditorApplication.update -= CountEditorUpdate;
            }
        }

        [UnityTest]
        public IEnumerator RapidSettingsChanges_PublishOnlyTheNewestRequestedResult()
        {
            CreateSource();
            Enable(8);
            Enable(12);
            Enable(20, 0.65f);
            yield return WaitForBake(1, result => result.Padding == 20 && Mathf.Approximately(result.AlphaThreshold, 0.65f));
            int completionsAfterLatest = completedCount;
            yield return Settle();

            SdfSprite result = FindEmbedded();
            Assert.That(result.Padding, Is.EqualTo(20));
            Assert.That(result.DistanceRange, Is.EqualTo(20));
            Assert.That(result.AlphaThreshold, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(completedCount, Is.EqualTo(completionsAfterLatest), "Superseded jobs must not publish later.");
            Assert.That(completedCount, Is.EqualTo(1), "Changes queued before the next Editor update should coalesce.");
        }

        [UnityTest]
        public IEnumerator MaximumSize_BoundsGeneratedTexturesWithoutChangingNativeDisplaySize()
        {
            CreateSource(128);
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            float originalNativeWidth = source.rect.width / source.pixelsPerUnit;
            string originalTextureId = StableId(source.texture);
            string originalMainId = StableId(AssetDatabase.LoadMainAssetAtPath(sourcePath));
            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            settings.maxSize = 64;
            settings.padding = 8;
            settings.range = 8;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, result => result.SourceSize.x == 64);

            SdfSprite result = FindEmbedded();
            Assert.That(result.SourceSize, Is.EqualTo(new Vector2Int(64, 64)));
            Assert.That(result.ColorTexture.width, Is.EqualTo(80));
            Assert.That(result.DistanceTexture.width, Is.EqualTo(128));
            Assert.That(result.SourceSize.x / result.PixelsPerUnit, Is.EqualTo(originalNativeWidth).Within(0.0001f));
            Texture2D original = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath).texture;
            Assert.That(original.width, Is.EqualTo(128), "Maximum SDF size must not downsample the original imported texture.");
            Assert.That(StableId(original), Is.EqualTo(originalTextureId));
            Assert.That(StableId(AssetDatabase.LoadMainAssetAtPath(sourcePath)), Is.EqualTo(originalMainId),
                "Attaching generated subassets must not replace the source's original main asset.");
        }

        [UnityTest]
        public IEnumerator EditingSourcePixels_RebakesAutomaticallyWithoutChangingOriginalRgbaOrLoopingImports()
        {
            CreateSource();
            Enable(8);
            yield return WaitForBake(1, result => SdfTestTextureReadback.Pixel(result.ColorTexture, 24, 24).a > 0.95f);
            string sourceSpriteId = StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath));
            string sourceTextureId = StableId(AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath).texture);

            WriteSourcePixels(true);
            byte[] editedPng = File.ReadAllBytes(sourcePath);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            yield return WaitForBake(2, result => SdfTestTextureReadback.Pixel(result.ColorTexture, 24, 24).a < 0.05f
                && SdfTestTextureReadback.Pixel(result.ColorTexture, 13, 24).r > 0.95f);

            SdfSprite edited = FindEmbedded();
            Assert.That(SdfTestTextureReadback.Pixel(edited.ColorTexture, 13, 18).a, Is.GreaterThan(0.95f),
                "The source's lower opaque strip must remain at the bottom after GPU readback.");
            Assert.That(SdfTestTextureReadback.Pixel(edited.ColorTexture, 13, 30).a, Is.LessThan(0.05f),
                "The asymmetric top strip must remain transparent after GPU readback.");

            Assert.That(File.ReadAllBytes(sourcePath), Is.EqualTo(editedPng));
            var importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            Texture2D originalTexture = importedSprite.texture;
            Assert.That(originalTexture, Is.Not.Null);
            Assert.That(originalTexture.width, Is.EqualTo(32));
            Assert.That(StableId(originalTexture), Is.EqualTo(sourceTextureId), "The original Sprite must keep its original RGBA texture.");
            Assert.That(AssetDatabase.GetAssetPath(originalTexture), Is.EqualTo(sourcePath));
            Assert.That(StableId(importedSprite), Is.EqualTo(sourceSpriteId));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(sourcePath)).isReadable, Is.False);

            int importsAfterCompletion = importedCount;
            int completionsAfterCompletion = completedCount;
            yield return Settle();
            Assert.That(importedCount, Is.EqualTo(importsAfterCompletion), "Embedding generated objects must not reimport repeatedly.");
            Assert.That(completedCount, Is.EqualTo(completionsAfterCompletion));
        }

        [UnityTest]
        public IEnumerator MultipleSprites_KeepSeparateArtworkMetadataAndStableAttachmentsAfterReimport()
        {
            CreateSpriteSheet();
            Enable(8);
            yield return WaitForBake(1, result => result.Padding == 8);

            Sprite left = FindSourceSprite("Left Green");
            Sprite right = FindSourceSprite("Right Red");
            SdfSprite leftData = SdfSprite.FromSprite(left);
            SdfSprite rightData = SdfSprite.FromSprite(right);
            Assert.That(leftData, Is.Not.Null);
            Assert.That(rightData, Is.Not.Null);
            Assert.That(leftData, Is.Not.EqualTo(rightData));
            Assert.That(leftData.SourceSprite, Is.EqualTo(left));
            Assert.That(rightData.SourceSprite, Is.EqualTo(right));
            Assert.That(left.rect, Is.EqualTo(new Rect(0, 0, 32, 32)));
            Assert.That(right.rect, Is.EqualTo(new Rect(32, 0, 32, 32)));
            Assert.That(leftData.SourceSize, Is.EqualTo(new Vector2Int(32, 32)));
            Assert.That(rightData.SourceSize, Is.EqualTo(new Vector2Int(32, 32)));
            Assert.That(Vector2.Distance(leftData.Pivot, new Vector2(0.25f, 0.75f)), Is.LessThan(0.0001f));
            Assert.That(Vector2.Distance(rightData.Pivot, new Vector2(0.8f, 0.2f)), Is.LessThan(0.0001f));
            Assert.That(leftData.Border, Is.EqualTo(new Vector4(2, 4, 6, 8)));
            Assert.That(rightData.Border, Is.EqualTo(new Vector4(5, 3, 7, 9)));
            Assert.That(leftData.PixelsPerUnit, Is.EqualTo(64));
            Assert.That(rightData.PixelsPerUnit, Is.EqualTo(64));

            Color leftCenter = SdfTestTextureReadback.Pixel(leftData.ColorTexture, 24, 24);
            Color rightCenter = SdfTestTextureReadback.Pixel(rightData.ColorTexture, 24, 24);
            Assert.That(leftCenter.g, Is.GreaterThan(0.95f));
            Assert.That(leftCenter.r, Is.LessThan(0.05f));
            Assert.That(rightCenter.r, Is.GreaterThan(0.95f));
            Assert.That(rightCenter.g, Is.LessThan(0.05f));
            Assert.That(SdfTestTextureReadback.Pixel(leftData.ColorTexture, 12, 10).a, Is.GreaterThan(0.95f));
            Assert.That(SdfTestTextureReadback.Pixel(rightData.ColorTexture, 12, 10).a, Is.LessThan(0.05f),
                "The second Sprite must use its own cropped alpha shape, not the first Sprite's rectangle.");

            string[] originalIds = SpriteAndDataIds(left, leftData, right, rightData);
            foreach (Object item in new Object[] { leftData, rightData, leftData.ColorTexture,
                rightData.ColorTexture, leftData.DistanceTexture, rightData.DistanceTexture })
                Assert.That(AssetDatabase.GetAssetPath(item), Is.EqualTo(sourcePath));

            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            yield return Settle();
            left = FindSourceSprite("Left Green");
            right = FindSourceSprite("Right Red");
            leftData = SdfSprite.FromSprite(left);
            rightData = SdfSprite.FromSprite(right);
            Assert.That(leftData, Is.Not.Null);
            Assert.That(rightData, Is.Not.Null);
            Assert.That(SpriteAndDataIds(left, leftData, right, rightData), Is.EqualTo(originalIds));
            Assert.That(completedCount, Is.EqualTo(1), "A force import with unchanged inputs should reuse the completed sheet cache.");
            Assert.That(Directory.GetFiles(folder, "*.asset"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator CompressedOddSize_ReimportAndOptOut_PreservePixelsAndObjectIdsWithoutCpuCopies()
        {
            CreateSource(33);
            Enable(9);
            yield return WaitForBake(1, result => result.Padding == 9);
            SdfSprite data = FindEmbedded();
            Assert.That(data.SourceSize, Is.EqualTo(new Vector2Int(33, 33)));
            Assert.That(data.DistanceTexture.width, Is.EqualTo(51));
            bool compressedTarget = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
                || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows
                || EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64
                || EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                || EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS
                || EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS;
            Assert.That(data.ColorTexture.width, Is.EqualTo(compressedTarget ? 52 : 51));
            if (compressedTarget)
                Assert.That(new[] { TextureFormat.BC7, TextureFormat.ETC2_RGBA8, TextureFormat.ASTC_4x4 }, Does.Contain(data.ColorTexture.format));
            else Assert.That(data.ColorTexture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(data.DistanceTexture.format, Is.EqualTo(TextureFormat.RHalf));
            Assert.That(data.ColorTexture.isReadable, Is.False);
            Assert.That(data.DistanceTexture.isReadable, Is.False);
            Assert.That(SdfTestTextureReadback.Pixel(data.ColorTexture, 25, 25).g, Is.GreaterThan(0.95f));
            string colorId = StableId(data.ColorTexture), distanceId = StableId(data.DistanceTexture), id = StableId(data);
            Assert.That(SdfBakeCache.TryRead(sourcePath, data.SourceSprite, data.BakeFingerprint, out SdfBakeData baked), Is.True);
            var request = UnityEngine.Rendering.AsyncGPUReadback.Request(data.DistanceTexture, 0);
            request.WaitForCompletion();
            Assert.That(request.hasError, Is.False);
            Assert.That(request.GetData<ushort>().ToArray(), Is.EqualTo(baked.distanceHalf), "Distance values must remain bit exact.");

            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            data = FindEmbedded();
            Assert.That(data.IsValid, Is.True);
            Assert.That(data.ColorTexture.isReadable, Is.False);
            Assert.That(StableId(data.ColorTexture), Is.EqualTo(colorId));

            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = false;
            settings.colorCompression = SdfColorCompression.Uncompressed;
            SdfTextureSettings.Set(sourcePath, settings);
            data = FindEmbedded();
            Assert.That(data.IsValid, Is.True);
            Assert.That(data.ColorTexture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(data.ColorTexture.width, Is.EqualTo(51));
            Assert.That(data.ColorTexture.isReadable, Is.False);
            Assert.That(data.DistanceTexture.isReadable, Is.False);
            Assert.That(StableId(data), Is.EqualTo(id));
            Assert.That(StableId(data.ColorTexture), Is.EqualTo(colorId));
            Assert.That(StableId(data.DistanceTexture), Is.EqualTo(distanceId));
            Assert.That(SdfTestTextureReadback.Pixel(data.ColorTexture, 25, 25).g, Is.GreaterThan(0.95f));
        }

        [UnityTest]
        public IEnumerator ActivePlatformOverride_ControlsSizeAndCompression_WithoutTouchingSourceImportSettings()
        {
            CreateSource(128);
            string original = ImporterSettingsJson();
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            SdfPlatformSettings active = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? settings.android
                : EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS || EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS
                    ? settings.ios : settings.standalone;
            active.overridden = true;
            active.maxSize = 64;
            active.colorCompression = SdfColorCompression.Uncompressed;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, result => result.SourceSize.x == 64);
            SdfSprite data = FindEmbedded();
            Assert.That(data.ColorTexture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(data.ColorTexture.isReadable, Is.False);
            Assert.That(data.NativeSize.x, Is.EqualTo(2).Within(0.0001f));
            Assert.That(ImporterSettingsJson(), Is.EqualTo(original));
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath).width, Is.EqualTo(128));
        }

        private void Enable(int padding, float threshold = 0.5f)
        {
            SdfTextureSettings settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            settings.compressDistance = false; // These lifecycle tests also cover the full-precision path.
            settings.maxSize = 512;
            settings.padding = padding;
            settings.range = padding;
            settings.alphaThreshold = threshold;
            SdfTextureSettings.Set(sourcePath, settings);
        }

        [UnityTest]
        public IEnumerator CompressedDistance_PadsToPotWithoutResizing_AndRetainsIdentityWhenDisabled()
        {
            CreateSource(400);
            string sourceSettings = ImporterSettingsJson();
            var settings = SdfTextureSettings.Get(sourcePath);
            Assert.That(settings.compressDistance, Is.True, "New bakes should use compressed distance storage.");
            settings.enabled = true;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, data => data.NormalizedDistance);
            var data = FindEmbedded();
            Assert.That(data.SourceSize, Is.EqualTo(new Vector2Int(400, 400)));
            Assert.That(data.DistanceTexture.width, Is.EqualTo(512));
            Assert.That(data.DistanceTexture.height, Is.EqualTo(512));
            Assert.That(data.ColorTexture.width, Is.EqualTo(464), "Only the distance map needs the extra POT storage.");
            var expected = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Standalone
                ? TextureFormat.BC4 : EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? TextureFormat.EAC_R : data.DistanceTexture.format;
            Assert.That(data.DistanceTexture.format, Is.EqualTo(expected));
            Assert.That(data.DistanceTexture.isReadable, Is.False);
            Assert.That(data.DistanceTexture.mipmapCount, Is.EqualTo(1));
            Assert.That(data.NativeSize, Is.EqualTo(new Vector2(6.25f, 6.25f)));
            Assert.That(SdfBakeCache.TryRead(sourcePath, data.SourceSprite, data.BakeFingerprint, out SdfBakeData cached), Is.True);
            var samples = SdfTestTextureReadback.Pixels(data.DistanceTexture);
            float maxError = 0;
            for (int y = 0; y < 512; y++)
                for (int x = 0; x < 512; x++)
                {
                    float reference = x < 464 && y < 464 ? Mathf.HalfToFloat(cached.distanceHalf[y * 464 + x]) : -32;
                    float decoded = samples[y * 512 + x].r / 255f * 64 - 32;
                    maxError = Mathf.Max(maxError, Mathf.Abs(decoded - reference));
                    if (x >= 464 || y >= 464) Assert.That(decoded, Is.LessThan(-31), "Extra POT texels must stay outside the shape.");
                }
            Assert.That(maxError, Is.LessThan(0.75f), "Compression must preserve distances to within a source texel.");
            var bytes = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.ComputeMipmapSize(512, 512, data.DistanceTexture.graphicsFormat);
            Assert.That(bytes, Is.EqualTo(data.DistanceTexture.format == TextureFormat.R8 ? 262144 : 131072),
                "BC4/EAC R should use 128 KiB for the 512-square distance map (256 KiB for R8 fallback).");
            Debug.Log("SDF_DISTANCE_STORAGE format=" + data.DistanceTexture.format + " bytes=" + bytes + " maxDistanceError=" + maxError);
            string descriptorId = StableId(data), distanceId = StableId(data.DistanceTexture);
            settings.enabled = false;
            settings.compressDistance = false;
            SdfTextureSettings.Set(sourcePath, settings);
            data = FindEmbedded();
            Assert.That(data.NormalizedDistance, Is.False);
            Assert.That(data.DistanceTexture.format, Is.EqualTo(TextureFormat.RHalf));
            Assert.That(data.DistanceTexture.width, Is.EqualTo(464));
            Assert.That(StableId(data), Is.EqualTo(descriptorId));
            Assert.That(StableId(data.DistanceTexture), Is.EqualTo(distanceId));
            Undo.PerformUndo();
            yield return Settle();
            data = FindEmbedded();
            Assert.That(data.NormalizedDistance, Is.True);
            Assert.That(data.DistanceTexture.width, Is.EqualTo(512));
            Assert.That(StableId(data.DistanceTexture), Is.EqualTo(distanceId));
            Assert.That(ImporterSettingsJson(), Is.EqualTo(sourceSettings));
        }

        [UnityTest]
        public IEnumerator CompressedDistance_RectangularPaddingKeepsSourceSizeAndIndependentColorSettings()
        {
            CreateSource(129, 37);
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            settings.padding = 9;
            settings.range = 9;
            settings.colorCompression = SdfColorCompression.Uncompressed;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, data => data.NormalizedDistance);
            var data = FindEmbedded();
            Assert.That(data.SourceSize, Is.EqualTo(new Vector2Int(129, 37)));
            Assert.That(data.DistanceTexture.width, Is.EqualTo(256));
            Assert.That(data.DistanceTexture.height, Is.EqualTo(64));
            Assert.That(data.ColorTexture.width, Is.EqualTo(147));
            Assert.That(data.ColorTexture.height, Is.EqualTo(55));
            Assert.That(data.ColorTexture.format, Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(data.NativeSize, Is.EqualTo(new Vector2(129f / 64, 37f / 64)));
            Assert.That(new[] { TextureFormat.BC4, TextureFormat.EAC_R, TextureFormat.R8 }, Does.Contain(data.DistanceTexture.format));
            Assert.That(SdfTestTextureReadback.Pixel(data.DistanceTexture, 255, 63).r, Is.LessThan(0.01f));
            Assert.That(SdfTestTextureReadback.Pixel(data.DistanceTexture, 73, 27).r, Is.GreaterThan(0.9f));
        }

        [UnityTest]
        public IEnumerator NativeCompressionSettings_ApplyFormatsCrunchAndResizeWithoutChangingSource()
        {
            CreateSource(129);
            var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            sourceImporter.filterMode = FilterMode.Point;
            sourceImporter.SaveAndReimport();
            string originalImporter = ImporterSettingsJson();
            var settings = SdfTextureSettings.Get(sourcePath);
            settings.enabled = true;
            settings.compressDistance = false; // Isolate color format/resize controls from distance compression.
            settings.padding = 8;
            settings.range = 8;
            var native = new TextureImporterPlatformSettings
            {
                name = "DefaultTexturePlatform", maxTextureSize = 64,
                format = TextureImporterFormat.RGBA32, resizeAlgorithm = TextureResizeAlgorithm.Bilinear,
                textureCompression = TextureImporterCompression.CompressedHQ, compressionQuality = 80
            };
            settings.texturePlatforms.Add(native);
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(1, data => data.SourceSize.x == 64 && data.ColorTexture.format == TextureFormat.RGBA32);
            string colorId = StableId(FindEmbedded().ColorTexture);
            var bilinear = SdfTestTextureReadback.Pixels(FindEmbedded().ColorTexture);
            Assert.That(Array.Exists(bilinear, pixel => pixel.a > 0 && pixel.a < 255), Is.True,
                "Bilinear resizing must filter edges even if the original Sprite uses Point filtering.");

            native.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            SdfTextureSettings.Set(sourcePath, settings);
            yield return WaitForBake(2, data => data.BakeFingerprint == SdfTextureSettings.Fingerprint(sourcePath));
            var mitchell = SdfTestTextureReadback.Pixels(FindEmbedded().ColorTexture);
            Assert.That(Array.Exists(mitchell, pixel => pixel.g > 240), Is.True, "Mitchell must preserve source colors.");
            bool resizeChanged = false;
            for (int i = 0; i < bilinear.Length; i++) resizeChanged |= !bilinear[i].Equals(mitchell[i]);
            Assert.That(resizeChanged, Is.True, "Resize Algorithm must change the generated pixels, not just metadata.");
            Assert.That(FindEmbedded().DistanceTexture.format, Is.EqualTo(TextureFormat.RHalf));

            if (BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Standalone)
            {
                native.format = TextureImporterFormat.DXT5;
                SdfTextureSettings.Set(sourcePath, settings);
                yield return WaitForBake(3, data => data.ColorTexture.format == TextureFormat.DXT5);
                native.format = TextureImporterFormat.Automatic;
                native.crunchedCompression = true;
                SdfTextureSettings.Set(sourcePath, settings);
                yield return WaitForBake(4, data => data.ColorTexture.format == TextureFormat.DXT5Crunched);
                Assert.That(FindEmbedded().ColorTexture.isReadable, Is.False);
                Assert.That(FindEmbedded().DistanceTexture.isReadable, Is.False);
                Assert.That(StableId(FindEmbedded().ColorTexture), Is.EqualTo(colorId));
            }
            else if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                native.format = TextureImporterFormat.ASTC_6x6;
                SdfTextureSettings.Set(sourcePath, settings);
                yield return WaitForBake(3, data => data.ColorTexture.format == TextureFormat.ASTC_6x6);
                Assert.That(FindEmbedded().ColorTexture.width, Is.EqualTo(84));
                Assert.That(FindEmbedded().DistanceTexture.width, Is.EqualTo(80));
                native.format = TextureImporterFormat.Automatic;
                native.crunchedCompression = true;
                SdfTextureSettings.Set(sourcePath, settings);
                yield return WaitForBake(4, data => data.ColorTexture.format == TextureFormat.ETC2_RGBA8Crunched);
                Assert.That(StableId(FindEmbedded().ColorTexture), Is.EqualTo(colorId));
            }
            Assert.That(ImporterSettingsJson(), Is.EqualTo(originalImporter));
        }

        private IEnumerator WaitForBake(int minimumCompletions, Func<SdfSprite, bool> expected)
        {
            double deadline = EditorApplication.timeSinceStartup + 30;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                SdfSprite result = FindEmbedded();
                if (completedCount >= minimumCompletions && result && result.IsValid && expected(result))
                    yield break;
                yield return null;
            }
            var actual = FindEmbedded();
            Assert.Fail("Auto-bake did not publish the expected result: " + SdfBakeQueue.GetStatus(sourcePath)
                + "; completions=" + completedCount + "/" + minimumCompletions
                + "; color=" + (actual ? actual.ColorTexture.format.ToString() : "missing"));
        }

        private static IEnumerator Settle()
        {
            double until = EditorApplication.timeSinceStartup + 0.75;
            while (EditorApplication.timeSinceStartup < until)
                yield return null;
        }

        private void CreateSource(int size = 32, int height = -1)
        {
            WriteSourcePixels(false, size, height);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private void CreateSpriteSheet()
        {
            var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[64 * 32];
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 64; x++)
                    pixels[y * 64 + x] = x < 32 ? new Color32(0, 255, 0, 255)
                        : new Color32(255, 0, 0, x >= 40 && x < 56 && y >= 4 && y < 28 ? (byte)255 : (byte)0);
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
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 64;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
#pragma warning disable 618
            importer.spritesheet = new[]
            {
                new SpriteMetaData
                {
                    name = "Left Green", rect = new Rect(0, 0, 32, 32), alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.25f, 0.75f), border = new Vector4(2, 4, 6, 8)
                },
                new SpriteMetaData
                {
                    name = "Right Red", rect = new Rect(32, 0, 32, 32), alignment = (int)SpriteAlignment.Custom,
                    pivot = new Vector2(0.8f, 0.2f), border = new Vector4(5, 3, 7, 9)
                }
            };
#pragma warning restore 618
            importer.SaveAndReimport();
        }

        private Sprite FindSourceSprite(string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                if (asset is Sprite source && source.name == name)
                    return source;
            Assert.Fail("The source sheet did not import Sprite " + name);
            return null;
        }

        private static string[] SpriteAndDataIds(Sprite left, SdfSprite leftData, Sprite right, SdfSprite rightData)
        {
            return new[]
            {
                StableId(left), StableId(leftData), StableId(leftData.ColorTexture), StableId(leftData.DistanceTexture),
                StableId(right), StableId(rightData), StableId(rightData.ColorTexture), StableId(rightData.DistanceTexture)
            };
        }

        private void WriteSourcePixels(bool edited, int size = 32, int height = -1)
        {
            if (height < 0) height = size;
            var texture = new Texture2D(size, height, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[size * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < size; x++)
                {
                    bool inside = y >= height / 4 && y < (edited ? height * 5 / 8 : height * 3 / 4)
                        && (edited ? x >= size / 16 && x < size * 3 / 8 : x >= size / 4 && x < size * 3 / 4);
                    pixels[y * size + x] = edited
                        ? new Color32(255, 0, 0, inside ? (byte)255 : (byte)0)
                        : new Color32(0, 255, 0, inside ? (byte)255 : (byte)0);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(sourcePath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private SdfSprite FindEmbedded()
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                if (asset is SdfSprite result)
                    return result;
            return null;
        }

        private void AssertSharedSourcePath(SdfSprite result)
        {
            Assert.That(AssetDatabase.GetAssetPath(result), Is.EqualTo(sourcePath));
            Assert.That(AssetDatabase.GetAssetPath(result.ColorTexture), Is.EqualTo(sourcePath));
            Assert.That(AssetDatabase.GetAssetPath(result.DistanceTexture), Is.EqualTo(sourcePath));
            int count = 0;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                if (asset is SdfSprite)
                    count++;
            Assert.That(count, Is.EqualTo(1), "A Single sprite import must expose exactly one embedded SDF.");
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
            var attached = new ScriptableObject[source.GetScriptableObjectsCount()];
            uint attachedCount = source.GetScriptableObjects(attached);
            Assert.That(attachedCount, Is.GreaterThan(0), "The generated descriptor must be attached to the original Sprite.");
            Assert.That(attached, Does.Contain(result), "Sharing the asset path alone does not establish a Sprite attachment.");
            Assert.That(SdfSprite.FromSprite(source), Is.EqualTo(result), "Runtime resolution must return the attached generated data.");
        }

        private string ImporterSettingsJson()
        {
            var settings = new TextureImporterSettings();
            ((TextureImporter)AssetImporter.GetAtPath(sourcePath)).ReadTextureSettings(settings);
            return JsonUtility.ToJson(settings);
        }

        private static string StableId(Object asset)
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId), Is.True);
            return guid + ":" + localId;
        }

        private void OnCompleted(string path)
        {
            if (path == sourcePath)
                completedCount++;
        }

        private void OnImported(string path)
        {
            if (path == sourcePath)
                importedCount++;
        }
    }

    internal static class SdfTestTextureReadback
    {
        public static Color32[] Pixels(Texture source)
        {
            Texture2D copy = Copy(source);
            try { return copy.GetPixels32(); }
            finally { Object.DestroyImmediate(copy); }
        }

        public static Color Pixel(Texture source, int x, int y)
        {
            Texture2D copy = Copy(source);
            try { return copy.GetPixel(x, y); }
            finally { Object.DestroyImmediate(copy); }
        }

        public static Texture2D Copy(Texture source)
        {
            RenderTexture previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply(false, false);
                return copy;
            }
            catch { Object.DestroyImmediate(copy); throw; }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }

    internal sealed class SdfAutoBakeImportCounter : AssetPostprocessor
    {
        internal static event Action<string> Imported;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (Imported == null)
                return;
            foreach (string path in importedAssets)
                Imported(path);
        }
    }
}
