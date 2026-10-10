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
        internal static Func<Camera,bool> IsOpticCamera;
        private static Material opticMaterial;
        private static Mesh opticTriangle;
        private static readonly HashSet<Camera> failed=new HashSet<Camera>();
        private static readonly List<WeakReference> recorded=new List<WeakReference>();
        private static bool subscribed;
        internal static void ConfigureOpticCapture(Shader shader)
        {
            if(shader==null || !shader.isSupported || shader.passCount<3)throw new InvalidOperationException("Optic surface capture shader unavailable");
            if(opticMaterial!=null)UnityEngine.Object.DestroyImmediate(opticMaterial);
            opticMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            if(opticTriangle==null) {
                opticTriangle=new Mesh{name="FTFX optic pre-ambient surface",hideFlags=HideFlags.HideAndDontSave};
                opticTriangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
                opticTriangle.triangles=new[]{0,1,2};opticTriangle.UploadMeshData(true);
            }
        }
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
        private static bool Supported(Camera c)=>Enabled && c!=null && !failed.Contains(c) &&
            (IsMainCamera?.Invoke(c)==true || IsOpticCamera?.Invoke(c)==true) &&
            c.allowHDR && !c.stereoEnabled && !c.orthographic && c.actualRenderingPath==RenderingPath.DeferredShading &&
            SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11 && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        internal static RenderTexture Get(Camera c,int w,int h)
        {
            if(!Supported(c) || c.pixelWidth!=w || c.pixelHeight!=h)return null;
            try{return Prepare(c,w,h);}catch(Exception error){Fail(c,error);return null;}
        }
        private static RenderTexture Prepare(Camera c,int w,int h)
        {
            if(!subscribed){Camera.onPreCull+=BeforeCamera;subscribed=true;}
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
            }
            // Rebuild even without resize: an excluded render may have emptied
            // this buffer while native ambient commands retain the texture.
            e.Clear.Clear();e.Clear.SetRenderTarget(e.Texture);e.Clear.ClearRenderTarget(false,true,Color.clear);
            e.Clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            return e.Texture;
        }
        private static void BeforeCamera(Camera c) {
            if(!entries.TryGetValue(c,out var entry))return;
            if(!Supported(c)){entry.Clear.Clear();return;}
            try{Prepare(c,c.pixelWidth,c.pixelHeight);}catch(Exception error){Fail(c,error);}
        }
        private static void Remember(CommandBuffer cb) {
            if(!recorded.Any(w=>ReferenceEquals(w.Target,cb)))recorded.Add(new WeakReference(cb));
        }
        internal static bool Begin(CommandBuffer cb,Camera c,bool producerAvailable)
        {
            cb.SetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled",0);
            if(!Supported(c))return false;
            try {
                if(IsOpticCamera?.Invoke(c)==true) {
                    if(opticMaterial==null || opticTriangle==null)return false;
                    var surface=Get(c,c.pixelWidth,c.pixelHeight);
                    if(surface==null)return false;
                    Remember(cb);
                    // Called at the native ambient draw boundary, after native
                    // decal/water writes. Capture material RGB, never scene light.
                    // Native draws use CameraTarget as both HDR colour and depth.
                    cb.BeginSample("FTFX SSR/Optic pre-ambient surface colour");
                    try{cb.SetRenderTarget(surface);cb.DrawMesh(opticTriangle,Matrix4x4.identity,opticMaterial,0,2);}
                    finally{
                        cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget,BuiltinRenderTextureType.CameraTarget);
                        cb.EndSample("FTFX SSR/Optic pre-ambient surface colour");
                    }
                    return false; // No UAV binding for the native optic shader.
                }
                if(!producerAvailable)return false;
                var t=Get(c,c.pixelWidth,c.pixelHeight);
                if(t==null)return false;
                Remember(cb);
                cb.SetGlobalVector("_FTFX_CA_SurfaceCaptureSize",new Vector4(t.width,t.height,0,0));
                cb.SetRandomWriteTarget(5,t);cb.SetGlobalFloat("_FTFX_CA_SurfaceCaptureEnabled",1);return true;
            }catch(Exception e){
                Fail(c,e);
                return false;
            }
        }
        private static void Fail(Camera c,Exception error) {
            if(c==null || !failed.Add(c))return;
            if(entries.TryGetValue(c,out var entry))entry.Clear.Clear();
            Debug.LogWarning("FTFX surface colour capture unavailable for "+c.name+": "+error.Message);
        }
        internal static void Prune() {
            // Native ambient buffers can retain recorded references across ADS
            // and setting changes. Keep live texture identities until teardown.
            foreach(var c in entries.Keys.Where(c=>c==null).ToArray()){entries[c].Dispose();entries.Remove(c);}
            failed.RemoveWhere(c=>c==null);
            recorded.RemoveAll(w=>!w.IsAlive);
        }
        internal static void Dispose(){
            if(subscribed){Camera.onPreCull-=BeforeCamera;subscribed=false;}
            // Native pre-cull rebuilds these buffers. Detach references before
            // destroying captures/materials, matching water capture teardown.
            foreach(var w in recorded)if(w.Target is CommandBuffer cb)try{cb.Clear();}catch{}
            recorded.Clear();
            foreach(var e in entries.Values)e.Dispose();entries.Clear();failed.Clear();Requested=null;IsMainCamera=null;IsOpticCamera=null;
            if(opticMaterial!=null)UnityEngine.Object.DestroyImmediate(opticMaterial);opticMaterial=null;
            if(opticTriangle!=null)UnityEngine.Object.DestroyImmediate(opticTriangle);opticTriangle=null;
        }
    }
}
