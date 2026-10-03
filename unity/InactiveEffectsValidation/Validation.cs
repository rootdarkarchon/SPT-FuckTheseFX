using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FuckTheseFX.Source.PostEffects;
using Object = UnityEngine.Object;

namespace FuckTheseFX
{
    internal sealed class TestSetting { internal bool Value = true; }
    internal sealed class TestLog { internal void LogInfo(object v) => Debug.Log(v); }
    internal static class Plugin
    {
        internal static TestSetting SkipInactiveEffects = new TestSetting();
        internal static TestLog MyLog = new TestLog();
    }
}
namespace FuckTheseFX.Source.PostEffects
{
    // The production contract checks modules of live game types. Surrogates
    // instead verify the same installed module IDs read directly with Cecil.
    internal static class InactiveEffectsContracts
    {
        internal static void Validate()
        {
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
            var files=new[]{"game-assembly.txt","firstpass-assembly.txt","runtime-assembly.txt"};
            var ids=new[]{"cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7","79ed6e2f-9190-49a0-a4ee-138078f70dc9","bb1716ca-81b5-4288-9a04-95ef431a6ddf"};
            for(int i=0;i<files.Length;i++)using(var a=AssemblyDefinition.ReadAssembly(File.ReadAllText(Path.Combine(root,files[i])).Trim()))
                if(a.MainModule.Mvid!=new Guid(ids[i]))throw new Exception("Native fixture ABI changed");
        }
    }
}

