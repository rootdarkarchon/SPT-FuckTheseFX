using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Diagnostic-only UAV capture, written by the existing CustomAmbient draw.
    // The before-lighting clear executes on every camera render, not only when
    // EFT rebuilds its ambient command buffer. Alpha counts producer writes.
    internal static class AmbientReflectionCapture
    {
        internal static Func<bool> Requested;
        internal static Func<Camera,bool> IsMainCamera;
        private sealed class Entry : IDisposable
        {
            internal Camera Camera;
            internal RenderTexture Texture;
            internal CommandBuffer Clear;
            public void Dispose() {
                if(Camera!=null)Camera.RemoveCommandBuffer(CameraEvent.BeforeLighting,Clear);
                Clear?.Dispose();
                if(Texture!=null){Texture.Release();UnityEngine.Object.DestroyImmediate(Texture);}
            }
        }
        private static readonly Dictionary<Camera,Entry> entries=new Dictionary<Camera,Entry>();
        internal static bool Enabled=>Requested?.Invoke()==true;
        internal static bool Begin(CommandBuffer cb,Camera camera,bool producerAvailable)
        {
            cb.SetGlobalFloat("_FTFX_CA_ReflectionCaptureEnabled",0);
            bool active=Enabled && producerAvailable && camera!=null && IsMainCamera?.Invoke(camera)==true &&
                camera.allowHDR && !camera.stereoEnabled && !camera.orthographic && camera.actualRenderingPath==RenderingPath.DeferredShading &&
                SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11 &&
                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBFloat);
            if(!active){Release(camera);return false;}
            int w=camera.pixelWidth,h=camera.pixelHeight;
            if(entries.TryGetValue(camera,out var entry) && (entry.Texture.width!=w || entry.Texture.height!=h || !entry.Texture.IsCreated())) {
                Release(camera);entry=null;
            }
            if(entry==null) {
                entry=new Entry {Camera=camera};
                try {
                    entry.Texture=new RenderTexture(w,h,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear) {
                        name="FTFX exact ambient reflection (diagnostic)",enableRandomWrite=true,filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};
                    if(!entry.Texture.Create())throw new InvalidOperationException("Ambient reflection capture allocation failed");
                    // Get() can be called before this camera's first lighting
                    // event. Never expose uninitialized write-count/data then.
                    var previous=RenderTexture.active;
                    try {RenderTexture.active=entry.Texture;GL.Clear(false,true,Color.clear);}
                    finally {RenderTexture.active=previous;}
                    entry.Clear=new CommandBuffer {name="FTFX ambient reflection capture clear"};
                    entry.Clear.SetRenderTarget(entry.Texture);entry.Clear.ClearRenderTarget(false,true,Color.clear);
                    entry.Clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                    camera.AddCommandBuffer(CameraEvent.BeforeLighting,entry.Clear);entries.Add(camera,entry);
                } catch(Exception error) {
                    entry.Dispose();Debug.LogWarning("FTFX ambient reflection diagnostic unavailable: "+error.Message);return false;
                }
            }
            cb.SetGlobalFloat("_FTFX_CA_ReflectionCaptureEnabled",1);
            cb.SetGlobalVector("_FTFX_CA_ReflectionCaptureSize",new Vector4(w,h,0,0));
            cb.SetRandomWriteTarget(4,entry.Texture);
            return true;
        }
        internal static RenderTexture Get(Camera camera,int width,int height) {
            return Enabled && camera!=null && IsMainCamera?.Invoke(camera)==true && entries.TryGetValue(camera,out var e) &&
                e.Texture.IsCreated() && e.Texture.width==width && e.Texture.height==height?e.Texture:null;
        }
        private static void Release(Camera camera) {
            if(ReferenceEquals(camera,null) || !entries.TryGetValue(camera,out var e))return;
            entries.Remove(camera);e.Dispose();
        }
        // EFT rebuilds InitAmbientBuffer in LateUpdate, before the next camera render.
        // Remove detached cameras and disabled diagnostics even if the AO producer stopped.
        internal static void Prune() {
            if(entries.Count==0)return;
            var dead=new List<Camera>();
            foreach(var pair in entries)if(!Enabled || pair.Key==null || IsMainCamera?.Invoke(pair.Key)!=true)dead.Add(pair.Key);
            foreach(var camera in dead)Release(camera);
        }
        internal static void Dispose() {foreach(var e in entries.Values)e.Dispose();entries.Clear();Requested=null;IsMainCamera=null;}
    }
}
