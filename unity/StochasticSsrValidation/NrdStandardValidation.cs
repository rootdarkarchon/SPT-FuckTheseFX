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
// Optional-provider ABI fixture: production discovers these entries by full type name.
namespace CloudSix.Source {
    internal enum FixtureTemporalMode { Off,Blend,Nubis,SkyDome }
    internal static class CloudConfig {
        public static ConfigEntry<bool> CustomSky,PublishSkyReflections;
        public static ConfigEntry<FixtureTemporalMode> TemporalMode;
    }
}
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
            ConfigContracts();RejectionDiagnosticsLifecycle();SyntheticParity();CloudSkyIntegration();CloudProbeIntegration();CapturedCloudSkyOrientation();Lifecycle();Routing();CapturedParity();
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
    private static void CloudSkyIntegration() {
        var config=new ConfigFile("cloud-fixture.cfg",false);config.SaveOnConfigSet=false;
        CloudSix.Source.CloudConfig.CustomSky=config.Bind("Sky","Custom Sky",true);
        CloudSix.Source.CloudConfig.PublishSkyReflections=config.Bind("Sky","Publish",true);
        CloudSix.Source.CloudConfig.TemporalMode=config.Bind("Clouds","Mode",CloudSix.Source.FixtureTemporalMode.SkyDome);
        var provider=new SsrSkySource();
        var map=new Texture2D(512,256,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};owned.Add(map);
        var pixels=new Color[512*256];
        for(int y=0;y<256;y++)for(int x=0;x<512;x++)pixels[y*512+x]=new Color(.5f+.25f*Mathf.Cos(2*Mathf.PI*(x+.5f)/512),(y+.5f)/256,.8f,1);
        map.SetPixels(pixels);map.Apply();Shader.SetGlobalTexture("_SsrSkyReflMap",map);
        Check(provider.Read()==map,"CloudSix global and live configuration activate sky source");
        CloudSix.Source.CloudConfig.CustomSky.Value=false;Check(provider.Read()==null,"Custom Sky off rejects still-bound stale map");CloudSix.Source.CloudConfig.CustomSky.Value=true;
        CloudSix.Source.CloudConfig.PublishSkyReflections.Value=false;Check(provider.Read()==null,"publish off rejects still-bound stale map");CloudSix.Source.CloudConfig.PublishSkyReflections.Value=true;
        foreach(var mode in new[]{CloudSix.Source.FixtureTemporalMode.Off,CloudSix.Source.FixtureTemporalMode.Blend,CloudSix.Source.FixtureTemporalMode.Nubis}) {
            CloudSix.Source.CloudConfig.TemporalMode.Value=mode;Check(provider.Read()==null,"non-publishing cloud mode rejects stale map: "+mode);
        }
        CloudSix.Source.CloudConfig.TemporalMode.Value=CloudSix.Source.FixtureTemporalMode.SkyDome;
        var released=new RenderTexture(512,256,0,RenderTextureFormat.ARGBHalf);owned.Add(released);released.Create();released.Release();
        Shader.SetGlobalTexture("_SsrSkyReflMap",released);Check(provider.Read()==null,"released cloud render texture is unavailable");
        var oldScene=scene;scene=Solid(new Color(.2f,.3f,.4f,.7f));
        using(var cb=new CommandBuffer())using(var p=new SsrReflectionPipeline(compute,blend,reflection,noise,native,gaussian)) {
            var o=Options();o.Freeze=true;o.Temporal=false;p.Ensure(width,height,128,RenderTextureFormat.ARGBHalf);
            void Render(){Globals(cb);TodGlobals(cb,.2f);p.Render(cb,camera,scene,camera.targetTexture,o,depths,normals,specular,true);Execute(cb);}
            Shader.SetGlobalTexture("_SsrSkyReflMap",null);Render();var record=Read(p.TracePipeline.Record);var raw=Read(p.TracePipeline.Radiance);
            Shader.SetGlobalTexture("_SsrSkyReflMap",map);Render();
            Check(Error(record,Read(p.TracePipeline.Record))==0 && Error(raw,Read(p.TracePipeline.Radiance))==0,"cloud provider leaves geometry/sky/miss tracing bit exact");
            Check(p.LastResetReason=="sky-provider or texture","provider activation resets NRD history");
            o.Temporal=true;Render();Render();Check(p.UsedHistory,"steady cloud texture keeps history");
            CloudSix.Source.CloudConfig.PublishSkyReflections.Value=false;Render();Check(!p.UsedHistory && p.LastResetReason=="sky-provider or texture","stale-provider rejection resets NRD history");
            CloudSix.Source.CloudConfig.PublishSkyReflections.Value=true;o.Temporal=false;
            Vector3 At(float y,float azimuth){float h=Mathf.Sqrt(1-y*y);return new Vector3(h*Mathf.Cos(azimuth),y,h*Mathf.Sin(azimuth));}
            var directions=new[]{At(0,0),At(.0625f,Mathf.PI*.5f),At(.25f,-Mathf.PI*.5f),At(.5625f,Mathf.PI),Vector3.up,At(.25f,-Mathf.PI*.5f+.00001f),At(.25f,-Mathf.PI*.5f-.00001f)};
            // Independently anchored publisher landmarks: +Z=u.5, +X=u.75,
            // -Z=u0/1 (the seam), -X=u.25. The test texture encodes cos(2pi*u).
            var expectedRed=new[]{.5f,.25f,.75f,.5f,.25f,.75f,.75f};
            int count=p.TracePipeline.Record.width*p.TracePipeline.Record.height;
            var records=new Color[count];var points=new Color[count];
            for(int j=0;j<count;j++) {
                int k=j%directions.Length;var d=camera.worldToCameraMatrix.MultiplyVector(directions[k]);
                records[j]=new Color(.5f,.5f,100,2);points[j]=new Color(d.x,d.y,d.z,.98f);
            }
            Globals(cb);TodGlobals(cb,.2f);
            cb.Blit(Sized(records,128,128),p.TracePipeline.Record);cb.Blit(Sized(points,128,128),p.TracePipeline.HitPoint);
            cb.Blit(Solid(new Color(.6f,2,0,0)),p.TracePipeline.Hit);cb.Blit(Solid(new Color(9,8,7,.75f)),p.TracePipeline.Radiance);
            cb.Blit(Solid(new Color(2,0,0,0)),p.TracePipeline.Depth[0]);
            // A published map must work without a TOD atmosphere model.
            cb.SetGlobalVector("TOD_kRadius",Vector4.zero);p.Reconstruct(cb,camera,scene,camera.targetTexture,o);Execute(cb);
            var resolved=Read(p.Resolved);var receiver=Read(p.ReceiverFallback.Radiance)[0];
            for(int k=0;k<directions.Length;k++) {
                var d=directions[k];var sky=new Color(expectedRed[k],Mathf.Sqrt(d.y),.8f,0);
                var expected=(receiver+sky*.75f)*.5f*.6f;expected.a=.6f;
                Check(Error(new[]{resolved[k]},new[]{expected})<.002,"cloud angular mapping, HDR coverage and bounded blend direction="+k);
            }
            Check(Error(new[]{resolved[5]},new[]{resolved[6]})<.0001,"cloud azimuth seam is continuous");
            Check(p.Nrd.Stats.Status==1,"CloudSix radiance passes through native NRD");
            records[0]=new Color(0,0,0,0);records[1]=new Color(0,0,0,1);points[2]=new Color(0,-1,0,.98f);
            Globals(cb);cb.SetGlobalVector("TOD_kRadius",Vector4.zero);cb.Blit(Sized(records,128,128),p.TracePipeline.Record);cb.Blit(Sized(points,128,128),p.TracePipeline.HitPoint);
            p.Reconstruct(cb,camera,scene,camera.targetTexture,o);Execute(cb);resolved=Read(p.Resolved);
            Check(resolved[0]==Color.clear && resolved[2]==Color.clear,"cloud map cannot fill unknown misses or downward sky without TOD");
            Check(Error(new[]{resolved[1]},new[]{new Color(4.05f,3.6f,3.15f,.6f)})<.004,"geometry resolve remains independent of cloud sky");
            o.View=ReflectionView.TodSkyRadiance;Globals(cb);p.Reconstruct(cb,camera,scene,camera.targetTexture,o);Execute(cb);Finite(Read(camera.targetTexture),"active cloud sky diagnostic");
            o.View=ReflectionView.Normal;o.CaptureContinuation=true;Render();AsyncGPUReadback.WaitAllRequests();
            var dir=Directory.GetDirectories("Capture-test/FTFX-SSR-Captures").OrderBy(x=>x).Last();
            var snapshot=(RenderTexture)AccessTools.Field(typeof(SsrReflectionPipeline),"skyCapture").GetValue(p);
            Check(snapshot.width==map.width && snapshot.height==map.height && Error(pixels,Read(snapshot))==0,"cloud capture snapshots exact map extent and linear pixels");
            Check(File.Exists(Path.Combine(dir,"cloud-sky.exr")) && File.ReadAllText(Path.Combine(dir,"settings.txt")).Contains("SkyProvider=CloudSixGlobal"),"cloud sky capture records provider and replay texture");
        }
        scene=oldScene;Shader.SetGlobalTexture("_SsrSkyReflMap",null);
    }
    private sealed class NativeProbeFixture {
        public int _faceNum=2,RenderedMask,Calls;
        public float _nextRenderTime=Time.realtimeSinceStartup+100,RenderDelay=10;
        public RenderTexture _cubeRT;
        public static readonly Color NativeColour=new Color(.03f,.04f,.05f,1);
        public void method_2() {
            if(_nextRenderTime>=Time.realtimeSinceStartup && RenderDelay!=0)return;
            RenderedMask=_faceNum<0?63:1<<(_faceNum==6?0:_faceNum);Calls++;
            using(var cb=new CommandBuffer()) {
                for(int f=0;f<6;f++)if((RenderedMask&(1<<f))!=0){cb.SetRenderTarget(_cubeRT,0,(CubemapFace)f);cb.ClearRenderTarget(false,true,NativeColour);}
                cb.GenerateMips(_cubeRT);Graphics.ExecuteCommandBuffer(cb);
            }
            Shader.SetGlobalTexture("_MyGlobalReflectionProbe",_cubeRT);_faceNum=(_faceNum<0?0:_faceNum==6?1:_faceNum+1);
            _nextRenderTime=Time.realtimeSinceStartup+RenderDelay;
        }
    }
    private static void CloudProbeIntegration() {
        var map=new Texture2D(512,256,TextureFormat.RGBAFloat,false,true){filterMode=FilterMode.Bilinear,wrapModeU=TextureWrapMode.Repeat,wrapModeV=TextureWrapMode.Clamp};owned.Add(map);
        var pixels=new Color[512*256];
        for(int y=0;y<256;y++)for(int x=0;x<512;x++) {
            float yy=Mathf.Pow((y+.5f)/256,2),h=Mathf.Sqrt(1-yy*yy),phi=((x+.5f)/512-.5f)*2*Mathf.PI;
            pixels[y*512+x]=new Color(.3f+.2f*h*Mathf.Sin(phi),.2f+.3f*yy,.5f+.2f*h*Mathf.Cos(phi),1);
        }
        map.SetPixels(pixels);map.Apply();Shader.SetGlobalTexture("_SsrSkyReflMap",map);
        var cube=new RenderTexture(64,64,0,RenderTextureFormat.ARGBHalf){dimension=TextureDimension.Cube,useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Trilinear};cube.Create();owned.Add(cube);
        var fixture=new NativeProbeFixture {_cubeRT=cube};var harmony=new Harmony("FTFX.CloudProbe.fixture");
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/CloudProbeAudit.shader");Check(shader!=null,"cloud probe sampling audit shader present");
        var audit=new Material(shader);owned.Add(audit);audit.SetTexture("_Probe",cube);
        var result=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat);result.Create();owned.Add(result);
        Color Sample(Vector3 direction,float lod=0){audit.SetVector("_Direction",new Vector4(direction.x,direction.y,direction.z,lod));Graphics.Blit(null,result,audit);return Read(result)[0];}
        Color Expected(Vector3 d){d.Normalize();return new Color(.3f+.2f*d.x,.2f+.3f*d.y,.5f+.2f*d.z,1);}
        using(var bridge=new CloudSkyProbe(reflection,typeof(NativeProbeFixture))) {
            bridge.Requested=()=>true;CloudSkyProbe.Active=bridge;
            harmony.Patch(bridge.Target,prefix:new HarmonyMethod(typeof(CloudSkyProbe),nameof(CloudSkyProbe.Before)),postfix:new HarmonyMethod(typeof(CloudSkyProbe),nameof(CloudSkyProbe.After)));
            try {
                fixture.method_2();Check(fixture.RenderedMask==63 && fixture.Calls==1,"cloud probe activation forces one complete native refresh");
                var directions=new[]{new Vector3(1,.5f,0),new Vector3(-1,.5f,0),new Vector3(0,.5f,1),new Vector3(0,.5f,-1),Vector3.up,new Vector3(.5f,1,.5f),new Vector3(-.5f,1,-.5f)};
                foreach(var d in directions)Check(Error(new[]{Sample(d)},new[]{Expected(d)})<.012,"native cubemap samples correct cloud direction "+d);
                foreach(var d in new[]{Vector3.down,new Vector3(1,-.5f,0),new Vector3(0,-.5f,-1)})Check(Error(new[]{Sample(d)},new[]{NativeProbeFixture.NativeColour})<.001,"cloud probe preserves native lower hemisphere "+d);
                var up=Sample(Vector3.up);Check(Error(new[]{Sample(Vector3.up,1)},new[]{up})<.01,"cloud probe regenerated roughness mips");
                fixture.method_2();Check(fixture.Calls==1,"steady cloud provider preserves native render delay");
                fixture._nextRenderTime=float.NegativeInfinity;fixture._faceNum=4;fixture.method_2();Check(fixture.Calls==2 && fixture.RenderedMask==16,"steady cloud provider uses native one-face update");
                CloudSix.Source.CloudConfig.CustomSky.Value=false;fixture.method_2();
                Check(fixture.Calls==3 && fixture.RenderedMask==63 && Error(new[]{Sample(Vector3.up)},new[]{NativeProbeFixture.NativeColour})<.001,"Custom Sky disable restores all native faces immediately");
                fixture.method_2();Check(fixture.Calls==3,"disabled cloud provider leaves native cadence unchanged");
                CloudSix.Source.CloudConfig.CustomSky.Value=true;fixture.method_2();Check(fixture.Calls==4 && Sample(Vector3.up).g>.4f,"Custom Sky re-enable restores bright native probe input");
                var replacement=new RenderTexture(32,32,0,RenderTextureFormat.ARGBHalf){dimension=TextureDimension.Cube,useMipMap=true,autoGenerateMips=false};replacement.Create();owned.Add(replacement);
                fixture._cubeRT=replacement;fixture.method_2();Check(fixture.Calls==5 && fixture.RenderedMask==63,"recreated probe receives complete cloud sky");
                Check(cube.IsCreated(),"cloud bridge never releases native probe ownership");
            } finally {harmony.UnpatchSelf();CloudSkyProbe.Active=null;CloudSix.Source.CloudConfig.CustomSky.Value=true;Shader.SetGlobalTexture("_SsrSkyReflMap",null);Shader.SetGlobalTexture("_MyGlobalReflectionProbe",null);}
        }
        Check(!ShaderUtil.ShaderHasError(shader),"cloud probe sampling audit compiles");
    }
    private static void CapturedCloudSkyOrientation() {
        const string folder="20261005-133533-123";
        var source=ReplayTexture(folder,"scene-source");var depth=ReplayTexture(folder,"trace-depth");var map=ReplayTexture(folder,"cloud-sky");
        map.filterMode=FilterMode.Bilinear;map.wrapModeU=TextureWrapMode.Repeat;map.wrapModeV=TextureWrapMode.Clamp;
        string root=Path.Combine(File.ReadAllText("game-path.txt").Trim(),"BepInEx/FTFX-SSR-Captures",folder);
        var settings=File.ReadAllLines(Path.Combine(root,"settings.txt"));
        var inverse=ReplayMatrix(settings.First(x=>x.StartsWith("RasterProjection="))).inverse;
        var world=ReplayMatrix(settings.First(x=>x.StartsWith("CameraToWorld=")));
        var colors=source.GetPixels();var depths=depth.GetPixels();var rays=new List<Vector3>();var expected=new List<Color>();
        // These are primary camera sky directions, independent of our map decoding.
        // The oracle is the cloud colour actually displayed in the captured frame.
        for(int y=source.height/2;y<source.height;y+=32)for(int x=16;x<source.width;x+=64) {
            int index=y*source.width+x;var h=inverse*new Vector4((x+.5f)/source.width*2-1,(y+.5f)/source.height*2-1,.5f,1);
            var ray=new Vector3(h.x,h.y,h.z).normalized;
            if(depths[index].r<9900 || world.MultiplyVector(ray).y<.02f)continue;
            rays.Add(ray);expected.Add(colors[index]);
        }
        Check(rays.Count>400,"captured visible sky supplies independent direction/colour oracle");
        var previousView=camera.worldToCameraMatrix;
        camera.worldToCameraMatrix=world.inverse;Shader.SetGlobalTexture("_SsrSkyReflMap",map);
        using(var cb=new CommandBuffer())using(var p=new SsrReflectionPipeline(compute,blend,reflection,noise,native,gaussian)) {
            var o=Options();o.Temporal=false;o.View=ReflectionView.TodSkyRadiance;p.Ensure(width,height,128,RenderTextureFormat.ARGBHalf);
            var points=new Color[128*128];
            for(int j=0;j<points.Length;j++){var ray=rays[j%rays.Count];points[j]=new Color(ray.x,ray.y,ray.z,.98f);}
            Globals(cb);cb.Blit(Sized(points,128,128),p.TracePipeline.HitPoint);
            cb.Blit(Solid(new Color(0,0,100,2)),p.TracePipeline.Record);cb.Blit(Solid(new Color(1,2,0,0)),p.TracePipeline.Hit);
            cb.Blit(Solid(new Color(0,0,0,1)),p.TracePipeline.Radiance);cb.Blit(Solid(new Color(2,0,0,0)),p.TracePipeline.Depth[0]);
            p.Reconstruct(cb,camera,scene,camera.targetTexture,o);Execute(cb);
            var actual=Read(camera.targetTexture);double sum=0;int count=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
                int tx=Math.Min(127,(int)((x+.5f)*128/width)),ty=Math.Min(127,(int)((y+.5f)*128/height));
                var a=actual[y*width+x];var e=expected[(ty*128+tx)%expected.Count];
                sum+=Math.Abs(a.r-e.r)+Math.Abs(a.g-e.g)+Math.Abs(a.b-e.b);count+=3;
            }
            Check(sum/count<.015,"CloudSix lookup matches captured displayed volumetric clouds; mean RGB error="+(sum/count));
        }
        camera.worldToCameraMatrix=previousView;Shader.SetGlobalTexture("_SsrSkyReflMap",null);
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
