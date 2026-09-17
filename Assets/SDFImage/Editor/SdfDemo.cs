using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SDFUI.Editor
{
    /// <summary>Creates self-contained sample assets without changing the open scene.</summary>
    public static class SdfDemo
    {
        [MenuItem("Tools/SDF Outline/Create Demo Prefab")]
        public static void Create()
        {
            string path = CreateAt("Assets/SDFImageDemo");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        public static string CreateAt(string folder)
        {
            if (!folder.StartsWith("Assets/", System.StringComparison.Ordinal) || folder.Contains(".."))
                throw new System.ArgumentException("Demo folder must be inside Assets.", nameof(folder));
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var star = CreateSprite(folder, "Star", 0);
            var panel = CreateSprite(folder, "RoundedPanel", 1);
            var ring = CreateSprite(folder, "Ring", 2);
            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("SDF Outline Demo", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler));
            root.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(960, 640);
                scaler.matchWidthOrHeight = 0.5f;
                var background = new GameObject("Background", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                background.transform.SetParent(root.transform, false);
                var bgRect = (RectTransform)background.transform;
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
                var bg = background.GetComponent<UnityEngine.UI.Image>();
                bg.color = new Color32(21, 28, 48, 255);
                bg.raycastTarget = false;

                var outer = Add(root.transform, "01 - Outer outline", star, new Vector2(-310, 150), new Vector2(130, 130));
                outer.OutlineColor = Color.white;
                outer.OutlineWidth = 7;

                var inner = Add(root.transform, "02 - Inner outline", star, new Vector2(0, 150), new Vector2(130, 130));
                inner.OutlineColor = new Color32(145, 57, 7, 255);
                inner.OutlinePosition = SdfOutlinePosition.Inner;
                inner.OutlineWidth = 7;

                var centered = Add(root.transform, "03 - Center outline and shadow", star, new Vector2(310, 150), new Vector2(130, 130));
                centered.OutlinePosition = SdfOutlinePosition.Center;
                centered.OutlineColor = new Color32(255, 247, 188, 255);
                centered.OutlineWidth = 9;
                centered.ShadowColor = new Color(0, 0, 0, 0.8f);
                centered.ShadowOffset = new Vector2(12, -14);
                centered.ShadowBlur = 12;

                var hollow = Add(root.transform, "04 - Hollow sprite and glow", ring, new Vector2(-310, -105), new Vector2(132, 132));
                hollow.OutlineWidth = 3;
                hollow.OutlineColor = new Color32(201, 255, 252, 255);
                hollow.ShadowColor = new Color(0.12f, 0.85f, 1, 0.65f);
                hollow.ShadowOffset = Vector2.zero;
                hollow.ShadowBlur = 18;
                hollow.ShadowSpread = 5;

                var sliced = Add(root.transform, "05 - Nine-sliced panel", panel, new Vector2(40, -105), new Vector2(290, 104));
                sliced.type = UnityEngine.UI.Image.Type.Sliced;
                sliced.OutlineColor = new Color32(225, 214, 255, 255);
                sliced.OutlineWidth = 3;
                sliced.ShadowColor = new Color(0, 0, 0, 0.7f);
                sliced.ShadowOffset = new Vector2(0, -9);
                sliced.ShadowBlur = 8;

                var clip = new GameObject("06 - RectMask2D", typeof(RectTransform), typeof(UnityEngine.UI.RectMask2D));
                clip.transform.SetParent(root.transform, false);
                var clipRect = (RectTransform)clip.transform;
                clipRect.sizeDelta = new Vector2(130, 150);
                clipRect.anchoredPosition = new Vector2(330, -105);
                var clipped = Add(clip.transform, "Clipped star", star, new Vector2(38, 0), new Vector2(140, 140));
                clipped.OutlineColor = Color.white;
                clipped.OutlineWidth = 6;
                clipped.ShadowColor = new Color(0, 0, 0, 0.8f);
                clipped.ShadowOffset = new Vector2(8, -8);
                clipped.ShadowBlur = 9;

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/SDF UI Demo.prefab");
                root.hideFlags = HideFlags.None;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return prefabPath;
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static SdfImage Add(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(SdfImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<SdfImage>();
            image.sprite = sprite;
            image.RefreshSdf();
            image.raycastTarget = false;
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            image.preserveAspect = true;
            image.OutlineWidth = 0;
            image.ShadowColor = Color.clear;
            return image;
        }

        private static Sprite CreateSprite(string folder, string name, int shape)
        {
            const int size = 256;
            const float density = size / 96f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 point = (new Vector2(x + 0.5f, y + 0.5f) - Vector2.one * (size * 0.5f)) / density;
                    float signedDistance;
                    Color color;
                    if (shape == 0)
                    {
                        float angle = Mathf.Atan2(point.x, point.y);
                        float radius = 31 + 10 * Mathf.Cos(angle * 5);
                        signedDistance = radius - point.magnitude;
                        color = Color.Lerp(new Color32(255, 155, 38, 255), new Color32(255, 226, 109, 255), y / (size - 1f));
                    }
                    else if (shape == 1)
                    {
                        Vector2 q = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - Vector2.one * 24;
                        signedDistance = 17 - new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude - Mathf.Min(Mathf.Max(q.x, q.y), 0);
                        color = Color.Lerp(new Color32(104, 67, 210, 255), new Color32(171, 128, 246, 255), y / (size - 1f));
                    }
                    else
                    {
                        signedDistance = 10 - Mathf.Abs(point.magnitude - 30);
                        color = new Color32(37, 195, 218, 255);
                    }
                    color.a = Mathf.Clamp01(signedDistance * density + 0.5f);
                    pixels[y * size + x] = color;
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".png");
                File.WriteAllBytes(path, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100 * density;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                if (shape == 1)
                    importer.spriteBorder = Vector4.one * (28 * density);
                importer.SaveAndReimport();
                var settings = SdfTextureSettings.Get(path);
                settings.enabled = true;
                SdfTextureSettings.Set(path, settings);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
