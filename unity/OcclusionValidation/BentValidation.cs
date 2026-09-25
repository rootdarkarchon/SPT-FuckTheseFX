using System;
using System.Linq;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class OcclusionValidation
{
 private static void BentCameraIsolation(GpuAssets assets)
 {
  var camera=Scene(128,96,out var target,out var geometry);
  var other=new GameObject("Other AO camera").AddComponent<Camera>();other.CopyFrom(camera);other.enabled=false;
  var otherTarget=new RenderTexture(128,96,24,RenderTextureFormat.ARGBHalf);otherTarget.Create();other.targetTexture=otherTarget;
  var config=new BepInEx.Configuration.ConfigFile(System.IO.Path.GetFullPath("bent-camera.cfg"),false){SaveOnConfigSet=false};
  var settings=new OcclusionSettings(config);settings.Actors.Value=false;settings.View.Value=OcclusionView.BentNormals;
  var field=typeof(CameraOcclusion).GetField("primary",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  using(var first=new CameraOcclusion(camera,assets))
  using(var second=new CameraOcclusion(other,assets))
  {
   settings.BentNormals.Value=true;first.Record(settings,2,true);
   settings.BentNormals.Value=false;second.Record(settings,2,true);
   camera.Render();var direction=Read(first.DebugTexture);
   Require(Mathf.Abs(direction.r-.5f)+Mathf.Abs(direction.g-.5f)+Mathf.Abs(direction.b-.5f)>.1f,"active bent debug has no direction");
   other.Render();var inactive=Read(second.DebugTexture);
   Require(Mathf.Abs(inactive.r-.5f)<.003f && Mathf.Abs(inactive.g-.5f)<.003f && Mathf.Abs(inactive.b-.5f)<.003f,"inactive bent debug not neutral");
   Require(((OcclusionPipeline)field.GetValue(first)).BentNormals && !((OcclusionPipeline)field.GetValue(second)).BentNormals,"camera variant state shared");
   // Rotate the camera and geometry together: XeGTAO view-space estimates stay the same,
   // while the displayed world-space direction must rotate with the scene.
   var rotation=Quaternion.Euler(0,65,0);camera.transform.rotation=rotation;
   foreach(var go in geometry){go.transform.position=rotation*go.transform.position;go.transform.rotation=rotation;}
   settings.BentNormals.Value=true;first.Record(settings,2,true);camera.Render();var rotated=Read(first.DebugTexture);
   var expected=rotation*new Vector3(direction.r*2-1,direction.g*2-1,direction.b*2-1).normalized;
   var actual=new Vector3(rotated.r*2-1,rotated.g*2-1,rotated.b*2-1).normalized;
   Require(Vector3.Dot(expected,actual)>.995f,"world bent direction does not follow camera/scene rotation");
   settings.BentNormals.Value=false;first.Record(settings,2,true);camera.Render();
   Require(Mathf.Abs(Read(first.DebugTexture).r-.5f)<.003f,"toggle off left stale bent debug");
   Require(Shader.GetGlobalFloat("_FTFX_CA_BentAvailable")==0,"bent availability leaked between cameras");
  }
  other.targetTexture=null;UnityEngine.Object.DestroyImmediate(other.gameObject);otherTarget.Release();UnityEngine.Object.DestroyImmediate(otherTarget);
  DestroyScene(camera,target,geometry);
  results.Add("PASS: independent camera variants, world-space bent debug rotation, inactive/toggled-off debug and global cleanup.");
 }
 private static void BentPipeline(GpuAssets assets)
 {
  const int w=131,h=99;
  var camera=Scene(w,h,out var target,out var geometry);
  var material=Material(assets);
  var resolved=GpuAssets.Target("bent resolve",w,h,GraphicsFormat.R32G32B32A32_SFloat);
  int comparisons=0;float maximumDelta=0;
  using(var scalar=new OcclusionPipeline(assets.Compute,false))
  using(var bent=new OcclusionPipeline(assets.Compute,false))
  {
   foreach(bool normals in new[]{true,false})
   for(int quality=0;quality<5;quality++)
   for(int passes=0;passes<=3;passes++)
   {
    var p=new Parameters{Radius=1,Multiplier=1.457f,Falloff=.615f,Distribution=2.5f,Sharpness=.4f,Power=1.5f,Denoise=passes,Normals=normals,
     Slices=quality==4?4:0,Steps=quality==4?4:0};
    scalar.Prepare(w,h,passes);bent.Prepare(w,h,passes);
    var cb=new CommandBuffer();scalar.Record(cb,camera,Math.Min(quality,3),p);bent.Record(cb,camera,Math.Min(quality,3),p,bentNormals:true);
    material.SetFloat("_BentNormals",1);material.SetVector("_Size",new Vector4(w,h,100f/65504,passes==0?1.5f:1));
    var properties=new MaterialPropertyBlock();properties.SetTexture("_XeAO",bent.Result);properties.SetTexture("_XeDepth",bent.Depth);
    cb.SetRenderTarget(resolved);cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,0,properties);
    cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
    camera.Render();camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);cb.Dispose();
    var a=ReadUInt(scalar.Result);var b=ReadUInt(bent.Result);
    var read=AsyncGPUReadback.Request(resolved,0);read.WaitForCompletion();Require(!read.hasError,"bent resolve readback");var pixels=read.GetData<Color>();
    for(int i=0;i<a.Length;i++)
    {
     uint visibility=b[i]>>24;float delta=Mathf.Abs(Math.Min(255f,a[i])-visibility);maximumDelta=Math.Max(maximumDelta,delta);comparisons++;
     Require(delta<=3,"bent visibility changed beyond quantization: "+delta+" passes="+passes+" quality="+quality+" index="+i+" scalar="+a[i]+" bent="+visibility);
     var n=new Vector3((b[i]&255)/127.5f-1,((b[i]>>8)&255)/127.5f-1,((b[i]>>16)&255)/127.5f-1);
     Require(n.sqrMagnitude>.1f && n.sqrMagnitude<1.1f,"invalid packed bent normal "+n);
     Require(Mathf.Abs(pixels[i].r-Mathf.Clamp01(visibility/255f*(passes==0?1.5f:1)))<.00001f,"packed visibility resolve/zero-denoise scaling");
    }
   }
   // A depth-only sky frame must replace previous occluders with neutral AO and valid directions.
   foreach(var go in geometry)go.SetActive(false);
   for(int passes=0;passes<=3;passes++)
   {
    var p=new Parameters{Radius=1,Multiplier=1.457f,Falloff=.615f,Distribution=2.5f,Sharpness=.4f,Power=1.5f,Denoise=passes,Normals=true};
    bent.Prepare(w,h,passes);var cb=new CommandBuffer();bent.Record(cb,camera,2,p,bentNormals:true);
    camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);camera.Render();camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);cb.Dispose();
    Require(ReadUInt(bent.Result).All(v=>(v>>24)==(passes==0?170:255)),"sky neutral packed visibility");
   }
  }
  GpuAssets.Release(ref resolved);UnityEngine.Object.DestroyImmediate(material);DestroyScene(camera,target,geometry);
  results.Add("PASS: bent/scalar visibility at all four presets and custom samples, both normal sources, denoise 0-3, odd resolution and sky replacement: "+comparisons+" pixels, max quantization delta="+maximumDelta+"/255.");
 }
}
