using System.Collections.Generic;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;

namespace FuckTheseFX.Source.GlassCulling
{
    // One availability aggregate shared by every glass LOD using this parent section.
    internal sealed class GlassCullingParentScope
    {
        internal readonly Transform Root;
        internal readonly PerfectCullingCrossSceneGroup Controller;
        internal readonly List<Renderer> Anchors = new List<Renderer>();
        internal readonly List<Renderer> Guards = new List<Renderer>();
        internal readonly HashSet<PerfectCullingBakeGroup> Groups = new HashSet<PerfectCullingBakeGroup>();
        internal readonly List<GlassCullingFamily> Families = new List<GlassCullingFamily>();
        internal Bounds Bounds;
        internal bool HasBounds, Bound;
        private bool retired;
        private int available;

        internal GlassCullingParentScope(Transform root, PerfectCullingCrossSceneGroup controller)
        { Root = root; Controller = controller; }

        internal bool Hidden => !retired && available == 0 && Root && Controller &&
            Root.gameObject.activeInHierarchy && Root.IsChildOf(Controller.transform);

        internal bool Contains(Renderer glass)
        {
            if (!glass || !HasBounds) return false;
            var bounds = glass.bounds;
            var box = Bounds; box.Expand(.2f);
            return box.Contains(bounds.min) && box.Contains(bounds.max);
        }

        internal void Add(Renderer renderer, HashSet<PerfectCullingBakeGroup> owners)
        {
            if (owners == null || owners.Count == 0) { Guards.Add(renderer); return; }
            Anchors.Add(renderer);
            Groups.UnionWith(owners);
            if (!renderer.gameObject.activeInHierarchy) return;
            if (!HasBounds) { Bounds = renderer.bounds; HasBounds = true; }
            else Bounds.Encapsulate(renderer.bounds);
        }

        internal void AvailabilityChanged(int delta)
        {
            bool wasHidden = available == 0;
            available += delta;
            if (wasHidden != (available == 0))
            {
                foreach (var family in Families) family.Apply(false);
            }
        }

        internal void Retire()
        {
            if (retired) return;
            retired = true;
            foreach (var family in Families) family.Retire();
        }
    }
}
