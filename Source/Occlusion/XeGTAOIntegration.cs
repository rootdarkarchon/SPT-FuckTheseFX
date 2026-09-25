using System;
using System.Collections.Generic;
using EFT.CameraControl;
using EFT.Settings.Graphics;
using HarmonyLib;
using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    // EFT continues to select/configure native AO. We suspend it only after our camera
    // resources and commands are valid, and retain its exact enabled states for fallback.
    internal sealed class XeGTAOIntegration : IDisposable
    {
        private sealed class Binding
        {
            internal CameraManager Manager;
            internal Camera Camera;
            internal ESSAOMode Mode;
            internal bool Hbao, Cinematic, Failed;
            internal CameraOcclusion Effect;
            internal void Restore()
            {
                Effect?.Dispose(); Effect = null;
                if(Manager == null) return;
                if(Manager._hbao != null) Manager._hbao.enabled = Hbao;
                if(Manager._ambientOcclusion != null) Manager._ambientOcclusion.enabled = Cinematic;
            }
        }
        private static XeGTAOIntegration instance;
        private readonly string pluginPath;
        private readonly OcclusionSettings settings;
        private readonly Dictionary<CameraManager,Binding> bindings = new Dictionary<CameraManager,Binding>();
        private readonly List<CameraManager> dead = new List<CameraManager>();
        private GpuAssets assets;
        private AmbientOcclusionBridge ambient;
        private Harmony harmony;
        private bool previousEnabled;
        private float resumeAt;
        private int cleanupFrames;
        private RenderTexture diagnostic;

        internal XeGTAOIntegration(string path, OcclusionSettings settings) { pluginPath = path; this.settings = settings; }
        internal void Initialize()
        {
            try
            {
                assets = new GpuAssets(pluginPath);
                ambient = new AmbientOcclusionBridge(assets.Ambient,settings,EligibleAmbient,LightingDebugTarget);
                ambient.Initialize();
                var target = AccessTools.DeclaredMethod(typeof(CameraManager),nameof(CameraManager.SetSSAO),new[] {typeof(ESSAOMode)});
                if(target == null || target.ReturnType != typeof(void)) throw new MissingMethodException("CameraManager.SetSSAO");
                instance = this;
                harmony = new Harmony(Plugin.Guid+".xegtao");
                harmony.Patch(target,prefix:new HarmonyMethod(typeof(XeGTAOIntegration),nameof(BeforeSettings)),postfix:new HarmonyMethod(typeof(XeGTAOIntegration),nameof(AfterSettings)));
                var raidStart = AccessTools.Method(typeof(EFT.UI.PreloaderUI),nameof(EFT.UI.PreloaderUI.ShowRaidStartInfo));
                if(raidStart != null) harmony.Patch(raidStart,postfix:new HarmonyMethod(typeof(XeGTAOIntegration),nameof(RaidStart)));
                previousEnabled = settings.Enabled.Value;
                Camera.onPreCull += BeforeCamera;
                Plugin.MyLog.LogInfo("Independent XeGTAO ready. Native AO is retained for Off and unsupported cameras.");
            }
            catch { Dispose(); throw; }
        }
        private static void RaidStart() { if(instance != null) instance.resumeAt = Time.unscaledTime+3; }
        private static void BeforeSettings(CameraManager __instance)
        {
            if(instance != null && instance.bindings.TryGetValue(__instance,out var existing))
            { existing.Restore(); instance.bindings.Remove(__instance); }
        }
        private static void AfterSettings(CameraManager __instance, ESSAOMode __0)
        {
            if(instance == null || __instance.Camera == null) return;
            instance.bindings[__instance] = new Binding { Manager = __instance, Camera = __instance.Camera, Mode = __0,
                Hbao = __instance._hbao != null && __instance._hbao.enabled,
                Cinematic = __instance._ambientOcclusion != null && __instance._ambientOcclusion.enabled };
        }
        private RenderTexture LightingDebugTarget(Camera camera)
        {
            foreach(var binding in bindings.Values)
                if(binding.Camera == camera && binding.Effect != null && binding.Effect.Active) return binding.Effect.LightingDebugTexture;
            return null;
        }
        private bool EligibleAmbient(Camera camera)
        {
            foreach(var binding in bindings.Values)
                if(binding.Camera == camera && binding.Effect != null && binding.Effect.Active && binding.Effect.AmbientActive) return true;
            return false;
        }
        private void BeforeCamera(Camera camera)
        {
            // Neutral globals for untracked/reflection cameras as well as normal cameras.
            CameraOcclusion.ResetGlobals();
            foreach(var binding in bindings.Values)
            {
                if(binding.Camera != camera) continue;
                bool enabled = settings.Enabled.Value && binding.Mode != ESSAOMode.Off && !binding.Failed &&
                    Time.unscaledTime >= resumeAt && GpuAssets.Supports(camera);
                if(!enabled) { binding.Restore(); ambient.Restore(); continue; }
                try
                {
                    if(binding.Effect == null) binding.Effect = new CameraOcclusion(camera,assets);
                    binding.Effect.Record(settings,settings.Quality.Value,ambient.Ready);
                    if(binding.Manager._hbao != null) binding.Manager._hbao.enabled = false;
                    if(binding.Manager._ambientOcclusion != null) binding.Manager._ambientOcclusion.enabled = false;
                    diagnostic = binding.Effect.DebugTexture;
                }
                catch(Exception error)
                {
                    binding.Restore(); binding.Failed = true; ambient.Restore(); diagnostic = null;
                    Plugin.MyLog.LogError("XeGTAO camera failed; native AO restored. Toggle XeGTAO off/on to retry. "+error);
                }
            }
        }
        internal void Update()
        {
            if(instance != this) return;
            if(previousEnabled != settings.Enabled.Value)
            {
                previousEnabled = settings.Enabled.Value;
                foreach(var binding in bindings.Values) { binding.Restore(); binding.Failed = false; }
                ambient.Restore(); diagnostic = null;
            }
            if(!settings.Ambient.Value || settings.AmbientStrength.Value <= 0) ambient.Restore();
            if(++cleanupFrames >= 300) { cleanupFrames = 0; ambient.Prune(); }
            dead.Clear();
            foreach(var pair in bindings) if(pair.Key == null || pair.Value.Camera == null) dead.Add(pair.Key);
            foreach(var key in dead) { bindings[key].Restore(); bindings.Remove(key); }
        }
        internal void DrawDiagnostic()
        {
            if(Event.current.type != EventType.Repaint || !settings.Enabled.Value || settings.View.Value == OcclusionView.Normal || diagnostic == null) return;
            int old = GUI.depth; GUI.depth = 1000;
            var color = GUI.color; GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),diagnostic,ScaleMode.StretchToFill,false);
            if(settings.LightingDebug)
            {
                string stage, legend;
                if(settings.BentDifferenceDebug)
                {
                    stage = settings.View.Value == OcclusionView.BentSHDifference ? "SH difference before interior blend" : "Diffuse lighting difference after interior blend, albedo and AO";
                    stage += " | gain "+settings.LightingDebugGain.ToString("0.#")+"x";
                    legend = "Black: zero; brighter: larger difference. Red: bent shader off; magenta: missing inputs. Requires Bent normals.";
                }
                else if(settings.View.Value == OcclusionView.AmbientShares)
                {
                    stage = "Ambient shares (relative luminance; gain ignored)";
                    legend = "Red: directional SH; green: upward SH; blue: fill. Mixed colors: mixed contributions. Black: no diffuse/fill light.";
                }
                else if(settings.View.Value == OcclusionView.AmbientInteriorTransmission)
                {
                    stage = "Directional SH transmission through interior blend and visibility (gain ignored)";
                    legend = "Black: 0%; white: 100%. Excludes material albedo and AO. Dark areas suppress the bent-normal SH difference.";
                }
                else
                {
                    stage = settings.View.Value+" contribution after blend, albedo and AO | gain "+settings.LightingDebugGain.ToString("0.#")+"x";
                    legend = "Black: zero contribution. Same gain across all three component views; higher gain can clip bright values.";
                }
                GUI.Box(new Rect(15,15,1050,70),stage+"\n"+legend+"\nCyan: no CustomAmbient capture. Requires XeGTAO + CustomAmbient fix. Excludes reflections/direct lights. Diagnostic view adds GPU work.");
            }
            GUI.depth = old; GUI.color = color;
        }
        public void Dispose()
        {
            Camera.onPreCull -= BeforeCamera;
            harmony?.UnpatchSelf(); harmony = null;
            foreach(var binding in bindings.Values) binding.Restore(); bindings.Clear();
            ambient?.Dispose(); ambient = null;
            assets?.Dispose(); assets = null; diagnostic = null;
            if(instance == this) instance = null;
            CameraOcclusion.ResetGlobals();
        }
    }
}
