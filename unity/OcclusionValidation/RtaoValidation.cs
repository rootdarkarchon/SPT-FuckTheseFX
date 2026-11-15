using System.IO;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class OcclusionValidation
{
 private static float RtaoCurve(float fade) => Mathf.Clamp01(1+(Mathf.Pow(.8f,Mathf.Lerp(.8f,1,fade))-1)*Mathf.Lerp(2,0,fade));
 // The fixture texture is constant R=.5 (V_hid); the term uses the same distance fade as the XeGTAO curve.
 private static float RtaoExpected(float fade,float k) => RtaoCurve(fade)*Mathf.Lerp(1,.5f,k*(1-fade));
 private static float RtaoCombined(GpuAssets assets,Material material,RtaoInput input,RenderTexture output)
 {
  input.Apply(material);
  using(var cb=new CommandBuffer())
  { cb.SetRenderTarget(output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,5);Graphics.ExecuteCommandBuffer(cb); }
  return Read(output).r;
 }
 private static void RtaoComposite(GpuAssets assets)
 {
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("rtao.cfg"),false){SaveOnConfigSet=false});
  settings.FadeStart.Value=35;settings.FadeEnd.Value=100;
  var material=Material(assets);
  var ao=GpuAssets.Target("rtao AO",16,16,GraphicsFormat.R32G32_SFloat);
  var hidden=GpuAssets.Target("rtao hidden visibility",16,16,GraphicsFormat.R16_SFloat);
  var output=GpuAssets.Target("rtao output",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var albedo=GpuAssets.Target("rtao albedo",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var emission=GpuAssets.Target("rtao emission",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  var source=GpuAssets.Target("rtao source",16,16,GraphicsFormat.R32G32B32A32_SFloat);
  material.SetVector("_Size",new Vector4(16,16,1,1));
  material.SetVector("_AOFade",settings.DistanceFade);
  material.SetVector("_Debug",new Vector4(6,0,1,0));
  material.SetTexture("_PrimaryAO",ao);material.SetTexture("_Emission",source);
  Clear(hidden,new Color(.5f,0,0,0));Clear(source,new Color(.5f,.5f,.5f,1));
  float end=settings.FadeEnd.Value,start=settings.FadeStart.Value,range=end-start;
  int checkedCases=0;
  foreach(float depth in new[]{0,start,start+range*.25f,start+range*.5f,end,end+10})
  {
   float fade=Mathf.Clamp01((depth-start)/range);
   Clear(ao,new Color(.8f,depth,0,0));
   float none=RtaoCombined(assets,material,RtaoInput.None,output);
   Require(Mathf.Abs(none-RtaoCurve(fade))<.0001f,"unaccepted RT input changed the XeGTAO curve");
   foreach(float k in new[]{0f,.5f,1f})
   {
    float actual=RtaoCombined(assets,material,new RtaoInput(hidden,k),output);
    if(k==0) Require(actual==none,"zero RT strength is not bit-identical to the unaccepted result depth="+depth);
    Require(Mathf.Abs(actual-RtaoExpected(fade,k))<.0001f,"RT term mismatch depth="+depth+" k="+k+" actual="+actual+" expected="+RtaoExpected(fade,k));
    if(fade>=1) Require(Mathf.Abs(actual-none)<.000001f,"RT term did not vanish with the AO distance fade depth="+depth);
    checkedCases++;
   }
  }
  // Real composite passes: HDR (1) and LDR (2) use the same Primary() including the RT term.
  float composeDepth=start+range*.5f,composeFade=.5f;
  Clear(ao,new Color(.8f,composeDepth,0,0));
  for(int pass=1;pass<=2;pass++)
  {
   new RtaoInput(hidden,1).Apply(material);
   float expected=RtaoExpected(composeFade,1);
   Clear(albedo,new Color(.2f,.3f,.4f,.6f));Clear(emission,new Color(.5f,.5f,.5f,1));
   using(var cb=new CommandBuffer())
   {
    cb.SetRenderTarget(new RenderTargetIdentifier[]{albedo,emission},albedo);
    cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,pass);Graphics.ExecuteCommandBuffer(cb);
   }
   Require(Mathf.Abs(Read(albedo).a-.6f*expected)<.0001f,"RT composite material AO mismatch pass="+pass);
   Require(Mathf.Abs(Read(emission).r-(pass==1?.5f*expected:Mathf.Pow(.5f,expected)))<.0001f,"RT composite emission mismatch pass="+pass);
  }
  // Acceptance of the published state, through the real Accept() path.
  var stateId=Shader.PropertyToID("_FTR_RtaoState");var textureId=Shader.PropertyToID("_FTR_HiddenVisibility");
  try
  {
   settings.RtEnabled.Value=true;settings.RtStrength.Value=50;
   Shader.SetGlobalVector(stateId,Vector4.zero);Shader.SetGlobalTexture(textureId,hidden);
   Require(!FtrRtao.Accept(settings).Accepted,"an empty FTR state was accepted");
   foreach(var bad in new[]{new Vector4(Time.frameCount,0,1,0),new Vector4(Time.frameCount,1,2,0),new Vector4(Time.frameCount-2,1,1,0),new Vector4(Time.frameCount+1,1,1,0)})
   { Shader.SetGlobalVector(stateId,bad);Require(!FtrRtao.Accept(settings).Accepted,"an invalid or stale FTR state was accepted: "+bad); }
   Shader.SetGlobalVector(stateId,new Vector4(Time.frameCount-1,1,1,0));
   Require(FtrRtao.Accept(settings).Accepted,"a valid FTR state one frame old was rejected");
   Shader.SetGlobalVector(stateId,new Vector4(Time.frameCount,1,1,0));
   Shader.SetGlobalTexture(textureId,(Texture)null);
   Require(!FtrRtao.Accept(settings).Accepted,"a missing FTR texture was accepted");
   Shader.SetGlobalTexture(textureId,hidden);
   settings.RtEnabled.Value=false;Require(!FtrRtao.Accept(settings).Accepted,"RT AO Enabled false accepted FTR state");
   settings.RtEnabled.Value=true;settings.RtStrength.Value=0;Require(!FtrRtao.Accept(settings).Accepted,"zero RT AO strength accepted FTR state");
   settings.RtStrength.Value=50;
   var accepted=FtrRtao.Accept(settings);Require(accepted.Accepted,"valid FTR state rejected");
   Clear(ao,new Color(.8f,start,0,0));
   float actual=RtaoCombined(assets,material,accepted,output);
   Require(Mathf.Abs(actual-RtaoExpected(0,.5f))<.0001f,"accepted 50% input did not reach the composite: "+actual);
   var request=settings.EffectiveRadius;
   Require(Mathf.Abs(request-settings.Radius.Value*settings.RadiusMultiplier.Value)<1e-6f,"effective radius is not EffectRadius*RadiusMultiplier");
   FtrRtao.Request(settings);
   var sent=Shader.GetGlobalVector("_FTFX_RtaoRequest");
   Require(sent.x==Time.frameCount && Mathf.Abs(sent.y-request)<1e-6f && sent.z==settings.RtRadius.Value && sent.w==1,"request vector mismatch: "+sent);
   FtrRtao.ClearRequest();
   Require(Shader.GetGlobalVector("_FTFX_RtaoRequest")==Vector4.zero,"request was not cleared");
  }
  finally { Shader.SetGlobalVector(stateId,Vector4.zero);Shader.SetGlobalTexture(textureId,(Texture)null);FtrRtao.ClearRequest(); }
  GpuAssets.Release(ref ao);GpuAssets.Release(ref hidden);GpuAssets.Release(ref output);GpuAssets.Release(ref albedo);GpuAssets.Release(ref emission);GpuAssets.Release(ref source);
  Object.DestroyImmediate(material);
  results.Add("PASS: RT AO term in the real Primary() composite ("+checkedCases+" curve cases, HDR/LDR passes): Product against the CPU formula after the XeGTAO curve, zero strength bit-identical to no input, term vanishing with the distance fade, and FTR state acceptance/request vector.");
 }
}
