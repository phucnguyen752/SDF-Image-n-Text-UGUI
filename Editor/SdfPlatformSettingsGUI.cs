using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SDFUI.Editor
{
    // Draw Unity's actual Sprite platform controls against detached settings. Never
    // call the source TextureImporterInspector's GUI, Apply, or serialized setters.
    internal sealed class SdfPlatformSettingsGUI : IDisposable
    {
        private readonly MethodInfo begin, draw;
        private readonly object[] arguments;
        private readonly PropertyInfo modelProperty, settingsProperty, targetProperty;
        private readonly MethodInfo setChanged;
        private readonly IList platforms;
        private UnityEditor.Editor sourceEditor;
        internal bool UsesNativeTabs => begin != null;
        internal bool UsesNativeSettings => platforms != null && draw != null;

        public SdfPlatformSettingsGUI() : this(null) { }

        public SdfPlatformSettingsGUI(TextureImporter source)
        {
            var assembly = typeof(EditorGUILayout).Assembly;
            var platformType = assembly.GetType("UnityEditor.Build.BuildPlatform");
            var baseType = assembly.GetType("UnityEditor.BaseTextureImportPlatformSettings");
            var concreteType = assembly.GetType("UnityEditor.TextureImportPlatformSettings");
            if (platformType == null || baseType == null || concreteType == null) return;
            begin = typeof(EditorGUILayout).GetMethod("BeginPlatformGrouping", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { platformType.MakeArrayType(), typeof(GUIContent) }, null);
            var valid = baseType.GetMethod("GetBuildPlayerValidPlatforms", BindingFlags.Public | BindingFlags.Static);
            if (begin == null || valid == null) return;
            arguments = new[] { valid.Invoke(null, null), new GUIContent("Default") };
            if (!source) return;

            var listType = typeof(List<>).MakeGenericType(baseType);
            draw = baseType.GetMethod("ShowPlatformSpecificSettings", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { listType, typeof(int) }, null);
            modelProperty = baseType.GetProperty("model");
            var dataType = modelProperty?.PropertyType;
            settingsProperty = dataType?.GetProperty("platformTextureSettings");
            targetProperty = dataType?.GetProperty("buildTarget");
            setChanged = dataType?.GetMethod("SetChanged");
            var reset = concreteType.GetMethod("ResetSerializedProperties");
            var editorType = assembly.GetType("UnityEditor.TextureImporterInspector");
            var platformField = editorType?.GetField("m_PlatformSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (draw == null || settingsProperty == null || targetProperty == null || setChanged == null || reset == null || platformField == null) return;
            sourceEditor = UnityEditor.Editor.CreateEditor(source, editorType);
            platforms = (IList)Activator.CreateInstance(listType);
            foreach (var platform in (IEnumerable)platformField.GetValue(sourceEditor))
            {
                reset.Invoke(platform, null);
                platforms.Add(platform);
            }
        }

        public void Draw(SdfTextureSettings settings)
        {
            if (!UsesNativeSettings)
            {
                EditorGUILayout.HelpBox("This Unity version does not expose the Sprite compression Inspector. SDF import settings were preserved.", MessageType.Error);
                return;
            }
            var defaults = settings.GetTexturePlatform("DefaultTexturePlatform", BuildTarget.NoTarget);
            foreach (var platform in platforms)
            {
                object model = modelProperty.GetValue(platform);
                var previous = (TextureImporterPlatformSettings)settingsProperty.GetValue(model);
                var next = settings.GetTexturePlatform(previous.name, (BuildTarget)targetProperty.GetValue(model));
                if (next.name != "DefaultTexturePlatform" && !next.overridden)
                {
                    string name = next.name;
                    next = JsonUtility.FromJson<TextureImporterPlatformSettings>(JsonUtility.ToJson(defaults));
                    next.name = name;
                    next.overridden = false;
                }
                settingsProperty.SetValue(model, next);
            }
            EditorGUI.BeginChangeCheck();
            bool ended = false;
            try
            {
                int selected = (int)begin.Invoke(null, arguments);
                // Unity closes its platform group inside this method.
                draw.Invoke(null, new object[] { platforms, selected });
                bool changed = EditorGUI.EndChangeCheck();
                ended = true;
                if (changed)
                {
                    foreach (var platform in platforms)
                    {
                        var value = (TextureImporterPlatformSettings)settingsProperty.GetValue(modelProperty.GetValue(platform));
                        // Retain overrides for build modules not installed on this machine.
                        int index = settings.texturePlatforms.FindIndex(item => item.name == value.name);
                        if (index >= 0) settings.texturePlatforms[index] = value;
                        else settings.texturePlatforms.Add(value);
                    }
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is ExitGUIException)
            {
                throw exception.InnerException;
            }
            finally
            {
                if (!ended) EditorGUI.EndChangeCheck();
                ClearChanged();
            }
        }

        private void ClearChanged()
        {
            if (platforms == null) return;
            foreach (var platform in platforms) setChanged.Invoke(modelProperty.GetValue(platform), new object[] { false });
        }

        public void Dispose()
        {
            ClearChanged();
            if (sourceEditor) UnityEngine.Object.DestroyImmediate(sourceEditor);
            sourceEditor = null;
        }
    }
}
