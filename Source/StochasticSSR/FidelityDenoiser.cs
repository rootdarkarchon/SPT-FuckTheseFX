using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Resource/lifecycle adapter. The three filtering stages are AMD's pinned
    // source, not the former custom Temporal/Spatial kernels.
    internal sealed class FidelityDenoiser : IDisposable
    {
        private readonly ComputeShader shader;
        private readonly int reproject,prefilter,accumulate;
        private RenderTexture reprojected,average,variance,samples,prefiltered,prefilterVariance,outputVariance;
        private RenderTexture historyVariance,historySamples,tiles;
        internal bool AdaptiveReady => Array.TrueForAll(Targets(),t=>t!=null && t.IsCreated());
        internal RenderTexture AdaptiveVariance => historyVariance;
        internal RenderTexture AdaptiveSamples => historySamples;
        internal FidelityDenoiser(ComputeShader source)
        {
            shader=UnityEngine.Object.Instantiate(source);
            reproject=shader.FindKernel("Reproject");prefilter=shader.FindKernel("Prefilter");accumulate=shader.FindKernel("Accumulate");
        }
        private static RenderTexture Create(int w,int h,bool scalar=false)
        {
            // Variance and sample count were wasting three half-float channels.
            var format=scalar?(SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf)?RenderTextureFormat.RHalf:RenderTextureFormat.RFloat):RenderTextureFormat.ARGBHalf;
            var t=new RenderTexture(w,h,0,format,RenderTextureReadWrite.Linear)
            { enableRandomWrite=true,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave,name="FTFX AMD reflections" };
            if(!t.Create())throw new InvalidOperationException("AMD reflections allocation failed");return t;
        }
        internal bool Ensure(int w,int h)
        {
            if(reprojected!=null && reprojected.width==w && reprojected.height==h &&
                Array.TrueForAll(Targets(),t=>t!=null && t.IsCreated()))return false;
            Release();
            reprojected=Create(w,h);average=Create((w+7)/8,(h+7)/8);variance=Create(w,h,true);samples=Create(w,h,true);
            prefiltered=Create(w,h);prefilterVariance=Create(w,h,true);outputVariance=Create(w,h,true);
            historyVariance=Create(w,h,true);historySamples=Create(w,h,true);tiles=Create((w+7)/8,(h+7)/8,true);return true;
        }
        internal bool Run(CommandBuffer cb,Camera camera,SsrPipeline p,Options o,Matrix4x4 previousView,Matrix4x4 previousProjection,bool valid)
        {
            int w=p.Radiance.width,h=p.Radiance.height;
            valid &= !Ensure(w,h);
            cb.SetComputeVectorParam(shader,"_Dimensions",new Vector4(w,h,p.CameraWidth,p.CameraHeight));
            cb.SetComputeVectorParam(shader,"_Settings",new Vector4(valid?1:0,Mathf.Clamp(o.HistoryFrames,1,16),o.Smoothness,o.Temporal?1:0));
            cb.SetComputeIntParam(shader,"_InclusiveCutoff",o.CustomReconstruction?1:0);
            cb.SetComputeIntParam(shader,"_EfficientDenoising",o.EfficientDenoising?1:0);
            cb.SetComputeMatrixParam(shader,"_InvProjection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,false).inverse);
            cb.SetComputeMatrixParam(shader,"_ViewToWorld",camera.cameraToWorldMatrix);
            cb.SetComputeMatrixParam(shader,"_WorldToPreviousClip",GL.GetGPUProjectionMatrix(previousProjection,false)*previousView);
            // Clear history on first use/reset: upstream may sample it even when
            // reprojection is rejected, so uninitialized GPU memory is not allowed.
            if(!valid)foreach(var t in new[]{p.History,p.HistoryGuide,p.HistoryPoint,p.HistorySurface,historyVariance,historySamples})
            { cb.SetRenderTarget(t);cb.ClearRenderTarget(false,true,Color.clear); }
            foreach(int k in new[]{reproject,prefilter,accumulate})
            {
                cb.SetComputeTextureParam(shader,k,"_Tiles",tiles);
                cb.SetComputeTextureParam(shader,k,"_Guide",p.Hit);cb.SetComputeTextureParam(shader,k,"_Point",p.HitPoint);
                cb.SetComputeTextureParam(shader,k,"_Input",k==accumulate && o.Spatial?prefiltered:p.DenoiseInput);
            }
            cb.SetComputeMatrixParam(shader,"_PreviousToCurrentView",camera.worldToCameraMatrix*previousView.inverse);
            cb.SetComputeMatrixParam(shader,"_Projection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,false));
            cb.SetComputeTextureParam(shader,reproject,"_CurrentDepth",p.Depth[0]);
            cb.SetComputeTextureParam(shader,reproject,"_Motion",p.Motion);
            cb.SetComputeTextureParam(shader,reproject,"_History",p.History);
            cb.SetComputeTextureParam(shader,reproject,"_HistoryGuide",p.HistoryGuide);
            cb.SetComputeTextureParam(shader,reproject,"_Surface",p.Surface);cb.SetComputeTextureParam(shader,reproject,"_HistorySurface",p.HistorySurface);
            cb.SetComputeTextureParam(shader,reproject,"_HistoryPoint",p.HistoryPoint);
            cb.SetComputeTextureParam(shader,reproject,"_HistorySamples",historySamples);
            cb.SetComputeTextureParam(shader,reproject,"_HistoryVariance",historyVariance);
            cb.SetComputeTextureParam(shader,reproject,"_Output",reprojected);
            cb.SetComputeTextureParam(shader,reproject,"_OutputVariance",variance);
            cb.SetComputeTextureParam(shader,reproject,"_OutputSamples",samples);
            cb.SetComputeTextureParam(shader,reproject,"_OutputAverage",average);
            cb.SetComputeTextureParam(shader,reproject,"_OutputTiles",tiles);
            cb.SetComputeTextureParam(shader,reproject,"_Weight",p.HistoryWeight);
            cb.SetComputeTextureParam(shader,reproject,"_Input",p.DenoiseInput);
            using(SsrGpuSample.Measure(cb,camera,"DNSR reproject"))
                cb.DispatchCompute(shader,reproject,(w+7)/8,(h+7)/8,1);
            if(o.Spatial)
            {
                cb.SetComputeTextureParam(shader,prefilter,"_Variance",variance);cb.SetComputeTextureParam(shader,prefilter,"_Average",average);
                cb.SetComputeTextureParam(shader,prefilter,"_Output",prefiltered);cb.SetComputeTextureParam(shader,prefilter,"_OutputVariance",prefilterVariance);
                cb.SetComputeTextureParam(shader,prefilter,"_Input",p.DenoiseInput);
                using(SsrGpuSample.Measure(cb,camera,"DNSR prefilter"))
                    cb.DispatchCompute(shader,prefilter,(w+7)/8,(h+7)/8,1);
            }
            if(o.Temporal)
            {
                cb.SetComputeTextureParam(shader,accumulate,"_Variance",o.Spatial?prefilterVariance:variance);
                cb.SetComputeTextureParam(shader,accumulate,"_Samples",samples);cb.SetComputeTextureParam(shader,accumulate,"_Average",average);
                cb.SetComputeTextureParam(shader,accumulate,"_Reprojected",reprojected);
                cb.SetComputeTextureParam(shader,accumulate,"_Output",p.TemporalResult);cb.SetComputeTextureParam(shader,accumulate,"_OutputVariance",outputVariance);
                cb.SetComputeTextureParam(shader,accumulate,"_Input",o.Spatial?prefiltered:p.DenoiseInput);
                using(SsrGpuSample.Measure(cb,camera,"DNSR temporal resolve"))
                    cb.DispatchCompute(shader,accumulate,(w+7)/8,(h+7)/8,1);
            }
            else cb.CopyTexture(o.Spatial?prefiltered:p.DenoiseInput,p.TemporalResult);
            // Native reconstruction immediately writes Filtered in FinishNativeDenoise.
            if(!o.CustomReconstruction)cb.CopyTexture(p.TemporalResult,p.Filtered);
            if(o.EfficientDenoising)p.RetainHistory();
            else
            {
                cb.CopyTexture(p.Surface,p.HistorySurface);
                cb.CopyTexture(p.TemporalResult,p.History);cb.CopyTexture(p.Hit,p.HistoryGuide);cb.CopyTexture(p.HitPoint,p.HistoryPoint);
            }
            // Bindings are recorded into the command buffer before swapping. Next
            // frame reads the completed output directly; no scalar history copies.
            if(o.Temporal)Swap(ref outputVariance,ref historyVariance);
            else Swap(ref variance,ref historyVariance);
            Swap(ref samples,ref historySamples);
            return valid;
        }
        private static void Swap(ref RenderTexture a,ref RenderTexture b) { var t=a;a=b;b=t; }
        private RenderTexture[] Targets()=>new[]{reprojected,average,variance,samples,prefiltered,prefilterVariance,outputVariance,historyVariance,historySamples,tiles};
        internal void Release()
        {
            foreach(var t in Targets())if(t!=null) { t.Release();UnityEngine.Object.DestroyImmediate(t); }
            reprojected=average=variance=samples=prefiltered=prefilterVariance=outputVariance=historyVariance=historySamples=tiles=null;
        }
        public void Dispose() { Release();UnityEngine.Object.DestroyImmediate(shader); }
    }
}