// Callback surrogates preserve native wrapper structure and neutral predicates.
// The SSAA selector/release bodies are replaced below with installed native IL.
public class SSAAPropagator : MonoBehaviour
{
    public RenderTexture[] _resampledColorTargetHDR, _resampledColorTargetLDR;
    public int _currentDestinationHDR, _currentDestinationLDR;
    public bool _HDRSourceDestination;
    [MethodImpl(MethodImplOptions.NoInlining)] public void FillCommandBuffer(CommandBuffer buffer, RenderTargetIdentifier source, RenderTargetIdentifier destination) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool GetSourceDestination(out RenderTexture source, out RenderTexture destination) { source = destination = null; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool GetSourceDestinationHDR(out RenderTexture source, out RenderTexture destination) { source = destination = null; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool GetSourceDestinationLDR(out RenderTexture source, out RenderTexture destination) { source = destination = null; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool GetSourceDestinationHDRToLDR(out RenderTexture source, out RenderTexture destination) { source = destination = null; return false; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void ReleaseSourceDestination(RenderTargetIdentifier source, RenderTargetIdentifier destination) { }
}
[ExecuteAlways] public class SSAAImpl : MonoBehaviour
{
    public SSAAPropagator _ssaaPropagator;
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (_ssaaPropagator.GetSourceDestination(out var current, out var next)) source = current;
        Graphics.Blit(source, destination);
    }
}
public class StatusFixture : MonoBehaviour
{
    public SSAAPropagator _ssaaPropagator;
    public int copies, activeDraws;
    protected void Render(RenderTexture source, RenderTexture destination, bool neutral)
    {
        var proxy = destination;
        if (_ssaaPropagator != null) _ssaaPropagator.GetSourceDestination(out source, out destination);
        if (neutral) { copies++; Graphics.Blit(source, destination); }
        else
        {
            activeDraws++;
            Graphics.Blit(source, destination, InactiveEffectsValidation.statusMaterial);
        }
        if (_ssaaPropagator != null) _ssaaPropagator.ReleaseSourceDestination(source, destination);
        // Do not write Unity's proxy: just like the installed redirected wrappers.
    }
}
[ExecuteAlways] public class DeathFade : StatusFixture
{
    public float _closeEyesValue, _fadeValue;
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
        => Render(source,destination,Math.Abs(_closeEyesValue)<Mathf.Epsilon && Math.Abs(_fadeValue)<Mathf.Epsilon);
}
[ExecuteAlways] public class BloodOnScreen : StatusFixture
{
    public float _lastHitTime, MaxBloodTime;
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
        => Render(source,destination,Time.time-_lastHitTime>MaxBloodTime);
}
public class Posteffect : StatusFixture { public float _effectAmount; }
[ExecuteAlways] public class PostprocessGrayscale : Posteffect
{
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
        => Render(source,destination,Math.Abs(_effectAmount)<Mathf.Epsilon);
}
[ExecuteAlways] public class DistortCameraFX : StatusFixture
{
    public bool _doNotRender;
    public int preCulls;
    public void OnPreCull() { preCulls++; }
    public void OnRenderImage(RenderTexture source, RenderTexture destination) => Render(source,destination,_doNotRender);
}
[ExecuteAlways] public class InfectionEffect : StatusFixture
{
    public float _effectValue;
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        _effectValue=Mathf.Clamp01(_effectValue); Render(source,destination,!(_effectValue>0));
    }
}
namespace UnityStandardAssets.ImageEffects
{
    public class PostEffectsBase : MonoBehaviour
    {
        protected SSAAPropagator _ssaaPropagator;
        public void Bind(SSAAPropagator propagator) { _ssaaPropagator=propagator; }
    }
    public class DepthOfField : PostEffectsBase
    {
        public float aperture, focalSize=.05f, focalDistance01=.3f, internalBlurWidth=2;
        public bool visualizeFocus, nearBlur, highResolution;
        public Shader dofHdrShader;
        public int allocations, cocDraws, blurDraws;
        public void WriteCoc(RenderTexture source, bool foreground)
        {
            cocDraws++;
            var material=InactiveEffectsValidation.dofMaterial;
            material.SetVector("_CurveParams",new Vector4(1,focalSize,aperture/10,focalDistance01));
            source.MarkRestoreExpected(); Graphics.Blit(source,source,material,0);
        }
        public void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_ssaaPropagator!=null) _ssaaPropagator.GetSourceDestination(out source,out destination);
            WriteCoc(source,true);
            var half=RenderTexture.GetTemporary(source.width>>1,source.height>>1,0,source.format);
            var blur=RenderTexture.GetTemporary(source.width>>1,source.height>>1,0,source.format);
            allocations+=2;
            var material=InactiveEffectsValidation.dofMaterial;
            material.SetVector("_Offsets",new Vector4(0,internalBlurWidth,.1f,internalBlurWidth));
            if(highResolution) { Graphics.Blit(source,destination,material,17); blurDraws++; }
            else
            {
                Graphics.Blit(source,half,material,6); Graphics.Blit(half,blur,material,17);
                material.SetTexture("_LowRez",blur); material.SetTexture("_FgOverlap",null);
                material.SetVector("_Offsets",Vector4.one*source.width/blur.width*internalBlurWidth);
                Graphics.Blit(source,destination,material,18); blurDraws+=3;
            }
            RenderTexture.ReleaseTemporary(half); RenderTexture.ReleaseTemporary(blur);
            if (_ssaaPropagator!=null) _ssaaPropagator.ReleaseSourceDestination(source,destination);
        }
    }
}

