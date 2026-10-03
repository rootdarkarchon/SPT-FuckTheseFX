using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Native's forced-miss resolve/filter path, with private per-camera history.
    // No native tracing, probe fallback, or FTFX hit data enters this buffer.
    internal sealed class NativeReceiverFallback : IDisposable
    {
        private readonly PropertySheetFactory factory=new PropertySheetFactory();
        private readonly PropertySheet sheet;
        private readonly ComputeShader gaussian;
        private readonly int kernel;
        private RenderTexture misses,current,history;
        internal Action<RenderTexture> RetireTexture;
        internal RenderTexture Radiance {get;private set;}
        internal int MipCount {get;private set;}
        private readonly List<RenderTexture> mips=new List<RenderTexture>();
        private bool valid;
        internal NativeReceiverFallback(Shader native,ComputeShader downsample)
        {
            if(native==null || !native.isSupported || downsample==null)throw new InvalidOperationException("Native receiver fallback shaders unavailable");
            sheet=factory.Get(native);gaussian=downsample;kernel=gaussian.FindKernel("KMain");
        }
        private static bool Ready(RenderTexture t)=>t!=null && t.IsCreated();
        private static RenderTexture Target(string name,int width,int height,FilterMode filter,bool mip=false,bool uav=false)
        {
            var t=new RenderTexture(width,height,0,RuntimeUtilities.defaultHDRRenderTextureFormat) {
                name="FTFX receiver fallback "+name,filterMode=filter,wrapMode=TextureWrapMode.Clamp,
                useMipMap=mip,autoGenerateMips=false,enableRandomWrite=uav,hideFlags=HideFlags.HideAndDontSave};
            if(t.Create())return t;
            UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("Receiver fallback allocation failed: "+name);
        }
        internal void Ensure(int size)=>Ensure(size,size);
        internal void Ensure(int width,int height)
        {
            if(Ready(Radiance) && Radiance.width==width && Radiance.height==height && Radiance.enableRandomWrite && Ready(misses) && mips.TrueForAll(Ready))return;
            Release();
            misses=Target("zero hit records",width,height,FilterMode.Point);
            Radiance=Target("filtered radiance",width,height,FilterMode.Trilinear,true,true);
            MipCount=Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Math.Min(width,height),2)-3),0,12);
            for(int i=0;i<MipCount;i++)mips.Add(Target("mip "+i,Math.Max(1,width>>(i+1)),Math.Max(1,height>>(i+1)),FilterMode.Bilinear,false,true));
        }
        internal void ResetHistory()=>valid=false;
        internal void Render(CommandBuffer cb,RenderTargetIdentifier source,float cutoff,bool temporal)
        {
            sheet.properties.Clear();sheet.properties.SetFloat("_CutOffThreshold",cutoff);
            sheet.properties.SetTexture("_Test",misses);
            cb.SetRenderTarget(misses);cb.ClearRenderTarget(false,true,Color.clear);
            if(temporal) {
                if(!Ready(current)){Drop(current);current=Target("current receiver colour",Radiance.width,Radiance.height,FilterMode.Bilinear);valid=false;}
                if(!Ready(history)){Drop(history);history=Target("history",Radiance.width,Radiance.height,FilterMode.Bilinear);valid=false;}
                RuntimeUtilities.BlitFullscreenTriangle(cb,source,current,sheet,1);
                // Matches native reset: initialize history from the original scene.
                if(!valid)RuntimeUtilities.BlitFullscreenTriangle(cb,source,history);
                sheet.properties.SetTexture("_History",history);
                RuntimeUtilities.BlitFullscreenTriangle(cb,current,Radiance,sheet,2);
                cb.CopyTexture(Radiance,0,0,history,0,0);
            } else {
                Drop(current);Drop(history);current=history=null;
                RuntimeUtilities.BlitFullscreenTriangle(cb,source,Radiance,sheet,1);
            }
            valid=temporal;
            RenderTargetIdentifier input=Radiance;
            for(int i=0;i<mips.Count;i++) {
                var target=mips[i];int w=target.width,h=target.height;
                // The last level reads a separate intermediate, so its SRV
                // cannot overlap the destination mip UAV. Earlier levels keep
                // their native source chain; no same-resource SRV/UAV alias.
                bool direct=i==mips.Count-1 && i>0;
                cb.SetComputeTextureParam(gaussian,kernel,"_Source",input);
                if(direct)cb.SetComputeTextureParam(gaussian,kernel,"_Result",Radiance,i+1);
                else cb.SetComputeTextureParam(gaussian,kernel,"_Result",target);
                cb.SetComputeVectorParam(gaussian,"_Size",new Vector4(w,h,1f/w,1f/h));
                // 768/1536 end in 12-pixel mips: dispatch the partial tile too.
                cb.DispatchCompute(gaussian,kernel,(w+7)/8,(h+7)/8,1);
                if(!direct)cb.CopyTexture(target,0,0,Radiance,0,i+1);input=target;
            }
        }
        private void Drop(RenderTexture t) {if(t!=null){if(RetireTexture!=null)RetireTexture(t);else {t.Release();UnityEngine.Object.DestroyImmediate(t);}}}
        private void Release() {valid=false;Drop(misses);Drop(current);Drop(history);Drop(Radiance);misses=current=history=null;Radiance=null;foreach(var mip in mips)Drop(mip);mips.Clear();}
        public void Dispose(){Release();factory.Release();}
    }
}
