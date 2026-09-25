using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using FuckTheseFX.Source.DistantShadows;

namespace FuckTheseFX
{
    public sealed class FixtureValue<T> { public T Value; public FixtureValue(T value) { Value = value; } }
    public sealed class FixtureLogger
    {
        public void LogInfo(object text) { Debug.Log(text); }
        public void LogWarning(object text) { Debug.LogWarning(text); }
        public void LogError(object text) { Debug.LogError(text); }
    }
    public static class Plugin
    {
        public static FixtureLogger MyLog = new FixtureLogger();
        public static FixtureValue<bool> AmbientHighlightEnabled = new FixtureValue<bool>(true);
        public static FixtureValue<bool> DistantShadowsEnabled = new FixtureValue<bool>(true);
        public static FixtureValue<bool> DistantShadowSamplingEnabled = new FixtureValue<bool>(true);
        public static FixtureValue<DistantShadowMaskResolution> DistantShadowResolution = new FixtureValue<DistantShadowMaskResolution>(DistantShadowMaskResolution.Half);
        public static FixtureValue<int> DistantShadowQuarterSamples = new FixtureValue<int>(7),
            DistantShadowHalfSamples = new FixtureValue<int>(12), DistantShadowFullSamples = new FixtureValue<int>(8);
    }
}

