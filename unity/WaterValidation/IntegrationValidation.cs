using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using FuckTheseFX.Source.Water;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Minimal native ownership surrogate. The separate installed-IL test checks the
// exact game field, methods and DrawMesh sites; this fixture exercises Unity lifetime.
public sealed class CommandBufferManager
{
    public readonly Dictionary<Camera,CommandBuffer> Cameras=new Dictionary<Camera,CommandBuffer>();
    public void DestroyBuffers()
    { foreach(var p in Cameras)if(p.Key!=null)p.Key.RemoveCommandBuffer(CameraEvent.BeforeReflections,p.Value);Cameras.Clear(); }
}
namespace WaterSSR
{
    public sealed class WaterRendererv3 : MonoBehaviour
    { public readonly CommandBufferManager commandBufferManager=new CommandBufferManager();public Material material_0; }
}
namespace FuckTheseFX
{
    internal static class Plugin
    {
        internal static ManualLogSource MyLog=BepInEx.Logging.Logger.CreateLogSource("Water validation");
        internal static ConfigEntry<WaterSurfaceMode> WaterSurface;
        internal static ConfigEntry<bool> WaterSurfaceDiagnosticTint;
        internal static ConfigEntry<float> WaterSurfaceSize, WaterSurfaceStrength, WaterFineRippleStrength, WaterAnimationSpeed;
    }
}
public static class WaterIntegrationValidation
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static void Run(Shader replacement)
    {
        AccessTools.Property(typeof(BepInEx.Paths),"BepInExConfigPath").SetValue(null,Path.GetFullPath("BepInEx-test.cfg"));
        AccessTools.Property(typeof(BepInEx.Paths),"ConfigPath").SetValue(null,Directory.GetCurrentDirectory());
        ValidateInstalledDrawLayout();
        var bundle=AssetBundle.LoadFromFile(Path.GetFullPath("native-reference"));
        var original=new Material(bundle.LoadAllAssets<Shader>()[0]);bundle.Unload(false);
        var texture=new Texture2D(4,4);texture.mipMapBias=-2;original.SetTexture("_BumpMapDetails",texture);original.EnableKeyword("Rain");
        var config=new ConfigFile(Path.GetFullPath("integration.cfg"),false);config.SaveOnConfigSet=false;
        FuckTheseFX.Plugin.WaterSurface=config.Bind("Water Surface","Mode",WaterSurfaceMode.Native);
        FuckTheseFX.Plugin.WaterSurfaceDiagnosticTint=config.Bind("Water Surface","Show replacement tint",false);
        FuckTheseFX.Plugin.WaterSurfaceSize=config.Bind("Water Surface","Surface size",1f);
        FuckTheseFX.Plugin.WaterSurfaceStrength=config.Bind("Water Surface","Surface strength",1f);
        FuckTheseFX.Plugin.WaterFineRippleStrength=config.Bind("Water Surface","Fine ripple strength",1f);
        FuckTheseFX.Plugin.WaterAnimationSpeed=config.Bind("Water Surface","Animation speed",1f);
        var self=new WaterSurfaceIntegration();var type=typeof(WaterSurfaceIntegration);
        AccessTools.Field(type,"active").SetValue(null,self);AccessTools.Field(type,"shader").SetValue(self,replacement);
        AccessTools.Field(type,"managerField").SetValue(self,AccessTools.Field(typeof(WaterSSR.WaterRendererv3),"commandBufferManager"));
        var select=AccessTools.Method(type,"SelectMaterial");
        var scene=SceneManager.GetActiveScene();scene.name="custom_Scripts";
        var owner=new GameObject("Customs renderer").AddComponent<WaterSSR.WaterRendererv3>();SceneManager.MoveGameObjectToScene(owner.gameObject,scene);
        var main=new GameObject("main").AddComponent<Camera>();main.enabled=false;main.tag="MainCamera";
        var optic=new GameObject("optic").AddComponent<Camera>();optic.enabled=false;optic.tag="OpticCamera";
        var other=new GameObject("secondary").AddComponent<Camera>();other.enabled=false;
        Material Select(Camera camera)=> (Material)select.Invoke(null,new object[]{original,owner,camera});
        Check(ReferenceEquals(Select(main),original),"Native must return the exact original material");
        FuckTheseFX.Plugin.WaterSurface.Value=WaterSurfaceMode.FTFX;self.Update();
        var clone=Select(main);Check(clone!=original && clone.shader==replacement,"Customs main must use replacement");
        Check(ReferenceEquals(Select(optic),clone),"Optics should reuse the parity material with native per-camera globals");
        Check(ReferenceEquals(Select(other),original),"Secondary camera must remain native");
        Check(original.shader.name=="Hidden/WaterForSSRv2" && texture.mipMapBias==-2,"Original shader/texture bias mutated");
        Check(clone.GetTexture("_BumpMapDetails")==texture && clone.IsKeywordEnabled("Rain"),"Material texture/keyword capture failed");
        Check(clone.GetFloat("_FTFXWaterDiagnostic")==0,"Diagnostic must default off");
        Check(clone.GetVector("_FTFXWaterNormalControls")==Vector4.one,"Surface defaults must preserve phase-two tuning");
        float phase=self.AnimationTime;
        self.AdvanceAnimation(2);
        Check(Math.Abs(self.AnimationTime-phase-.1f)<.00001,"Default animation clock did not advance by elapsed time");
        Check(Math.Abs(clone.GetFloat("_FTFXWaterAnimationTime")-self.AnimationTime)<.00001,"Cached draw material did not receive advancing phase");
        phase=self.AnimationTime;
        FuckTheseFX.Plugin.WaterAnimationSpeed.Value=2;
        Check(Select(main).GetFloat("_FTFXWaterAnimationTime")==phase && Select(optic).GetFloat("_FTFXWaterAnimationTime")==phase,"Speed change or camera selection jumped animation phase");
        self.AdvanceAnimation(2);
        Check(Math.Abs(self.AnimationTime-phase-.2f)<.00001,"Double speed did not double subsequent motion");
        phase=self.AnimationTime;FuckTheseFX.Plugin.WaterAnimationSpeed.Value=0;
        self.AdvanceAnimation(30);
        Check(self.AnimationTime==phase && Select(main).GetFloat("_FTFXWaterAnimationTime")==phase,"Zero speed must freeze at the current position");
        FuckTheseFX.Plugin.WaterAnimationSpeed.Value=.5f;self.AdvanceAnimation(2);
        Check(Math.Abs(self.AnimationTime-phase-.05f)<.00001,"Resume at half speed must continue from frozen position");
        Check(Select(main).GetFloat("_FTFXWaterAnimationTime")==Select(optic).GetFloat("_FTFXWaterAnimationTime"),"Camera count must not advance water time");
        FuckTheseFX.Plugin.WaterSurfaceSize.Value=2;FuckTheseFX.Plugin.WaterSurfaceStrength.Value=.5f;
        FuckTheseFX.Plugin.WaterFineRippleStrength.Value=3;FuckTheseFX.Plugin.WaterAnimationSpeed.Value=0;
        Check(ReferenceEquals(Select(main),clone) && clone.GetVector("_FTFXWaterNormalControls")==new Vector4(2,.5f,3,0),"Live settings did not update existing material");
        Check(Select(optic).GetVector("_FTFXWaterNormalControls")==new Vector4(2,.5f,3,0),"Optic tuning diverged from main");
        FuckTheseFX.Plugin.WaterSurfaceDiagnosticTint.Value=true;
        Check(Select(main).GetFloat("_FTFXWaterDiagnostic")==1 && Select(optic).GetFloat("_FTFXWaterDiagnostic")==1,"Tint must reach both replacement camera draws");
        scene.name="Shoreline_Middle";
        Check(ReferenceEquals(Select(main),original),"Other maps must stay native");scene.name="custom_Scripts";
        var cb=new CommandBuffer();main.AddCommandBuffer(CameraEvent.BeforeReflections,cb);owner.commandBufferManager.Cameras[main]=cb;
        FuckTheseFX.Plugin.WaterSurface.Value=WaterSurfaceMode.Native;self.Update();
        Check(clone==null && main.GetCommandBuffers(CameraEvent.BeforeReflections).Length==0,"Switch must detach cached draws before freeing clones");
        Check(ReferenceEquals(Select(main),original),"Switch back did not restore native material");
        FuckTheseFX.Plugin.WaterSurfaceDiagnosticTint.Value=false;
        FuckTheseFX.Plugin.WaterSurface.Value=WaterSurfaceMode.FTFX;self.Update();clone=Select(main);
        self.Suspend();Check(clone==null && ReferenceEquals(Select(main),original),"Suspend must restore native rendering");
        phase=self.AnimationTime;self.AdvanceAnimation(10);Check(self.AnimationTime==phase,"Suspended integration advanced water time");
        self.Resume();clone=Select(main);Check(clone!=original,"Resume did not restore FTFX selection");
        Check(clone.GetFloat("_FTFXWaterDiagnostic")==0,"Tint off must restore parity");
        self.Dispose();Check(clone==null && ReferenceEquals(Select(main),original),"Dispose must remove override and release clones");
        Check(original.shader.name=="Hidden/WaterForSSRv2" && texture.mipMapBias==-2,"Cleanup mutated native assets");
        cb.Dispose();Object.DestroyImmediate(original);Object.DestroyImmediate(texture);
        Object.DestroyImmediate(owner.gameObject);Object.DestroyImmediate(main.gameObject);Object.DestroyImmediate(optic.gameObject);Object.DestroyImmediate(other.gameObject);
        Debug.Log("PASS: water Native/FTFX selection, map/camera exclusion, shared native textures/keywords, switch/suspend/resume/dispose and buffer detachment.");
        Debug.Log("PASS: elapsed-time animation, cached material updates, speed-change continuity, freeze/resume, main/optic phase agreement and suspend.");
    }
    private static void ValidateInstalledDrawLayout()
    {
        using(var assembly=Mono.Cecil.AssemblyDefinition.ReadAssembly(File.ReadAllText("game-assembly.txt").Trim()))
        {
            Check(assembly.MainModule.Mvid==new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7"),"Installed water ABI mismatch");
            var method=assembly.MainModule.Types.Single(t=>t.FullName=="WaterSSR.WaterRendererv3").Methods.Single(m=>m.Name=="method_2");
            var opcodes=typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>(int)o.Value);
            var code=new List<CodeInstruction>();
            foreach(var instruction in method.Body.Instructions)
            {
                object operand=instruction.Operand;
                if(operand is Mono.Cecil.FieldReference field && field.Name=="material_0" && field.DeclaringType.FullName=="WaterSSR.WaterRendererv3")
                    operand=AccessTools.Field(typeof(WaterSSR.WaterRendererv3),"material_0");
                if(operand is Mono.Cecil.MethodReference call && call.Name=="DrawMesh" && call.DeclaringType.FullName==typeof(CommandBuffer).FullName)
                {
                    Check(call.Parameters.Count==5 || call.Parameters.Count==6,"Unexpected DrawMesh signature");
                    Type[] signature=call.Parameters.Count==6?new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int),typeof(MaterialPropertyBlock)}:new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int)};
                    operand=AccessTools.Method(typeof(CommandBuffer),"DrawMesh",signature);
                }
                code.Add(new CodeInstruction(opcodes[instruction.OpCode.Value],operand));
            }
            // Operand identities match the fixture type; instruction shape is read
            // directly from the installed method, never recreated from sample C#.
            var patched=WaterSurfaceIntegration.ReplaceDrawMaterials(code).ToList();
            Check(patched.Count==code.Count+6,"Unexpected number of inserted instructions");
            int original=0,selectors=0;
            foreach(var instruction in patched)
            {
                if(original<code.Count && ReferenceEquals(instruction,code[original])) {original++;continue;}
                if(instruction.operand is MethodInfo m && m.Name=="SelectMaterial")selectors++;
                else Check(instruction.opcode==OpCodes.Ldarg_0 || instruction.opcode==OpCodes.Ldarg_1,"Unexpected hook operation");
            }
            Check(original==code.Count && selectors==2,"Native instruction/property-block/draw preservation failed");
            var malformed=code.Where(c=>!(c.operand is MethodInfo m && m.Name=="DrawMesh")).ToList();bool rejected=false;
            try{WaterSurfaceIntegration.ReplaceDrawMaterials(malformed).ToList();}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"Changed draw layout did not fail closed");
            Debug.Log("PASS: installed water IL transformed through production selector hook: every original instruction retained, two material substitutions, malformed layout rejected.");
        }
    }
}
