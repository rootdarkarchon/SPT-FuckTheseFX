using System.IO;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public static partial class OcclusionValidation
{
 private static Color ReadAt(RenderTexture rt,int x,int y)
 {
  var previous=RenderTexture.active;RenderTexture.active=rt;
  var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
  t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();var result=t.GetPixel(x,y);
  Object.DestroyImmediate(t);RenderTexture.active=previous;return result;
 }
 private static void FoliageLiveShader(GpuAssets assets)
 {
  var mat=Material(assets);mat.SetVector("_Size",new Vector4(16,16,1,0));
  var ao=GpuAssets.Target("live AO",16,16,GraphicsFormat.R32G32_SFloat);
  var alpha=GpuAssets.Target("live alpha",16,16,GraphicsFormat.R8_UNorm);
  var actor=GpuAssets.Target("live actors",16,16,GraphicsFormat.R8_UNorm);
  var scene=GpuAssets.Target("live scene",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var other=GpuAssets.Target("live comparison",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var output=GpuAssets.Target("live output",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  mat.SetTexture("_PrimaryAO",ao);mat.SetTexture("_LiveAlpha",alpha);mat.SetTexture("_ActorMask",actor);
  float visibility=Mathf.Clamp01(1+(Mathf.Pow(.8f,.8f)-1)*2);
  for(int scenario=0;scenario<7;scenario++)
  {
   Clear(ao,new Color(.8f,scenario==3?100:scenario==4?120:10,0,0));
   Clear(alpha,scenario==1?Color.white:Color.black);Clear(actor,scenario==2?Color.white:Color.black);
   Clear(scene,new Color(.5f,.4f,.3f,.7f));
   mat.SetVector("_LiveSettings",new Vector4(scenario==5?0:1,scenario==6?.5f:-1,100,0));
   using(var cb=new CommandBuffer())
   { cb.SetRenderTarget(scene);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,8);Graphics.ExecuteCommandBuffer(cb); }
   float expected=scenario==0 || scenario==6?visibility:1;
   var right=ReadAt(scene,12,8);var left=ReadAt(scene,3,8);
   Require(Mathf.Abs(right.r-.5f*expected)<.0001f && Mathf.Abs(right.a-.7f)<.0001f,"live AO mask/fade/zero/alpha scenario "+scenario);
   Require(Mathf.Abs(left.r-.5f*(scenario==6?1:expected))<.0001f,"live AO divider side reversed");
  }
  // Preview uses point-aligned snapshots and the stated absolute difference/gain, independent of extra strength.
  Clear(scene,new Color(.2f,.5f,.8f,1));Clear(other,new Color(.3f,.4f,.6f,1));
  mat.SetTexture("_LiveA",scene);mat.SetTexture("_LiveB",other);mat.SetVector("_LiveDisplay",new Vector4(2,1,0,0));
  using(var cb=new CommandBuffer()) {cb.SetRenderTarget(output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,9);Graphics.ExecuteCommandBuffer(cb);}
  var diff=Read(output);Require(Mathf.Abs(diff.r-.2f)<.0001f && Mathf.Abs(diff.g-.2f)<.0001f && Mathf.Abs(diff.b-.4f)<.0001f,"live absolute difference/gain mismatch");
  GpuAssets.Release(ref ao);GpuAssets.Release(ref alpha);GpuAssets.Release(ref actor);GpuAssets.Release(ref scene);GpuAssets.Release(ref other);GpuAssets.Release(ref output);Object.DestroyImmediate(mat);
  results.Add("PASS: live foliage AO masking, actor/sky/nonzero-alpha exclusion, zero strength, split direction, alpha preservation and absolute difference preview.");
 }
 private static void FoliageLivePipeline(GpuAssets assets)
 {
  var camera=Scene(64,64,out var target,out var geometry);
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("foliage-live.cfg"),false){SaveOnConfigSet=false});
  settings.View.Value=OcclusionView.FoliageLive;settings.Actors.Value=false;
  settings.FoliagePreview.Value=FoliageLivePreview.AfterOpaque;
  var opaque=new CommandBuffer {name=FoliageLive.OpaqueName};
  opaque.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);opaque.ClearRenderTarget(false,true,new Color(.8f,.4f,.2f,1));
  opaque.SetGlobalFloat("_FTFX_LiveNativeExecuted",17);
  camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);
  var tail=new CommandBuffer {name="Live fixture native tail"};camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,tail);
  using(var effect=new CameraOcclusion(camera,assets))
  {
   effect.Record(settings,2,true,true);camera.Render();
   Require(effect.Live!=null && effect.Live.OpaqueFound,"live opaque stage not found");
   Require(Mathf.Abs(Read(effect.Live.Stage(3)).r-.8f)<.002f,"opaque output not captured precisely");
   Require(Mathf.Abs(Read(effect.Live.Display).r-.8f)<.002f,"live preview not drawn");
   int count=camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length;
   for(int i=0;i<3;i++)
   {
    settings.FoliageBypassOpaque.Value=true;effect.Record(settings,2,true,true);camera.Render();
    Require(camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==count,"live guards accumulated");
    Require(Mathf.Abs(Read(effect.Live.Stage(4)).r-Read(effect.Live.Stage(2)).r)<.002f,"opaque bypass did not restore preceding scene");
    Require(Mathf.Abs(Read(effect.Live.Stage(3)).r-.8f)<.002f && Shader.GetGlobalFloat("_FTFX_LiveNativeExecuted")==17,"bypass skipped native commands/output capture");
    var buffers=camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque);int index=-1;
    for(int j=0;j<buffers.Length;j++) if(buffers[j].name==FoliageLive.OpaqueName) index=j;
    Require(index>0 && buffers[index-1].name.EndsWith("before opaque") && buffers[index+1].name.EndsWith("after opaque") && buffers[index+2].name==tail.name,"native order or guard placement changed");
   }
   settings.Bleed.Enabled.Value=true;effect.Record(settings,2,true,true);
   var bleedField=typeof(CameraOcclusion).GetField("bleed",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   Require(bleedField.GetValue(effect)!=null,"live mode suppressed normal GI");
   settings.FoliageBypassGI.Value=true;effect.Record(settings,2,true,true);
   Require(bleedField.GetValue(effect)==null && settings.Bleed.Enabled.Value,"temporary GI bypass rewrote saved enable");
   settings.ResetFoliageExperiment();effect.Record(settings,2,true,true);
   Require(bleedField.GetValue(effect)!=null,"GI did not resume");
   settings.Bleed.Enabled.Value=false;
   camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);effect.Record(settings,2,true,true);
   Require(!effect.Live.OpaqueFound,"missing opaque buffer was accepted");
   settings.FoliageExtraAO.Value=100;effect.Record(settings,2,true,true);
   Require(!HasFoliagePass(camera),"missing opaque contract did not fail closed");
   camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);
   effect.Record(settings,2,true,false);Require(effect.Live==null,"optic retained main-only live resources");
   camera.allowHDR=false;effect.Record(settings,2,true,true);Require(effect.Live==null,"LDR entered HDR live experiment");camera.allowHDR=true;
   effect.Record(settings,2,true,true);Require(effect.Live!=null,"live did not resume after supported camera returned");
   settings.View.Value=OcclusionView.Normal;effect.Record(settings,2,true,true);
   Require(effect.Live==null,"normal mode retained live resources");
   Require(HasFoliagePass(camera),"normal mode lost persistent foliage AO");
   Require(camera.GetCommandBuffers(CameraEvent.AfterForwardAlpha).Length==0,"normal foliage AO retained diagnostic copies");
   settings.FoliageExtraAO.Value=20;settings.ResetFoliageExperiment();
   Require(settings.FoliageExtraAO.Value==20,"debug reset cleared persistent foliage strength");
   settings.Ambient.Value=false;effect.Record(settings,2,true,true);camera.Render();
   Require(HasFoliagePass(camera),"foliage AO incorrectly depends on ambient fix");
   settings.FoliageExtraAO.Value=0;effect.Record(settings,2,true,true);
   Require(!HasFoliagePass(camera),"zero foliage strength retained pass");
  }
  var remaining=camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque);
  Require(remaining.Length==2 && remaining[0].name==tail.name && remaining[1].name==opaque.name,"cleanup changed native buffers");
  camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);opaque.Dispose();camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,tail);tail.Dispose();
  DestroyScene(camera,target,geometry);
  results.Add("PASS: live opaque bracketing, reversible bypasses, saved settings, missing-stage fail-closed, main/optic/HDR gates, persistent normal foliage AO without diagnostic copies or ambient dependency, zero-strength cleanup.");
 }
 private static bool HasFoliagePass(Camera camera) => System.Array.Exists(camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque),cb=>cb.name=="FTFX foliage extra AO");
 private static void PersistentFoliageAO(GpuAssets assets)
 {
  var camera=Scene(64,64,out var target,out var geometry);
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("foliage-persistent.cfg"),false){SaveOnConfigSet=false});
  Require(settings.FoliageExtraAO.Value==20 && settings.FadeStart.Value==100 && settings.FadeEnd.Value==250,"new foliage defaults");
  var ao=GpuAssets.Target("persistent AO",64,64,GraphicsFormat.R32G32_SFloat);
  var alpha=GpuAssets.Target("persistent alpha",64,64,GraphicsFormat.R8_UNorm);
  var actor=GpuAssets.Target("persistent actor",64,64,GraphicsFormat.R8_UNorm);
  Clear(alpha,Color.black);Clear(actor,Color.black);
  var surface=new CommandBuffer {name="Known surface lighting before fog"};
  surface.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);surface.ClearRenderTarget(false,true,new Color(.8f,.4f,.2f,1));
  camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,surface);
  var fog=new Material(Shader.Find("Hidden/FTFXValidation/FoliageFog"));
  var opaque=new CommandBuffer {name=FoliageLive.OpaqueName};
  opaque.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);opaque.DrawMesh(assets.Quad,Matrix4x4.identity,fog,0,0);
  camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);
  camera.farClipPlane=1000;
  using(var pass=new FoliageOcclusion(camera,assets))
  {
   foreach(float depth in new[]{50f,100f,175f,250f})
   foreach(float strength in new[]{.2f,.6f})
   foreach(float transmission in new[]{1f,.25f,0f})
   {
    settings.FoliageExtraAO.Value=Mathf.RoundToInt(strength*100);
    fog.SetVector("_Fog",new Vector4(.6f,.6f,.6f,1-transmission));
    Clear(ao,new Color(.8f,depth/(camera.farClipPlane/65504f),0,0));
    float fade=Mathf.Clamp01((depth-100)/150);
    float visibility=Mathf.Clamp01(1+(Mathf.Pow(.8f,Mathf.Lerp(.8f,1,fade))-1)*Mathf.Lerp(2,0,fade));
    // Fog colour must not be multiplied by AO; fully opaque fog must erase all surface contrast.
    float expected=.8f*Mathf.Lerp(1,visibility,strength)*transmission+.6f*(1-transmission);
    float baseline=.8f*transmission+.6f*(1-transmission);
    settings.View.Value=OcclusionView.Normal;pass.Record(settings,ao,alpha,actor);camera.Render();
    Require(Mathf.Abs(ReadAt(target,16,32).r-expected)<.002f && Mathf.Abs(ReadAt(target,48,32).r-expected)<.002f,"normal foliage AO must affect both sides with shared 100/250 fade");
    settings.View.Value=OcclusionView.FoliageLive;pass.Record(settings,ao,alpha,actor);camera.Render();
    Require(Mathf.Abs(ReadAt(target,16,32).r-baseline)<.002f && Mathf.Abs(ReadAt(target,48,32).r-expected)<.002f,"fog must cover live split AO without darkening fog colour");
   }
  }
  Require(!HasFoliagePass(camera),"persistent foliage pass leaked on dispose");
  camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);opaque.Dispose();
  camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,surface);surface.Dispose();Object.DestroyImmediate(fog);
  GpuAssets.Release(ref ao);GpuAssets.Release(ref alpha);GpuAssets.Release(ref actor);DestroyScene(camera,target,geometry);
  results.Add("PASS: 20%/60% foliage AO precedes opaque fog, preserves fog colour at 0/25/100% transmission, matches live split, shares 100/250 fade and releases its pass.");
 }
}
