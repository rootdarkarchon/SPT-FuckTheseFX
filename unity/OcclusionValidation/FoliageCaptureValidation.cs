using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class OcclusionValidation
{
 private static void FoliageCapturePipeline(GpuAssets assets)
 {
  var camera=Scene(64,64,out var target,out var geometry);
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("foliage-capture.cfg"),false){SaveOnConfigSet=false});
  settings.View.Value=OcclusionView.FoliageLightingCapture;settings.AmbientStrength.Value=100;settings.Foliage.Value=100;settings.Actors.Value=false;
  var mat=new Material(assets.Ambient);mat.EnableKeyword("UNITY_HDR_ON");
  mat.SetFloat("_FTFX_CAAmount",1);mat.SetFloat("_FTFX_CA_FoliageStrength",1);
  foreach(var name in new[]{"_SHAr","_SHAg","_SHAb"})mat.SetVector(name,new Vector4(0,0,0,.2f));
  mat.SetTexture("_StencilShadow",Texture2D.blackTexture);
  var mesh=new Mesh{vertices=new[]{new Vector3(-1,-1,.1f),new Vector3(1,-1,.1f),new Vector3(-1,1,.1f),new Vector3(1,1,.1f)},triangles=new[]{0,1,3,3,2,0}};
  var native=new AmbientLight(mat){Quad=mesh};var camSettings=new AmbientLight.CamSettings();
  var hook=AccessTools.Method(typeof(AmbientLight),nameof(AmbientLight.InitAmbientBuffer));
  var field=AccessTools.Field(typeof(AmbientOcclusionBridge),"instance");
  var owner=new Harmony("ftfx.foliage.capture.test");
  var init=new CommandBuffer();init.SetGlobalFloat("_SrcBlend",1);init.SetGlobalFloat("_DstBlend",1);init.EnableShaderKeyword("UNITY_HDR_ON");
  camera.AddCommandBuffer(CameraEvent.BeforeGBuffer,init);camera.AddCommandBuffer(CameraEvent.AfterLighting,camSettings.CbAmbient);
  var late=new CommandBuffer { name="Known later replacement" };
  using(var effect=new CameraOcclusion(camera,assets))
  using(var bridge=new AmbientOcclusionBridge(assets.Ambient,settings,c=>c==camera,c=>c==camera?effect.LightingDebugTexture:null,c=>c==camera?effect.Foliage:null,c=>c==camera?effect.Live:null))
  {
   try
   {
    effect.Record(settings,2,true,true);native.InitAmbientBuffer(camSettings,camera);camera.Render();var baseline=Read(target);
    mat.EnableKeyword("FTFX_BENT_DIAGNOSTICS");mat.EnableKeyword("FTFX_FOLIAGE_CAPTURE");
    field.SetValue(null,bridge);owner.Patch(hook,transpiler:new HarmonyMethod(typeof(AmbientOcclusionBridge),"InstrumentDraw"));
    effect.Record(settings,2,true,true);native.InitAmbientBuffer(camSettings,camera);camera.Render();
    Require((Read(target)-baseline).maxColorComponent<.001f && (baseline-Read(target)).maxColorComponent<.001f,"capture changed camera lighting");
    Require(Read(effect.Foliage.AO).a==1 && Read(effect.Foliage.Unoccluded).a==1 && Read(effect.Foliage.Removed).a==1,"native fragment did not populate capture UAVs");
    Require(Read(effect.Foliage.Stage(4)).r>Read(effect.Foliage.Stage(3)).r,"before/after captures do not surround native ambient addition");
    Require(Shader.GetGlobalFloat("_FTFX_CA_DebugAvailable")==0,"foliage UAV state leaked");
    late.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);late.ClearRenderTarget(false,true,new Color(.9f,.1f,.2f,1));
    camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha,late);
    effect.Record(settings,2,true,true);native.InitAmbientBuffer(camSettings,camera);camera.Render();
    Require(Mathf.Abs(Read(effect.Foliage.Stage(6)).r-.9f)<.002f,"late stage replacement not captured");
    Require(Mathf.Abs(Read(effect.Foliage.Stage(5)).r-.9f)>.01f,"earlier stage was overwritten by late replacement");
    var folder=effect.Foliage.Save(Path.GetFullPath("FoliageCaptureOutput"));
    Require(Directory.GetFiles(folder,"*.exr").Length==8 && Directory.GetFiles(folder,"*.png").Length==10 && File.Exists(Path.Combine(folder,"COMPLETE.txt")),"capture export incomplete");
    Require(File.ReadAllText(Path.Combine(folder,"capture.txt")).Contains("Known later replacement"),"capture missing stage inventory");
    // Raw EXR magic and PNG magic; file creation alone is insufficient.
    var exr=File.ReadAllBytes(Path.Combine(folder,"ao-inputs.exr"));var png=File.ReadAllBytes(Path.Combine(folder,"effective-ao.png"));
    Require(exr[0]==0x76 && exr[1]==0x2f && exr[2]==0x31 && exr[3]==1 && png[0]==137 && png[1]==80,"invalid exported image encoding");
    settings.Bleed.Enabled.Value=true;effect.Record(settings,2,true,true);native.InitAmbientBuffer(camSettings,camera);camera.Render();
    Require(typeof(CameraOcclusion).GetField("bleed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(effect)!=null,"capture disabled enabled contact GI");
    effect.Record(settings,2,true,false);Require(effect.Foliage==null,"non-main camera retained foliage capture");
    settings.View.Value=OcclusionView.FoliageLive;
    mat.DisableKeyword("FTFX_FOLIAGE_CAPTURE");mat.DisableKeyword("FTFX_BENT_DIAGNOSTICS");
    effect.Record(settings,2,true,true);native.InitAmbientBuffer(camSettings,camera);camera.Render();
    Require(Read(effect.Live.Stage(1)).r>Read(effect.Live.Stage(0)).r,"live taps did not surround actual native ambient draw");
    settings.View.Value=OcclusionView.Normal;effect.Record(settings,2,true,true);
    Require(effect.Foliage==null && effect.LightingDebugTexture==null,"capture resources retained in Normal mode");
   }
   finally {owner.UnpatchSelf();field.SetValue(null,null);}
  }
  camera.RemoveCommandBuffer(CameraEvent.AfterLighting,camSettings.CbAmbient);camSettings.CbAmbient.Dispose();
  camera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer,init);init.Dispose();camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha,late);late.Dispose();
  Require(camera.GetCommandBuffers(CameraEvent.AfterEverything).Length==0,"capture stage buffer leaked");
  Object.DestroyImmediate(mat);Object.DestroyImmediate(mesh);DestroyScene(camera,target,geometry);
  results.Add("PASS: same-frame foliage UAVs and before/after native ambient, scene preservation, later replacement stage separation, EXR/PNG export, contact GI preservation, main-camera gating and cleanup.");
 }
}
