using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SDFUI.Editor
{
    [CustomEditor(typeof(SdfSprite))]
    public sealed class SdfSpriteEditor : UnityEditor.Editor
    {
        private SdfTextureSettings settings;
        private string path, savedJson;
        private SdfPlatformSettingsGUI platformGUI;
        private bool showTextures;

        private void OnEnable()
        {
            path = AssetDatabase.GetAssetPath(target);
            platformGUI = new SdfPlatformSettingsGUI(AssetImporter.GetAtPath(path) as TextureImporter);
            Reload();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            platformGUI?.Dispose();
        }

        private void OnUndoRedo() => Reload();

        private void Reload()
        {
            settings = SdfTextureSettings.Get(path);
            savedJson = JsonUtility.ToJson(settings);
            if (this) Repaint();
        }

        public override void OnInspectorGUI()
        {
            // Imported objects are read-only; these controls edit the owning source's import metadata.
            bool wasEnabled = GUI.enabled;
            try { GUI.enabled = true; DrawSettings(); }
            finally { GUI.enabled = wasEnabled; }
        }

        private void DrawSettings()
        {
            var sprite = (SdfSprite)target;
            if (!sprite || !(AssetImporter.GetAtPath(path) is TextureImporter))
            {
                EditorGUILayout.HelpBox("Select an SDF generated from a source image inside Assets.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Source Sprite", sprite.SourceSprite, typeof(Sprite), false);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SDF Import Settings", EditorStyles.boldLabel);
            settings.enabled = EditorGUILayout.Toggle(new GUIContent("Auto Update", "Rebake when source pixels or bake settings change."), settings.enabled);
            settings.padding = EditorGUILayout.IntSlider("Padding", settings.padding, 4, 128);
            settings.range = EditorGUILayout.Slider("Distance Range", settings.range, 4, settings.padding);
            settings.alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", settings.alphaThreshold, 0.01f, 0.99f);
            settings.compressDistance = EditorGUILayout.Toggle(new GUIContent("Compress Distance",
                "Pad the distance map to power-of-two dimensions without resizing the sprite, then encode BC4 on desktop or EAC R on mobile (R8 elsewhere). Disable for full-precision RHalf."), settings.compressDistance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Texture", EditorStyles.boldLabel);
            platformGUI.Draw(settings);

            bool changed = savedJson != JsonUtility.ToJson(settings);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!changed))
                {
                    if (GUILayout.Button("Revert", GUILayout.Width(75))) Reload();
                    if (GUILayout.Button("Apply", GUILayout.Width(75)))
                    {
                        SdfTextureSettings.Set(path, settings);
                        Reload();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            string status = SdfBakeQueue.GetStatus(path);
            if (status.StartsWith("Error:")) EditorGUILayout.HelpBox(status, MessageType.Error);
            else EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            if (status.StartsWith("Baking ") || status == "Queued") Repaint();

            using (new EditorGUI.DisabledScope(changed))
                if (GUILayout.Button("Refresh SDF"))
                {
                    settings.enabled = true;
                    SdfBakeQueue.Cancel(path);
                    SdfTextureSettings.Set(path, settings);
                    Reload();
                }
            if (GUILayout.Button(new GUIContent("Clear SDF",
                "Remove generated SDF textures for all sprites in this source image and turn off Auto Update. Keep the source and saved bake settings.")))
            {
                string sourcePath = path;
                Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(sourcePath);
                SdfTextureSettings.Clear(sourcePath);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.Space();
            showTextures = EditorGUILayout.Foldout(showTextures, "Imported Textures", true);
            if (showTextures)
            {
                DrawTexture("Color", sprite.ColorTexture);
                DrawTexture("Distance", sprite.DistanceTexture);
            }
        }

        private static void DrawTexture(string label, Texture2D texture)
        {
            if (texture) EditorGUILayout.LabelField(label, texture.width + " x " + texture.height + " · " + texture.format);
        }

        public override bool HasPreviewGUI() => target && ((SdfSprite)target).ColorTexture;
        public override void OnPreviewGUI(Rect rectangle, GUIStyle background)
        {
            var texture = ((SdfSprite)target).ColorTexture;
            if (texture) EditorGUI.DrawPreviewTexture(rectangle, texture, null, ScaleMode.ScaleToFit);
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var texture = ((SdfSprite)target).ColorTexture;
            if (!texture || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            float scale = Mathf.Min((float)width / texture.width, (float)height / texture.height);
            width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var preview = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                preview.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                // Unity owns this temporary Project thumbnail; it is not a baked runtime texture.
                preview.Apply(false, false);
                return preview;
            }
            catch { DestroyImmediate(preview); throw; }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
