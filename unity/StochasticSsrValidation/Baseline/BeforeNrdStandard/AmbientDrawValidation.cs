using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using FuckTheseFX.Source.Occlusion;
using FuckTheseFX.Source.StochasticSSR;

public static partial class OcclusionValidation
{
 private static int researchDraws;
 private static IEnumerable<CodeInstruction> ResearchDrawFixture(IEnumerable<CodeInstruction> instructions)
 {
  var draw=AccessTools.Method(typeof(CommandBuffer),nameof(CommandBuffer.DrawMesh),new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(int),typeof(int),typeof(MaterialPropertyBlock)});
  int count=0;
  foreach(var i in instructions)
  {
   if(i.Calls(draw)) {i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(OcclusionValidation),nameof(ResearchForward));count++;}
   yield return i;
  }
  Require(count==1,"Research wrapper lost native draw call");
 }
 private static void ResearchForward(CommandBuffer cb,Mesh mesh,Matrix4x4 matrix,Material material,int sub,int pass,MaterialPropertyBlock properties)
 { researchDraws++;cb.DrawMesh(mesh,matrix,material,sub,pass,properties); }
 private static void AmbientDrawInstrumentation(GpuAssets assets)
 {
  var camera=Scene(64,64,out var target,out var geometry);
  var output=GpuAssets.Target("Actual ambient diagnostic",64,64,GraphicsFormat.R8G8B8A8_UNorm,true);
  var settings=new OcclusionSettings(new BepInEx.Configuration.ConfigFile(System.IO.Path.GetFullPath("ambient-draw.cfg"),false){SaveOnConfigSet=false});
  settings.View.Value=OcclusionView.BentSHDifference;
  var material=new Material(assets.Ambient);material.EnableKeyword("FTFX_BENT_DIAGNOSTICS");material.EnableKeyword("UNITY_HDR_ON");
  var mesh=new Mesh{vertices=new[]{new Vector3(-1,-1,.1f),new Vector3(1,-1,.1f),new Vector3(-1,1,.1f),new Vector3(1,1,.1f)},triangles=new[]{0,1,3,3,2,0}};
  var native=new AmbientLight(material){Quad=mesh};var camSettings=new AmbientLight.CamSettings();
  var hook=AccessTools.Method(typeof(AmbientLight),nameof(AmbientLight.InitAmbientBuffer));
  var bridge=new AmbientOcclusionBridge(assets.Ambient,settings,c=>true,c=>c==camera?output:null);
  var instanceField=AccessTools.Field(typeof(AmbientOcclusionBridge),"instance");instanceField.SetValue(null,bridge);
  var owner=new Harmony("ftfx.diagnostic.test");var research=new Harmony("com.matsix.customambient.research");
  var ours=new HarmonyMethod(typeof(AmbientOcclusionBridge),"InstrumentDraw"){priority=Priority.First,before=new[]{research.Id}};
  var theirs=new HarmonyMethod(typeof(OcclusionValidation),nameof(ResearchDrawFixture));
  try
  {
   AmbientReflectionCapture.Requested=()=>true;AmbientReflectionCapture.IsMainCamera=c=>c==camera;
   AmbientSurfaceCapture.Requested=()=>true;AmbientSurfaceCapture.IsMainCamera=c=>c==camera;
   material.EnableKeyword("FTFX_REFLECTION_CAPTURE");
   camera.AddCommandBuffer(CameraEvent.AfterLighting,camSettings.CbAmbient);
   for(int order=0;order<2;order++)
   {
    if(order==0){research.Patch(hook,transpiler:theirs);owner.Patch(hook,transpiler:ours);}
    else {owner.Patch(hook,transpiler:ours);research.Patch(hook,transpiler:theirs);}
    settings.View.Value=OcclusionView.BentSHDifference;Clear(output,Color.cyan);researchDraws=0;
    native.InitAmbientBuffer(camSettings,camera);camera.Render();
    var captured=Read(output);Require(captured.r==1 && captured.g==0 && captured.b==0,"Actual native fragment was not captured through research wrapper: "+captured);
    Require(researchDraws==1,"Research draw skipped/duplicated");Require(Shader.GetGlobalFloat("_FTFX_CA_DebugAvailable")==0,"Diagnostic UAV availability leaked");
    var reflectionCapture=AmbientReflectionCapture.Get(camera,64,64);
    Require(reflectionCapture!=null && Read(reflectionCapture).a==1,"Reflection UAV did not coexist with ambient debug UAV through actual draw hook");
    Require(Shader.GetGlobalFloat("_FTFX_CA_ReflectionCaptureEnabled")==0,"Reflection capture availability leaked past ambient draw");
    var surfaceCapture=AmbientSurfaceCapture.Get(camera,64,64);
    Require(surfaceCapture!=null && Read(surfaceCapture).a==1,"Pre-ambient surface capture did not run through actual draw hook");
    Require(Shader.GetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled")==0,"Surface capture availability leaked past ambient draw");
    settings.View.Value=OcclusionView.Normal;Clear(output,Color.cyan);native.InitAmbientBuffer(camSettings,camera);camera.Render();
    var disabled=Read(output);Require(disabled.r==0 && disabled.g==1 && disabled.b==1,"Normal view still wrote debug UAV");
    Require(Read(reflectionCapture).a==1,"Reflection capture was incorrectly coupled to AO debug view");
    AmbientReflectionCapture.Requested=()=>false;
    native.InitAmbientBuffer(camSettings,camera);camera.Render();
    Require(Read(surfaceCapture).a==1 && Shader.GetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled")==0,"Surface capture must work and unbind independently of reflection/debug captures");
    AmbientReflectionCapture.Requested=()=>true;
    owner.UnpatchSelf();research.UnpatchSelf();
   }
  }
  finally
  {
   owner.UnpatchSelf();research.UnpatchSelf();instanceField.SetValue(null,null);bridge.Dispose();
   AmbientReflectionCapture.Dispose();
   AmbientSurfaceCapture.Dispose();
   camera.RemoveCommandBuffer(CameraEvent.AfterLighting,camSettings.CbAmbient);camSettings.CbAmbient.Dispose();
   GpuAssets.Release(ref output);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(mesh);DestroyScene(camera,target,geometry);
  }
  results.Add("PASS: actual ambient draw UAV capture, simultaneous reflection/debug UAVs, research wrapper preserved in both patch installation orders, independent Normal-view bypass and cleanup.");
 }
}
