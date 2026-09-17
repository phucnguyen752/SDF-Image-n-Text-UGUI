using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFUI
{
    // Compatible images share an immutable render state. A sole owner can update
    // its entry in place; shared owners switch entries before changing the style.
    internal static class SdfImageMaterials
    {
        internal const int PropertyCount = 10;
        private static readonly int[] PropertyIds =
        {
            Shader.PropertyToID("_SdfDecode"), Shader.PropertyToID("_SourceSize"),
            Shader.PropertyToID("_ImageRect"), Shader.PropertyToID("_SourceBorder"),
            Shader.PropertyToID("_LocalBorder"), Shader.PropertyToID("_Outline"),
            Shader.PropertyToID("_OutlineColor"), Shader.PropertyToID("_OutlineTextureColor"),
            Shader.PropertyToID("_Shadow"), Shader.PropertyToID("_ShadowColor")
        };
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int SdfTex = Shader.PropertyToID("_SdfTex");
        private static readonly int HasSprite = Shader.PropertyToID("_HasSprite");
        private static readonly int LayerCount = Shader.PropertyToID("_LayerCount");
        private static readonly int LayerSizes = Shader.PropertyToID("_LayerSizes");
        private static readonly int LayerColors = Shader.PropertyToID("_LayerColors");
        private static readonly int LayerModes = Shader.PropertyToID("_LayerModes");
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private static int entryCount;
        // At most one spare per live render state avoids allocation when several
        // shared groups animate. Spares hold no textures and shrink with live usage.
        private static readonly Stack<Entry> spares = new Stack<Entry>(4);

        internal sealed class Entry
        {
            internal Material material;
            internal int users, hash;
            internal Entry next;
            private Texture color, distance;
            private int count;
            private readonly Vector4[] properties = new Vector4[PropertyCount];
            private readonly Vector4[] sizes = new Vector4[SdfImage.MaxEffectLayers];
            private readonly Vector4[] colors = new Vector4[SdfImage.MaxEffectLayers];
            private readonly Vector4[] modes = new Vector4[SdfImage.MaxEffectLayers];
            private readonly List<Material> stencilMaterials = new List<Material>(2);

            internal bool Matches(Texture colorTexture, Texture distanceTexture, Vector4[] values,
                int layerCount, Vector4[] layerSizes, Vector4[] layerColors, Vector4[] layerModes) =>
                material && color == colorTexture && distance == distanceTexture && count == layerCount
                && Equal(properties, values, PropertyCount) && Equal(sizes, layerSizes, count)
                && Equal(colors, layerColors, count) && Equal(modes, layerModes, count);

            internal void Set(Texture colorTexture, Texture distanceTexture, Vector4[] values,
                int layerCount, Vector4[] layerSizes, Vector4[] layerColors, Vector4[] layerModes)
            {
                color = colorTexture;
                distance = distanceTexture;
                count = layerCount;
                Array.Copy(values, properties, PropertyCount);
                Array.Copy(layerSizes, sizes, count);
                Array.Copy(layerColors, colors, count);
                Array.Copy(layerModes, modes, count);
                Apply(material);
                foreach (var stencil in stencilMaterials) Apply(stencil);
            }

            private void Apply(Material target)
            {
                if (!target || !target.HasProperty(SdfTex)) return;
                target.SetFloat(HasSprite, 1);
                target.SetTexture(MainTex, color);
                target.SetTexture(SdfTex, distance);
                for (int i = 0; i < PropertyCount; i++)
                    if (i == 6 || i == 9) target.SetColor(PropertyIds[i], properties[i]);
                    else target.SetVector(PropertyIds[i], properties[i]);
                target.SetInt(LayerCount, count);
                target.SetVectorArray(LayerSizes, sizes);
                target.SetVectorArray(LayerColors, colors);
                target.SetVectorArray(LayerModes, modes);
            }

            internal void ApplyStencil(Material target)
            {
                if (!target || target == material || !target.HasProperty(SdfTex)) return;
                foreach (var stencil in stencilMaterials) if (stencil == target) return;
                Apply(target);
                // Retain native uGUI derivatives across pooled base-material reuse.
                // Bound unusual stencil configurations, and leave other modifiers owned by their caller.
                if (stencilMaterials.Count >= 4) return;
                var retained = UnityEngine.UI.StencilMaterial.Add(material, target.GetInt("_Stencil"),
                    (UnityEngine.Rendering.StencilOp)target.GetInt("_StencilOp"),
                    (UnityEngine.Rendering.CompareFunction)target.GetInt("_StencilComp"),
                    (UnityEngine.Rendering.ColorWriteMask)target.GetInt("_ColorMask"),
                    target.GetInt("_StencilReadMask"), target.GetInt("_StencilWriteMask"));
                if (retained == target) stencilMaterials.Add(retained);
                else UnityEngine.UI.StencilMaterial.Remove(retained);
            }

            internal void ReleaseStencils()
            {
                foreach (var stencil in stencilMaterials) UnityEngine.UI.StencilMaterial.Remove(stencil);
                stencilMaterials.Clear();
            }

            internal void ClearTextures()
            {
                color = distance = null;
                material.SetTexture(MainTex, null);
                material.SetTexture(SdfTex, null);
                foreach (var stencil in stencilMaterials)
                    if (stencil) { stencil.SetTexture(MainTex, null); stencil.SetTexture(SdfTex, null); }
            }
        }

        internal static Material Acquire(Shader shader, Texture color, Texture distance, Vector4[] properties,
            int count, Vector4[] sizes, Vector4[] colors, Vector4[] modes, ref Entry current)
        {
            if (current != null && current.Matches(color, distance, properties, count, sizes, colors, modes))
                return current.material;
            int hash;
            unchecked
            {
                hash = color.GetInstanceID() * 31 + distance.GetInstanceID();
                hash = hash * 31 + count;
                for (int i = 0; i < PropertyCount; i++) hash = hash * 31 + properties[i].GetHashCode();
                for (int i = 0; i < count; i++)
                {
                    hash = hash * 31 + sizes[i].GetHashCode();
                    hash = hash * 31 + colors[i].GetHashCode();
                    hash = hash * 31 + modes[i].GetHashCode();
                }
            }
            entries.TryGetValue(hash, out var candidate);
            for (; candidate != null; candidate = candidate.next)
                if (candidate.Matches(color, distance, properties, count, sizes, colors, modes))
                {
                    candidate.users++;
                    Release(ref current, true);
                    current = candidate;
                    return candidate.material;
                }

            if (current != null && current.users == 1 && current.material)
                Remove(current);
            else
            {
                var replacement = spares.Count > 0 ? spares.Pop() : new Entry();
                if (!replacement.material)
                    replacement.material = new Material(shader)
                    {
                        name = "SDF Image (Shared)",
                        hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector
                    };
                Release(ref current, true);
                current = replacement;
                current.users = 1;
            }
            current.hash = hash;
            current.Set(color, distance, properties, count, sizes, colors, modes);
            entries.TryGetValue(hash, out current.next);
            entries[hash] = current;
            entryCount++;
            return current.material;
        }

        internal static void Release(ref Entry current, bool retain = false)
        {
            var previous = current;
            current = null;
            if (previous == null || --previous.users > 0) return;
            Remove(previous);
            if (retain && previous.material && spares.Count < entryCount)
            {
                previous.ClearTextures();
                spares.Push(previous);
            }
            else Destroy(previous);
            while (spares.Count > entryCount) Destroy(spares.Pop());
        }

        private static void Remove(Entry entry)
        {
            if (entries.TryGetValue(entry.hash, out var head))
            {
                if (ReferenceEquals(head, entry))
                {
                    if (entry.next == null) entries.Remove(entry.hash);
                    else entries[entry.hash] = entry.next;
                    entryCount--;
                }
                else
                    for (var previous = head; previous != null; previous = previous.next)
                        if (ReferenceEquals(previous.next, entry)) { previous.next = entry.next; entryCount--; break; }
            }
            entry.next = null;
        }

        private static bool Equal(Vector4[] left, Vector4[] right, int count)
        {
            for (int i = 0; i < count; i++) if (!left[i].Equals(right[i])) return false;
            return true;
        }

        private static void Destroy(Entry entry)
        {
            entry.ReleaseStencils();
            if (entry.material)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(entry.material);
                else UnityEngine.Object.DestroyImmediate(entry.material);
            }
            entry.material = null;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void BeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Clear;

        private static void Clear()
        {
            foreach (var head in entries.Values)
                for (var entry = head; entry != null; entry = entry.next) Destroy(entry);
            entries.Clear();
            entryCount = 0;
            while (spares.Count > 0) Destroy(spares.Pop());
        }
#endif
        // With domain reload disabled, live images retain their reference-counted
        // entries. OnDisable/OnDestroy release them; no per-frame watcher is needed.
    }
}
