using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SDFUI.Editor
{
    /// <summary>One asynchronous GPU snapshot or CPU bake at a time. Asset publishing is handled by subscribers.</summary>
    [InitializeOnLoad]
    public static class SdfBakeQueue
    {
        private const int MaximumBatchPixels = 4 * 1024 * 1024;
        private const int MaximumSpriteCount = 128;
        private static readonly Queue<string> pending = new Queue<string>();
        private static readonly HashSet<string> queued = new HashSet<string>();
        private static readonly Dictionary<string, string> statuses = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> failedFingerprints = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> attemptedPublications = new Dictionary<string, string>();
        private static Work active;
        public static event Action<string> Completed;

        private sealed class SpriteWork
        {
            public string key, file;
            public Rect rect;
            public SdfBakeData data;
        }

        private sealed class Work
        {
            public string path, fingerprint, directory;
            public Texture2D source;
            public bool sRGB, reading, committing, reloadLocked;
            public TextureResizeAlgorithm resizeAlgorithm;
            public RenderTexture readbackTexture;
            public readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            public readonly List<SpriteWork> sprites = new List<SpriteWork>();
            public Task worker;
            public int index;
        }

        static SdfBakeQueue()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += CancelAll;
            EditorApplication.quitting += CancelAll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) CancelAll();
            };
        }

        public static void Enqueue(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            try
            {
                if (!SdfTextureSettings.Get(path).enabled)
                {
                    Cancel(path);
                    statuses[path] = "Disabled";
                    return;
                }
                string fingerprint = SdfTextureSettings.Fingerprint(path);
                if (failedFingerprints.TryGetValue(path, out string failed) && failed == fingerprint) return;
                if (active != null && active.path == path)
                {
                    if (active.fingerprint == fingerprint && !active.cancellation.IsCancellationRequested) return;
                    active.cancellation.Cancel();
                }
                if (queued.Add(path)) pending.Enqueue(path);
                statuses[path] = "Queued";
            }
            catch (Exception exception) { statuses[path] = "Error: " + exception.Message; }
        }

        public static void Cancel(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            path = path.Replace('\\', '/');
            queued.Remove(path);
            failedFingerprints.Remove(path);
            attemptedPublications.Remove(path);
            if (active != null && active.path == path) active.cancellation.Cancel();
            statuses[path] = "Canceled";
        }

        public static string GetStatus(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Disabled";
            return statuses.TryGetValue(path.Replace('\\', '/'), out string status) ? status : "Not baked";
        }

        private static void CancelAll()
        {
            foreach (string path in queued) statuses[path] = "Canceled";
            queued.Clear();
            pending.Clear();
            if (active != null)
            {
                active.cancellation.Cancel();
                statuses[active.path] = "Canceled";
            }
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return;
            try
            {
                if (active == null)
                {
                    while (pending.Count > 0)
                    {
                        string path = pending.Dequeue();
                        if (!queued.Remove(path)) continue;
                        Begin(path);
                        break;
                    }
                    return;
                }
                if (active.reading || (active.worker != null && !active.worker.IsCompleted)) return;
                if (active.worker != null)
                {
                    // IsCompleted was checked above; this propagates errors without waiting on the Editor thread.
                    active.worker.GetAwaiter().GetResult();
                    active.worker = null;
                    if (!active.committing) active.index++;
                }
                if (!IsCurrent(active))
                {
                    string path = active.path;
                    bool changed = !active.cancellation.IsCancellationRequested;
                    Finish();
                    // Explicit cancellation stays canceled; a newer Enqueue already has its own pending entry.
                    if (changed && SdfTextureSettings.Get(path).enabled) Enqueue(path);
                    return;
                }
                if (active.committing)
                {
                    string path = active.path;
                    SdfBakeCache.PublishLatest(active.directory, active.fingerprint);
                    statuses[path] = "Ready";
                    failedFingerprints.Remove(path);
                    attemptedPublications[path] = active.fingerprint;
                    Finish();
                    Completed?.Invoke(path);
                }
                else if (active.index == active.sprites.Count)
                {
                    Work work = active;
                    work.committing = true;
                    work.worker = Task.Run(() => SdfBakeCache.CommitBatch(work.directory, work.fingerprint,
                        work.cancellation.Token), work.cancellation.Token);
                }
                else StartReadback(active);
            }
            catch (OperationCanceledException) { Finish(); }
            catch (Exception exception) { Fail(exception); }
        }

        private static void Begin(string path)
        {
            var settings = SdfTextureSettings.Get(path);
            if (!settings.enabled) { statuses[path] = "Disabled"; return; }
            active = new Work { path = path, fingerprint = SdfTextureSettings.Fingerprint(path), directory = SdfBakeCache.DirectoryFor(path) };
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            active.source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!importer || !active.source || importer.textureType != TextureImporterType.Sprite)
                throw new InvalidOperationException("Select a Sprite imported from a source image.");
            active.sRGB = importer.sRGBTexture;
            var platform = settings.ResolveTexturePlatform(EditorUserBuildSettings.activeBuildTarget);
            active.resizeAlgorithm = platform.resizeAlgorithm;
            var identities = new HashSet<string>();
            long totalPixels = 0;
            bool needsPublish = false;
            foreach (Object item in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(item is Sprite source)) continue;
                SdfSprite attached = SdfSprite.FromSprite(source);
                needsPublish |= !attached || attached.BakeFingerprint != active.fingerprint;
                Rect rect = source.rect;
                if (rect.width <= 0 || rect.height <= 0 || rect.xMin < 0 || rect.yMin < 0 ||
                    rect.xMax > active.source.width + 0.01f || rect.yMax > active.source.height + 0.01f)
                    throw new InvalidOperationException("A Sprite rectangle cannot be mapped to its original imported texture.");
                string key = SdfBakeCache.SpriteKey(source);
                if (!identities.Add(key)) throw new InvalidOperationException("The source contains duplicate Sprite IDs.");
                float scale = Mathf.Min(1f, platform.maxTextureSize / Mathf.Max(rect.width, rect.height));
                int width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
                totalPixels += (long)(width + settings.padding * 2) * (height + settings.padding * 2);
                if (identities.Count > MaximumSpriteCount || totalPixels > MaximumBatchPixels)
                    throw new InvalidOperationException("This image exceeds the SDF budget of 128 sprites or 4 million padded pixels. Reduce Max Size or padding.");
                Vector4 border = source.border;
                border.x *= (float)width / rect.width; border.z *= (float)width / rect.width;
                border.y *= (float)height / rect.height; border.w *= (float)height / rect.height;
                active.sprites.Add(new SpriteWork
                {
                    key = key, rect = rect, file = SdfBakeCache.FileFor(active.directory, key, active.fingerprint),
                    data = new SdfBakeData
                    {
                        fingerprint = active.fingerprint, sRGB = active.sRGB, width = width, height = height, padding = settings.padding,
                        range = settings.range, alphaThreshold = settings.alphaThreshold, ppu = source.pixelsPerUnit * scale,
                        border = border, pivot = new Vector2(source.pivot.x / rect.width, source.pivot.y / rect.height)
                    }
                });
            }
            if (active.sprites.Count == 0) throw new InvalidOperationException("The source image contains no imported Sprites.");
            var keys = new string[active.sprites.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = active.sprites[i].key;
            if (SdfBakeCache.BatchReady(active.directory, keys, active.fingerprint))
            {
                if (needsPublish)
                {
                    if (attemptedPublications.TryGetValue(path, out string attempted) && attempted == active.fingerprint)
                        throw new InvalidOperationException("The source reimport did not attach its SDF result. Check import errors, then disable and enable SDF to retry.");
                    attemptedPublications[path] = active.fingerprint;
                }
                else attemptedPublications.Remove(path);
                // The attached result may already match after returning to an older cached setting.
                SdfBakeCache.PublishLatest(active.directory, active.fingerprint);
                statuses[path] = "Ready";
                Finish();
                if (needsPublish) Completed?.Invoke(path);
                return;
            }
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new NotSupportedException("SDF auto-bake requires an Editor graphics device with asynchronous GPU readback.");
            StartReadback(active);
        }

        private static bool IsCurrent(Work work) => !work.cancellation.IsCancellationRequested &&
            SdfTextureSettings.Get(work.path).enabled && SdfTextureSettings.Fingerprint(work.path) == work.fingerprint;

        private static void StartReadback(Work work)
        {
            SpriteWork sprite = work.sprites[work.index];
            statuses[work.path] = "Baking " + (work.index + 1) + "/" + work.sprites.Count;
            if (!work.source) throw new InvalidOperationException("The source texture changed while preparing its bake.");
            work.readbackTexture = RenderTexture.GetTemporary(sprite.data.width, sprite.data.height, 0,
                RenderTextureFormat.ARGB32, work.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            bool previousSRGB = GL.sRGBWrite;
            RenderTexture previousTarget = RenderTexture.active;
            try
            {
                GL.sRGBWrite = work.sRGB && QualitySettings.activeColorSpace == ColorSpace.Linear;
                SdfTextureResize.Blit(work.source, sprite.rect, work.readbackTexture, work.resizeAlgorithm, work.sRGB);
                EditorApplication.LockReloadAssemblies();
                work.reloadLocked = true;
                work.reading = true;
                AsyncGPUReadback.Request(work.readbackTexture, 0, TextureFormat.RGBA32, request => ReadbackReady(work, sprite, request));
            }
            catch
            {
                ReleaseReadback(work);
                throw;
            }
            finally
            {
                GL.sRGBWrite = previousSRGB;
                RenderTexture.active = previousTarget;
            }
        }

        private static void ReadbackReady(Work work, SpriteWork sprite, AsyncGPUReadbackRequest request)
        {
            try
            {
                if (work.cancellation.IsCancellationRequested || active != work) return;
                if (request.hasError) throw new InvalidOperationException("The GPU could not read the sprite pixels asynchronously.");
                Color32[] pixels = request.GetData<Color32>().ToArray();
                work.worker = Task.Run(() =>
                {
                    BuildPixels(sprite.data, pixels, work.cancellation.Token);
                    SdfBakeCache.Write(sprite.file, sprite.data, work.cancellation.Token);
                    sprite.data.color = null;
                    sprite.data.distanceHalf = null;
                }, work.cancellation.Token);
            }
            catch (Exception exception) { if (active == work) Fail(exception); }
            finally { ReleaseReadback(work); }
        }

        private static void ReleaseReadback(Work work)
        {
            work.reading = false;
            if (work.readbackTexture) RenderTexture.ReleaseTemporary(work.readbackTexture);
            work.readbackTexture = null;
            if (work.reloadLocked) EditorApplication.UnlockReloadAssemblies();
            work.reloadLocked = false;
        }

        private static void Finish()
        {
            if (active == null) return;
            active.cancellation.Dispose();
            active = null;
        }

        private static void Fail(Exception exception)
        {
            if (active == null) return;
            statuses[active.path] = "Error: " + exception.Message;
            failedFingerprints[active.path] = active.fingerprint;
            Finish();
        }

        private static void BuildPixels(SdfBakeData data, Color32[] source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int width = data.width + data.padding * 2;
            int height = data.height + data.padding * 2;
            var pixels = new Color32[width * height];
            for (int y = 0; y < data.height; y++)
            {
                token.ThrowIfCancellationRequested();
                Array.Copy(source, y * data.width, pixels, (y + data.padding) * width + data.padding, data.width);
            }
            Dilate(pixels, width, height, token);
            float[] distances = SdfDistanceTransform.Generate(pixels, width, height, data.alphaThreshold, data.range, token);
            var half = new ushort[distances.Length];
            for (int i = 0; i < half.Length; i++)
            {
                if (i % width == 0) token.ThrowIfCancellationRequested();
                // EDT distances are finite, >= 0.5 in magnitude, and <= 4096: normal half-float range.
                uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(distances[i]));
                uint magnitude = bits & 0x7fffffff;
                uint rounded = magnitude + 0xfff + ((magnitude >> 13) & 1);
                half[i] = (ushort)(((bits >> 16) & 0x8000) | ((rounded - 0x38000000) >> 13));
            }
            data.color = pixels;
            data.distanceHalf = half;
        }

        private static void Dilate(Color32[] pixels, int width, int height, CancellationToken token)
        {
            var queue = new int[pixels.Length];
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (i % width == 0) token.ThrowIfCancellationRequested();
                if (pixels[i].a != 0) queue[count++] = i;
            }
            int seeds = count;
            for (int head = 0; head < count; head++)
            {
                if (head % width == 0) token.ThrowIfCancellationRequested();
                int index = queue[head];
                int x = index % width, y = index / width;
                Color32 color = pixels[index]; color.a = 1;
                if (x > 0) Visit(index - 1, color);
                if (x + 1 < width) Visit(index + 1, color);
                if (y > 0) Visit(index - width, color);
                if (y + 1 < height) Visit(index + width, color);
            }
            for (int i = seeds; i < count; i++)
            {
                if (i % width == 0) token.ThrowIfCancellationRequested();
                Color32 color = pixels[queue[i]]; color.a = 0; pixels[queue[i]] = color;
            }
            void Visit(int index, Color32 color)
            {
                if (pixels[index].a != 0) return;
                pixels[index] = color;
                queue[count++] = index;
            }
        }
    }
}
