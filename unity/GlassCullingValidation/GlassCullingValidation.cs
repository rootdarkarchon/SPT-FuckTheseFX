using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FuckTheseFX;
using FuckTheseFX.Source.GlassCulling;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Comfort.Common;
using EFT;

namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    public enum GameStatus { Stopped, Running, Runned, Starting, Started, Stopping, SoftStopping }
    public enum EGameType { Offline, Hideout }
    public class AbstractGame : MonoBehaviour
    {
        public GameStatus Status { get; set; }
        public EGameType GameType { get; set; }
    }
    public class GameWorld : MonoBehaviour { public string LocationId { get; set; } = "tarkovstreets"; }
    public class Player : MonoBehaviour { public GameWorld GameWorld { get; set; } }
    public class EftGamePlayerOwner : MonoBehaviour { public Player Player { get; set; } }
    public class BaseLocalGame<T> : AbstractGame
    {
        public T PlayerOwner { get; set; }
    }
    public class LocalGame : BaseLocalGame<EftGamePlayerOwner> { }
}

namespace FuckTheseFX
{
    internal class Setting<T> { internal T Value; }
    internal class TestLog
    {
        internal void LogInfo(object value) => Debug.Log(value);
        internal void LogError(object value) => Debug.LogError(value);
    }
    internal static class Plugin
    {
        internal static TestLog MyLog = new TestLog();
        internal static Setting<bool> GlassCullingEnabled = new Setting<bool>();
    }
}
// Small game fixtures model the installed IL contracts; the production code uses the real types.
namespace Koenigz.PerfectCulling
{
    public class PerfectCullingBakeGroup
    {
        public struct RuntimeGroupContent { public Renderer Renderer; }
        public Renderer[] renderers;
        public RuntimeGroupContent[] RuntimeGroupData;
        public int runtimeGroupDataSize;
        [MethodImpl(MethodImplOptions.NoInlining)] public int Init()
        {
            RuntimeGroupData = Array.ConvertAll(renderers, r => new RuntimeGroupContent { Renderer = r });
            runtimeGroupDataSize = RuntimeGroupData.Length; return 0;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Toggle(bool visible)
        { for (int i = 0; i < runtimeGroupDataSize; i++) RuntimeGroupData[i].Renderer.enabled = visible; }
        [MethodImpl(MethodImplOptions.NoInlining)] public void RuntimeSetForceRenderingOff(bool off)
        { for (int i = 0; i < runtimeGroupDataSize; i++) RuntimeGroupData[i].Renderer.forceRenderingOff = off; }
    }
}
namespace Koenigz.PerfectCulling.EFT
{
    public class PerfectCullingCrossSceneContent : MonoBehaviour { public PerfectCullingBakeGroup[] BakeGroups; }
    public class PerfectCullingCrossSceneContentMeshes : PerfectCullingCrossSceneContent { public Renderer[] Windows; }
    public class PerfectCullingCrossSceneGroup : MonoBehaviour { public PerfectCullingBakeGroup[] bakeGroups; }
}
public class CullingObject : MonoBehaviour
{
    public List<Component> _componentsToTurnOff;
    public List<GameObject> _gameObjectsToTurnOff;
}
public class WindowsManager : MonoBehaviour { public static WindowsManager Instance; }

public static class GlassCullingValidation
{
    private static int assertions;
    private static void Check(bool result, string description)
    { if (!result) throw new Exception(description); assertions++; }
    private static void Discover(GlassCullingIntegration feature)
    {
        typeof(GlassCullingIntegration).GetField("scanAfter", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(feature, -1f);
        for (int i = 0; i < 100; i++) feature.Update();
    }
    private static void Repair(GlassCullingIntegration feature)
    {
        // Advance maintenance opportunities explicitly, without sleeping in editor tests.
        var timer = typeof(GlassCullingIntegration).GetField("nextAuditAt", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < 64; i++) { timer.SetValue(feature, -1f); feature.Update(); }
    }
    private static Renderer Mesh(Transform parent, string name, Material material, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = scale;
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        return renderer;
    }
    public static void RunUpdateBenchmark()
    {
        var root = new GameObject("steady glass Update benchmark");
        GlassCullingIntegration feature = null;
        Material opaque = null, glassMaterial = null;
        try
        {
            opaque = new Material(Shader.Find("Standard"));
            glassMaterial = new Material(Shader.Find("Global Fog/Transparent Reflective Specular"));
            var game = root.AddComponent<AbstractGame>(); game.Status = GameStatus.Started;
            Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = root.AddComponent<GameWorld>();
            const int sections = 512;
            var bodies = new Renderer[sections]; var candidates = new List<GlassCullingParentIndex.Candidate>();
            for (int i = 0; i < sections; i++)
            {
                var part = new GameObject("section"); part.transform.SetParent(root.transform, false);
                part.transform.localPosition = Vector3.right * (i * 8);
                bodies[i] = Mesh(part.transform, "frame", opaque, Vector3.one * 4);
                var lod = part.AddComponent<LODGroup>(); var panes = new Renderer[4];
                for (int j = 0; j < panes.Length; j++)
                {
                    panes[j] = Mesh(part.transform, "glass", glassMaterial, Vector3.one);
                    candidates.Add(new GlassCullingParentIndex.Candidate { Lod = lod, Glass = panes[j] });
                }
                lod.SetLODs(new[] { new LOD(.01f, panes) });
            }
            var group = new PerfectCullingBakeGroup { renderers = bodies }; group.Init(); group.Toggle(false);
            var controller = root.AddComponent<PerfectCullingCrossSceneGroup>(); controller.bakeGroups = new[] { group };
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>>();
            foreach (var body in bodies) owners.Add(body, new HashSet<PerfectCullingBakeGroup> { group });
            var index = new GlassCullingParentIndex();
            foreach (var step in index.Build(candidates, new Dictionary<Transform, PerfectCullingCrossSceneGroup> { { root.transform, controller } }, owners)) { }
            Plugin.GlassCullingEnabled.Value = true; feature = new GlassCullingIntegration(); feature.Initialize(); feature.Update();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var familyList = (List<GlassCullingFamily>)typeof(GlassCullingIntegration).GetField("families", flags).GetValue(feature);
            for (int i = 0; i < candidates.Count; i += 4)
            {
                var c = candidates[i]; var panes = new[] { c.Glass, candidates[i+1].Glass, candidates[i+2].Glass, candidates[i+3].Glass };
                var family = new GlassCullingFamily(c.Lod, Array.Empty<Renderer>(), panes, new[] { group }, c.Match.Root, "parent", c.Match);
                c.Match.Families.Add(family); familyList.Add(family); family.Apply();
            }
            typeof(GlassCullingIntegration).GetField("parentIndex", flags).SetValue(feature, index);
            typeof(GlassCullingIntegration).GetField("pending", flags).SetValue(feature, false);
            var nextAudit = typeof(GlassCullingIntegration).GetField("nextAuditAt", flags);
            nextAudit?.SetValue(feature, float.MaxValue);
            for (int i = 0; i < 100; i++) feature.Update();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) feature.Update();
            watch.Stop();
            var result = $"families={sections}, glass={candidates.Count}, calls=2000, idleUpdateMeanMs={watch.Elapsed.TotalMilliseconds / 2000:F6}";
            if (nextAudit != null)
            {
                watch.Restart();
                for (int i = 0; i < 200; i++) { nextAudit.SetValue(feature, -1f); feature.Update(); }
                watch.Stop();
                result += $", forcedMaintenanceMeanMs={watch.Elapsed.TotalMilliseconds / 200:F6}";
            }
            File.WriteAllText(nextAudit == null ? "update-baseline.txt" : "update-optimized.txt", result);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            feature?.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(opaque); Object.DestroyImmediate(glassMaterial);
            Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        }
    }

