using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using FuckTheseFX.Source.Bloom;
using FuckTheseFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX
{
    internal class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal class TestLog
    {
        internal void LogInfo(object value) => Debug.Log(value);
        internal void LogWarning(object value) => Debug.LogWarning(value);
        internal void LogError(object value) => Debug.LogError(value);
    }
    internal static class Plugin
    {
        internal static TestLog MyLog = new TestLog();
        internal static Setting<bool> BloomEnabled = new Setting<bool>(true);
        internal static Setting<float> BloomInputScale = new Setting<float>(1);
        internal static Setting<bool> BloomUseNativeResolution = new Setting<bool>(false);
    }
}

public class SSAA : MonoBehaviour
{
    public int InputWidth, InputHeight;
    public int GetInputWidth() => InputWidth;
    public int GetInputHeight() => InputHeight;
}

// Models the inspected production contract: clear, record multiple passes, execute once,
// then release buffers. Both native final-combine bindings are present for the real transpiler.
public class UltimateBloom : MonoBehaviour
{
    private static readonly int _colorBuffer = Shader.PropertyToID("_ColorBuffer");
    private Material material;
    private CommandBuffer buffer;
    private Mesh triangle;
    public bool Deferred, Throw, Passthrough;
    public int InputWidth, InputHeight;
    public RenderTexture LastInput;
    public Action Nested;

    public void Init(Shader shader)
    {
        material = new Material(shader);
        buffer = new CommandBuffer();
        triangle = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(-1,3,0), new Vector3(3,-1,0) },
            uv = new[] { new Vector2(0,0), new Vector2(0,2), new Vector2(2,0) }, triangles = new[] { 0,1,2 } };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnRenderImageInternal(RenderTexture source, RenderTexture destination)
    {
        buffer.Clear();
        InputWidth = source.width; InputHeight = source.height; LastInput = source;
        if (Throw) throw new InvalidOperationException("fixture failure");
        Nested?.Invoke();
        var glow = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        glow.filterMode = FilterMode.Bilinear;
        try
        {
            if (Deferred)
            {
                buffer.Blit(source, glow, material, 0);
                var properties = new MaterialPropertyBlock();
                properties.SetTexture(_colorBuffer, source);
                properties.SetTexture("_MainTex", Passthrough ? Texture2D.blackTexture : glow);
                buffer.SetRenderTarget(destination);
                buffer.SetViewport(new Rect(0, 0, destination.width, destination.height));
                buffer.DrawMesh(triangle, Matrix4x4.identity, material, 0, 2, properties);
                Graphics.ExecuteCommandBuffer(buffer);
            }
            else
            {
                Graphics.Blit(source, glow, material, 0);
                material.SetTexture(_colorBuffer, source);
                Graphics.Blit(Passthrough ? (Texture)Texture2D.blackTexture : glow, destination, material, 1);
            }
        }
        finally { RenderTexture.ReleaseTemporary(glow); }
    }

    public void Cleanup()
    {
        buffer?.Dispose();
        DestroyImmediate(material); DestroyImmediate(triangle);
    }
}

