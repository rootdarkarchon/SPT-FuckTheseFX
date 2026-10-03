using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Installed Tarkov Gaussian mip reconstruction, applied after Custom
    // temporal accumulation. RGB is radiance times measured coverage.
    // These display mips never become measured temporal history.
    internal sealed class NativeReflectionReconstruction : IDisposable
    {
        private readonly ComputeShader gaussian;
        private readonly int kernel;
        private readonly List<RenderTexture> mips=new List<RenderTexture>();
        internal RenderTexture Signal {get;private set;}
        internal int MipCount {get;private set;}
        internal NativeReflectionReconstruction(ComputeShader gaussian)
        {
            this.gaussian=gaussian??throw new InvalidOperationException("Native SSR Gaussian shader unavailable");
            kernel=gaussian.FindKernel("KMain");
        }
        private static bool Ready(RenderTexture t)=>t!=null && t.IsCreated();
        private static RenderTexture Target(int width,int height,bool mip,bool uav)
        {
            var t=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear) {
                name="FTFX native reflection reconstruction "+width+"x"+height,
                filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,
                useMipMap=mip,autoGenerateMips=false,enableRandomWrite=uav,hideFlags=HideFlags.HideAndDontSave};
            if(t.Create())return t;
            UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("Native SSR reconstruction allocation failed");
        }
        internal void Ensure(int width,int height)
        {
            if(Ready(Signal) && Signal.width==width && Signal.height==height && mips.TrueForAll(Ready))return;
            Dispose();
            Signal=Target(width,height,true,false);
            MipCount=Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Math.Min(width,height),2)-3),0,12);
            for(int i=0;i<MipCount;i++)mips.Add(Target(Math.Max(1,width>>(i+1)),Math.Max(1,height>>(i+1)),false,true));
        }
        internal RenderTexture Render(CommandBuffer cb,RenderTexture source)
        {
            cb.CopyTexture(source,0,0,Signal,0,0);
            RenderTargetIdentifier input=Signal;
            for(int i=0;i<mips.Count;i++) {
                var target=mips[i];int w=target.width,h=target.height;
                cb.SetComputeTextureParam(gaussian,kernel,"_Source",input);
                cb.SetComputeTextureParam(gaussian,kernel,"_Result",target);
                cb.SetComputeVectorParam(gaussian,"_Size",new Vector4(w,h,1f/w,1f/h));
                cb.DispatchCompute(gaussian,kernel,(w+7)/8,(h+7)/8,1);
                cb.CopyTexture(target,0,0,Signal,0,i+1);input=target;
            }
            return Signal;
        }
        private static void Drop(RenderTexture t) {if(t!=null){t.Release();UnityEngine.Object.DestroyImmediate(t);}}
        public void Dispose() {Drop(Signal);Signal=null;MipCount=0;foreach(var mip in mips)Drop(mip);mips.Clear();}
    }
}
