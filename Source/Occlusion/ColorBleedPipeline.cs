using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
namespace FuckTheseFX.Source.Occlusion {
 internal sealed class ColorBleedPipeline : IDisposable {
  private readonly Camera camera;private readonly Mesh quad;private readonly Material material;private readonly ComputeShader compute;
  private readonly Texture2D noise;private readonly ComputeBuffer raster=new ComputeBuffer(17,16);
  private readonly Vector4[] rows=new Vector4[17];private readonly int trace,reduce,reduce2,reduce3,prepareGeometry;
  internal readonly CommandBuffer Commands;
  internal RenderTexture Source,Guide,Geometry,Bounce,Output,ViewDepth,Motion;
  internal int HierarchyDispatches {get;private set;}
  private RenderTexture debug,validation,traceInfo;private readonly List<RenderTexture> hierarchy=new List<RenderTexture>();private readonly Dictionary<RenderTexture,ulong> allocation=new Dictionary<RenderTexture,ulong>();
  private NrdDiffuseBackend nrd;private int width,height,resolutionPercent,hash;private bool recorded,history,reset=true,pending;
  private ColorBleedSettings settings;private RenderTexture inputDepth;private uint sequence;private int aa;
  private Matrix4x4 previousView,previousStable;private Vector2 previousJitter;
  private Matrix4x4 projection,view,stable;private Vector2 jitter;private float lastRender;
  internal static readonly CameraEvent Event=CameraEvent.BeforeImageEffectsOpaque;
  internal RenderTexture DebugTexture=>recorded?debug:null;
  internal RenderTexture HistoryDiagnosticTexture=>DebugTexture;
  internal bool UsedHistory{get;private set;}
  internal ulong TextureBytes{get;private set;}
  internal NrdDiffuseBackend.Statistics NativeStats=>nrd!=null?nrd.Stats:default;
  internal ColorBleedPipeline(Camera camera,GpuAssets assets) {
   this.camera=camera;quad=assets.Quad;noise=assets.ColorBleedNoise;
   if(assets.ColorBleedCompute==null||noise==null||assets.ColorBleed==null||!assets.ColorBleed.isSupported||assets.ColorBleed.passCount!=2)throw new InvalidOperationException("Diffuse GI shader assets unavailable");
   material=new Material(assets.ColorBleed){hideFlags=HideFlags.HideAndDontSave};compute=UnityEngine.Object.Instantiate(assets.ColorBleedCompute);
   trace=compute.FindKernel("Trace");reduce=compute.FindKernel("Reduce");reduce2=compute.FindKernel("Reduce2");reduce3=compute.FindKernel("Reduce3");prepareGeometry=compute.FindKernel("PrepareGeometry");
   Commands=new CommandBuffer{name="FTFX Screen Space Contact GI ["+camera.GetInstanceID()+"]"};
   camera.depthTextureMode|=DepthTextureMode.MotionVectors;
   Camera.onPreRender+=BeforeRender;Camera.onPostRender+=AfterRender;
  }
  internal static bool Supports(Camera c)=>GpuAssets.Supports(c)&&c.allowHDR&&SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat,FormatUsage.LoadStore)&&SystemInfo.IsFormatSupported(GraphicsFormat.A2B10G10R10_UNormPack32,FormatUsage.LoadStore)&&SystemInfo.IsFormatSupported(GraphicsFormat.R32G32B32A32_SFloat,FormatUsage.LoadStore);
  private RenderTexture Target(string name,int w,int h,GraphicsFormat f,bool uav=true) {
   var t=GpuAssets.Target("FTFX GI "+name,w,h,f,uav);ulong bytes=(ulong)w*(ulong)h*(f==GraphicsFormat.R32G32B32A32_SFloat?16UL:f==GraphicsFormat.R16G16B16A16_SFloat?8UL:4UL);allocation[t]=bytes;TextureBytes+=bytes;return t;
  }
  // Round each percentage-scaled dimension up without floating-point rounding drift.
  internal static int ScaledDimension(int size,int percent)=>Math.Max(1,(int)(((long)size*Mathf.Clamp(percent,10,50)+99)/100));
  private void Ensure(int w,int h,int percent) {
   if(width==w&&height==h&&resolutionPercent==percent&&Bounce!=null&&Bounce.IsCreated()&&Source.IsCreated()&&Guide.IsCreated()&&Geometry.IsCreated()&&ViewDepth.IsCreated()&&Motion.IsCreated()&&Output.IsCreated())return;
   ReleaseTargets();width=w;height=h;resolutionPercent=percent;int lw=ScaledDimension(w,percent),lh=ScaledDimension(h,percent);
   Source=Target("fresh source",w,h,GraphicsFormat.R16G16B16A16_SFloat,false);
   Bounce=Target("packed incident",lw,lh,GraphicsFormat.R16G16B16A16_SFloat);
   Guide=Target("packed normals",lw,lh,GraphicsFormat.A2B10G10R10_UNormPack32);
   Geometry=Target("cached geometric normals",lw,lh,GraphicsFormat.R32G32B32A32_SFloat);
   ViewDepth=Target("view depth",lw,lh,GraphicsFormat.R32_SFloat);
   Motion=Target("motion",lw,lh,GraphicsFormat.R16G16B16A16_SFloat);
   Output=Target("REBLUR diffuse",lw,lh,GraphicsFormat.R16G16B16A16_SFloat);
   for(int level=1;(w>1||h>1)&&level<=14;level++){w=(w+1)/2;h=(h+1)/2;hierarchy.Add(Target("min depth "+level,w,h,GraphicsFormat.R32_SFloat));}
   nrd=new NrdDiffuseBackend(lw,lh);Reset();
  }
  internal void Record(ColorBleedSettings s,RenderTexture depth,RenderTexture visibility,RenderTexture actors,bool showContribution=false,float gain=1,bool showHistory=false,OcclusionView diagnostic=OcclusionView.Normal) {
   ContactGICapture.Cancel(camera);Commands.Clear();nrd?.Cancel();recorded=false;pending=false;settings=s;inputDepth=depth;
   Ensure(depth.width,depth.height,s.Resolution.Value);
   bool gamma=QualitySettings.activeColorSpace==ColorSpace.Gamma;
   int next=ConfigurationHash(s,gamma);if(next!=hash){Reset();hash=next;}
   int dv=showHistory?17:showContribution?16:(int)diagnostic;bool show=dv==16||dv==17||dv>=20;
   if(show&&debug==null)debug=Target("diagnostic",width,height,GraphicsFormat.R16G16B16A16_SFloat,false);
   if(!show)Release(ref debug);
   if(dv==17&&validation==null)validation=Target("NRD validation",Bounce.width,Bounce.height,GraphicsFormat.R8G8B8A8_UNorm);
   if(dv!=17)Release(ref validation);
   bool classify=dv==23||s.Capture.Value;
   if(classify&&traceInfo==null)traceInfo=Target("trace counters",Bounce.width,Bounce.height,GraphicsFormat.R16G16B16A16_SFloat);
   if(!classify)Release(ref traceInfo);
   Vector4 size=new Vector4(width,height,Bounce.width,Bounce.height),cfg=new Vector4(s.Radius.Value,s.Strength.Value,s.DepthTolerance.Value,s.Distance.Value),limits=new Vector4(s.DonorLimit.Value,s.BounceFraction,gamma?1:0,camera.farClipPlane);
   material.SetVector("_GISize",size);material.SetVector("_GISettings",cfg);material.SetVector("_GILimits",limits);material.SetBuffer("_GIRaster",raster);
   material.SetFloat("_GIReconstructionCutoff",s.ReconstructionCutoff.Value);material.SetFloat("_GIDebugGain",gain);material.SetInt("_GIDebugView",dv);
   material.SetTexture("_GIDepth",depth);material.SetTexture("_GIVisibility",visibility);material.SetTexture("_GIActors",actors);
   material.SetTexture("_GISource",Source);material.SetTexture("_GIOutput",Output);material.SetTexture("_GIRaw",Bounce);material.SetTexture("_GINormal",Guide);material.SetTexture("_GIGeometry",Geometry);material.SetTexture("_GIViewZ",ViewDepth);
   material.SetTexture("_GIValidation",validation!=null?(Texture)validation:Texture2D.blackTexture);material.SetTexture("_GITrace",traceInfo!=null?(Texture)traceInfo:Texture2D.blackTexture);
   Commands.BeginSample("FTFX GI Total");Commands.BeginSample("FTFX GI SourceCapture");Commands.Blit(BuiltinRenderTextureType.CameraTarget,Source);Commands.EndSample("FTFX GI SourceCapture");
   Commands.BeginSample("FTFX GI Hierarchy");RenderTexture prev=depth;
   HierarchyDispatches=0;
   for(int i=0;i<hierarchy.Count;i+=3) {
    int count=Math.Min(3,hierarchy.Count-i),kernel=count==3?reduce3:count==2?reduce2:reduce;var mip=hierarchy[i];
    Commands.SetComputeIntParams(compute,"_ReduceSize",prev.width,prev.height);Commands.SetComputeTextureParam(compute,kernel,"_ReduceInput",prev);Commands.SetComputeTextureParam(compute,kernel,"_ReduceOutput",mip);
    if(count>=2)Commands.SetComputeTextureParam(compute,kernel,"_ReduceSecond",hierarchy[i+1]);
    if(count==3)Commands.SetComputeTextureParam(compute,kernel,"_ReduceThird",hierarchy[i+2]);
    Commands.DispatchCompute(compute,kernel,(mip.width+7)/8,(mip.height+7)/8,1);prev=hierarchy[i+count-1];HierarchyDispatches++;
   }
   Commands.EndSample("FTFX GI Hierarchy");Commands.BeginSample("FTFX GI TraceInputs");
   Commands.SetComputeVectorParam(compute,"_GISize",size);Commands.SetComputeVectorParam(compute,"_GISettings",cfg);Commands.SetComputeVectorParam(compute,"_GILimits",limits);
   Commands.SetComputeIntParam(compute,"_RayCount",s.Rays.Value);Commands.SetComputeIntParam(compute,"_MaxSteps",s.RaySteps.Value);Commands.SetComputeIntParam(compute,"_MaxMip",hierarchy.Count);Commands.SetComputeIntParam(compute,"_Diagnose",classify?1:0);
   Commands.SetComputeBufferParam(compute,trace,"_GIRaster",raster);
   Commands.SetComputeTextureParam(compute,trace,"_GIDepth",depth);Commands.SetComputeTextureParam(compute,trace,"_GIActors",actors);Commands.SetComputeTextureParam(compute,trace,"_GISource",Source);Commands.SetComputeTextureParam(compute,trace,"_BlueNoise",noise);
   for(int i=1;i<=14;i++)Commands.SetComputeTextureParam(compute,trace,"_HiZ"+i,hierarchy[Math.Min(i,hierarchy.Count)-1]);
   Commands.SetComputeTextureParam(compute,trace,"_CameraGBufferTexture0",BuiltinRenderTextureType.GBuffer0);
   Commands.SetComputeTextureParam(compute,trace,"_CameraGBufferTexture1",BuiltinRenderTextureType.GBuffer1);
   Commands.SetComputeTextureParam(compute,trace,"_CameraGBufferTexture2",BuiltinRenderTextureType.GBuffer2);
   Commands.SetComputeTextureParam(compute,trace,"_CameraMotionVectorsTexture",BuiltinRenderTextureType.MotionVectors);
   Commands.SetComputeTextureParam(compute,trace,"_Radiance",Bounce);Commands.SetComputeTextureParam(compute,trace,"_Normal",Guide);Commands.SetComputeTextureParam(compute,trace,"_ViewZ",ViewDepth);Commands.SetComputeTextureParam(compute,trace,"_Motion",Motion);Commands.SetComputeTextureParam(compute,trace,"_TraceInfo",traceInfo!=null?traceInfo:Output);
   Commands.DispatchCompute(compute,trace,(Bounce.width+7)/8,(Bounce.height+7)/8,1);Commands.SetComputeTextureParam(compute,prepareGeometry,"_GIDepth",depth);Commands.SetComputeBufferParam(compute,prepareGeometry,"_GIRaster",raster);
   Commands.SetComputeTextureParam(compute,prepareGeometry,"_StoredNormal",Guide);Commands.SetComputeTextureParam(compute,prepareGeometry,"_StoredViewZ",ViewDepth);Commands.SetComputeTextureParam(compute,prepareGeometry,"_Geometry",Geometry);
   Commands.DispatchCompute(compute,prepareGeometry,(Bounce.width+7)/8,(Bounce.height+7)/8,1);Commands.EndSample("FTFX GI TraceInputs");
   Commands.BeginSample("FTFX GI NRD");nrd.Queue(Commands,Bounce,Guide,ViewDepth,Motion,Output,validation,Frame());Commands.EndSample("FTFX GI NRD");
   Commands.BeginSample("FTFX GI Reconstruction");Commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);Commands.DrawMesh(quad,Matrix4x4.identity,material,0,0);Commands.EndSample("FTFX GI Reconstruction");Commands.EndSample("FTFX GI Total");
   if(show){Commands.SetRenderTarget(debug);Commands.DrawMesh(quad,Matrix4x4.identity,material,0,1);}
   if(s.Capture.Value)ContactGICapture.Record(Commands,camera,s,Source,depth,Guide,Geometry,ViewDepth,Motion,Bounce,Output,traceInfo,actors,visibility,()=>CaptureMetadata());
   recorded=true;EnsureFirst();
  }
  private NrdDiffuseBackend.Frame Frame() {
   var p=stable;p.SetRow(1,-p.GetRow(1));var pp=reset?p:previousStable; if(!reset)pp.SetRow(1,-pp.GetRow(1));
   return new NrdDiffuseBackend.Frame{Projection=p,PreviousProjection=pp,View=view,PreviousView=reset?view:previousView,Jitter=jitter,PreviousJitter=reset?jitter:previousJitter,Reset=reset?1u:0,Frames=settings.Temporal.Value?(uint)settings.HistoryFrames.Value:0,Index=sequence,AntiFirefly=settings.AntiFirefly.Value?1u:0,DeltaMs=lastRender>0?Mathf.Clamp((Time.realtimeSinceStartup-lastRender)*1000,1,1000):16.666667f,Version=1,Radius=settings.Radius.Value,Validation=validation!=null?1u:0};
  }
  private void BeforeRender(Camera c) {
   if(c!=camera||!recorded)return;
   if(c.pixelWidth!=width||c.pixelHeight!=height||!Supports(c)){ContactGICapture.Cancel(camera);Commands.Clear();nrd.Cancel();recorded=false;Reset();return;}
   var layer=c.GetComponent<PostProcessLayer>();int mode=layer!=null?(int)layer.antialiasingMode:0;
   // DLSS has a separate mode even when temporal antialiasing is disabled.
   var ssaa=c.GetComponent<SSAA>();if(ssaa!=null&&ssaa.IsUsingDLSS())mode=100;
   projection=GL.GetGPUProjectionMatrix(c.projectionMatrix,false);stable=GL.GetGPUProjectionMatrix((layer!=null&&layer.antialiasingMode==PostProcessLayer.Antialiasing.TemporalAntialiasing||mode==100)?c.nonJitteredProjectionMatrix:c.projectionMatrix,false);view=c.worldToCameraMatrix;
   jitter=new Vector2(-.5f*(projection.m02-stable.m02),-.5f*(projection.m12-stable.m12));
   var old=previousView.inverse;var world=view.inverse;
   if(mode!=aa||Time.realtimeSinceStartup-lastRender>.5f||history&&(Vector3.Distance(old.GetColumn(3),world.GetColumn(3))>Mathf.Max(1,settings.Radius.Value*2)||Vector3.Dot(old.GetColumn(2),world.GetColumn(2))<.8660254f||Vector3.Dot(old.GetColumn(1),world.GetColumn(1))<.8660254f||ProjectionChanged(stable,previousStable)))Reset();aa=mode;
   Store(0,projection);Store(4,projection.inverse);Store(8,c.cameraToWorldMatrix);Store(12,view);rows[16]=new Vector4(sequence&65535,sequence>>16,(float)((((ulong)sequence*(uint)settings.Rays.Value)>>5)*.754877666%1),(float)((((ulong)sequence*(uint)settings.Rays.Value)>>5)*.569840296%1));raster.SetData(rows);
   UsedHistory=history&&settings.Temporal.Value;nrd.Update(Frame());pending=true;EnsureFirst();
  }
  private void AfterRender(Camera c) {
   if(c!=camera||!recorded||!pending)return;pending=false;ContactGICapture.Commit(camera);sequence++;history=true;reset=false;
   previousStable=stable;previousView=view;previousJitter=jitter;lastRender=Time.realtimeSinceStartup;
  }
  private static bool ProjectionChanged(Matrix4x4 a,Matrix4x4 b){for(int i=0;i<16;i++)if(Mathf.Abs(a[i]-b[i])>1e-5f)return true;return false;}
  private void Store(int start,Matrix4x4 m){for(int r=0;r<4;r++)rows[start+r]=m.GetRow(r);}
  private void Reset(){history=false;reset=true;UsedHistory=false;}
  private int ConfigurationHash(ColorBleedSettings s,bool gamma) {
   unchecked{int h=s.Radius.Value.GetHashCode();h=h*31+s.DepthTolerance.Value.GetHashCode();h=h*31+s.DonorLimit.Value.GetHashCode();h=h*31+s.Distance.Value.GetHashCode();h=h*31+s.Rays.Value;h=h*31+s.RaySteps.Value;h=h*31+s.HistoryFrames.Value;h=h*31+s.Temporal.Value.GetHashCode();h=h*31+s.AntiFirefly.Value.GetHashCode();h=h*31+gamma.GetHashCode();h=h*31+camera.nearClipPlane.GetHashCode();h=h*31+camera.farClipPlane.GetHashCode();return h*31+(camera.targetTexture!=null?camera.targetTexture.GetInstanceID():0);}
  }
  internal void EnsureFirst(){var existing=camera.GetCommandBuffers(Event);if(existing.Length>0&&existing[0].name==Commands.name)return;foreach(var cb in existing)camera.RemoveCommandBuffer(Event,cb);camera.AddCommandBuffer(Event,Commands);foreach(var cb in existing)if(cb.name!=Commands.name)camera.AddCommandBuffer(Event,cb);}
  private string CaptureMetadata()=>ContactGICapture.Metadata(camera,settings,rows,sequence,aa,Frame(),TextureBytes,NativeStats);
  private void Release(ref RenderTexture t){var value=t;t=null;if(value!=null){if(allocation.TryGetValue(value,out ulong bytes)){TextureBytes-=bytes;allocation.Remove(value);}ContactGICapture.Retire(value);}}
  private void ReleaseTargets(){nrd?.Dispose();nrd=null;Release(ref Source);Release(ref Guide);Release(ref Geometry);Release(ref Bounce);Release(ref Output);Release(ref ViewDepth);Release(ref Motion);Release(ref debug);Release(ref validation);Release(ref traceInfo);foreach(var t in hierarchy)ContactGICapture.Retire(t);hierarchy.Clear();allocation.Clear();TextureBytes=0;Reset();}
  public void Dispose(){ContactGICapture.Cancel(camera);Camera.onPreRender-=BeforeRender;Camera.onPostRender-=AfterRender;if(camera!=null)camera.RemoveCommandBuffer(Event,Commands);Commands.Clear();Commands.Dispose();ReleaseTargets();raster.Dispose();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(compute);}
 }
}
