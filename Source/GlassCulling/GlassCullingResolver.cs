using System.Collections.Generic;
using Koenigz.PerfectCulling;
using UnityEngine;

namespace FuckTheseFX.Source.GlassCulling
{
    internal static class GlassCullingResolver
    {
        internal sealed class Assembly
        {
            internal Transform Root;
            internal Renderer[] Peers = new Renderer[0];
            internal bool TooLarge;
        }

        // Walk only compact nearby assemblies. Stop instead of searching an entire PROPS folder.
        // Yield per node, including nodes with no renderers, to retain the discovery work budget.
        internal static IEnumerable<bool> InspectAssembly(Assembly assembly)
        {
            var stack = new Stack<Transform>();
            var peers = new List<Renderer>();
            stack.Push(assembly.Root);
            int nodes = 0;
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!node) continue;
                if (++nodes > 128 || node.childCount > 128) { assembly.TooLarge = true; yield break; }
                foreach (var renderer in node.GetComponents<Renderer>())
                    if (GlassCullingFamily.HasOpaqueMaterial(renderer))
                    {
                        peers.Add(renderer);
                        if (peers.Count > 16) { assembly.TooLarge = true; yield break; }
                    }
                for (int i = 0; i < node.childCount; i++) stack.Push(node.GetChild(i));
                yield return true;
            }
            assembly.Peers = peers.ToArray();
        }

        internal static GlassCullingFamily SelectAdditional(LODGroup lod, Renderer glass,
            Renderer[] members, Assembly assembly, Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners,
            HashSet<Renderer> registered, HashSet<GameObject> controlledObjects, out string reason)
        {
            reason = "no-part-reference";
            if (!lod || !lod.enabled) return null;
            if (!Eligible(glass, registered, controlledObjects)) { reason = "already-controlled-or-ineligible"; return null; }
            if (!glass.transform.IsChildOf(lod.transform)) return null;
            var anchors = new List<Renderer>();
            bool listed = false;
            foreach (var renderer in members)
            {
                listed |= renderer == glass;
                if (GlassCullingFamily.HasOpaqueMaterial(renderer)) anchors.Add(renderer);
            }
            if (!listed) { reason = "glass-not-in-LOD"; return null; }

            if (anchors.Count > 0)
            {
                for (var node = glass.transform; node; node = node.parent)
                {
                    var peers = new List<Renderer>();
                    foreach (var anchor in anchors)
                        if (anchor.transform == node || anchor.transform.parent == node) peers.Add(anchor);
                    // Do not escape an ambiguous local part by trying a broader parent.
                    if (peers.Count > 0)
                        return Match(lod, glass, node, peers.ToArray(), "part", owners, out reason);
                    if (node == lod.transform) break;
                }
                return null;
            }
            if (assembly != null && assembly.TooLarge) { reason = "enclosing-hierarchy-search-limit"; return null; }
            if (assembly == null || !assembly.Root || assembly.Peers.Length == 0) return null;
            if (!lod.transform.IsChildOf(assembly.Root)) return null;
            return Match(lod, glass, assembly.Root, assembly.Peers, "assembly", owners, out reason);
        }

        private static GlassCullingFamily Match(LODGroup lod, Renderer glass, Transform scope, Renderer[] peers,
            string kind, Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners, out string reason)
        {
            reason = "reference-without-runtime-owner";
            HashSet<PerfectCullingBakeGroup> common = null;
            bool mixed = false;
            foreach (var peer in peers)
            {
                if (!peer || !owners.TryGetValue(peer, out var set) || set.Count == 0) return null;
                if (common == null) common = set;
                else if (!GlassCullingFamily.SameOwners(common, set)) { reason = "conflicting-part-owners"; return null; }
                mixed |= !GlassCullingFamily.IsOpaque(peer);
            }
            if (!GlassCullingFamily.Encloses(peers, glass)) { reason = "outside-reference-bounds"; return null; }
            reason = "accepted";
            return new GlassCullingFamily(lod, peers, new[] { glass }, new List<PerfectCullingBakeGroup>(common).ToArray(),
                scope, mixed ? kind + "-mixed" : kind);
        }

        internal static bool Eligible(Renderer glass, HashSet<Renderer> registered, HashSet<GameObject> controlledObjects)
        {
            if (!GlassCullingFamily.IsGlass(glass) || registered.Contains(glass)) return false;
            for (var parent = glass.transform; parent; parent = parent.parent)
                if (controlledObjects.Contains(parent.gameObject)) return false;
            return true;
        }
    }
}
