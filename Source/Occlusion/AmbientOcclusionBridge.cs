using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    // The reviewed EFT 40743 ambient shader keeps native reflection math and changes
    // only diffuse/fill occlusion. EFT owns the materials and draw scheduling.
    internal sealed class AmbientOcclusionBridge : IDisposable
    {
        private const string ShaderHash = "D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C";
        private static AmbientOcclusionBridge instance;
        private readonly Shader shader;
        private readonly OcclusionSettings settings;
        private readonly Func<Camera,bool> eligible;
        private readonly Dictionary<Material,Shader> originals = new Dictionary<Material,Shader>();
        private readonly List<Material> dead = new List<Material>();
        private FieldInfo materialField;
        private Harmony harmony;
        internal bool Ready { get; private set; }
        internal AmbientOcclusionBridge(Shader shader, OcclusionSettings settings, Func<Camera,bool> eligible)
        { this.shader = shader; this.settings = settings; this.eligible = eligible; }
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
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(AmbientOcclusionBridge),nameof(BeforeAmbient)));
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
                    material.SetFloat("_FTFX_CAAmount",self.settings.AmbientStrength.Value);
                    material.SetFloat("_FTFX_CA_FoliageStrength",self.settings.Foliage.Value);
                }
                else material.shader = original;
            }
            catch(Exception error) { Plugin.MyLog.LogError("CustomAmbient fix failed; native shader restored. "+error); self.Dispose(); }
        }
        internal void Restore()
        {
            foreach(var pair in originals)
                if(pair.Key != null && pair.Key.shader == shader && pair.Value != null) pair.Key.shader = pair.Value;
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
