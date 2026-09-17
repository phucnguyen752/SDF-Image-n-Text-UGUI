using UnityEngine;
using UnityEngine.UI;

namespace SDFUI
{
    /// <summary>Applies the same face-only material policy to TMP fallback/material submeshes.</summary>
    [ExecuteAlways, AddComponentMenu("")]
    public sealed class SdfTextFaceMaterial : MonoBehaviour, IMaterialModifier
    {
        public SdfText Owner { get; set; }
        private Material source, stencil, instance;
        private SdfTextMaterials.Entry materialEntry;

        public Material GetModifiedMaterial(Material baseMaterial)
        {
            if (!Owner || !Owner.isActiveAndEnabled || !Owner.EffectsSupported || !SdfText.IsDistanceField(baseMaterial))
                return baseMaterial;
            var subMesh = GetComponent<TMPro.TMP_SubMeshUI>();
            source = subMesh ? subMesh.sharedMaterial : baseMaterial;
            stencil = baseMaterial;
            return SdfText.FaceOnly(source, ref instance, ref materialEntry, stencil);
        }

        private void OnDisable()
        {
            SdfTextMaterials.Release(ref materialEntry);
            source = stencil = instance = null;
        }

        private void OnDestroy() => SdfTextMaterials.Release(ref materialEntry);
    }
}
