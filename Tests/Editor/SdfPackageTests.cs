using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SDFUI.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SDFUI.Tests
{
    public sealed class SdfPackageTests
    {
        [Serializable]
        private sealed class PackageManifest
        {
            public string name;
            public string displayName;
            public string unity;
        }

        [Test]
        public void InstalledPackage_ResolvesItsPublicAssembliesShaderAndComponentIcon()
        {
            GameObject instance = null;
            try
            {
                instance = new GameObject("SDF package identity test", typeof(RectTransform), typeof(SdfImage));
                instance.hideFlags = HideFlags.HideAndDontSave;
                var image = instance.GetComponent<SdfImage>();
                MonoScript script = MonoScript.FromMonoBehaviour(image);
                string root = LibraryRoot(script);
                var manifest = JsonUtility.FromJson<PackageManifest>(
                    File.ReadAllText(FileUtil.GetPhysicalPath(root + "/package.json")));
                Assert.That(manifest.name, Is.EqualTo("com.sdfimage.ugui"));
                Assert.That(manifest.displayName, Is.EqualTo("SDF Outline"));
                Assert.That(manifest.unity, Is.EqualTo("6000.0"));
                Assert.That(typeof(SdfImage).Assembly.GetName().Name, Is.EqualTo("SDFUI"));
                Assert.That(typeof(SdfTextureSettings).Assembly.GetName().Name, Is.EqualTo("SDFUI.Editor"));

                Shader shader = Resources.Load<Shader>("SDFImage");
                Assert.That(shader, Is.Not.Null, "The renamed resource must resolve in Assets and UPM installations.");
                Assert.That(shader.name, Is.EqualTo("UI/SDF Image"));
                Assert.That(AssetDatabase.GetAssetPath(shader), Is.EqualTo(root + "/Runtime/Resources/SDFImage.shader"));

                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Editor/Icons/SdfImage.png");
                Assert.That(icon, Is.Not.Null);
                Assert.That(new Vector2Int(icon.width, icon.height), Is.EqualTo(new Vector2Int(64, 64)));
                var scriptImporter = (MonoImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(script));
                Assert.That(scriptImporter.GetIcon(), Is.EqualTo(icon), "The script .meta must retain its custom icon GUID.");
                Assert.That(EditorGUIUtility.ObjectContent(image, typeof(SdfImage)).image, Is.EqualTo(icon));
                Assert.That(ObjectNames.GetInspectorTitle(image), Does.Contain("SDF Image"));
            }
            finally
            {
                if (instance) Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ShippedDemoPrefab_ResolvesAllScriptsToTheInstalledLibraryAfterFolderAndAssemblyRename()
        {
            var descriptor = ScriptableObject.CreateInstance<SdfSprite>();
            try
            {
                string root = LibraryRoot(MonoScript.FromScriptableObject(descriptor));
                string prefab = File.ReadAllText(FileUtil.GetPhysicalPath(root + "/Samples~/Demo/SDF UI Demo.prefab"));
                var scripts = Regex.Matches(prefab, @"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32}), type: 3\}");
                Assert.That(scripts.Count, Is.GreaterThan(0), "The shipped sample must contain serialized components.");
                int sdfImages = 0;
                foreach (Match match in scripts)
                {
                    string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    Assert.That(script, Is.Not.Null, "The sample has an unresolved script GUID: " + match.Groups[1].Value);
                    Type componentType = script.GetClass();
                    Assert.That(componentType, Is.Not.Null, "The sample script no longer resolves to a class: " + path);
                    Assert.That(componentType, Is.Not.EqualTo(typeof(SdfAutoBake)), "New samples use one Image component.");
                    if (componentType != typeof(SdfImage)) continue;
                    sdfImages++;
                    Assert.That(path, Is.EqualTo(root + "/Runtime/SdfImage.cs"));
                }
                Assert.That(sdfImages, Is.EqualTo(6), "All six example graphics must resolve to the renamed SdfImage script.");
            }
            finally
            {
                Object.DestroyImmediate(descriptor);
            }
        }

        private static string LibraryRoot(MonoScript script)
        {
            Assert.That(script, Is.Not.Null);
            string path = AssetDatabase.GetAssetPath(script);
            return Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
        }
    }
}
