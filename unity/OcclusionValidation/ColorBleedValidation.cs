using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public static partial class OcclusionValidation
{
 private static Color[] BleedPixels(RenderTexture target)
 {
  var request=AsyncGPUReadback.Request(target,0,TextureFormat.RGBAFloat);request.WaitForCompletion();
  Require(!request.hasError,"bleed readback");return request.GetData<Color>().ToArray();
 }
 private static Texture2D BleedTexture(int w,int h,Func<int,int,Color> sample)
 {
  var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);var data=new Color[w*h];
  for(int y=0;y<h;y++)for(int x=0;x<w;x++)data[y*w+x]=sample(x,y);
  t.SetPixels(data);t.Apply();return t;
 }
 private static void ColorBleedShaders(GpuAssets assets)
 {
  const int w=128,h=96;
  var camera=Scene(w,h,out var target,out var geometry);
  var mat=new Material(assets.ColorBleed);
  var textures=new System.Collections.Generic.List<Texture2D>();
  Texture2D Tex(Func<int,int,Color> f) { var t=BleedTexture(w,h,f);textures.Add(t);return t; }
  float mx=2/camera.projectionMatrix.m00, my=-2/camera.projectionMatrix.m11;
  float ax=-mx/2, ay=-my/2;
  float Z(int x) => 2/(1+Mathf.Abs(((x+.5f)/w)*mx+ax));
  var depth=Tex((x,y)=>new Color(Z(x),0,0,0));
  var normals=Tex((x,y)=> {
   var n=new Vector3(x<w/2?1:-1,0,-1).normalized;
   var world=camera.cameraToWorldMatrix.MultiplyVector(Vector3.Scale(n,new Vector3(1,-1,-1)));
   return new Color(world.x*.5f+.5f,world.y*.5f+.5f,world.z*.5f+.5f,1);
  });
  float near=.1f,far=100,mul=near*far/(far-near),add=far/(far-near);
  if(SystemInfo.usesReversedZBuffer){mul=-mul;add=-near/(far-near);}
  var raw=Tex((x,y)=>new Color(add-mul/Z(x),0,0,0));
  var lit=Tex((x,y)=>x<w/2?new Color(.02f,.02f,.02f,1):new Color(4,.1f,.05f,1));
  var albedo=Tex((x,y)=>new Color(.8f,.8f,.8f,0)); // zero authored alpha is NOT zero AO
  var spec=Tex((x,y)=>new Color(.04f,.04f,.04f,.2f));
  var visibility=Tex((x,y)=>Color.white);
  var actors=Tex((x,y)=>Color.black);
  var output=GpuAssets.Target("bleed composite test",w,h,GraphicsFormat.R32G32B32A32_SFloat);
  var diagnostic=GpuAssets.Target("bleed diagnostic test",w,h,GraphicsFormat.R16G16B16A16_SFloat);
  void ShowDebug(float gain)
  {
   mat.SetFloat("_CBDebugGain",gain);
   var cb=new CommandBuffer();cb.SetRenderTarget(diagnostic);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,3);
   Graphics.ExecuteCommandBuffer(cb);cb.Dispose();
  }
  mat.SetVector("_CBNDCToView",new Vector4(mx,my,ax,ay));mat.SetMatrix("_CBWorldToView",camera.worldToCameraMatrix);
  mat.SetVector("_CBDepthUnpack",new Vector4(mul,add,far,0));
  mat.SetVector("_CBSettings",new Vector4(.75f,.3f,.02f,25));
  mat.SetTexture("_CBDepth",depth);mat.SetTexture("_CBVisibility",visibility);mat.SetTexture("_CBActors",actors);
  mat.SetTexture("_CameraGBufferTexture0",albedo);mat.SetTexture("_CameraGBufferTexture1",spec);mat.SetTexture("_CameraGBufferTexture2",normals);
  mat.SetTexture("_FTFX_BleedScene",lit);mat.SetTexture("_FTFX_BleedCurrentDepth",raw);
  int nonzero=0;
  foreach(int div in new[]{2,4})
  {
   mat.SetVector("_CBSettings",new Vector4(.75f,.3f,.02f,25));
   var source=GpuAssets.Target("bleed source test",w/div,h/div,GraphicsFormat.R16G16B16A16_SFloat);
   var guide=GpuAssets.Target("bleed guide test",w/div,h/div,GraphicsFormat.R8G8B8A8_UNorm);
   var bounce=GpuAssets.Target("bleed gather test",w/div,h/div,GraphicsFormat.R16G16B16A16_SFloat);
   var filtered=GpuAssets.Target("bleed filtered test",w/div,h/div,GraphicsFormat.R16G16B16A16_SFloat);
   mat.SetVector("_CBSize",new Vector4(w,h,w/div,h/div));
   mat.SetTexture("_CBSource",source);mat.SetTexture("_CBGuide",guide);mat.SetTexture("_CBBounce",bounce);
   mat.SetTexture("_CBSpatialInput",bounce);
   mat.SetTexture("_CBFilteredBounce",filtered);
   void Run(bool capture=true)
   {
    var cb=new CommandBuffer();
    if(capture){cb.SetRenderTarget(new RenderTargetIdentifier[]{source,guide},source);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,0);}
    cb.SetRenderTarget(bounce);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,1);
    cb.SetRenderTarget(filtered);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4);
    cb.SetRenderTarget(output);cb.ClearRenderTarget(false,true,new Color(0,0,0,.37f));cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,2);
    Graphics.ExecuteCommandBuffer(cb);cb.Dispose();
   }
   foreach(int samples in new[]{4,6,8,16,32})
   {
    mat.SetVector("_CBLimits",new Vector4(2,.2f,div,samples));Run();
    var values=BleedPixels(output);
    int redReceivers=0;
    for(int y=h/4;y<3*h/4;y++)for(int x=w/3;x<w/2-2;x++)
    {
     var c=values[y*w+x];if(c.r>.00005f && c.r>c.g*2)redReceivers++;
    }
    nonzero+=redReceivers;
    Require(redReceivers>10,"no visible red bounce on neutral receiver, div="+div+" samples="+samples+" max="+values.Max(c=>c.r));
    Require(values.All(c=>!float.IsNaN(c.r) && !float.IsInfinity(c.r) && c.r>=0 && c.g>=0 && c.b>=0 && Mathf.Abs(c.a-.37f)<.0001f),"finite additive output / alpha");
    Require(values.All(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f<=.4002f),"maximum reflected bounce bound before receiver gates");
    var captured=BleedPixels(source);
    Require(captured.All(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f<=2.003f),"donor luminance cap");
    var initial=values;
    foreach(float gain in new[]{1f,16f})
    {
     ShowDebug(gain);var shown=BleedPixels(diagnostic);
     for(int k=0;k<shown.Length;k++)
     {
      var expected=initial[k]*gain;var actual=shown[k];
      Require(Mathf.Abs(actual.r-expected.r)<.00002f+expected.r*.001f && Mathf.Abs(actual.g-expected.g)<.00002f+expected.g*.001f &&
              Mathf.Abs(actual.b-expected.b)<.00002f+expected.b*.001f && actual.a==1,"debug differs from additive term at "+k+" gain="+gain);
     }
     Require(BleedPixels(output).Zip(initial,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max()==0,"debug draw changed scene RGB");
    }
    Run();Require(BleedPixels(output).Zip(initial,(a,b)=>Mathf.Abs(a.r-b.r)).Max()<.000001f,"fresh repeated render changed (feedback)");
   }
   var fixedSamples=BleedPixels(bounce);
   var geometricGuide=BleedPixels(guide);
   var changedNormals=Tex((x,y)=>new Color(.5f,.5f,1,1));
   mat.SetTexture("_CameraGBufferTexture2",changedNormals);Run();
   Require(BleedPixels(guide).Zip(geometricGuide,(a,b)=>a==b).All(v=>v),"normal-map changes alter geometric filtering guide");
   mat.SetTexture("_CameraGBufferTexture2",normals);Run();
   mat.SetInt("_CBSampleTemporal",1);mat.SetInt("_CBSampleFrame",1);Run();
   Require(BleedPixels(bounce).Zip(fixedSamples,(a,b)=>Mathf.Abs(a.r-b.r)).Max()>.0001f,"temporal sample phase did not vary gather");
   mat.SetInt("_CBSampleTemporal",0);mat.SetInt("_CBSampleFrame",0);Run();
   Require(BleedPixels(bounce).Zip(fixedSamples,(a,b)=>Mathf.Abs(a.r-b.r)).Max()==0,"temporal-off did not restore fixed samples");
   // Dim Gamma scene: the bright HDR fixture above only proves nonzero output.
   // Measure a shadowed neutral wall beside a modestly lit red donor, including
   // pixels where this sparse gather finds no compatible donor.
   var dimLit=Tex((x,y)=>x<w/2?new Color(.075f,.075f,.075f,1):new Color(.2f,.05f,.02f,1));
   var dimAlbedo=Tex((x,y)=>new Color(.25f,.25f,.25f,0));
   mat.SetTexture("_FTFX_BleedScene",dimLit);mat.SetTexture("_CBGammaScene",dimLit);
   mat.SetTexture("_CameraGBufferTexture0",dimAlbedo);mat.SetFloat("_CBGamma",1);
   foreach(int samples in new[]{4,6,8,16,32})
   {
   mat.SetVector("_CBLimits",new Vector4(2,.2f,div,samples));float previousMean=0;
   foreach(float strength in new[]{.3f,1f,4f})
   {
    mat.SetVector("_CBSettings",new Vector4(.75f,strength,.02f,25));Run();
    var dimPixels=BleedPixels(output);float mean=0,maximum=0;int count=0,litCount=0;
    for(int y=h/4;y<3*h/4;y++)for(int x=w/3;x<w/2-2;x++)
    {
     float red=dimPixels[y*w+x].r;mean+=red;maximum=Mathf.Max(maximum,red);count++;if(red>.00001f)litCount++;
    }
    results.Add("MEASURE: dim Gamma bleed div="+div+" samples="+samples+" strength="+strength+" mean red increment="+(mean/count).ToString("G6")+" peak="+maximum.ToString("G6")+" receivers="+litCount+"/"+count);
    Require(mean/count>previousMean,"dim-scene strength has no useful response");previousMean=mean/count;
    // v1.3.1 at 1.0 added only 0.00021 red (half) / 0.00019 (quarter).
    // At full strength require >5% of the 0.075 base in this fixed fixture.
    if(strength==1)Require(mean/count>.00375f,"dim Gamma transfer still too weak");
   }
   }
   ColorBleedTemporalScene(assets,camera,mat,source,guide,bounce,filtered,output,diagnostic);
   mat.SetVector("_CBSettings",new Vector4(.75f,.3f,.02f,25));
   mat.SetTexture("_FTFX_BleedScene",lit);mat.SetFloat("_CBGamma",0);
   // Hold the gathered light fixed, compare gamma compositing against a CPU
   // decode/add/encode reference, including HDR values above one.
   mat.SetTexture("_CBGammaScene",lit);
   var whiteAlbedo=Tex((x,y)=>Color.white);mat.SetTexture("_CameraGBufferTexture0",whiteAlbedo);
   Run();var linearTerm=BleedPixels(output);
   mat.SetFloat("_CBGamma",1);Run(false);var encodedTerm=BleedPixels(output);
   float Decode(float c) => c<=.04045f?c/12.92f:c<1?Mathf.Pow((c+.055f)/1.055f,2.4f):Mathf.Pow(c,2.2f);
   float Encode(float c) => c<=.0031308f?c*12.92f:c<1?1.055f*Mathf.Pow(c,1/2.4f)-.055f:Mathf.Pow(c,1/2.2f);
   var basePixels=lit.GetPixels();
   for(int k=0;k<encodedTerm.Length;k++)
   {
    var b=basePixels[k];var l=linearTerm[k];var e=encodedTerm[k];
    Require(Mathf.Abs(e.r-(Encode(Decode(b.r)+l.r)-b.r))<.00002f && Mathf.Abs(e.g-(Encode(Decode(b.g)+l.g)-b.g))<.00002f &&
            Mathf.Abs(e.b-(Encode(Decode(b.b)+l.b)-b.b))<.00002f,"gamma increment differs from linear-add reference at "+k);
   }
   ShowDebug(1);var gammaDebug=BleedPixels(diagnostic);
   float gammaDebugError=gammaDebug.Zip(encodedTerm,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max();
   Require(gammaDebug.Zip(encodedTerm,(a,b)=>Mathf.Abs(a.r-b.r)<.00002f+b.r*.001f && Mathf.Abs(a.g-b.g)<.00002f+b.g*.001f && Mathf.Abs(a.b-b.b)<.00002f+b.b*.001f).All(v=>v),"gamma debug exceeds RGBAHalf precision: "+gammaDebugError);
   Run();var gammaSource=BleedPixels(source);
   Require(gammaSource.All(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f<=2.003f),"gamma source not clamped after decode");
   mat.SetFloat("_CBGamma",0);mat.SetTexture("_CameraGBufferTexture0",albedo);
   mat.SetTexture("_FTFX_BleedScene",Texture2D.blackTexture);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"mismatched source dimensions did not reject");
   ShowDebug(16);Require(BleedPixels(diagnostic).All(c=>c.r==1 && c.g==0 && c.b==1 && c.a==1),"mismatched input debug not magenta");
   mat.SetVector("_CBSettings",new Vector4(.75f,4,.02f,25));
   var dark=Tex((x,y)=>Color.black);mat.SetTexture("_FTFX_BleedScene",dark);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"dark albedo emitted light");
   mat.SetTexture("_FTFX_BleedScene",lit);
   mat.SetTexture("_CBVisibility",dark);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"zero visibility glows");
   mat.SetTexture("_CBVisibility",visibility);
   mat.SetVector("_CBSettings",new Vector4(.75f,0,.02f,25));Run();ShowDebug(16);
   Require(BleedPixels(diagnostic).All(c=>c.r==0 && c.g==0 && c.b==0 && c.a==1),"zero-strength debug is not black");
   mat.SetVector("_CBSettings",new Vector4(.75f,4,.02f,25));
   mat.SetTexture("_CBActors",visibility);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"actor exclusion");
   mat.SetTexture("_CBActors",actors);
   var smooth=Tex((x,y)=>new Color(.04f,.04f,.04f,1));mat.SetTexture("_CameraGBufferTexture1",smooth);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"smooth donors not suppressed");
   mat.SetTexture("_CameraGBufferTexture1",spec);
   Run();float open=BleedPixels(bounce).Sum(c=>c.r);
   var blocker=Tex((x,y)=>new Color(.01f,0,0,0));mat.SetTexture("_CBDepth",blocker);Run(false);
   Require(open>0 && BleedPixels(bounce).All(c=>c.r==0 && c.g==0 && c.b==0),"midpoint blocker leaked");
   mat.SetTexture("_CBDepth",depth);
   var mismatchedDepth=Tex((x,y)=>new Color(add-mul/.1f,0,0,0));mat.SetTexture("_FTFX_BleedCurrentDepth",mismatchedDepth);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"forward/deferred depth mismatch leaked");
   mat.SetTexture("_FTFX_BleedCurrentDepth",raw);
   var blackReceiver=Tex((x,y)=>new Color(0,0,0,1));mat.SetTexture("_CameraGBufferTexture0",blackReceiver);Run();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"black receiver reflectance ignored");
   mat.SetTexture("_CameraGBufferTexture0",albedo);
   // High gain must still honor a tiny local donor reference and apply AO AFTER
   // that cap. These deliberately overdriven gather values force saturation.
   Run();
   void CompositeOnly()
   {
    var cb=new CommandBuffer();cb.SetRenderTarget(filtered);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4);
    cb.SetRenderTarget(output);cb.ClearRenderTarget(false,true,Color.black);
    cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,2);Graphics.ExecuteCommandBuffer(cb);cb.Dispose();
   }
   float PeakLuminance() => BleedPixels(output).Max(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f);
   Clear(bounce,new Color(1,.1f,.05f,.002f));CompositeOnly();float capped=PeakLuminance();
   Require(capped>.00039f && capped<.000401f,"reflected addition does not saturate at the local donor cap");
   var partialAO=Tex((x,y)=>new Color(.2f,0,0,0));mat.SetTexture("_CBVisibility",partialAO);CompositeOnly();
   Require(Mathf.Abs(PeakLuminance()-capped*.5f)<.000001f,"AO was applied before the cap or bypassed at high strength");
   var creviceAO=Tex((x,y)=>new Color(.04f,0,0,0));mat.SetTexture("_CBVisibility",creviceAO);CompositeOnly();
   Require(PeakLuminance()==0,"near-closed crevice glows at high strength");
   mat.SetTexture("_CBVisibility",visibility);Clear(bounce,new Color(1,.1f,.05f,0));CompositeOnly();
   Require(PeakLuminance()==0,"zero local donor reference produced light");
   // Guides on an unrelated foreground plane must not spread across silhouettes.
   Run();Clear(source,new Color(1,0,0,.3f));Clear(bounce,new Color(1,1,1,1));
   var composite=new CommandBuffer();composite.SetRenderTarget(output);composite.ClearRenderTarget(false,true,Color.black);
   composite.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,2);Graphics.ExecuteCommandBuffer(composite);composite.Dispose();
   Require(BleedPixels(output).All(c=>c.r==0 && c.g==0 && c.b==0),"foreground guides produced a halo");
   GpuAssets.Release(ref source);GpuAssets.Release(ref guide);GpuAssets.Release(ref bounce);GpuAssets.Release(ref filtered);
  }
  foreach(var t in textures)UnityEngine.Object.DestroyImmediate(t);
  GpuAssets.Release(ref output);GpuAssets.Release(ref diagnostic);UnityEngine.Object.DestroyImmediate(mat);DestroyScene(camera,target,geometry);
  results.Add("PASS: color bleed half/quarter x 4/6/8/16/32 samples; "+nonzero+" red-lit neutral receiver pixels; dark source, zero AO, actors, smooth donors, midpoint occluders, black receivers, edge rejection, HDR cap, alpha and repeat-frame stability.");
  results.Add("PASS: isolated diagnostic equals additive RGB at 1x/16x; gain does not change scene RGB; zero strength black, mismatched inputs magenta; gamma decode/add/encode matches CPU reference including HDR.");
  results.Add("PASS: dim Gamma strength response at 0.3/1/4, half/quarter and 4/6/8/16/32 samples; 1.0 exceeds 5% red-channel lift in the fixed receiver region; high-gain reflected cap, downstream partial/crevice AO and zero-reference rejection.");
 }

 private static void ColorBleedCamera(GpuAssets assets)
 {
  var camera=Scene(131,99,out var target,out var geometry);
  var config=new ConfigFile(Path.GetFullPath("bleed-camera.cfg"),false){SaveOnConfigSet=false};
  var settings=new OcclusionSettings(config);settings.Bleed.Enabled.Value=true;settings.Actors.Value=false;
  settings.Bleed.Resolution.Value=ColorBleedResolution.Half;
  settings.Bleed.Temporal.Value=false; // deterministic debug-gain comparison; history has a separate fixture
  var ambient=new CommandBuffer {name="Fixture CustomAmbient"};
  ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);ambient.ClearRenderTarget(false,true,new Color(1,0,0,1));
  var opaque=new CommandBuffer {name="Fixture opaque fog"};
  opaque.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);opaque.ClearRenderTarget(false,true,new Color(0,0,1,1));
  camera.AddCommandBuffer(CameraEvent.AfterLighting,ambient);camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,opaque);
  var field=typeof(CameraOcclusion).GetField("bleed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  using(var effect=new CameraOcclusion(camera,assets))
  {
   effect.Record(settings,0,true,true);camera.Render();
   var bleed=(ColorBleedPipeline)field.GetValue(effect);
   Require(bleed!=null && bleed.Source.width==66 && bleed.Source.height==50,"main camera odd dimensions");
   Require(bleed.FilteredBounce!=null && bleed.FilteredBounce.width==66 && bleed.FilteredBounce.height==50,"default filter odd dimensions");
   var originalFilter=bleed.FilteredBounce;
   settings.Bleed.SpatialFilter.Value=false;effect.Record(settings,0,true,true);camera.Render();
   Require(originalFilter==null && bleed.FilteredBounce==null,"filter bypass retained target");
   settings.Bleed.SpatialFilter.Value=true;effect.Record(settings,0,true,true);camera.Render();
   Require(bleed.FilteredBounce!=null,"filter re-enable failed");
   Require(bleed.DebugTexture==null && bleed.GammaScene==null,"Linear normal mode allocated diagnostic/gamma targets");
   Require(camera.GetCommandBuffers(ColorBleedPipeline.Event)[0].name==bleed.Commands.name,"bleed not ahead of native opaque effects");
   Require(camera.GetCommandBuffers(ColorBleedPipeline.Event).Count(cb=>cb.name==bleed.Commands.name)==1,"duplicate native bleed buffer");
   var captured=BleedPixels(bleed.Source);
   Require(captured.Count(c=>c.r>.1f)>20 && captured.All(c=>c.g<.001f && c.b<.001f),"capture must contain ambient red before opaque blue");
   Require(Read(target).b>.9f,"native opaque buffer lost or reordered before bleed");
   // Native code can reattach a buffer: preserve its order with respect to all peers.
   camera.RemoveCommandBuffer(ColorBleedPipeline.Event,bleed.Commands);camera.AddCommandBuffer(ColorBleedPipeline.Event,bleed.Commands);
   camera.Render();Require(camera.GetCommandBuffers(ColorBleedPipeline.Event)[0].name==bleed.Commands.name,"late ordering repair");
   Require(camera.GetCommandBuffers(ColorBleedPipeline.Event).Count(cb=>cb.name==bleed.Commands.name)==1,"ordering repair duplicated bleed");
   settings.View.Value=OcclusionView.ScreenSpaceContactGI;settings.ColorBleedGain.Value=1;effect.Record(settings,0,true,true);camera.Render();
   Require(effect.ColorBleedDebugTexture!=null && effect.ColorBleedDebugTexture.width==131 && effect.ColorBleedDebugTexture.height==99,"debug target dimensions");
   var debugOne=BleedPixels(effect.ColorBleedDebugTexture);var sceneOne=BleedPixels(target);
   settings.ColorBleedGain.Value=16;effect.Record(settings,0,true,true);camera.Render();var debugScaled=BleedPixels(effect.ColorBleedDebugTexture);
   for(int k=0;k<debugOne.Length;k++)Require(Mathf.Abs(debugScaled[k].r-debugOne[k].r*16)<.00003f+debugScaled[k].r*.002f && debugScaled[k].g<.001f && debugScaled[k].b<.001f,"camera debug gain / opaque contamination");
   Require(BleedPixels(target).Zip(sceneOne,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max()==0,"debug altered rendered scene");
   settings.Bleed.Strength.Value=0;effect.Record(settings,0,true,true);camera.Render();
   Require(effect.ColorBleedDebugTexture!=null && BleedPixels(effect.ColorBleedDebugTexture).All(c=>c.r==0 && c.g==0 && c.b==0),"strength zero must provide valid black debug");
   settings.Bleed.Strength.Value=.3f;
   settings.Bleed.Resolution.Value=ColorBleedResolution.Quarter;effect.Record(settings,0,true,true);camera.Render();
   Require(bleed.Source.width==33 && bleed.Source.height==25,"quarter resize");
   Require(bleed.FilteredBounce.width==33 && bleed.FilteredBounce.height==25,"filter quarter resize");
   var replacement=new RenderTexture(137,103,24,RenderTextureFormat.ARGBHalf);replacement.Create();
   camera.targetTexture=replacement;camera.Render();
   Require(bleed.Commands.sizeInBytes==0,"late resize executed stale guides");
   effect.Record(settings,0,true,true);camera.Render();Require(bleed.Source.width==35 && bleed.Source.height==26,"render-size recovery");
   Require(effect.ColorBleedDebugTexture.width==137 && effect.ColorBleedDebugTexture.height==103,"debug resize recovery");
   Require(bleed.FilteredBounce.width==35 && bleed.FilteredBounce.height==26,"filter late resize recovery");
   camera.targetTexture=target;effect.Record(settings,0,true,true);camera.Render();replacement.Release();UnityEngine.Object.DestroyImmediate(replacement);
   var oldDebug=effect.ColorBleedDebugTexture;settings.View.Value=OcclusionView.Normal;effect.Record(settings,0,true,true);camera.Render();
   Require(oldDebug==null && effect.ColorBleedDebugTexture==null,"Normal retained diagnostic target");
   var old=bleed.Source;var oldFilter=bleed.FilteredBounce;settings.Bleed.Enabled.Value=false;effect.Record(settings,0,true,true);camera.Render();
   Require(oldFilter==null,"disable retained filter target");
   Require(field.GetValue(effect)==null && old==null,"disabled bleed retained buffers");
   Require(camera.GetCommandBuffers(ColorBleedPipeline.Event)[0].name==opaque.name,"disable disturbed native buffer order");
   settings.Bleed.Enabled.Value=true;effect.Record(settings,0,true,false);Require(field.GetValue(effect)==null,"optic/secondary camera not excluded");
   camera.allowHDR=false;effect.Record(settings,0,true,true);Require(field.GetValue(effect)==null && effect.Active,"LDR gate disturbed AO");
   camera.allowHDR=true;effect.Record(settings,0,true,true);camera.Render();Require(field.GetValue(effect)!=null,"HDR recovery");
   settings.View.Value=OcclusionView.Depth;effect.Record(settings,0,true,true);Require(field.GetValue(effect)==null,"AO diagnostics run bleed");
  }
  Require(camera.GetCommandBuffers(ColorBleedPipeline.Event).Length==1 && camera.GetCommandBuffers(ColorBleedPipeline.Event)[0].name==opaque.name,"bleed disposal disturbed other buffers");
  camera.RemoveCommandBuffer(CameraEvent.AfterLighting,ambient);camera.RemoveCommandBuffer(ColorBleedPipeline.Event,opaque);ambient.Dispose();opaque.Dispose();
  DestroyScene(camera,target,geometry);
  results.Add("PASS: real deferred camera capture after ambient and before opaque effects, odd dimensions, half/quarter resize, actor-mask reuse without actor AO, main-camera/HDR/debug gates, ordering repair, toggle recovery and cleanup.");
 }

 private static void ColorBleedGammaCamera(GpuAssets assets)
 {
  UnityEditor.PlayerSettings.colorSpace=ColorSpace.Gamma;
  try
  {
   Require(QualitySettings.activeColorSpace==ColorSpace.Gamma,"Gamma fixture did not enter game color space");
   var camera=Scene(131,99,out var target,out var geometry);
   var config=new ConfigFile(Path.GetFullPath("bleed-gamma.cfg"),false){SaveOnConfigSet=false};
   var settings=new OcclusionSettings(config);settings.Bleed.Enabled.Value=true;settings.View.Value=OcclusionView.ScreenSpaceContactGI;
   settings.Bleed.Temporal.Value=false;
   var ambient=new CommandBuffer();ambient.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
   ambient.ClearRenderTarget(false,true,new Color(.5f,.2f,.1f,.7f));camera.AddCommandBuffer(CameraEvent.AfterLighting,ambient);
   var field=typeof(CameraOcclusion).GetField("bleed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   using(var effect=new CameraOcclusion(camera,assets))
   {
    Require(ColorBleedPipeline.Supports(camera),"Gamma main HDR camera rejected");
    effect.Record(settings,0,true,true);camera.Render();var bleed=(ColorBleedPipeline)field.GetValue(effect);
    Require(bleed!=null && bleed.GammaScene!=null && bleed.GammaScene.width==131 && effect.ColorBleedDebugTexture!=null,"Gamma path/debug not allocated");
    var captured=Read(bleed.GammaScene);Require(Mathf.Abs(captured.r-.5f)<.001f && Mathf.Abs(captured.g-.2f)<.001f,"gamma snapshot did not preserve encoded base");
    Require(BleedPixels(bleed.Source).Any(c=>c.a>0 && c.r>0 && c.r<.3f),"gamma donor radiance was not decoded");
    var scene=BleedPixels(target);settings.ColorBleedGain.Value=128;effect.Record(settings,0,true,true);camera.Render();
    Require(BleedPixels(target).Zip(scene,(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Max()==0,"Gamma debug gain changed scene");
   var oldDebug=effect.ColorBleedDebugTexture;settings.View.Value=OcclusionView.Normal;effect.Record(settings,0,true,true);camera.Render();
   Require(oldDebug==null && effect.ColorBleedDebugTexture==null && bleed.GammaScene!=null,"Gamma normal mode retained debug or lost scene snapshot");
   settings.Bleed.Temporal.Value=true;effect.Record(settings,0,true,true);camera.Render();camera.Render();
   Require(bleed.UsedHistory && BleedPixels(bleed.HistoryState).Any(c=>c.a>=64),"Gamma camera history did not accumulate");
    var oldScene=bleed.GammaScene;settings.Bleed.Enabled.Value=false;effect.Record(settings,0,true,true);
    Require(oldScene==null && field.GetValue(effect)==null,"Gamma resources leaked on disable");
   }
   camera.RemoveCommandBuffer(CameraEvent.AfterLighting,ambient);ambient.Dispose();DestroyScene(camera,target,geometry);
   results.Add("PASS: actual Gamma HDR camera accepted; encoded scene snapshot preserved; donors decoded; isolated debug and independent gain; Normal/debug/disable cleanup.");
  }
  finally { UnityEditor.PlayerSettings.colorSpace=ColorSpace.Linear; }
 }
}
