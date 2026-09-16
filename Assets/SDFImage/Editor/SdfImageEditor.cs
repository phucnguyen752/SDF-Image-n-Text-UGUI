using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UI;
using UnityEditorInternal;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace SDFUI.Editor
{
    [CustomEditor(typeof(SdfImage)), CanEditMultipleObjects]
    public sealed class SdfImageEditor : ImageEditor
    {
        private SerializedProperty effectsEnabled, layers, layerCount;
        private ReorderableList layerList;

        protected override void OnEnable()
        {
            base.OnEnable();
            foreach (SdfImage image in targets) _ = image.Layers;
            serializedObject.Update();
            effectsEnabled = serializedObject.FindProperty("sdfEffectsEnabled");
            layers = serializedObject.FindProperty("sdfLayers");
            layerCount = serializedObject.FindProperty("sdfLayers.Array.size");
            layerList = new ReorderableList(serializedObject, layers, !serializedObject.isEditingMultipleObjects, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Layers (top = front)"),
                drawElementCallback = DrawLayer,
                elementHeightCallback = LayerHeight,
                onAddCallback = AddLayer,
                onCanAddCallback = list => !layerCount.hasMultipleDifferentValues && list.count < SdfImage.MaxEffectLayers,
                onCanRemoveCallback = list => !layerCount.hasMultipleDifferentValues && list.count > 0
            };
        }

        public override void OnInspectorGUI()
        {
            // Keep all standard Image controls and their native layout/behavior.
            base.OnInspectorGUI();
            serializedObject.Update();
            bool ready = true, hasSource = false, busy = false;
            string status = string.Empty;
            foreach (SdfImage image in targets)
            {
                hasSource |= image.SourceSprite;
                ready &= image.SdfData && image.SdfData.IsValid;
                string current = SdfBakeQueue.GetStatus(AssetDatabase.GetAssetPath(image.SourceSprite));
                if (IsBusy(current)) { busy = true; status = current; }
            }

            if (hasSource)
            {
                if (busy || !ready) EditorGUILayout.Space(4);
                if (busy)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(status + "…", EditorStyles.miniLabel);
                        if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(64)))
                            ChangeGeneration(false);
                    }
                }
                else if (!ready)
                {
                    EditorGUILayout.LabelField("Generate once to unlock outline and shadow.", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(!CanGenerate()))
                        if (GUILayout.Button("Generate SDF", GUILayout.Height(28))) ChangeGeneration(true);
                    if (!CanGenerate())
                        EditorGUILayout.HelpBox("Generation requires an imported Sprite inside Assets.", MessageType.Info);
                }
                foreach (SdfImage image in targets)
                {
                    string error = SdfBakeQueue.GetStatus(AssetDatabase.GetAssetPath(image.SourceSprite));
                    if (error.StartsWith("Error:", System.StringComparison.Ordinal))
                    { EditorGUILayout.HelpBox(error, MessageType.Error); break; }
                }
            }

            if (ready)
            {
                EditorGUILayout.Space(4);
                bool supported = true;
                foreach (SdfImage image in targets)
                    supported &= image.type == Image.Type.Simple || image.type == Image.Type.Sliced && image.fillCenter;
                if (!supported)
                    EditorGUILayout.HelpBox("SDF effects use Simple or Sliced with Fill Center enabled. " +
                        "Other modes render as a standard Unity Image.", MessageType.Info);
                using (new EditorGUI.DisabledScope(!supported))
                {
                    EditorGUILayout.LabelField("SDF Effects", EditorStyles.boldLabel);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (ToggleSection(effectsEnabled, "Effects Enabled"))
                        {
                            layerList.draggable = !serializedObject.isEditingMultipleObjects;
                            layerList.DoLayoutList();
                            if (layers.arraySize > SdfImage.MaxEffectLayers)
                                EditorGUILayout.HelpBox("Only the first 16 layers are rendered.", MessageType.Warning);
                            else if (layerCount.hasMultipleDifferentValues)
                                EditorGUILayout.HelpBox("Selected images have different layer counts. Edit one image at a time to add, remove or reorder layers.", MessageType.Info);
                            else if (serializedObject.isEditingMultipleObjects)
                                EditorGUILayout.HelpBox("Select a single image to reorder layers.", MessageType.None);
                        }
                    }
                }
            }

            if (serializedObject.ApplyModifiedProperties())
                foreach (SdfImage image in targets) image.RefreshEffects();
            DrawLegacyBindingCleanup();
        }

        private float LayerHeight(int index)
        {
            var element = layers.GetArrayElementAtIndex(index);
            var textureColor = element.FindPropertyRelative("useTextureColor");
            int rows = textureColor.hasMultipleDifferentValues ? 8 : textureColor.boolValue ? 7 : 6;
            return 4 + rows * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing)
                + EditorGUI.GetPropertyHeight(element.FindPropertyRelative("offset"), new GUIContent("Offset"));
        }

        private void DrawLayer(Rect rect, int index, bool active, bool focused)
        {
            var element = layers.GetArrayElementAtIndex(index);
            var enabled = element.FindPropertyRelative("enabled");
            string title = $"Layer {index + 1}";
            if (index == 0) title += " (Front)";
            else if (!layerCount.hasMultipleDifferentValues && index == layers.arraySize - 1) title += " (Back)";
            EditorGUI.BeginProperty(rect, GUIContent.none, element);
            rect.y += 2;
            DrawLayerField(ref rect, enabled, title);
            using (new EditorGUI.DisabledScope(!enabled.boolValue && !enabled.hasMultipleDifferentValues))
            {
                var position = element.FindPropertyRelative("position");
                DrawLayerField(ref rect, position, "Position", "Outer, Inner or Center outlines; Underlay fills the silhouette behind the sprite for shadows and glow.");
                var textureColor = element.FindPropertyRelative("useTextureColor");
                DrawLayerField(ref rect, textureColor, "Use Texture Color");
                if (!textureColor.boolValue || textureColor.hasMultipleDifferentValues)
                    DrawLayerField(ref rect, element.FindPropertyRelative("color"), "Color");
                if (textureColor.boolValue || textureColor.hasMultipleDifferentValues)
                {
                    DrawLayerField(ref rect, element.FindPropertyRelative("textureColorIntensity"), "Intensity");
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.Slider(rect, element.FindPropertyRelative("color").FindPropertyRelative("a"), 0, 1, "Opacity");
                    rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                }
                DrawLayerField(ref rect, element.FindPropertyRelative("width"), position.enumValueIndex == 3 ? "Spread" : "Width");
                DrawLayerField(ref rect, element.FindPropertyRelative("softness"), "Softness");
                DrawLayerField(ref rect, element.FindPropertyRelative("offset"), "Offset");
            }
            EditorGUI.EndProperty();
        }

        private static void DrawLayerField(ref Rect rect, SerializedProperty property, string label, string tooltip = null)
        {
            var content = new GUIContent(label, tooltip);
            rect.height = EditorGUI.GetPropertyHeight(property, content);
            EditorGUI.PropertyField(rect, property, content);
            rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
        }

        private void AddLayer(ReorderableList list)
        {
            int index = layers.arraySize;
            layers.arraySize++;
            var element = layers.GetArrayElementAtIndex(index);
            var defaults = new SdfImageEffect();
            element.FindPropertyRelative("enabled").boolValue = defaults.Enabled;
            element.FindPropertyRelative("color").colorValue = defaults.Color;
            element.FindPropertyRelative("width").floatValue = defaults.Width;
            element.FindPropertyRelative("softness").floatValue = defaults.Softness;
            element.FindPropertyRelative("offset").vector2Value = defaults.Offset;
            element.FindPropertyRelative("position").enumValueIndex = (int)defaults.Position;
            element.FindPropertyRelative("useTextureColor").boolValue = defaults.UseTextureColor;
            element.FindPropertyRelative("textureColorIntensity").floatValue = defaults.TextureColorIntensity;
            element.FindPropertyRelative("legacyRole").intValue = 0;
            list.index = index;
        }
        private static bool ToggleSection(SerializedProperty toggle, string title)
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, new GUIContent(title), toggle);
            EditorGUI.showMixedValue = toggle.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUI.ToggleLeft(rect, title, toggle.boolValue, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck()) toggle.boolValue = value;
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
            return toggle.boolValue || toggle.hasMultipleDifferentValues;
        }

        private bool CanGenerate()
        {
            foreach (SdfImage image in targets)
            {
                string path = AssetDatabase.GetAssetPath(image.SourceSprite);
                if (!image.SourceSprite || !path.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                    !(AssetImporter.GetAtPath(path) is TextureImporter)) return false;
            }
            return true;
        }

        private void ChangeGeneration(bool enabled)
        {
            serializedObject.ApplyModifiedProperties();
            var paths = new HashSet<string>();
            foreach (SdfImage image in targets)
            {
                string path = AssetDatabase.GetAssetPath(image.SourceSprite);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                    !(AssetImporter.GetAtPath(path) is TextureImporter)) continue;
                if (paths.Add(path))
                {
                    var settings = SdfTextureSettings.Get(path);
                    settings.enabled = enabled;
                    // Explicit Generate also retries a previously failed/cancelled generation.
                    SdfBakeQueue.Cancel(path);
                    SdfTextureSettings.Set(path, settings);
                }
                SdfSourceImporter.RefreshTarget(image);
            }
        }

        private void DrawLegacyBindingCleanup()
        {
            if (targets.Length != 1) return;
            var image = (SdfImage)target;
            var binding = image.GetComponent<SdfAutoBake>();
            if (!binding) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("Sprite and generation now live in this component. The old Auto Bake component can be removed.", MessageType.None);
            if (!GUILayout.Button("Remove Legacy Auto Bake")) return;
            Undo.RecordObject(image, "Move SDF Source Into Image");
            binding.Resolve();
            image.RefreshSdf();
            EditorUtility.SetDirty(image);
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            Undo.DestroyObjectImmediate(binding);
        }

        private static bool IsBusy(string status) => status == "Queued" || status.StartsWith("Baking ", System.StringComparison.Ordinal);

        public override bool RequiresConstantRepaint()
        {
            foreach (SdfImage image in targets)
                if (IsBusy(SdfBakeQueue.GetStatus(AssetDatabase.GetAssetPath(image.SourceSprite)))) return true;
            return false;
        }

        [MenuItem("GameObject/UI/SDF Image", false, 2030)]
        private static void CreateImage(MenuCommand command)
        {
            var selectedSprite = Selection.activeObject as Sprite;
            var parent = command.context as GameObject;
            if (!parent) parent = Selection.activeGameObject;
            Canvas canvas = parent ? parent.GetComponentInParent<Canvas>() : null;
            if (!canvas)
            {
                var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create SDF Canvas");
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                parent = canvasObject;
            }
            var imageObject = new GameObject("SDF Image", typeof(RectTransform), typeof(SdfImage));
            Undo.RegisterCreatedObjectUndo(imageObject, "Create SDF Image");
            GameObjectUtility.SetParentAndAlign(imageObject, parent ? parent : canvas.gameObject);
            var image = imageObject.GetComponent<SdfImage>();
            image.rectTransform.sizeDelta = new Vector2(160, 160);
            image.raycastTarget = false;
            image.ShadowEnabled = false;
            if (selectedSprite) image.sprite = selectedSprite;
            Selection.activeGameObject = imageObject;
        }
    }
}
