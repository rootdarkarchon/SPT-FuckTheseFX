using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using HarmonyLib;

namespace FuckTheseFX.Research
{
    [BepInPlugin("com.rootdarkarchon.ftfx.ssrprofiling", "FTFX SSR Profiling", "0.1.0")]
    [BepInDependency("com.matsix.customambient.research", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class ProfilingPlugin : BaseUnityPlugin
    {
        internal const string PostProcessingHash = "B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548";
        private Harmony harmony;
        private void Awake()
        {
            var mode = Config.Bind("Capture", "Mode", SsrProfilingMode.PerPass,
                "PerPass adds GPU timestamps around native SSR operations in CustomAmbient Research SceneGpu captures. NativeMarkersOnly measures the existing markers. Change between captures; each capture keeps its starting mode. No reflection settings are changed.");
            try
            {
                var renderer = AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.ScreenSpaceReflectionsRenderer")
                    ?? throw new TypeLoadException("Native SSR renderer missing");
                using (var file = File.OpenRead(renderer.Assembly.Location))
                using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != PostProcessingHash)
                        throw new InvalidOperationException("Unsupported post-processing assembly; SSR profiling refused.");
                var bridge = new ResearchBridge(AccessTools.TypeByName("CustomAmbientResearch.DeepGpuProfiler")
                    ?? throw new TypeLoadException("Research GPU profiler missing"), () => mode.Value, message => Logger.LogError(message));
                SsrPassHooks.SelectSink = bridge.Select;
                harmony = new Harmony(Info.Metadata.GUID);
                harmony.Patch(AccessTools.Method(renderer, "Render"),
                    prefix: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.BeforeRender)),
                    finalizer: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.AfterRender)),
                    transpiler: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.Transpile)));
                Logger.LogInfo("SSR GPU pass profiling ready. Use CustomAmbient Research > Capture > Mode = SceneGpu. No replacement SSR is installed.");
            }
            catch (Exception e) { Logger.LogError(e); Cleanup(); enabled = false; }
        }
        private void Cleanup() { harmony?.UnpatchSelf(); harmony = null; SsrPassHooks.SelectSink = null; }
        private void OnDestroy() => Cleanup();
    }
}
