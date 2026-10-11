using System.Collections.Generic;
using Koenigz.PerfectCulling;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.GlassCulling
{
    // An association, not another visibility test. Only native renderer flags are consumed.
    internal sealed class GlassCullingFamily
    {
        internal readonly LODGroup Lod;
        internal readonly Transform Scope;
        internal readonly string Kind;
        internal readonly Renderer[] Peers, Glass;
        internal readonly PerfectCullingBakeGroup[] Owners;
        internal readonly GlassCullingParentScope Parent;
        private readonly bool[] forced;
        private bool retired;
        private bool hasVisibility, lastHidden;
        private int forcedCount;

        internal GlassCullingFamily(LODGroup lod, Renderer[] peers, Renderer[] glass, PerfectCullingBakeGroup[] owners,
            Transform scope = null, string kind = "lod", GlassCullingParentScope parent = null)
        {
            Lod = lod; Peers = peers; Glass = glass; Owners = owners;
            Scope = scope ? scope : lod.transform; Kind = kind;
            Parent = parent;
            forced = new bool[glass.Length];
        }

        private static bool IsGlassMaterial(Material material)
        {
            if (!material || !material.shader) return false;
            var name = material.shader.name;
            // FogSix preserves the native suffix and validates this property before accepting its port.
            // Both shaders describe the same pane population across in-place swaps and restoration.
            return name == "Global Fog/Transparent Reflective Specular" ||
                (name == "FogSix/Glass/Transparent Reflective Specular" && material.HasProperty("_FogSixGlass"));
        }

        internal static bool IsGlass(Renderer renderer)
        {
            if (!(renderer is MeshRenderer) || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) return false;
            foreach (var material in materials)
                if (!IsGlassMaterial(material)) return false;
            return true;
        }

        internal static bool IsOpaque(Renderer renderer)
        {
            if (!renderer || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) return false;
            foreach (var material in materials)
                if (!material || !material.shader || material.renderQueue > 2500 ||
                    IsGlassMaterial(material)) return false;
            return true;
        }

        // A native-controlled mixed-material body is still a useful visibility reference.
        // This predicate is never used to select the glass being suppressed.
        internal static bool HasOpaqueMaterial(Renderer renderer)
        {
            if (!renderer || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            foreach (var material in renderer.sharedMaterials)
                if (material && material.shader && material.renderQueue <= 2500 &&
                    !IsGlassMaterial(material)) return true;
            return false;
        }

        internal static bool Encloses(Renderer[] peers, Renderer glass)
        {
            if (!glass || !TryBounds(peers, null, out var bounds)) return false;
            var glassBounds = glass.bounds;
            return bounds.Contains(glassBounds.min) && bounds.Contains(glassBounds.max);
        }

        private static bool TryBounds(Renderer[] peers, Transform scope, out Bounds bounds)
        {
            bounds = new Bounds();
            if (peers.Length == 0) return false;
            bool first = true;
            foreach (var peer in peers)
            {
                if (!peer || (scope && !peer.transform.IsChildOf(scope))) return false;
                if (first) { bounds = peer.bounds; first = false; }
                else bounds.Encapsulate(peer.bounds);
            }
            // Ten centimetres per side permits coplanar windows and floating point error.
            bounds.Expand(.2f);
            return true;
        }

        internal int Apply(bool validate = true)
        {
            if (retired) return 0;
            if (!Lod || !Scope) { Retire(); return 0; }
            if (!Lod.enabled || !Lod.gameObject.activeInHierarchy || !Scope.gameObject.activeInHierarchy) { Restore(); return 0; }
            bool hidden = Parent == null || Parent.Hidden;
            if (Parent == null) foreach (var peer in Peers)
            {
                // Destruction is not culling evidence. Release if the association loses a peer.
                if (!peer || (validate && !peer.transform.IsChildOf(Scope))) { Retire(); return 0; }
                if (peer.gameObject.activeInHierarchy && peer.enabled && !peer.forceRenderingOff) hidden = false;
            }
            // Native setters may repeat an unchanged state. Only a transition needs pane work;
            // the infrequent maintenance path explicitly validates external/structural changes.
            if (!validate && hasVisibility && hidden == lastHidden) return forcedCount;
            hasVisibility = true; lastHidden = hidden;
            var peerBounds = new Bounds();
            if (hidden && Parent == null && !TryBounds(Peers, Scope, out peerBounds)) { Retire(); return 0; }
            int count = 0;
            for (int i = 0; i < Glass.Length; i++)
            {
                var glass = Glass[i];
                if (!glass) continue;
                bool suppress = hidden && glass.transform.IsChildOf(Scope) && glass.gameObject.activeInHierarchy;
                if (suppress)
                {
                    if (Parent != null) suppress = Parent.Contains(glass);
                    else
                    {
                        var glassBounds = glass.bounds;
                        suppress = peerBounds.Contains(glassBounds.min) && peerBounds.Contains(glassBounds.max);
                    }
                }
                if (suppress)
                {
                    // Do not claim a flag already held by the game or another feature.
                    if (!glass.forceRenderingOff) { glass.forceRenderingOff = true; forced[i] = true; }
                    if (forced[i]) count++;
                }
                else Release(i);
            }
            forcedCount = count;
            return count;
        }

        private void Release(int index)
        {
            if (forced[index] && Glass[index]) Glass[index].forceRenderingOff = false;
            forced[index] = false;
        }

        internal void Restore() { for (int i = 0; i < Glass.Length; i++) Release(i); forcedCount = 0; hasVisibility = false; }
        internal void Retire() { Restore(); retired = true; }

        internal static bool SameOwners(HashSet<PerfectCullingBakeGroup> a, HashSet<PerfectCullingBakeGroup> b) =>
            a != null && a.Count > 0 && b != null && a.SetEquals(b);
    }
}
