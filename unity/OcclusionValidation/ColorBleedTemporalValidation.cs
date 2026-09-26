using System;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FuckTheseFX.Source.Occlusion;

public static partial class OcclusionValidation
{
 private static void ColorBleedTemporalScene(GpuAssets assets,Camera camera,Material mat,RenderTexture source,RenderTexture guide,RenderTexture bounce,RenderTexture filtered,RenderTexture output,RenderTexture diagnostic)
 {
  // Full dim-Gamma path: varying real gathers -> temporal -> spatial -> actual
  // encoded addition. Compare frame-to-frame changes with spatial filtering alone.
  RenderTexture Target(string n) => GpuAssets.Target(n,source.width,source.height,GraphicsFormat.R16G16B16A16_SFloat);
  RenderTexture temporal=Target("scene temporal"),state=Target("scene state"),history=Target("scene history"),oldState=Target("scene old state"),oldSource=Target("scene old source"),secondFilter=Target("scene second filter");
  var moments=GpuAssets.Target("scene moments",source.width,source.height,GraphicsFormat.R32G32_SFloat);
  var oldMoments=GpuAssets.Target("scene previous moments",source.width,source.height,GraphicsFormat.R32G32_SFloat);
  mat.SetTexture("_CBMoments",moments);mat.SetTexture("_CBHistoryMoments",oldMoments);mat.SetTexture("_CBCurrentState",state);mat.SetInt("_CBUseStatistics",1);
  mat.SetMatrix("_CBViewToWorld",camera.cameraToWorldMatrix);mat.SetMatrix("_CBPreviousView",camera.worldToCameraMatrix);
  mat.SetVector("_CBPreviousNDC",mat.GetVector("_CBNDCToView"));mat.SetVector("_CBSettings",new Vector4(.75f,1,.02f,25));
  mat.SetTexture("_CBHistoryBounce",history);mat.SetTexture("_CBHistoryState",oldState);mat.SetTexture("_CBHistorySource",oldSource);
  int w=output.width,h=output.height;double temporalChange=0,rawChange=0;Color[] previousTemporal=null,previousRaw=null,current=null;
  void Composite(CommandBuffer cb,bool adaptive=true)
  {
   cb.SetRenderTarget(filtered);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4);
   var properties=new MaterialPropertyBlock();properties.SetTexture("_CBSpatialInput",filtered);properties.SetFloat("_CBSpatialStep",2);
   cb.SetRenderTarget(secondFilter);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4,properties);
   if(adaptive)
   {
    properties=new MaterialPropertyBlock();properties.SetTexture("_CBSpatialInput",secondFilter);properties.SetFloat("_CBSpatialStep",4);
    cb.SetRenderTarget(filtered);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4,properties);
   }
   properties=new MaterialPropertyBlock();properties.SetTexture("_CBFilteredBounce",adaptive?filtered:secondFilter);
   cb.SetRenderTarget(output);cb.ClearRenderTarget(false,true,new Color(0,0,0,.37f));cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,2,properties);
  }
  for(int frame=0;frame<32;frame++)
  {
   mat.SetInt("_CBSampleTemporal",1);mat.SetInt("_CBSampleFrame",frame);
   mat.SetVector("_CBTemporal",new Vector4(frame>0?1:0,8,0,0));mat.SetTexture("_CBSpatialInput",temporal);
   var cb=new CommandBuffer();cb.SetRenderTarget(new RenderTargetIdentifier[]{source,guide},source);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,0);
   cb.SetRenderTarget(bounce);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,1);
   cb.SetRenderTarget(new RenderTargetIdentifier[]{temporal,state,moments},temporal);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,5);Composite(cb);
   cb.CopyTexture(temporal,history);cb.CopyTexture(state,oldState);cb.CopyTexture(source,oldSource);
   cb.CopyTexture(moments,oldMoments);
   Graphics.ExecuteCommandBuffer(cb);cb.Dispose();current=BleedPixels(output);
   mat.SetTexture("_CBSpatialInput",bounce);cb=new CommandBuffer();Composite(cb,false);Graphics.ExecuteCommandBuffer(cb);cb.Dispose();var raw=BleedPixels(output);
   if(frame>=16)for(int y=h/4;y<3*h/4;y++)for(int x=w/3;x<w/2-2;x++)
   {
    int k=y*w+x;temporalChange+=Math.Pow(current[k].r-previousTemporal[k].r,2);rawChange+=Math.Pow(raw[k].r-previousRaw[k].r,2);
   }
   previousTemporal=current;previousRaw=raw;
  }
  double ratio=temporalChange/Math.Max(rawChange,1e-20);
  Require(rawChange>0 && ratio<.6,"actual temporal Gamma scene did not reduce changes: "+ratio);
  Require(current.All(c=>!float.IsNaN(c.r) && c.r>=0 && Mathf.Abs(c.a-.37f)<.0001f),"temporal composite alpha/finite values");
  float mean=0;int count=0;for(int y=h/4;y<3*h/4;y++)for(int x=w/3;x<w/2-2;x++){mean+=current[y*w+x].r;count++;}
  Require(mean/count>.002f,"temporal convergence lost useful dim-scene brightness");
  mat.SetTexture("_CBSpatialInput",temporal);var restore=new CommandBuffer();Composite(restore);Graphics.ExecuteCommandBuffer(restore);restore.Dispose();
  foreach(float gain in new[]{1f,16f})
  {
   var properties=new MaterialPropertyBlock();properties.SetTexture("_CBFilteredBounce",filtered);
   mat.SetFloat("_CBDebugGain",gain);var cb=new CommandBuffer();cb.SetRenderTarget(diagnostic);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,3,properties);
   Graphics.ExecuteCommandBuffer(cb);cb.Dispose();var debug=BleedPixels(diagnostic);
   for(int k=0;k<debug.Length;k++)Require(Mathf.Abs(debug[k].r-current[k].r*gain)<.00002f+current[k].r*gain*.001f &&
    Mathf.Abs(debug[k].g-current[k].g*gain)<.00002f+current[k].g*gain*.001f && Mathf.Abs(debug[k].b-current[k].b*gain)<.00002f+current[k].b*gain*.001f,"temporal debug differs from scene addition");
  }
  mat.SetTexture("_CBSpatialInput",bounce);mat.SetInt("_CBSampleTemporal",0);mat.SetInt("_CBSampleFrame",0);mat.SetVector("_CBTemporal",Vector4.zero);
  mat.SetInt("_CBUseStatistics",0);
  foreach(var t in new[]{temporal,state,history,oldState,oldSource,secondFilter,moments,oldMoments}) { t.Release();UnityEngine.Object.DestroyImmediate(t); }
  results.Add("PASS: real varying gather + temporal + spatial Gamma composition, div="+(int)mat.GetVector("_CBLimits").z+"; frame-difference energy ratio="+ratio.ToString("F3")+"; mean red="+(mean/count).ToString("F6")+"; debug 1x/16x agrees with actual addition.");
 }

 private static void ColorBleedTemporal(GpuAssets assets)
 {
  ColorBleedSampling(assets);
  const int w=33,h=25;
  foreach(int div in new[]{2,4})
  {
   int fullW=w*div-1,fullH=h*div-1;
   var mat=new Material(assets.ColorBleed);
   var source=BleedTexture(w,h,(x,y)=>new Color(.05f,.03f,.02f,2));
   var guide=BleedTexture(w,h,(x,y)=>new Color(1,1,1,1f/255));
   var input=BleedTexture(w,h,(x,y)=>Color.clear);
   var staging=BleedTexture(w,h,(x,y)=>Color.clear);
   RenderTexture Target(string name) => GpuAssets.Target(name,w,h,GraphicsFormat.R16G16B16A16_SFloat);
   RenderTexture output=Target("temporal test"),state=Target("temporal state test"),history=Target("history test"),oldState=Target("old state test"),oldSource=Target("old source test");
   var moments=GpuAssets.Target("moments test",w,h,GraphicsFormat.R32G32_SFloat);
   var oldMoments=GpuAssets.Target("old moments test",w,h,GraphicsFormat.R32G32_SFloat);
   mat.SetTexture("_CBHistoryMoments",oldMoments);
   var diagnostic=GpuAssets.Target("temporal diagnostic test",w,h,GraphicsFormat.R8G8B8A8_UNorm);
   void Fill(Texture2D t,Func<int,int,Color> f)
   { var p=new Color[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)p[y*w+x]=f(x,y);t.SetPixels(p);t.Apply(); }
   mat.SetVector("_CBSize",new Vector4(fullW,fullH,w,h));
   mat.SetVector("_CBLimits",new Vector4(2,.2f,div,6));mat.SetVector("_CBSettings",new Vector4(.75f,1,.02f,25));
   mat.SetVector("_CBDepthUnpack",new Vector4(0,0,100,0));
   mat.SetVector("_CBNDCToView",new Vector4(1,-1,-.5f,.5f));mat.SetVector("_CBPreviousNDC",new Vector4(1,-1,-.5f,.5f));
   mat.SetMatrix("_CBViewToWorld",Matrix4x4.identity);mat.SetMatrix("_CBPreviousView",Matrix4x4.identity);
   mat.SetTexture("_CBSource",source);mat.SetTexture("_CBGuide",guide);mat.SetTexture("_CBBounce",input);
   mat.SetTexture("_CBHistoryBounce",history);mat.SetTexture("_CBHistoryState",oldState);mat.SetTexture("_CBHistorySource",oldSource);
   int historyFrames=8;
   Color[] Run(bool valid=true,bool save=true)
   {
    mat.SetVector("_CBTemporal",new Vector4(valid?1:0,historyFrames,0,0));
    var cb=new CommandBuffer();cb.SetRenderTarget(new RenderTargetIdentifier[]{output,state,moments,diagnostic},output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,5);
    if(save){cb.CopyTexture(output,history);cb.CopyTexture(state,oldState);cb.Blit(source,oldSource);cb.CopyTexture(moments,oldMoments);}
    Graphics.ExecuteCommandBuffer(cb);cb.Dispose();return BleedPixels(output);
   }
   Fill(input,(x,y)=>new Color(.1f,.05f,.025f,.2f));
   Require(Run(false).All(c=>Mathf.Abs(c.r-.1f)<.0001f),"temporal cold start differs from current input");
   Require(BleedPixels(state).All(c=>c.a==32),"cold start count/offset packing");
   Require(BleedPixels(diagnostic).All(c=>c.r==1 && Mathf.Abs(c.g-.5f)<.003f && c.b==0),"fresh history diagnostic is not orange");
   // Squared dim radiance must survive storage (RGBAHalf can underflow here).
   Fill(input,(x,y)=>new Color(.0001f,.0001f,.0001f,.0001f));Run(false);
   Require(BleedPixels(moments).All(c=>Mathf.Abs(c.r-.0001f)<1e-7f && Mathf.Abs(c.g-1e-8f)<1e-10f),"dim luminance moments lost precision");
   // A stationary impulse must not creep toward nominal block centers.
   Fill(input,(x,y)=>x==w/2 && y==h/2?new Color(1,.5f,.25f,1):Color.clear);
   var stationary=Run(false);
   for(int frame=0;frame<12;frame++)Run();
   Require(BleedPixels(output).Zip(stationary,(a,b)=>Mathf.Abs(a.r-b.r)).Max()<.002f,"static history drifts across texels");
   Fill(guide,(x,y)=>new Color(1,1,1,(1+(x==w-1?0:div-1)+4*(y==h-1?0:div-1))/255f));
   historyFrames=16;stationary=Run(false);for(int frame=0;frame<20;frame++)Run();
   Require(BleedPixels(output).Zip(stationary,(a,b)=>Mathf.Abs(a.r-b.r)).Max()<.002f,"offset representative history drifts");
   var packedState=BleedPixels(state);
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)Require(packedState[y*w+x].a==512+(x==w-1?0:div-1)+4*(y==h-1?0:div-1),"sixteen-frame age/offset packing");
   historyFrames=30;for(int frame=0;frame<35;frame++)Run();
   packedState=BleedPixels(state);
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)Require(packedState[y*w+x].a==960+(x==w-1?0:div-1)+4*(y==h-1?0:div-1),"thirty-frame age/offset packing");
   Require(BleedPixels(output).Zip(stationary,(a,b)=>Mathf.Abs(a.r-b.r)).Max()<.002f,"thirty-frame history drifts");
   results.Add("PASS: 30-frame history cap and representative offset packing remain exact, div="+div);
   historyFrames=8;Fill(guide,(x,y)=>new Color(1,1,1,1f/255));
   // Four rotating sparse patterns have a known quarter-intensity mean.
   for(int frame=0;frame<32;frame++)
   {
    Fill(input,(x,y)=>(x+y*3+frame)%4==0?new Color(1,.5f,.25f,1):Color.clear);
    Run(frame>0);
   }
   var raw=input.GetPixels();var accumulated=BleedPixels(output);
   double Variance(Color[] p) { double m=p.Average(c=>(double)c.r);return p.Average(c=>(c.r-m)*(c.r-m)); }
   double ratio=Variance(accumulated)/Variance(raw);
   Require(ratio<.1 && Math.Abs(accumulated.Average(c=>c.r)-.25)<.025,"temporal convergence mean/variance: "+ratio);
   Require(accumulated.All(c=>c.r>0 && Mathf.Abs(c.g-c.r*.5f)<.0005f && Mathf.Abs(c.a-c.r)<.0005f),"temporal holes / radiance-reference weights");
   Require(BleedPixels(state).All(c=>c.a==256),"history count did not reach eight: "+string.Join(",",BleedPixels(state).Select(c=>c.a).Distinct()));
   Fill(input,(x,y)=>Color.clear);
   for(int frame=0;frame<64;frame++)Run();
   Require(BleedPixels(output).Max(c=>c.r)<.0001f,"persistently absent donors do not decay at the configured rate");

   void Seed()
   {
    Fill(source,(x,y)=>new Color(.05f,.03f,.02f,2));Fill(guide,(x,y)=>new Color(1,1,1,1f/255));
    Fill(input,(x,y)=>new Color(1,.5f,.25f,1));Run(false);Fill(input,(x,y)=>Color.clear);
   }
   Seed();historyFrames=16;Clear(history,new Color(.1f,0,0,.2f));Clear(oldState,new Color(2,.5f,.5f,512));
   Fill(input,(x,y)=>new Color(0,0,.1f,.2f));var changedSample=Run(true,false);
   Fill(input,(x,y)=>Color.clear);var missedSample=Run(true,false);
   results.Add("MEASURE: 16-frame red-history retention, other-color sample="+changedSample[(h/2)*w+w/2].r.ToString("F6")+", zero sample="+missedSample[(h/2)*w+w/2].r.ToString("F6")+"; expected unmodified history term=0.093750");
   Require(Mathf.Abs(changedSample[(h/2)*w+w/2].r-.09375f)<.0002f && Mathf.Abs(missedSample[(h/2)*w+w/2].r-.09375f)<.0002f,"sparse/other-color samples discard mature history");
   Require(BleedPixels(diagnostic).All(c=>c.g>.93f && c.r<.07f && c.b==0),"mature history diagnostic does not show actual retention");
   for(int frame=0;frame<96;frame++)
   {
    Fill(input,(x,y)=>frame%2==0?new Color(.1f,0,0,.2f):new Color(0,0,.1f,.2f));Run();
   }
   var mixed=BleedPixels(output);
   Require(mixed.All(c=>c.r>.045f && c.r<.055f && c.b>.045f && c.b<.055f),"alternating donor hues fail to converge to their mixture");
   historyFrames=8;
   // Bright and dim donors alternate while geometry and local source lighting
   // stay fixed. The current stochastic reference cannot bound the running mean.
   Seed();historyFrames=16;
   float expected=0;
   for(int frame=0;frame<96;frame++)
   {
    float value=frame%8==0?.2f:.001f;
    Fill(input,(x,y)=>new Color(value,value,value,value));Run(frame>0);
    expected=frame==0?value:Mathf.Lerp(value,expected,Mathf.Min(frame/(frame+1f),15f/16));
   }
   results.Add("MEASURE: varying donor brightness, expected="+expected.ToString("F6")+", actual="+BleedPixels(output)[w*h/2].r.ToString("F6"));
   Require(BleedPixels(output).All(c=>Mathf.Abs(c.r-expected)<.0003f && Mathf.Abs(c.a-expected)<.0003f),"dim random samples erase bright historical donors");
   Require(BleedPixels(diagnostic).All(c=>c.b==0 && c.g>.93f),"valid dim donors trigger history limiting");
   var statistics=BleedPixels(moments);
   Require(statistics.All(c=>Mathf.Abs(c.r-expected)<1e-5f && c.g-c.r*c.r>.001f),"varying donors lack correct luminance mean/positive variance");
   historyFrames=8;
   // A fixed geometric plane with subpixel-varying normal-map or material
   // samples must not be mistaken for newly exposed geometry / changed lighting.
   var planeDepth=BleedTexture(fullW,fullH,(x,y)=>new Color(2,0,0,0));
   mat.SetTexture("_CBDepth",planeDepth);
   void SetPlane(Func<int,int,float> depth)
   { planeDepth.SetPixels(Enumerable.Range(0,fullW*fullH).Select(k=>new Color(depth(k%fullW,k/fullW),0,0,0)).ToArray());planeDepth.Apply(); }
   for(int detail=0;detail<4;detail++)
   {
    float Z(int x,int y) => detail<2?2:2/(1+1.5f*((x+.5f)/fullW-.5f));
    SetPlane(Z);
    historyFrames=16;
    for(int frame=0;frame<64;frame++)
    {
     // Capture stores the geometric guide. Match the sloped plane for texture
     // tests; normal-detail tests deliberately exercise depth-normal recovery.
     Fill(guide,(x,y)=>detail==3?new Color(0,.7f,1,1f/255):new Color(detail%2==0 && frame%2==1?0:1,detail%2==0?11f/14:1,1,1f/255));
     Fill(source,(x,y)=>detail%2==1 && (x+y+frame)%2==1?new Color(.25f,.15f,.1f,Z(x*div,y*div)):new Color(.05f,.03f,.02f,Z(x*div,y*div)));
     Fill(input,(x,y)=>frame%2==0?new Color(.1f,0,0,.2f):new Color(0,0,.1f,.2f));Run(frame>0);
    }
    var retained=BleedPixels(diagnostic).Count(c=>c.g>.8f)/(float)(w*h);
    Require(retained>.99f,"stationary textured geometry discards history, case "+detail+": "+retained);
    Require(BleedPixels(output).All(c=>c.r>.045f && c.r<.055f && c.b>.045f && c.b<.055f),"stationary textured geometry fails color convergence, case "+detail);
    results.Add("PASS: fixed "+(detail<2?"front-facing":"grazing")+" geometry / changing "+(detail%2==0?"normal detail":"texture samples")+" retained fraction="+retained.ToString("F3")+" with converged donor colors.");
   }
   SetPlane((x,y)=>2);Seed();
   Fill(source,(x,y)=>new Color(.5f,.3f,.2f,2));
   Require(Run(true,false).All(c=>c.r==0),"depth-derived normal bypasses real lighting rejection");
   // A bright neighboring foreground must not enlarge the receiver's lighting envelope.
   Seed();Clear(oldSource,new Color(.5f,.3f,.2f,2));
   Fill(source,(x,y)=>x==w/2 && y==h/2?new Color(.05f,.03f,.02f,2):new Color(.5f,.3f,.2f,1));
   Require(Run(true,false)[(h/2)*w+w/2].r==0,"lighting envelope leaks across depth boundary");
   Seed();
   float Tilt(int x,int y) => 2/(1+1.5f*((x+.5f)/fullW-.5f));
   SetPlane(Tilt);Fill(source,(x,y)=>new Color(.05f,.03f,.02f,Tilt(x*div,y*div)));
   Require(Run(true,false).All(c=>c.r==0),"changed geometric plane retains history with unchanged shading normal");
   mat.SetTexture("_CBDepth",Texture2D.blackTexture);UnityEngine.Object.DestroyImmediate(planeDepth);historyFrames=8;
   Seed();Fill(source,(x,y)=>new Color(.05f,.03f,.02f,20));Fill(input,(x,y)=>Color.white);Run(false);
   Fill(input,(x,y)=>Color.clear);Fill(source,(x,y)=>new Color(.05f,.03f,.02f,20.1f));
   Require(Run(true,false).All(c=>c.r==0),"precision allowance accepts a distinct surface 10 cm away at 20 m");
   for(int rejection=0;rejection<6;rejection++)
   {
    Seed();
    if(rejection==0)Fill(source,(x,y)=>new Color(.05f,.03f,.02f,1));
    if(rejection==1)Fill(guide,(x,y)=>new Color(1,.75f,1,1f/255));
    if(rejection==2)Fill(guide,(x,y)=>Color.clear);
    if(rejection==3)Fill(source,(x,y)=>new Color(.5f,.3f,.2f,2));
    if(rejection==4)mat.SetMatrix("_CBPreviousView",Matrix4x4.Translate(new Vector3(100,0,0)));
    var rejected=Run(rejection!=5,false);
    Require(rejected.All(c=>c.r==0 && c.a==0),"stale history survived rejection "+rejection);
    Require(BleedPixels(moments).All(c=>c.r==0 && c.g==0),"rejected radiance retained old moments "+rejection);
    Color[] reasons={Color.red,new Color(1,1,0),Color.blue,Color.cyan,new Color(1,.5f,0),new Color(1,.5f,0)};
    Require(BleedPixels(diagnostic).All(c=>Mathf.Abs(c.r-reasons[rejection].r)<.003f && Mathf.Abs(c.g-reasons[rejection].g)<.003f && Mathf.Abs(c.b-reasons[rejection].b)<.003f),"history diagnostic rejection color "+rejection);
    mat.SetMatrix("_CBPreviousView",Matrix4x4.identity);
   }
   // Translate the camera one low-resolution cell. History must follow the
   // surface into the adjacent cell, not stick to screen coordinates.
   Seed();Fill(staging,(x,y)=>x==w/2?new Color(1,.5f,.25f,1):Color.clear);Graphics.Blit(staging,history);
   Fill(staging,(x,y)=>new Color(2,.5f,.5f,256));Graphics.Blit(staging,oldState);
   mat.SetMatrix("_CBViewToWorld",Matrix4x4.Translate(new Vector3(2f*div/fullW,0,0)));
   var shifted=Run(true,false);
   Require(shifted[(h/2)*w+w/2-1].r>.87f && shifted[(h/2)*w+w/2].r<.001f,"camera reprojection did not move history with the surface");
   mat.SetMatrix("_CBViewToWorld",Matrix4x4.identity);
   // Cross the old 20% bilinear-support cliff. Both sides must retain a smooth,
   // reduced contribution instead of periodically resetting to the raw sample.
   Seed();historyFrames=16;Clear(history,Color.white);
   Fill(staging,(x,y)=>x==w/2?new Color(2,.5f,.5f,512):Color.clear);Graphics.Blit(staging,oldState);
   float[] coverageResults=new float[2];
   for(int j=0;j<2;j++)
   {
    float shift=j==0?.79f:.81f;
    mat.SetMatrix("_CBViewToWorld",Matrix4x4.Translate(new Vector3(2f*div/fullW*shift,0,0)));
    coverageResults[j]=Run(true,false)[(h/2)*w+w/2].r;
   }
   Require(coverageResults.Min()>.1f && Math.Abs(coverageResults[0]-coverageResults[1])<.03f,"marginal bilinear coverage still has a rejection cliff: "+string.Join(",",coverageResults));
   results.Add("PASS: 21% / 19% history coverage retains "+string.Join(" / ",coverageResults.Select(v=>v.ToString("F4")))+" without a cold reset; dim and varying luminance moments preserved.");
   mat.SetMatrix("_CBViewToWorld",Matrix4x4.identity);historyFrames=8;
   // Impossible HDR outliers remain bounded by the configured donor ceiling.
   Seed();Clear(history,new Color(100,100,100,100));Fill(input,(x,y)=>new Color(.01f,.005f,0,.02f));
   var clamped=Run(true,false);Require(clamped.Max(c=>c.r*.2126f+c.g*.7152f+c.b*.0722f)<=2 && clamped.Max(c=>c.a)<=2,"temporal donor ceiling failed");
   Require(BleedPixels(diagnostic).All(c=>c.r>.97f && c.b>.97f && c.g<.02f),"strong history limiting diagnostic is not magenta");
   foreach(var t in new[]{source,guide,input,staging})UnityEngine.Object.DestroyImmediate(t);
   foreach(var t in new[]{output,state,history,oldState,oldSource,diagnostic,moments,oldMoments}) { t.Release();UnityEngine.Object.DestroyImmediate(t); }
   UnityEngine.Object.DestroyImmediate(mat);
   results.Add("PASS: temporal div="+div+" variance ratio="+ratio.ToString("F3")+"; sparse convergence, stable representatives, count/reference, decay, disocclusion/normal/invalid/lighting/offscreen/reset rejection, translation reprojection and HDR clamp.");
  }
 }

 private static void ColorBleedTemporalCamera(GpuAssets assets)
 {
  var camera=Scene(131,99,out var target,out var geometry);
  var config=new ConfigFile(Path.GetFullPath("bleed-temporal.cfg"),false){SaveOnConfigSet=false};
  var settings=new OcclusionSettings(config);settings.Bleed.Enabled.Value=true;
  settings.Bleed.Resolution.Value=ColorBleedResolution.Half;
  var field=typeof(CameraOcclusion).GetField("bleed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  using(var effect=new CameraOcclusion(camera,assets))
  {
   effect.Record(settings,0,true,true);var bleed=(ColorBleedPipeline)field.GetValue(effect);
   Require(bleed.TemporalBounce!=null && bleed.HistorySource.width==66 && !bleed.HistoryValid && bleed.TemporalMoments.graphicsFormat==GraphicsFormat.R32G32_SFloat && bleed.HistoryMoments.width==66,"initial temporal allocation/state/moments");
   effect.Record(settings,0,true,true);Require(bleed.RenderSerial==0 && !bleed.HistoryValid,"Record advanced unrendered history");
   camera.Render();Require(bleed.HistoryValid && !bleed.UsedHistory && bleed.RenderSerial==1,"first render history promotion");
   camera.Render();Require(bleed.UsedHistory && bleed.RenderSerial==2,"repeat camera render did not use history");
   var counter=typeof(ColorBleedPipeline).GetField("sampleIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   var samplingMaterial=(Material)typeof(ColorBleedPipeline).GetField("material",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(bleed);
   counter.SetValue(bleed,63);camera.Render();camera.Render();
   Require(samplingMaterial.GetInt("_CBSampleFrame")==64,"sampling repeats after 64 renders");
   counter.SetValue(bleed,int.MaxValue);camera.Render();camera.Render();
   Require(samplingMaterial.GetInt("_CBSampleFrame")==int.MinValue,"sampling counter loses bits at signed rollover");
   var oldState=BleedPixels(bleed.HistoryState);Require(oldState.Any(c=>c.a>=64),"GPU history count never accumulated");
   settings.ColorBleedGain.Value=64;effect.Record(settings,0,true,true);Require(bleed.HistoryValid,"debug display gain reset history");
   settings.View.Value=OcclusionView.ScreenSpaceContactGIHistory;effect.Record(settings,0,true,true);camera.Render();
   Require(bleed.HistoryDiagnosticTexture!=null && bleed.HistoryDiagnosticTexture.width==66 && bleed.DebugTexture==null && bleed.UsedHistory,"history view allocation/routing reset accumulation");
   Require(BleedPixels(bleed.HistoryDiagnosticTexture).Any(c=>c.g>.5f),"history diagnostic never shows retained samples");
   var diagnostic=bleed.HistoryDiagnosticTexture;settings.View.Value=OcclusionView.Normal;effect.Record(settings,0,true,true);
   Require(diagnostic==null && bleed.HistoryDiagnosticTexture==null && bleed.HistoryValid,"Normal retained diagnostic target or reset history");
   settings.Bleed.Strength.Value=1;effect.Record(settings,0,true,true);Require(!bleed.HistoryValid,"strength change did not reset history");camera.Render();
   camera.transform.position+=Vector3.right*3;camera.Render();Require(!bleed.UsedHistory,"camera cut used stale history");
   camera.Render();Require(bleed.UsedHistory,"cut did not recover on next render");
   camera.fieldOfView=35;camera.Render();Require(!bleed.UsedHistory,"FOV cut used stale history");
   camera.transform.rotation=Quaternion.Euler(0,0,45);camera.Render();Require(!bleed.UsedHistory,"roll cut used stale history");
   var old=bleed.HistoryBounce;settings.Bleed.Temporal.Value=false;effect.Record(settings,0,true,true);camera.Render();
   Require(old==null && bleed.HistoryBounce==null && bleed.TemporalMoments==null && bleed.HistoryMoments==null && !bleed.HistoryValid,"temporal bypass retained resources/history");
   settings.Bleed.Temporal.Value=true;settings.Bleed.Resolution.Value=ColorBleedResolution.Quarter;
   effect.Record(settings,0,true,true);camera.Render();Require(!bleed.UsedHistory && bleed.HistoryBounce.width==33 && bleed.HistoryBounce.height==25,"quarter history reset/dimensions");
   var replacement=new RenderTexture(137,103,24,RenderTextureFormat.ARGBHalf);replacement.Create();camera.targetTexture=replacement;
   camera.Render();Require(!bleed.HistoryValid && bleed.Commands.sizeInBytes==0,"late resize retained history commands");
   effect.Record(settings,0,true,true);camera.Render();Require(!bleed.UsedHistory && bleed.HistoryState.width==35 && bleed.HistoryState.height==26,"history resize recovery");
   camera.targetTexture=target;effect.Record(settings,0,true,true);camera.Render();replacement.Release();UnityEngine.Object.DestroyImmediate(replacement);
   old=bleed.HistoryBounce;settings.Bleed.Enabled.Value=false;effect.Record(settings,0,true,true);Require(old==null,"disabled effect retained history");
  }
  DestroyScene(camera,target,geometry);
  ColorBleedJitteredCamera(assets);
  results.Add("PASS: temporal camera render-only advancement, GPU history counts, debug gain independence, settings/camera/FOV/resize resets, temporal bypass/re-enable and resource cleanup.");
 }

 private static void ColorBleedJitteredCamera(GpuAssets assets)
 {
  const int renderW=1025,renderH=577;
  var camera=Scene(renderW,renderH,out var target,out var geometry);
  camera.transform.position=new Vector3(-200,.5f,-212);
  camera.transform.rotation=Quaternion.Euler(2,30,0);
  geometry[0].transform.position=new Vector3(-200,-.1f,-160);geometry[0].transform.localScale=new Vector3(200,.2f,200);
  for(int i=1;i<geometry.Length;i++)geometry[i].transform.position=new Vector3(-200+i,1,-203);
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("bleed-jitter.cfg"),false){SaveOnConfigSet=false});
  settings.Bleed.Enabled.Value=true;settings.Bleed.HistoryFrames.Value=16;settings.View.Value=OcclusionView.ScreenSpaceContactGIHistory;
  var projection=camera.projectionMatrix;
  using(var effect=new CameraOcclusion(camera,assets))
  {
   for(int orientation=0;orientation<3;orientation++)
   {
    var anchor=new Vector3(-200,0,-212);
    var roll=Quaternion.AngleAxis(orientation==1?90:0,Vector3.forward);
    camera.transform.position=anchor+roll*new Vector3(0,.5f,0);
    camera.transform.rotation=orientation==2?Quaternion.identity:roll*Quaternion.Euler(2,30,0);
    geometry[0].transform.rotation=roll;
    geometry[0].transform.localScale=orientation==2?new Vector3(200,200,.2f):new Vector3(200,.2f,200);
    geometry[0].transform.position=anchor+(orientation==2?new Vector3(0,0,23.303f):roll*new Vector3(0,-.1f,52));
    for(int i=1;i<geometry.Length;i++)geometry[i].transform.position=anchor+roll*new Vector3(i,1,9);
   foreach(int div in new[]{2,4})
   {
    settings.Bleed.Resolution.Value=(ColorBleedResolution)div;
    double rejected=0,valid=0,up=0;
    for(int frame=0;frame<32;frame++)
    {
     var p=projection;p.m02+=(Mathf.Repeat(frame*.61803399f,1)-.5f)*2/renderW;p.m12+=(Mathf.Repeat(frame*.75487766f,1)-.5f)*2/renderH;camera.projectionMatrix=p;
     effect.Record(settings,0,true,true);camera.Render();
     if(frame<16)continue;
     var bleed=(ColorBleedPipeline)typeof(CameraOcclusion).GetField("bleed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(effect);
     var guide=BleedPixels(bleed.Guide);var states=BleedPixels(bleed.HistoryState);var diagnostic=BleedPixels(bleed.HistoryDiagnosticTexture);
     for(int k=0;k<guide.Length;k++)if(guide[k].a>0 && states[k].a>0)
     {
      valid++;if(diagnostic[k].g<.8f || diagnostic[k].b>.1f)rejected++;
      bool correctNormal=orientation==0?Mathf.Abs(states[k].g-.5f)<.02f && states[k].b>.98f:
       orientation==1?states[k].g<.02f && Mathf.Abs(states[k].b-.5f)<.02f:states[k].g>.98f && states[k].b>.98f;
      if(correctNormal)up++;
     }
    }
    Require(valid>1000 && rejected/valid<.015 && up/valid>.9,"shallow jittered plane loses history");
    results.Add("PASS: jittered world-offset plane orientation="+orientation+" (floor/wall/front), div="+div+", rejected="+(rejected/valid).ToString("F3")+", correct normal="+(up/valid).ToString("F3"));
   }
   }
  }
  DestroyScene(camera,target,geometry);
 }
}
