using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEngine;
using FuckTheseFX;
using FuckTheseFX.Source.Prism;
using FuckTheseFX.Source.Sharpen;

namespace FuckTheseFX
{
    internal sealed class Setting<T>
    {
        T value;
        internal Setting(T initial) { value = initial; }
        internal T Value { get => value; set { this.value = value; SettingChanged?.Invoke(this, EventArgs.Empty); } }
        internal event EventHandler SettingChanged;
    }
    internal sealed class TestLog { internal void LogInfo(object value) => Debug.Log(value); }
    internal static class Plugin
    {
        internal static TestLog MyLog = new TestLog();
        internal static Setting<int> PrismExposurePercent = new Setting<int>(100);
        internal static Setting<bool> SharpenEnabled = new Setting<bool>(true);
    }
}

public sealed class CC_Sharpen : MonoBehaviour { }

public static class PostEffectsValidation
{
    static int checks, observedWidth, observedHeight;
    static RenderTextureFormat observedFormat;
    static void Require(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    public static void Observe(RenderTexture texture)
    {
        observedWidth = texture.width; observedHeight = texture.height; observedFormat = texture.format;
    }

    // Import the ACTUAL installed first allocation instruction sequence. The
    // observer replaces the remainder of the native reducer only in this fixture.
    // It verifies Harmony's emitted call and real Unity allocation, not adaptation
    // image quality, native shader performance, or the rest of Prism's rendering.
    static MethodInfo LoadNativeAllocation()
    {
        using (var game = AssemblyDefinition.ReadAssembly(File.ReadAllText(Path.Combine(Application.dataPath, "../installed-assembly.txt")).Trim()))
        using (var fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("NativeExposureAllocationFixture", new Version(1,0)), "Fixture", ModuleKind.Dll))
        {
            var module = fixture.MainModule;
            var type = new TypeDefinition("", "PrismEffects", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            module.Types.Add(type);
            var ctor = new MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            type.Methods.Add(ctor);
            var constructor = ctor.Body.GetILProcessor();
            constructor.Emit(OpCodes.Ldarg_0); constructor.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes))); constructor.Emit(OpCodes.Ret);
            var method = new MethodDefinition("HandleExposure", Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void) { ImplAttributes = Mono.Cecil.MethodImplAttributes.NoInlining };
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(RenderTexture))));
            method.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(RenderTextureFormat))));
            type.Methods.Add(method);
            var native = game.MainModule.Types.Single(t => t.Name == "PrismEffects").Methods.Single(m => m.Name == "HandleExposure");
            var allocation = typeof(RenderTexture).GetMethod("GetTemporary", new[] { typeof(int), typeof(int), typeof(int), typeof(RenderTextureFormat) });
            var emit = method.Body.GetILProcessor();
            foreach (var instruction in native.Body.Instructions.Take(11))
            {
                if (instruction.Operand is MethodReference reference)
                    emit.Emit(instruction.OpCode, module.ImportReference(reference.Name == "get_width" ? typeof(Texture).GetProperty("width").GetGetMethod() : allocation));
                else { Require(instruction.Operand == null, "Unexpected native allocation operand"); emit.Emit(instruction.OpCode); }
            }
            emit.Emit(OpCodes.Dup);
            emit.Emit(OpCodes.Call, module.ImportReference(typeof(PostEffectsValidation).GetMethod(nameof(Observe))));
            emit.Emit(OpCodes.Call, module.ImportReference(typeof(RenderTexture).GetMethod("ReleaseTemporary")));
            emit.Emit(OpCodes.Ret);
            using (var stream = new MemoryStream())
            {
                fixture.Write(stream);
                return Assembly.Load(stream.ToArray()).GetType("PrismEffects").GetMethod("HandleExposure");
            }
        }
    }

    static void Allocation(MethodInfo method, object instance, int width, int height, int percent, int expected)
    {
        var source = new RenderTexture(width,height,0,RenderTextureFormat.ARGBHalf);
        try
        {
            Plugin.PrismExposurePercent.Value = percent;
            method.Invoke(instance, new object[] { source, RenderTextureFormat.ARGBHalf });
            Require(observedWidth == expected && observedHeight == expected, $"{width}x{height} at {percent}% must produce {expected} square");
            Require(observedFormat == RenderTextureFormat.ARGBHalf, "Exposure format preserved");
            Require(source.width == width && source.height == height, "Scene dimensions unchanged");
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    public static void Run()
    {
        try
        {
            Require(SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11, "D3D11 runtime");
            var method = LoadNativeAllocation(); var instance = Activator.CreateInstance(method.DeclaringType);
            Allocation(method,instance,3840,2160,100,1920);
            using (var integration = new PrismExposureIntegration())
            {
                integration.Initialize();
                Allocation(method,instance,3840,2160,100,1920);
                Allocation(method,instance,3840,2160,50,960);
                Allocation(method,instance,3840,2160,25,480);
                Allocation(method,instance,2560,1440,66,844);
                Allocation(method,instance,1920,1080,50,480);
                Allocation(method,instance,101,77,100,50);
                Allocation(method,instance,101,77,50,25);
                Allocation(method,instance,64,48,1,1);
                Allocation(method,instance,64,48,200,32);
                Allocation(method,instance,64,48,-10,1);
                Allocation(method,instance,3840,2160,100,1920);
                var wrong = new[] { new CodeInstruction(System.Reflection.Emit.OpCodes.Ret) };
                bool rejected = false;
                try { PrismExposureIntegration.ResizeExposureInput(wrong).ToList(); } catch (NotSupportedException) { rejected = true; }
                Require(rejected, "Unsupported allocation fails closed");
            }
            Allocation(method,instance,3840,2160,50,1920);
            Require(Harmony.GetPatchInfo(method) == null || !Harmony.GetPatchInfo(method).Owners.Contains("com.rootdarkarchon.fuckthesefx.prism-exposure"), "Exposure patch removed on dispose");

            var cameraOwner = new GameObject("Sharpen validation"); cameraOwner.AddComponent<Camera>();
            var enabled = cameraOwner.AddComponent<CC_Sharpen>();
            var disabled = cameraOwner.AddComponent<CC_Sharpen>(); disabled.enabled = false;
            var onPreCull = typeof(SharpenFilterIntegration).GetMethod("OnCameraPreCull", BindingFlags.Instance | BindingFlags.NonPublic);
            using (var integration = new SharpenFilterIntegration())
            {
                integration.Initialize();
                Require(enabled.enabled && !disabled.enabled, "Default preserves game enabled states");
                Plugin.SharpenEnabled.Value = false;
                Require(!enabled.enabled && !disabled.enabled, "Off suppresses enabled filter");
                Plugin.SharpenEnabled.Value = true;
                Require(enabled.enabled && !disabled.enabled, "On restores only owned filter");
                Plugin.SharpenEnabled.Value = false;
                enabled.enabled = true;
                onPreCull.Invoke(integration, new object[] { cameraOwner.GetComponent<Camera>() });
                Require(!enabled.enabled, "Camera callback suppresses native re-enable");
                var lateOwner = new GameObject("Late camera"); var lateCamera = lateOwner.AddComponent<Camera>();
                var lateEffect = lateOwner.AddComponent<CC_Sharpen>();
                onPreCull.Invoke(integration, new object[] { lateCamera });
                Require(!lateEffect.enabled, "New camera is discovered while off");
                UnityEngine.Object.DestroyImmediate(lateOwner);
            }
            Require(enabled.enabled && !disabled.enabled, "Dispose restores only changed live components");
            Plugin.SharpenEnabled.Value = false;
            Require(enabled.enabled, "Setting handler removed on dispose");
            UnityEngine.Object.DestroyImmediate(cameraOwner);
            File.WriteAllText(Path.Combine(Application.dataPath, "../validation.txt"), $"POST EFFECTS VALIDATION PASSED: {checks} checks\nGPU: {SystemInfo.graphicsDeviceName}\nNative allocation prefix + real Unity textures + selective component ownership. No live EFT performance or exposure-image acceptance.\n");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
