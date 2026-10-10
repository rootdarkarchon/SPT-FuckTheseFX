using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Replace each eligible camera renderer as one transaction; native resolve/history/
    // Gaussian mips never see the new signal. A separate forced-miss native path
    // supplies receiver-colour fallback. Unsupported and vision optics execute native.
    internal sealed class SsrHooks : IDisposable
    {
        internal static SsrHooks Active;
        internal Func<Options> Settings;
        internal Func<Camera,bool> IsMainCamera;
        internal Func<Camera,bool> IsOpticCamera;
        internal Func<Camera,Options,Options> CameraOptions;
        internal Func<Camera,int> CameraGeneration;
        internal Action<string> Error;
        private readonly ComputeShader compute;
        private readonly Texture2D noise;
        private readonly Shader support,reflection;
        private readonly PropertyInfo settings;
        private readonly MethodInfo reset;
        private readonly Dictionary<Camera,SsrReflectionPipeline> cameras=new Dictionary<Camera,SsrReflectionPipeline>();
        private readonly Dictionary<object,Camera> renderers=new Dictionary<object,Camera>();
        private readonly List<Camera> dead=new List<Camera>();
        private readonly List<object> deadRenderers=new List<object>();
        private readonly HashSet<Camera> failed=new HashSet<Camera>();
        private readonly Dictionary<Camera,int> generations=new Dictionary<Camera,int>();
        private readonly Dictionary<Camera,Activity> activity=new Dictionary<Camera,Activity>();
        private struct Activity { internal PostProcessRenderContext Context;internal ScreenSpaceReflections Effect;internal int Frame; }
        private Options previous;
        private bool hasPrevious;
        internal sealed class State { internal SsrReflectionPipeline Pipeline;internal SsrHooks Owner;internal Camera Camera; }
        internal SsrHooks(Type renderer,ComputeShader compute,Shader support,Shader reflection,Texture2D noise)
        {
            this.compute=compute;this.support=support;this.reflection=reflection;this.noise=noise;
            settings=AccessTools.Property(renderer,"settings") ?? throw new MissingMemberException(renderer.FullName,"settings");
            reset=AccessTools.Method(renderer,"ResetHistory") ?? throw new MissingMethodException(renderer.FullName,"ResetHistory");
        }
        internal static bool Before(object __instance,PostProcessRenderContext __0,out State __state)
        {
            __state=null;var self=Active;
            if(self==null || __0.isSceneView)return true;
            bool optic=self.IsOpticCamera?.Invoke(__0.camera)==true;
            if(self.IsMainCamera?.Invoke(__0.camera)!=true && !optic){self.UseNative(__instance,__0.camera);return true;}
            var o=self.Settings();
            try {
                if(!self.hasPrevious || !o.SameRendering(self.previous)) {
                    foreach(var r in self.renderers.Keys)self.reset.Invoke(r,null);
                    foreach(var p in self.cameras.Values)p.ResetHistory();
                    self.previous=o;self.hasPrevious=true;self.failed.Clear();
                }
                if(o.Mode==SsrMode.Native || (optic && !o.Optics) || self.failed.Contains(__0.camera) || !SsrPipeline.Supports(__0.camera,__0.width,__0.height)) {
                    self.UseNative(__instance,__0.camera);
                    return true;
                }
                // Compare configured settings globally before deriving camera-local resolution.
                // Alternating main and optic profiles must never reset each other's histories.
                if(self.CameraOptions!=null)o=self.CameraOptions(__0.camera,o);
                var effect=(ScreenSpaceReflections)self.settings.GetValue(__instance);
                var traceSize=o.TraceDimensions();
                if(optic){int size=Math.Min(traceSize.x,Math.Min(__0.width,__0.height));traceSize=new Vector2Int(size,size);}
                o.ApplyNativeCutoff(effect.cutOffThreshold.value);
                if(!self.cameras.TryGetValue(__0.camera,out var pipeline)) {
                    pipeline=new SsrReflectionPipeline(self.compute,self.support,self.reflection,self.noise,
                        __0.resources.shaders.screenSpaceReflections,__0.resources.computeShaders.gaussianDownsample);self.cameras.Add(__0.camera,pipeline);
                }
                int generation=self.CameraGeneration?.Invoke(__0.camera)??0;
                if(!self.generations.TryGetValue(__0.camera,out int oldGeneration) || generation!=oldGeneration) {
                    pipeline.ResetHistory();self.generations[__0.camera]=generation;
                }
                pipeline.Ensure(__0.width,__0.height,traceSize.x,traceSize.y,__0.sourceFormat);
                if(!self.renderers.ContainsKey(__instance)){self.renderers.Add(__instance,__0.camera);self.reset.Invoke(__instance,null);}
                int aaMode=!__0.IsTemporalAntialiasingActive()?0:(__0.SSAA!=null && __0.SSAA.IsUsingDLSS()?2:1);
                pipeline.PrepareRaster(__0.camera,aaMode,__0.width,__0.height);
                using(SsrGpuSample.Measure(__0.command,__0.camera,"Total"))pipeline.Render(__0.command,__0.camera,__0.source,__0.destination,o);
                self.activity[__0.camera]=new Activity {Context=__0,Effect=effect,Frame=Time.frameCount};
                __state=new State {Pipeline=pipeline,Owner=self,Camera=__0.camera};return false;
            } catch(Exception e) {
                self.failed.Add(__0.camera);
                self.activity.Remove(__0.camera);
                if(self.cameras.TryGetValue(__0.camera,out var failedPipeline))failedPipeline.ResetHistory();
                self.reset.Invoke(__instance,null);
                self.Error?.Invoke("Stochastic SSR failed for "+__0.camera.name+"; native SSR retained for this camera until settings change. "+e);
                return true;
            }
        }
        internal static Exception After(Exception __exception,State __state) {
            if(__exception!=null && __state!=null){__state.Pipeline.ResetHistory();__state.Owner.activity.Remove(__state.Camera);}
            return __exception;
        }
        internal bool HasActiveRenderer(int frame) {
            if(Settings==null)return false;
            var configured=Settings();if(configured.Mode!=SsrMode.Stochastic)return false;
            foreach(var pair in activity) {
                var camera=pair.Key;var current=pair.Value;int age=frame-current.Frame;
                // AmbientLight can update before this frame's SSR hook. Accept
                // the previous frame only while the camera/effect remain eligible.
                if(camera==null || !camera.isActiveAndEnabled || age<0 || age>1 || failed.Contains(camera))continue;
                bool optic=IsOpticCamera?.Invoke(camera)==true;
                if(IsMainCamera?.Invoke(camera)!=true && !optic)continue;
                if(optic && !configured.Optics)continue;
                var layer=camera.GetComponent<PostProcessLayer>();
                if(layer==null || !layer.isActiveAndEnabled)continue;
                if(current.Context.isSceneView || !SsrPipeline.Supports(camera,current.Context.width,current.Context.height))continue;
                if(current.Effect!=null && current.Effect.active && current.Effect.IsEnabledAndSupported(current.Context))return true;
            }
            return false;
        }
        internal bool HasFailed(Camera camera)=>camera!=null && failed.Contains(camera);
        private void UseNative(object renderer,Camera camera)
        {
            if(camera!=null)activity.Remove(camera);
            if(renderers.TryGetValue(renderer,out var previousCamera)) {
                activity.Remove(previousCamera);
                renderers.Remove(renderer);reset.Invoke(renderer,null);
                if(cameras.TryGetValue(previousCamera,out var previousPipeline))previousPipeline.ResetHistory();
            }
            if(camera!=null && cameras.TryGetValue(camera,out var idle))idle.ResetHistory();
        }
        internal void Prune() {
            dead.Clear();foreach(var pair in cameras)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var c in dead){cameras[c].Dispose();cameras.Remove(c);generations.Remove(c);failed.Remove(c);activity.Remove(c);}
            deadRenderers.Clear();foreach(var pair in renderers)if(pair.Value==null)deadRenderers.Add(pair.Key);
            foreach(var r in deadRenderers)renderers.Remove(r);
        }
        internal void Suspend() {
            foreach(var r in renderers.Keys)try{reset.Invoke(r,null);}catch{}
            renderers.Clear();foreach(var p in cameras.Values)p.Dispose();cameras.Clear();generations.Clear();failed.Clear();activity.Clear();hasPrevious=false;
        }
        public void Dispose(){Suspend();if(Active==this)Active=null;}
    }
}