public static class DistantShadowHookFixture
{
    public static void Run(Camera camera, Mesh quad, Material native, Shader replacement)
    {
        AmbientHighlightFixture.Run(camera);
        var owner = camera.gameObject.AddComponent<DistantShadow>();
        owner.MasterCamera = camera; owner.mesh = quad; owner.ComputeShadowMaskMaterial = native;
        CheckResolution(owner);
        var integration = new DistantShadowSamplingIntegration();
        var type = typeof(DistantShadowSamplingIntegration);
        void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).SetValue(integration, value);
        var method = AccessTools.Method(typeof(DistantShadow), "SetupComputeShadowMaskCommandBuffer");
        var harmony = new Harmony("fuckthesefx.distantshadow.validation");
        Set("shader", replacement); Set("method", method); Set("harmony", harmony);
        Set("mainBuffer", AccessTools.Field(typeof(DistantShadow), "_computeMaskCmdBuf"));
        Set("opticBuffer", AccessTools.Field(typeof(DistantShadow), "_computeMaskOpticCmdBuf"));
        var taps = (Vector4[][])AccessTools.Field(type, "taps").GetValue(integration);
        for (int i = 1; i <= 25; i++) taps[i] = DistantShadowSamples.Taps(i);
        Set("active", integration);
        harmony.Patch(method, transpiler: new HarmonyMethod(type, "ReplaceMaskDraw"), postfix: new HarmonyMethod(type, "AfterRecording"));
        var select = AccessTools.Method(type, "SelectMaterial");
        using (var cb = new CommandBuffer())
        using (var optic = new CommandBuffer())
        using (var foreign = new CommandBuffer())
        {
            owner.SetBuffer(cb);
            owner.SetOpticBuffer(optic);
            Material Select(CommandBuffer buffer, DistantShadow.ResolutionState resolution) =>
                (Material)select.Invoke(null, new object[] { native, owner, resolution, buffer });
            void Check(bool ok, string message) { if (!ok) throw new Exception("Hook fixture: " + message); }
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = false;
            Check(Select(cb, DistantShadow.ResolutionState.FULL) == native, "Disabled adjustments preserve native filtering");
            Check(Select(optic, DistantShadow.ResolutionState.FULL) == native, "Disabled adjustments preserve native optic filtering");
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = true;
            var selected = Select(cb, DistantShadow.ResolutionState.FULL);
            Check(selected != native && selected.shader == replacement, "Reduced main draw uses replacement");
            Check(Select(optic, DistantShadow.ResolutionState.FULL).shader == replacement, "Optic buffer uses replacement");
            native.EnableKeyword("RENDER_FOR_SCOPE");
            Check(Select(optic, DistantShadow.ResolutionState.FULL).shader == replacement, "Scope keyword supported");
            native.DisableKeyword("RENDER_FOR_SCOPE");
            Check(Select(foreign, DistantShadow.ResolutionState.FULL) == native, "Unowned buffer retains native");
            FuckTheseFX.Plugin.DistantShadowSamplingEnabled.Value = false;
            Check(Select(cb, DistantShadow.ResolutionState.FULL) == native, "Disable restores native selection");
            FuckTheseFX.Plugin.DistantShadowSamplingEnabled.Value = true;
            FuckTheseFX.Plugin.DistantShadowFullSamples.Value = 25;
            Check(Select(cb, DistantShadow.ResolutionState.FULL) == native, "25 restores exact native selection");
            Check(Select(optic, DistantShadow.ResolutionState.FULL) == native, "25 restores exact native optic selection");
            FuckTheseFX.Plugin.DistantShadowFullSamples.Value = 8;
            owner.EnableAdaptiveBias = true;
            Check(Select(cb, DistantShadow.ResolutionState.FULL) == native, "Unsupported adaptive bias retains native");
            owner.EnableAdaptiveBias = false;
            foreach (DistantShadow.ResolutionState resolution in Enum.GetValues(typeof(DistantShadow.ResolutionState)))
            {
                cb.Clear();
                cb.DisableShaderKeyword("RENDER_FOR_SCOPE");
                owner.SetupComputeShadowMaskCommandBuffer(cb, 0, 1, Vector4.zero, Vector4.zero, new Rect(), new Rect(),
                    resolution, null, null, null, camera.targetTexture, null);
                int count = resolution == DistantShadow.ResolutionState.QUARTER ? 7 : resolution == DistantShadow.ResolutionState.HALF ? 12 : 8;
                Check(cb.sizeInBytes > 0, "Patched recording executes " + resolution);
                camera.AddCommandBuffer(CameraEvent.AfterEverything, cb); camera.Render(); camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                Check(Shader.GetGlobalInt("_FTFX_DSCount") == count, "Resolution-selected budget actually submitted " + resolution);
            }
            var scopeObject = new GameObject("Optic hook camera");
            var scopeCamera = scopeObject.AddComponent<Camera>();
            scopeCamera.fieldOfView = 18; scopeCamera.cullingMask = 0;
            scopeCamera.targetTexture = new RenderTexture(65, 39, 0, RenderTextureFormat.ARGBFloat);
            scopeCamera.targetTexture.Create();
            try
            {
                foreach (DistantShadow.ResolutionState resolution in Enum.GetValues(typeof(DistantShadow.ResolutionState)))
                {
                    optic.Clear(); optic.EnableShaderKeyword("RENDER_FOR_SCOPE");
                    owner.SetupComputeShadowMaskCommandBuffer(optic, 0, 1, Vector4.zero, Vector4.zero, new Rect(), new Rect(),
                        resolution, null, null, null, scopeCamera.targetTexture, null);
                    int count = resolution == DistantShadow.ResolutionState.QUARTER ? 7 : resolution == DistantShadow.ResolutionState.HALF ? 12 : 8;
                    // Execute the retained main buffer between recording and submitting optics.
                    camera.AddCommandBuffer(CameraEvent.AfterEverything, cb); camera.Render(); camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
                    scopeCamera.AddCommandBuffer(CameraEvent.AfterEverything, optic); scopeCamera.Render(); scopeCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, optic);
                    Check(Shader.GetGlobalInt("_FTFX_DSCount") == count, "Optic submits its own recorded resolution budget " + resolution);
                }
                integration.Dispose();
                Check(optic.sizeInBytes > 0, "Retained optic buffer rebuilt natively on disposal");
            }
            finally
            {
                var target = scopeCamera.targetTexture; scopeCamera.targetTexture = null;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(scopeObject);
                Shader.DisableKeyword("RENDER_FOR_SCOPE");
            }
            Check(native.shader.name == "EFT/DistantShadowComputeMask", "Native material ownership preserved");
            Check(Harmony.GetPatchInfo(method) == null || !Harmony.GetPatchInfo(method).Owners.Contains(harmony.Id), "Unpatch on disposal");
            Check(cb.sizeInBytes > 0, "Retained buffer rebuilt natively on disposal");
        }
        UnityEngine.Object.DestroyImmediate(owner);
        File.WriteAllText("hook-validation.txt", "PASSED production Harmony main/optic sampling at all resolutions, separate camera budget submission, resolution switching/restoration, native 25/off/unsupported/unowned fallbacks, material ownership and both buffer disposal restorations. Fixture ABI plus separately checked installed ABI; live EFT hook remains unverified.");
    }

    private static void CheckResolution(DistantShadow owner)
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception("Resolution fixture: " + message); }
        var state = new DistantShadowResolutionState();
        Check(state.Select(2, -1) == 2, "Follow preserves external full");
        Check(state.Select(2, 0) == 0 && state.Select(0, 1) == 1 && state.Select(1, -1) == 2, "Switching retains original external selection");
        Check(state.Select(2, 0) == 0 && state.Select(1, 0) == 0 && state.Select(0, -1) == 1, "Latest observed external selection restored");
        Check(state.Select(2, 0) == 0 && state.Select(1, -1) == 1, "Fresh external selection wins when releasing");
        Check(state.Select(2, 0) == 0 && state.Select(0, 99) == 2, "Invalid setting releases override");
        using (var resolution = new DistantShadowResolutionIntegration())
        {
            resolution.Initialize();
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = false;
            owner.CurrentMaskResolution = DistantShadow.ResolutionState.HALF;
            owner.Update();
            Check(owner.FixtureObservedResolution == DistantShadow.ResolutionState.HALF, "Disabled adjustments leave native Update input unchanged");
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = true;
            foreach (var value in new[] { DistantShadowMaskResolution.Full, DistantShadowMaskResolution.Quarter, DistantShadowMaskResolution.Half })
            {
                FuckTheseFX.Plugin.DistantShadowResolution.Value = value;
                owner.Update();
                Check((int)owner.FixtureObservedResolution == (int)value, "Override visible inside Update: " + value);
            }
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = true;
            FuckTheseFX.Plugin.DistantShadowResolution.Value = DistantShadowMaskResolution.Full;
            owner.Update();
            owner.CurrentMaskResolution = DistantShadow.ResolutionState.QUARTER;
            owner.Update();
            Check(owner.FixtureObservedResolution == DistantShadow.ResolutionState.FULL, "Explicit override wins external write");
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = false;
            owner.Update();
            Check(owner.FixtureObservedResolution == DistantShadow.ResolutionState.QUARTER, "Disabling restores observed external resolution");
            FuckTheseFX.Plugin.DistantShadowsEnabled.Value = true;
            FuckTheseFX.Plugin.DistantShadowResolution.Value = DistantShadowMaskResolution.Full;
            owner.Update();
        }
        Check(owner.CurrentMaskResolution == DistantShadow.ResolutionState.QUARTER, "Dispose restores external resolution");
        owner.Update();
        Check(owner.FixtureObservedResolution == DistantShadow.ResolutionState.QUARTER, "Dispose removes prefix");
        FuckTheseFX.Plugin.DistantShadowResolution.Value = DistantShadowMaskResolution.Half;
    }
}
