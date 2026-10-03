using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Captures CustomAmbient's sampled GBuffer0 RGB, not the lit camera target.
    internal static class AmbientSurfaceCapture
    {
        internal static Func<bool> Requested;
        internal static Func<Camera,bool> IsMainCamera;
        internal static bool Enabled=>Requested?.Invoke()==true;
        private sealed class Entry : IDisposable
        {
            internal Camera Camera;internal RenderTexture Texture;internal CommandBuffer Clear;
            public void Dispose() {
                if(Camera!=null)Camera.RemoveCommandBuffer(CameraEvent.BeforeLighting,Clear);
                Clear?.Dispose();if(Texture!=null){Texture.Release();UnityEngine.Object.DestroyImmediate(Texture);}
            }
        }
        private static readonly Dictionary<Camera,Entry> entries=new Dictionary<Camera,Entry>();
        private static bool Supported(Camera c)=>Enabled && c!=null && IsMainCamera?.Invoke(c)==true &&
            c.allowHDR && !c.stereoEnabled && !c.orthographic && c.actualRenderingPath==RenderingPath.DeferredShading &&
            SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11 && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        internal static RenderTexture Get(Camera c,int w,int h)
        {
            if(!Supported(c) || c.pixelWidth!=w || c.pixelHeight!=h)return null;
            if(!entries.TryGetValue(c,out var e)) {
                e=new Entry{Camera=c};
                try {
                    e.Texture=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name="FTFX pre-ambient surface colour",enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
                    e.Clear=new CommandBuffer{name="FTFX pre-ambient surface clear"};
                    c.AddCommandBuffer(CameraEvent.BeforeLighting,e.Clear);entries.Add(c,e);
                }catch{e.Dispose();throw;}
            }
            if(!e.Texture.IsCreated() || e.Texture.width!=w || e.Texture.height!=h) {
                // Retain object identity for commands recorded before the producer.
                e.Texture.Release();e.Texture.width=w;e.Texture.height=h;
                if(!e.Texture.Create())throw new InvalidOperationException("Surface colour capture allocation failed");
                var previous=RenderTexture.active;
                try{RenderTexture.active=e.Texture;GL.Clear(false,true,Color.clear);}finally{RenderTexture.active=previous;}
                e.Clear.Clear();e.Clear.SetRenderTarget(e.Texture);e.Clear.ClearRenderTarget(false,true,Color.clear);
                e.Clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
            return e.Texture;
        }
        internal static bool Begin(CommandBuffer cb,Camera c,bool producerAvailable)
        {
            cb.SetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled",0);
            if(!producerAvailable || !Supported(c))return false;
            try {
                var t=Get(c,c.pixelWidth,c.pixelHeight);
                cb.SetGlobalVector("_FTFX_CA_SurfaceCaptureSize",new Vector4(t.width,t.height,0,0));
                cb.SetRandomWriteTarget(5,t);cb.SetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled",1);return true;
            }catch(Exception e){Debug.LogWarning("FTFX surface colour capture unavailable: "+e.Message);return false;}
        }
        internal static void Prune() {
            foreach(var c in entries.Keys.Where(c=>!Enabled || c==null || IsMainCamera?.Invoke(c)!=true).ToArray()){entries[c].Dispose();entries.Remove(c);}
        }
        internal static void Dispose(){foreach(var e in entries.Values)e.Dispose();entries.Clear();Requested=null;IsMainCamera=null;}
    }
}
