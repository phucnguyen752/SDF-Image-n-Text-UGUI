using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFUI
{
    // uGUI batches by material identity. Effect style lives in vertices, so all layers
    // using a source preset can share a material, regardless of their color or width.
    internal static class SdfTextMaterials
    {
        internal sealed class Entry
        {
            internal Key key;
            internal Material material;
            internal int users, revision;
            private int sourceCrc, stencilCrc;

            internal void Refresh()
            {
                if (!key.source || !material) return;
                int source = key.source.ComputeCRC();
                int stencil = key.stencil ? key.stencil.ComputeCRC() : 0;
                if (revision != 0 && source == sourceCrc && stencil == stencilCrc) return;
                sourceCrc = source;
                stencilCrc = stencil;
                if (!key.shader)
                {
                    material.shader = key.source.shader;
                    SdfText.CopyRenderProperties(key.source, material, key.stencil);
                    material.SetFloat("_OutlineWidth", 0);
                    material.SetFloat("_OutlineSoftness", 0);
                    material.DisableKeyword("OUTLINE_ON");
                    material.DisableKeyword("UNDERLAY_ON");
                    material.DisableKeyword("UNDERLAY_INNER");
                    material.DisableKeyword("GLOW_ON");
                }
                else
                {
                    material.CopyPropertiesFromMaterial(key.source);
                    material.shaderKeywords = Array.Empty<string>();
                    material.SetInt("_StencilComp", 8);
                    material.SetInt("_Stencil", 0);
                    material.SetInt("_StencilOp", 0);
                    material.SetInt("_StencilReadMask", 255);
                    material.SetInt("_StencilWriteMask", 255);
                    material.SetInt("_ColorMask", 15);
                }
                revision++;
                Revision++;
            }
        }

        internal struct Key : IEquatable<Key>
        {
            internal Material source, stencil;
            internal Shader shader;
            public bool Equals(Key other) => source == other.source && stencil == other.stencil && shader == other.shader;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = source.GetInstanceID();
                    // Keep a dictionary key stable even after Unity destroys a source asset.
                    hash = hash * 31 + (ReferenceEquals(stencil, null) ? 0 : stencil.GetInstanceID());
                    return hash * 31 + (ReferenceEquals(shader, null) ? 0 : shader.GetInstanceID());
                }
            }
        }

        private static readonly Dictionary<Key, Entry> entries = new Dictionary<Key, Entry>();
        private static bool watching;
        internal static int Revision { get; private set; }

        internal static void WatchChanges()
        {
            if (watching) return;
            watching = true;
            Canvas.preWillRenderCanvases += RefreshSharedMaterials;
        }

        private static void RefreshSharedMaterials()
        {
            // Poll each shared render material once per Canvas cycle, rather than once
            // per label. Direct edits to source presets still reach the same render.
            foreach (var entry in entries.Values) entry.Refresh();
        }

        internal static Material Face(Material source, Material stencil, ref Entry entry) =>
            Acquire(new Key { source = source, stencil = stencil == source ? null : stencil }, ref entry);

        internal static Material Effect(Material source, Shader shader, ref Entry entry) =>
            Acquire(new Key { source = source, shader = shader }, ref entry);

        private static Material Acquire(Key key, ref Entry entry)
        {
            WatchChanges();
            if (entry == null || !entry.material || !entry.key.Equals(key))
            {
                Release(ref entry);
                if (!entries.TryGetValue(key, out entry))
                {
                    entry = new Entry
                    {
                        key = key,
                        material = key.shader ? new Material(key.shader) : new Material(key.source)
                    };
                    entry.material.name = key.shader ? "SDF Text Effect (Shared)" : "SDF Text Face (Shared)";
                    entry.material.hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
                    entries.Add(key, entry);
                }
                entry.users++;
            }
            entry.Refresh();
            return entry.material;
        }

        internal static void Release(ref Entry entry)
        {
            var previous = entry;
            entry = null;
            if (previous == null || --previous.users > 0) return;
            if (entries.TryGetValue(previous.key, out var current) && ReferenceEquals(previous, current))
                entries.Remove(previous.key);
            SdfText.Release(previous.material);
            previous.material = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Revision++;
            foreach (var entry in entries.Values)
            {
                SdfText.Release(entry.material);
                entry.material = null;
            }
            entries.Clear();
        }

#if UNITY_EDITOR
        private static void StopBeforeReload()
        {
            Canvas.preWillRenderCanvases -= RefreshSharedMaterials;
            watching = false;
            Reset();
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void BeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopBeforeReload;
#endif
    }
}
