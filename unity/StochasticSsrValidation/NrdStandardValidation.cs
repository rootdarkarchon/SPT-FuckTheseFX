using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using FuckTheseFX.Source.StochasticSSR;
using Before=FuckTheseFX.Source.StochasticSSR.PreNrdStandard;
namespace EFT.CameraControl { public class CameraManager { public static CameraManager Instance; public Camera Camera; } }
namespace FuckTheseFX { internal static class Plugin { internal const string Guid="com.rootdarkarchon.fuckthesefx"; internal static BepInEx.Logging.ManualLogSource MyLog=new BepInEx.Logging.ManualLogSource("SSR validation"); } }
public static class NrdStandardValidation {
    private static readonly List<string> checks=new List<string>();
    private static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private static Camera camera;
    private static Texture2D depths,normals,specular,scene,noise,materialOcclusion,whiteGBuffer;
    private static Shader native,blend,reflection;
    private static ComputeShader compute,gaussian;
    private static AssetBundle game;
    private static Type rendererType;
    private static int width=192,height=128;
    [System.Runtime.InteropServices.DllImport("FTFXNrd",CallingConvention=System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern IntPtr FTFX_NrdQueue(IntPtr id,IntPtr signal,IntPtr normal,IntPtr receiver,IntPtr motion,IntPtr output,ref NrdSpecularBackend.Frame frame);
    private static Options Options()=>new Options {Mode=SsrMode.Stochastic,Steps=350,Thickness=1,Smoothness=.4f,HistoryFrames=16,EdgePixels=0,FallbackStrength=.65f,Temporal=true,ContinuationSamples=350,ContinuationPixels=500,ContinuationDepth=200,ThicknessConfidenceFloor=1,ZeroSmoothnessProbe=true,ZeroSmoothnessProbeStrength=.3f,ZeroSmoothnessProbeClose=.1f,ZeroSmoothnessProbeFadeStart=5,ZeroSmoothnessProbeFadeEnd=15};
    private static Before.Options Baseline(Options o) {
        object b=new Before.Options();
        foreach(var f in typeof(Options).GetFields(BindingFlags.Instance|BindingFlags.NonPublic)) {
            var target=typeof(Before.Options).GetField(f.Name,BindingFlags.Instance|BindingFlags.NonPublic);
            if(target==null)continue;object value=f.GetValue(o);
            if(f.FieldType.IsEnum)value=Enum.ToObject(target.FieldType,(int)value);
            target.SetValue(b,value);
        }
        var result=(Before.Options)b;result.NrdSpecular=true;result.BypassHitNormal=true;result.GlobalContinuedSky=true;result.LimitNativeRange=false;result.WaterNormals=false;result.UseCapturedContribution=false;return result;
    }
    private static void Finite(Color[] pixels,string name) { Check(pixels.All(v=>!float.IsNaN(v.r+v.g+v.b+v.a) && !float.IsInfinity(v.r+v.g+v.b+v.a)),name+" finite"); }
    public static void Run() {
        try {
            AccessTools.Property(typeof(Paths),"BepInExConfigPath").SetValue(null,Path.GetFullPath("BepInEx-test.cfg"));
            AccessTools.Property(typeof(Paths),"ConfigPath").SetValue(null,Directory.GetCurrentDirectory());
            AccessTools.Property(typeof(Paths),"BepInExRootPath").SetValue(null,Path.GetFullPath("Capture-test"));
            compute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/HiZ.compute");blend=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Support.shader");reflection=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Reflection.shader");
            NrdSpecularBackend.ValidationShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/NrdInputs.shader");NrdSpecularBackend.ValidationLibrary=Path.GetFullPath("Native/FTFXNrd.dll");
            Before.NrdSpecularBackend.ValidationShader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/NrdInputs.shader");Before.NrdSpecularBackend.ValidationLibrary=NrdSpecularBackend.ValidationLibrary;
            noise=new Texture2D(128,128,TextureFormat.RG16,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};owned.Add(noise);noise.LoadRawTextureData(File.ReadAllBytes("Assets/AMD/BlueNoise.bytes"));noise.Apply();
            game=AssetBundle.LoadFromFile(Path.Combine(File.ReadAllText("game-path.txt").Trim(),"EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"));
            native=game.LoadAllAssets<Shader>().Single(s=>s.name=="Hidden/PostProcessing/ScreenSpaceReflections");gaussian=game.LoadAllAssets<ComputeShader>().First(c=>c.name.IndexOf("Gaussian",StringComparison.OrdinalIgnoreCase)>=0);
            var go=new GameObject("NRD standard fixture");owned.Add(go);camera=go.AddComponent<Camera>();camera.enabled=false;camera.renderingPath=RenderingPath.DeferredShading;camera.allowHDR=true;camera.nearClipPlane=.1f;camera.farClipPlane=500;camera.fieldOfView=65;
            rendererType=AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.ScreenSpaceReflectionsRenderer");
            var factory=new PropertySheetFactory();var context=Context(factory);context.command.Dispose();factory.Release();Fixture();
            ConfigContracts();RejectionDiagnosticsLifecycle();SyntheticParity();Lifecycle();Routing();CapturedParity();
            foreach(var sh in new[]{reflection,NrdSpecularBackend.ValidationShader,blend})Check(!ShaderUtil.GetShaderMessages(sh).Any(m=>m.severity.ToString()=="Error"),"executed shader compiles: "+sh.name);
            Check(!ShaderUtil.GetComputeShaderMessages(compute).Any(m=>m.severity.ToString()=="Error"),"executed Hi-Z variants compile");
            var ssr=new AssetBundleBuild {assetBundleName="stochasticssr",assetNames=new[]{"Assets/HiZ.compute","Assets/Support.shader","Assets/Reflection.shader","Assets/WaterCapture.shader","Assets/AMD/BlueNoise.bytes"}};
            var nrd=new AssetBundleBuild {assetBundleName="nrdssr",assetNames=new[]{"Assets/NrdInputs.shader"}};
            Check(BuildPipeline.BuildAssetBundles("Output",new[]{ssr,nrd},BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64)!=null,"release bundles build");
            File.WriteAllLines("validation.txt",checks);
        } catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);throw;}
        finally {foreach(var x in owned)if(x!=null)UnityEngine.Object.DestroyImmediate(x);if(game!=null)game.Unload(true);}
    }
    private static void Check(bool value,string text) { if(!value)throw new Exception(text);checks.Add(text);Debug.Log("PASS "+text); }

    private static void Execute(CommandBuffer cb) { Graphics.ExecuteCommandBuffer(cb);cb.Clear(); }

    private static Texture2D Solid(Color c){var t=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);t.SetPixel(0,0,c);t.Apply();owned.Add(t);return t;}

    private static Texture2D Sized(Color[] pixels,int w,int h) {
        var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};t.SetPixels(pixels);t.Apply();owned.Add(t);return t;
    }

    private static Texture2D Texture(Color[] data) {var t=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};t.SetPixels(data);t.Apply();owned.Add(t);return t;}

    private static Color[] Read(RenderTexture t) {var r=AsyncGPUReadback.Request(t,0,TextureFormat.RGBAFloat);r.WaitForCompletion();if(r.hasError)throw new Exception("Readback failed");return r.GetData<Color>().ToArray();}

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
        if(whiteGBuffer==null || whiteGBuffer.width!=width || whiteGBuffer.height!=height)whiteGBuffer=Texture(Enumerable.Repeat(Color.white,width*height).ToArray());
        cb.SetGlobalTexture("_CameraGBufferTexture0",materialOcclusion != null ? materialOcclusion : whiteGBuffer);cb.SetGlobalTexture("_StencilShadow",Texture2D.blackTexture);cb.SetGlobalTexture("_CameraMotionVectorsTexture",Texture2D.blackTexture);
        cb.SetGlobalFloat("_SSRFactor",1);cb.SetGlobalFloat("_ReflectionBottomShade",1);cb.SetGlobalVector("_WorldSpaceCameraPos",camera.transform.position);
        cb.SetGlobalVector("_ProjectionParams",new Vector4(1,camera.nearClipPlane,camera.farClipPlane,1/camera.farClipPlane));
        cb.SetGlobalVector("_ScreenParams",new Vector4(width,height,1+1f/width,1+1f/height));cb.SetGlobalVector("_Time",Vector4.zero);
        float f=camera.farClipPlane,n=camera.nearClipPlane;cb.SetGlobalVector("_ZBufferParams",SystemInfo.usesReversedZBuffer?new Vector4(f/n-1,1,(f/n-1)/f,1/f):new Vector4(1-f/n,f/n,(1-f/n)/f,1/n));
    }

    private static void TodGlobals(CommandBuffer cb,float fogginess)
    {
        cb.SetGlobalVector("TOD_SunSkyColor",new Vector4(.7f,.8f,1,0));cb.SetGlobalVector("TOD_MoonSkyColor",new Vector4(.01f,.02f,.04f,0));
        cb.SetGlobalVector("TOD_GroundColor",new Vector4(.04f,.03f,.02f,0));cb.SetGlobalVector("TOD_MoonHaloColor",new Vector4(.01f,.02f,.03f,0));
        cb.SetGlobalVector("TOD_LocalSunDirection",new Vector4(0,.8f,.6f,0));cb.SetGlobalVector("TOD_LocalMoonDirection",new Vector4(0,-.8f,-.6f,0));
        cb.SetGlobalVector("TOD_kBetaMie",new Vector4(.1f,1.25f,-1,0));cb.SetGlobalVector("TOD_kSun",new Vector4(.1f,.2f,.3f,.01f));
        cb.SetGlobalVector("TOD_k4PI",new Vector4(.1f,.2f,.3f,.02f));cb.SetGlobalVector("TOD_kRadius",new Vector4(1,1,1.025f,1.025f*1.025f));
        cb.SetGlobalVector("TOD_kScale",new Vector4(40,.25f,160,.0001f));cb.SetGlobalFloat("TOD_Contrast",1.1f);
        cb.SetGlobalFloat("TOD_ScatteringBrightness",1.2f);cb.SetGlobalFloat("TOD_Fogginess",fogginess);cb.SetGlobalFloat("TOD_MoonHaloPower",8);
        cb.SetGlobalMatrix("TOD_World2Sky",Matrix4x4.identity);
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

    private static PostProcessEffectRenderer CreateRenderer(out ScreenSpaceReflections effect)
    {
        var renderer=(PostProcessEffectRenderer)Activator.CreateInstance(rendererType);
        effect=ScriptableObject.CreateInstance<ScreenSpaceReflections>();owned.Add(effect);
        effect.preset.value=(ScreenSpaceReflectionPreset)7;effect.resolution.value=(ScreenSpaceReflectionResolution)1;
        effect.maximumIterationCount.value=128;effect.cutOffThreshold.value=.5f;
        AccessTools.Method(rendererType,"SetSettings").Invoke(renderer,new object[]{effect});return renderer;
    }

    private static double Error(Color[] a,Color[] b)=>a.Select((v,i)=>Math.Max(Math.Max(Math.Abs(v.r-b[i].r),Math.Abs(v.g-b[i].g)),Math.Max(Math.Abs(v.b-b[i].b),Math.Abs(v.a-b[i].a)))).Max();


    private static void ConfigContracts() {
        File.WriteAllText("defaults.cfg", "");var c=new ConfigFile("defaults.cfg",false);c.SaveOnConfigSet=false;var settings=new SsrSettings(c);
        Check(c.Count==25 && !settings.Enabled.Value && settings.Steps.Value==350 && settings.ContinuationSamples.Value==350 && settings.HistoryFrames.Value==8 && settings.Read().HistoryFrames==8 && settings.ReceiverCutoff.Value==40 && settings.FallbackStrength.Value==65 && settings.CutoffEnvironmentBlend.Value==25,"fresh integer defaults, eight-frame runtime history and 25 SSR settings");
        Check(settings.Read().ReceiverCutoff==.4f && settings.Read().FallbackStrength==.65f && settings.Read().CutoffEnvironmentBlend==.25f,"SSR percentages resolve once");
        settings.HistoryFrames.Value=32;Check(settings.Read().HistoryFrames==32,"32-frame history setting");
        settings.FallbackStrength.Value=37;c.Save();
        var saved=new SsrSettings(new ConfigFile("defaults.cfg",false));Check(saved.FallbackStrength.Value==37,"new SSR percentage persists");
        Check(!typeof(SsrReflectionPipeline).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Any(f=>f.Name.StartsWith("History")||f.Name.Contains("Anchor")||f.Name=="DepthGapFilled"||f.Name=="exactResidual"),"no custom GGX histories, display attachment or exact residual remain");
    }
    private static void RejectionDiagnosticsLifecycle() {
        using(var cb=new CommandBuffer())using(var p=new SsrPipeline(compute,blend,noise)) {
            var o=Options();o.Freeze=true;p.Ensure(width,height,128,RenderTextureFormat.ARGBHalf);
            Check(p.Ready && p.TraceStop==null,"normal allocation ready without rejection diagnostics");
            Check(p.Raster.Buffer.count==16,"raster buffer contains only four current matrices");
            void Trace(bool diagnostics=false,bool preserve=false,bool traversal=false) {Globals(cb);p.Trace(cb,camera,scene,o,depths,normals,specular,diagnostics,preserve,traversal);Execute(cb);}
            Trace();var record=Read(p.Record);var raw=Read(p.Radiance);var info=Read(p.TraceInfo);
            Check(p.TraceStop==null && p.Sample==null && p.DepthHit==null,"normal trace has no diagnostic UAV allocations");
            Trace(true);var rejected=p.TraceStop;Check(rejected!=null && rejected.IsCreated(),"explicit diagnostics allocate first rejection target");
            Check(Error(record,Read(p.Record))==0 && Error(raw,Read(p.Radiance))==0 && Error(info,Read(p.TraceInfo))==0,"diagnostic variant preserves exact rendering and continuation state");
            var rejectedData=Read(rejected);Trace(false,true);Check(p.TraceStop==rejected && rejected.IsCreated() && Error(rejectedData,Read(rejected))==0,"pending capture preservation keeps rejection data unchanged");
            Trace();Check(p.TraceStop==null && rejected==null && p.Ready,"normal trace retires diagnostic target without invalidating pipeline");
            o.View=ReflectionView.RejectionLocation;Trace();Check(p.TraceStop!=null && p.Sample==null && p.DepthHit==null,"rejection-location view allocates only its diagnostic target");
            o.View=ReflectionView.Normal;o.Freeze=false;Trace();int phase=p.LastSamplePhase;Trace(true);Check(p.LastSamplePhase==phase+1,"diagnostic toggle does not reset ray sequence");
            p.TraceStop.Release();Trace(true);Check(p.TraceStop.IsCreated(),"lost rejection target recreated");
            o.Freeze=true;Trace();record=Read(p.Record);raw=Read(p.Radiance);info=Read(p.TraceInfo);Trace(true,false,true);
            Check(Error(record,Read(p.Record))==0 && Error(raw,Read(p.Radiance))==0 && Error(info,Read(p.TraceInfo))==0,"traversal diagnostic variant preserves exact rendering and continuation state");
            Check(p.DepthHit.format==RenderTextureFormat.ARGBFloat && Read(p.Sample).Any(v=>v.r>0),"traversal capture emits work counters with optional rejection UAV");
        }
    }
    private static void Parity(string label,int size,int frames,Options o,Action<int> animate=null,bool diagnostics=false) {
        var oldCompute=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/BeforeNrdStandard/HiZ.compute");var oldReflection=AssetDatabase.LoadAssetAtPath<Shader>("Assets/BeforeNrdStandard/Reflection.shader");
        using(var cb=new CommandBuffer())using(var a=new Before.ConfidenceSsrPipeline(oldCompute,blend,oldReflection,noise,native,gaussian))using(var b=new SsrReflectionPipeline(compute,blend,reflection,noise,native,gaussian)) {
            a.Ensure(width,height,size,RenderTextureFormat.ARGBHalf);b.Ensure(width,height,size,RenderTextureFormat.ARGBHalf);
            var beforeTarget=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf);beforeTarget.Create();owned.Add(beforeTarget);
            for(int frame=0;frame<frames;frame++) {
                animate?.Invoke(frame);
                if(frame==frames/2 && frames>4){a.ResetHistory();b.ResetHistory();}
                AccessTools.Field(typeof(Before.ConfidenceSsrPipeline),"lastTime").SetValue(a,Time.realtimeSinceStartup);
                Globals(cb);TodGlobals(cb,.2f);a.Render(cb,camera,scene,beforeTarget,Baseline(o),depths,normals,specular,diagnostics);Execute(cb);
                AccessTools.Field(typeof(SsrReflectionPipeline),"lastTime").SetValue(b,Time.realtimeSinceStartup);
                Globals(cb);TodGlobals(cb,.2f);b.Render(cb,camera,scene,camera.targetTexture,o,depths,normals,specular,diagnostics);Execute(cb);
                Check(a.Nrd.Stats.Status==1 && b.Nrd.Stats.Status==1 && b.Nrd.Stats.Failures==0,label+" native NRD dispatched frame "+frame);
                var resources=new[]{(Read(a.TracePipeline.Record),Read(b.TracePipeline.Record),"classification"),(Read(a.TracePipeline.Radiance),Read(b.TracePipeline.Radiance),"raw"),(Read(a.Resolved),Read(b.Resolved),"resolve"),(Read(a.Nrd.Output),Read(b.Nrd.Output),"NRD"),(Read(beforeTarget),Read(camera.targetTexture),"composition")};
                foreach(var row in resources){double error=Error(row.Item1,row.Item2);Check(error==0,label+" exact "+row.Item3+" frame="+frame+" max="+error+" phases="+a.TracePipeline.LastSamplePhase+","+b.TracePipeline.LastSamplePhase);Finite(row.Item2,label+" "+row.Item3);}
                Check(diagnostics?b.TracePipeline.TraceStop!=null:b.TracePipeline.TraceStop==null,label+" optional first-rejection allocation frame="+frame);
                if(diagnostics)Check(Error(Read(a.TracePipeline.TraceStop),Read(b.TracePipeline.TraceStop))==0,label+" exact first rejection frame="+frame);
                Check(Read(b.Resolved).Any(v=>v.a>0),label+" nonzero measured signal");
            }
        }
    }
    private static void SyntheticParity() {
        Parity("diagnostics-on",128,3,Options(),diagnostics:true);
        var original=specular.GetPixels();var oldNormals=normals;var oldDepth=depths;
        var probe=new Cubemap(8,TextureFormat.RGBAFloat,true);owned.Add(probe);
        for(int face=0;face<6;face++)for(int mip=0;mip<probe.mipmapCount;mip++){int n=Math.Max(1,8>>mip);probe.SetPixels(Enumerable.Repeat(new Color(.2f+face*.03f,.3f,.4f,1),n*n).ToArray(),(CubemapFace)face,mip);}probe.Apply();Shader.SetGlobalTexture("_MyGlobalReflectionProbe",probe);
        foreach(var reconstruction in new[]{ReflectionReconstruction.Custom,ReflectionReconstruction.NativeGaussian})foreach(bool temporal in new[]{false,true}) {
            var o=Options();o.Reconstruction=reconstruction;o.Temporal=temporal;o.Freeze=false;o.SupportSmoothness=.5f;
            var data=original.Select((v,j)=>new Color(v.r,v.g,v.b,j%17==0?0:j%13==0?.2f:v.a>=.5f?.72f:v.a)).ToArray();specular=Texture(data);
            Parity("rough/excluded/probe "+reconstruction+" temporal="+temporal,128,8,o);
        }
        specular=Texture(original);var d=depths.GetPixels();for(int j=0;j<d.Length;j++)if(original[j].a<.5f)d[j]=new Color(SystemInfo.usesReversedZBuffer?0:1,0,0,0);depths=Texture(d);
        var sky=Options();sky.Freeze=false;Parity("sky/geometry transition",128,8,sky);
        var baseNormals=normals.GetPixels();
        Parity("animated launch normals",128,12,Options(),frame=>{float t=.08f*Mathf.Sin(frame*.7f);normals=Texture(baseNormals.Select(v=>{var n=new Vector3(v.r*2-1+t,v.g*2-1,v.b*2-1).normalized;return new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f,1);}).ToArray());});
        normals=oldNormals;depths=oldDepth;specular=Texture(original);
    }
    private static void Lifecycle() {
        using(var cb=new CommandBuffer())using(var p=new SsrReflectionPipeline(compute,blend,reflection,noise,native,gaussian)) {
            var o=Options();p.Ensure(width,height,128,RenderTextureFormat.ARGBHalf);
            void Render(){Globals(cb);TodGlobals(cb,.2f);p.Render(cb,camera,scene,camera.targetTexture,o,depths,normals,specular);Execute(cb);Check(p.Nrd.Stats.Status==1,"lifecycle NRD success");}
            Render();o.Temporal=false;Render();o.Temporal=true;foreach(int frames in new[]{1,16,32}){o.HistoryFrames=frames;Render();}o.NrdAntiFirefly=true;Render();o.NrdAntiFirefly=false;Render();
            var snapshot=Read(camera.targetTexture);Globals(cb);TodGlobals(cb,.2f);cb.Blit(scene,camera.targetTexture);p.ResetHistory();p.Render(cb,camera,camera.targetTexture,camera.targetTexture,o,depths,normals,specular);Execute(cb);Finite(Read(camera.targetTexture),"alias result");
            o.CaptureContinuation=true;Globals(cb);TodGlobals(cb,.2f);p.Render(cb,camera,scene,camera.targetTexture,o,depths,normals,specular);
            var capture=(ContinuationCapture)AccessTools.Field(typeof(SsrReflectionPipeline),"continuationCapture").GetValue(p);var detached=p.Resolved;var detachedRejection=p.TracePipeline.TraceStop;p.Ensure(width,height,96,RenderTextureFormat.ARGBHalf);
            Check(capture.Pending && detached.IsCreated() && detachedRejection.IsCreated() && p.TracePipeline.TraceStop==null,"resize retains pending readback targets including rejection diagnostics");Execute(cb);AsyncGPUReadback.WaitAllRequests();Check(!capture.Pending && detached==null && detachedRejection==null,"capture completes and retires resized targets including rejection diagnostics");
            var dir=Directory.GetDirectories("Capture-test/FTFX-SSR-Captures").OrderBy(x=>x).Last();Check(File.Exists(Path.Combine(dir,"nrd-display.exr")) && File.ReadAllText(Path.Combine(dir,"settings.txt")).Contains("Denoiser=NRD_REBLUR_SPECULAR"),"NRD diagnostics saved");
            Check(!Directory.GetFiles(dir).Any(x=>Path.GetFileName(x).StartsWith("previous-")||Path.GetFileName(x)=="temporal.exr"||Path.GetFileName(x)=="depth-gap-display.exr"),"removed histories absent from capture");
            o.CaptureContinuation=false;o.CaptureTraversal=true;Render();AsyncGPUReadback.WaitAllRequests();
            dir=Directory.GetDirectories("Capture-test/FTFX-SSR-Captures").OrderBy(x=>x).Last();
            Check(!capture.Pending && File.Exists(Path.Combine(dir,"traversal-work.exr")) && File.Exists(Path.Combine(dir,"first-rejection.exr")),"traversal mode rearm saves work and first rejection diagnostics");
            o.CaptureTraversal=false;Render();Check(p.TracePipeline.TraceStop==null,"capture disable releases rejection diagnostics");p.Nrd.Output.Release();Render();Check(p.Nrd.Output.IsCreated(),"lost NRD target recovers");
            p.PrepareRaster(camera,2,width,height);Globals(cb);TodGlobals(cb,.2f);p.Render(cb,camera,scene,camera.targetTexture,o,depths,normals,specular);p.TracePipeline.Raster.FinalizeRaster(camera);AccessTools.Method(typeof(NrdSpecularBackend),"FinalizeRaster").Invoke(p.Nrd,new object[]{camera});Execute(cb);p.TracePipeline.Raster.Commit();Check(p.Nrd.Stats.Status==1,"late raster packet executes");
        }
    }
    private static void Routing() {
        using(var hooks=new SsrHooks(rendererType,compute,blend,reflection,noise)){
            var factory=new PropertySheetFactory();
            string failure="";hooks.Error=e=>failure=e;hooks.IsMainCamera=c=>c==camera;var o=Options();hooks.Settings=()=>o;SsrHooks.Active=hooks;var renderer=CreateRenderer(out var effect);var context=Context(factory);
            try {
                o.Mode=SsrMode.Native;Check(SsrHooks.Before(renderer,context,out var state)&&state==null,"disabled custom SSR retains native renderer");
                o.Mode=SsrMode.Stochastic;hooks.IsMainCamera=c=>false;Check(SsrHooks.Before(renderer,context,out state),"optics/non-main retain native renderer");hooks.IsMainCamera=c=>true;
                AccessTools.Field(typeof(NrdSpecularBackend),"unavailable").SetValue(null,true);Check(SsrHooks.Before(renderer,context,out state)&&state==null&&failure.Contains("NRD backend unavailable"),"missing NRD returns native without custom denoiser fallback");
                AccessTools.Field(typeof(NrdSpecularBackend),"unavailable").SetValue(null,false);hooks.Suspend();
                Check(!SsrHooks.Before(renderer,context,out state)&&state!=null,"available NRD replaces native SSR");state.Pipeline.TracePipeline.Raster.FinalizeRaster(camera);AccessTools.Method(typeof(NrdSpecularBackend),"FinalizeRaster").Invoke(state.Pipeline.Nrd,new object[]{camera});Execute(context.command);Check(state.Pipeline.Nrd.Stats.Status==1,"live hook executes NRD");
                var handle=(IntPtr)AccessTools.Field(typeof(NrdSpecularBackend),"handle").GetValue(state.Pipeline.Nrd);var invalid=new NrdSpecularBackend.Frame();
                Check(FTFX_NrdQueue(handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,ref invalid)==IntPtr.Zero,"invalid native input is safely rejected");
                Check(SsrHooks.Before(renderer,context,out state) && state==null && failure.Contains("NRD dispatch failed"),"failed NRD dispatch restores native SSR");hooks.Suspend();
                Check(!SsrHooks.Before(renderer,context,out state),"suspend/re-enable recovers failed NRD");state.Pipeline.TracePipeline.Raster.FinalizeRaster(camera);AccessTools.Method(typeof(NrdSpecularBackend),"FinalizeRaster").Invoke(state.Pipeline.Nrd,new object[]{camera});Execute(context.command);Check(state.Pipeline.Nrd.Stats.Status==1,"recreated NRD executes after failure");hooks.Suspend();
            } finally {context.command.Dispose();renderer.Release();factory.Release();AccessTools.Field(typeof(NrdSpecularBackend),"unavailable").SetValue(null,false);}
        }
    }
    private static Texture2D ReplayTexture(string folder,string name) {
        using(var r=new BinaryReader(File.OpenRead(Path.Combine("Replay",folder,name+".rgba32f")))){int w=r.ReadInt32(),h=r.ReadInt32();var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Point};t.LoadRawTextureData(r.ReadBytes(checked(w*h*16)));t.Apply();owned.Add(t);return t;}
    }
    private static Matrix4x4 ReplayMatrix(string line){var values=line.Substring(line.IndexOf('=')+1).Split(',').Select(x=>float.Parse(x,System.Globalization.CultureInfo.InvariantCulture)).ToArray();var m=new Matrix4x4();for(int i=0;i<16;i++)m[i]=values[i];return m;}
    private static void CapturedParity() {
        foreach(string folder in new[]{"20261002-231147-581","20261003-032313-165","20261002-215552-280"}) {
            string root=Path.Combine(File.ReadAllText("game-path.txt").Trim(),"BepInEx/FTFX-SSR-Captures",folder);var settings=File.ReadAllLines(Path.Combine(root,"settings.txt"));var projection=ReplayMatrix(settings.First(x=>x.StartsWith("RasterProjection=")));
            scene=ReplayTexture(folder,"scene-source");normals=ReplayTexture(folder,"gbuffer2");specular=ReplayTexture(folder,"gbuffer1");materialOcclusion=ReplayTexture(folder,"gbuffer0");width=scene.width;height=scene.height;
            var eye=ReplayTexture(folder,"trace-depth");var z=eye.GetPixels();camera.nearClipPlane=projection.m23/(1+projection.m22);camera.farClipPlane=z.Max(v=>v.r);camera.aspect=projection.m11/projection.m00;
            var cp=projection;cp.SetRow(2,projection.GetRow(3)-2*projection.GetRow(2));camera.projectionMatrix=cp;camera.nonJitteredProjectionMatrix=cp;camera.worldToCameraMatrix=ReplayMatrix(settings.First(x=>x.StartsWith("CameraToWorld="))).inverse;
            depths=Texture(z.Select(v=>{var c=projection*new Vector4(0,0,-v.r,1);return new Color(c.z/c.w,0,0,0);}).ToArray());camera.targetTexture=new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf);camera.targetTexture.Create();owned.Add(camera.targetTexture);
            using(var a=new WaterReflectionCapture(AssetDatabase.LoadAssetAtPath<Shader>("Assets/WaterCapture.shader"),false){Requested=()=>true,IsMainCamera=c=>c==camera})
            using(var b=new Before.WaterReflectionCapture(AssetDatabase.LoadAssetAtPath<Shader>("Assets/BeforeNrdStandard/WaterCapture.shader"),false){Requested=()=>true,IsMainCamera=c=>c==camera})using(var cb=new CommandBuffer()) {
                var water=ReplayTexture(folder,"water");cb.Blit(water,WaterReflectionCapture.Get(camera,width,height));cb.Blit(water,Before.WaterReflectionCapture.Get(camera,width,height));Execute(cb);
                Parity("captured "+folder,768,20,Options());
            }
        }
    }
}
