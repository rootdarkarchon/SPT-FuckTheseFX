using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine.Rendering;
using HarmonyLib;
using UnityEngine;
using FuckTheseFX.Source.StochasticSSR;

namespace FuckTheseFX.Source.Occlusion
{
    // The reviewed EFT 40743 ambient shader keeps native reflection math and changes
    // only diffuse/fill occlusion and optional directional SH. EFT owns the native material and draw scheduling.
    internal sealed class AmbientOcclusionBridge : IDisposable
    {
        private const string NativeShaderName = "Hidden/WriteScreenAmbient";
        private static AmbientOcclusionBridge instance;
        private readonly Shader shader;
        private readonly OcclusionSettings settings;
        private readonly Func<Camera,bool> eligible;
        private readonly Func<Camera,RenderTexture> debugTarget;
        private readonly Func<Camera,FoliageDiagnostics> foliageTarget;
        private readonly Func<Camera,FoliageLive> liveTarget;
        private readonly Dictionary<(Material Native,Camera Camera),Material> variants = new Dictionary<(Material,Camera),Material>();
        private readonly List<(Material,Camera)> dead = new List<(Material,Camera)>();
        private Shader nativeShader;
        private Harmony harmony;
        internal bool Ready { get; private set; }
        internal AmbientOcclusionBridge(Shader shader, OcclusionSettings settings, Func<Camera,bool> eligible, Func<Camera,RenderTexture> debugTarget = null, Func<Camera,FoliageDiagnostics> foliageTarget = null, Func<Camera,FoliageLive> liveTarget = null)
        { this.shader = shader; this.settings = settings; this.eligible = eligible; this.debugTarget = debugTarget; this.foliageTarget = foliageTarget; this.liveTarget = liveTarget; }
        internal void Initialize()
        {
            try
            {
                if(shader == null || !shader.isSupported) throw new InvalidOperationException("Ambient shader unavailable.");
                if(typeof(AmbientLight).Module.ModuleVersionId != new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7"))
                    throw new InvalidOperationException("Unsupported EFT ambient ABI; expected EFT 40743.");
                var method = AccessTools.Method(typeof(AmbientLight),"InitAmbientBuffer",new[] {typeof(AmbientLight.CamSettings),typeof(Camera)})
                    ?? throw new MissingMethodException("AmbientLight.InitAmbientBuffer");
                harmony = new Harmony(Plugin.Guid+".occlusion-ambient");
                instance = this;
                harmony.Patch(method,transpiler:new HarmonyMethod(typeof(AmbientOcclusionBridge),nameof(InstrumentDraw)) { priority = Priority.First, before = new[] { "com.matsix.customambient.research" } });
                Ready = true;
            }
            catch(Exception error) { Plugin.MyLog.LogWarning("CustomAmbient fix unavailable; native ambient retained. "+error.Message); Dispose(); }
        }
        // AmbientLight records every camera's CbAmbient with one shared material, and a command buffer
        // resolves that material's shader when it executes. Each eligible camera therefore draws with its
        // own variant and the native material is never modified: re-recording another camera (an optic RT
        // resize re-records at render time) cannot switch the main camera back to the native shader.
        private static Material SelectMaterial(Material native, Camera camera)
        {
            var self = instance;
            if(self == null || native == null || camera == null) return native;
            try
            {
                var current = native.shader;
                if(self.nativeShader == null)
                {
                    if(current == null || current.name != NativeShaderName) return native;
                    self.nativeShader = current;
                }
                // Never override another mod's substitution of the native shader.
                else if(current != self.nativeShader) return native;
                if(!self.eligible(camera)) return native;
                var key = (native,camera);
                if(!self.variants.TryGetValue(key,out var variant) || variant == null)
                {
                    // EFT sets no material properties after creation; per-draw values use its property block and globals.
                    // Unity may reclaim a variant only after Dispose drops it, when EFT has re-recorded every buffer.
                    variant = new Material(native) { name = native.name+" (FTFX "+camera.name+")", hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset };
                    self.variants[key] = variant;
                }
                if(variant.shader != self.shader) variant.shader = self.shader;
                if(AmbientSurfaceCapture.Enabled) variant.EnableKeyword("FTFX_SURFACE_CAPTURE");
                else variant.DisableKeyword("FTFX_SURFACE_CAPTURE");
                if(self.settings.LightingDebug) variant.EnableKeyword("FTFX_BENT_DIAGNOSTICS");
                else variant.DisableKeyword("FTFX_BENT_DIAGNOSTICS");
                if(self.settings.FoliageCapture && self.foliageTarget?.Invoke(camera) != null) variant.EnableKeyword("FTFX_FOLIAGE_CAPTURE");
                else variant.DisableKeyword("FTFX_FOLIAGE_CAPTURE");
                if(self.settings.BentNormals.Value) variant.EnableKeyword("FTFX_BENT_NORMALS");
                else variant.DisableKeyword("FTFX_BENT_NORMALS");
                variant.SetFloat("_FTFX_CAAmount",self.settings.AmbientStrengthFraction);
                variant.SetFloat("_FTFX_CA_FoliageStrength",self.settings.FoliageFraction);
                return variant;
            }
            catch(Exception error) { Plugin.MyLog.LogError("CustomAmbient fix failed; native shader restored. "+error); self.Dispose(); return native; }
        }
        // Select the camera's material and surround, rather than replace, the native call so research draw wrappers still see it.
        private static IEnumerable<CodeInstruction> InstrumentDraw(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var draw = AccessTools.Method(typeof(CommandBuffer),nameof(CommandBuffer.DrawMesh),new[] {typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int),typeof(MaterialPropertyBlock)});
            var buffer = AccessTools.Field(typeof(AmbientLight.CamSettings),"CbAmbient");
            var material = AccessTools.Field(typeof(AmbientLight),"_screenAmbientMaterial") ?? throw new MissingFieldException("AmbientLight._screenAmbientMaterial");
            var codes = new List<CodeInstruction>(instructions);
            int drawIndex = -1, count = 0;
            for(int i = 0; i < codes.Count; i++) if(codes[i].Calls(draw)) { drawIndex = i; count++; }
            if(count != 1) throw new InvalidOperationException("Unsupported CustomAmbient draw layout.");
            int materialIndex = codes.FindLastIndex(drawIndex,drawIndex+1,code => code.LoadsField(material));
            if(materialIndex < 0) throw new InvalidOperationException("Unsupported CustomAmbient draw material.");
            var selected = generator.DeclareLocal(typeof(Material));
            var captured = generator.DeclareLocal(typeof(bool));
            codes.InsertRange(materialIndex+1,new[]
            {
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(AmbientOcclusionBridge),nameof(SelectMaterial))),
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Stloc,selected)
            });
            drawIndex += 4;
            var instruction = codes[drawIndex];
            var first = new CodeInstruction(OpCodes.Ldarg_1);
            first.MoveLabelsFrom(instruction);
            first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
            codes.InsertRange(drawIndex,new[]
            {
                first,
                new CodeInstruction(OpCodes.Ldfld,buffer),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldloc,selected),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(AmbientOcclusionBridge),nameof(BeginDebugDraw))),
                new CodeInstruction(OpCodes.Stloc,captured)
            });
            codes.InsertRange(drawIndex+7,new[]
            {
                new CodeInstruction(OpCodes.Ldloc,captured),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldfld,buffer),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(AmbientOcclusionBridge),nameof(EndDebugDraw)))
            });
            return codes;
        }
        private static bool BeginDebugDraw(CommandBuffer cb, Camera camera, Material material)
        {
            var self = instance;
            bool surface=AmbientSurfaceCapture.Begin(cb,camera,self!=null && material.shader==self.shader);
            var live = self != null && self.settings.FoliageLive && material.shader == self.shader ? self.liveTarget?.Invoke(camera) : null;
            live?.BeforeAmbient(cb);
            var target = self != null && self.settings.LightingDebug && material.shader == self.shader ? self.debugTarget?.Invoke(camera) : null;
            if(target == null || !target.IsCreated()) return surface || live != null;
            if(self.settings.FoliageCapture)
            {
                var foliage = self.foliageTarget?.Invoke(camera);
                if(foliage == null) return surface;
                foliage.BeforeAmbient(cb);
            }
            cb.SetGlobalVector("_FTFX_CA_DebugInfo",new Vector4((int)self.settings.View.Value,self.settings.LightingDebugGain,target.width,target.height));
            cb.SetGlobalFloat("_FTFX_CA_DebugAvailable",1);
            cb.SetRandomWriteTarget(1,target);
            return true;
        }
        private static void EndDebugDraw(bool captured, CommandBuffer cb, Camera camera)
        {
            if(!captured) return;
            cb.ClearRandomWriteTargets();
            cb.SetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled",0);
            if(instance != null && instance.settings.FoliageLive) { instance.liveTarget?.Invoke(camera)?.AfterAmbient(cb); return; }
            cb.SetGlobalFloat("_FTFX_CA_DebugAvailable",0);
            if(instance != null && instance.settings.FoliageCapture) instance.foliageTarget?.Invoke(camera)?.AfterAmbient(cb);
        }
        // Reverted variants match the native material until their camera is eligible again on the next record.
        internal void Restore()
        {
            foreach(var pair in variants) Revert(pair.Value);
        }
        internal void Restore(Camera camera)
        {
            foreach(var pair in variants) if(pair.Key.Camera == camera) Revert(pair.Value);
        }
        private void Revert(Material variant)
        {
            if(variant == null || nativeShader == null || variant.shader != shader) return;
            variant.DisableKeyword("FTFX_SURFACE_CAPTURE"); variant.DisableKeyword("FTFX_FOLIAGE_CAPTURE"); variant.DisableKeyword("FTFX_BENT_DIAGNOSTICS"); variant.DisableKeyword("FTFX_BENT_NORMALS");
            variant.shader = nativeShader;
        }
        // A destroyed camera or AmbientLight has already disposed the buffers that referenced the variant.
        internal void Prune()
        {
            dead.Clear();
            foreach(var pair in variants) if(pair.Key.Native == null || pair.Key.Camera == null || pair.Value == null) dead.Add(pair.Key);
            foreach(var key in dead) { if(variants[key] != null) UnityEngine.Object.Destroy(variants[key]); variants.Remove(key); }
            dead.Clear();
        }
        public void Dispose()
        {
            Ready = false; harmony?.UnpatchSelf(); harmony = null;
            // Live buffers still reference reverted variants until EFT re-records them; Unity reclaims them afterwards.
            Restore(); variants.Clear();
            if(instance == this) instance = null;
        }
    }
}
