using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SDFUI.Editor
{
    /// <summary>Publishes already-computed data into the source image's import result. Never computes SDF here.</summary>
    public sealed class SdfSourceImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 12;

        private void OnPostprocessSprites(Texture2D texture, Sprite[] sprites)
        {
            if (context == null || !(assetImporter is TextureImporter importer) ||
                !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return;
            context.DependsOnCustomDependency(SdfBakeCache.DependencyName(assetPath));
            var settings = SdfTextureSettings.Get(importer);
            if (settings.cleared) return;
            BuildTarget target = context.selectedBuildTarget;
            var storage = settings.ResolveTexturePlatform(target);
            string fingerprint = settings.enabled ? SdfTextureSettings.Fingerprint(assetPath, importer, target) : string.Empty;
            foreach (var source in sprites)
            {
                SdfBakeData data = default;
                bool current = settings.enabled && SdfBakeCache.TryRead(assetPath, source, fingerprint, out data);
                // Keep a completed old result while an update is pending or auto-generation is disabled.
                if (!current && !SdfBakeCache.TryReadLatest(assetPath, source, out data)) continue;
                int width = data.width + data.padding * 2, height = data.height + data.padding * 2;
                TextureFormat format = SdfTextureCompression.Format(storage, target);
                Vector2Int colorSize = SdfTextureCompression.Size(width, height, format);
                // Extend the unused right/top edge for block compression; never resize the artwork.
                int colorWidth = colorSize.x;
                int colorHeight = colorSize.y;
                var color = SdfTextureCompression.Encode(PadColor(data.color, width, height, colorWidth, colorHeight),
                    colorWidth, colorHeight, storage, format, data.sRGB, message => context.LogImportWarning(message));
                color.name = source.name + " SDF Color";
                color.hideFlags = HideFlags.HideInHierarchy;
                var distance = settings.compressDistance
                    ? SdfTextureCompression.EncodeDistance(data, storage, target, message => context.LogImportWarning(message))
                    : new Texture2D(width, height, TextureFormat.RHalf, false, true);
                distance.name = source.name + " SDF";
                distance.wrapMode = TextureWrapMode.Clamp;
                distance.filterMode = FilterMode.Bilinear;
                distance.hideFlags = HideFlags.None;
                if (!settings.compressDistance)
                {
                    distance.SetPixelData(data.distanceHalf, 0);
                    distance.Apply(false, true);
                }
                var descriptor = ScriptableObject.CreateInstance<SdfSprite>();
                descriptor.name = source.name + " SDF Data";
                descriptor.hideFlags = HideFlags.HideInHierarchy;
                descriptor.Initialize(source, color, distance, new Vector2Int(data.width, data.height), data.border,
                    data.pivot, data.ppu, data.padding, data.range, data.alphaThreshold, data.fingerprint, settings.compressDistance);
                string identifier = "sdf-image/" + SdfBakeCache.SpriteKey(source);
                context.AddObjectToAsset(identifier + "/color", color);
                context.AddObjectToAsset(identifier + "/distance", distance);
                context.AddObjectToAsset(identifier + "/sprite", descriptor);
                if (!source.AddScriptableObject(descriptor))
                    throw new InvalidOperationException("Could not attach SDF data to Sprite '" + source.name + "'.");
            }
        }

        private static Color32[] PadColor(Color32[] source, int width, int height, int paddedWidth, int paddedHeight)
        {
            if (width == paddedWidth && height == paddedHeight) return source;
            var pixels = new Color32[paddedWidth * paddedHeight];
            for (int y = 0; y < paddedHeight; y++)
            {
                int row = Math.Min(y, height - 1) * width;
                Array.Copy(source, row, pixels, y * paddedWidth, width);
                for (int x = width; x < paddedWidth; x++) pixels[y * paddedWidth + x] = source[row + width - 1];
            }
            return pixels;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            foreach (string path in imported)
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && AssetImporter.GetAtPath(path) is TextureImporter &&
                    (SdfTextureSettings.Get(path).enabled || Directory.Exists(Path.Combine(SdfBakeCache.CacheDirectory, AssetDatabase.AssetPathToGUID(path)))))
                    SdfAutoBakeService.Schedule(path);
            foreach (string path in deleted) SdfBakeQueue.Cancel(path);
            foreach (string path in movedFrom) SdfBakeQueue.Cancel(path);
        }

        public static void RefreshTarget(SdfAutoBake binding) => SdfAutoBakeService.ScheduleBinding(binding);
        public static void RefreshTarget(SdfImage image) => SdfAutoBakeService.ScheduleImage(image);
    }

    [InitializeOnLoad]
    internal static class SdfAutoBakeService
    {
        private static readonly HashSet<string> pendingSources = new HashSet<string>();
        private static readonly HashSet<string> publishSources = new HashSet<string>();
        private static readonly HashSet<SdfImage> pendingImages = new HashSet<SdfImage>();
        private static readonly HashSet<SdfAutoBake> pendingBindings = new HashSet<SdfAutoBake>();
        private static Queue<string> restoreDirectories;

        static SdfAutoBakeService()
        {
            SdfImage.ChangedEditor += ScheduleImage;
            SdfAutoBake.Changed += ScheduleBinding;
            SdfBakeQueue.Completed += path => publishSources.Add(path);
            EditorApplication.update += Update;
            EditorApplication.delayCall += RestoreEnabledSources;
            Undo.undoRedoPerformed += RestoreEnabledSources;
        }

        internal static void Schedule(string path) => pendingSources.Add(path);
        internal static void ScheduleImage(SdfImage image)
        {
            if (image) pendingImages.Add(image);
        }

        internal static void ScheduleBinding(SdfAutoBake binding)
        {
            if (binding) pendingBindings.Add(binding);
        }

        private static void RestoreEnabledSources()
        {
            foreach (var image in Resources.FindObjectsOfTypeAll<SdfImage>())
                ScheduleImage(image);
            foreach (var binding in Resources.FindObjectsOfTypeAll<SdfAutoBake>())
                ScheduleBinding(binding);
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (restoreDirectories == null)
                restoreDirectories = new Queue<string>(Directory.Exists(SdfBakeCache.CacheDirectory)
                    ? Directory.GetDirectories(SdfBakeCache.CacheDirectory) : Array.Empty<string>());
            if (restoreDirectories.Count > 0)
            {
                string guid = Path.GetFileName(restoreDirectories.Dequeue());
                if (Guid.TryParseExact(guid, "N", out _))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is TextureImporter) pendingSources.Add(path);
                }
            }
            if (publishSources.Count > 0)
            {
                // Publish one source per Editor update to leave room for input and repaint.
                string path = null;
                foreach (string source in publishSources) { path = source; break; }
                publishSources.Remove(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                pendingSources.Add(path);
            }
            if (pendingSources.Count > 0)
            {
                var paths = new List<string>(pendingSources);
                pendingSources.Clear();
                var images = Resources.FindObjectsOfTypeAll<SdfImage>();
                var bindings = Resources.FindObjectsOfTypeAll<SdfAutoBake>();
                foreach (string path in paths)
                {
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter)) { SdfBakeQueue.Cancel(path); continue; }
                    if (SdfBakeCache.RegisterPublishedDependency(path)) publishSources.Add(path);
                    if (SdfTextureSettings.Get(path).enabled) SdfBakeQueue.Enqueue(path);
                    else SdfBakeQueue.Cancel(path);
                    foreach (var binding in bindings)
                        if (binding.Source && AssetDatabase.GetAssetPath(binding.Source) == path)
                            pendingBindings.Add(binding);
                    foreach (var image in images)
                        if (image.SourceSprite && AssetDatabase.GetAssetPath(image.SourceSprite) == path)
                            pendingImages.Add(image);
                }
            }
            if (pendingBindings.Count > 0)
            {
                var bindings = new List<SdfAutoBake>(pendingBindings);
                pendingBindings.Clear();
                foreach (var binding in bindings)
                {
                    if (!binding || !binding.Target || EditorUtility.IsPersistent(binding)) continue;
                    // Older scenes may still carry the separate source binding component.
                    var target = binding.Target;
                    var previousSource = target.sprite;
                    binding.Resolve();
                    if (target.sprite != previousSource)
                    {
                        EditorUtility.SetDirty(target);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                    }
                    pendingImages.Add(target);
                }
            }
            if (pendingImages.Count == 0) return;
            var refresh = new List<SdfImage>(pendingImages);
            pendingImages.Clear();
            foreach (var image in refresh)
            {
                if (!image) continue;
                image.RefreshSdf();
                if (!image.SourceSprite) continue;
                string path = AssetDatabase.GetAssetPath(image.SourceSprite);
                // Assigning an ordinary Sprite is valid and does not opt it into generation.
                if (!SdfTextureSettings.Get(path).enabled) continue;
                // Enable, Undo/Redo and explicit refresh may revisit an already current bake.
                // Keep Ready stable instead of queueing another cache check for that source.
                var data = image.SdfData;
                if (!data || !data.IsValid || data.BakeFingerprint != SdfTextureSettings.Fingerprint(path))
                    SdfBakeQueue.Enqueue(path);
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
