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

        internal GlassCullingFamily(LODGroup lod, Renderer[] peers, Renderer[] glass, PerfectCullingBakeGroup[] owners,
            Transform scope = null, string kind = "lod", GlassCullingParentScope parent = null)
        {
            Lod = lod; Peers = peers; Glass = glass; Owners = owners;
            Scope = scope ? scope : lod.transform; Kind = kind;
            Parent = parent;
            forced = new bool[glass.Length];
        }

        internal static bool IsGlass(Renderer renderer)
        {
            if (!(renderer is MeshRenderer) || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) return false;
            foreach (var material in materials)
                if (!material || !material.shader || material.shader.name != "Global Fog/Transparent Reflective Specular") return false;
            return true;
        }

        internal static bool IsOpaque(Renderer renderer)
        {
            if (!renderer || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0) return false;
            foreach (var material in materials)
                if (!material || !material.shader || material.renderQueue > 2500 ||
                    material.shader.name == "Global Fog/Transparent Reflective Specular") return false;
            return true;
        }

        // A native-controlled mixed-material body is still a useful visibility reference.
        // This predicate is never used to select the glass being suppressed.
        internal static bool HasOpaqueMaterial(Renderer renderer)
        {
            if (!renderer || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            foreach (var material in renderer.sharedMaterials)
                if (material && material.shader && material.renderQueue <= 2500 &&
                    material.shader.name != "Global Fog/Transparent Reflective Specular") return true;
            return false;
        }

        internal static bool Encloses(Renderer[] peers, Renderer glass)
        {
            if (peers.Length == 0 || !glass) return false;
            var bounds = new Bounds();
            bool first = true;
            foreach (var peer in peers)
            {
                if (!peer) return false;
                if (first) { bounds = peer.bounds; first = false; }
                else bounds.Encapsulate(peer.bounds);
            }
            // Ten centimetres per side permits coplanar windows and floating point error.
            bounds.Expand(.2f);
            return bounds.Contains(glass.bounds.min) && bounds.Contains(glass.bounds.max);
        }

        internal int Apply()
        {
            if (retired) return 0;
            if (!Lod || !Scope) { Retire(); return 0; }
            if (!Lod.enabled || !Lod.gameObject.activeInHierarchy || !Scope.gameObject.activeInHierarchy) { Restore(); return 0; }
            bool hidden = Parent == null || Parent.Hidden;
            if (Parent == null) foreach (var peer in Peers)
            {
                // Destruction is not culling evidence. Release if the association loses a peer.
                if (!peer || !peer.transform.IsChildOf(Scope)) { Retire(); return 0; }
                if (peer.gameObject.activeInHierarchy && peer.enabled && !peer.forceRenderingOff) hidden = false;
            }
            int count = 0;
            for (int i = 0; i < Glass.Length; i++)
            {
                var glass = Glass[i];
                if (!glass) continue;
                bool suppress = hidden && glass.transform.IsChildOf(Scope) && glass.gameObject.activeInHierarchy &&
                    (Parent == null ? Encloses(Peers, glass) : Parent.Contains(glass));
                if (suppress)
                {
                    // Do not claim a flag already held by the game or another feature.
                    if (!glass.forceRenderingOff) { glass.forceRenderingOff = true; forced[i] = true; }
                    if (forced[i]) count++;
                }
                else Release(i);
            }
            return count;
        }

        private void Release(int index)
        {
            if (forced[index] && Glass[index]) Glass[index].forceRenderingOff = false;
            forced[index] = false;
        }

        internal void Restore() { for (int i = 0; i < Glass.Length; i++) Release(i); }
        internal void Retire() { Restore(); retired = true; }

        internal static bool SameOwners(HashSet<PerfectCullingBakeGroup> a, HashSet<PerfectCullingBakeGroup> b) =>
            a != null && a.Count > 0 && b != null && a.SetEquals(b);
    }
}
