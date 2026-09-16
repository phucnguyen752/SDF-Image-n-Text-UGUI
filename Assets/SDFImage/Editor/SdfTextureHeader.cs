using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SDFUI.Editor
{
    [InitializeOnLoad]
    internal static class SdfTextureHeader
    {
        private static UnityEditor.Editor sdfEditor;
        private static readonly Type TextureInspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TextureImporterInspector");
        private static readonly FieldInfo SpriteSections = TextureInspectorType?.GetField("m_GUIElementMethods", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string FoldoutKey = "SDFImage.SourceInspectorExpanded";
        static SdfTextureHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += Draw;
            Selection.selectionChanged += ClearEditor;
            AssemblyReloadEvents.beforeAssemblyReload += ClearEditor;
        }

        private static void ClearEditor()
        {
            if (sdfEditor) UnityEngine.Object.DestroyImmediate(sdfEditor);
            sdfEditor = null;
        }

        private static void Draw(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1 || !(editor.target is TextureImporter || editor.target is Texture2D || editor.target is Sprite)) return;
            string path = AssetDatabase.GetAssetPath(editor.target);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer || importer.textureType != TextureImporterType.Sprite) return;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is SdfSprite data)
                {
                    if (editor.target != data.DistanceTexture) continue;
                    // Texture2D subassets remain below the original texture. A visible ScriptableObject
                    // would replace the native importer's main asset, breaking normal Project drag/drop.
                    UnityEditor.Editor.CreateCachedEditor(data, typeof(SdfSpriteEditor), ref sdfEditor);
                    sdfEditor.OnInspectorGUI();
                    string state = SdfBakeQueue.GetStatus(path);
                    if (state == "Queued" || state.StartsWith("Baking ", StringComparison.Ordinal)) editor.Repaint();
                    return;
                }
            // Install during the header callback, but draw alongside the native Sprite and
            // Advanced sections. The original importer still owns all texture controls.
            if (TryInstallSourceSection(editor)) return;
            // Sprite subasset inspectors (and future Editors with a different internal API)
            // still provide the same entry point as a compact foldout.
            DrawSourceSection(editor);
        }

        internal static bool TryInstallSourceSection(UnityEditor.Editor editor)
        {
            if (TextureInspectorType == null || !TextureInspectorType.IsInstanceOfType(editor)
                || !(SpriteSections?.GetValue(editor) is IDictionary sections)) return false;
            foreach (DictionaryEntry section in sections)
            {
                if (section.Key.ToString() != "Sprite" || !(section.Value is Delegate original)) continue;
                if (original.Target is ISourceSection) return true;
                var hookType = typeof(SourceSection<>).MakeGenericType(section.Key.GetType());
                var hook = Activator.CreateInstance(hookType, original, editor);
                sections[section.Key] = Delegate.CreateDelegate(original.GetType(), hook, hookType.GetMethod("Draw"));
                return true;
            }
            return false;
        }

        private interface ISourceSection { }

        private sealed class SourceSection<T> : ISourceSection
        {
            private readonly Action<T> drawSprite;
            private readonly UnityEditor.Editor editor;

            public SourceSection(Delegate original, UnityEditor.Editor editor)
            {
                this.editor = editor;
                foreach (Delegate draw in original.GetInvocationList())
                    drawSprite += (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), draw.Target, draw.Method);
            }

            public void Draw(T elements)
            {
                drawSprite(elements);
                DrawSourceSection(editor);
            }
        }

        private static void DrawSourceSection(UnityEditor.Editor editor)
        {
            if (!editor || editor.targets.Length != 1) return;
            string path = AssetDatabase.GetAssetPath(editor.target);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return;
            EditorGUILayout.Space();
            bool expanded = SessionState.GetBool(FoldoutKey, true);
            bool next = EditorGUILayout.Foldout(expanded, "SDF", true);
            if (next != expanded) SessionState.SetBool(FoldoutKey, next);
            if (!next) return;
            using (new EditorGUI.IndentLevelScope())
            {
                SdfSprite first = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is SdfSprite data && data.IsValid) { first = data; break; }
                DrawActions(editor, path, first);
            }
        }

        private static void DrawActions(UnityEditor.Editor editor, string path, SdfSprite first)
        {
            string status = SdfBakeQueue.GetStatus(path);
            bool ready = first && first.IsValid;
            bool busy = status == "Queued" || status.StartsWith("Baking ", StringComparison.Ordinal);
            if (status.StartsWith("Error:", StringComparison.Ordinal)) EditorGUILayout.HelpBox(status, MessageType.Error);
            else if (busy || ready) EditorGUILayout.LabelField(busy ? status : "Ready", EditorStyles.miniLabel);
            Rect action = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            using (new EditorGUI.DisabledScope(busy && !ready))
                if (GUI.Button(action, ready ? "Open SDF Import Settings" : "Generate"))
                {
                    if (ready) Selection.activeObject = first.DistanceTexture;
                    else
                    {
                        var settings = SdfTextureSettings.Get(path);
                        settings.enabled = true;
                        SdfTextureSettings.Set(path, settings);
                    }
                    GUIUtility.ExitGUI();
                }
            if (busy) editor.Repaint();
        }
    }
}
