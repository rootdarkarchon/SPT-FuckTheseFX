using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // FTFX owns the complete reflection signal. RGB is premultiplied by coverage
    // from resolve through temporal accumulation; alpha is coverage, never scene alpha.
    internal sealed class SsrReflectionPipeline : IDisposable
    {
        internal readonly SsrPipeline TracePipeline;
        internal readonly NativeReceiverFallback ReceiverFallback;
        internal NativeReflectionReconstruction NativeReconstruction {get;private set;}
        private readonly ComputeShader gaussian;
        internal string LastResetReason {get;private set;}="initial";
        internal RenderTexture Resolved;
        private RenderTexture coverageCapture,haloCapture,fallbackCapture,gapCapture;
        private readonly RenderTexture[] replayInputs=new RenderTexture[6];
        private readonly ContinuationCapture continuationCapture=new ContinuationCapture();
        internal enum Pass { Resolve,Compose,ReplayInput,ComposeNormal }
        private readonly Material material;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly Mesh triangle;
        private Matrix4x4 previousView,previousProjection;
        private Options previous;
        private bool valid;
        internal NrdSpecularBackend Nrd {get;private set;}
        private void RetireNrd(){if(Nrd==null)return;var retired=Nrd;Nrd=null;continuationCapture.ReleaseWhenComplete(retired.Dispose);}
        private bool disposed;
        private int lastFrame=-10;
        private int previousTarget;
        private float lastTime,near,far;
        internal bool UsedHistory { get; private set; }
        internal SsrReflectionPipeline(ComputeShader compute,Shader support,Shader reflection,Texture2D noise,Shader native,ComputeShader gaussian)
        {
            this.gaussian=gaussian;
            ReceiverFallback=new NativeReceiverFallback(native,gaussian);
            TracePipeline=new SsrPipeline(compute,support,noise);
            TracePipeline.RetireTexture=t=>continuationCapture.ReleaseWhenComplete(()=>{t.Release();UnityEngine.Object.DestroyImmediate(t);});
            ReceiverFallback.RetireTexture=TracePipeline.RetireTexture;
            material=new Material(reflection){hideFlags=HideFlags.HideAndDontSave};
            triangle=new Mesh {name="FTFX reflection triangle",hideFlags=HideFlags.HideAndDontSave};
            triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
            triangle.triangles=new[]{0,1,2};triangle.UploadMeshData(true);
        }
        private static bool Ready(RenderTexture t)=>t!=null && t.IsCreated();
        private static RenderTexture Target(string name,int width,int height,RenderTextureFormat format=RenderTextureFormat.ARGBHalf,bool random=false)
        {
            var t=new RenderTexture(width,height,0,format,RenderTextureReadWrite.Linear)
            {name="FTFX SSR "+name,enableRandomWrite=random,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            if(t.Create())return t;
            UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("Reflection allocation failed: "+name);
        }
        internal void Ensure(int w,int h,int size,RenderTextureFormat format)=>Ensure(w,h,size,size,format);
        internal void Ensure(int w,int h,int traceWidth,int traceHeight,RenderTextureFormat format)
        {
            bool changed=TracePipeline.CameraWidth!=w || TracePipeline.CameraHeight!=h || !TracePipeline.Ready || TracePipeline.Radiance.width!=traceWidth || TracePipeline.Radiance.height!=traceHeight || TracePipeline.NativeResult.format!=format;
            if(!changed && Ready(Resolved)){ReceiverFallback.Ensure(traceWidth,traceHeight);return;}
            // Retire detached resources only after pending capture readbacks.
            Release();
            TracePipeline.Ensure(w,h,traceWidth,traceHeight,format);
            ReceiverFallback.Ensure(traceWidth,traceHeight);
            if(NativeReconstruction!=null){var retired=NativeReconstruction;NativeReconstruction=null;continuationCapture.ReleaseWhenComplete(retired.Dispose);}
            Resolved=Target("radiance times coverage",traceWidth,traceHeight);
        }
        internal void PrepareRaster(Camera camera,int mode,int width,int height) {
            var raster=TracePipeline.Raster;raster.Enable(camera);
            if(raster.Record(camera,mode,width,height)){ResetHistory();LastResetReason="AA-mode or raster-size";}
        }
        internal void ResetHistory() { if(Nrd!=null)Nrd.Reset();TracePipeline.Raster.InvalidateHistory();valid=false;UsedHistory=false;LastResetReason="explicit or resource lifecycle";TracePipeline.ResetHistory();ReceiverFallback.ResetHistory(); }
        internal void Render(CommandBuffer cb,Camera camera,RenderTargetIdentifier source,RenderTargetIdentifier destination,Options o,
            RenderTargetIdentifier? rawDepth=null,RenderTargetIdentifier? normals=null,RenderTargetIdentifier? specular=null,bool traceDiagnostics=false)
        {
            RequireNrd();
            string reason="";
            if(Time.frameCount-lastFrame>1)reason+="frame-gap;";
            if(Time.realtimeSinceStartup-lastTime>.5f)reason+="time-gap;";
            if(Vector3.Distance(previousView.inverse.GetColumn(3),camera.cameraToWorldMatrix.GetColumn(3))>1)reason+="translation;";
            if(Vector3.Dot(previousView.inverse.GetColumn(2),camera.cameraToWorldMatrix.GetColumn(2))<.8660254f ||
               Vector3.Dot(previousView.inverse.GetColumn(1),camera.cameraToWorldMatrix.GetColumn(1))<.8660254f)reason+="rotation;";
            if(Mathf.Abs(previousProjection.m00-camera.projectionMatrix.m00)>.01 || Mathf.Abs(previousProjection.m11-camera.projectionMatrix.m11)>.01 ||
               near!=camera.nearClipPlane || far!=camera.farClipPlane || previousTarget!=(camera.targetTexture!=null?camera.targetTexture.GetInstanceID():0))reason+="projection-target;";
            if(!o.SameRendering(previous))reason+="settings;";
            if(reason.Length>0){ResetHistory();LastResetReason=reason;}
            continuationCapture.Rearm(o.CaptureContinuation,o.CaptureTraversal);
            bool diagnostics=traceDiagnostics || continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal) || o.View==ReflectionView.DepthHitKind || o.View==ReflectionView.DepthHitConfidence;
            TracePipeline.Trace(cb,camera,source,o,rawDepth,normals,specular,diagnostics,continuationCapture.Pending,o.CaptureTraversal && continuationCapture.Wants(true));
            Reconstruct(cb,camera,source,destination,o);

            previous=o;previousView=camera.worldToCameraMatrix;previousProjection=camera.projectionMatrix;
            near=camera.nearClipPlane;far=camera.farClipPlane;lastFrame=Time.frameCount;lastTime=Time.realtimeSinceStartup;
            previousTarget=camera.targetTexture!=null?camera.targetTexture.GetInstanceID():0;
        }
        // Also callable by frozen-signal GPU fixtures without changing the production tracer.
        internal void Reconstruct(CommandBuffer cb,Camera camera,RenderTargetIdentifier source,RenderTargetIdentifier destination,Options o)
        {
            RequireNrd();
            var p=TracePipeline;var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
            continuationCapture.Rearm(o.CaptureContinuation,o.CaptureTraversal);
            bool nativeReconstruction=o.Reconstruction==ReflectionReconstruction.NativeGaussian;
            properties.Clear();
            properties.SetVector("_NativeReconstruction",Vector4.zero);
            properties.SetBuffer("_SsrRasterState",p.Raster.Buffer);properties.SetFloat("_SsrLateRaster",p.Raster.Enabled?1:0);
            var water=WaterReflectionCapture.Get(camera,p.CameraWidth,p.CameraHeight);
            properties.SetTexture("_WaterCoverage",water!=null?(Texture)water:Texture2D.blackTexture);
            properties.SetFloat("_WaterAvailable",water!=null?1:0);
            var surface=o.SurfaceColourInfluence>0?AmbientSurfaceCapture.Get(camera,p.CameraWidth,p.CameraHeight):null;
            properties.SetTexture("_PreAmbientSurface",surface!=null?(Texture)surface:Texture2D.blackTexture);
            properties.SetFloat("_SurfaceColourInfluence",surface!=null?o.SurfaceColourInfluence:0);
            properties.SetVector("_Size",new Vector4(p.CameraWidth,p.CameraHeight,p.Radiance.width,p.Radiance.height));
            UsedHistory=valid && o.Temporal;
            properties.SetVector("_ReflectionOptions",new Vector4(o.Smoothness,(int)o.View,0,0));
            properties.SetVector("_ZeroSmoothnessProbe",new Vector4(o.ZeroSmoothnessProbe?1:0,o.ZeroSmoothnessProbeStrength,0,0));
            properties.SetVector("_ZeroSmoothnessProbeFade",new Vector4(o.ZeroSmoothnessProbeClose,o.ZeroSmoothnessProbeFadeStart,o.ZeroSmoothnessProbeFadeEnd,0));
            properties.SetVector("_ReceiverCutoff",new Vector4(o.SupportSmoothness??o.Smoothness,o.CutoffEnvironmentBlend,0,0));
            properties.SetVector("_ReceiverRange",new Vector4(o.LimitNativeRange?1:0,o.Distance,0,0));
            properties.SetMatrix("_InvProjection",projection.inverse);properties.SetMatrix("_CameraToWorld",camera.cameraToWorldMatrix);
            properties.SetTexture("_Raw",p.Radiance);properties.SetTexture("_Guide",p.Hit);properties.SetTexture("_Point",p.HitPoint);properties.SetTexture("_Record",p.Record);
            properties.SetTexture("_TraceInfo",p.TraceInfo);properties.SetTexture("_TraceStop",p.TraceStop!=null?(Texture)p.TraceStop:Texture2D.blackTexture);
            properties.SetTexture("_DepthHit",p.DepthHit!=null?(Texture)p.DepthHit:Texture2D.blackTexture);properties.SetFloat("_ThicknessConfidenceFloor",o.ThicknessConfidenceFloor);
            properties.SetVector("_ContinuationLimits",new Vector4(o.ContinuationSamples,o.ContinuationPixels,o.ContinuationDepth,0));
            properties.SetTexture("_Depth",p.Depth[0]);
            using(SsrGpuSample.Measure(cb,camera,"Receiver colour fallback"))ReceiverFallback.Render(cb,source,o.SupportSmoothness??o.Smoothness,o.Temporal);
            properties.SetTexture("_ReceiverFallback",ReceiverFallback.Radiance);
            properties.SetFloat("_FallbackMipCount",ReceiverFallback.MipCount);
            properties.SetFloat("_FallbackStrength",o.FallbackStrength);
            using(SsrGpuSample.Measure(cb,camera,"Radiance and coverage resolve"))Draw(cb,source,Resolved,Pass.Resolve);
            using(SsrGpuSample.Measure(cb,camera,"NRD specular denoising")) {
                if(!Nrd.Render(cb,camera,p,Resolved,o))throw new InvalidOperationException("NRD frame preparation failed: "+Nrd.Error);
            }
            RenderTexture signal=Nrd.Output;
            var unfilled=signal;
            properties.SetTexture("_UnfilledSignal",unfilled);
            properties.SetTexture("_Signal",signal);
            if(nativeReconstruction) {
                if(NativeReconstruction==null)NativeReconstruction=new NativeReflectionReconstruction(gaussian);
                NativeReconstruction.Ensure(unfilled.width,unfilled.height);
                using(SsrGpuSample.Measure(cb,camera,"Native Gaussian reconstruction"))signal=NativeReconstruction.Render(cb,signal);
                properties.SetTexture("_Signal",signal);
                properties.SetVector("_NativeReconstruction",new Vector4(1,NativeReconstruction.MipCount,0,0));
            } else if(NativeReconstruction!=null && !continuationCapture.Pending) {
                NativeReconstruction.Dispose();NativeReconstruction=null;
            }
            if(continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal)) {
                for(int k=0;k<replayInputs.Length;k++) {
                    EnsureCapture(ref replayInputs[k],p.CameraWidth,p.CameraHeight);
                    CaptureReplayInput(cb,source,replayInputs[k],k);
                }
            }
            bool alias=source.Equals(destination);
            using(SsrGpuSample.Measure(cb,camera,"Environment replacement"))Draw(cb,source,alias?new RenderTargetIdentifier(p.NativeResult):destination,o.View==ReflectionView.Normal?Pass.ComposeNormal:Pass.Compose);
            if(alias)using(SsrGpuSample.Measure(cb,camera,"Alias result copy"))p.CopyFinal(cb,destination);
            if(continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal)) {
                EnsureCapture(ref coverageCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref haloCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref fallbackCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref gapCapture,p.CameraWidth,p.CameraHeight);
                properties.SetVector("_ReflectionOptions",new Vector4(o.Smoothness,26,0,0));Draw(cb,source,coverageCapture,Pass.Compose);
                properties.SetVector("_ReflectionOptions",new Vector4(o.Smoothness,33,0,0));Draw(cb,source,haloCapture,Pass.Compose);
                properties.SetVector("_ReflectionOptions",new Vector4(o.Smoothness,28,0,0));Draw(cb,source,fallbackCapture,Pass.Compose);
                properties.SetVector("_ReflectionOptions",new Vector4(o.Smoothness,36,0,0));Draw(cb,source,gapCapture,Pass.Compose);
                var targets=new Dictionary<string,RenderTexture> {
                    {o.CaptureTraversal?"traversal-work":"sample-direction-pdf",p.Sample},{"trace-cost",p.TraceInfo},{"first-rejection",p.TraceStop},{"trace-record",p.Record},{"trace-guide",p.Hit},
                    {"resolved",Resolved},{"filtered",unfilled},{"gap-filled",signal},{"gap-fill-coverage",gapCapture},{"coverage-stages",coverageCapture},{"halo-stages",haloCapture},
                    {"receiver-fallback",fallbackCapture},{"trace-depth",p.Depth[0]},{"raw-radiance",p.Radiance},{o.CaptureTraversal?"traversal-topology":"depth-hit",p.DepthHit}
                };
                targets.Add("nrd-normal-roughness",Nrd.Normal);targets.Add("nrd-receiver-hit-distance-eligibility",Nrd.Receiver);
                targets.Add("nrd-motion",Nrd.Motion);targets.Add("nrd-display",Nrd.Output);
                string[] inputNames={"scene-source","gbuffer0","gbuffer1","gbuffer2","motion","water"};
                for(int k=0;k<replayInputs.Length;k++)targets.Add(inputNames[k],replayInputs[k]);
                string metadata="Denoiser=NRD_REBLUR_SPECULAR"+"\nDirection=Ggx\nDepthGapFill=ReceiverColourFallback\nSourceBlend=0\nRelaxReturn=True\nSkyAfterContinuation=True\nSkyRecoveryScope="+(o.GlobalContinuedSky?"AllEligible":"Water")+"\nSpatial=False\nHitAware=False\nShallowFade=0\nTraceShape=Square\nReconstruction="+o.Reconstruction+
                    "\nProjection="+MatrixText(projection)+"\nCameraToWorld="+MatrixText(camera.cameraToWorldMatrix)+
                    "\nPreviousView="+MatrixText(previousView)+"\nPreviousProjection="+MatrixText(GL.GetGPUProjectionMatrix(previousProjection,false))+"\nHistoryValid="+UsedHistory+"\nLastResetReason="+LastResetReason;
                var rasterSnapshot=p.Raster.Current;
                continuationCapture.Request(cb,targets,o,p.CameraWidth,p.CameraHeight,metadata,
                    rasterSnapshot!=null?(Func<string>)(()=>rasterSnapshot.Metadata()):null);
            } else if(!continuationCapture.Pending) {Drop(ref coverageCapture);Drop(ref haloCapture);Drop(ref fallbackCapture);Drop(ref gapCapture);for(int k=0;k<replayInputs.Length;k++)Drop(ref replayInputs[k]);}
            valid=o.Temporal;
        }
        private void RequireNrd() {
            if(Nrd!=null && Nrd.Stats.Status<0){string error=Nrd.Error;RetireNrd();throw new InvalidOperationException("NRD dispatch failed: "+error);}
            if(Nrd==null)Nrd=NrdSpecularBackend.TryCreate(TracePipeline.RetireTexture);
            if(Nrd==null)throw new InvalidOperationException("NRD backend unavailable; native SSR is required");
        }
        private void EnsureCapture(ref RenderTexture texture,int w,int h) {
            if(texture!=null && texture.IsCreated() && texture.width==w && texture.height==h)return;
            Drop(ref texture);texture=new RenderTexture(w,h,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            if(!texture.Create())throw new InvalidOperationException("SSR diagnostic allocation failed");
        }
        // Sample the actual shader globals explicitly. Generic Blit of a named
        // global can resolve to the active scene target instead of the texture.
        internal void CaptureReplayInput(CommandBuffer cb,RenderTargetIdentifier source,RenderTexture target,int input) {
            properties.SetInt("_ReplayInput",input);Draw(cb,source,target,Pass.ReplayInput);
        }
        private void Draw(CommandBuffer cb,RenderTargetIdentifier source,RenderTargetIdentifier target,Pass pass)
        {
            cb.SetGlobalTexture("_MainTex",source);cb.SetRenderTarget(target,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store);
            cb.DrawMesh(triangle,Matrix4x4.identity,material,0,(int)pass,properties);
        }
        private static string MatrixText(Matrix4x4 m) {
            var values=new string[16];for(int i=0;i<16;i++)values[i]=m[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            return string.Join(",",values);
        }
        private void Drop(ref RenderTexture t) { if(t!=null){var retired=t;continuationCapture.ReleaseWhenComplete(()=>{retired.Release();UnityEngine.Object.DestroyImmediate(retired);});}t=null; }
        private void Release() {
            ResetHistory();for(int i=0;i<replayInputs.Length;i++)Drop(ref replayInputs[i]);
            Drop(ref coverageCapture);Drop(ref haloCapture);Drop(ref fallbackCapture);Drop(ref gapCapture);
            Drop(ref Resolved);
        }
        public void Dispose() {if(disposed)return;disposed=true;continuationCapture.Dispose();Release();RetireNrd();if(NativeReconstruction!=null)continuationCapture.ReleaseWhenComplete(NativeReconstruction.Dispose);NativeReconstruction=null;ReceiverFallback.Dispose();TracePipeline.Dispose();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(triangle);}
    }
}

