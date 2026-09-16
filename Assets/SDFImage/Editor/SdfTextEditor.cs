using TMPro;
using TMPro.EditorUtilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace SDFUI.Editor
{
    [CustomEditor(typeof(SdfText)), CanEditMultipleObjects]
    public sealed class SdfTextEditor : TMP_EditorPanelUI
    {
        private SerializedProperty effectsEnabled, layers, layerCount;
        private ReorderableList layerList;

        protected override void OnEnable()
        {
            base.OnEnable();
            foreach (SdfText text in targets) _ = text.Layers;
            serializedObject.Update();
            effectsEnabled = serializedObject.FindProperty("sdfEffectsEnabled");
            layers = serializedObject.FindProperty("sdfLayers");
            layerCount = serializedObject.FindProperty("sdfLayers.Array.size");
            // Unity's multi-object array move copies one target's values to the others.
            layerList = new ReorderableList(serializedObject, layers,
                !serializedObject.isEditingMultipleObjects, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Layers (top = front)"),
                drawElementCallback = DrawLayer,
                elementHeightCallback = LayerHeight,
                onAddCallback = AddLayer,
                onCanAddCallback = list => !layerCount.hasMultipleDifferentValues,
                onCanRemoveCallback = list => !layerCount.hasMultipleDifferentValues && list.count > 0
            };
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("SDF Effects", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Draws all effect layers behind the whole label. " +
                "Sizes use local Canvas units; font atlas padding limits spread and softness.", MessageType.None);

            bool supported = true;
            foreach (SdfText text in targets)
                supported &= text.EffectsSupported;
            if (!supported)
                EditorGUILayout.HelpBox("Place Canvas, Mask and RectMask2D components on a parent object. " +
                    "SDF Text effects do not support these components on the text object itself.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!supported))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (ToggleSection(effectsEnabled, "Effects Enabled"))
                    {
                        bool differentCounts = layerCount.hasMultipleDifferentValues;
                        layerList.draggable = !serializedObject.isEditingMultipleObjects;
                        layerList.DoLayoutList();
                        if (differentCounts)
                            EditorGUILayout.HelpBox("Selected labels have different layer counts. " +
                                "Edit one label at a time to add, remove or reorder layers.", MessageType.Info);
                        else if (serializedObject.isEditingMultipleObjects)
                            EditorGUILayout.HelpBox("Select a single label to reorder layers.", MessageType.None);
                    }
                }
            }

            if (serializedObject.ApplyModifiedProperties())
                foreach (SdfText text in targets) text.RefreshEffects();
        }

        private float LayerHeight(int index)
        {
            var element = layers.GetArrayElementAtIndex(index);
            return 4 + 4 * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing)
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
                DrawLayerField(ref rect, element.FindPropertyRelative("color"), "Color");
                DrawLayerField(ref rect, element.FindPropertyRelative("width"), "Spread",
                    "Positive expands the glyph shape, zero preserves its size, and negative contracts it.");
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
            var defaults = new SdfTextEffect();
            element.FindPropertyRelative("enabled").boolValue = defaults.Enabled;
            element.FindPropertyRelative("color").colorValue = defaults.Color;
            element.FindPropertyRelative("width").floatValue = defaults.Width;
            element.FindPropertyRelative("softness").floatValue = defaults.Softness;
            element.FindPropertyRelative("offset").vector2Value = defaults.Offset;
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

        [MenuItem("GameObject/UI/SDF Text", false, 2031)]
        private static void CreateText(MenuCommand command)
        {
            var parent = command.context as GameObject;
            if (!parent) parent = Selection.activeGameObject;
            Canvas canvas = parent ? parent.GetComponentInParent<Canvas>() : null;
            if (!canvas)
            {
                var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                StageUtility.PlaceGameObjectInCurrentStage(canvasObject);
                Undo.RegisterCreatedObjectUndo(canvasObject, "Create SDF Canvas");
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                parent = canvasObject;
            }

            var textObject = new GameObject("SDF Text", typeof(RectTransform), typeof(SdfText));
            StageUtility.PlaceGameObjectInCurrentStage(textObject);
            Undo.RegisterCreatedObjectUndo(textObject, "Create SDF Text");
            GameObjectUtility.SetParentAndAlign(textObject, parent ? parent : canvas.gameObject);
            var text = textObject.GetComponent<SdfText>();
            text.rectTransform.sizeDelta = new Vector2(300, 100);
            text.text = "SDF Text";
            text.fontSize = TMP_Settings.defaultFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.EffectsEnabled = true;
            Selection.activeGameObject = textObject;
        }
    }
}
