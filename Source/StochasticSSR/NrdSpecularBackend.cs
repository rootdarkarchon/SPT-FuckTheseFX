using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // NRD is isolated here and in FTFXNrd.dll. Unity only
    // gathers live globals; upstream NRD executes its own DXBC on the render thread.
    internal sealed class NrdSpecularBackend : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Frame {
            internal Matrix4x4 Projection,PreviousProjection,View,PreviousView;
            internal Vector2 Jitter,PreviousJitter;
            internal uint Reset,Frames,Index,AntiFirefly;
            internal float DeltaMs;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Statistics {
            internal ulong PermanentBytes,TransientBytes,AdapterBytes,Frames,Dispatches,Failures;
            internal int Status,Pending;
            internal double QueueUs,EventUs;
        }
        [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdAbi();
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdCallback();
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdCreate(uint w,uint h);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdDestroy(IntPtr id);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdCancelOwner(IntPtr id);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdQueue(IntPtr id,IntPtr signal,IntPtr normal,IntPtr receiver,IntPtr motion,IntPtr output,ref Frame frame);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdUpdate(IntPtr token,ref Frame frame);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdCancel(IntPtr token);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdStats(IntPtr id,out Statistics stats);
        [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Ansi)] static extern void FTFX_NrdError(IntPtr id,StringBuilder text,uint size);
        internal static Shader ValidationShader {get;set;}
        internal static string ValidationLibrary {get;set;}
        private static AssetBundle bundle;
        private static IntPtr library;
        private static bool unavailable,warned;
        private readonly Material material;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly Mesh triangle;
        private readonly Action<RenderTexture> retire;
        internal RenderTexture Normal,Receiver,Motion,Output;
        private IntPtr handle,callback;
        private Camera owner;
        private uint index;
        private bool reset=true,disposed;
        private Matrix4x4 previousProjection,previousView;
        private Vector2 previousJitter;
        private readonly List<Pending> pending=new List<Pending>();
        private readonly List<IntPtr> lifetimes=new List<IntPtr>();
        private sealed class Pending {internal IntPtr Token;internal Frame Frame;internal SsrRasterState.Snapshot Snapshot;}
        internal Statistics Stats {get {FTFX_NrdStats(handle,out var result);return result;}}
        // FTFX reconstructs clip Y as 2*textureUV.y-1. NRD's D3D convention
        // projects UV.y as .5-.5*clipY/w. Flip the projection row, not the
        // textures or world normals; both backends then reconstruct one receiver.
        internal static Matrix4x4 NrdProjection(Matrix4x4 projection){projection.SetRow(1,-projection.GetRow(1));return projection;}
        internal string Error {get {var text=new StringBuilder(512);FTFX_NrdError(handle,text,512);return text.ToString();}}
        internal static NrdSpecularBackend TryCreate(Action<RenderTexture> retire) {
            if(unavailable || SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)return null;
            try {
                string root=Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if(library==IntPtr.Zero)library=LoadLibrary(ValidationLibrary??Path.Combine(root,"FTFXNrd.dll"));
                if(library==IntPtr.Zero)throw new InvalidOperationException("FTFXNrd.dll could not be loaded");
                if(FTFX_NrdAbi()!=Marshal.SizeOf(typeof(Frame)))throw new InvalidOperationException("NRD bridge ABI mismatch");
                Shader shader=ValidationShader;
                if(shader==null){if(bundle==null)bundle=AssetBundle.LoadFromFile(Path.Combine(root,"Assets/nrdssr"));if(bundle!=null)shader=bundle.LoadAsset<Shader>("Assets/NrdInputs.shader");}
                if(shader==null || !shader.isSupported)throw new InvalidOperationException("NRD input adapter unavailable");
                return new NrdSpecularBackend(shader,retire);
            }catch(Exception e){unavailable=true;Warn(e.Message);return null;}
        }
        private static void Warn(string text){if(!warned){warned=true;Plugin.MyLog.LogWarning("NRD backend unavailable; native SSR required: "+text);}}
        private NrdSpecularBackend(Shader shader,Action<RenderTexture> retire) {
            this.retire=retire;material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};callback=FTFX_NrdCallback();
            triangle=new Mesh{hideFlags=HideFlags.HideAndDontSave};triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};triangle.triangles=new[]{0,1,2};triangle.UploadMeshData(true);
        }
        private RenderTexture Target(string name,int w,int h,RenderTextureFormat format,bool uav=false) {
            var t=new RenderTexture(w,h,0,format,RenderTextureReadWrite.Linear){name="FTFX NRD "+name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,enableRandomWrite=uav,hideFlags=HideFlags.HideAndDontSave};
            if(!t.Create()){UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("NRD allocation failed");}return t;
        }
        private void Ensure(int w,int h) {
            if(Output!=null && Output.IsCreated() && Output.width==w && Output.height==h && Normal.IsCreated() && Receiver.IsCreated() && Motion.IsCreated())return;
            Release();handle=FTFX_NrdCreate((uint)w,(uint)h);if(handle==IntPtr.Zero)throw new InvalidOperationException("NRD instance creation failed");lifetimes.Add(handle);
            Normal=Target("normal roughness",w,h,RenderTextureFormat.ARGBFloat);Receiver=Target("receiver hit distance eligibility",w,h,RenderTextureFormat.ARGBFloat);
            Motion=Target("surface motion",w,h,RenderTextureFormat.RGHalf);Output=Target("premultiplied display",w,h,RenderTextureFormat.ARGBHalf,true);Reset();
        }
        internal void Reset(){reset=true;}
        internal bool Render(CommandBuffer cb,Camera camera,SsrPipeline p,RenderTexture signal,Options o) {
            try {
                Ensure(signal.width,signal.height);
                if(Stats.Status<0){Warn("native dispatch failed: "+Error);return false;}
                if(owner!=camera){if(owner!=null)Camera.onPreRender-=FinalizeRaster;owner=camera;Camera.onPreRender+=FinalizeRaster;reset=true;}
                properties.Clear();properties.SetVector("_Size",new Vector4(p.CameraWidth,p.CameraHeight,signal.width,signal.height));
                properties.SetVector("_Clip",new Vector4(camera.nearClipPlane,camera.farClipPlane,0,0));
                properties.SetVector("_Eligibility",new Vector4(o.Smoothness,0,0,o.ZeroSmoothnessProbe?1:0));
                properties.SetMatrix("_InvProjection",GL.GetGPUProjectionMatrix(camera.projectionMatrix,false).inverse);
                properties.SetBuffer("_SsrRasterState",p.Raster.Buffer);properties.SetFloat("_SsrLateRaster",p.Raster.Enabled?1:0);
                properties.SetTexture("_Guide",p.Hit);properties.SetTexture("_Point",p.HitPoint);properties.SetTexture("_Record",p.Record);
                var water=WaterReflectionCapture.Get(camera,p.CameraWidth,p.CameraHeight);properties.SetTexture("_WaterCoverage",water!=null?(Texture)water:Texture2D.blackTexture);properties.SetFloat("_WaterAvailable",water!=null?1:0);
                cb.SetRenderTarget(new[]{new RenderTargetIdentifier(Normal),new RenderTargetIdentifier(Receiver),new RenderTargetIdentifier(Motion)},new RenderTargetIdentifier(BuiltinRenderTextureType.None));
                cb.DrawMesh(triangle,Matrix4x4.identity,material,0,0,properties);
                var current=GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix,false);var view=camera.worldToCameraMatrix;
                var jitter=SsrRasterState.JitterUV(GL.GetGPUProjectionMatrix(camera.projectionMatrix,false),current);
                var f=new Frame{Projection=NrdProjection(current),PreviousProjection=NrdProjection(reset?current:previousProjection),View=view,PreviousView=reset?view:previousView,
                    Jitter=jitter,PreviousJitter=reset?jitter:previousJitter,Reset=reset?1u:0,Frames=(uint)(o.Temporal?Mathf.Max(1,o.HistoryFrames):0),Index=index++,AntiFirefly=o.NrdAntiFirefly?1u:0,
                    DeltaMs=Time.unscaledDeltaTime>0?Mathf.Clamp(Time.unscaledDeltaTime*1000,1,1000):16.666667f};
                var token=FTFX_NrdQueue(handle,signal.GetNativeTexturePtr(),Normal.GetNativeTexturePtr(),Receiver.GetNativeTexturePtr(),Motion.GetNativeTexturePtr(),Output.GetNativeTexturePtr(),ref f);
                if(token==IntPtr.Zero){Warn("native frame preparation failed");return false;}
                cb.IssuePluginEventAndData(callback,1,token);
                if(p.Raster.Enabled && p.Raster.Current!=null && !p.Raster.Current.Ready)pending.Add(new Pending{Token=token,Frame=f,Snapshot=p.Raster.Current});
                else {previousProjection=current;previousView=view;previousJitter=jitter;}
                reset=false;return true;
            }catch(Exception e){Warn(e.Message);return false;}
        }
        private void FinalizeRaster(Camera camera) {
            if(camera!=owner)return;
            foreach(var item in pending) {
                var s=item.Snapshot;if(s==null || !s.Ready){FTFX_NrdCancel(item.Token);reset=true;continue;}
                var f=item.Frame;f.Projection=NrdProjection(s.Stable);f.View=s.View;f.Jitter=s.Jitter;
                f.PreviousProjection=NrdProjection(f.Reset!=0?s.Stable:previousProjection);f.PreviousView=f.Reset!=0?s.View:previousView;f.PreviousJitter=f.Reset!=0?s.Jitter:previousJitter;
                FTFX_NrdUpdate(item.Token,ref f);previousProjection=s.Stable;previousView=s.View;previousJitter=s.Jitter;
            }
            pending.Clear();
        }
        private void Drop(ref RenderTexture target){if(target!=null){retire(target);target=null;}}
        private void Release() {
            // Concrete packets keep native views and backend state alive. Cancel
            // only frames that never reached late raster finalization.
            foreach(var p in pending)FTFX_NrdCancel(p.Token);pending.Clear();
            if(handle!=IntPtr.Zero){FTFX_NrdDestroy(handle);handle=IntPtr.Zero;}Drop(ref Normal);Drop(ref Receiver);Drop(ref Motion);Drop(ref Output);reset=true;index=0;
        }
        internal int DiscardQueuedFrames(){int count=0;foreach(var id in lifetimes)count+=FTFX_NrdCancelOwner(id);return count;}
        public void Dispose(){if(disposed)return;disposed=true;if(owner!=null)Camera.onPreRender-=FinalizeRaster;owner=null;DiscardQueuedFrames();lifetimes.Clear();Release();UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(triangle);}
    }
}
