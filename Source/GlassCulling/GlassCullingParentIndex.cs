using System.Collections.Generic;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.GlassCulling
{
    internal sealed class GlassCullingParentIndex
    {
        internal sealed class Candidate
        {
            internal LODGroup Lod;
            internal Renderer Glass;
            internal string Reason;
            internal GlassCullingParentScope Match;
            internal readonly List<Transform> Chain = new List<Transform>();
        }

        private sealed class GroupAvailability
        {
            internal int Available;
            internal readonly List<GlassCullingParentScope> Scopes = new List<GlassCullingParentScope>();
            internal void Change(int delta)
            {
                bool before = Available != 0;
                Available += delta;
                if (before != (Available != 0))
                    foreach (var scope in Scopes) scope.AvailabilityChanged(Available != 0 ? 1 : -1);
            }
        }

        private sealed class Reference
        {
            internal Renderer Renderer;
            internal Transform Parent;
            internal Bounds Bounds;
            internal bool Available;
            internal readonly List<GlassCullingParentScope> Anchors = new List<GlassCullingParentScope>();
            internal readonly List<GlassCullingParentScope> Guards = new List<GlassCullingParentScope>();
            internal readonly List<GroupAvailability> Groups = new List<GroupAvailability>();

            internal void Read(bool validateBounds = true)
            {
                if (!Renderer || Renderer.transform.parent != Parent)
                {
                    foreach (var scope in Anchors) scope.Retire();
                    foreach (var scope in Guards) scope.Retire();
                    foreach (var group in Groups) foreach (var scope in group.Scopes) scope.Retire();
                    return;
                }
                if (validateBounds)
                {
                    var box = Renderer.bounds;
                    bool moved = (box.center - Bounds.center).sqrMagnitude > .000001f ||
                        (box.size - Bounds.size).sqrMagnitude > .000001f;
                    foreach (var scope in Anchors)
                        if (moved || !scope.Root || !Renderer.transform.IsChildOf(scope.Root)) scope.Retire();
                }
                bool current = IsAvailable(Renderer);
                if (current == Available) return;
                Available = current;
                foreach (var scope in Guards) scope.AvailabilityChanged(current ? 1 : -1);
                foreach (var group in Groups) group.Change(current ? 1 : -1);
            }
        }

        private readonly Dictionary<Renderer, Reference> references = new Dictionary<Renderer, Reference>();
        private readonly List<Reference> audit = new List<Reference>();
        private readonly HashSet<PerfectCullingBakeGroup> watchedGroups = new HashSet<PerfectCullingBakeGroup>();
        private readonly Dictionary<PerfectCullingBakeGroup, GroupAvailability> groupAvailability = new Dictionary<PerfectCullingBakeGroup, GroupAvailability>();
        private int cursor;
        internal int ScopeCount { get; private set; }
        internal int GroupMemberVisits { get; private set; }
        internal int IndexedGroupCount => groupAvailability.Count;
        internal IEnumerable<PerfectCullingBakeGroup> Groups => watchedGroups;

        private static bool IsAvailable(Renderer renderer) => renderer && renderer.gameObject.activeInHierarchy &&
            renderer.enabled && !renderer.forceRenderingOff && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly;

        // No whole-scene renderer enumeration. Each relevant native root is traversed once,
        // yielding for individual nodes/children/renderers, and all candidate ancestors share the index.
        internal IEnumerable<bool> Build(List<Candidate> candidates,
            Dictionary<Transform, PerfectCullingCrossSceneGroup> controllers,
            Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners)
        {
            var scopes = new Dictionary<Transform, GlassCullingParentScope>();
            var roots = new HashSet<Transform>();
            foreach (var candidate in candidates)
            {
                PerfectCullingCrossSceneGroup controller = null;
                for (var node = candidate.Glass.transform; node; node = node.parent)
                {
                    candidate.Chain.Add(node);
                    if (controllers.TryGetValue(node, out var found))
                    {
                        controller = found;
                        break;
                    }
                    yield return true;
                }
                if (!controller) { candidate.Chain.Clear(); candidate.Reason = "no-parent-controller"; continue; }
                roots.Add(controller.transform);
                foreach (var node in candidate.Chain)
                {
                    if (!scopes.ContainsKey(node)) scopes.Add(node, new GlassCullingParentScope(node, controller));
                    yield return true;
                }
            }
            // Nested controllers are separate boundaries, even if both occur among candidate roots.
            var renderers = new List<Renderer>();
            foreach (var root in roots)
            {
                var stack = new Stack<KeyValuePair<Transform, int>>();
                stack.Push(new KeyValuePair<Transform, int>(root, -1));
                while (stack.Count > 0)
                {
                    var visit = stack.Pop(); var node = visit.Key;
                    if (!node) continue;
                    if (visit.Value == -1)
                    {
                        if (node != root && controllers.ContainsKey(node)) continue;
                        renderers.Clear(); node.GetComponents(renderers);
                        foreach (var renderer in renderers)
                        {
                            if (GlassCullingFamily.HasOpaqueMaterial(renderer))
                                for (var parent = node; parent; parent = parent.parent)
                                {
                                    if (scopes.TryGetValue(parent, out var scope))
                                    { owners.TryGetValue(renderer, out var native); scope.Add(renderer, native); }
                                    if (parent == root) break;
                                    yield return true;
                                }
                            yield return true;
                        }
                        stack.Push(new KeyValuePair<Transform, int>(node, 0));
                    }
                    else if (visit.Value < node.childCount)
                    {
                        stack.Push(new KeyValuePair<Transform, int>(node, visit.Value + 1));
                        stack.Push(new KeyValuePair<Transform, int>(node.GetChild(visit.Value), -1));
                    }
                    yield return true;
                }
            }
            var bindings = new HashSet<GlassCullingParentScope>();
            foreach (var candidate in candidates)
            {
                if (!candidate.Glass || !candidate.Lod) continue;
                foreach (var node in candidate.Chain)
                {
                    var scope = scopes[node];
                    if (!scope.Contains(candidate.Glass)) continue;
                    candidate.Match = scope;
                    bindings.Add(scope);
                    break;
                }
                if (candidate.Match == null && candidate.Chain.Count > 0) candidate.Reason = "outside-parent-reference-bounds";
                yield return true;
            }
            foreach (var scope in bindings)
            {
                foreach (var step in Bind(scope, owners)) yield return step;
                scope.Bound = true; ScopeCount++;
            }
        }

        private Reference GetReference(Renderer renderer, Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners)
        {
            if (owners.TryGetValue(renderer, out var aliases)) watchedGroups.UnionWith(aliases);
            if (!references.TryGetValue(renderer, out var reference))
            {
                reference = new Reference { Renderer = renderer, Parent = renderer.transform.parent,
                    Bounds = renderer.bounds, Available = IsAvailable(renderer) };
                references.Add(renderer, reference); audit.Add(reference);
            }
            // Bounds validation belongs to the bounded audit, not every new subscription.
            else reference.Read(false);
            return reference;
        }

        private IEnumerable<bool> Bind(GlassCullingParentScope scope,
            Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners)
        {
            foreach (var anchor in scope.Anchors)
            {
                if (!anchor) { scope.Retire(); continue; }
                GetReference(anchor, owners).Anchors.Add(scope);
                yield return true;
            }
            // Unowned opaque renderers remain conservative visibility guards.
            foreach (var guard in scope.Guards)
            {
                if (!guard) { scope.Retire(); continue; }
                var reference = GetReference(guard, owners);
                reference.Guards.Add(scope);
                if (reference.Available) scope.AvailabilityChanged(1);
                yield return true;
            }
            foreach (var group in scope.Groups)
            {
                watchedGroups.Add(group);
                if (!groupAvailability.TryGetValue(group, out var availability))
                {
                    availability = new GroupAvailability();
                    var data = group.RuntimeGroupData;
                    int count = group.runtimeGroupDataSize;
                    if (data == null || count < 0 || count > data.Length) { scope.Retire(); yield break; }
                    var members = new HashSet<Renderer>();
                    // Full membership and aliases are retained, but scanned/subscribed ONCE per group.
                    // Sections share this aggregate instead of each copying the entire group.
                    for (int i = 0; i < count; i++)
                    {
                        GroupMemberVisits++;
                        var renderer = data[i].Renderer;
                        if (renderer && renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && members.Add(renderer))
                        {
                            var reference = GetReference(renderer, owners);
                            reference.Groups.Add(availability);
                            if (reference.Available) availability.Change(1);
                        }
                        yield return true;
                    }
                    groupAvailability.Add(group, availability);
                }
                availability.Scopes.Add(scope);
                if (availability.Available != 0) scope.AvailabilityChanged(1);
                yield return true;
            }
            scope.Anchors.Clear(); scope.Guards.Clear();
        }

        internal void GroupChanged(PerfectCullingBakeGroup group)
        {
            if (!watchedGroups.Contains(group)) return;
            var data = group.RuntimeGroupData;
            int count = group.runtimeGroupDataSize;
            if (data == null || count < 0 || count > data.Length) return;
            for (int i = 0; i < count; i++)
                if (data[i].Renderer && references.TryGetValue(data[i].Renderer, out var reference)) reference.Read(false);
        }

        internal void Audit()
        {
            int count = System.Math.Min(128, audit.Count);
            while (count-- > 0)
            {
                if (cursor >= audit.Count) cursor = 0;
                audit[cursor++].Read();
            }
        }
    }
}
