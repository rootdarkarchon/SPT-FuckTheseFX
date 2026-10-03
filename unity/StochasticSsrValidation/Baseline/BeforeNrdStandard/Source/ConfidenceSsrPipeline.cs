using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // FTFX owns the complete reflection signal. RGB is premultiplied by coverage
    // from resolve through temporal accumulation; alpha is coverage, never scene alpha.
    internal sealed class ConfidenceSsrPipeline : IDisposable
    {
        internal readonly SsrPipeline TracePipeline;
        internal readonly NativeReceiverFallback ReceiverFallback;
        internal NativeReflectionReconstruction NativeReconstruction {get;private set;}
        private readonly ComputeShader gaussian;
        internal RenderTexture Resolved, Temporal, History, HistoryGuide, HistoryPoint,HistoryMaterial;
        internal RenderTexture TemporalStats,HistoryStats,HistoryPlane;
        internal RenderTexture TemporalAnchor,HistoryAnchor;
        // Published outputs stay readable until their next producer. The other
        // allocation is retained here, never copied, then reclaimed for writing.
        private RenderTexture spareTemporal,spareGuide,sparePoint,spareStats,spareAnchor;
        internal string LastResetReason {get;private set;}="initial";
        internal RenderTexture DepthGapFilled;
        private RenderTexture exactResidual;
        private RenderTexture coverageCapture,haloCapture,fallbackCapture,gapCapture;
        private RenderTexture previousRecordCapture;
        private readonly RenderTexture[] replayInputs=new RenderTexture[6];
        private readonly ContinuationCapture continuationCapture=new ContinuationCapture();
        internal enum Pass { Resolve,Temporal,Compose,MaterialContext,ExactSubtract,ReceiverContext,ReplayInput,DepthGapFill,ComposeNormal,ResolveDisplay,TemporalDisplay,CoverageDisplay }
        private readonly Material material;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly Mesh triangle;
        private Matrix4x4 previousView,previousProjection;
        private Options previous;
        private bool valid;
        private bool recordHistoryAvailable;
        internal NrdSpecularBackend Nrd {get;private set;}
        private bool nrdFailed;
        private void RetireNrd(){if(Nrd==null)return;var retired=Nrd;Nrd=null;continuationCapture.ReleaseWhenComplete(retired.Dispose);}
        private bool SelectNrd(Options o) {
            if(!o.NrdSpecular){RetireNrd();nrdFailed=false;return false;}
            if(Nrd!=null && Nrd.Stats.Status<0){Plugin.MyLog.LogWarning("Experimental NRD dispatch failed; using FTFX until NRD is toggled off/on: "+Nrd.Error);RetireNrd();nrdFailed=true;ResetHistory();}
            if(!nrdFailed && Nrd==null)Nrd=NrdSpecularBackend.TryCreate(TracePipeline.RetireTexture);
            return Nrd!=null;
        }
        private bool disposed;
        private int lastFrame=-10;
        private int previousTarget;
        private float lastTime,near,far;
        internal bool UsedHistory { get; private set; }
        internal ConfidenceSsrPipeline(ComputeShader compute,Shader support,Shader reflection,Texture2D noise,Shader native,ComputeShader gaussian)
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
            // Detach history aliases before the tracer retires its allocations.
            Release();
            TracePipeline.Ensure(w,h,traceWidth,traceHeight,format);
            ReceiverFallback.Ensure(traceWidth,traceHeight);
            if(NativeReconstruction!=null){var retired=NativeReconstruction;NativeReconstruction=null;continuationCapture.ReleaseWhenComplete(retired.Dispose);}
            Resolved=Target("radiance times coverage",traceWidth,traceHeight);
        }
        private void EnsureTemporal(bool enabled,bool coverage)
        {
            if(!enabled){ReleaseTemporal();return;}
            if(!(Ready(Temporal) && Ready(History) && Ready(HistoryGuide) && Ready(HistoryPoint) && Ready(HistoryMaterial) && Ready(TemporalStats) && Ready(HistoryStats) && Ready(HistoryPlane))) {
            ReleaseTemporal();ResetHistory();
            int traceWidth=Resolved.width,traceHeight=Resolved.height;
            Temporal=Target("temporal coverage",traceWidth,traceHeight);
            History=Target("history radiance times coverage",traceWidth,traceHeight);HistoryGuide=Target("history receiver",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat,true);HistoryPoint=Target("history hit",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat,true);
            HistoryMaterial=Target("history material",traceWidth,traceHeight);
            TemporalStats=Target("temporal count moments rejection",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            HistoryStats=Target("history count moments",traceWidth,traceHeight,RenderTextureFormat.ARGBFloat);
            HistoryPlane=Target("history receiver plane",traceWidth,traceHeight);
            }
            if(coverage && (!Ready(TemporalAnchor) || !Ready(HistoryAnchor))) {
                ReleasePair(ref TemporalAnchor,ref HistoryAnchor,ref spareAnchor);
                TemporalAnchor=Target("temporal source anchor",Resolved.width,Resolved.height,RenderTextureFormat.ARGBFloat);
                HistoryAnchor=Target("history source anchor",Resolved.width,Resolved.height,RenderTextureFormat.ARGBFloat);
                ResetHistory();
            } else if(!coverage) ReleasePair(ref TemporalAnchor,ref HistoryAnchor,ref spareAnchor);
        }
        private void ReleaseTemporal()
        {
            recordHistoryAvailable=false;
            ReleasePair(ref Temporal,ref History,ref spareTemporal);
            ReleasePair(ref TemporalStats,ref HistoryStats,ref spareStats);
            ReleasePair(ref TemporalAnchor,ref HistoryAnchor,ref spareAnchor);
            ReleaseTraceHistory(ref HistoryGuide,TracePipeline.Hit,ref spareGuide);
            ReleaseTraceHistory(ref HistoryPoint,TracePipeline.HitPoint,ref sparePoint);
            Drop(ref HistoryMaterial);Drop(ref HistoryPlane);
        }
        private static void AcquireWrite(ref RenderTexture output,RenderTexture history,ref RenderTexture spare) {
            if(!ReferenceEquals(output,history) || output==null)return;
            if(spare==null)throw new InvalidOperationException("Missing SSR history write allocation");
            output=spare;spare=null;
        }
        private static void Publish(RenderTexture output,ref RenderTexture history,ref RenderTexture spare) {
            if(ReferenceEquals(output,history))return; // Frozen-input reconstruction.
            if(spare!=null)throw new InvalidOperationException("SSR history allocation already retained");
            spare=history;history=output;
        }
        internal void PrepareTraceHistory() {
            AcquireWrite(ref TracePipeline.Hit,HistoryGuide,ref spareGuide);
            AcquireWrite(ref TracePipeline.HitPoint,HistoryPoint,ref sparePoint);
        }
        private void ReleasePair(ref RenderTexture output,ref RenderTexture history,ref RenderTexture spare) {
            if(ReferenceEquals(output,history))history=null;
            Drop(ref output);Drop(ref history);Drop(ref spare);
        }
        private void ReleaseTraceHistory(ref RenderTexture history,RenderTexture output,ref RenderTexture spare) {
            if(ReferenceEquals(output,history))history=null; // Tracer owns this allocation.
            Drop(ref history);Drop(ref spare);
        }
        internal void PrepareRaster(Camera camera,int mode,int width,int height) {
            var raster=TracePipeline.Raster;raster.Enable(camera);
            if(raster.Record(camera,mode,width,height)){ResetHistory();LastResetReason="AA-mode or raster-size";}
        }
        internal void ResetHistory() { if(Nrd!=null)Nrd.Reset();TracePipeline.Raster.InvalidateHistory();valid=false;UsedHistory=false;LastResetReason="explicit or resource lifecycle";TracePipeline.ResetHistory();ReceiverFallback.ResetHistory(); }
        internal void Render(CommandBuffer cb,Camera camera,RenderTargetIdentifier source,RenderTargetIdentifier destination,Options o,
            RenderTargetIdentifier? rawDepth=null,RenderTargetIdentifier? normals=null,RenderTargetIdentifier? specular=null,bool traceDiagnostics=false)
        {
            // Allocate/reset before tracing so enabling history cannot rewind
            // the stochastic sample phase after this frame's ray dispatch.
            EnsureTemporal(o.Temporal && !SelectNrd(o),o.CoverageTemporal && o.HistoryFrames>1);
            PrepareTraceHistory();
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
            if(o.Temporal && continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal)) {
                // Capture the full previous record before tracing overwrites its
                // single allocation. Rendering retains only classification in
                // HistoryPlane.z; this snapshot exists for one-shot diagnostics.
                EnsureCapture(ref previousRecordCapture,TracePipeline.Record.width,TracePipeline.Record.height);
                if(recordHistoryAvailable)cb.CopyTexture(TracePipeline.Record,previousRecordCapture);
                else {cb.SetRenderTarget(previousRecordCapture);cb.ClearRenderTarget(false,true,Color.clear);}
            }
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
            bool useNrd=SelectNrd(o);
            EnsureTemporal(o.Temporal && !useNrd,o.CoverageTemporal && o.HistoryFrames>1);
            AcquireWrite(ref Temporal,History,ref spareTemporal);
            AcquireWrite(ref TemporalStats,HistoryStats,ref spareStats);
            AcquireWrite(ref TemporalAnchor,HistoryAnchor,ref spareAnchor);
            var p=TracePipeline;var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
            bool nativeReconstruction=o.Reconstruction==ReflectionReconstruction.NativeGaussian;
            properties.Clear();
            properties.SetVector("_NativeReconstruction",Vector4.zero);
            properties.SetBuffer("_SsrRasterState",p.Raster.Buffer);properties.SetFloat("_SsrLateRaster",p.Raster.Enabled?1:0);
            var water=WaterReflectionCapture.Get(camera,p.CameraWidth,p.CameraHeight);
            properties.SetTexture("_WaterCoverage",water!=null?(Texture)water:Texture2D.blackTexture);
            properties.SetFloat("_WaterAvailable",water!=null?1:0);
            properties.SetTexture("_WaterNormal",p.WaterNormalsActive?(Texture)p.WaterNormal:Texture2D.blackTexture);
            properties.SetFloat("_WaterNormalsEnabled",o.WaterNormals && p.WaterNormalsActive?1:0);
            var surface=o.SurfaceColourInfluence>0?AmbientSurfaceCapture.Get(camera,p.CameraWidth,p.CameraHeight):null;
            properties.SetTexture("_PreAmbientSurface",surface!=null?(Texture)surface:Texture2D.blackTexture);
            properties.SetFloat("_SurfaceColourInfluence",surface!=null?o.SurfaceColourInfluence:0);
            properties.SetVector("_Size",new Vector4(p.CameraWidth,p.CameraHeight,p.Radiance.width,p.Radiance.height));
            UsedHistory=valid && o.Temporal;
            properties.SetVector("_Options",new Vector4(UsedHistory?1:0,o.HistoryFrames,o.Smoothness,(int)o.View));
            bool coverageTemporal=!useNrd && o.Temporal && o.CoverageTemporal && o.HistoryFrames>1;
            properties.SetVector("_CoverageTemporal",new Vector4(coverageTemporal?1:0,o.CoverageFullSmoothness,o.CoverageEndSmoothness,0));
            properties.SetVector("_CoverageFilter",new Vector4(o.CoverageFilterStrength,o.CoverageFilterFullSmoothness,o.CoverageFilterEndSmoothness,o.CoverageFilterSmoothStrength));
            properties.SetFloat("_CoverageFilterHistory",o.Temporal && o.HistoryFrames>1?1:0);
            properties.SetVector("_ZeroSmoothnessProbe",new Vector4(o.ZeroSmoothnessProbe?1:0,o.ZeroSmoothnessProbeStrength,0,0));
            properties.SetVector("_ZeroSmoothnessProbeFade",new Vector4(o.ZeroSmoothnessProbeClose,o.ZeroSmoothnessProbeFadeStart,o.ZeroSmoothnessProbeFadeEnd,0));
            properties.SetTexture("_HistoryAnchor",HistoryAnchor!=null?(Texture)HistoryAnchor:Texture2D.blackTexture);
            properties.SetVector("_ReceiverCutoff",new Vector4(o.SupportSmoothness??o.Smoothness,o.CutoffEnvironmentBlend,0,0));
            properties.SetVector("_ReceiverRange",new Vector4(o.LimitNativeRange?1:0,o.Distance,0,0));
            properties.SetMatrix("_InvProjection",projection.inverse);properties.SetMatrix("_CameraToWorld",camera.cameraToWorldMatrix);
            properties.SetMatrix("_Projection",projection);
            properties.SetMatrix("_PreviousClip",GL.GetGPUProjectionMatrix(previousProjection,false)*previousView*camera.cameraToWorldMatrix);
            properties.SetMatrix("_PreviousToCurrent",camera.worldToCameraMatrix*previousView.inverse);
            properties.SetTexture("_Raw",p.Radiance);properties.SetTexture("_Guide",p.Hit);properties.SetTexture("_Point",p.HitPoint);properties.SetTexture("_Record",p.Record);
            properties.SetTexture("_TraceInfo",p.TraceInfo);properties.SetTexture("_TraceStop",p.TraceStop);
            properties.SetTexture("_DepthHit",p.DepthHit!=null?(Texture)p.DepthHit:Texture2D.blackTexture);properties.SetFloat("_ThicknessConfidenceFloor",o.ThicknessConfidenceFloor);
            properties.SetVector("_ContinuationLimits",new Vector4(o.ContinuationSamples,o.ContinuationPixels,o.ContinuationDepth,0));
            properties.SetTexture("_Depth",p.Depth[0]);properties.SetTexture("_History",History!=null?(Texture)History:Texture2D.blackTexture);properties.SetTexture("_HistoryGuide",HistoryGuide!=null?(Texture)HistoryGuide:Texture2D.blackTexture);
            properties.SetTexture("_HistoryPoint",HistoryPoint!=null?(Texture)HistoryPoint:Texture2D.blackTexture);
            properties.SetTexture("_HistoryMaterial",HistoryMaterial!=null?(Texture)HistoryMaterial:Texture2D.blackTexture);
            properties.SetTexture("_HistoryStats",HistoryStats!=null?(Texture)HistoryStats:Texture2D.blackTexture);properties.SetTexture("_HistoryPlane",HistoryPlane!=null?(Texture)HistoryPlane:Texture2D.blackTexture);
            properties.SetTexture("_TemporalStats",TemporalStats!=null?(Texture)TemporalStats:Texture2D.blackTexture);
            properties.SetMatrix("_PreviousInverseProjection",GL.GetGPUProjectionMatrix(previousProjection,false).inverse);
            var captured=AmbientReflectionCapture.Get(camera,p.CameraWidth,p.CameraHeight);
            properties.SetTexture("_CapturedReflection",captured!=null?(Texture)captured:Texture2D.blackTexture);
            properties.SetFloat("_CaptureAvailable",captured!=null?1:0);
            properties.SetFloat("_UseCapturedContribution",o.UseCapturedContribution?1:0);
            using(SsrGpuSample.Measure(cb,camera,"Receiver colour fallback"))ReceiverFallback.Render(cb,source,o.SupportSmoothness??o.Smoothness,o.Temporal);
            properties.SetTexture("_ReceiverFallback",ReceiverFallback.Radiance);
            properties.SetFloat("_FallbackMipCount",ReceiverFallback.MipCount);
            properties.SetFloat("_FallbackStrength",o.FallbackStrength);
            bool roundTrip=o.View==ReflectionView.ExactRoundTrip || o.View==ReflectionView.ExactRoundTripError;
            if(roundTrip) {
                if(exactResidual==null || !exactResidual.IsCreated() || exactResidual.width!=p.CameraWidth || exactResidual.height!=p.CameraHeight) {
                    Drop(ref exactResidual);
                    exactResidual=new RenderTexture(p.CameraWidth,p.CameraHeight,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) {
                        name="FTFX exact subtraction residual (diagnostic)",filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};
                    if(!exactResidual.Create())throw new InvalidOperationException("Exact subtraction diagnostic allocation failed");
                }
                Draw(cb,source,exactResidual,Pass.ExactSubtract);properties.SetTexture("_ExactResidual",exactResidual);
            } else Drop(ref exactResidual);
            if(!Ready(DepthGapFilled))DepthGapFilled=Target("missing depth coverage display",p.Radiance.width,p.Radiance.height);
            using(SsrGpuSample.Measure(cb,camera,"Radiance and coverage resolve")) {
                if(o.Temporal && !useNrd)Draw(cb,source,Resolved,Pass.Resolve);
                else DrawMany(cb,source,Pass.ResolveDisplay,Resolved,DepthGapFilled);
            }
            RenderTexture signal=Resolved;
            RenderTexture historySignal;
            if(useNrd)using(SsrGpuSample.Measure(cb,camera,"NRD specular replacement")) {
                if(Nrd.Render(cb,camera,p,Resolved,o))signal=Nrd.Output;
                else {useNrd=false;RetireNrd();nrdFailed=true;o.Temporal=false;UsedHistory=false;}
            }
            if(o.Temporal && !useNrd) {
                properties.SetTexture("_Signal",signal);
                using(SsrGpuSample.Measure(cb,camera,"Confidence temporal")) {
                    if(coverageTemporal)DrawMany(cb,source,Pass.TemporalDisplay,Temporal,TemporalStats,DepthGapFilled,TemporalAnchor);
                    else DrawMany(cb,source,Pass.TemporalDisplay,Temporal,TemporalStats,DepthGapFilled);
                }
                signal=Temporal;
            }
            historySignal=signal;
            properties.SetTexture("_Signal",signal);
            // History/display carry the temporal estimate when enabled. Raw
            // classifications and measured point metadata stay separate.
            RenderTexture unfilled=signal;
            properties.SetTexture("_UnfilledSignal",unfilled);
            // Filter only display output. History store below keeps the measured
            // temporal estimate, preventing recursive spatial diffusion.
            bool coverageFilter=!useNrd && o.CoverageFilter && o.CoverageFilterStrength>0 && !nativeReconstruction;
            if(coverageFilter)using(SsrGpuSample.Measure(cb,camera,"Coverage reconstruction filter"))Draw(cb,source,DepthGapFilled,Pass.CoverageDisplay);
            // The legacy display attachment shares the resolve/temporal draw.
            signal=useNrd?unfilled:DepthGapFilled;properties.SetTexture("_Signal",signal);
            if(nativeReconstruction) {
                if(NativeReconstruction==null)NativeReconstruction=new NativeReflectionReconstruction(gaussian);
                NativeReconstruction.Ensure(unfilled.width,unfilled.height);
                using(SsrGpuSample.Measure(cb,camera,"Native Gaussian reconstruction"))signal=NativeReconstruction.Render(cb,signal);
                properties.SetTexture("_Signal",signal);
                properties.SetVector("_NativeReconstruction",new Vector4(1,NativeReconstruction.MipCount,0,0));
            } else if(NativeReconstruction!=null && !continuationCapture.Pending) {
                NativeReconstruction.Dispose();NativeReconstruction=null;
            }
            continuationCapture.Rearm(o.CaptureContinuation,o.CaptureTraversal);
            if(continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal)) {
                for(int k=0;k<replayInputs.Length;k++) {
                    EnsureCapture(ref replayInputs[k],p.CameraWidth,p.CameraHeight);
                    CaptureReplayInput(cb,source,replayInputs[k],k);
                }
            }
            bool alias=source.Equals(destination);
            using(SsrGpuSample.Measure(cb,camera,"Environment replacement"))Draw(cb,source,alias?new RenderTargetIdentifier(p.NativeResult):destination,o.View==ReflectionView.Normal?Pass.ComposeNormal:Pass.Compose);
            if(alias)using(SsrGpuSample.Measure(cb,camera,"Alias result copy"))p.CopyFinal(cb,destination);
            continuationCapture.Rearm(o.CaptureContinuation,o.CaptureTraversal);
            if(continuationCapture.Wants(o.CaptureContinuation || o.CaptureTraversal)) {
                EnsureCapture(ref coverageCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref haloCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref fallbackCapture,p.CameraWidth,p.CameraHeight);
                EnsureCapture(ref gapCapture,p.CameraWidth,p.CameraHeight);
                properties.SetVector("_Options",new Vector4(UsedHistory?1:0,o.HistoryFrames,o.Smoothness,26));Draw(cb,source,coverageCapture,Pass.Compose);
                properties.SetVector("_Options",new Vector4(UsedHistory?1:0,o.HistoryFrames,o.Smoothness,33));Draw(cb,source,haloCapture,Pass.Compose);
                properties.SetVector("_Options",new Vector4(UsedHistory?1:0,o.HistoryFrames,o.Smoothness,28));Draw(cb,source,fallbackCapture,Pass.Compose);
                properties.SetVector("_Options",new Vector4(UsedHistory?1:0,o.HistoryFrames,o.Smoothness,36));Draw(cb,source,gapCapture,Pass.Compose);
                var targets=new Dictionary<string,RenderTexture> {
                    {o.CaptureTraversal?"traversal-work":"sample-direction-pdf",p.Sample},{"trace-cost",p.TraceInfo},{"first-rejection",p.TraceStop},{"trace-record",p.Record},{"trace-guide",p.Hit},
                    {"resolved",Resolved},{"filtered",unfilled},{"gap-filled",signal},{"gap-fill-coverage",gapCapture},{"coverage-stages",coverageCapture},{"halo-stages",haloCapture},
                    {"receiver-fallback",fallbackCapture},{"trace-depth",p.Depth[0]},{"raw-radiance",p.Radiance},{o.CaptureTraversal?"traversal-topology":"depth-hit",p.DepthHit}
                };
                if(p.WaterNormalsActive)targets.Add("normal-variance",p.WaterNormal);
                targets.Add("depth-gap-display",DepthGapFilled);
                string[] inputNames={"scene-source","gbuffer0","gbuffer1","gbuffer2","motion","water"};
                for(int k=0;k<replayInputs.Length;k++)targets.Add(inputNames[k],replayInputs[k]);
                if(useNrd) {
                    targets.Add("nrd-normal-roughness",Nrd.Normal);targets.Add("nrd-receiver-hit-distance-eligibility",Nrd.Receiver);
                    targets.Add("nrd-motion",Nrd.Motion);targets.Add("nrd-display",Nrd.Output);
                }
                if(o.Temporal && !useNrd) {
                    targets.Add("previous-guide",HistoryGuide);targets.Add("previous-material",HistoryMaterial);
                    targets.Add("temporal",Temporal);targets.Add("temporal-statistics",TemporalStats);
                    targets.Add("previous-measured",History);targets.Add("previous-measured-point",HistoryPoint);
                    // Frozen-input fixtures may reconstruct without tracing.
                    if(previousRecordCapture==null){EnsureCapture(ref previousRecordCapture,p.Record.width,p.Record.height);cb.CopyTexture(p.Record,previousRecordCapture);}
                    targets.Add("previous-measured-record",previousRecordCapture);targets.Add("previous-temporal-statistics",HistoryStats);
                    targets.Add("previous-measured-plane",HistoryPlane);
                    if(coverageTemporal) {targets.Add("temporal-source-anchor",TemporalAnchor);targets.Add("previous-source-anchor",HistoryAnchor);}
                }
                string metadata="Denoiser="+(useNrd?"NRD_REBLUR_SPECULAR":"Custom")+"\nDirection=Ggx\nDepthGapFill=ReceiverColourFallback\nSourceBlend=0\nRelaxReturn=True\nSkyAfterContinuation=True\nSkyRecoveryScope="+(o.GlobalContinuedSky?"AllEligible":"Water")+"\nSpatial=False\nHitAware=False\nShallowFade=0\nTraceShape=Square\nReconstruction="+o.Reconstruction+
                    "\nProjection="+MatrixText(projection)+"\nCameraToWorld="+MatrixText(camera.cameraToWorldMatrix)+
                    "\nPreviousView="+MatrixText(previousView)+"\nPreviousProjection="+MatrixText(GL.GetGPUProjectionMatrix(previousProjection,false))+"\nHistoryValid="+UsedHistory+"\nCoverageTemporal="+coverageTemporal+"\nCoverageFullSmoothness="+o.CoverageFullSmoothness+"\nCoverageEndSmoothness="+o.CoverageEndSmoothness+"\nCoverageFilter="+coverageFilter+"\nCoverageFilterStrength="+o.CoverageFilterStrength+"\nCoverageFilterFullSmoothness="+o.CoverageFilterFullSmoothness+"\nCoverageFilterEndSmoothness="+o.CoverageFilterEndSmoothness+"\nCoverageFilterSmoothStrength="+o.CoverageFilterSmoothStrength+"\nLastResetReason="+LastResetReason;
                var rasterSnapshot=p.Raster.Current;
                continuationCapture.Request(cb,targets,o,p.CameraWidth,p.CameraHeight,metadata,
                    rasterSnapshot!=null?(Func<string>)(()=>rasterSnapshot.Metadata()):null);
            } else if(!continuationCapture.Pending) {Drop(ref coverageCapture);Drop(ref haloCapture);Drop(ref fallbackCapture);Drop(ref gapCapture);Drop(ref previousRecordCapture);for(int k=0;k<replayInputs.Length;k++)Drop(ref replayInputs[k]);}
            // Store raw metadata only AFTER every reconstruction/diagnostic
            // consumer. Statistical anchors never replace raw hit authority.
            if(o.Temporal && !useNrd) {
                using(SsrGpuSample.Measure(cb,camera,"History store")) {
                    Draw(cb,source,HistoryPlane,Pass.ReceiverContext);
                    Draw(cb,source,HistoryMaterial,Pass.MaterialContext);
                }
                // Command buffers already hold the concrete allocations. Publish
                // only after all readers/readbacks and context writers are queued.
                Publish(historySignal,ref History,ref spareTemporal);
                Publish(p.Hit,ref HistoryGuide,ref spareGuide);
                Publish(p.HitPoint,ref HistoryPoint,ref sparePoint);
                Publish(TemporalStats,ref HistoryStats,ref spareStats);
                if(coverageTemporal)Publish(TemporalAnchor,ref HistoryAnchor,ref spareAnchor);
            }
            valid=o.Temporal;recordHistoryAvailable=o.Temporal;
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
        private void DrawMany(CommandBuffer cb,RenderTargetIdentifier source,Pass pass,params RenderTexture[] targets) {
            var ids=new RenderTargetIdentifier[targets.Length];for(int i=0;i<ids.Length;i++)ids[i]=targets[i];
            cb.SetGlobalTexture("_MainTex",source);cb.SetRenderTarget(ids,new RenderTargetIdentifier(BuiltinRenderTextureType.None));
            cb.DrawMesh(triangle,Matrix4x4.identity,material,0,(int)pass,properties);
        }
        private void Drop(ref RenderTexture t) { if(t!=null){var retired=t;continuationCapture.ReleaseWhenComplete(()=>{retired.Release();UnityEngine.Object.DestroyImmediate(retired);});}t=null; }
        private void Release() {
            ResetHistory();for(int i=0;i<replayInputs.Length;i++)Drop(ref replayInputs[i]);
            ReleaseTemporal();Drop(ref DepthGapFilled);Drop(ref exactResidual);
            Drop(ref coverageCapture);Drop(ref haloCapture);Drop(ref fallbackCapture);Drop(ref gapCapture);Drop(ref previousRecordCapture);
            Drop(ref Resolved);
        }
        public void Dispose() {if(disposed)return;disposed=true;continuationCapture.Dispose();Release();RetireNrd();if(NativeReconstruction!=null)continuationCapture.ReleaseWhenComplete(NativeReconstruction.Dispose);NativeReconstruction=null;ReceiverFallback.Dispose();TracePipeline.Dispose();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(triangle);}
    }
}

