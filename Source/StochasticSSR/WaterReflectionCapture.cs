using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Observe issued native draws; never replace the water's shading material.
    internal sealed class WaterReflectionCapture : IDisposable
    {
        internal static WaterReflectionCapture Active;
        internal Func<bool> Requested;
        internal Func<Camera,bool> IsMainCamera;
        private readonly Material material;
        private readonly Mesh triangle;
        private readonly Harmony harmony=new Harmony("com.rootdarkarchon.fuckthesefx.ssr.watercapture");
        private readonly Dictionary<Camera,Entry> entries=new Dictionary<Camera,Entry>();
        private readonly ConditionalWeakTable<CommandBuffer,Binding> bindings=new ConditionalWeakTable<CommandBuffer,Binding>();
        private readonly List<WeakReference> recorded=new List<WeakReference>();
        private bool failed;
        private sealed class Binding { internal RenderTargetIdentifier[] Targets;internal RenderTargetIdentifier Depth; }
        private sealed class Entry : IDisposable
        {
            internal Camera Camera;
            internal RenderTexture Bottom,Mask;
            internal CommandBuffer Capture;
            public void Dispose() {
                if(Camera!=null)Camera.RemoveCommandBuffer(CameraEvent.AfterGBuffer,Capture);
                Capture?.Dispose();
                foreach(var t in new[]{Bottom,Mask})if(t!=null){t.Release();UnityEngine.Object.DestroyImmediate(t);}
            }
        }
        internal WaterReflectionCapture(Shader shader,bool installHooks=true)
        {
            if(shader==null || !shader.isSupported)throw new InvalidOperationException("Water capture shader unavailable");
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            triangle=new Mesh{name="FTFX water depth copy",hideFlags=HideFlags.HideAndDontSave};
            triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
            triangle.triangles=new[]{0,1,2};triangle.UploadMeshData(true);
            try {
                if(installHooks) {
                    var v3=AccessTools.TypeByName("WaterSSR.WaterRendererv3");
                    var v2=AccessTools.TypeByName("WaterForSSRv2");
                    if(v3==null || v2==null || v3.Module.ModuleVersionId!=new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7"))
                        throw new NotSupportedException("Unsupported water capture ABI");
                    foreach(var m in new[]{AccessTools.Method(v3,"method_2",new[]{typeof(Camera)}),
                        AccessTools.Method(v2,"RenderBuffer",new[]{typeof(CommandBuffer),typeof(Camera)})}) {
                        if(m==null)throw new MissingMethodException("Native water draw missing");
                        harmony.Patch(m,transpiler:new HarmonyMethod(typeof(WaterReflectionCapture),nameof(ObserveDraws)){priority=Priority.Last});
                    }
                }
                Active=this;Camera.onPreCull+=BeforeCamera;
            } catch {Dispose();throw;}
        }
        internal static IEnumerable<CodeInstruction> ObserveDraws(IEnumerable<CodeInstruction> input,MethodBase __originalMethod)
        {
            var code=input.ToList();int targets=0,draws=0;
            foreach(var i in code) {
                if(i.operand is MethodInfo m && m.DeclaringType==typeof(CommandBuffer)) {
                    var p=m.GetParameters();
                    if(m.Name=="SetRenderTarget" && p.Length==2 && p[0].ParameterType==typeof(RenderTargetIdentifier[])) {
                        var camera=new CodeInstruction(__originalMethod.Name=="method_2"?OpCodes.Ldarg_1:OpCodes.Ldarg_2);
                        camera.MoveLabelsFrom(i);camera.MoveBlocksFrom(i);yield return camera;
                        i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(WaterReflectionCapture),nameof(Targets));targets++;
                    } else if(m.Name=="DrawMesh" && (p.Length==5 || p.Length==6)) {
                        // Keep the native call's branch labels on the extra argument.
                        var camera=new CodeInstruction(__originalMethod.Name=="method_2"?OpCodes.Ldarg_1:OpCodes.Ldarg_2);
                        camera.MoveLabelsFrom(i);camera.MoveBlocksFrom(i);yield return camera;
                        i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(WaterReflectionCapture),p.Length==6?nameof(DrawBlock):nameof(Draw));draws++;
                    }
                }
                yield return i;
            }
            if(targets!=1 || draws!=2)throw new InvalidOperationException("Native water capture draw contract changed");
        }
        private static void Targets(CommandBuffer cb,RenderTargetIdentifier[] targets,RenderTargetIdentifier depth,Camera camera)
        {
            Active?.CaptureBottom(cb,camera);
            cb.SetRenderTarget(targets,depth);
            if(Active==null)return;
            var b=Active.bindings.GetValue(cb,key=>new Binding());b.Targets=(RenderTargetIdentifier[])targets.Clone();b.Depth=depth;
        }
        private static void DrawBlock(CommandBuffer cb,Mesh mesh,Matrix4x4 matrix,Material original,int submesh,int pass,MaterialPropertyBlock block,Camera camera)
        {
            cb.DrawMesh(mesh,matrix,original,submesh,pass,block);
            if(pass==0 && SupportedMaterial(original))Active?.Record(cb,mesh,matrix,submesh,camera);
        }
        private static void Draw(CommandBuffer cb,Mesh mesh,Matrix4x4 matrix,Material original,int submesh,int pass,Camera camera)
        {
            cb.DrawMesh(mesh,matrix,original,submesh,pass);
            if(pass==0 && SupportedMaterial(original))Active?.Record(cb,mesh,matrix,submesh,camera);
        }
        private static bool SupportedMaterial(Material m)=>m!=null && m.shader!=null &&
            (m.shader.name=="Hidden/WaterForSSRv2" || m.shader.name=="Hidden/FTFX/WaterParity");
        private bool Enabled(Camera c)=>!failed && Requested?.Invoke()==true && c!=null && IsMainCamera?.Invoke(c)==true &&
            SsrPipeline.Supports(c,c.pixelWidth,c.pixelHeight) && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat);
        private void BeforeCamera(Camera c)
        {
            if(!entries.TryGetValue(c,out var e))return;
            if(!Enabled(c)){e.Capture.Clear();return;}
            try {Prepare(c);}catch(Exception error){Fail(error);}
        }
        private static RenderTexture Target(string name,int w,int h,RenderTextureFormat format)
        {
            var t=new RenderTexture(w,h,0,format,RenderTextureReadWrite.Linear){name="FTFX water "+name,
                filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            if(!t.Create()){UnityEngine.Object.DestroyImmediate(t);throw new InvalidOperationException("Water capture allocation failed");}
            var previous=RenderTexture.active;try{RenderTexture.active=t;GL.Clear(false,true,Color.clear);}finally{RenderTexture.active=previous;}
            return t;
        }
        private Entry Prepare(Camera c)
        {
            if(!entries.TryGetValue(c,out var e)) {
                e=new Entry{Camera=c,Capture=new CommandBuffer{name="FTFX water - clear capture"}};
                try {e.Bottom=Target("opaque depth",c.pixelWidth,c.pixelHeight,RenderTextureFormat.RFloat);
                    e.Mask=Target("surface depth and shoreline",c.pixelWidth,c.pixelHeight,RenderTextureFormat.RGFloat);
                    c.AddCommandBuffer(CameraEvent.AfterGBuffer,e.Capture);entries.Add(c,e);
                }catch{e.Dispose();throw;}
            }
            foreach(var t in new[]{e.Bottom,e.Mask})if(t.width!=c.pixelWidth || t.height!=c.pixelHeight || !t.IsCreated()) {
                // Retain texture object identity for already recorded native buffers.
                t.Release();t.width=c.pixelWidth;t.height=c.pixelHeight;if(!t.Create())throw new InvalidOperationException("Water capture resize failed");
            }
            e.Capture.Clear();e.Capture.SetRenderTarget(e.Mask);e.Capture.ClearRenderTarget(false,true,Color.clear);
            e.Capture.SetGlobalFloat("_FTFXWaterBottomCaptured",0);
            e.Capture.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);return e;
        }
        private void CaptureBottom(CommandBuffer cb,Camera camera)
        {
            if(!Enabled(camera))return;
            try {
                var e=Prepare(camera);
                if(!recorded.Any(w=>ReferenceEquals(w.Target,cb)))recorded.Add(new WeakReference(cb));
                // The resolved depth may still be stale at AfterGBuffer. Read
                // it at the native water boundary, before water depth writes.
                // A GPU guard preserves the first snapshot across all owners.
                cb.SetGlobalTexture("_WaterSourceDepth",BuiltinRenderTextureType.ResolvedDepth);
                cb.SetRenderTarget(e.Bottom);cb.DrawMesh(triangle,Matrix4x4.identity,material,0,0);
                cb.SetGlobalFloat("_FTFXWaterBottomCaptured",1);
            }catch(Exception error){Fail(error);}
        }
        private void Record(CommandBuffer cb,Mesh mesh,Matrix4x4 matrix,int submesh,Camera camera)
        {
            if(!Enabled(camera) || !bindings.TryGetValue(cb,out var b))return;
            try {
                var e=Prepare(camera);
                if(!recorded.Any(w=>ReferenceEquals(w.Target,cb)))recorded.Add(new WeakReference(cb));
                var block=new MaterialPropertyBlock();block.SetTexture("_WaterBottom",e.Bottom);
                cb.SetRenderTarget(e.Mask,b.Depth);
                cb.DrawMesh(mesh,matrix,material,submesh,1,block);
            }catch(Exception error){Fail(error);}
            finally{cb.SetRenderTarget(b.Targets,b.Depth);}
        }
        private void Fail(Exception error){failed=true;Plugin.MyLog.LogWarning("Water reflection capture unavailable; ordinary SSSR retained. "+error);}
        internal static RenderTexture Get(Camera c,int w,int h)
        {
            var a=Active;
            if(a==null || !a.Enabled(c) || c.pixelWidth!=w || c.pixelHeight!=h)return null;
            // SSR records its commands from PostProcessLayer.OnPreCull. Native
            // water callbacks may record later, though their GPU draws execute
            // before SSR. Bind a stable resource now, including on the first
            // frame; CPU producer timestamps cannot establish GPU freshness.
            // The per-render AfterGBuffer clear prevents stale/no-water coverage.
            try {return a.Prepare(c).Mask;}catch(Exception error){a.Fail(error);return null;}
        }
        internal void Prune()
        {
            foreach(var c in entries.Keys.Where(c=>c==null).ToArray()){entries[c].Dispose();entries.Remove(c);}
            recorded.RemoveAll(w=>!w.IsAlive);
        }
        public void Dispose()
        {
            Camera.onPreCull-=BeforeCamera;if(Active==this)Active=null;harmony.UnpatchSelf();
            // Native pre-cull rebuilds these buffers. Detach our recorded resource
            // references before releasing them, including when no owner remains.
            foreach(var w in recorded)if(w.Target is CommandBuffer cb)try{cb.Clear();}catch{}
            recorded.Clear();foreach(var e in entries.Values)e.Dispose();entries.Clear();
            if(material!=null)UnityEngine.Object.DestroyImmediate(material);if(triangle!=null)UnityEngine.Object.DestroyImmediate(triangle);
        }
    }
}
