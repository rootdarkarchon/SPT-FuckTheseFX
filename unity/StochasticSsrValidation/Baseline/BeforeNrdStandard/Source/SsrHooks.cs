using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Replace the main-camera renderer as one transaction; native resolve/history/
    // Gaussian mips never see the new signal. A separate forced-miss native path
    // supplies receiver-colour fallback. Disabled and optics execute native.
    internal sealed class SsrHooks : IDisposable
    {
        internal static SsrHooks Active;
        internal Func<Options> Settings;
        internal Func<Camera,bool> IsMainCamera;
        internal Action<string> Error;
        private readonly ComputeShader compute;
        private readonly Texture2D noise;
        private readonly Shader support,reflection;
        private readonly PropertyInfo settings;
        private readonly MethodInfo reset;
        private readonly Dictionary<Camera,ConfidenceSsrPipeline> cameras=new Dictionary<Camera,ConfidenceSsrPipeline>();
        private readonly Dictionary<object,Camera> renderers=new Dictionary<object,Camera>();
        private readonly List<Camera> dead=new List<Camera>();
        private readonly List<object> deadRenderers=new List<object>();
        private Options previous;
        private bool hasPrevious,failed;
        internal sealed class State { internal ConfidenceSsrPipeline Pipeline; }
        internal SsrHooks(Type renderer,ComputeShader compute,Shader support,Shader reflection,Texture2D noise)
        {
            this.compute=compute;this.support=support;this.reflection=reflection;this.noise=noise;
            settings=AccessTools.Property(renderer,"settings") ?? throw new MissingMemberException(renderer.FullName,"settings");
            reset=AccessTools.Method(renderer,"ResetHistory") ?? throw new MissingMethodException(renderer.FullName,"ResetHistory");
        }
        internal static bool Before(object __instance,PostProcessRenderContext __0,out State __state)
        {
            __state=null;var self=Active;
            if(self==null || !self.IsMainCamera(__0.camera) || __0.isSceneView)return true;
            var o=self.Settings();
            try {
                if(!self.hasPrevious || !o.SameRendering(self.previous)) {
                    foreach(var r in self.renderers.Keys)self.reset.Invoke(r,null);
                    foreach(var p in self.cameras.Values)p.ResetHistory();
                    self.previous=o;self.hasPrevious=true;self.failed=false;
                }
                if(o.Mode==SsrMode.Native || self.failed || !SsrPipeline.Supports(__0.camera,__0.width,__0.height)) {
                    if(self.renderers.Remove(__instance))self.reset.Invoke(__instance,null);
                    if(self.cameras.TryGetValue(__0.camera,out var idle))idle.ResetHistory();
                    return true;
                }
                var effect=(ScreenSpaceReflections)self.settings.GetValue(__instance);
                var traceSize=o.TraceDimensions(__0.width,__0.height);
                o.ApplyNativeCutoff(effect.cutOffThreshold.value);
                if(!self.cameras.TryGetValue(__0.camera,out var pipeline)) {
                    pipeline=new ConfidenceSsrPipeline(self.compute,self.support,self.reflection,self.noise,
                        __0.resources.shaders.screenSpaceReflections,__0.resources.computeShaders.gaussianDownsample);self.cameras.Add(__0.camera,pipeline);
                }
                pipeline.Ensure(__0.width,__0.height,traceSize.x,traceSize.y,__0.sourceFormat);
                if(!self.renderers.ContainsKey(__instance)){self.renderers.Add(__instance,__0.camera);self.reset.Invoke(__instance,null);}
                int aaMode=!__0.IsTemporalAntialiasingActive()?0:(__0.SSAA!=null && __0.SSAA.IsUsingDLSS()?2:1);
                pipeline.PrepareRaster(__0.camera,aaMode,__0.width,__0.height);
                using(SsrGpuSample.Measure(__0.command,__0.camera,"Total"))pipeline.Render(__0.command,__0.camera,__0.source,__0.destination,o);
                __state=new State {Pipeline=pipeline};return false;
            } catch(Exception e) {
                self.failed=true;
                foreach(var p in self.cameras.Values)p.ResetHistory();
                self.reset.Invoke(__instance,null);
                self.Error?.Invoke("Stochastic SSR failed; native SSR retained until settings change. "+e);
                return true;
            }
        }
        internal static Exception After(Exception __exception,State __state) { if(__exception!=null)__state?.Pipeline.ResetHistory();return __exception; }
        internal void Prune() {
            dead.Clear();foreach(var pair in cameras)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var c in dead){cameras[c].Dispose();cameras.Remove(c);}
            deadRenderers.Clear();foreach(var pair in renderers)if(pair.Value==null)deadRenderers.Add(pair.Key);
            foreach(var r in deadRenderers)renderers.Remove(r);
        }
        internal void Suspend() {
            foreach(var r in renderers.Keys)try{reset.Invoke(r,null);}catch{}
            renderers.Clear();foreach(var p in cameras.Values)p.Dispose();cameras.Clear();hasPrevious=false;failed=false;
        }
        public void Dispose(){Suspend();if(Active==this)Active=null;}
    }
}
