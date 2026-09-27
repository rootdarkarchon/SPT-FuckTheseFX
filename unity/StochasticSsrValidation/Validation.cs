using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using FuckTheseFX.Source.StochasticSSR;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

// Startup dependencies only: release hooks below execute the actual installed
// post-processing renderer. The game camera singleton itself is checked by ABI.
namespace EFT.CameraControl { public class CameraManager { public static CameraManager Instance; public Camera Camera; } }
namespace FuckTheseFX { internal static class Plugin { internal const string Guid="com.rootdarkarchon.fuckthesefx"; internal static BepInEx.Logging.ManualLogSource MyLog=new BepInEx.Logging.ManualLogSource("SSR validation"); } }

public static class StochasticSsrValidation
{
    private static readonly List<string> checks=new List<string>();
    private static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private static Camera camera;
    private static Texture2D depths,normals,specular,scene,noise;
    private static Shader native,blend;
    private static ComputeShader compute,amd,gaussian;
    private static AssetBundle game,baselineBundle;
    private static Type rendererType,oldHookType,oldOptionsType;
    private static int[] passes;
    private static int width=192,height=128;
    private static void Check(bool value,string text) { if(!value)throw new Exception(text);checks.Add(text);Debug.Log("PASS "+text); }
    public static void Run()
    {
        try
        {
            AccessTools.Property(typeof(Paths),"BepInExConfigPath").SetValue(null,Path.GetFullPath("BepInEx-test.cfg"));
            AccessTools.Property(typeof(Paths),"ConfigPath").SetValue(null,Directory.GetCurrentDirectory());
            compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HiZ.compute");
            blend=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Support.shader");
            amd=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/FidelityDenoiser.compute");
            Check(!ShaderUtil.GetComputeShaderMessages(compute).Any(m=>m.severity.ToString()=="Error") && !ShaderUtil.GetComputeShaderMessages(amd).Any(m=>m.severity.ToString()=="Error"),"release compute shaders compile");
            noise=new Texture2D(128,128,TextureFormat.RG16,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};owned.Add(noise);
            noise.LoadRawTextureData(File.ReadAllBytes("Assets/AMD/BlueNoise.bytes"));noise.Apply();
            game=AssetBundle.LoadFromFile(Path.Combine(File.ReadAllText("game-path.txt").Trim(),"EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"));
            native=game.LoadAllAssets<Shader>().Single(s=>s.name=="Hidden/PostProcessing/ScreenSpaceReflections");
            gaussian=game.LoadAllAssets<ComputeShader>().First(c=>c.name.IndexOf("Gaussian",StringComparison.OrdinalIgnoreCase)>=0);
            var go=new GameObject("SSR release fixture");owned.Add(go);camera=go.AddComponent<Camera>();camera.enabled=false;
            camera.renderingPath=RenderingPath.DeferredShading;camera.allowHDR=true;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.fieldOfView=65;
            Fixture();
            rendererType=AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.ScreenSpaceReflectionsRenderer");
            var baseline=Assembly.LoadFile(Path.GetFullPath("Baseline/FTFX.SsrPrototype.dll"));
            oldHookType=baseline.GetType("FuckTheseFX.SsrPrototype.PrototypeHooks",true);
            oldOptionsType=baseline.GetType("FuckTheseFX.SsrPrototype.Options",true);
            baselineBundle=AssetBundle.LoadFromFile(Path.GetFullPath("Baseline/ssrprototype"));
            var config=new ConfigFile(Path.GetFullPath("test.cfg"),false);config.SaveOnConfigSet=false;
            var settings=new SsrSettings(config);
            Check(config.Count==10 && settings.Enabled.Value && settings.Sampling.Value==ReconstructionMode.Native,"release has ten settings with Enabled and Native defaults");
            foreach(var guid in new[]{StochasticSsrIntegration.SsrsixGuid})
            {
                Chainloader.PluginInfos.Add(guid,new PluginInfo());
                try
                {
                    using(var integration=new StochasticSsrIntegration("deliberately missing assets/plugin.dll",settings))
                    {
                        integration.Initialize();
                        Check(AccessTools.Field(typeof(StochasticSsrIntegration),"bundle").GetValue(integration)==null &&
                            AccessTools.Field(typeof(StochasticSsrIntegration),"hooks").GetValue(integration)==null &&
                            AccessTools.Field(typeof(StochasticSsrIntegration),"harmony").GetValue(integration)==null,"conflict bypasses all asset loading and patching: "+guid);
                    }
                }
                finally {Chainloader.PluginInfos.Remove(guid);}
            }
            foreach(var backend in new[]{ReconstructionMode.Native,ReconstructionMode.Custom})
            {
                settings.Sampling.Value=backend;
                var expected=RenderSequence(settings.Read(),true,12,false);
                var actual=RenderSequence(settings.Read(),false,12,false);
                Check(expected.Zip(actual,(a,b)=>Error(a,b)).Max()==0,"exact RGBA parity with accepted 0.10.0 prototype across twelve frames: "+backend);
                var aliasExpected=RenderSequence(settings.Read(),true,1,false,true);
                var aliasActual=RenderSequence(settings.Read(),false,1,false,true);
                Check(Error(aliasExpected[0],aliasActual[0])==0,"aliased source/destination retains exact prototype RGBA: "+backend);
            }
            Lifecycle(settings);
            Check(BuildPipeline.BuildAssetBundles("Output",new[]{new AssetBundleBuild {assetBundleName="stochasticssr",assetNames=new[]{"Assets/HiZ.compute","Assets/Support.shader","Assets/FidelityDenoiser.compute","Assets/AMD/BlueNoise.bytes"}}},BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64)!=null,"release bundle build");
            File.WriteAllLines("validation.txt",checks);
        }
        catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);throw;}
        finally {foreach(var o in owned)if(o!=null)UnityEngine.Object.DestroyImmediate(o);if(baselineBundle!=null)baselineBundle.Unload(true);if(game!=null)game.Unload(true);}
    }
    private static object OldOptions(Options o)
    {
        object boxed=Activator.CreateInstance(oldOptionsType);
        foreach(var field in typeof(Options).GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
        {
            var target=AccessTools.Field(oldOptionsType,field.Name);if(target==null)continue;
            object value=field.GetValue(o);if(target.FieldType.IsEnum)value=Enum.Parse(target.FieldType,value.ToString());target.SetValue(boxed,value);
        }
        return boxed;
    }
    private static List<Color[]> RenderSequence(Options options,bool baseline,int frames,bool optic,bool alias=false)
    {
        var output=new List<Color[]>();var factory=new PropertySheetFactory();var renderer=CreateRenderer(out var effect);
        var context=Context(factory);SsrHooks hooks=null;object oldHooks=null;
        if(alias)context.source=camera.targetTexture;
        var harmony=new Harmony("ssr.release.parity");
        var blit=AccessTools.Method(typeof(RuntimeUtilities),"BlitFullscreenTriangle",new[]{typeof(CommandBuffer),typeof(RenderTargetIdentifier),typeof(RenderTargetIdentifier),typeof(PropertySheet),typeof(int),typeof(bool),typeof(Rect?)});
        if(baseline)
        {
            oldHooks=Activator.CreateInstance(oldHookType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{baselineBundle.LoadAsset<ComputeShader>("Assets/HiZ.compute"),baselineBundle.LoadAsset<Shader>("Assets/Confidence.shader"),baselineBundle.LoadAsset<ComputeShader>("Assets/FidelityDenoiser.compute"),noise},null);
            var old=OldOptions(options);
            var func=Expression.Lambda(typeof(Func<>).MakeGenericType(oldOptionsType),Expression.Constant(old,oldOptionsType)).Compile();
            AccessTools.Field(oldHookType,"Settings").SetValue(oldHooks,func);
            AccessTools.Field(oldHookType,"IsMainCamera").SetValue(oldHooks,new Func<Camera,bool>(c=>!optic && c==camera));
            AccessTools.Field(oldHookType,"Error").SetValue(oldHooks,new Action<string>(s=>{throw new Exception(s);}));
            AccessTools.Field(oldHookType,"Active").SetValue(null,oldHooks);
            harmony.Patch(blit,prefix:new HarmonyMethod(AccessTools.Method(oldHookType,"TracePass")),postfix:new HarmonyMethod(typeof(StochasticSsrValidation),nameof(Count)));
        }
        else
        {
            hooks=new SsrHooks(rendererType,compute,blend,amd,noise){Settings=()=>options,IsMainCamera=c=>!optic && c==camera,Error=s=>{throw new Exception(s);}};
            SsrHooks.Active=hooks;
            harmony.Patch(blit,prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.TracePass)),postfix:new HarmonyMethod(typeof(StochasticSsrValidation),nameof(Count)));
        }
        try
        {
            for(int i=0;i<frames;i++)
            {
                Globals(context.command);passes=new int[4];
                if(alias)context.command.Blit(scene,camera.targetTexture);
                if(baseline)
                {
                    var args=new object[]{renderer,context,null};bool run=(bool)AccessTools.Method(oldHookType,"Before").Invoke(null,args);
                    if(run)renderer.Render(context);
                    AccessTools.Method(oldHookType,"After").Invoke(null,new[]{null,(object)renderer,context,args[2]});
                }
                else
                {
                    SsrHooks.Before(renderer,context,out var state);renderer.Render(context);SsrHooks.After(null,context,state);
                    if(state?.Pipeline!=null)
                    {
                        var p=state.Pipeline;
                        Check(new[]{"Diagnostic","EnvironmentResult","ReplacementResult","Resolved","Confidence"}.All(name=>AccessTools.Field(typeof(SsrPipeline),name)==null),"no prototype diagnostic or comparison fields, frame "+i);
                        Check(p.Radiance.width==((RenderTexture)AccessTools.Field(rendererType,"m_Resolve").GetValue(renderer)).width,"actual native square trace dimensions, frame "+i);
                        Check((p.History!=null)==options.CustomReconstruction,"only Custom allocates denoiser history, frame "+i);
                    }
                }
                Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();
                bool custom=options.Mode!=SsrMode.Native && !optic;
                Check(passes.SequenceEqual(new[]{custom?0:1,1,custom && options.CustomReconstruction?0:1,1}),"one tracer and exactly one reconstruction: "+(baseline?"prototype":"release")+"/"+options.Reconstruction+"/"+i);
                var pixels=Read(camera.targetTexture);Check(pixels.Any(c=>c.r>.01f) && pixels.All(c=>!float.IsNaN(c.r) && !float.IsInfinity(c.r)),"finite nonzero output, frame "+i);
                output.Add(pixels);
            }
        }
        finally {harmony.UnpatchSelf();hooks?.Dispose();(oldHooks as IDisposable)?.Dispose();context.command.Dispose();factory.Release();renderer.Release();}
        return output;
    }
    private static void Lifecycle(SsrSettings settings)
    {
        settings.Enabled.Value=false;var off=RenderSequence(settings.Read(),false,1,false);
        var nativeOff=RenderSequence(settings.Read(),true,1,false);
        Check(Error(off[0],nativeOff[0])==0,"disabled release equals native output exactly");
        settings.Enabled.Value=true;
        var optic=RenderSequence(settings.Read(),false,1,true);
        Check(Error(optic[0],nativeOff[0])==0,"optic/non-main camera equals native output exactly");
        using(var hooks=new SsrHooks(rendererType,compute,blend,amd,noise){Settings=settings.Read,IsMainCamera=c=>c==camera,Error=s=>{throw new Exception(s);}})
        {
            SsrHooks.Active=hooks;var factory=new PropertySheetFactory();var renderer=CreateRenderer(out var effect);var context=Context(factory);
            var harmony=new Harmony("ssr.release.lifecycle");
            var blit=AccessTools.Method(typeof(RuntimeUtilities),"BlitFullscreenTriangle",new[]{typeof(CommandBuffer),typeof(RenderTargetIdentifier),typeof(RenderTargetIdentifier),typeof(PropertySheet),typeof(int),typeof(bool),typeof(Rect?)});
            harmony.Patch(blit,prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.TracePass)),postfix:new HarmonyMethod(typeof(StochasticSsrValidation),nameof(Count)));
            passes=new int[4];
            try
            {
                SsrPipeline last=null;
                foreach(var backend in new[]{ReconstructionMode.Native,ReconstructionMode.Custom,ReconstructionMode.Native})
                {
                    settings.Sampling.Value=backend;Globals(context.command);SsrHooks.Before(renderer,context,out var state);renderer.Render(context);SsrHooks.After(null,context,state);
                    Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();last=state.Pipeline;
                    Check(last.ActualSampleIndex==0,"backend change resets stochastic sequence: "+backend);
                }
                var target=last.NativeResult;hooks.Suspend();
                Check(target==null && (bool)AccessTools.Field(rendererType,"m_ResetHistory").GetValue(renderer),"suspend releases custom resources and resets native history");
                settings.Enabled.Value=false;SsrHooks.Before(renderer,context,out var disabled);Check(disabled==null,"live disabled bypass has no render state");SsrHooks.After(null,context,disabled);
                settings.Enabled.Value=true;Globals(context.command);SsrHooks.Before(renderer,context,out var resumed);renderer.Render(context);SsrHooks.After(null,context,resumed);Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();
                Check(resumed.Pipeline.NativeResult!=null && resumed.Traces==1,"live re-enable recreates resources and restores custom tracing");
                // Destroy a GPU resource and ensure the complete set is rebuilt.
                target=resumed.Pipeline.NativeResult;resumed.Pipeline.Depth[1].Release();
                Globals(context.command);SsrHooks.Before(renderer,context,out var restored);renderer.Render(context);SsrHooks.After(null,context,restored);Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();
                Check(target==null && restored.Pipeline.Depth[1].IsCreated(),"lost depth resource recovers through complete recreation");
                harmony.Patch(AccessTools.Method(rendererType,"Render"),prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.Before)),finalizer:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.After)));
                foreach(var backend in new[]{ReconstructionMode.Native,ReconstructionMode.Custom})
                {
                    settings.Sampling.Value=backend;passes=new int[4];Globals(context.command);
                    renderer.Render(context);Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();
                    Check(passes.SequenceEqual(new[]{0,1,backend==ReconstructionMode.Native?1:0,1}),"actual Harmony render prefix/finalizer and blit routing: "+backend);
                }
                settings.Enabled.Value=false;passes=new int[4];Globals(context.command);renderer.Render(context);Graphics.ExecuteCommandBuffer(context.command);context.command.Clear();
                Check(passes.SequenceEqual(new[]{1,1,1,1}),"actual patched renderer runs entirely native when disabled");
            }
            finally {harmony.UnpatchSelf();context.command.Dispose();factory.Release();renderer.Release();}
        }
    }
    private static PostProcessEffectRenderer CreateRenderer(out ScreenSpaceReflections effect)
    {
        var renderer=(PostProcessEffectRenderer)Activator.CreateInstance(rendererType);
        effect=ScriptableObject.CreateInstance<ScreenSpaceReflections>();owned.Add(effect);
        effect.preset.value=(ScreenSpaceReflectionPreset)7;effect.resolution.value=(ScreenSpaceReflectionResolution)1;
        effect.maximumIterationCount.value=128;effect.cutOffThreshold.value=0;
        AccessTools.Method(rendererType,"SetSettings").Invoke(renderer,new object[]{effect});return renderer;
    }
    private static PostProcessRenderContext Context(PropertySheetFactory factory)
    {
        var resources=ScriptableObject.CreateInstance<PostProcessResources>();owned.Add(resources);
        resources.shaders=new PostProcessResources.Shaders {screenSpaceReflections=native,copy=game.LoadAllAssets<Shader>().Single(s=>s.name=="Hidden/PostProcessing/Copy")};
        resources.computeShaders=new PostProcessResources.ComputeShaders {gaussianDownsample=gaussian};resources.blueNoise256=new[]{noise};
        AccessTools.Field(typeof(RuntimeUtilities),"s_Resources").SetValue(null,resources);
        var context=new PostProcessRenderContext {camera=camera,command=new CommandBuffer(),source=scene,destination=camera.targetTexture};
        AccessTools.Property(typeof(PostProcessRenderContext),"resources").SetValue(context,resources);
        AccessTools.Property(typeof(PostProcessRenderContext),"propertySheets").SetValue(context,factory);
        AccessTools.Property(typeof(PostProcessRenderContext),"sourceFormat").SetValue(context,RenderTextureFormat.ARGBHalf);return context;
    }
    private static void Count(int __4,bool __runOriginal) {if(__runOriginal && __4>=0 && __4<4)passes[__4]++;}
    private static Texture2D Texture(Color[] data) {var t=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.SetPixels(data);t.Apply();owned.Add(t);return t;}
    private static Color[] Read(RenderTexture t) {var r=AsyncGPUReadback.Request(t,0,TextureFormat.RGBAFloat);r.WaitForCompletion();if(r.hasError)throw new Exception("Readback failed");return r.GetData<Color>().ToArray();}
    private static double Error(Color[] a,Color[] b) => a.Select((v,i)=>Math.Max(Math.Max(Math.Abs(v.r-b[i].r),Math.Abs(v.g-b[i].g)),Math.Max(Math.Abs(v.b-b[i].b),Math.Abs(v.a-b[i].a)))).Max();
    private static void Fixture()
    {
        camera.aspect=(float)width/height;camera.targetTexture=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);camera.targetTexture.Create();owned.Add(camera.targetTexture);
        var projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);var d=new Color[width*height];var n=new Color[d.Length];var s=new Color[d.Length];var colors=new Color[d.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=y*width+x;var ray=camera.worldToCameraMatrix.MultiplyVector(camera.ScreenPointToRay(new Vector3(x+.5f,y+.5f,0)).direction).normalized;
            float wall=-8/ray.z,floor=ray.y<-.00001f?-1/ray.y:float.MaxValue;bool isFloor=floor<wall;var p=ray*(isFloor?floor:wall);
            var clip=projection*new Vector4(p.x,p.y,p.z,1);d[i]=new Color(clip.z/clip.w,0,0,0);
            var normal=camera.cameraToWorldMatrix.MultiplyVector(isFloor?Vector3.up:Vector3.forward);n[i]=new Color(normal.x*.5f+.5f,normal.y*.5f+.5f,normal.z*.5f+.5f,1);
            s[i]=new Color(.2f,.15f,.1f,isFloor?.98f:.4f);colors[i]=isFloor?new Color(.2f,.25f,.3f,.7f):(p.x<0?new Color(2,.2f,.1f,.7f):new Color(.1f,.4f,2,.7f));
        }
        depths=Texture(d);normals=Texture(n);specular=Texture(s);scene=Texture(colors);
    }
    private static void Globals(CommandBuffer cb)
    {
        cb.SetGlobalTexture("_CameraDepthTexture",depths);cb.SetGlobalTexture("_CameraGBufferTexture2",normals);cb.SetGlobalTexture("_CameraGBufferTexture1",specular);
        cb.SetGlobalTexture("_CameraGBufferTexture0",Texture2D.whiteTexture);cb.SetGlobalTexture("_StencilShadow",Texture2D.blackTexture);cb.SetGlobalTexture("_CameraMotionVectorsTexture",Texture2D.blackTexture);
        cb.SetGlobalFloat("_SSRFactor",1);cb.SetGlobalFloat("_ReflectionBottomShade",1);cb.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
        float f=camera.farClipPlane,n=camera.nearClipPlane;cb.SetGlobalVector("_ZBufferParams",SystemInfo.usesReversedZBuffer?new Vector4(f/n-1,1,(f/n-1)/f,1/f):new Vector4(1-f/n,f/n,(1-f/n)/f,1/n));
    }
}