    public static void Run()
    {
        GlassCullingIntegration feature = null;
        GameObject root = null;
        Material glassMaterial = null, opaqueMaterial = null;
        try
        {
            root = new GameObject("building or vehicle family");
            var game = root.AddComponent<AbstractGame>();
            game.GameType = EGameType.Offline;
            Singleton<AbstractGame>.Instance = game;
            Singleton<GameWorld>.Instance = root.AddComponent<GameWorld>();
            var lod = root.AddComponent<LODGroup>();
            glassMaterial = new Material(Shader.Find("Global Fog/Transparent Reflective Specular"));
            opaqueMaterial = new Material(Shader.Find("Standard"));
            var body0 = Mesh(root.transform, "body LOD0", opaqueMaterial, Vector3.one * 4);
            var body1 = Mesh(root.transform, "body LOD1", opaqueMaterial, Vector3.one * 4);
            var glass = Mesh(root.transform, "window", glassMaterial, Vector3.one);
            lod.SetLODs(new[] { new LOD(.5f, new[] { body0, glass }), new LOD(.01f, new[] { body1, glass }) });
            var group = new PerfectCullingBakeGroup { renderers = new[] { body0, body1 } }; group.Init();
            var other = new PerfectCullingBakeGroup { renderers = new[] { body0, body1 } }; other.Init();
            var ownerSet = new HashSet<PerfectCullingBakeGroup> { group };
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> { [body0] = ownerSet, [body1] = ownerSet };
            var registered = new HashSet<Renderer> { body0, body1 };
            var candidates = new HashSet<Renderer> { glass };
            var members = new[] { body0, body1, glass };
            var controlled = new HashSet<GameObject>();
            Func<GlassCullingFamily> select = () => GlassCullingIntegration.SelectFamily(lod, candidates, members, owners, registered, controlled, out _);
            Check(select() != null, "Exact shared LOD accepted");
            owners[body1] = new HashSet<PerfectCullingBakeGroup> { other };
            Check(select() == null, "Conflicting native owners rejected"); owners[body1] = ownerSet;
            owners.Remove(body1); Check(select() == null, "Unregistered opaque peer rejected"); owners[body1] = ownerSet;
            registered.Add(glass); Check(select() == null, "Already registered glass rejected"); registered.Remove(glass);
            controlled.Add(root); Check(select() == null, "Existing GameObject activation owner respected"); controlled.Clear();
            glass.transform.localPosition = Vector3.right * 10; Check(select() == null, "Protruding glass rejected"); glass.transform.localPosition = Vector3.zero;
            glass.sharedMaterials = new[] { glassMaterial, opaqueMaterial }; Check(select() == null, "Mixed material rejected"); glass.sharedMaterials = new[] { glassMaterial };
            glass.shadowCastingMode = ShadowCastingMode.ShadowsOnly; Check(select() == null, "Persistent shadows excluded"); glass.shadowCastingMode = ShadowCastingMode.Off;
            var family = select();
            group.Toggle(false); Check(family.Apply() == 1 && glass.enabled && glass.forceRenderingOff, "Occlusion suppresses without disabling glass");
            body1.enabled = true; family.Apply(); Check(!glass.forceRenderingOff, "Any visible LOD peer releases glass");
            group.Toggle(false); family.Apply(); glass.enabled = false;
            group.Toggle(true); family.Apply(); Check(!glass.enabled && !glass.forceRenderingOff, "Broken disabled window stays disabled"); glass.enabled = true;
            glass.forceRenderingOff = true; group.Toggle(false); family.Apply(); family.Restore();
            Check(glass.forceRenderingOff, "Preexisting force-off preserved"); glass.forceRenderingOff = false;
            family.Apply(); family.Restore(); Check(!glass.forceRenderingOff, "Owned suppression restored");
            family.Apply(); glass.transform.localPosition = Vector3.right * 10; family.Apply(); Check(!glass.forceRenderingOff, "Moved window released"); glass.transform.localPosition = Vector3.zero;
            family.Apply(); lod.enabled = false; family.Apply(); Check(!glass.forceRenderingOff, "Disabled LOD releases suppression"); lod.enabled = true;

            var content = root.AddComponent<PerfectCullingCrossSceneContentMeshes>();
            content.Windows = new[] { glass }; content.BakeGroups = new[] { group };
            root.AddComponent<PerfectCullingCrossSceneGroup>().bakeGroups = new[] { group }; // Alias, not another owner.
            feature = new GlassCullingIntegration(); feature.Initialize();
            Plugin.GlassCullingEnabled.Value = false; feature.Update(); Check(!glass.forceRenderingOff, "Default disabled unchanged");
            Plugin.GlassCullingEnabled.Value = true;
            foreach (var status in new[] { GameStatus.Stopped, GameStatus.Running, GameStatus.Runned, GameStatus.Starting })
            {
                game.Status = status; feature.Update(); Discover(feature);
                Check(!glass.forceRenderingOff, "No culling discovery before Started: " + status);
                var pendingScan = typeof(GlassCullingIntegration).GetField("scan", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(feature);
                Check(pendingScan == null, "Loading does not start an iterator: " + status);
            }
            game.Status = GameStatus.Started;
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Discovery applies initial native state");
            var auditTimer = typeof(GlassCullingIntegration).GetField("nextAuditAt", BindingFlags.NonPublic | BindingFlags.Instance);
            auditTimer.SetValue(feature, float.MaxValue);
            body1.enabled = true; // Deliberately bypass the native visibility hooks.
            for (int i = 0; i < 100; i++) feature.Update();
            Check(glass.forceRenderingOff, "Idle frames do not poll renderer state before the maintenance deadline");
            Repair(feature);
            Check(!glass.forceRenderingOff, "Scheduled maintenance repairs an unhooked renderer change");
            group.Toggle(false);
            glass.forceRenderingOff = false;
            group.Toggle(false);
            Check(!glass.forceRenderingOff, "Repeated unchanged native state skips pane writes");
            Repair(feature);
            Check(glass.forceRenderingOff, "Maintenance still repairs externally cleared owned suppression");
            auditTimer.SetValue(feature, float.MaxValue);
            group.Toggle(true); Check(!glass.forceRenderingOff, "Patched native Toggle restores immediately");
            group.RuntimeSetForceRenderingOff(true); Check(glass.forceRenderingOff, "Patched baked LOD suppression propagates immediately");
            group.RuntimeSetForceRenderingOff(false); Check(!glass.forceRenderingOff, "Patched baked LOD restoration propagates immediately");
            Check(group.renderers.Length == 2 && group.runtimeGroupDataSize == 2, "Native authored and runtime lists unchanged");
            group.Toggle(false); group.Init(); Check(!glass.forceRenderingOff, "Runtime rebuild invalidates and restores old associations");
            Discover(feature); Check(glass.forceRenderingOff, "Runtime rebuild rediscovers");
            feature.Suspend(); Check(!glass.forceRenderingOff, "Plugin disable restores immediately"); feature.Update(); Discover(feature);
            typeof(GlassCullingIntegration).GetMethod("SceneUnloaded", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(feature, new object[] { root.scene });
            Check(!glass.forceRenderingOff, "Scene unload restores and invalidates"); Discover(feature);
            Plugin.GlassCullingEnabled.Value = false; feature.Update(); Check(!glass.forceRenderingOff, "Live toggle restores");
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            WindowsManager.Instance = root.AddComponent<WindowsManager>(); feature.Update(); Check(!glass.forceRenderingOff, "Windows manager bypass restores");
            Object.DestroyImmediate(WindowsManager.Instance); WindowsManager.Instance = null; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Manager removal rediscovers");
            game.Status = GameStatus.Stopping; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Raid end releases suppression and prevents rediscovery");
            var newWorld = new GameObject("next map world"); newWorld.transform.SetParent(root.transform);
            Singleton<GameWorld>.Instance = newWorld.AddComponent<GameWorld>();
            game.Status = GameStatus.Starting; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Replacement map stays gated while loading");
            game.Status = GameStatus.Started; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Replacement map discovers after Started");
            Singleton<AbstractGame>.Instance = null; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Missing game releases and gates discovery");
            Singleton<AbstractGame>.Instance = game; game.GameType = EGameType.Hideout; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Started hideout does not trigger raid discovery");
            game.GameType = EGameType.Offline; feature.Update(); Discover(feature);
            Object.DestroyImmediate(body1.gameObject); Repair(feature); Check(!glass.forceRenderingOff, "Destroyed peer releases and retires association");
            feature.Dispose(); Discover(feature); Check(!glass.forceRenderingOff, "Disposed integration cannot restart from Update");
            feature = null; Check(!glass.forceRenderingOff, "Dispose restores");
            Extended(glassMaterial, opaqueMaterial);
            ParentBoundaries(glassMaterial, opaqueMaterial);
            NativeStartup(glassMaterial, opaqueMaterial);
            FogSixGlass(glassMaterial, opaqueMaterial);
            File.WriteAllLines("scaling.txt", new[] { SharedGroupScale(512, glassMaterial, opaqueMaterial), SharedGroupScale(1024, glassMaterial, opaqueMaterial) });
            File.WriteAllText("validation.txt", $"PASS: {assertions} glass culling assertions, including actual Harmony hooks in Unity 2022.3.43f1. Game fixtures are not a live raid test.");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        finally
        {
            feature?.Dispose(); if (root) Object.DestroyImmediate(root);
            Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null;
            if (glassMaterial) Object.DestroyImmediate(glassMaterial); if (opaqueMaterial) Object.DestroyImmediate(opaqueMaterial);
        }
    }

    private static void FogSixGlass(Material nativeMaterial, Material opaqueMaterial)
    {
        var root = new GameObject("FogSix glass material swaps");
        GlassCullingIntegration feature = null;
        Material paneMaterial = null, noCanaryMaterial = null, unsupportedMaterial = null;
        Shader noCanaryShader = null;
        try
        {
            var fogSixShader = Shader.Find("FogSix/Glass/Transparent Reflective Specular");
            Check(fogSixShader && fogSixShader.isSupported, "FogSix alias fixture compiled");
            paneMaterial = new Material(fogSixShader);
            Check(paneMaterial.HasProperty("_FogSixGlass"), "FogSix fixture exposes shader-swap canary");
            paneMaterial.SetFloat("_FogSixGlass", 0);
            var game = root.AddComponent<AbstractGame>(); game.Status = GameStatus.Started;
            Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = root.AddComponent<GameWorld>();
            var lod = root.AddComponent<LODGroup>();
            var frame = Mesh(root.transform, "opaque frame", opaqueMaterial, Vector3.one * 4);
            var pane = Mesh(root.transform, "already ported pane", paneMaterial, Vector3.one);
            lod.SetLODs(new[] { new LOD(.01f, new[] { frame, pane }) });
            var group = new PerfectCullingBakeGroup { renderers = new[] { frame } }; group.Init(); group.Toggle(false);
            root.AddComponent<PerfectCullingCrossSceneGroup>().bakeGroups = new[] { group };
            var content = root.AddComponent<PerfectCullingCrossSceneContentMeshes>();
            content.Windows = new[] { pane }; content.BakeGroups = new[] { group };

            Check(GlassCullingFamily.IsGlass(pane), "Exact FogSix specular alias with canary accepted");
            paneMaterial.renderQueue = 2000;
            Check(!GlassCullingFamily.IsOpaque(pane) && !GlassCullingFamily.HasOpaqueMaterial(pane),
                "Ported pane at opaque queue cannot become an opaque visibility anchor");
            frame.sharedMaterial = paneMaterial;
            Check(GlassCullingIntegration.SelectFamily(lod, new HashSet<Renderer> { pane }, new[] { frame, pane },
                new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> { [frame] = new HashSet<PerfectCullingBakeGroup> { group } },
                new HashSet<Renderer> { frame }, new HashSet<GameObject>(), out _) == null,
                "Native-controlled ported pane cannot anchor another glass target at opaque queue");
            frame.sharedMaterial = opaqueMaterial;
            paneMaterial.renderQueue = -1;
            pane.sharedMaterials = new[] { paneMaterial, opaqueMaterial };
            Check(!GlassCullingFamily.IsGlass(pane), "Ported glass mixed with opaque material rejected as a target");
            pane.sharedMaterials = new[] { paneMaterial, nativeMaterial };
            Check(GlassCullingFamily.IsGlass(pane), "Native and supported ported glass slots can coexist");
            pane.sharedMaterials = new[] { paneMaterial };

            // Compile separately rather than importing two assets with the same Shader.Find name.
            noCanaryShader = ShaderUtil.CreateShaderAsset(
                "Shader \"FogSix/Glass/Transparent Reflective Specular\" { SubShader { Tags { \"Queue\"=\"Transparent\" } Pass { Color (1,0,1,1) } } }");
            Check(noCanaryShader && noCanaryShader.name == fogSixShader.name, "No-canary fixture uses the exact supported alias");
            noCanaryMaterial = new Material(noCanaryShader);
            Check(!noCanaryMaterial.HasProperty("_FogSixGlass"), "Negative alias fixture lacks the canary");
            pane.sharedMaterial = noCanaryMaterial;
            Check(!GlassCullingFamily.IsGlass(pane), "Exact FogSix alias without canary rejected");
            unsupportedMaterial = new Material(Shader.Find("FogSix/Glass/Transparent Bumped Specular"));
            Check(unsupportedMaterial.HasProperty("_FogSixGlass"), "Unsupported alias fixture has a valid canary");
            pane.sharedMaterial = unsupportedMaterial;
            Check(!GlassCullingFamily.IsGlass(pane), "Other FogSix glass suffix rejected despite canary");
            pane.sharedMaterial = paneMaterial;

            Plugin.GlassCullingEnabled.Value = true;
            feature = new GlassCullingIntegration(); feature.Initialize(); feature.Update(); Discover(feature);
            var familyField = typeof(GlassCullingIntegration).GetField("families", BindingFlags.Instance | BindingFlags.NonPublic);
            // Discovery is scene-wide; Run's other fixture root remains alive. Select this pane's
            // association explicitly and reject missing or duplicate ownership for this target.
            Func<GlassCullingFamily> paneFamily = () =>
            {
                var matches = ((List<GlassCullingFamily>)familyField.GetValue(feature))
                    .FindAll(family => Array.IndexOf(family.Glass, pane) >= 0);
                return matches.Count == 1 ? matches[0] : null;
            };
            Check(paneFamily() != null && pane.forceRenderingOff, "Discovery associates glass already ported by FogSix");
            group.Toggle(true); Check(!pane.forceRenderingOff, "Native visibility callback releases already ported pane");
            group.RuntimeSetForceRenderingOff(true); Check(pane.forceRenderingOff, "Native force-off callback suppresses already ported pane");
            group.RuntimeSetForceRenderingOff(false); Check(!pane.forceRenderingOff, "Native force-off restoration releases already ported pane");
            group.Toggle(false);
            Plugin.GlassCullingEnabled.Value = false; feature.Update();
            Check(!pane.forceRenderingOff, "Culling toggle restores already ported pane");
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            Check(pane.forceRenderingOff, "Culling toggle rediscovers already ported pane");

            // FogSix swaps shader in place. Establish a native association, then swap without invalidation.
            Plugin.GlassCullingEnabled.Value = false; feature.Update();
            paneMaterial.shader = nativeMaterial.shader;
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            var originalFamily = paneFamily();
            Check(originalFamily != null && pane.forceRenderingOff, "Native material association established before shader swap");
            paneMaterial.shader = fogSixShader; Repair(feature);
            Check(ReferenceEquals(originalFamily, paneFamily()) && pane.forceRenderingOff,
                "Native-to-FogSix shader swap preserves the existing association and suppression");
            group.Toggle(true); Check(!pane.forceRenderingOff, "Native visibility callback releases pane after FogSix shader swap");
            group.Toggle(false); Check(pane.forceRenderingOff, "Native visibility callback suppresses pane after FogSix shader swap");
            paneMaterial.shader = nativeMaterial.shader; Repair(feature);
            Check(ReferenceEquals(originalFamily, paneFamily()) && pane.forceRenderingOff,
                "FogSix-to-native shader swap preserves the existing association and suppression");
            group.Toggle(true); Check(!pane.forceRenderingOff, "Native visibility callback releases pane after shader restoration");
            group.Toggle(false);
            Plugin.GlassCullingEnabled.Value = false; feature.Update();
            Check(!pane.forceRenderingOff, "Culling toggle restores shader-swapped pane");
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            Check(pane.forceRenderingOff, "Culling toggle rediscovers shader-restored pane");
        }
        finally
        {
            feature?.Dispose(); Object.DestroyImmediate(root);
            if (paneMaterial) Object.DestroyImmediate(paneMaterial);
            if (noCanaryMaterial) Object.DestroyImmediate(noCanaryMaterial);
            if (unsupportedMaterial) Object.DestroyImmediate(unsupportedMaterial);
            if (noCanaryShader) Object.DestroyImmediate(noCanaryShader);
            Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        }
    }

    private static void Extended(Material glassMaterial, Material opaqueMaterial)
    {
        var root = new GameObject("extended fixture");
        GlassCullingIntegration feature = null;
        try
        {
            var car = new GameObject("multipart car"); car.transform.SetParent(root.transform);
            var carLod = car.AddComponent<LODGroup>();
            var body0 = Mesh(car.transform, "body0", opaqueMaterial, Vector3.one * 4);
            var body1 = Mesh(car.transform, "body1", opaqueMaterial, Vector3.one * 4);
            var windshield = Mesh(car.transform, "windshield", glassMaterial, Vector3.one);
            var trunk = new GameObject("trunk"); trunk.transform.SetParent(car.transform, false); trunk.transform.localPosition = Vector3.forward * 4;
            var trunk0 = Mesh(trunk.transform, "trunk0", opaqueMaterial, Vector3.one * 2);
            var trunk1 = Mesh(trunk.transform, "trunk1", opaqueMaterial, Vector3.one * 2);
            var trunkGlass = Mesh(trunk.transform, "trunkGlass", glassMaterial, Vector3.one);
            var hood = new GameObject("inactive unowned hood"); hood.transform.SetParent(car.transform, false);
            var hoodMesh = Mesh(hood.transform, "hood", opaqueMaterial, Vector3.one); hood.SetActive(false);
            carLod.SetLODs(new[] { new LOD(.5f, new[] { body0, windshield, trunk0, trunkGlass, hoodMesh }),
                new LOD(.01f, new[] { body1, windshield, trunk1, trunkGlass }) });
            var bodyGroup = new PerfectCullingBakeGroup { renderers = new[] { body0, body1 } }; bodyGroup.Init();
            var trunkGroup = new PerfectCullingBakeGroup { renderers = new[] { trunk0, trunk1 } }; trunkGroup.Init();

            var mixed = new GameObject("mixed-material car"); mixed.transform.SetParent(root.transform);
            var mixedLod = mixed.AddComponent<LODGroup>();
            var mixedBody = Mesh(mixed.transform, "body", opaqueMaterial, Vector3.one * 4);
            mixedBody.sharedMaterials = new[] { opaqueMaterial, glassMaterial };
            var mixedGlass = Mesh(mixed.transform, "separate pure glass", glassMaterial, Vector3.one);
            mixedLod.SetLODs(new[] { new LOD(.01f, new[] { mixedBody, mixedGlass }) });
            var mixedGroup = new PerfectCullingBakeGroup { renderers = new[] { mixedBody } }; mixedGroup.Init();

            var assembly = new GameObject("window assembly"); assembly.transform.SetParent(root.transform);
            var frame0 = Mesh(assembly.transform, "frame0", opaqueMaterial, Vector3.one * 3);
            var frame1 = Mesh(assembly.transform, "frame1", opaqueMaterial, Vector3.one * 3);
            var glassRoot = new GameObject("glass-only LOD"); glassRoot.transform.SetParent(assembly.transform, false);
            var paneLod = glassRoot.AddComponent<LODGroup>();
            var pane = Mesh(glassRoot.transform, "pane", glassMaterial, Vector3.one);
            paneLod.SetLODs(new[] { new LOD(.01f, new[] { pane }) });
            var frameGroup = new PerfectCullingBakeGroup { renderers = new[] { frame0, frame1 } }; frameGroup.Init();
            var inspected = new GlassCullingResolver.Assembly { Root = assembly.transform };
            foreach (var step in GlassCullingResolver.InspectAssembly(inspected)) { }
            Check(!inspected.TooLarge && inspected.Peers.Length == 2, "Compact enclosing assembly found");

            var broad = new GameObject("broad scene folder"); broad.transform.SetParent(root.transform);
            var broadPeers = new List<Renderer>();
            for (int i = 0; i < 17; i++) broadPeers.Add(Mesh(broad.transform, "unrelated frame", opaqueMaterial, Vector3.one * 3));
            var broadGlassRoot = new GameObject("standalone glass"); broadGlassRoot.transform.SetParent(broad.transform, false);
            var broadLod = broadGlassRoot.AddComponent<LODGroup>();
            var broadGlass = Mesh(broadGlassRoot.transform, "glass", glassMaterial, Vector3.one);
            broadLod.SetLODs(new[] { new LOD(.01f, new[] { broadGlass }) });
            var outsidePeer = Mesh(root.transform, "same native group outside the selected parent", opaqueMaterial, Vector3.one);
            outsidePeer.transform.localPosition = Vector3.right * 30;
            var broadGroup = new PerfectCullingBakeGroup { renderers = broadPeers.GetRange(0, 16).ToArray() }; broadGroup.Init();
            var broadOther = new PerfectCullingBakeGroup { renderers = new[] { broadPeers[16], outsidePeer } }; broadOther.Init();
            var outsideAlias = new PerfectCullingBakeGroup { renderers = new[] { outsidePeer } }; outsideAlias.Init();
            var unownedGuard = Mesh(broad.transform, "unowned opaque guard", opaqueMaterial, Vector3.one);
            var broadInspection = new GlassCullingResolver.Assembly { Root = broad.transform };
            foreach (var step in GlassCullingResolver.InspectAssembly(broadInspection)) { }
            Check(broadInspection.TooLarge, "Broad folder rejected even with consistent owners");

            var groups = new[] { bodyGroup, trunkGroup, mixedGroup, frameGroup, broadGroup, broadOther, outsideAlias };
            var content = root.AddComponent<PerfectCullingCrossSceneContentMeshes>();
            content.Windows = new[] { windshield, trunkGlass, mixedGlass, pane, broadGlass, windshield };
            content.BakeGroups = groups;
            root.AddComponent<PerfectCullingCrossSceneGroup>().bakeGroups = groups;
            feature = new GlassCullingIntegration(); feature.Initialize();
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            var matches = (List<GlassCullingFamily>)typeof(GlassCullingIntegration).GetField("families", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(feature);
            var seen = new HashSet<Renderer>();
            foreach (var match in matches) foreach (var glass in match.Glass) Check(seen.Add(glass), "No duplicate glass ownership across resolver stages");
            Check(seen.Contains(windshield) && seen.Contains(trunkGlass) && seen.Contains(mixedGlass) && seen.Contains(pane), "All three extended discovery paths registered");
            Check(seen.Contains(broadGlass), "Broad parent fallback attaches without requiring identical native owners");
            GlassCullingFamily broadMatch = matches.Find(f => Array.IndexOf(f.Glass, broadGlass) >= 0);
            Check(broadMatch.Parent != null && broadMatch.Scope == broad.transform, "Nearest containing section selected below native controller");
            Check(matches.Find(f => Array.IndexOf(f.Glass, windshield) >= 0).Parent == null, "Precise matches retain priority over parent fallback");
            broadGroup.Toggle(false); broadOther.Toggle(false);
            Check(!broadGlass.forceRenderingOff, "Available unowned opaque guard prevents parent suppression");
            unownedGuard.enabled = false; Repair(feature);
            Check(broadGlass.forceRenderingOff, "All native groups and unowned guard unavailable suppress parent glass");
            broadOther.Toggle(true); Check(!broadGlass.forceRenderingOff, "Any owner restores parent glass immediately");
            broadOther.RuntimeSetForceRenderingOff(true); Check(broadGlass.forceRenderingOff, "Parent consumes baked LOD force-off route");
            broadOther.RuntimeSetForceRenderingOff(false); Check(!broadGlass.forceRenderingOff, "Parent restores from baked LOD force-off route");
            broadOther.Toggle(false); outsidePeer.enabled = true; Repair(feature);
            Check(!broadGlass.forceRenderingOff, "Available native-group member outside section prevents suppression");
            outsidePeer.enabled = false; Repair(feature);
            Check(broadGlass.forceRenderingOff, "External native-group member change is audited");
            outsideAlias.Toggle(true); Check(!broadGlass.forceRenderingOff, "Other owner of outside group member restores immediately");
            outsideAlias.Toggle(false); Check(broadGlass.forceRenderingOff, "Other owner of outside group member can suppress immediately");
            unownedGuard.gameObject.SetActive(false); unownedGuard.enabled = true; Repair(feature);
            Check(broadGlass.forceRenderingOff, "Inactive unowned guard does not prevent culling");
            unownedGuard.gameObject.SetActive(true); Repair(feature);
            Check(!broadGlass.forceRenderingOff, "Reactivated unowned guard releases glass");
            unownedGuard.enabled = false; Repair(feature);
            broadGlass.enabled = false; broadOther.Toggle(true); broadOther.Toggle(false);
            Check(!broadGlass.enabled && broadGlass.forceRenderingOff, "Parent preserves broken-window enabled state");
            broadGlass.enabled = true;
            broadGlass.transform.localPosition = Vector3.up * 20; Repair(feature);
            Check(!broadGlass.forceRenderingOff, "Glass leaving cached parent bounds releases suppression");
            broadGlass.transform.localPosition = Vector3.zero; Repair(feature);
            Check(broadGlass.forceRenderingOff, "Glass returning to valid bounds is suppressed again");
            Check(broadGroup.renderers.Length == 16 && broadOther.runtimeGroupDataSize == 2, "Parent fallback never edits native membership arrays");

            bodyGroup.Toggle(false);
            Check(windshield.forceRenderingOff && !trunkGlass.forceRenderingOff, "Body hidden independently of visible trunk despite inactive unowned hood");
            trunkGroup.Toggle(false); bodyGroup.Toggle(true);
            Check(!windshield.forceRenderingOff && trunkGlass.forceRenderingOff, "Trunk hidden independently of visible body");
            trunkGroup.Toggle(true); Check(!trunkGlass.forceRenderingOff, "Trunk restored by its own native owner");
            mixedGroup.Toggle(false); Check(mixedGlass.forceRenderingOff && !mixedBody.enabled, "Mixed body drives separate glass without enabling body");
            mixedGroup.Toggle(true); Check(!mixedGlass.forceRenderingOff, "Mixed-material body visibility restores glass");
            frameGroup.RuntimeSetForceRenderingOff(true); Check(pane.forceRenderingOff, "External frame baked-LOD route reaches nested glass");
            frameGroup.RuntimeSetForceRenderingOff(false); Check(!pane.forceRenderingOff, "Nested glass restored by frame owner");
            pane.enabled = false; frameGroup.Toggle(false); frameGroup.Toggle(true);
            Check(!pane.enabled && !pane.forceRenderingOff, "Nested broken pane stays disabled"); pane.enabled = true;
            frameGroup.Toggle(false); Check(pane.forceRenderingOff, "Nested pane can be suppressed again");
            pane.transform.SetParent(root.transform, true); Repair(feature); Check(!pane.forceRenderingOff, "Reparented glass releases old assembly association");
            pane.transform.SetParent(glassRoot.transform, false);

            // The immediate container has two different owners: do not select one or climb past it.
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> {
                [frame0] = new HashSet<PerfectCullingBakeGroup> { frameGroup },
                [frame1] = new HashSet<PerfectCullingBakeGroup> { bodyGroup } };
            var rejected = GlassCullingResolver.SelectAdditional(paneLod, pane, new[] { pane }, inspected, owners,
                new HashSet<Renderer>(), new HashSet<GameObject>(), out _);
            Check(rejected == null, "Conflicting enclosing assembly owners remain rejected");
            owners[frame1] = owners[frame0];
            Check(GlassCullingResolver.SelectAdditional(paneLod, pane, new[] { pane }, inspected, owners,
                new HashSet<Renderer> { pane }, new HashSet<GameObject>(), out _) == null, "Additional resolver preserves existing glass owner");
            broadPeers[0].transform.SetParent(root.transform, true); Repair(feature);
            Check(!broadGlass.forceRenderingOff, "Lost parent reference retires aggregate and releases glass");
            bodyGroup.Toggle(false); mixedGroup.Toggle(false);
            feature.Suspend(); Check(!windshield.forceRenderingOff && !mixedGlass.forceRenderingOff && !pane.forceRenderingOff, "All added association kinds restore on reset");
        }
        finally { feature?.Dispose(); Object.DestroyImmediate(root); }
    }

    private static void ParentBoundaries(Material glassMaterial, Material opaqueMaterial)
    {
        var root = new GameObject("parent boundaries");
        var orphan = new GameObject("no native ancestor");
        try
        {
            var controller = root.AddComponent<PerfectCullingCrossSceneGroup>();
            var frame = Mesh(root.transform, "building reference", opaqueMaterial, Vector3.one * 10);
            var group = new PerfectCullingBakeGroup { renderers = new[] { frame } }; group.Init(); group.Toggle(false);
            controller.bakeGroups = new[] { group };
            var glassRoot = new GameObject("glass branch"); glassRoot.transform.SetParent(root.transform, false);
            var lod = glassRoot.AddComponent<LODGroup>();
            var glass = Mesh(glassRoot.transform, "glass", glassMaterial, Vector3.one);
            var outside = Mesh(glassRoot.transform, "outside reference union", glassMaterial, Vector3.one);
            outside.transform.localPosition = Vector3.right * 20;
            lod.SetLODs(new[] { new LOD(.01f, new[] { glass, outside }) });
            var nested = new GameObject("separate native asset"); nested.transform.SetParent(root.transform, false);
            var nestedController = nested.AddComponent<PerfectCullingCrossSceneGroup>();
            nestedController.bakeGroups = Array.Empty<PerfectCullingBakeGroup>();
            var nestedLod = nested.AddComponent<LODGroup>();
            var nestedGlass = Mesh(nested.transform, "nested glass", glassMaterial, Vector3.one);
            var orphanLod = orphan.AddComponent<LODGroup>();
            var orphanGlass = Mesh(orphan.transform, "orphan glass", glassMaterial, Vector3.one);
            var candidates = new List<GlassCullingParentIndex.Candidate> {
                new GlassCullingParentIndex.Candidate { Lod = lod, Glass = glass },
                new GlassCullingParentIndex.Candidate { Lod = lod, Glass = outside },
                new GlassCullingParentIndex.Candidate { Lod = nestedLod, Glass = nestedGlass },
                new GlassCullingParentIndex.Candidate { Lod = orphanLod, Glass = orphanGlass } };
            var controllers = new Dictionary<Transform, PerfectCullingCrossSceneGroup> { [root.transform] = controller, [nested.transform] = nestedController };
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>> { [frame] = new HashSet<PerfectCullingBakeGroup> { group } };
            var index = new GlassCullingParentIndex();
            int steps = 0;
            foreach (var step in index.Build(candidates, controllers, owners)) steps++;
            Check(steps > 10, "Parent discovery yields throughout indexing and binding");
            Check(candidates[0].Match != null && candidates[0].Match.Root == root.transform, "Glass can inherit whole native building parent");
            Check(candidates[1].Match == null, "Parent fallback does not attach glass outside reference bounds");
            Check(candidates[2].Match == null, "Parent search stops at nested native asset boundary");
            Check(candidates[3].Match == null, "Parent search requires actual native ancestor");
            var parent = candidates[0].Match;
            var family = new GlassCullingFamily(lod, Array.Empty<Renderer>(), new[] { glass }, Array.Empty<PerfectCullingBakeGroup>(), root.transform, "parent", parent);
            parent.Families.Add(family); family.Apply();
            Check(glass.forceRenderingOff, "Parent reference initially off suppresses glass");
            family.Restore(); Check(!glass.forceRenderingOff, "Parent family restoration releases its owned flag");
            family.Apply(); frame.transform.localPosition = Vector3.right; index.Audit();
            Check(!glass.forceRenderingOff, "Moved reference retires cached parent bounds instead of hiding glass");
            frame.transform.localPosition = Vector3.zero; family.Apply();
            Check(!glass.forceRenderingOff, "Retired parent does not restart without discovery");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(orphan); }
    }

    private static string SharedGroupScale(int count, Material glassMaterial, Material opaqueMaterial)
    {
        var root = new GameObject("many glass sections sharing a native group");
        try
        {
            var candidates = new List<GlassCullingParentIndex.Candidate>();
            var bodies = new Renderer[count];
            for (int i = 0; i < count; i++)
            {
                var part = new GameObject("section"); part.transform.SetParent(root.transform, false);
                part.transform.localPosition = Vector3.right * (i * 8);
                bodies[i] = Mesh(part.transform, "native frame", opaqueMaterial, Vector3.one * 4);
                var glass = Mesh(part.transform, "glass", glassMaterial, Vector3.one);
                var lod = part.AddComponent<LODGroup>(); lod.SetLODs(new[] { new LOD(.01f, new[] { glass }) });
                candidates.Add(new GlassCullingParentIndex.Candidate { Lod = lod, Glass = glass });
            }
            var group = new PerfectCullingBakeGroup { renderers = bodies }; group.Init();
            var controller = root.AddComponent<PerfectCullingCrossSceneGroup>(); controller.bakeGroups = new[] { group };
            var owners = new Dictionary<Renderer, HashSet<PerfectCullingBakeGroup>>();
            foreach (var body in bodies) owners.Add(body, new HashSet<PerfectCullingBakeGroup> { group });
            var index = new GlassCullingParentIndex(); long steps = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var step in index.Build(candidates, new Dictionary<Transform, PerfectCullingCrossSceneGroup> { { root.transform, controller } }, owners)) steps++;
            watch.Stop();
            Check(index.ScopeCount == count && candidates.TrueForAll(c => c.Match != null), "Scale: all glass sections associate");
            Check(index.IndexedGroupCount == 1 && index.GroupMemberVisits == count, "Scale: native members indexed once across all sections");
            Check(steps < count * 40, "Scale: shared-group discovery work stays linear");
            foreach (var candidate in candidates)
            {
                var family = new GlassCullingFamily(candidate.Lod, Array.Empty<Renderer>(), new[] { candidate.Glass },
                    new[] { group }, candidate.Match.Root, "parent", candidate.Match);
                candidate.Match.Families.Add(family); family.Apply();
            }
            group.Toggle(false); index.GroupChanged(group);
            Check(candidates.TrueForAll(c => c.Glass.forceRenderingOff), "Scale: shared native hide reaches every glass section");
            bodies[count - 1].enabled = true; index.GroupChanged(group);
            Check(candidates.TrueForAll(c => !c.Glass.forceRenderingOff), "Scale: available outside member releases every glass section");
            return $"sections={count}, members={count}, iteratorSteps={steps}, resolverCpuMs={watch.Elapsed.TotalMilliseconds:F2}";
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void NativeStartup(Material glassMaterial, Material opaqueMaterial)
    {
        var root = new GameObject("native startup fixture");
        GlassCullingIntegration feature = null;
        try
        {
            var game = root.AddComponent<LocalGame>();
            var world = root.AddComponent<GameWorld>();
            Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
            game.Status = GameStatus.Running;
            var lod = root.AddComponent<LODGroup>();
            var frame = Mesh(root.transform, "frame", opaqueMaterial, Vector3.one * 3);
            var glass = Mesh(root.transform, "glass", glassMaterial, Vector3.one);
            lod.SetLODs(new[] { new LOD(.01f, new[] { frame, glass }) });
            var group = new PerfectCullingBakeGroup { renderers = new[] { frame } }; group.Init(); group.Toggle(false);
            root.AddComponent<PerfectCullingCrossSceneGroup>().bakeGroups = new[] { group };
            var content = root.AddComponent<PerfectCullingCrossSceneContentMeshes>();
            content.Windows = new[] { glass }; content.BakeGroups = new[] { group };
            Plugin.GlassCullingEnabled.Value = true;
            feature = new GlassCullingIntegration(); feature.Initialize(); feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "World and Running status alone do not start discovery");
            var player = root.AddComponent<Player>(); player.GameWorld = world;
            feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Player creation alone waits for owner initialization");
            var owner = root.AddComponent<EftGamePlayerOwner>(); game.PlayerOwner = owner;
            feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Owner without player does not start discovery");
            owner.Player = player; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff && game.Status == GameStatus.Running, "Native startup automatically discovers before gameplay Started without a toggle");
            var matches = typeof(GlassCullingIntegration).GetField("families", BindingFlags.Instance | BindingFlags.NonPublic);
            var family = ((List<GlassCullingFamily>)matches.GetValue(feature))[0];
            game.Status = GameStatus.Started; feature.Update();
            Check(ReferenceEquals(family, ((List<GlassCullingFamily>)matches.GetValue(feature))[0]), "Later Started status does not discard loading-screen associations");
            foreach (var location in new[] { "bigmap", "woods", "factory4_day", "factory4_night", "interchange", "shoreline", "rezervbase", "laboratory", "lighthouse", "sandbox", "sandbox_high", "hideout", "", null })
            {
                world.LocationId = location; feature.Update(); Discover(feature);
                Check(!glass.forceRenderingOff, "Non-Streets or unknown location restores glass: " + location);
                Check(((List<GlassCullingFamily>)matches.GetValue(feature)).Count == 0,
                    "Non-Streets or unknown location prevents associations: " + location);
                group.Toggle(true); group.Toggle(false);
                Check(!glass.forceRenderingOff, "Native culling hooks stay inactive outside Streets: " + location);
            }
            world.LocationId = "TarkovStreets"; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Streets location comparison ignores casing and rediscovers after a location change");
            game.Status = GameStatus.Stopping; feature.Update();
            Check(!glass.forceRenderingOff, "Raid stop restores startup-created associations");
            game.PlayerOwner = null; game.Status = GameStatus.Running; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "New initialization waits for the new owner");
            Plugin.GlassCullingEnabled.Value = false; game.PlayerOwner = owner; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Native startup honors a disabled setting");
            Plugin.GlassCullingEnabled.Value = true; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Enabling after player readiness needs no startup event");
            var replacement = new GameObject("replacement world"); replacement.transform.SetParent(root.transform);
            Singleton<GameWorld>.Instance = replacement.AddComponent<GameWorld>(); feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Player from the old world cannot start the replacement world scan");
            player.GameWorld = Singleton<GameWorld>.Instance; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Replacement world scans once its own player is ready");
            game.GameType = EGameType.Hideout; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Native setup in hideout stays excluded");
            game.GameType = EGameType.Offline; game.Status = GameStatus.SoftStopping; feature.Update();
            Discover(feature);
            Check(!glass.forceRenderingOff, "Soft stopping restores glass despite a retained owner");
            game.Status = GameStatus.Stopped; feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Stopped status excludes a retained owner");
            game.Status = GameStatus.Starting; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Starting status retains native player readiness");
            game.Status = GameStatus.Started; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Already-started native raid works without a startup event");
            feature.Dispose(); feature = new GlassCullingIntegration(); feature.Initialize();
            game.Status = GameStatus.Running; feature.Update(); Discover(feature);
            Check(glass.forceRenderingOff, "Late feature initialization detects an already-ready loading screen");
            Object.DestroyImmediate(owner); feature.Update(); Discover(feature);
            Check(!glass.forceRenderingOff, "Destroyed owner cancels discovery and restores glass");
        }
        finally
        {
            feature?.Dispose(); Object.DestroyImmediate(root);
            Singleton<AbstractGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        }
    }
}
