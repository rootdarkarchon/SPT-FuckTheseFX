using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Keep native hit response, material mips and composition. Intercept only
    // ray generation and (when selected) temporal reconstruction.
    internal sealed class SsrHooks : IDisposable
    {
        internal static SsrHooks Active;
        internal Func<Options> Settings;
        internal Func<Camera,bool> IsMainCamera;
        internal Action<string> Error;
        [ThreadStatic] private static State tracing;
        private readonly ComputeShader compute, denoiser;
        private readonly Texture2D noise;
        private readonly Shader shader;
        private readonly FieldInfo resolve;
        private readonly PropertyInfo settings;
        private readonly MethodInfo reset;
        private readonly Dictionary<Camera,SsrPipeline> cameras=new Dictionary<Camera,SsrPipeline>();
        private readonly Dictionary<object,Camera> renderers=new Dictionary<object,Camera>();
        private readonly List<object> deadRenderers=new List<object>();
        private readonly List<Camera> dead=new List<Camera>();
        private Options previous;
        private bool hasPrevious, failed;

        internal sealed class State
        {
            internal State Parent;
            internal SsrPipeline Pipeline;
            internal Options Options;
            internal PostProcessRenderContext Context;
            internal object Renderer;
            internal PropertySheet Sheet;
            internal RenderTargetIdentifier Destination;
            internal bool Direct, Replaced;
            internal int Traces;
            internal SsrGpuSample Total;
        }

        internal SsrHooks(Type renderer, ComputeShader compute, Shader shader, ComputeShader denoiser, Texture2D noise)
        {
            this.compute=compute;this.shader=shader;this.denoiser=denoiser;this.noise=noise;
            resolve=AccessTools.Field(renderer,"m_Resolve") ?? throw new MissingFieldException(renderer.FullName,"m_Resolve");
            settings=AccessTools.Property(renderer,"settings") ?? throw new MissingMemberException(renderer.FullName,"settings");
            reset=AccessTools.Method(renderer,"ResetHistory") ?? throw new MissingMethodException(renderer.FullName,"ResetHistory");
        }

        internal static void Before(object __instance, PostProcessRenderContext __0, out State __state)
        {
            var parent=tracing;tracing=null;
            __state=parent==null?null:new State { Parent=parent };
            var self=Active;
            if(self==null || !self.IsMainCamera(__0.camera) || __0.isSceneView)return;
            var options=self.Settings();
            try
            {
                if(!self.hasPrevious || !options.Equals(self.previous))
                {
                    foreach(var renderer in self.renderers.Keys)self.reset.Invoke(renderer,null);
                    foreach(var pipeline in self.cameras.Values)pipeline.ResetHistory();
                    self.previous=options;self.hasPrevious=true;self.failed=false;
                }
                if(options.Mode==SsrMode.Native || self.failed || !SsrPipeline.Supports(__0.camera,__0.width,__0.height))
                {
                    if(self.renderers.Remove(__instance))self.reset.Invoke(__instance,null);
                    if(self.cameras.TryGetValue(__0.camera,out var paused))paused.ResetHistory();
                    return;
                }
                var native=__0.resources.shaders.screenSpaceReflections;
                if(native==null || !native.isSupported || native.passCount!=4)return;
                if(!self.cameras.TryGetValue(__0.camera,out var p))
                {
                    p=new SsrPipeline(self.compute,self.shader,self.denoiser,self.noise);
                    self.cameras.Add(__0.camera,p);
                }
                if(!self.renderers.ContainsKey(__instance))
                { self.renderers.Add(__instance,__0.camera);self.reset.Invoke(__instance,null); }
                // Native Render selects/allocates its square resolve before pass 0.
                // Allocate here too so an aliased destination can be redirected.
                var prior=self.resolve.GetValue(__instance) as RenderTexture;
                p.Ensure(__0.width,__0.height,1,__0.sourceFormat,prior!=null?prior.width:0);
                __state=new State { Parent=parent, Pipeline=p, Options=options, Context=__0,
                    Renderer=__instance, Sheet=__0.propertySheets.Get(native), Destination=__0.destination,
                    Direct=!__0.source.Equals(__0.destination), Replaced=true };
                if(!__state.Direct)__0.destination=p.NativeResult;
                __state.Total=SsrGpuSample.Measure(__0.command,__0.camera,"Total");
                tracing=__state;
            }
            catch(Exception error)
            {
                if(__state!=null && __state.Replaced)
                { __0.destination=__state.Destination;__state.Total?.Dispose(); }
                __state=parent==null?null:new State { Parent=parent };tracing=null;
                self.Fail(error);
            }
        }

        internal static bool TracePass(CommandBuffer __0, RenderTargetIdentifier __1, RenderTargetIdentifier __2, PropertySheet __3, int __4)
        {
            var s=tracing;var self=Active;
            if(self==null || s==null || !s.Replaced || !ReferenceEquals(__0,s.Context.command) || !ReferenceEquals(__3,s.Sheet))return true;
            try
            {
                var o=s.Options;
                var effect=(ScreenSpaceReflections)self.settings.GetValue(s.Renderer);
                o.NativeResponse=true;o.NativeTraceContract=true;o.Smoothness=effect.cutOffThreshold.value;
                o.Distance=o.RayDistance(s.Context.camera);
                if(__4==0 && __2.Equals(new RenderTargetIdentifier(Shader.PropertyToID("_Test"))))
                {
                    var target=(RenderTexture)self.resolve.GetValue(s.Renderer);
                    s.Pipeline.Ensure(s.Context.width,s.Context.height,1,s.Context.sourceFormat,target.width);
                    s.Context.destination=s.Direct?s.Destination:new RenderTargetIdentifier(s.Pipeline.NativeResult);
                    s.Pipeline.Trace(__0,s.Context.camera,__1,o);
                    if(s.Context.sourceFormat==RenderTextureFormat.ARGBHalf)__0.CopyTexture(s.Pipeline.NativeTest,__2);
                    else __0.Blit(s.Pipeline.NativeTest,__2);
                    if(!o.CustomReconstruction)s.Pipeline.CompleteTraceFrame(s.Context.camera,o);
                    s.Traces++;return false;
                }
                if(__4==2 && o.CustomReconstruction && s.Traces==1)
                {
                    s.Pipeline.ReconstructNative(__0,s.Context.camera,__1,__2,o);
                    return false;
                }
            }
            catch(Exception error)
            {
                // The original pass overwrites its output after any partial
                // custom commands. Subsequent passes then follow native routing.
                s.Options.Mode=SsrMode.Native;s.Pipeline.ResetHistory();tracing=null;
                self.Fail(error);
            }
            return true;
        }

        internal static Exception After(Exception __exception, PostProcessRenderContext __0, State __state)
        {
            tracing=__state?.Parent;
            if(__state==null || !__state.Replaced)return __exception;
            __0.destination=__state.Destination;
            try
            {
                if(__exception==null)
                {
                    if(!__state.Direct)__state.Pipeline.CopyFinal(__0.command,__state.Destination);
                    if(__state.Options.Mode!=SsrMode.Native && __state.Traces!=1)
                        Active?.Fail(new InvalidOperationException("Expected exactly one replaced SSR trace pass."));
                }
            }
            finally { __state.Total?.Dispose(); }
            return __exception;
        }

        private void Fail(Exception error)
        {
            if(!failed)Error?.Invoke("Stochastic SSR failed; native SSR retained until settings change. "+error);
            failed=true;
            foreach(var p in cameras.Values)p.ResetHistory();
            foreach(var renderer in renderers.Keys)try { reset.Invoke(renderer,null); } catch { }
        }

        internal void Prune()
        {
            dead.Clear();foreach(var pair in cameras)if(pair.Key==null)dead.Add(pair.Key);
            foreach(var camera in dead){cameras[camera].Dispose();cameras.Remove(camera);}
            deadRenderers.Clear();foreach(var pair in renderers)if(pair.Value==null)deadRenderers.Add(pair.Key);
            foreach(var renderer in deadRenderers)renderers.Remove(renderer);
        }
        internal void Suspend()
        {
            if(Active==this)tracing=null;
            foreach(var renderer in renderers.Keys)try { reset.Invoke(renderer,null); } catch { }
            renderers.Clear();
            foreach(var p in cameras.Values)p.Dispose();cameras.Clear();
            hasPrevious=false;failed=false;
        }
        public void Dispose() { Suspend();if(Active==this)Active=null; }
    }
}
