using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using WaterSSR;

namespace FuckTheseFX.Source.Water
{
    public enum WaterSurfaceMode { Native, FTFX }

    // Substitute the two native material loads, never the draws or settings blocks.
    internal sealed class WaterSurfaceIntegration : IDisposable
    {
        private const string AssetsHash="266A9AD217731AC3953E6603BDD5D8648F143DCEE976C76A4783C74891876550";
        private static WaterSurfaceIntegration active;
        private Harmony harmony;
        private AssetBundle bundle;
        private Shader shader;
        private FieldInfo managerField;
        private readonly Dictionary<Material, Material> materials=new Dictionary<Material, Material>();
        private readonly HashSet<WaterRendererv3> owners=new HashSet<WaterRendererv3>();
        private readonly HashSet<Camera> announced=new HashSet<Camera>();
        private readonly List<Material> dead=new List<Material>();
        private bool suspended,failed;
        private WaterSurfaceMode previous;
        private int cleanupFrame;
        private static readonly int AnimationTimeId=Shader.PropertyToID("_FTFXWaterAnimationTime");
        private double animationTime;
        internal float AnimationTime => (float)animationTime;

        internal void Initialize(string pluginPath)
        {
            try
            {
                if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11 || QualitySettings.activeColorSpace!=ColorSpace.Gamma)
                    throw new NotSupportedException("Water surface requires the pinned D3D11/Gamma path.");
                if(typeof(WaterRendererv3).Module.ModuleVersionId!=new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7"))
                    throw new NotSupportedException("Unsupported EFT water ABI; expected EFT 40743.");
                using(var stream=File.OpenRead(Path.Combine(Application.dataPath,"sharedassets15.assets")))
                using(var sha=SHA256.Create())
                    if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=AssetsHash)
                        throw new NotSupportedException("Unsupported native water shader assets.");
                managerField=AccessTools.Field(typeof(WaterRendererv3),"commandBufferManager");
                if(managerField==null || managerField.FieldType!=typeof(CommandBufferManager)) throw new MissingFieldException("WaterRendererv3.commandBufferManager");
                var method=AccessTools.DeclaredMethod(typeof(WaterRendererv3),"method_2",new[]{typeof(Camera)});
                if(method==null || method.IsStatic || method.ReturnType!=typeof(void)) throw new MissingMethodException("WaterRendererv3.method_2(Camera)");
                bundle=AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(pluginPath),"Assets","water"));
                shader=bundle!=null?bundle.LoadAsset<Shader>("Assets/WaterParity.shader"):null;
                if(shader==null || !shader.isSupported) throw new InvalidOperationException("Water shader bundle missing or unsupported.");
                harmony=new Harmony("com.rootdarkarchon.fuckthesefx.water");
                harmony.Patch(method,transpiler:new HarmonyMethod(typeof(WaterSurfaceIntegration),nameof(ReplaceDrawMaterials)){priority=Priority.First});
                previous=Plugin.WaterSurface.Value;active=this;
                Plugin.MyLog.LogInfo("Water surface normals ready: Customs v3 main/optic only; Water Surface / Mode = "+previous+".");
            }
            catch(Exception error) { Dispose();Plugin.MyLog.LogError("Water replacement unavailable; retaining native water. "+error); }
        }

        internal static IEnumerable<CodeInstruction> ReplaceDrawMaterials(IEnumerable<CodeInstruction> instructions)
        {
            var code=instructions.ToList();int first=0,second=0;
            var selector=AccessTools.Method(typeof(WaterSurfaceIntegration),nameof(SelectMaterial));
            var draw0=AccessTools.Method(typeof(CommandBuffer),nameof(CommandBuffer.DrawMesh),new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int),typeof(MaterialPropertyBlock)});
            var draw1=AccessTools.Method(typeof(CommandBuffer),nameof(CommandBuffer.DrawMesh),new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int)});
            for(int i=4;i<code.Count;i++)
            {
                bool primary=code[i].Calls(draw0),depth=code[i].Calls(draw1);
                if(!primary && !depth) continue;
                int index=i-(primary?4:3);
                if(code[index].opcode!=OpCodes.Ldfld || !(code[index].operand is FieldInfo f) ||
                    f.DeclaringType!=typeof(WaterRendererv3) || f.Name!="material_0" ||
                    code[index+1].opcode!=OpCodes.Ldc_I4_0 || code[index+2].opcode!=(primary?OpCodes.Ldc_I4_0:OpCodes.Ldc_I4_1) ||
                    (primary && code[index+3].opcode!=OpCodes.Ldloc_3)) throw new InvalidOperationException("Native water draw layout changed.");
                code.InsertRange(index+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call,selector)});
                if(primary)first++;else second++;i+=3;
            }
            if(first!=1 || second!=1) throw new InvalidOperationException($"Expected two native water draws, found {first}/{second}.");
            return code;
        }

        private static Material SelectMaterial(Material original,WaterRendererv3 owner,Camera camera)
        {
            var self=active;
            if(self==null || self.suspended || self.failed || Plugin.WaterSurface.Value!=WaterSurfaceMode.FTFX ||
                owner==null || camera==null || owner.gameObject.scene.name!="custom_Scripts" ||
                (!camera.CompareTag("MainCamera") && !camera.CompareTag("OpticCamera")) ||
                original==null || original.shader==null || original.shader.name!="Hidden/WaterForSSRv2") return original;
            try
            {
                if(!self.materials.TryGetValue(original,out var material) || material==null)
                {
                    material=new Material(original){shader=self.shader,hideFlags=HideFlags.HideAndDontSave,name="FTFX Customs water normals"};
                    self.materials[original]=material;
                }
                // MPB/global values and texture objects remain native; no texture bias mutation.
                material.shader=self.shader;
                material.CopyPropertiesFromMaterial(original);
                material.SetFloat("_FTFXWaterDiagnostic",Plugin.WaterSurfaceDiagnosticTint.Value?1f:0f);
                material.SetVector("_FTFXWaterNormalControls",new Vector4(Plugin.WaterSurfaceSize.Value,
                    Plugin.WaterSurfaceStrength.Value,Plugin.WaterFineRippleStrength.Value,Plugin.WaterAnimationSpeed.Value));
                material.SetFloat(AnimationTimeId,self.AnimationTime);
                self.owners.Add(owner);
                if(self.announced.Add(camera)) Plugin.MyLog.LogInfo("FTFX water normals active: "+owner.gameObject.scene.name+" / "+camera.name+" ("+camera.tag+").");
                return material;
            }
            catch(Exception error)
            {
                self.failed=true;
                // Restore any already-recorded references without clearing a buffer mid-recording.
                self.RestoreCloneShaders();
                Plugin.MyLog.LogError("Water replacement failed; restored native shading. "+error);
                return original;
            }
        }

        private void RestoreCloneShaders()
        {
            foreach(var pair in materials)
                if(pair.Key!=null && pair.Value!=null) { pair.Value.shader=pair.Key.shader;pair.Value.CopyPropertiesFromMaterial(pair.Key); }
        }

        private void ReleaseRecordedMaterials()
        {
            RestoreCloneShaders();
            bool detached=true;
            foreach(var owner in owners)
            {
                if(owner==null) continue;
                try { (managerField.GetValue(owner) as CommandBufferManager)?.DestroyBuffers(); }
                catch(Exception error) { detached=false;Plugin.MyLog.LogWarning("Native water buffer cleanup deferred: "+error); }
            }
            // Native pre-cull creates and records fresh buffers for each camera next render.
            // If detachment failed, retain native-shaded clones rather than dangling GPU refs.
            if(!detached) return;
            owners.Clear();announced.Clear();
            foreach(var material in materials.Values) Destroy(material);
            materials.Clear();
        }

        internal void Update()
        {
            if(active!=this) return;
            AdvanceAnimation(Time.deltaTime);
            if(previous!=Plugin.WaterSurface.Value) { ReleaseRecordedMaterials();previous=Plugin.WaterSurface.Value; }
            if(++cleanupFrame%300!=0) return;
            owners.RemoveWhere(x=>x==null);announced.RemoveWhere(x=>x==null);dead.Clear();
            foreach(var pair in materials) if(pair.Key==null)dead.Add(pair.Key);
            // Native OnDisable detaches its buffers before destroying the original material.
            foreach(var key in dead){Destroy(materials[key]);materials.Remove(key);}
        }
        internal void AdvanceAnimation(float elapsedSeconds)
        {
            if(suspended || failed) return;
            // Integrate the rate once per Update, not once per water draw/camera.
            // Keep the native seconds/20 unit, but never multiply absolute time by
            // a live speed setting: changing speed must not relocate the pattern.
            animationTime+=Math.Max(0f,elapsedSeconds)*Mathf.Clamp(Plugin.WaterAnimationSpeed.Value,0f,2f)*0.05d;
            // Update retained materials too, even if native reuses a recorded buffer.
            foreach(var material in materials.Values)
                if(material!=null)material.SetFloat(AnimationTimeId,AnimationTime);
        }
        internal void Suspend() { suspended=true;ReleaseRecordedMaterials(); }
        internal void Resume() { suspended=false; }
        public void Dispose()
        {
            if(active==this)active=null;
            try { harmony?.UnpatchSelf(); } catch(Exception e) { Plugin.MyLog.LogWarning("Water hook cleanup: "+e); }
            harmony=null;ReleaseRecordedMaterials();
            if(bundle!=null)bundle.Unload(false);bundle=null;shader=null;
        }
        private static void Destroy(Material material)
        {
            if(material==null)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(material);else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
