using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine.Rendering;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    // The reviewed EFT 40743 ambient shader keeps native reflection math and changes
    // only diffuse/fill occlusion and optional directional SH. EFT owns the materials and draw scheduling.
    internal sealed class AmbientOcclusionBridge : IDisposable
    {
        private const string ShaderHash = "D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C";
        private static AmbientOcclusionBridge instance;
        private readonly Shader shader;
        private readonly OcclusionSettings settings;
        private readonly Func<Camera,bool> eligible;
        private readonly Func<Camera,RenderTexture> debugTarget;
        private readonly Func<Camera,FoliageDiagnostics> foliageTarget;
        private readonly Func<Camera,FoliageLive> liveTarget;
        private readonly Dictionary<Material,Shader> originals = new Dictionary<Material,Shader>();
        private readonly List<Material> dead = new List<Material>();
        private FieldInfo materialField;
        private Harmony harmony;
        internal bool Ready { get; private set; }
        internal AmbientOcclusionBridge(Shader shader, OcclusionSettings settings, Func<Camera,bool> eligible, Func<Camera,RenderTexture> debugTarget = null, Func<Camera,FoliageDiagnostics> foliageTarget = null, Func<Camera,FoliageLive> liveTarget = null)
        { this.shader = shader; this.settings = settings; this.eligible = eligible; this.debugTarget = debugTarget; this.foliageTarget = foliageTarget; this.liveTarget = liveTarget; }
        internal void Initialize()
        {
            try
            {
                if(shader == null || !shader.isSupported) throw new InvalidOperationException("Ambient shader unavailable.");
                using(var file = File.OpenRead(Path.Combine(Application.dataPath,"StreamingAssets","Windows","shaders")))
                using(var sha = SHA256.Create())
                    if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","") != ShaderHash)
                        throw new InvalidOperationException("Unrecognized EFT shader bundle; CustomAmbient fix requires EFT 40743.");
                materialField = AccessTools.Field(typeof(AmbientLight),"_screenAmbientMaterial") ?? throw new MissingFieldException("AmbientLight._screenAmbientMaterial");
                var method = AccessTools.Method(typeof(AmbientLight),"InitAmbientBuffer",new[] {typeof(AmbientLight.CamSettings),typeof(Camera)})
                    ?? throw new MissingMethodException("AmbientLight.InitAmbientBuffer");
                harmony = new Harmony(Plugin.Guid+".occlusion-ambient");
                instance = this;
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(AmbientOcclusionBridge),nameof(BeforeAmbient)),transpiler:new HarmonyMethod(typeof(AmbientOcclusionBridge),nameof(InstrumentDraw)) { priority = Priority.First, before = new[] { "com.matsix.customambient.research" } });
                Ready = true;
            }
            catch(Exception error) { Plugin.MyLog.LogWarning("CustomAmbient fix unavailable; native ambient retained. "+error.Message); Dispose(); }
        }
        private static void BeforeAmbient(AmbientLight __instance,Camera __1)
        {
            var self = instance; if(self == null) return;
            try
            {
                var material = self.materialField.GetValue(__instance) as Material;
                if(material == null) return;
                if(!self.originals.TryGetValue(material,out var original))
                {
                    if(material.shader == null || material.shader.name != "Hidden/WriteScreenAmbient") return;
                    original = material.shader; self.originals.Add(material,original);
                }
                // Never overwrite a later shader substitution by another mod.
                if(material.shader != original && material.shader != self.shader) return;
                if(self.eligible(__1))
                {
                    material.shader = self.shader;
                    if(self.settings.LightingDebug) material.EnableKeyword("FTFX_BENT_DIAGNOSTICS");
                    else material.DisableKeyword("FTFX_BENT_DIAGNOSTICS");
                    if(self.settings.FoliageCapture && self.foliageTarget?.Invoke(__1) != null) material.EnableKeyword("FTFX_FOLIAGE_CAPTURE");
                    else material.DisableKeyword("FTFX_FOLIAGE_CAPTURE");
                    if(self.settings.BentNormals.Value) material.EnableKeyword("FTFX_BENT_NORMALS");
                    else material.DisableKeyword("FTFX_BENT_NORMALS");
                    material.SetFloat("_FTFX_CAAmount",self.settings.AmbientStrength.Value);
                    material.SetFloat("_FTFX_CA_FoliageStrength",self.settings.Foliage.Value);
                }
                else { material.DisableKeyword("FTFX_FOLIAGE_CAPTURE"); material.DisableKeyword("FTFX_BENT_DIAGNOSTICS"); material.DisableKeyword("FTFX_BENT_NORMALS"); material.shader = original; }
            }
            catch(Exception error) { Plugin.MyLog.LogError("CustomAmbient fix failed; native shader restored. "+error); self.Dispose(); }
        }
        // Surround, rather than replace, the native call so research draw wrappers still see it.
        private static IEnumerable<CodeInstruction> InstrumentDraw(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var draw = AccessTools.Method(typeof(CommandBuffer),nameof(CommandBuffer.DrawMesh),new[] {typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int),typeof(MaterialPropertyBlock)});
            var buffer = AccessTools.Field(typeof(AmbientLight.CamSettings),"CbAmbient");
            var material = AccessTools.Field(typeof(AmbientLight),"_screenAmbientMaterial");
            var captured = generator.DeclareLocal(typeof(bool));
            int count = 0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(draw))
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_1);
                    first.MoveLabelsFrom(instruction);
                    first.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return first;
                    yield return new CodeInstruction(OpCodes.Ldfld,buffer);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld,material);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(AmbientOcclusionBridge),nameof(BeginDebugDraw)));
                    yield return new CodeInstruction(OpCodes.Stloc,captured);
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Ldloc,captured);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldfld,buffer);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(AmbientOcclusionBridge),nameof(EndDebugDraw)));
                    count++;
                }
                else yield return instruction;
            }
            if(count != 1) throw new InvalidOperationException("Unsupported CustomAmbient draw layout.");
        }
        private static bool BeginDebugDraw(CommandBuffer cb, Camera camera, Material material)
        {
            var self = instance;
            var live = self != null && self.settings.FoliageLive && material.shader == self.shader ? self.liveTarget?.Invoke(camera) : null;
            live?.BeforeAmbient(cb);
            var target = self != null && self.settings.LightingDebug && material.shader == self.shader ? self.debugTarget?.Invoke(camera) : null;
            if(target == null || !target.IsCreated()) return live != null;
            if(self.settings.FoliageCapture)
            {
                var foliage = self.foliageTarget?.Invoke(camera);
                if(foliage == null) return false;
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
            if(instance != null && instance.settings.FoliageLive) { instance.liveTarget?.Invoke(camera)?.AfterAmbient(cb); return; }
            cb.ClearRandomWriteTargets();
            cb.SetGlobalFloat("_FTFX_CA_DebugAvailable",0);
            if(instance != null && instance.settings.FoliageCapture) instance.foliageTarget?.Invoke(camera)?.AfterAmbient(cb);
        }
        internal void Restore()
        {
            foreach(var pair in originals)
                if(pair.Key != null && pair.Key.shader == shader && pair.Value != null) { pair.Key.DisableKeyword("FTFX_FOLIAGE_CAPTURE"); pair.Key.DisableKeyword("FTFX_BENT_DIAGNOSTICS"); pair.Key.DisableKeyword("FTFX_BENT_NORMALS"); pair.Key.shader = pair.Value; }
            originals.Clear();
        }
        internal void Prune()
        {
            dead.Clear(); foreach(var pair in originals) if(pair.Key == null) dead.Add(pair.Key);
            foreach(var material in dead) originals.Remove(material);
            dead.Clear();
        }
        public void Dispose() { Ready = false; harmony?.UnpatchSelf(); harmony = null; Restore(); if(instance == this) instance = null; }
    }
}