public static class BloomValidation
{
    private static int checks;
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        checks++; Debug.Log("PASS: " + message);
    }
    private static void Policy(int w, int h, float scale, bool native, int nw, int nh, int ew, int eh)
    {
        BloomResolutionPolicy.Resolve(w,h,scale,native,nw,nh,out int rw,out int rh);
        Require(rw == ew && rh == eh, $"Policy {w}x{h}, scale {scale}, native {native} => {ew}x{eh}");
    }
    private static Color[] Read(RenderTexture rt)
    {
        var previous = RenderTexture.active;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        try { RenderTexture.active = rt; texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply(); return texture.GetPixels(); }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
    }
    private static double Difference(Color[] a, Color[] b) => a.Zip(b, (x,y) => Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)).Average();
    private static RenderTexture Target(int w, int h)
    {
        var rt = new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Bilinear; rt.Create(); return rt;
    }

    public static void Run()
    {
        try { RunChecks(); File.WriteAllText("validation.txt", $"BLOOM VALIDATION PASSED: {checks} checks\nGPU: {SystemInfo.graphicsDeviceName}\n"); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void RunChecks()
    {
        Require(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11, "D3D11 rendering");
        Policy(3840,2160,.5f,false,0,0,1920,1080);
        Policy(3840,2160,1f,false,0,0,3840,2160);
        Policy(3840,2160,.5f,true,2560,1440,2560,1440);
        Policy(3840,2160,1f,true,1280,720,1280,720);
        Policy(3840,2160,.5f,true,0,0,3840,2160);
        Policy(1920,1080,.5f,true,3840,2160,1920,1080);
        Policy(101,77,.5f,false,0,0,51,39);
        Policy(1,1,.5f,false,0,0,1,1);
        Policy(64,64,float.NaN,false,0,0,64,64);
        Policy(64,64,.1f,false,0,0,16,16);
        Policy(3840,2160,.25f,false,0,0,960,540);
        Policy(100,100,.67f,false,0,0,67,67);
        Policy(64,64,2f,false,0,0,64,64);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/BloomFixture.shader");
        if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader)) Debug.Log(message.message);
        Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Fixture shader compiles");
        var owner = new GameObject("Bloom camera");
        var bloom = owner.AddComponent<UltimateBloom>(); bloom.Init(shader);
        var ssaa = owner.AddComponent<SSAA>(); ssaa.InputWidth = 40; ssaa.InputHeight = 32;
        var source = Target(64,48); var output = Target(64,48); var nestedOutput = Target(64,48);
        var pattern = new Texture2D(64,48,TextureFormat.RGBAFloat,false,true);
        var pixels = new Color[64*48];
        for (int y=0;y<48;y++) for(int x=0;x<64;x++) pixels[y*64+x] = new Color((x%2)*.6f+.1f,y/96f,x/128f,1);
        pattern.SetPixels(pixels); pattern.Apply(); Graphics.Blit(pattern,source);
        var untouched = Read(source);
        var integration = new BloomResolutionIntegration();
        try
        {
            bloom.Passthrough = true;
            bloom.OnRenderImageInternal(source,output);
            var baseline = Read(output);
            Require(Difference(untouched,baseline)<.001, "Fixture native combine preserves input orientation and scene");
            var target = AccessTools.Method(typeof(UltimateBloom), "OnRenderImageInternal");
            var legacy = new Harmony("com.matsix.sptaoreplacer.bloom-resolution");
            legacy.Patch(target, transpiler: new HarmonyMethod(typeof(BloomResolutionIntegration), "PreserveSceneSource"));
            try
            {
                using (var conflicting = new BloomResolutionIntegration())
                {
                    bool continued = false;
                    Require(FeatureStartup.TryInitialize("Bloom", conflicting.Initialize), "Legacy bloom conflict returns without aborting initialization");
                    FeatureStartup.TryInitialize("Next feature", () => continued = true);
                    Require(continued && !Harmony.GetPatchInfo(target).Owners.Contains("com.rootdarkarchon.fuckthesefx.bloom-resolution"), "Conflict leaves legacy hook intact and later features can start");
                    bloom.OnRenderImageInternal(source,output);
                    Require(ReferenceEquals(bloom.LastInput,source) && Difference(baseline,Read(output))<.001, "Conflict fallback keeps native input and scene");
                }
            }
            finally { legacy.Unpatch(target, HarmonyPatchType.All, legacy.Id); }
            Require(!FeatureStartup.TryInitialize("Expected fixture failure", () => throw new InvalidOperationException("test startup failure")), "Unexpected initialization exception is contained");
            integration.Initialize();
            Require(Harmony.GetPatchInfo(target)?.Owners.Contains("com.rootdarkarchon.fuckthesefx.bloom-resolution") == true, "Production Harmony hook installs");
            foreach (bool deferred in new[] {false,true})
            {
                bloom.Deferred = deferred;
                FuckTheseFX.Plugin.BloomInputScale.Value = 1;
                bloom.OnRenderImageInternal(source,output);
                Require(ReferenceEquals(bloom.LastInput,source) && Difference(baseline,Read(output))<.001, $"{deferred}: 1.0 uses original input and output");
                FuckTheseFX.Plugin.BloomInputScale.Value = .5f;
                bloom.OnRenderImageInternal(source,output);
                Require(bloom.InputWidth==32 && bloom.InputHeight==24, $"{deferred}: reduced input dimensions");
                Require(output.width==64 && output.height==48 && Difference(baseline,Read(output))<.001, $"{deferred}: full-resolution high-frequency scene preserved");
                bloom.Passthrough=false;
                bloom.OnRenderImageInternal(source,output);
                var bright = Read(output);
                Require(bright.Average(c=>c.r)-baseline.Average(c=>c.r)>.05, $"{deferred}: reduced bloom contributes after deferred execution");
                Require(Difference(untouched,Read(source))<.00001, $"{deferred}: original source unchanged");
                FuckTheseFX.Plugin.BloomEnabled.Value = false;
                FuckTheseFX.Plugin.BloomUseNativeResolution.Value = true;
                bloom.OnRenderImageInternal(source,output);
                Require(ReferenceEquals(bloom.LastInput,source) && Read(output).Average(c=>c.r)-baseline.Average(c=>c.r)>.05, $"{deferred}: disabled adjustments preserve native bloom and original input");
                FuckTheseFX.Plugin.BloomEnabled.Value = true;
                FuckTheseFX.Plugin.BloomUseNativeResolution.Value = false;
                FuckTheseFX.Plugin.BloomInputScale.Value = .25f;
                bloom.Passthrough=true;
                bloom.OnRenderImageInternal(source,output);
                Require(bloom.InputWidth==16 && bloom.InputHeight==12 && Difference(baseline,Read(output))<.001, $"{deferred}: quarter input keeps full scene detail");
                FuckTheseFX.Plugin.BloomUseNativeResolution.Value = true;
                bloom.OnRenderImageInternal(source,output);
                Require(bloom.InputWidth==40 && bloom.InputHeight==32 && Difference(baseline,Read(output))<.001, $"{deferred}: internal dimensions override slider");
                ssaa.InputWidth=24; ssaa.InputHeight=16;
                bloom.OnRenderImageInternal(source,output);
                Require(bloom.InputWidth==24 && bloom.InputHeight==16, $"{deferred}: native resolution can override below slider minimum");
                ssaa.enabled=false;
                bloom.OnRenderImageInternal(source,output);
                Require(ReferenceEquals(bloom.LastInput,source), $"{deferred}: inactive upscaler ignores slider and keeps full input");
                ssaa.enabled=true; ssaa.InputWidth=40; ssaa.InputHeight=32;
                FuckTheseFX.Plugin.BloomUseNativeResolution.Value=false;
            }
            var innerOwner = new GameObject("Nested bloom camera");
            var inner = innerOwner.AddComponent<UltimateBloom>(); inner.Init(shader); inner.Passthrough=true; inner.Deferred=true;
            try
            {
                bloom.Nested = () => inner.OnRenderImageInternal(source,nestedOutput);
                bloom.OnRenderImageInternal(source,output);
                Require(Difference(baseline,Read(output))<.001 && Difference(baseline,Read(nestedOutput))<.001, "Nested cameras retain separate original sources");
                bloom.Nested=null;
            }
            finally { inner.Cleanup(); UnityEngine.Object.DestroyImmediate(innerOwner); }
            bloom.Throw=true;
            bool threw=false;
            try { bloom.OnRenderImageInternal(source,output); } catch(InvalidOperationException) { threw=true; }
            bloom.Throw=false;
            Require(threw && AccessTools.Field(typeof(BloomResolutionIntegration), "current").GetValue(null)==null, "Exception propagates and temporary scope is cleared");
            bloom.OnRenderImageInternal(source,output);
            Require(Difference(baseline,Read(output))<.001, "Rendering recovers after exception");
            integration.Dispose();
            bloom.OnRenderImageInternal(source,output);
            Require(ReferenceEquals(bloom.LastInput,source), "Dispose restores native input routing");
        }
        finally
        {
            integration.Dispose(); bloom.Cleanup();
            foreach(var rt in new[]{source,output,nestedOutput}) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            UnityEngine.Object.DestroyImmediate(pattern); UnityEngine.Object.DestroyImmediate(owner);
        }
    }
}