public static class InactiveEffectsValidation
{
    public static Material statusMaterial, dofMaterial;
    static int checks;
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath,".."));
    static void Check(bool ok,string message) { if(!ok)throw new Exception(message); checks++; }
    static void NativeSelectors()
    {
        var harmony=new Harmony("inactive-fixture-native-selectors");
        foreach(var name in new[]{"GetSourceDestination","GetSourceDestinationHDR","GetSourceDestinationLDR","GetSourceDestinationHDRToLDR","ReleaseSourceDestination"})
            harmony.Patch(AccessTools.Method(typeof(SSAAPropagator),name),transpiler:new HarmonyMethod(typeof(InactiveEffectsValidation),nameof(SelectorBody)));
    }
    static IEnumerable<CodeInstruction> SelectorBody(MethodBase original,ILGenerator generator)
    {
        using(var assembly=AssemblyDefinition.ReadAssembly(File.ReadAllText(Path.Combine(Root,"runtime-assembly.txt")).Trim()))
        {
            var method=assembly.MainModule.Types.Single(t=>t.Name=="SSAAPropagator").Methods.Single(m=>m.Name==original.Name);
            var opcodes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>(ushort)o.Value);
            var labels=new Dictionary<Mono.Cecil.Cil.Instruction,Label>();
            foreach(var i in method.Body.Instructions)if(i.Operand is Mono.Cecil.Cil.Instruction target && !labels.ContainsKey(target))labels[target]=generator.DefineLabel();
            var result=new List<CodeInstruction>();
            foreach(var i in method.Body.Instructions)
            {
                object operand=i.Operand;
                if(operand is FieldReference field) operand=AccessTools.Field(typeof(SSAAPropagator),field.Name);
                else if(operand is MethodReference called)
                {
                    var type=called.DeclaringType.Name=="SSAAPropagator"?typeof(SSAAPropagator):typeof(Object);
                    operand=AccessTools.Method(type,called.Name);
                }
                else if(operand is ParameterDefinition parameter) operand=parameter.Index+1;
                else if(operand is Mono.Cecil.Cil.Instruction branch) operand=labels[branch];
                var code=new CodeInstruction(opcodes[(ushort)i.OpCode.Value],operand);
                if(labels.TryGetValue(i,out var label))code.labels.Add(label);
                result.Add(code);
            }
            return result;
        }
    }
    static RenderTexture Texture(int w,int h,RenderTextureFormat format)
    {
        var t=new RenderTexture(w,h,0,format){filterMode=FilterMode.Bilinear}; t.Create(); return t;
    }
    static Color[] Read(RenderTexture texture)
    {
        var old=RenderTexture.active; RenderTexture.active=texture;
        var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGBAFloat,false,true);
        pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0); pixels.Apply();
        var result=pixels.GetPixels(); Object.DestroyImmediate(pixels); RenderTexture.active=old; return result;
    }
    static void Same(Color[] a,Color[] b,float tolerance,string message)
    {
        Check(a.Length==b.Length,message+" dimensions"); float error=0;
        for(int i=0;i<a.Length;i++) for(int c=0;c<4;c++)error=Math.Max(error,Math.Abs(a[i][c]-b[i][c]));
        Check(error<=tolerance,message+" RGBA error "+error);
    }
    static void Seed(SSAAPropagator p,bool hdr,Texture input)
    {
        p._currentDestinationHDR=p._currentDestinationLDR=0; p._HDRSourceDestination=hdr;
        Graphics.Blit(input,hdr?p._resampledColorTargetHDR[0]:p._resampledColorTargetLDR[0]);
        var old=RenderTexture.active;
        RenderTexture.active=hdr?p._resampledColorTargetHDR[1]:p._resampledColorTargetLDR[1];
        GL.Clear(false,true,Color.magenta); RenderTexture.active=old;
        p.FillCommandBuffer(null,default,default);
    }
    static Texture2D Pattern()
    {
        var t=new Texture2D(32,24,TextureFormat.RGBAFloat,false,true); var pixels=new Color[32*24];
        for(int y=0;y<24;y++)for(int x=0;x<32;x++)pixels[y*32+x]=new Color(x/32f,y/24f,(x%3)/3f,(x+y)%9/9f);
        t.SetPixels(pixels);t.Apply();return t;
    }
    static Color[] RunStatuses(SSAAPropagator p,StatusFixture[] effects,RenderTexture proxy,Texture input,int mask,bool hdr,bool skip)
    {
        FuckTheseFX.Plugin.SkipInactiveEffects.Value=skip; Seed(p,hdr,input);
        ((DeathFade)effects[0])._fadeValue=(mask&1)!=0?.3f:0;
        ((BloodOnScreen)effects[1])._lastHitTime=(mask&2)!=0?Time.time:Time.time-10;
        ((BloodOnScreen)effects[1]).MaxBloodTime=1;
        ((PostprocessGrayscale)effects[2])._effectAmount=(mask&4)!=0?.3f:0;
        ((DistortCameraFX)effects[3])._doNotRender=(mask&8)==0;
        ((InfectionEffect)effects[4])._effectValue=(mask&16)!=0?.3f:-1;
        foreach(var effect in effects)
        {
            effect.copies=effect.activeDraws=0;
            AccessTools.Method(effect.GetType(),"OnRenderImage").Invoke(effect,new object[]{proxy,proxy});
        }
        p.GetSourceDestination(out var current,out _);
        return Read(current);
    }
    public static void Run()
    {
        try
        {
            Check(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Direct3D11,"D3D11"); NativeSelectors();
            var bundle=AssetBundle.LoadFromFile(Path.Combine(Root,"native-dof-reference"));
            dofMaterial=new Material(bundle.LoadAllAssets<Shader>().Single()); bundle.Unload(false);
            Check(dofMaterial.shader.isSupported && dofMaterial.passCount==23,"Installed native DoF shader loaded");
            statusMaterial=new Material(Shader.Find("Hidden/InactiveEffectsValidation"));
            var owner=new GameObject("inactive fixture"); var camera=owner.AddComponent<Camera>(); camera.enabled=false;
            var p=owner.AddComponent<SSAAPropagator>();
            p._resampledColorTargetHDR=new[]{Texture(32,24,RenderTextureFormat.ARGBHalf),Texture(32,24,RenderTextureFormat.ARGBHalf)};
            p._resampledColorTargetLDR=new[]{Texture(32,24,RenderTextureFormat.ARGB32),Texture(32,24,RenderTextureFormat.ARGB32)};
            var proxy=Texture(32,24,RenderTextureFormat.ARGBHalf); var input=Pattern();
            var effects=new StatusFixture[]{owner.AddComponent<DeathFade>(),owner.AddComponent<BloodOnScreen>(),owner.AddComponent<PostprocessGrayscale>(),owner.AddComponent<DistortCameraFX>(),owner.AddComponent<InfectionEffect>()};
            foreach(var effect in effects)effect._ssaaPropagator=p;
            var dof=owner.AddComponent<UnityStandardAssets.ImageEffects.DepthOfField>();dof.Bind(p);dof.dofHdrShader=dofMaterial.shader;
            var final=owner.AddComponent<SSAAImpl>();final._ssaaPropagator=p;
            using(var integration=new InactiveEffectsIntegration())
            {
                integration.Initialize();
                foreach(bool hdr in new[]{false,true})for(int mask=0;mask<32;mask++)
                {
                    var native=RunStatuses(p,effects,proxy,input,mask,hdr,false);
                    var reduced=RunStatuses(p,effects,proxy,input,mask,hdr,true);
                    Same(native,reduced,hdr?.003f:.008f,$"Status mask {mask} HDR {hdr}");
                    Check(effects.Sum(e=>e.copies)==0,"No copies for neutral effects");
                    Check(effects.Sum(e=>e.activeDraws)==Enumerable.Range(0,5).Count(i=>(mask&(1<<i))!=0),"Active status effects retained");
                    Check(((InfectionEffect)effects[4])._effectValue>=0,"Infection clamp preserved");
                    final.OnRenderImage(proxy,proxy);
                    Check(!InactiveEffectsIntegration.HasChain(p,out _),"Chain closes at final consumer");
                }
                Seed(p,false,input);((DeathFade)effects[0])._fadeValue=0;((DeathFade)effects[0])._closeEyesValue=.2f;
                effects[0].copies=effects[0].activeDraws=0;((DeathFade)effects[0]).OnRenderImage(proxy,proxy);
                Check(effects[0].activeDraws==1,"Eye-closing remains active independently of fade");
                Seed(p,false,input);((BloodOnScreen)effects[1])._lastHitTime=Time.time;((BloodOnScreen)effects[1]).MaxBloodTime=0;
                effects[1].activeDraws=0;((BloodOnScreen)effects[1]).OnRenderImage(proxy,proxy);
                Check(effects[1].activeDraws==1,"Blood equality does not skip");
                Seed(p,false,input);((PostprocessGrayscale)effects[2])._effectAmount=float.NaN;
                effects[2].activeDraws=0;((PostprocessGrayscale)effects[2]).OnRenderImage(proxy,proxy);
                Check(effects[2].activeDraws==1,"Unordered grayscale comparison matches native");
                Seed(p,false,input);((InfectionEffect)effects[4])._effectValue=float.NaN;
                effects[4].copies=0;((InfectionEffect)effects[4]).OnRenderImage(proxy,proxy);
                Check(float.IsNaN(((InfectionEffect)effects[4])._effectValue)&&effects[4].copies==0,"Infection unordered neutral branch preserved");
                ((DistortCameraFX)effects[3]).OnPreCull();Check(((DistortCameraFX)effects[3]).preCulls==1,"Distortion pre-cull still runs");

                foreach(bool hdr in new[]{false,true})foreach(bool high in new[]{false,true})
                {
                    dof.highResolution=high;dof.aperture=0;dof.nearBlur=dof.visualizeFocus=false;
                    FuckTheseFX.Plugin.SkipInactiveEffects.Value=false;Seed(p,hdr,input);dof.OnRenderImage(proxy,proxy);
                    p.GetSourceDestination(out var nativeTexture,out _);var native=Read(nativeTexture);
                    FuckTheseFX.Plugin.SkipInactiveEffects.Value=true;Seed(p,hdr,input);
                    dof.allocations=dof.blurDraws=dof.cocDraws=0;dof.OnRenderImage(proxy,proxy);
                    p.GetSourceDestination(out var reducedTexture,out _);
                    Same(native,Read(reducedTexture),hdr?.003f:.008f,$"Native DoF shader zero aperture HDR {hdr} high {high}");
                    Check(dof.allocations==0&&dof.blurDraws==0&&dof.cocDraws==1,"Only native CoC draw remains");
                    Check((hdr?p._currentDestinationHDR:p._currentDestinationLDR)==0,"DoF retains current texture/index");
                    Check(Read(reducedTexture).All(c=>c.a==0),"Zero CoC alpha preserved");
                }
                // Vary depth/focus so the CoC identity is not established only
                // against Unity's default depth texture. Test both disc branches.
                Shader.SetGlobalVector("_ZBufferParams",new Vector4(-999,1000,-9.99f,10));
                foreach(float depth in new[]{0f,.2f,1f})
                {
                    var depthTexture=new Texture2D(32,24,TextureFormat.RFloat,false,true);
                    depthTexture.SetPixels(Enumerable.Repeat(new Color(depth,0,0,1),32*24).ToArray());depthTexture.Apply();
                    Shader.SetGlobalTexture("_CameraDepthTexture",depthTexture);
                    foreach(float focus in new[]{0f,.3f,1f})foreach(float focalSize in new[]{0f,.05f,2f})foreach(bool high in new[]{false,true})
                    {
                        dof.aperture=0;dof.focalDistance01=focus;dof.focalSize=focalSize;dof.highResolution=high;
                        FuckTheseFX.Plugin.SkipInactiveEffects.Value=false;Seed(p,true,input);dof.OnRenderImage(proxy,proxy);
                        p.GetSourceDestination(out var normal,out _);var expected=Read(normal);
                        FuckTheseFX.Plugin.SkipInactiveEffects.Value=true;Seed(p,true,input);dof.OnRenderImage(proxy,proxy);
                        p.GetSourceDestination(out var reduced,out _);
                        Same(expected,Read(reduced),.003f,$"DoF depth {depth} focus {focus} size {focalSize} high {high}");
                    }
                    Shader.SetGlobalTexture("_CameraDepthTexture",null);Object.DestroyImmediate(depthTexture);
                }
                dof.focalSize=.05f;
                Seed(p,true,input);p.GetSourceDestinationHDRToLDR(out var hdrSource,out var ldrDestination);
                Graphics.Blit(hdrSource,ldrDestination);p.ReleaseSourceDestination(hdrSource,ldrDestination);
                Check(InactiveEffectsIntegration.HasChain(p,out var transitioned)&&transitioned==ldrDestination,"HDR-to-LDR boundary retains current frame chain");
                foreach(string reason in new[]{"aperture","visualize","near","badfocus","disabled","missing","closed"})
                {
                    FuckTheseFX.Plugin.SkipInactiveEffects.Value=true;Seed(p,true,input);
                    dof.aperture=0;dof.visualizeFocus=dof.nearBlur=false;dof.focalDistance01=.3f;p.enabled=true;
                    if(reason=="aperture")dof.aperture=.001f;
                    if(reason=="visualize")dof.visualizeFocus=true;
                    if(reason=="near")dof.nearBlur=true;
                    if(reason=="badfocus")dof.focalDistance01=float.NaN;
                    if(reason=="disabled")p.enabled=false;
                    if(reason=="missing")p._resampledColorTargetHDR[1].Release();
                    if(reason=="closed")final.OnRenderImage(proxy,proxy);
                    Check(!InactiveEffectsIntegration.CanExitDof(dof,p._resampledColorTargetHDR[0]),"DoF native fallback: "+reason);
                    if(reason=="missing")p._resampledColorTargetHDR[1].Create();
                }
                p.enabled=true;dof.focalDistance01=.3f;dof.aperture=.1f;dof.visualizeFocus=dof.nearBlur=false;
                Seed(p,true,input);dof.allocations=0;dof.OnRenderImage(proxy,proxy);
                Check(dof.allocations==2,"Positive aperture resumes allocations immediately");

                Seed(p,false,input);((DeathFade)effects[0])._closeEyesValue=((DeathFade)effects[0])._fadeValue=0;
                effects[0].copies=0;final.OnRenderImage(proxy,proxy);((DeathFade)effects[0]).OnRenderImage(proxy,proxy);
                Check(effects[0].copies==1,"Closed SSAA chain uses original neutral copy");
                Seed(p,false,input);p.enabled=false;effects[0].copies=0;((DeathFade)effects[0]).OnRenderImage(proxy,proxy);
                Check(effects[0].copies==1,"Disabled SSAA propagator uses original neutral copy");p.enabled=true;
                Seed(p,false,input);
                var stamps=(Dictionary<SSAAPropagator,int>)AccessTools.Field(typeof(InactiveEffectsIntegration),"chains").GetValue(null);
                stamps[p]=Time.frameCount-1;
                Check(!InactiveEffectsIntegration.HasChain(p,out _),"Previous-frame chain cannot bypass a pass");
                Seed(p,true,input);dof.aperture=0;dof.dofHdrShader=statusMaterial.shader;
                Check(!InactiveEffectsIntegration.CanExitDof(dof,p._resampledColorTargetHDR[0]),"Replacement DoF shader retains original pipeline");
                dof.dofHdrShader=dofMaterial.shader;

                foreach(var effect in effects)effect._ssaaPropagator=null;
                ((DeathFade)effects[0])._closeEyesValue=((DeathFade)effects[0])._fadeValue=0; effects[0].copies=0;
                ((DeathFade)effects[0]).OnRenderImage(p._resampledColorTargetHDR[0],proxy);
                Check(effects[0].copies==1,"Ordinary Unity chain still writes destination");
                foreach(var effect in effects)effect._ssaaPropagator=p;
                // Real camera callbacks exercise Unity's unused proxy target handling.
                ((PostprocessGrayscale)effects[2])._effectAmount=0;((InfectionEffect)effects[4])._effectValue=0;
                ((BloodOnScreen)effects[1])._lastHitTime=Time.time-10;((BloodOnScreen)effects[1]).MaxBloodTime=1;
                ((DistortCameraFX)effects[3])._doNotRender=true;dof.enabled=false;
                camera.targetTexture=proxy;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.blue;
                FuckTheseFX.Plugin.SkipInactiveEffects.Value=true;Seed(p,false,input);
                foreach(var effect in effects)effect.copies=effect.activeDraws=0;
                camera.Render();
                Same(Read(proxy),Read(p._resampledColorTargetLDR[0]),.008f,"Real camera final output uses retained SSAA image");
                Check(effects.Sum(e=>e.copies)==0,"Real image callbacks issue no neutral copies");
            }
            var remaining=Harmony.GetPatchInfo(AccessTools.Method(typeof(DeathFade),"OnRenderImage"));
            Check(remaining==null || !remaining.Owners.Contains("com.rootdarkarchon.fuckthesefx.inactive-effects"),"Status patches removed on dispose");
            RenderTexture.active=null;
            foreach(var t in p._resampledColorTargetHDR.Concat(p._resampledColorTargetLDR))Object.DestroyImmediate(t);
            Object.DestroyImmediate(proxy);Object.DestroyImmediate(input);Object.DestroyImmediate(owner);
            Object.DestroyImmediate(statusMaterial);Object.DestroyImmediate(dofMaterial);
            File.WriteAllText(Path.Combine(Root,"validation.txt"),$"INACTIVE EFFECTS PASSED: {checks} checks\nGPU: {SystemInfo.graphicsDeviceName}\nInstalled SSAA selector/release IL, 64 status combinations, native DoF shader RGBA parity (depth/focus sweep), guarded fallbacks and real camera callbacks. No live EFT timing claim.\n");
        }
        catch(Exception ex){Debug.LogException(ex);EditorApplication.Exit(1);}
    }
}
