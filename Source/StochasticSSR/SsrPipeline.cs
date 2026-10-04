using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal enum SsrMode { Native, Stochastic }
    public enum ReflectionReconstruction { Custom, NativeGaussian }
    public enum ReflectionResolution { R256=256, R512=512, R768=768, R1024=1024, R1536=1536, R2048=2048 }
    public enum ReflectionView { Normal=0, HitMask=1, HitUV=2, HitDepthDistance=3, RawHitRadiance=4, TodSkyRadiance=5, EnvironmentProbeRadiance=6, SourceClassification=7, Confidence=8, ChosenRadiance=9, NormalLength=10, NormalizedNormal=11, MaterialWeight=12, EnvironmentContribution=13, ReplacementDelta=14, EnvironmentRemovedScene=15, AddedReflection=16, TraceTermination=25, CoverageStages=26, ReconstructionLoss=27, ReceiverColourFallback=28, WaterCoverage=29, ContinuationSource=30, ContinuationCost=31, RejectionLocation=32, HaloStages=33, DepthHitKind=34, DepthHitConfidence=35, GapFillCoverage=36 }
    internal struct Options
    {
        internal SsrMode Mode;
        internal int Steps,HistoryFrames,ContinuationSamples;
        internal float Thickness,Smoothness,EdgePixels,FallbackStrength,SurfaceColourInfluence,ContinuationPixels,ContinuationDepth,ThicknessConfidenceFloor;
        internal bool Temporal,Freeze,CaptureContinuation,CaptureTraversal;
        internal ReflectionResolution Resolution;
        internal ReflectionView View;
        internal ReflectionReconstruction Reconstruction;
        internal float? ReceiverCutoff;
        internal float CutoffEnvironmentBlend;
        internal bool ZeroSmoothnessProbe;
        internal bool NrdAntiFirefly;
        internal float ZeroSmoothnessProbeStrength,ZeroSmoothnessProbeClose,ZeroSmoothnessProbeFadeStart,ZeroSmoothnessProbeFadeEnd;
        // The receiver cutoff does not change native forced-miss fallback support.
        internal float? SupportSmoothness;
        internal void ApplyNativeCutoff(float native) {
            Smoothness=native;SupportSmoothness=null;
            if(ReceiverCutoff.HasValue && ReceiverCutoff.Value!=native) {
                SupportSmoothness=native;Smoothness=Mathf.Clamp01(ReceiverCutoff.Value);
            }
        }
        // Resolution denotes the square trace extent.
        internal Vector2Int TraceDimensions()
        {
            int size=Enum.IsDefined(typeof(ReflectionResolution),Resolution)?(int)Resolution:1024;
            return new Vector2Int(size,size);
        }
        internal bool SameRendering(Options other) {var copy=this;copy.CaptureContinuation=copy.CaptureTraversal=false;other.CaptureContinuation=other.CaptureTraversal=false;return copy.Equals(other);}
    }
    // Tracing only: radiance, coverage, hit geometry and source classification.
    internal sealed class SsrPipeline : IDisposable
    {
        internal readonly SsrRasterState Raster=new SsrRasterState();
        internal RenderTexture NativeResult,Radiance,Hit,HitPoint,Record,TraceInfo,TraceStop,DepthHit,Sample;
        internal Action<RenderTexture> RetireTexture;
        internal readonly List<RenderTexture> Depth=new List<RenderTexture>();
        internal int CameraWidth {get;private set;}
        internal int CameraHeight {get;private set;}
        private readonly ComputeShader compute;
        private readonly Texture2D noise;
        private readonly Material support;
        private readonly Mesh triangle;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly int reduceKernel,traceKernel,continuedSkyKernel;
        private readonly int traceGroupWidth,traceGroupHeight;

        private int sampleIndex;
        internal int LastSamplePhase {get;private set;}
        private RenderTextureFormat format;
        internal SsrPipeline(ComputeShader source,Shader shader,Texture2D noise)
        {
            compute=UnityEngine.Object.Instantiate(source);support=new Material(shader){hideFlags=HideFlags.HideAndDontSave};this.noise=noise;
            reduceKernel=compute.FindKernel("ReduceDepth");traceKernel=compute.FindKernel("Trace");
            continuedSkyKernel=compute.HasKernel("FinalizeContinuedSky")?compute.FindKernel("FinalizeContinuedSky"):-1;
            compute.GetKernelThreadGroupSizes(traceKernel,out uint gx,out uint gy,out uint gz);
            if(gx==0 || gy==0 || gz!=1)throw new InvalidOperationException("Unsupported SSR trace group layout");
            traceGroupWidth=(int)gx;traceGroupHeight=(int)gy;
            triangle=new Mesh{name="FTFX SSR trace triangle",hideFlags=HideFlags.HideAndDontSave};
            triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};triangle.triangles=new[]{0,1,2};triangle.UploadMeshData(true);
        }
        internal static bool Supports(Camera c,int w,int h)=>c!=null && !c.orthographic && !c.stereoEnabled && c.allowHDR &&
            c.actualRenderingPath==RenderingPath.DeferredShading && c.cameraType==CameraType.Game && w>=16 && h>=16 && w<=8192 && h<=8192 &&
            SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11 && SystemInfo.supportsComputeShaders &&
            SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat) && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat) && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        internal bool Ready {
            get {if(!Created(NativeResult) || !Created(Radiance) || !Created(Hit) || !Created(HitPoint) || !Created(Record) || !Created(TraceInfo) || Depth.Count==0)return false;
                foreach(var d in Depth)if(!Created(d))return false;return true;}
        }
        private static bool Created(RenderTexture t)=>t!=null && t.IsCreated();
        internal void Ensure(int w,int h,int size,RenderTextureFormat sceneFormat)=>Ensure(w,h,size,size,sceneFormat);
        internal void Ensure(int w,int h,int traceWidth,int traceHeight,RenderTextureFormat sceneFormat)
        {
            if(Ready && CameraWidth==w && CameraHeight==h && Radiance.width==traceWidth && Radiance.height==traceHeight && format==sceneFormat)return;
            Release();CameraWidth=w;CameraHeight=h;format=sceneFormat;
            NativeResult=Target("alias result",w,h,sceneFormat,false);
            Radiance=Target("raw hit radiance and estimator",traceWidth,traceHeight,RenderTextureFormat.ARGBHalf);
            Hit=Target("confidence and receiver",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            HitPoint=Target("hit geometry and smoothness",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            Record=Target("source classification and exact source cell",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            TraceInfo=Target("continuation samples pixels depth flags",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            // Partial edge cells use the same minimum as far-plane padding.
            // Keep cell sizes in camera pixels, but avoid processing the padding.
            int dw=w,dh=h;
            while(true){Depth.Add(Target("Hi-Z "+Depth.Count,dw,dh,RenderTextureFormat.RFloat));if(dw==1 && dh==1)break;dw=(dw+1)/2;dh=(dh+1)/2;}
        }
        private static RenderTexture Target(string name,int w,int h,RenderTextureFormat f,bool random=true)
        {
            var t=new RenderTexture(w,h,0,f,RenderTextureReadWrite.Linear){name="FTFX SSR "+name,enableRandomWrite=random,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            if(t.Create())return t;UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("SSR allocation failed: "+name);
        }
        internal void ResetHistory(){sampleIndex=0;}
        internal void Trace(CommandBuffer cb,Camera camera,RenderTargetIdentifier source,Options o,RenderTargetIdentifier? rawDepth=null,RenderTargetIdentifier? normals=null,RenderTargetIdentifier? specular=null,bool diagnostics=false,bool preserveDiagnostics=false,bool traversalDiagnostics=false)
        {
            bool rejectionDiagnostics=diagnostics || o.View==ReflectionView.RejectionLocation;
            if(rejectionDiagnostics) {
                if(!Created(TraceStop)){Drop(ref TraceStop);TraceStop=Target("first rejected UV ray and scene depth",Radiance.width,Radiance.height,RenderTextureFormat.ARGBFloat);}
                compute.EnableKeyword("FTFX_REJECTION_DIAGNOSTICS");
            } else {
                compute.DisableKeyword("FTFX_REJECTION_DIAGNOSTICS");
                if(!preserveDiagnostics)Drop(ref TraceStop);
            }
            if(traversalDiagnostics)compute.EnableKeyword("FTFX_TRAVERSAL_DIAGNOSTICS");else compute.DisableKeyword("FTFX_TRAVERSAL_DIAGNOSTICS");
            if(diagnostics) {
                if(!Created(Sample))Sample=Target("original ray direction and PDF",Radiance.width,Radiance.height,RenderTextureFormat.ARGBFloat);
                var diagnosticFormat=traversalDiagnostics?RenderTextureFormat.ARGBFloat:RenderTextureFormat.ARGBHalf;
                // Pending capture callbacks still own the retired format.
                if(Created(DepthHit) && DepthHit.format!=diagnosticFormat)Drop(ref DepthHit);
                if(!Created(DepthHit))DepthHit=Target("accepted depth diagnostics",Radiance.width,Radiance.height,diagnosticFormat);
                compute.EnableKeyword("FTFX_TRACE_DIAGNOSTICS");
            } else {
                compute.DisableKeyword("FTFX_TRACE_DIAGNOSTICS");
                if(!preserveDiagnostics){Drop(ref Sample);Drop(ref DepthHit);}
            }
            int w=CameraWidth,h=CameraHeight;var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
            if(Raster.Enabled)compute.EnableKeyword("FTFX_LATE_RASTER");else compute.DisableKeyword("FTFX_LATE_RASTER");
            cb.SetComputeFloatParam(compute,"_SsrLateRaster",Raster.Enabled?1:0);
            cb.SetComputeBufferParam(compute,traceKernel,"_SsrRasterState",Raster.Buffer);
            cb.SetComputeMatrixParam(compute,"_Projection",projection);cb.SetComputeMatrixParam(compute,"_InverseProjection",projection.inverse);cb.SetComputeMatrixParam(compute,"_WorldToView",camera.worldToCameraMatrix);
            cb.SetComputeVectorParam(compute,"_Size",new Vector4(w,h,Radiance.width,Radiance.height));
            cb.SetComputeVectorParam(compute,"_Trace",new Vector4(0,o.Thickness,o.Smoothness,o.Steps));
            cb.SetComputeVectorParam(compute,"_Clip",new Vector4(camera.nearClipPlane,camera.farClipPlane,Depth.Count-1,0));
            LastSamplePhase=o.Freeze?0:sampleIndex++;
            cb.SetComputeIntParam(compute,"_Frame",LastSamplePhase);
            cb.SetComputeFloatParam(compute,"_EdgePixels",o.EdgePixels);
            cb.SetComputeFloatParam(compute,"_ThicknessConfidenceFloor",o.ThicknessConfidenceFloor);
            cb.SetComputeVectorParam(compute,"_Continuation",new Vector4(o.ContinuationSamples,o.ContinuationPixels,o.ContinuationDepth,0));
            cb.SetComputeIntParam(compute,"_ZeroSmoothnessProbe",o.ZeroSmoothnessProbe?1:0);
            using(SsrGpuSample.Measure(cb,camera,"HiZ build")) {
                properties.Clear();properties.SetBuffer("_SsrRasterState",Raster.Buffer);properties.SetFloat("_SsrLateRaster",Raster.Enabled?1:0);properties.SetMatrix("_InverseProjection",projection.inverse);properties.SetVector("_Size",new Vector4(w,h,0,0));
                properties.SetVector("_DepthOptions",new Vector4(camera.nearClipPlane,camera.farClipPlane,SystemInfo.usesReversedZBuffer?1:0,rawDepth.HasValue?1:0));
                cb.SetGlobalTexture("_FTFXSSR_RawDepth",rawDepth??new RenderTargetIdentifier(Texture2D.blackTexture));Draw(cb,source,Depth[0],1);
                for(int i=1;i<Depth.Count;i++) {
                    cb.SetComputeTextureParam(compute,reduceKernel,"_DepthPrevious",Depth[i-1]);cb.SetComputeTextureParam(compute,reduceKernel,"_DepthOutput",Depth[i]);
                    cb.DispatchCompute(compute,reduceKernel,(Depth[i].width+7)/8,(Depth[i].height+7)/8,1);
                }
            }
            var water=WaterReflectionCapture.Get(camera,w,h);
            using(SsrGpuSample.Measure(cb,camera,"HiZ trace")) {
                for(int i=0;i<14;i++)cb.SetComputeTextureParam(compute,traceKernel,"_HiZ"+i,Depth[Math.Min(i,Depth.Count-1)]);
                cb.SetComputeTextureParam(compute,traceKernel,"_Normals",normals??new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer2));
                cb.SetComputeTextureParam(compute,traceKernel,"_Specular",specular??new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer1));
                cb.SetComputeTextureParam(compute,traceKernel,"_BlueNoise",noise);cb.SetComputeTextureParam(compute,traceKernel,"_Scene",source);
                cb.SetComputeTextureParam(compute,traceKernel,"_RadianceOutput",Radiance);cb.SetComputeTextureParam(compute,traceKernel,"_HitOutput",Hit);
                cb.SetComputeTextureParam(compute,traceKernel,"_PointOutput",HitPoint);cb.SetComputeTextureParam(compute,traceKernel,"_RecordOutput",Record);
                cb.SetComputeTextureParam(compute,traceKernel,"_TraceInfo",TraceInfo);
                if(rejectionDiagnostics)cb.SetComputeTextureParam(compute,traceKernel,"_TraceStop",TraceStop);
                if(diagnostics)cb.SetComputeTextureParam(compute,traceKernel,"_DepthHit",DepthHit);
                if(diagnostics)cb.SetComputeTextureParam(compute,traceKernel,"_SampleOutput",Sample);
                cb.DispatchCompute(compute,traceKernel,(Radiance.width+traceGroupWidth-1)/traceGroupWidth,(Radiance.height+traceGroupHeight-1)/traceGroupHeight,1);
            }
            if(continuedSkyKernel>=0) {
                using(SsrGpuSample.Measure(cb,camera,"Completed continuation sky")) {
                    cb.SetComputeBufferParam(compute,continuedSkyKernel,"_SsrRasterState",Raster.Buffer);
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_WaterSurface",(Texture)water??Texture2D.blackTexture);
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_Normals",normals??new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer2));
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_HiZ0",Depth[0]);
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_RadianceOutput",Radiance);cb.SetComputeTextureParam(compute,continuedSkyKernel,"_HitOutput",Hit);
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_PointOutput",HitPoint);cb.SetComputeTextureParam(compute,continuedSkyKernel,"_RecordOutput",Record);
                    cb.SetComputeTextureParam(compute,continuedSkyKernel,"_TraceInfo",TraceInfo);
                    cb.DispatchCompute(compute,continuedSkyKernel,(Radiance.width+7)/8,(Radiance.height+7)/8,1);
                }
            }
        }
        internal void CopyFinal(CommandBuffer cb,RenderTargetIdentifier target){properties.Clear();properties.SetVector("_Size",new Vector4(CameraWidth,CameraHeight,0,0));Draw(cb,NativeResult,target,0);}
        private void Draw(CommandBuffer cb,RenderTargetIdentifier source,RenderTargetIdentifier target,int pass) {
            cb.SetGlobalTexture("_MainTex",source);cb.SetRenderTarget(target,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);cb.DrawMesh(triangle,Matrix4x4.identity,support,0,pass,properties);
        }
        private void Drop(ref RenderTexture t){if(t!=null){if(RetireTexture!=null)RetireTexture(t);else {t.Release();UnityEngine.Object.DestroyImmediate(t);}}t=null;}
        private void Release(){Drop(ref NativeResult);Drop(ref Radiance);Drop(ref Hit);Drop(ref HitPoint);Drop(ref Record);Drop(ref TraceInfo);Drop(ref TraceStop);Drop(ref DepthHit);Drop(ref Sample);foreach(var d in Depth){var target=d;Drop(ref target);}Depth.Clear();ResetHistory();}
        public void Dispose(){Raster.Dispose();Release();UnityEngine.Object.DestroyImmediate(compute);UnityEngine.Object.DestroyImmediate(support);UnityEngine.Object.DestroyImmediate(triangle);}
    }
}
