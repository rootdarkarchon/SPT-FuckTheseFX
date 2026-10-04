using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
public static partial class OcclusionValidation {
 private static Color[] BleedPixels(RenderTexture rt) {
  var previous=RenderTexture.active;RenderTexture.active=rt;var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();var pixels=t.GetPixels();UnityEngine.Object.DestroyImmediate(t);RenderTexture.active=previous;return pixels;
 }
 private static void NrdDiffuseValidation(GpuAssets assets) {
  NrdDiffuseBackend.ValidationLibrary=Path.GetFullPath("Output/FTFXNrd.dll");
  var probe=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/ContactGIProbe.compute");
  Require(probe!=null&&ShaderUtil.GetComputeShaderMessages(probe).Length==0,"diffuse probe availability");
  int kernel=probe.FindKernel("Probe"),w=65,h=37;
  var noisy=GpuAssets.Target("native diffuse input",w,h,GraphicsFormat.R16G16B16A16_SFloat,true);
  var normal=GpuAssets.Target("native diffuse normal",w,h,GraphicsFormat.A2B10G10R10_UNormPack32,true);
  var depth=GpuAssets.Target("native diffuse depth",w,h,GraphicsFormat.R32_SFloat,true);
  var motion=GpuAssets.Target("native diffuse motion",w,h,GraphicsFormat.R16G16B16A16_SFloat,true);
  var output=GpuAssets.Target("native diffuse output",w,h,GraphicsFormat.R16G16B16A16_SFloat,true);
  var expected=GpuAssets.Target("reference",w,h,GraphicsFormat.R32G32B32A32_SFloat,true);
  var validation=GpuAssets.Target("validation",w,h,GraphicsFormat.R8G8B8A8_UNorm,true);
  var camera=new GameObject("NRD contract camera").AddComponent<Camera>();camera.enabled=false;
  var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);projection.SetRow(1,-projection.GetRow(1));
  probe.SetTexture(kernel,"_Probe",expected);probe.SetTexture(kernel,"_Radiance",noisy);probe.SetTexture(kernel,"_Normal",normal);probe.SetTexture(kernel,"_ViewZ",depth);probe.SetTexture(kernel,"_Motion",motion);probe.SetTexture(kernel,"_BlueNoise",assets.ColorBleedNoise);
  var raster=new ComputeBuffer(17,16);raster.SetData(new Vector4[17]);probe.SetBuffer(kernel,"_GIRaster",raster);
  using(var backend=new NrdDiffuseBackend(w,h)) {
   for(int mode=2;mode<=5;mode++)for(int frame=0;frame<40;frame++) {
    probe.SetInt("_ProbeMode",mode);probe.SetInt("_ProbeFrame",frame);probe.Dispatch(kernel,(w+7)/8,(h+7)/8,1);
    var f=new NrdDiffuseBackend.Frame{Projection=projection,PreviousProjection=projection,View=camera.worldToCameraMatrix,PreviousView=camera.worldToCameraMatrix,Reset=frame==0?1u:0,Frames=32,Index=(uint)frame,AntiFirefly=1,DeltaMs=16.666667f,Version=1,Radius=.75f,Validation=1};
    var cb=new CommandBuffer();backend.Queue(cb,noisy,normal,depth,motion,output,validation,f);Graphics.ExecuteCommandBuffer(cb);cb.Dispose();
    var actual=Read(output); // Fixture-only fence, never used by production.
    Require(backend.Stats.Status>=0,"actual REBLUR_DIFFUSE execution: "+backend.Error);
    Require(!float.IsNaN(actual.r)&&!float.IsInfinity(actual.r)&&actual.a>=0&&actual.a<=1,"finite diffuse output");
    Require(backend.Stats.AdapterBytes==0,"diffuse allocated SSR packing/coverage adapters");
    // Packed upstream output is YCoCg. Constant radiance must converge without
    // a brightness floor, including black surfaces with nonzero hit distance.
    if(frame==39&&mode!=4) {
     var raw=Read(noisy);Require(Mathf.Abs(actual.r-raw.r)<.01f,"constant/black NRD mean bias mode="+mode+" output="+actual+" packed="+raw+" frames="+backend.Stats.Frames+" status="+backend.Stats.Status+" depth="+Read(depth));
     Require(raw.a>.35f&&raw.a<.45f,"brightness-independent normalized hit distance");
    }
   }
   Require(backend.Stats.Frames==160&&backend.Stats.Dispatches>1,"diffuse did not execute upstream dispatch list");
   Require(backend.ViewBuilds==1&&backend.NativeTextureQueries==6,"diffuse views/pointers rebuilt on stable resources");
   for(int frame=0;frame<48;frame++) {
    probe.SetInt("_ProbeMode",frame<40?2:5);probe.SetInt("_ProbeFrame",frame);probe.Dispatch(kernel,(w+7)/8,(h+7)/8,1);
    var stepFrame=new NrdDiffuseBackend.Frame{Projection=projection,PreviousProjection=projection,View=camera.worldToCameraMatrix,PreviousView=camera.worldToCameraMatrix,Reset=frame==0?1u:0,Frames=32,Index=(uint)frame,AntiFirefly=1,DeltaMs=16.666667f,Version=1,Radius=.75f};
    using(var commands=new CommandBuffer()){backend.Queue(commands,noisy,normal,depth,motion,output,null,stepFrame);Graphics.ExecuteCommandBuffer(commands);}
    if(frame==47)results.Add("MEASURED: synthetic eight-render lighting-step relative packed-luminance error="+(Mathf.Abs(Read(output).r-Read(noisy).r)/Read(noisy).r).ToString("R")+". Diagnostic measurement, not an in-game visual-quality verdict.");
   }
   Require(backend.ViewBuilds==2&&backend.NativeTextureQueries==6,"validation change failed to invalidate native view cache");
   var replacement=GpuAssets.Target("replacement input generation",w,h,GraphicsFormat.R16G16B16A16_SFloat,true);
   var cancelled=new CommandBuffer();var cancelFrame=new NrdDiffuseBackend.Frame{Version=1,Radius=.75f};backend.Queue(cancelled,replacement,normal,depth,motion,output,null,cancelFrame);backend.Cancel();Graphics.ExecuteCommandBuffer(cancelled);cancelled.Dispose();Require(backend.Stats.Pending==0,"diffuse cancellation");
   Require(backend.ViewBuilds==3&&backend.NativeTextureQueries==7,"new resource generation reused stale view/pointer");GpuAssets.Release(ref replacement);
  }
  probe.SetInt("_ProbeMode",0);double mean=0;
  for(int i=0;i<32;i++){probe.SetInt("_ProbeFrame",i);probe.Dispatch(kernel,(w+7)/8,(h+7)/8,1);var v=Read(expected);Require(v.b>=0&&Mathf.Abs(v.a-1)<.002,"cosine hemisphere unit direction");mean+=v.b;}
  Require(mean/32>.60&&mean/32<.73,"cosine PDF distribution");
  probe.SetVector("_GILimits",new Vector4(2,.2f,1,100));probe.SetInt("_ProbeMode",7);probe.Dispatch(kernel,(w+7)/8,(h+7)/8,1);
  var gamma=Read(expected);float[] original={1.2f,.05f,.3f},addition={.02f,.1f,.4f};
  for(int i=0;i<3;i++)Require(Mathf.Abs(gamma[i]-(EncodeGI(DecodeGI(original[i])+addition[i])-original[i]))<.0001f,"exact Gamma decode/add/encode");
  Require(gamma.a==0,"additive alpha contract");
  GpuAssets.Release(ref noisy);GpuAssets.Release(ref normal);GpuAssets.Release(ref depth);GpuAssets.Release(ref motion);GpuAssets.Release(ref output);GpuAssets.Release(ref expected);GpuAssets.Release(ref validation);UnityEngine.Object.DestroyImmediate(camera.gameObject);
  raster.Dispose();
  results.Add("PASS: actual NRD diffuse native execution, one denoiser/no adapters, finite black/bright/colour inputs, hit-distance independence, cosine sampling, cancellation and odd dimensions.");
 }
 private static float DecodeGI(float c)=>c<=.04045f?c/12.92f:c<1?Mathf.Pow((c+.055f)/1.055f,2.4f):Mathf.Pow(c,2.2f);
 private static float EncodeGI(float c)=>c<=.0031308f?c*12.92f:c<1?1.055f*Mathf.Pow(c,1/2.4f)-.055f:Mathf.Pow(c,1/2.2f);
}
