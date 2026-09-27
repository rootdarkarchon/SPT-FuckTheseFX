using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal enum SsrMode { Native, StochasticNative }
    public enum ReconstructionMode { Native, Custom }
    internal struct Options
    {
        internal SsrMode Mode;
        internal ReconstructionMode Reconstruction;
        internal int Divisor, Steps;
        internal float Distance, Thickness, Smoothness;
        internal bool Temporal, Spatial;
        internal bool NativeResponse;
        internal bool NativeTraceContract;
        internal bool CompactRays, LimitNativeRange, EfficientDenoising;
        internal bool StreamlinedOutput, AdaptiveRays;
        internal int HistoryFrames;
        internal float EdgePixels;
        internal bool StochasticRays => Mode==SsrMode.StochasticNative;
        internal bool CustomReconstruction => Mode==SsrMode.StochasticNative && Reconstruction==ReconstructionMode.Custom;
        internal bool AdaptiveSampling => AdaptiveRays && CustomReconstruction && Temporal && Spatial && HistoryFrames>=4;
        internal float RayDistance(Camera camera) => !LimitNativeRange?camera.farClipPlane:Mathf.Min(Distance,camera.farClipPlane);
    }

    // AMD history contains the complete reflection signal, using native resolve for misses.
    internal sealed class SsrPipeline : IDisposable
    {
        internal RenderTexture NativeResult, Radiance, Hit;
        internal RenderTexture HitPoint, TemporalResult, Filtered, History, HistoryGuide, HistoryPoint, Motion, HistoryWeight;
        internal RenderTexture DenoiseInput;
        internal RenderTexture AdaptiveMask;
        internal bool ActualAdaptiveRays,ActualRawRadiance;
        internal int ActualTraceSteps;
        internal float ActualTraceDistance,ActualTraceCutoff;
        internal bool ActualNativeTraceContract;
        internal bool ActualCompactRays;
        internal ComputeBuffer RayList, RayCount, RayDispatch;
        internal int ActualSampleIndex;
        private readonly FidelityDenoiser denoiser;
        private readonly Texture2D blueNoise;
        internal RenderTexture NativeTest, Surface, HistorySurface;
        internal readonly List<RenderTexture> Depth = new List<RenderTexture>();
        private readonly ComputeShader compute;
        private readonly Material blend;
        private readonly MaterialPropertyBlock blendProperties = new MaterialPropertyBlock(), depthProperties = new MaterialPropertyBlock();
        private readonly Mesh triangle;
        private readonly int reduceKernel, traceKernel;
        private readonly int classifyKernel, resetRaysKernel, prepareRaysKernel;
        private int width, height, divisor, traceSize;
        private int sampleIndex, lastFrame=-10;
        private float lastTime;
        private float previousNear,previousFar;
        private int previousTarget;
        private Options previousOptions;
        private bool pendingHistorySwap;
        private Matrix4x4 previousView, previousProjection;
        private Vector4 sampleOffset;
        internal bool HistoryValid { get; private set; }
        internal bool UsedHistory { get; private set; }
        private RenderTextureFormat sceneFormat;
        internal SsrPipeline(ComputeShader compute, Shader blendShader, ComputeShader amdShader=null, Texture2D blueNoise=null)
        {
            this.compute = UnityEngine.Object.Instantiate(compute);
            this.blueNoise=blueNoise;
            if(amdShader!=null)denoiser=new FidelityDenoiser(amdShader);
            blend = new Material(blendShader) { hideFlags = HideFlags.HideAndDontSave };
            reduceKernel = compute.FindKernel("ReduceDepth"); traceKernel = compute.FindKernel("Trace");
            classifyKernel=compute.FindKernel("ClassifyRays");resetRaysKernel=compute.FindKernel("ResetRays");
            prepareRaysKernel=compute.FindKernel("PrepareRayDispatch");
            triangle = new Mesh { name = "FTFX SSR triangle", hideFlags = HideFlags.HideAndDontSave };
            triangle.vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,3,0), new Vector3(3,-1,0) };
            triangle.triangles = new[] { 0,1,2 }; triangle.UploadMeshData(true);
        }
        internal static bool Supports(Camera c, int w, int h) => c != null && !c.orthographic && !c.stereoEnabled && c.allowHDR &&
            c.actualRenderingPath == RenderingPath.DeferredShading && c.cameraType == CameraType.Game && w >= 16 && h >= 16 && w <= 8192 && h <= 8192 &&
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 && SystemInfo.supportsComputeShaders &&
            SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat) &&
            SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);

        internal int CameraWidth => width;
        internal int CameraHeight => height;

        internal void Ensure(int w, int h, int div,RenderTextureFormat format=RenderTextureFormat.ARGBHalf,int squareTraceSize=0)
        {
            if (w == width && h == height && div == divisor && format==sceneFormat && traceSize==squareTraceSize && TargetsReady()) return;
            ReleaseTargets(); width = w; height = h; divisor = div;sceneFormat=format;traceSize=squareTraceSize;
            try
            {
                NativeResult = Target("native composition",w,h,format);
                Radiance = Target("hit radiance",traceSize>0?traceSize:(w+div-1)/div,traceSize>0?traceSize:(h+div-1)/div,RenderTextureFormat.ARGBHalf,true);
                DenoiseInput=Target("complete reflection signal",Radiance.width,Radiance.height,RenderTextureFormat.ARGBHalf,true);
                Hit = Target("hit confidence and guide",Radiance.width,Radiance.height,RenderTextureFormat.ARGBHalf,true);
                HitPoint=Target("reflected hit position",Radiance.width,Radiance.height,RenderTextureFormat.ARGBHalf,true);
                NativeTest=Target("native hit records",Radiance.width,Radiance.height,RenderTextureFormat.ARGBHalf,true);
                AdaptiveMask=Target("adaptive quad mask",(Radiance.width+1)/2,(Radiance.height+1)/2,RenderTextureFormat.RFloat,true);
                Surface=Target("receiver geometry",Radiance.width,Radiance.height,RenderTextureFormat.ARGBHalf,true);
                int pw = Mathf.NextPowerOfTwo(w), ph = Mathf.NextPowerOfTwo(h);
                do { Depth.Add(Target("Hi-Z level "+Depth.Count,pw,ph,RenderTextureFormat.RFloat,true)); if(pw==1 && ph==1) break; pw=Math.Max(1,pw/2); ph=Math.Max(1,ph/2); } while (true);
            }
            catch { ReleaseTargets(); throw; }
        }
        private void EnsureHistory()
        {
            if(Ready(TemporalResult) && Ready(Filtered) && Ready(History) && Ready(HistoryGuide) && Ready(HistoryPoint) && Ready(Motion) && Ready(HistoryWeight) &&
                Ready(HistorySurface))return;
            ReleaseHistory();
            int w=Radiance.width,h=Radiance.height;
            TemporalResult=Target("temporal reflection",w,h,RenderTextureFormat.ARGBHalf,true);
            Filtered=Target("spatial reflection",w,h,RenderTextureFormat.ARGBHalf,true);
            History=Target("previous reflection",w,h,RenderTextureFormat.ARGBHalf,true);
            HistoryGuide=Target("previous receiver",w,h,RenderTextureFormat.ARGBHalf,true);
            HistoryPoint=Target("previous reflected hit",w,h,RenderTextureFormat.ARGBHalf,true);
            Motion=Target("receiver motion",w,h,RenderTextureFormat.ARGBHalf);
            HistoryWeight=Target("accepted history weight",w,h,RenderTextureFormat.RFloat,true);
            HistorySurface=Target("previous geometry",w,h,RenderTextureFormat.ARGBHalf,true);
        }
        internal void RetainHistory() { pendingHistorySwap=true; }
        private static void Swap(ref RenderTexture a,ref RenderTexture b) { var t=a;a=b;b=t; }
        private void AdvanceHistory()
        {
            if(!pendingHistorySwap)return;
            pendingHistorySwap=false;
            // Defer until the next trace so reconstruction and composition still see
            // this frame's outputs. Recorded commands already own their bindings.
            if(!Ready(TemporalResult) || !Ready(History) || !Ready(Hit) || !Ready(HistoryGuide) ||
                !Ready(HitPoint) || !Ready(HistoryPoint) || !Ready(Surface) || !Ready(HistorySurface))
            { return; } // EnsureHistory rebuilds after tracing, as in the copy path.
            Swap(ref TemporalResult,ref History);Swap(ref Hit,ref HistoryGuide);
            Swap(ref HitPoint,ref HistoryPoint);Swap(ref Surface,ref HistorySurface);
        }
        internal void ResetHistory() { HistoryValid=false;UsedHistory=false;sampleIndex=0; }
        private void PrepareFrame(Camera camera,Options o)
        {
            AdvanceHistory();
            var p=camera.projectionMatrix;var oldWorld=previousView.inverse;var world=camera.cameraToWorldMatrix;
            bool changed=!o.Equals(previousOptions);
            bool cut=Time.frameCount-lastFrame>1 || Time.realtimeSinceStartup-lastTime>.5f ||
                Vector3.Distance(oldWorld.GetColumn(3),world.GetColumn(3))>1 || Vector3.Dot(oldWorld.GetColumn(2),world.GetColumn(2))<.8660254f ||
                Vector3.Dot(oldWorld.GetColumn(1),world.GetColumn(1))<.8660254f ||
                Mathf.Abs(p.m00/previousProjection.m00-1)>.05f || Mathf.Abs(p.m11/previousProjection.m11-1)>.05f ||
                Mathf.Abs(p.m02-previousProjection.m02)>.1f || Mathf.Abs(p.m12-previousProjection.m12)>.1f ||
                camera.nearClipPlane!=previousNear || camera.farClipPlane!=previousFar ||
                (camera.targetTexture!=null?camera.targetTexture.GetInstanceID():0)!=previousTarget;
            if(changed || cut || !o.StochasticRays)ResetHistory();
            sampleOffset=new Vector4(.5f,.5f,0,0);
            // Fixed receiver grid: only the reflection direction varies over time.
        }
        private bool TargetsReady()
        {
            if(!Ready(AdaptiveMask) || !Ready(DenoiseInput) || !Ready(NativeResult) || !Ready(Radiance) || !Ready(Hit) || !Ready(HitPoint) || !Ready(NativeTest) || !Ready(Surface))return false;
            if(Depth.Count==0)return false;
            foreach(var rt in Depth)if(rt==null || !rt.IsCreated())return false;
            return true;
        }
        private static bool Ready(RenderTexture rt) => rt!=null && rt.IsCreated();
        private static RenderTexture Target(string name,int w,int h,RenderTextureFormat format,bool random=false)
        {
            var rt = new RenderTexture(w,h,0,format,RenderTextureReadWrite.Linear) { name="FTFX SSR "+name,enableRandomWrite=random,
                filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave,autoGenerateMips=false,useMipMap=false };
            if(rt.Create()) return rt;
            UnityEngine.Object.DestroyImmediate(rt); throw new InvalidOperationException("SSR allocation failed: "+name);
        }

        internal void Trace(CommandBuffer cb, Camera camera, RenderTargetIdentifier source, Options o,
            RenderTargetIdentifier? rawDepth=null,RenderTargetIdentifier? normals=null,RenderTargetIdentifier? specular=null)
        {
            PrepareFrame(camera,o);
            var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
            cb.SetComputeMatrixParam(compute,"_Projection",projection);
            cb.SetComputeMatrixParam(compute,"_InverseProjection",projection.inverse);
            cb.SetComputeMatrixParam(compute,"_WorldToView",camera.worldToCameraMatrix);
            cb.SetComputeVectorParam(compute,"_Size",new Vector4(width,height,Radiance.width,Radiance.height));
            cb.SetComputeVectorParam(compute,"_Trace",new Vector4(o.Distance,o.Thickness,o.Smoothness,o.Steps));
            cb.SetComputeVectorParam(compute,"_Clip",new Vector4(camera.nearClipPlane,camera.farClipPlane,Depth.Count-1,SystemInfo.usesReversedZBuffer?1:0));
            cb.SetComputeIntParam(compute,"_Stochastic",o.StochasticRays?1:0);
            cb.SetComputeIntParam(compute,"_NativeResponse",o.NativeResponse?1:0);
            cb.SetComputeIntParam(compute,"_NativeTraceContract",o.NativeTraceContract?1:0);
            ActualRawRadiance=!o.StreamlinedOutput;
            ActualAdaptiveRays=o.AdaptiveSampling;
            cb.SetComputeIntParam(compute,"_WriteRawRadiance",ActualRawRadiance?1:0);
            cb.SetComputeIntParam(compute,"_WriteGuides",ActualRawRadiance || o.CustomReconstruction?1:0);
            cb.SetComputeIntParam(compute,"_AdaptiveRays",ActualAdaptiveRays?1:0);
            ActualTraceSteps=o.Steps;ActualTraceDistance=o.Distance;ActualTraceCutoff=o.Smoothness;
            ActualNativeTraceContract=o.NativeTraceContract;ActualSampleIndex=sampleIndex;
            ActualCompactRays=o.CompactRays;
            cb.SetComputeIntParam(compute,"_CompactRays",o.CompactRays?1:0);
            cb.SetComputeIntParam(compute,"_Frame",sampleIndex);
            cb.SetComputeFloatParam(compute,"_EdgePixels",o.EdgePixels);
            cb.SetComputeVectorParam(compute,"_SampleOffset",sampleOffset);
            using(SsrGpuSample.Measure(cb,camera,"HiZ build"))
            {
                // Read the same camera-global texture as native SSR at execution time. ResolvedDepth
                // is not a reliable built-in identifier at this late event on every Unity camera.
                depthProperties.Clear();depthProperties.SetMatrix("_InverseProjection",projection.inverse);
                depthProperties.SetVector("_Size",new Vector4(width,height,0,0));
                depthProperties.SetVector("_DepthOptions",new Vector4(camera.nearClipPlane,camera.farClipPlane,SystemInfo.usesReversedZBuffer?1:0,rawDepth.HasValue?1:0));
                cb.SetGlobalTexture("_FTFXSSR_RawDepth",rawDepth ?? new RenderTargetIdentifier(Texture2D.blackTexture));
                Draw(cb,source,Depth[0],blend,1,depthProperties);
                for(int i=1;i<Depth.Count;i++)
                {
                    cb.SetComputeTextureParam(compute,reduceKernel,"_DepthPrevious",Depth[i-1]);
                    cb.SetComputeTextureParam(compute,reduceKernel,"_DepthOutput",Depth[i]);
                    cb.DispatchCompute(compute,reduceKernel,(Depth[i].width+7)/8,(Depth[i].height+7)/8,1);
                }
            }
            if(ActualAdaptiveRays)
            {
                int classify=compute.FindKernel("ClassifyAdaptive");
                bool valid=HistoryValid && Ready(History) && Ready(HistoryGuide) && Ready(HistoryPoint) &&
                    Ready(HistorySurface) && denoiser!=null && denoiser.AdaptiveReady;
                cb.SetComputeIntParam(compute,"_AdaptiveHistory",valid?1:0);
                cb.SetComputeMatrixParam(compute,"_AdaptivePreviousClip",GL.GetGPUProjectionMatrix(previousProjection,false)*previousView*camera.cameraToWorldMatrix);
                cb.SetComputeMatrixParam(compute,"_AdaptivePreviousToCurrent",camera.worldToCameraMatrix*previousView.inverse);
                cb.SetComputeTextureParam(compute,classify,"_AdaptiveGuide",valid?(Texture)HistoryGuide:Texture2D.blackTexture);
                cb.SetComputeTextureParam(compute,classify,"_AdaptivePoint",valid?(Texture)HistoryPoint:Texture2D.blackTexture);
                cb.SetComputeTextureParam(compute,classify,"_AdaptiveVariance",valid?(Texture)denoiser.AdaptiveVariance:Texture2D.whiteTexture);
                cb.SetComputeTextureParam(compute,classify,"_AdaptiveSamples",valid?(Texture)denoiser.AdaptiveSamples:Texture2D.blackTexture);
                cb.SetComputeTextureParam(compute,classify,"_AdaptiveMotion",normals.HasValue?new RenderTargetIdentifier(Texture2D.blackTexture):new RenderTargetIdentifier(BuiltinRenderTextureType.MotionVectors));
                cb.SetComputeTextureParam(compute,classify,"_AdaptiveMaskOutput",AdaptiveMask);
                BindTrace(cb,classify,source,normals,specular);
                using(SsrGpuSample.Measure(cb,camera,"Adaptive ray classification"))
                    cb.DispatchCompute(compute,classify,(AdaptiveMask.width+7)/8,(AdaptiveMask.height+7)/8,1);
            }
            EnsureRayList(o.CompactRays);
            if(o.CompactRays)
            {
                using(SsrGpuSample.Measure(cb,camera,"Classify and compact rays"))
                {
                    cb.SetComputeBufferParam(compute,resetRaysKernel,"_RayCount",RayCount);
                    cb.DispatchCompute(compute,resetRaysKernel,1,1,1);
                    BindTrace(cb,classifyKernel,source,normals,specular);
                    cb.SetComputeBufferParam(compute,classifyKernel,"_RayList",RayList);
                    cb.SetComputeBufferParam(compute,classifyKernel,"_RayCount",RayCount);
                    cb.DispatchCompute(compute,classifyKernel,(Radiance.width+7)/8,(Radiance.height+7)/8,1);
                    cb.SetComputeBufferParam(compute,prepareRaysKernel,"_RayCount",RayCount);
                    cb.SetComputeBufferParam(compute,prepareRaysKernel,"_RayDispatch",RayDispatch);
                    cb.DispatchCompute(compute,prepareRaysKernel,1,1,1);
                }
            }
            using(SsrGpuSample.Measure(cb,camera,o.CompactRays?"HiZ indirect trace":"HiZ trace"))
            {
                int kernel=traceKernel;
                BindTrace(cb,kernel,source,normals,specular);
                cb.SetComputeBufferParam(compute,kernel,"_RayList",RayList);cb.SetComputeBufferParam(compute,kernel,"_RayCount",RayCount);
                if(o.CompactRays)
                {
                    cb.DispatchCompute(compute,kernel,RayDispatch,0);
                }
                else cb.DispatchCompute(compute,kernel,(Radiance.width+7)/8,(Radiance.height+7)/8,1);
            }
        }

        private void EnsureRayList(bool compact)
        {
            int count=compact?Radiance.width*Radiance.height:1;
            if(RayList!=null && RayList.IsValid() && RayList.count>=count && RayCount!=null && RayCount.IsValid() && RayDispatch!=null && RayDispatch.IsValid())return;
            ReleaseRayList();
            RayList=new ComputeBuffer(count,sizeof(uint));RayCount=new ComputeBuffer(1,sizeof(uint));
            RayDispatch=new ComputeBuffer(3,sizeof(uint),ComputeBufferType.IndirectArguments);
        }
        private void ReleaseRayList()
        { RayList?.Release();RayCount?.Release();RayDispatch?.Release();RayList=RayCount=RayDispatch=null; }
        private void BindTrace(CommandBuffer cb,int kernel,RenderTargetIdentifier source,RenderTargetIdentifier? normals,RenderTargetIdentifier? specular)
        {
            for(int i=0;i<14;i++)cb.SetComputeTextureParam(compute,kernel,"_HiZ"+i,Depth[Math.Min(i,Depth.Count-1)]);
            cb.SetComputeTextureParam(compute,kernel,"_BlueNoise",blueNoise!=null?blueNoise:Texture2D.grayTexture);
            cb.SetComputeTextureParam(compute,kernel,"_Normals",normals ?? new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer2));
            cb.SetComputeTextureParam(compute,kernel,"_Specular",specular ?? new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer1));
            cb.SetComputeTextureParam(compute,kernel,"_Scene",source);
            cb.SetComputeTextureParam(compute,kernel,"_RadianceOutput",Radiance);cb.SetComputeTextureParam(compute,kernel,"_HitOutput",Hit);
            cb.SetComputeTextureParam(compute,kernel,"_PointOutput",HitPoint);cb.SetComputeTextureParam(compute,kernel,"_NativeTestOutput",NativeTest);
            cb.SetComputeTextureParam(compute,kernel,"_SurfaceOutput",Surface);
            cb.SetComputeTextureParam(compute,kernel,"_AdaptiveMask",AdaptiveMask);
        }
        // Replace native pass 2 using exactly its pass-1 input, including misses.
        // Keep the native square grid throughout; no camera-resolution round trip.
        internal void ReconstructNative(CommandBuffer cb,Camera camera,RenderTargetIdentifier source,RenderTargetIdentifier destination,Options o,RenderTargetIdentifier? motion=null,RenderTargetIdentifier? normals=null)
        {
            EnsureHistory();
            if(ActualAdaptiveRays)
            {
                int reconstruct=compute.FindKernel("ReconstructAdaptive");
                cb.SetComputeTextureParam(compute,reconstruct,"_NativeResolved",source);
                cb.SetComputeTextureParam(compute,reconstruct,"_NativeTestInput",NativeTest);
                cb.SetComputeTextureParam(compute,reconstruct,"_AdaptiveMask",AdaptiveMask);
                cb.SetComputeTextureParam(compute,reconstruct,"_DenoiseOutput",DenoiseInput);
                using(SsrGpuSample.Measure(cb,camera,"Adaptive radiance reconstruction"))
                    cb.DispatchCompute(compute,reconstruct,(Radiance.width+7)/8,(Radiance.height+7)/8,1);
            }
            else CopyNativeBase(cb,source,DenoiseInput);
            if(!o.Temporal && !o.Spatial)
            {
                CopyNativeBase(cb,source,destination);
                cb.SetRenderTarget(HistoryWeight);cb.ClearRenderTarget(false,true,Color.clear);
                UsedHistory=false;HistoryValid=false;
            }
            else
            {
                if(denoiser==null)throw new InvalidOperationException("AMD reflections shader missing");
                if(motion.HasValue)cb.Blit(motion.Value,Motion);
                else
                {
                    blendProperties.Clear();blendProperties.SetVector("_Size",new Vector4(width,height,Radiance.width,Radiance.height));
                    blendProperties.SetVector("_SampleOffset",sampleOffset);Draw(cb,source,Motion,blend,2,blendProperties);
                }
                using(SsrGpuSample.Measure(cb,camera,"Custom native-signal reconstruction"))
                    UsedHistory=denoiser.Run(cb,camera,this,o,previousView,previousProjection,o.Temporal && HistoryValid);
                // AMD filters RGB. Preserve native pass-1 alpha and rejected receivers.
                int finish=compute.FindKernel("FinishNativeDenoise");
                cb.SetComputeVectorParam(compute,"_Size",new Vector4(width,height,Radiance.width,Radiance.height));
                cb.SetComputeVectorParam(compute,"_Trace",new Vector4(o.Distance,o.Thickness,o.Smoothness,o.Steps));
                cb.SetComputeTextureParam(compute,finish,"_NativeResolved",DenoiseInput);
                cb.SetComputeTextureParam(compute,finish,"_FilteredInput",TemporalResult);
                cb.SetComputeTextureParam(compute,finish,"_PointInput",HitPoint);
                cb.SetComputeTextureParam(compute,finish,"_Normals",normals ?? new RenderTargetIdentifier(BuiltinRenderTextureType.GBuffer2));
                cb.SetComputeTextureParam(compute,finish,"_RadianceOutput",Filtered);
                cb.DispatchCompute(compute,finish,(Radiance.width+7)/8,(Radiance.height+7)/8,1);
                CopyNativeBase(cb,Filtered,destination);
                HistoryValid=o.Temporal;
            }
            CompleteTraceFrame(camera,o);
        }
        private void CopyNativeBase(CommandBuffer cb,RenderTargetIdentifier source,RenderTargetIdentifier destination)
        {
            if(sceneFormat==RenderTextureFormat.ARGBHalf)cb.CopyTexture(source,0,0,destination,0,0);
            else cb.Blit(source,destination);
        }

        // Native temporal resolve owns history when custom reconstruction is off.
        // Only advance the stochastic sequence/camera bookkeeping here; allocating
        // or filtering AMD history would defeat this isolated sampling comparison.
        internal void CompleteTraceFrame(Camera camera,Options o)
        {
            previousView=camera.worldToCameraMatrix;previousProjection=camera.projectionMatrix;previousOptions=o;
            previousNear=camera.nearClipPlane;previousFar=camera.farClipPlane;
            previousTarget=camera.targetTexture!=null?camera.targetTexture.GetInstanceID():0;
            lastFrame=Time.frameCount;lastTime=Time.realtimeSinceStartup;sampleIndex=unchecked(sampleIndex+1);
        }

        internal void CopyFinal(CommandBuffer cb,RenderTargetIdentifier destination)
        {
            // Preserve the integer-pixel RGBA copy for the rare
            // aliased-source route. Native's general blit can transform UVs.
            blendProperties.Clear();
            blendProperties.SetVector("_Size",new Vector4(width,height,Radiance.width,Radiance.height));
            Draw(cb,NativeResult,destination,blend,0,blendProperties);
        }
        private void Draw(CommandBuffer cb,RenderTargetIdentifier source,RenderTargetIdentifier target,Material material,int pass,MaterialPropertyBlock properties)
        {
            cb.SetGlobalTexture("_MainTex",source);
            cb.SetRenderTarget(target,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
            cb.DrawMesh(triangle,Matrix4x4.identity,material,0,pass,properties);
        }
        private void ReleaseTargets()
        {
            ReleaseRayList();
            Release(AdaptiveMask);AdaptiveMask=null;
            ReleaseHistory();denoiser?.Release();
            Release(DenoiseInput);DenoiseInput=null;
            Release(NativeTest);Release(Surface);NativeTest=Surface=null;
            foreach(var rt in Depth) Release(rt); Depth.Clear();
            Release(NativeResult);Release(Radiance);Release(Hit);Release(HitPoint);
            HitPoint=null;
            NativeResult=Radiance=Hit=null;
        }
        private void ReleaseHistory()
        {
            pendingHistorySwap=false;
            ResetHistory();Release(TemporalResult);Release(Filtered);Release(History);Release(HistoryGuide);Release(HistoryPoint);Release(Motion);Release(HistoryWeight);
            Release(HistorySurface);HistorySurface=null;
            TemporalResult=Filtered=History=HistoryGuide=HistoryPoint=Motion=HistoryWeight=null;
        }
        private static void Release(RenderTexture rt) { if(rt==null)return; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        public void Dispose() { ReleaseTargets();denoiser?.Dispose(); UnityEngine.Object.DestroyImmediate(compute); UnityEngine.Object.DestroyImmediate(blend); UnityEngine.Object.DestroyImmediate(triangle); }
    }
}


