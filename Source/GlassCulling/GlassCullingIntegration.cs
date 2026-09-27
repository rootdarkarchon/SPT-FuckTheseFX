using System;
using System.Collections.Generic;
using System.Diagnostics;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FuckTheseFX.Source.GlassCulling
{
    internal sealed class GlassCullingIntegration : IDisposable
    {
        private const string HarmonyId = "com.rootdarkarchon.fuckthesefx.glass-culling";
        private static GlassCullingIntegration instance;
        private Harmony harmony;
        private readonly List<GlassCullingFamily> families = new List<GlassCullingFamily>();
        private readonly Dictionary<PerfectCullingBakeGroup, List<GlassCullingFamily>> byGroup =
            new Dictionary<PerfectCullingBakeGroup, List<GlassCullingFamily>>();
        private IEnumerator<bool> scan;
        private bool initialized, enabled, pending, failed, managerPresent;
        private float scanAfter;
        private int auditCursor;
        private AbstractGame raid;
        private GameWorld raidWorld;
        private GlassCullingParentIndex parentIndex;

        internal void Initialize()
        {
            instance = this;
            try
            {
                harmony = new Harmony(HarmonyId);
                var changed = new HarmonyMethod(typeof(GlassCullingIntegration), nameof(GroupChanged));
                harmony.Patch(AccessTools.Method(typeof(PerfectCullingBakeGroup), "Toggle"), postfix: changed);
                harmony.Patch(AccessTools.Method(typeof(PerfectCullingBakeGroup), "RuntimeSetForceRenderingOff"), postfix: changed);
                harmony.Patch(AccessTools.Method(typeof(PerfectCullingBakeGroup), "Init"),
                    prefix: new HarmonyMethod(typeof(GlassCullingIntegration), nameof(GroupInitializing)));
                SceneManager.sceneLoaded += SceneLoaded;
                SceneManager.sceneUnloaded += SceneUnloaded;
                initialized = true;
            }
            catch { Dispose(); throw; }
        }

        internal void Update()
        {
            if (!initialized) return;
            var currentRaid = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance : null;
            var currentWorld = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (raid != currentRaid || raidWorld != currentWorld)
            {
                Invalidate(); raid = currentRaid; raidWorld = currentWorld;
            }
            // Poll native readiness so enabling during loading cannot miss a one-shot event.
            bool ready = GlassCullingRaidGate.IsReady(raid, raidWorld);
            bool requested = Plugin.GlassCullingEnabled.Value && !failed && ready;
            // A manager can be introduced at runtime without a scene load. Never compete with it.
            bool manager = WindowsManager.Instance && WindowsManager.Instance.isActiveAndEnabled;
            if (enabled != requested || managerPresent != manager)
            {
                enabled = requested; managerPresent = manager;
                Invalidate();
                if (enabled && managerPresent) Plugin.MyLog.LogInfo("Glass culling: bypassed while WindowsManager is active.");
            }
            if (!enabled || managerPresent) return;
            try
            {
                if (pending && Time.realtimeSinceStartup >= scanAfter)
                {
                    pending = false; scan = BuildAssociations();
                }
                if (scan != null)
                {
                    // Discovery is amortized. Native FindObjectsOfType calls themselves cannot be sliced.
                    long start = Stopwatch.GetTimestamp();
                    do
                    {
                        if (!scan.MoveNext()) { scan.Dispose(); scan = null; break; }
                    } while ((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency < 2);
                }
                // Bounded repair for destruction, external renderer changes and moved windows.
                // Native visibility transitions use the immediate hooks below, including optic changes.
                int checks = Math.Min(32, families.Count);
                while (checks-- > 0)
                {
                    if (auditCursor >= families.Count) auditCursor = 0;
                    families[auditCursor++].Apply();
                }
                parentIndex?.Audit();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void GroupChanged(PerfectCullingBakeGroup __instance)
        {
            var self = instance;
            if (self == null || !self.enabled || self.managerPresent || self.failed) return;
            try
            {
                if (self.byGroup.TryGetValue(__instance, out var affected))
                    foreach (var family in affected) family.Apply();
                self.parentIndex?.GroupChanged(__instance);
            }
            catch (Exception error) { self.Fail(error); }
        }

        private static void GroupInitializing(PerfectCullingBakeGroup __instance)
        {
            var self = instance;
            if (self == null || !self.enabled) return;
            // Runtime arrays are rebuilt by Init. Never retain associations across that boundary.
            self.Invalidate();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Invalidate();
        private void SceneUnloaded(Scene scene) => Invalidate();

        private void Invalidate()
        {
            scan?.Dispose(); scan = null;
            parentIndex = null;
            foreach (var family in families) family.Restore();
            families.Clear(); byGroup.Clear(); auditCursor = 0;
            pending = true; scanAfter = Time.realtimeSinceStartup + 2;
        }

        private IEnumerator<bool> BuildAssociations()
        {
            var candidates = new Dictionary<LODGroup, HashSet<Renderer>>();
            var wanted = new HashSet<Renderer>();
            var members = new Dictionary<LODGroup, Renderer[]>();
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>>();
            var registered = new HashSet<Renderer>();
            int windows = 0, noLod = 0;
            var reasons = new Dictionary<string, int>();
            // The serialized exclusion list identifies the population actually omitted for batching.
            foreach (var content in Object.FindObjectsOfType<PerfectCullingCrossSceneContentMeshes>(true))
            {
                if (!content || content.Windows == null) continue;
                foreach (var glass in content.Windows)
                {
                    yield return true;
                    if (!glass || !glass.gameObject.activeInHierarchy || !GlassCullingFamily.IsGlass(glass)) continue;
                    windows++;
                    var lod = glass.GetComponentInParent<LODGroup>();
                    if (!lod) { noLod++; continue; }
                    if (!candidates.TryGetValue(lod, out var set)) candidates.Add(lod, set = new HashSet<Renderer>());
                    set.Add(glass);
                }
            }
            foreach (var pair in candidates)
            {
                if (!pair.Key) continue;
                var set = new HashSet<Renderer>();
                foreach (var level in pair.Key.GetLODs())
                    foreach (var renderer in level.renderers) if (renderer) { set.Add(renderer); wanted.Add(renderer); }
                members.Add(pair.Key, new List<Renderer>(set).ToArray());
                yield return true;
            }
            var assemblies = new Dictionary<LODGroup, GlassCullingResolver.Assembly>();
            var assemblyCache = new Dictionary<Transform, GlassCullingResolver.Assembly>();
            foreach (var pair in members)
            {
                if (!pair.Key) continue;
                bool hasReference = false;
                foreach (var renderer in pair.Value)
                    if (GlassCullingFamily.HasOpaqueMaterial(renderer)) { hasReference = true; break; }
                if (hasReference) continue;
                var parent = pair.Key.transform.parent;
                for (int depth = 0; depth < 2 && parent; depth++, parent = parent.parent)
                {
                    if (!assemblyCache.TryGetValue(parent, out var assembly))
                    {
                        assembly = new GlassCullingResolver.Assembly { Root = parent };
                        assemblyCache.Add(parent, assembly);
                        foreach (var step in GlassCullingResolver.InspectAssembly(assembly)) yield return step;
                    }
                    if (assembly.TooLarge) { assemblies.Add(pair.Key, assembly); break; }
                    if (assembly.Peers.Length == 0) continue;
                    assemblies.Add(pair.Key, assembly);
                    foreach (var peer in assembly.Peers) wanted.Add(peer);
                    break;
                }
                yield return true;
            }
            var groups = new HashSet<PerfectCullingBakeGroup>();
            var controllers = new Dictionary<Transform, PerfectCullingCrossSceneGroup>();
            foreach (var controller in Object.FindObjectsOfType<PerfectCullingCrossSceneGroup>(true))
            {
                if (!controller || controller.bakeGroups == null) continue;
                controllers[controller.transform] = controller;
                foreach (var group in controller.bakeGroups) { if (group != null) groups.Add(group); yield return true; }
            }
            // Some contents and controllers alias the same group objects. Reference identity deduplicates them.
            foreach (var content in Object.FindObjectsOfType<PerfectCullingCrossSceneContent>(true))
            {
                if (!content || content.BakeGroups == null) continue;
                foreach (var group in content.BakeGroups) { if (group != null) groups.Add(group); yield return true; }
            }
            foreach (var group in groups)
            {
                if (group.renderers != null)
                    foreach (var renderer in group.renderers) if (renderer && wanted.Contains(renderer)) registered.Add(renderer);
                var data = group.RuntimeGroupData;
                int count = group.runtimeGroupDataSize;
                if (data != null && count >= 0 && count <= data.Length)
                    for (int i = 0; i < count; i++)
                    {
                        var renderer = data[i].Renderer;
                        if (renderer)
                        {
                            if (wanted.Contains(renderer)) registered.Add(renderer);
                            if (!owners.TryGetValue(renderer, out var set)) owners.Add(renderer, set = new HashSet<PerfectCullingBakeGroup>());
                            set.Add(group);
                        }
                        yield return true;
                    }
                yield return true;
            }
            // Also respect the independent CullingObject component/activation lists.
            var controlledObjects = new HashSet<GameObject>();
            foreach (var culler in Object.FindObjectsOfType<CullingObject>(true))
            {
                if (culler._componentsToTurnOff != null)
                    foreach (var component in culler._componentsToTurnOff)
                        if (component is Renderer renderer && wanted.Contains(renderer)) registered.Add(renderer);
                if (culler._gameObjectsToTurnOff != null)
                    foreach (var go in culler._gameObjectsToTurnOff) if (go) controlledObjects.Add(go);
                yield return true;
            }
            int attached = 0, suppressed = 0;
            var assigned = new HashSet<Renderer>();
            var matchedLods = new HashSet<LODGroup>();
            var kinds = new Dictionary<string, int>();
            var unmatched = new List<GlassCullingParentIndex.Candidate>();
            foreach (var pair in candidates)
            {
                if (!pair.Key || !members.TryGetValue(pair.Key, out var familyMembers)) continue;
                // Keep the existing successful whole-LOD associations first.
                var original = SelectFamily(pair.Key, pair.Value, familyMembers, owners, registered, controlledObjects, out _);
                if (original != null)
                {
                    Register(original); suppressed += original.Apply();
                    foreach (var glass in original.Glass) assigned.Add(glass);
                    attached += original.Glass.Length; Count(kinds, original.Kind, original.Glass.Length); matchedLods.Add(pair.Key);
                }
                assemblies.TryGetValue(pair.Key, out var assembly);
                var parts = new Dictionary<Transform, List<GlassCullingFamily>>();
                foreach (var glass in pair.Value)
                {
                    if (assigned.Contains(glass)) continue;
                    var match = GlassCullingResolver.SelectAdditional(pair.Key, glass, familyMembers, assembly,
                        owners, registered, controlledObjects, out var reason);
                    if (match == null)
                    {
                        if (pair.Key.enabled && glass && glass.transform.IsChildOf(pair.Key.transform) &&
                            Array.IndexOf(familyMembers, glass) >= 0 && GlassCullingResolver.Eligible(glass, registered, controlledObjects))
                            unmatched.Add(new GlassCullingParentIndex.Candidate { Lod = pair.Key, Glass = glass, Reason = reason });
                        else Count(reasons, reason, 1);
                    }
                    else
                    {
                        assigned.Add(glass);
                        if (!parts.TryGetValue(match.Scope, out var list)) parts.Add(match.Scope, list = new List<GlassCullingFamily>());
                        list.Add(match);
                    }
                    yield return true;
                }
                foreach (var part in parts.Values)
                {
                    var first = part[0];
                    var glass = new Renderer[part.Count];
                    for (int i = 0; i < part.Count; i++) glass[i] = part[i].Glass[0];
                    var family = new GlassCullingFamily(pair.Key, first.Peers, glass, first.Owners, first.Scope, first.Kind);
                    Register(family); suppressed += family.Apply(); attached += glass.Length;
                    Count(kinds, family.Kind, glass.Length); matchedLods.Add(pair.Key);
                    yield return true;
                }
                yield return true;
            }
            if (unmatched.Count > 0)
            {
                parentIndex = new GlassCullingParentIndex();
                foreach (var step in parentIndex.Build(unmatched, controllers, owners)) yield return step;
                var parentFamilies = new Dictionary<LODGroup, Dictionary<GlassCullingParentScope, List<Renderer>>>();
                foreach (var candidate in unmatched)
                {
                    if (candidate.Match == null) Count(reasons, candidate.Reason, 1);
                    else if (candidate.Glass && candidate.Lod && assigned.Add(candidate.Glass))
                    {
                        if (!parentFamilies.TryGetValue(candidate.Lod, out var scopes))
                            parentFamilies.Add(candidate.Lod, scopes = new Dictionary<GlassCullingParentScope, List<Renderer>>());
                        if (!scopes.TryGetValue(candidate.Match, out var glass)) scopes.Add(candidate.Match, glass = new List<Renderer>());
                        glass.Add(candidate.Glass);
                    }
                    yield return true;
                }
                foreach (var lod in parentFamilies)
                    foreach (var pair in lod.Value)
                    {
                        var parent = pair.Key;
                        var family = new GlassCullingFamily(lod.Key, Array.Empty<Renderer>(), pair.Value.ToArray(),
                            Array.Empty<PerfectCullingBakeGroup>(), parent.Root, "parent", parent);
                        Register(family); suppressed += family.Apply(); attached += family.Glass.Length;
                        Count(kinds, family.Kind, family.Glass.Length); matchedLods.Add(lod.Key);
                        yield return true;
                    }
            }
            Plugin.MyLog.LogInfo($"Glass culling ready: {attached} glass renderers associated; {suppressed} initially suppressed.");
#if DEBUG
            var nativeGroups = new HashSet<PerfectCullingBakeGroup>(byGroup.Keys);
            if (parentIndex != null) nativeGroups.UnionWith(parentIndex.Groups);
            Plugin.MyLog.LogInfo($"Glass culling ready: {attached} glass renderers in {families.Count} associations / {matchedLods.Count} LOD families; " +
                $"{suppressed} initially suppressed. Candidates={windows}, noLOD={noLod}, nativeGroups={nativeGroups.Count}, parentScopes={parentIndex?.ScopeCount ?? 0}.");
            foreach (var kind in kinds) Plugin.MyLog.LogInfo($"Glass culling matched: {kind.Key}={kind.Value} renderers.");
            foreach (var reason in reasons) Plugin.MyLog.LogInfo($"Glass culling skipped: {reason.Key}={reason.Value} renderers.");
#endif
        }

        private static void Count(Dictionary<string, int> counts, string key, int value)
        {
            counts.TryGetValue(key, out int old); counts[key] = old + value;
        }

        private void Register(GlassCullingFamily family)
        {
            families.Add(family);
            if (family.Parent != null) { family.Parent.Families.Add(family); return; }
            foreach (var owner in family.Owners)
            {
                if (!byGroup.TryGetValue(owner, out var list)) byGroup.Add(owner, list = new List<GlassCullingFamily>());
                list.Add(family);
            }
        }

        internal static GlassCullingFamily SelectFamily(LODGroup lod, HashSet<Renderer> candidates, Renderer[] members,
            Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> owners, HashSet<Renderer> registered, HashSet<GameObject> controlledObjects,
            out string reason)
        {
            reason = "inactive-LOD";
            if (!lod || !lod.enabled) return null;
            var peers = new List<Renderer>();
            HashSet<PerfectCullingBakeGroup> common = null;
            foreach (var renderer in members)
            {
                if (!GlassCullingFamily.IsOpaque(renderer)) continue;
                if (!owners.TryGetValue(renderer, out var set) || set.Count == 0) { reason = "opaque-peer-without-runtime-owner"; return null; }
                if (common == null) common = set;
                else if (!GlassCullingFamily.SameOwners(common, set)) { reason = "conflicting-native-owners"; return null; }
                peers.Add(renderer);
            }
            if (peers.Count == 0 || common == null) { reason = "no-opaque-peer"; return null; }
            var peerArray = peers.ToArray();
            var glass = new List<Renderer>();
            foreach (var renderer in members)
            {
                if (!candidates.Contains(renderer) || registered.Contains(renderer) || !GlassCullingFamily.IsGlass(renderer)) continue;
                bool controlled = false;
                for (var parent = renderer.transform; parent; parent = parent.parent)
                    if (controlledObjects.Contains(parent.gameObject)) { controlled = true; break; }
                if (!controlled && GlassCullingFamily.Encloses(peerArray, renderer)) glass.Add(renderer);
            }
            reason = glass.Count == 0 ? "no-unowned-contained-glass" : "accepted";
            return glass.Count == 0 ? null : new GlassCullingFamily(lod, peerArray, glass.ToArray(), new List<PerfectCullingBakeGroup>(common).ToArray());
        }

        private void Fail(Exception error)
        {
            failed = true;
            Invalidate();
            Plugin.MyLog.LogError("Glass culling disabled for this session and suppression restored: " + error);
        }

        public void Dispose()
        {
            initialized = false;
            Suspend();
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            harmony?.UnpatchSelf(); harmony = null;
            if (instance == this) instance = null;
            raid = null; raidWorld = null;
        }

        internal void Suspend() { enabled = false; Invalidate(); }
    }
}
