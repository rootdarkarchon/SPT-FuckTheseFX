using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class OcclusionValidation
{
 private static void DistanceFading(GpuAssets assets)
 {
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("distance-fade.cfg"),false){SaveOnConfigSet=false});
  var material=Material(assets);
  var ao=GpuAssets.Target("fade AO",16,16,GraphicsFormat.R32G32_SFloat);
  var albedo=GpuAssets.Target("fade albedo",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var emission=GpuAssets.Target("fade emission",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var source=GpuAssets.Target("fade source",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  material.SetVector("_Size",new Vector4(16,16,1,1));
  material.SetTexture("_PrimaryAO",ao);material.SetTexture("_Emission",source);
  foreach(var range in new[]{new Vector2(35,100),new Vector2(60,150),new Vector2(0,1),new Vector2(60,60),new Vector2(60,20)})
  {
   settings.FadeStart.Value=range.x;settings.FadeEnd.Value=range.y;
   var curve=settings.DistanceFade;material.SetVector("_AOFade",curve);
   float end=Mathf.Max(range.x+1,range.y);
   Require(settings.FadeEnd.Value==range.y,"normalization rewrote saved fade end");
   foreach(float depth in new[]{0,range.x,(range.x+end)*.5f,end,end+10})
   {
    float fade=Mathf.Clamp01((depth-range.x)/(end-range.x));
    float expected=Mathf.Clamp01(1+(Mathf.Pow(.8f,Mathf.Lerp(.8f,1,fade))-1)*Mathf.Lerp(2,0,fade));
    Clear(ao,new Color(.8f,depth,0,0));Clear(source,new Color(.5f,.5f,.5f,1));
    for(int pass=1;pass<=2;pass++)
    {
     Clear(albedo,new Color(.2f,.3f,.4f,.6f));Clear(emission,new Color(.5f,.5f,.5f,1));
     using(var cb=new CommandBuffer())
     {
      cb.SetRenderTarget(new RenderTargetIdentifier[]{albedo,emission},albedo);
      cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,pass);Graphics.ExecuteCommandBuffer(cb);
     }
     Require(Mathf.Abs(Read(albedo).a-.6f*expected)<.0001f,"fade material AO mismatch");
     Require(Mathf.Abs(Read(emission).r-(pass==1?.5f*expected:Mathf.Pow(.5f,expected)))<.0001f,"fade HDR/LDR mismatch");
    }
    foreach(int view in new[]{3,6})
    {
     material.SetVector("_Debug",new Vector4(view,0,1,0));
     using(var cb=new CommandBuffer())
     { cb.SetRenderTarget(emission);cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,5);Graphics.ExecuteCommandBuffer(cb); }
     Require(Mathf.Abs(Read(emission).r-(view==6?expected:.8f))<.0001f,"combined/raw diagnostic fade mismatch");
    }
   }
  }
  GpuAssets.Release(ref ao);GpuAssets.Release(ref albedo);GpuAssets.Release(ref emission);GpuAssets.Release(ref source);
  Object.DestroyImmediate(material);

  // Record both cameras before rendering, as main/optic sequencing must not share material state.
  var camera=Scene(64,64,out var target,out var geometry);
  var optic=new GameObject("Fade optic camera").AddComponent<Camera>();optic.CopyFrom(camera);optic.enabled=false;optic.fieldOfView=20;
  var opticTarget=new RenderTexture(64,64,24,RenderTextureFormat.ARGBHalf);opticTarget.Create();optic.targetTexture=opticTarget;
  var field=typeof(CameraOcclusion).GetField("composite",BindingFlags.Instance|BindingFlags.NonPublic);
  using(var first=new CameraOcclusion(camera,assets))
  using(var second=new CameraOcclusion(optic,assets))
  {
   settings.Actors.Value=false;settings.Ambient.Value=true;settings.AmbientStrength.Value=100;
   for(int iteration=0;iteration<2;iteration++)
   {
    settings.FadeStart.Value=iteration==0?35:60;settings.FadeEnd.Value=iteration==0?100:150;
    var firstCurve=settings.DistanceFade;first.Record(settings,2,true);
    settings.FadeStart.Value=0;settings.FadeEnd.Value=1;
    var secondCurve=settings.DistanceFade;second.Record(settings,2,true);
    foreach(bool main in new[]{true,false,true})
    {
     (main?camera:optic).Render();
     var expected=main?firstCurve:secondCurve;
     Require(Shader.GetGlobalVector("_FTFX_CA_AOFade")==expected,"ambient fade not owned by rendered camera");
     Require(((Material)field.GetValue(main?first:second)).GetVector("_AOFade")==expected,"primary/ambient fade diverged");
    }
   }
  }
  optic.targetTexture=null;Object.DestroyImmediate(optic.gameObject);opticTarget.Release();Object.DestroyImmediate(opticTarget);
  DestroyScene(camera,target,geometry);
  results.Add("PASS: configurable AO fade defaults, custom/endpoints, equal/reversed ranges, HDR/LDR, CombinedAO/raw views, live changes and main/optic camera isolation.");
 }
}
