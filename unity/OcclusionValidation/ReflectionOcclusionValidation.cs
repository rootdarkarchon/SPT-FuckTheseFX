using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public static partial class OcclusionValidation
{
 private static void ReflectionOcclusionShaders(GpuAssets assets)
 {
  var mat=Material(assets);mat.SetVector("_Size",new Vector4(16,16,1,1));
  var ao=GpuAssets.Target("reflection AO",16,16,GraphicsFormat.R32G32_SFloat);
  var source=GpuAssets.Target("authored material",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var albedo=GpuAssets.Target("reflection material",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var emission=GpuAssets.Target("diffuse emission",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var diffuse=GpuAssets.Target("diffuse visibility",16,16,GraphicsFormat.R8_UNorm);
  var spec=GpuAssets.Target("specular",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var normal=GpuAssets.Target("normal",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var depth=GpuAssets.Target("camera depth",16,16,GraphicsFormat.R32_SFloat);
  var motionDepth=GpuAssets.Target("native instanced depth",8,8,GraphicsFormat.R32_SFloat);
  var emissionSource=GpuAssets.Target("emission source",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  Clear(emissionSource,new Color(.5f,.4f,.3f,.7f));
  mat.SetTexture("_PrimaryAO",ao);mat.SetTexture("_FTFXMaterialInput",source);mat.SetTexture("_Emission",emissionSource);
  mat.SetTexture("_CameraGBufferTexture1",spec);mat.SetTexture("_CameraGBufferTexture2",normal);
  mat.SetTexture("_CameraDepthTexture",depth);mat.SetTexture("_CameraMotionVectorsDepth",motionDepth);
  Clear(depth,new Color(.5f,0,0,0));
  int cases=0,grazingCases=0;
  foreach(bool nativeReducedReflection in new[]{false,true})
  foreach(float raw in new[]{0f,.55f,.8f,1f})
  foreach(float authored in new[]{0f,.4f,1f})
  foreach(float roughness in new[]{0f,.3f,1f})
  foreach(float nov in new[]{0f,.1f,.5f,1f})
  foreach(int pass in new[]{1,2})
  {
   float visibility=Mathf.Clamp01(1+(Mathf.Pow(raw,.8f)-1)*2);
   float expected=nativeReducedReflection ? visibility : Mathf.Lerp(visibility,1,.5f*(1-roughness)*(1-roughness));
   Clear(motionDepth,new Color(nativeReducedReflection?.5f:0,0,0,0));
   Clear(ao,new Color(raw,10,0,0));Clear(spec,new Color(.1f,.1f,.1f,1-roughness));
   Clear(normal,new Color((Mathf.Sqrt(1-nov*nov)+1)*.5f,.5f,(1-nov)*.5f,1));
   Clear(source,new Color(.2f,.3f,.4f,authored));
   foreach(bool enabled in new[]{false,true})
   {
    mat.SetFloat("_ReflectionOcclusion",enabled?1:0);
    Clear(albedo,new Color(.2f,.3f,.4f,authored));Clear(emission,new Color(.5f,.4f,.3f,.7f));
    using(var cb=new CommandBuffer())
    {
     cb.SetRenderTarget(diffuse);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,10);
     cb.SetRenderTarget(new RenderTargetIdentifier[]{albedo,emission},albedo);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,pass);
     Graphics.ExecuteCommandBuffer(cb);
    }
    var a=Read(albedo);var e=Read(emission);var d=Read(diffuse);
    Require(Mathf.Abs(a.a-authored*(enabled?expected:visibility))<.00002f,"reflection visibility equation mismatch");
    Require(Mathf.Abs(a.r-.2f)<.00001f && Mathf.Abs(e.a-.7f)<.00001f,"reflection fix changed RGB or lighting alpha");
    Require(Mathf.Abs(e.r-(pass==1?.5f*visibility:Mathf.Pow(.5f,visibility)))<.00002f,"reflection fix changed HDR/LDR diffuse emission");
    Require(Mathf.Abs(d.r-authored*visibility)<.5f/255+.00001f,"diffuse visibility not preserved at native R8 precision");
    Require(expected>=visibility-.000001f && expected<=1,"reflection correction darkened legacy AO or exceeds unoccluded energy");
    if(roughness==1 || raw==1 || authored==0) Require(Mathf.Abs(a.a-authored*visibility)<.00002f,"rough/unoccluded/zero-alpha compatibility changed");
    if(nativeReducedReflection) Require(Mathf.Abs(a.a-authored*visibility)<.00002f,"native vegetation reflection visibility changed when relief was enabled");
    if(enabled && !nativeReducedReflection && authored>0 && raw<1)
    {
     float retained=(1-a.a/authored)/(1-visibility);
     Require(retained>=.5f-.0001f && retained<=1.0001f,"reflection fix removed more than half the screen occlusion");
     if(roughness==0)
      Require(Mathf.Abs(retained-.5f)<.0001f,"smooth reflection did not retain exactly half the screen occlusion");
     if(nov<=.1f && roughness<1)
     {
      Require(a.a>authored*visibility+.0001f,"glossy grazing-angle correction was a no-op");grazingCases++;
     }
    }
    cases++;
   }
  }
  Require(grazingCases==48,"fixture did not exercise all glossy grazing-angle cases");
  // Mirror the native raw-depth tolerance, including a lower-resolution motion texture.
  Clear(ao,new Color(.55f,10,0,0));Clear(spec,new Color(.1f,.1f,.1f,1));Clear(normal,new Color(.5f,1,.5f,1));
  mat.SetFloat("_ReflectionOcclusion",1);
  foreach(float surfaceDepth in new[]{.001f,.5f,.9f})
  foreach(float offset in new[]{-.00002f,-.000005f,0f,.00002f})
  foreach(int pass in new[]{1,2})
  {
   Clear(depth,new Color(surfaceDepth,0,0,0));Clear(motionDepth,new Color(surfaceDepth+offset,0,0,0));
   Clear(albedo,Color.white);Clear(emission,Color.white);
   using(var cb=new CommandBuffer()){cb.SetRenderTarget(new RenderTargetIdentifier[]{albedo,emission},albedo);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,pass);Graphics.ExecuteCommandBuffer(cb);}
   float visibility=Mathf.Clamp01(1+(Mathf.Pow(.55f,.8f)-1)*2);
   float expected=offset>=-.00001f ? visibility : (visibility+1)*.5f;
   Require(Mathf.Abs(Read(albedo).a-expected)<.00002f,"native motion-depth classification tolerance mismatch");
  }
  // Invalid normal data must fall back, never generate NaNs or lighten arbitrary surfaces.
  Clear(normal,new Color(.5f,.5f,.5f,1));Clear(ao,new Color(.8f,10,0,0));Clear(albedo,Color.white);Clear(emission,Color.white);
  using(var cb=new CommandBuffer()){cb.SetRenderTarget(new RenderTargetIdentifier[]{albedo,emission},albedo);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,1);Graphics.ExecuteCommandBuffer(cb);}
  Require(Mathf.Abs(Read(albedo).a-Mathf.Clamp01(1+(Mathf.Pow(.8f,.8f)-1)*2))<.00002f,"invalid-normal fallback");
  foreach(var t in new[]{ao,source,albedo,emission,diffuse,spec,normal,depth,motionDepth,emissionSource}){t.Release();Object.DestroyImmediate(t);}Object.DestroyImmediate(mat);
  results.Add("PASS: "+cases+" reflection AO cases including "+grazingCases+" effective grazing-angle cases: native vegetation path unchanged, smoothness-squared relief, at least half the screen occlusion retained, authored material AO, zero-alpha, legacy off, diffuse HDR/LDR preservation and invalid-normal fallback; 24 motion-depth tolerance cases with lower-resolution input.");
 }
 private static void ReflectionOcclusionLifecycle(GpuAssets assets)
 {
  var main=Scene(64,64,out var mainTarget,out var geometry);
  var optic=new GameObject("Reflection optic").AddComponent<Camera>();optic.CopyFrom(main);optic.enabled=false;
  var opticTarget=new RenderTexture(48,48,24,RenderTextureFormat.ARGBHalf);opticTarget.Create();optic.targetTexture=opticTarget;
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("reflection-lifecycle.cfg"),false){SaveOnConfigSet=false});
  var field=typeof(CameraOcclusion).GetField("diffuseOcclusion",BindingFlags.Instance|BindingFlags.NonPublic);
  using(var first=new CameraOcclusion(main,assets)) using(var second=new CameraOcclusion(optic,assets))
  {
   foreach(bool hdr in new[]{true,false})
   {
    main.allowHDR=hdr;optic.allowHDR=hdr;
    settings.ReflectionOcclusion.Value=true;first.Record(settings,2,true,true);
    settings.ReflectionOcclusion.Value=false;second.Record(settings,2,true,false);
    main.Render();optic.Render();
    var captured=field.GetValue(first) as RenderTexture;
    var opticCaptured=field.GetValue(second) as RenderTexture;
    Require(captured!=null && captured.IsCreated() && captured.width==64 && opticCaptured!=null && opticCaptured.width==48 && captured!=opticCaptured,"main/optic protected diffuse resource isolation");
    Require(Shader.GetGlobalFloat("_FTFX_CA_DiffuseCaptured")==0,"diffuse visibility leaked beyond camera");
    first.Record(settings,2,true,true);Require(field.GetValue(first)==captured,"reflection off discarded protected diffuse AO");
    settings.ReflectionOcclusion.Value=true;settings.Ambient.Value=false;first.Record(settings,2,true,true);
    Require(field.GetValue(first)==null,"disabled ambient allowed inconsistent reflection/diffuse data");settings.Ambient.Value=true;
    settings.AmbientStrength.Value=0;first.Record(settings,2,true,true);
    Require(field.GetValue(first)==null,"zero ambient strength retained protected diffuse AO");settings.AmbientStrength.Value=60;
    first.Record(settings,2,false,true);Require(field.GetValue(first)==null,"unavailable ambient allowed reflection fix");
    second.Record(settings,2,true,false);optic.Render();Require(((RenderTexture)field.GetValue(second)).width==48,"optic correction did not use its own dimensions");
   }
   mainTarget.Release();mainTarget.width=80;mainTarget.height=60;mainTarget.Create();
   settings.ReflectionOcclusion.Value=false;first.Record(settings,2,true,true);main.Render();
   var resized=field.GetValue(first) as RenderTexture;
   Require(resized!=null && resized.width==80 && resized.height==60,"protected diffuse AO did not follow camera resize with reflection fix off");
  }
  optic.targetTexture=null;Object.DestroyImmediate(optic.gameObject);opticTarget.Release();Object.DestroyImmediate(opticTarget);DestroyScene(main,mainTarget,geometry);
  results.Add("PASS: protected diffuse visibility survives reflection toggles; main/optic resources, HDR/LDR, zero-strength/disabled/unavailable ambient gates, resize and per-camera global cleanup.");
 }
}
