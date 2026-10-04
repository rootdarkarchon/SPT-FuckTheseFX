using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using FuckTheseFX.Source.Occlusion;
public static partial class OcclusionValidation {
 private static void ContactGICameraValidation(GpuAssets assets) {
  var camera=Scene(259,147,out var target,out var geometry);
  foreach(var g in geometry){var m=g.GetComponent<Renderer>().sharedMaterial;m.SetFloat("_Glossiness",0);m.SetColor("_Color",Color.white);}
  var ambient=new CommandBuffer{name="GI fixture fresh ambient"};ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(.6f,.2f,.1f,.7f));camera.AddCommandBuffer(CameraEvent.AfterLighting,ambient);
  var config=new ConfigFile(Path.GetFullPath("diffuse-camera.cfg"),false){SaveOnConfigSet=false};var settings=new OcclusionSettings(config);
  settings.Bleed.Resolution.Value=25;settings.Bleed.Enabled.Value=true;settings.Bleed.Rays.Value=4;settings.Bleed.Radius.Value=1.5f;settings.View.Value=OcclusionView.ScreenSpaceContactGI;settings.ColorBleedGain.Value=1;
  var field=typeof(CameraOcclusion).GetField("bleed",BindingFlags.Instance|BindingFlags.NonPublic);
  using(var effect=new CameraOcclusion(camera,assets)) {
   double rawEnergy=0,denoisedEnergy=0;Color[] previousRaw=null,previousDenoised=null;int hits=0;
   for(int frame=0;frame<96;frame++) {
    effect.Record(settings,0,true,true);var p=(ColorBleedPipeline)field.GetValue(effect);Require(p!=null,"diffuse camera integration failed");var compare=frame==64?AppendContactGIReconstructionValidation(p,assets,effect):null;camera.Render();compare?.Invoke();
    var raw=BleedPixels(p.Bounce);var denoised=BleedPixels(p.Output);
    hits+=raw.Count(v=>v.a>0);Require(raw.All(v=>!float.IsNaN(v.r)&&!float.IsInfinity(v.r)),"finite ray estimator");
    if(frame>=64)for(int i=0;i<raw.Length;i++){rawEnergy+=RGBDelta(raw[i],previousRaw[i]);denoisedEnergy+=RGBDelta(denoised[i],previousDenoised[i]);}
    previousRaw=raw;previousDenoised=denoised;
   }
   var bleed=(ColorBleedPipeline)field.GetValue(effect);
   Require(hits>0&&previousRaw.Any(v=>v.r>0),"diffuse rays produced no contact bounce hits="+hits);
   settings.View.Value=OcclusionView.ScreenSpaceContactGI;
   Require(bleed.NativeStats.Frames==96&&bleed.UsedHistory,"camera NRD history did not advance on renders");
   // Reuse one actual ray phase under black source lighting. Geometry distances
   // must survive independently of emitted/contributed radiance.
   typeof(ColorBleedPipeline).GetField("sequence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bleed,95u);
   ambient.Clear();ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(0,0,0,.7f));
   effect.Record(settings,0,true,true);camera.Render();var black=BleedPixels(bleed.Bounce);
   Require(black.All(v=>v.r==0&&v.g==0&&v.b==0)&&black.Zip(previousRaw,(a,b)=>a.a==b.a).All(v=>v),"black donors changed accepted finite hit distances");
   ambient.Clear();ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(.6f,.2f,.1f,.7f));
   effect.Record(settings,0,true,true);camera.Render();
   // Actual traced HDR donors must respond to limits above one, with unchanged geometry.
   float savedDonorLimit=settings.Bleed.DonorLimit.Value;
   ambient.Clear();ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(6f,2f,1f,.7f));
   settings.Bleed.DonorLimit.Value=1;typeof(ColorBleedPipeline).GetField("sequence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bleed,95u);
   effect.Record(settings,0,true,true);camera.Render();var limited=BleedPixels(bleed.Bounce);
   settings.Bleed.DonorLimit.Value=4;typeof(ColorBleedPipeline).GetField("sequence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bleed,95u);
   effect.Record(settings,0,true,true);camera.Render();var brighter=BleedPixels(bleed.Bounce);
   Require(brighter.Sum(v=>(double)v.r)>limited.Sum(v=>(double)v.r)*1.2,"HDR donor cap response limited="+limited.Sum(v=>(double)v.r)+" higher="+brighter.Sum(v=>(double)v.r));
   Require(brighter.Zip(limited,(a,b)=>a.a==b.a).All(v=>v),"donor cap changed hit distances");
   settings.Bleed.DonorLimit.Value=savedDonorLimit;
   ambient.Clear();ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(.6f,.2f,.1f,.7f));effect.Record(settings,0,true,true);camera.Render();
   Require(bleed.Source.width==259&&bleed.Bounce.width==65&&bleed.Bounce.height==37,"quarter odd receiver dimensions");
   Require(BleedPixels(bleed.Source).All(v=>Mathf.Abs(v.r-.6f)<.002f&&Mathf.Abs(v.a-.7f)<.002f),"fresh source contains GI feedback");
   Require(BleedPixels(target).All(v=>Mathf.Abs(v.a-.7f)<.002f),"diffuse composite changed destination alpha");
   // Independent estimator reference: 1024 renders x 4 rays, before NRD.
   var reference=new double[previousRaw.Length*3];var converged=new double[previousRaw.Length*3];
   for(int frame=0;frame<1024;frame++) {
    effect.Record(settings,0,true,true);camera.Render();var r=BleedPixels(bleed.Bounce);var d=BleedPixels(bleed.Output);
    for(int i=0;i<r.Length;i++){var a=UnpackGI(r[i]);var b=UnpackGI(d[i]);reference[i*3]+=a.r/1024;reference[i*3+1]+=a.g/1024;reference[i*3+2]+=a.b/1024;converged[i*3]+=b.r/1024;converged[i*3+1]+=b.g/1024;converged[i*3+2]+=b.b/1024;}
   }
   double referenceSum=reference.Sum(),convergedSum=converged.Sum();
   File.WriteAllText("diffuse-reference.txt","4096 samples/receiver reference RGB sum="+referenceSum.ToString("R")+" NRD RGB sum="+convergedSum.ToString("R")+". Whole-image incident sum; designated receiver RGB/chroma and barrier acceptance require captured-region analysis.");
   settings.Bleed.Strength.Value=1;effect.Record(settings,0,true,true);camera.Render();Require(bleed.UsedHistory,"strength invalidated incident light history");
   settings.Bleed.ReconstructionCutoff.Value=.005f;effect.Record(settings,0,true,true);camera.Render();Require(bleed.UsedHistory,"reconstruction cutoff invalidated incident light history");settings.Bleed.ReconstructionCutoff.Value=0;
   settings.ColorBleedGain.Value=8;effect.Record(settings,0,true,true);camera.Render();Require(bleed.UsedHistory,"diagnostic gain invalidated incident light history");
   settings.Bleed.Radius.Value=.75f;effect.Record(settings,0,true,true);camera.Render();Require(!bleed.UsedHistory,"transport change did not reset history");
   camera.transform.position+=Vector3.right*3;effect.Record(settings,0,true,true);camera.Render();Require(!bleed.UsedHistory,"camera cut did not reset history");
   settings.Bleed.Temporal.Value=false;effect.Record(settings,0,true,true);camera.Render();Require(!bleed.UsedHistory&&bleed.NativeStats.Status==1,"temporal off disabled spatial NRD");
   settings.Bleed.Capture.Value=true;settings.Bleed.CaptureFrames.Value=8;
   ContactGICapture.ValidationRoot=Path.GetFullPath("Output/ContactGICaptures");
   for(int frame=0;frame<8;frame++){effect.Record(settings,0,true,true);camera.Render();AsyncGPUReadback.WaitAllRequests();}
   Require(!settings.Bleed.Capture.Value,"capture did not stop after requested actual renders");
   File.WriteAllText("contactgi-replay.txt",ContactGICapture.LastFolder);
   settings.Bleed.Resolution.Value=50;effect.Record(settings,0,true,true);camera.Render();Require(bleed.Bounce.width==130&&bleed.Bounce.height==74,"half odd resize");
   settings.Bleed.Temporal.Value=true;
   foreach(int percent in new[]{10,13,20,25,33,50}) {
    settings.Bleed.Resolution.Value=percent;effect.Record(settings,0,true,true);camera.Render();
    Require(bleed.Bounce.width==(259*percent+99)/100&&bleed.Bounce.height==(147*percent+99)/100,"percentage receiver dimensions "+percent);
    Require(!bleed.UsedHistory&&bleed.NativeStats.Status==1,"percentage resize reset/native execution "+percent);
    effect.Record(settings,0,true,true);camera.Render();Require(bleed.UsedHistory==settings.Bleed.Temporal.Value,"percentage stable render history "+percent);
   }
   settings.Bleed.Strength.Value=10;effect.Record(settings,0,true,true);camera.Render();Require(bleed.UsedHistory==settings.Bleed.Temporal.Value,"Strength 10 invalidated incident-light history");
   settings.Bleed.Strength.Value=0;effect.Record(settings,0,true,true);camera.Render();Require(BleedPixels(effect.ColorBleedDebugTexture).All(v=>v.r==0&&v.g==0&&v.b==0),"zero strength reconstructs light");
   settings.View.Value=OcclusionView.Normal;settings.Bleed.Strength.Value=1;effect.Record(settings,0,true,true);camera.Render();Require(bleed.DebugTexture==null,"normal mode retained diagnostics");
   settings.Bleed.Enabled.Value=false;effect.Record(settings,0,true,true);camera.Render();Require(field.GetValue(effect)==null,"disabled GI retained backend");
   results.Add("PASS: diffuse camera contact hits="+hits+", fresh full-res source, alpha, 10-50 percent odd dimensions, Strength/gain preservation, transport/cut reset, spatial-only NRD, optional diagnostics and lifecycle. Stationary RGB adjacent-frame energy raw="+rawEnergy.ToString("R")+" NRD="+denoisedEnergy.ToString("R")+" (diagnostic fixture).");
  }
  camera.RemoveCommandBuffer(CameraEvent.AfterLighting,ambient);ambient.Dispose();target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(camera.gameObject);foreach(var g in geometry){UnityEngine.Object.DestroyImmediate(g.GetComponent<Renderer>().sharedMaterial);UnityEngine.Object.DestroyImmediate(g);}
 }
 private static Color UnpackGI(Color v)=>new Color(v.r+v.g-v.b,v.r+v.b,v.r-v.g-v.b,v.a);
 private static double RGBDelta(Color a,Color b){double r=a.r-b.r,g=a.g-b.g,z=a.b-b.b;return r*r+g*g+z*z;}
}
