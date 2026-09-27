using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using FuckTheseFX.Research;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using CustomAmbientResearch;

// Same operation shapes/branches as the pinned native method, exercised by real Harmony and D3D11.
namespace UnityEngine.Rendering.PostProcessing
{
    public sealed class PropertySheet { public Material material; }
    public static class RuntimeUtilities
    {
        public static readonly List<int> Passes = new List<int>();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void BlitFullscreenTriangle(CommandBuffer cb, RenderTargetIdentifier source, RenderTargetIdentifier destination,
            PropertySheet sheet, int pass, bool clear, Rect? rect)
        { Passes.Add(pass); cb.Blit(source, destination, sheet.material, pass); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void BlitFullscreenTriangle(CommandBuffer cb, RenderTargetIdentifier source, RenderTargetIdentifier destination, bool clear, Rect? rect)
        { Passes.Add(-1); cb.Blit(source, destination); }
    }
}

public static class SsrProfilingValidation
{
    public sealed class Context
    {
        public Camera camera { get; set; }
        public CommandBuffer command { get; set; }
        public int width => 16;
        public int height => 16;
        public bool SceneView, Reset;
    }
    public sealed class Renderer
    {
        public RenderTexture scene, hit, temp, history, output, m_Resolve;
        public RenderTexture[] mips;
        public ComputeShader compute;
        public UnityEngine.Rendering.PostProcessing.PropertySheet sheet;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Render(Context context)
        {
            var cb = context.command;
            UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, scene, hit, sheet, 0, false, null);
            if (context.SceneView)
                UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, scene, m_Resolve, sheet, 1, false, null);
            else
            {
                if (context.Reset)
                    UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, scene, history, false, null);
                UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, scene, temp, sheet, 1, false, null);
                UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, temp, m_Resolve, sheet, 2, false, null);
                cb.CopyTexture(m_Resolve, 0, 0, history, 0, 0);
            }
            RenderTargetIdentifier source = m_Resolve;
            for (int i = 0; i < mips.Length; i++)
            {
                cb.SetComputeTextureParam(compute, 0, "_Source", source);
                cb.SetComputeTextureParam(compute, 0, "_Result", mips[i]);
                cb.DispatchCompute(compute, 0, 1, 1, 1);
                cb.CopyTexture(mips[i], 0, 0, m_Resolve, 0, i + 1);
                source = mips[i];
            }
            cb.SetGlobalTexture("_SsrFixtureResolve", m_Resolve);
            UnityEngine.Rendering.PostProcessing.RuntimeUtilities.BlitFullscreenTriangle(cb, scene, output, sheet, 3, false, null);
        }
    }

    internal sealed class TestDeep
    {
        internal static TestDeep active;
        internal bool recording = true;
        internal Camera camera;
        internal StringBuilder Settings = new StringBuilder();
        internal int Window = 1;
        internal GpuTiming gpu;
        internal Dictionary<string, int> stages = new Dictionary<string, int>();
        internal Dictionary<string, int> ordinals = new Dictionary<string, int>();
        internal struct Stamp { internal int Stage; internal CommandBuffer Buffer; }
        private Stamp BeginRecord(CommandBuffer cb, string name, object input)
        {
            int n = ordinals.TryGetValue(name, out int old) ? old + 1 : 1; ordinals[name] = n;
            name += "/" + n;
            if (!stages.TryGetValue(name, out int id)) { id = 17 + stages.Count; stages.Add(name, id); }
            gpu.DeepMark(cb, id);
            return new Stamp { Stage = id, Buffer = cb };
        }
        private void EndRecord(Stamp stamp) => gpu.DeepMark(stamp.Buffer, stamp.Stage, true);
    }

    private static RenderTexture Target(int size, bool mips = false, bool uav = false)
    {
        var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf) { useMipMap = mips, autoGenerateMips = false, enableRandomWrite = uav };
        rt.Create(); return rt;
    }
    private static Color[] Pixels(RenderTexture rt)
    {
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
        var result = tex.GetPixels(); UnityEngine.Object.DestroyImmediate(tex); RenderTexture.active = previous; return result;
    }
    private static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }

    public static void Run()
    {
        try { Validate(); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); throw; }
    }
    private static void Validate()
    {
        var gpu = new GpuTiming(); gpu.Initialize(Path.GetFullPath("Native/CustomAmbientGpu.dll"), GpuTiming.StageCapacity);
        var go = new GameObject("SSR profiling fixture"); var camera = go.AddComponent<Camera>(); camera.enabled = false;
        var r = new Renderer { scene = Target(16), hit = Target(16), temp = Target(16), history = Target(16), output = Target(16),
            m_Resolve = Target(16, true), mips = new[] { Target(8, false, true), Target(4, false, true) },
            compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Fixture.compute"),
            sheet = new UnityEngine.Rendering.PostProcessing.PropertySheet { material = new Material(Shader.Find("Hidden/SSRProfilingFixture")) } };
        var cb = new CommandBuffer { name = "SSR fixture" };
        var context = new Context { camera = camera, command = cb };
        var clear = new CommandBuffer(); clear.SetRenderTarget(r.scene); clear.ClearRenderTarget(false, true, new Color(.2f, .4f, .6f, 1)); Graphics.ExecuteCommandBuffer(clear); clear.Dispose();
        var baselines = new List<Color[]>(); var baselinePasses = new List<int[]>();
        for (int branch = 0; branch < 3; branch++)
        {
            context.Reset = branch == 1; context.SceneView = branch == 2; cb.Clear();
            UnityEngine.Rendering.PostProcessing.RuntimeUtilities.Passes.Clear(); r.Render(context); Graphics.ExecuteCommandBuffer(cb);
            baselines.Add(Pixels(r.output)); baselinePasses.Add(UnityEngine.Rendering.PostProcessing.RuntimeUtilities.Passes.ToArray());
        }
        var errors = new List<string>(); var mode = SsrProfilingMode.PerPass;
        var bridge = new ResearchBridge(typeof(TestDeep), () => mode, errors.Add);
        SsrPassHooks.SelectSink = bridge.Select;
        var harmony = new Harmony("ftfx.ssr.validation");
        var method = AccessTools.Method(typeof(Renderer), nameof(Renderer.Render));
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.BeforeRender)),
            finalizer: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.AfterRender)),
            transpiler: new HarmonyMethod(typeof(SsrPassHooks), nameof(SsrPassHooks.Transpile)));
        int cases = 0;
        try
        {
            // Warm history, reset history, scene-view branch, baseline mode, wrong camera, no recording,
            // no active profiler, mode frozen for a session, then a new session taking the new mode.
            TestDeep held = null;
            for (int test = 0; test < 9; test++)
            {
                int branch = test < 3 ? test : 0;
                mode = test == 3 || test >= 7 ? SsrProfilingMode.NativeMarkersOnly : SsrProfilingMode.PerPass;
                var deep = test == 7 ? held : new TestDeep { camera = camera, gpu = gpu };
                if (test == 0) held = deep;
                deep.Window = test + 1; deep.ordinals.Clear();
                deep.recording = test != 5;
                if (test == 4) deep.camera = null;
                TestDeep.active = test == 6 ? null : deep;
                context.Reset = branch == 1; context.SceneView = branch == 2; cb.Clear();
                UnityEngine.Rendering.PostProcessing.RuntimeUtilities.Passes.Clear();
                var ticket = gpu.Reserve(100 + test, test + 1, camera.GetInstanceID(), 0);
                Require(ticket != IntPtr.Zero, "No GPU ticket"); gpu.Event(cb, 1, ticket);
                r.Render(context); gpu.Event(cb, 2, ticket); Graphics.ExecuteCommandBuffer(cb);
                Require(Pixels(r.output).SequenceEqual(baselines[branch]), "Markers changed pixels, case " + test);
                Require(UnityEngine.Rendering.PostProcessing.RuntimeUtilities.Passes.SequenceEqual(baselinePasses[branch]), "Native passes/arguments changed");
                gpu.Immediate(3); Pixels(r.output); gpu.Immediate(4); Pixels(r.output);
                var batch = new GpuTiming.Row[512]; var rows = new List<GpuTiming.Row>(); int count;
                while ((count = gpu.Read(batch)) > 0) rows.AddRange(batch.Take(count));
                Require(rows.All(row => row.Flags == 0), "Invalid GPU query");
                var root = rows.Single(row => row.Stage == 0);
                bool measured = test < 3 || test == 7;
                int expected = measured ? (branch == 1 ? 10 : branch == 2 ? 7 : 9) : 0;
                Require(rows.Count(row => row.Stage >= 17) == expected, "Wrong pass coverage case " + test + ": " + rows.Count);
                foreach (var row in rows.Where(row => row.Stage >= 17))
                    Require(row.Begin >= root.Begin && row.End <= root.End && row.End >= row.Begin, "Timestamp outside executed root");
                var ordered = rows.Where(row => row.Stage >= 17).OrderBy(row => row.Begin).ToArray();
                for (int i = 1; i < ordered.Length; i++) Require(ordered[i - 1].End <= ordered[i].Begin, "Pass timestamps overlap");
                if (measured)
                {
                    Require(deep.stages.Keys.Count(key => key.StartsWith("SSR-pass/mip-filter/")) == 2, "Mip dispatch ordinals missing");
                    Require(deep.stages.Keys.Count(key => key.StartsWith("SSR-pass/mip-copy/")) == 2, "Mip copy ordinals missing");
                    Require(deep.Settings.ToString().Contains("mode=PerPass"), "Capture metadata missing/frozen mode wrong");
                }
                Require(gpu.Pending == 0 && gpu.Dropped == 0, "Queries did not drain"); cases++;
            }
            Require(errors.Count == 0, string.Join("\n", errors));
            bool refused = false;
            try { SsrPassHooks.Transpile(new[] { new CodeInstruction(System.Reflection.Emit.OpCodes.Ret) }).ToList(); }
            catch (InvalidOperationException) { refused = true; }
            Require(refused, "Changed execution contract accepted");
        }
        finally { harmony.UnpatchSelf(); SsrPassHooks.SelectSink = null; TestDeep.active = null; gpu.Dispose(); cb.Dispose(); }
        context.Reset = false; context.SceneView = false; var after = new CommandBuffer(); context.command = after;
        r.Render(context); Graphics.ExecuteCommandBuffer(after); Require(Pixels(r.output).SequenceEqual(baselines[0]), "Cleanup changed rendering"); after.Dispose();
        foreach (var rt in new[] { r.scene, r.hit, r.temp, r.history, r.output, r.m_Resolve }.Concat(r.mips)) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        UnityEngine.Object.DestroyImmediate(r.sheet.material); UnityEngine.Object.DestroyImmediate(go);
        File.WriteAllText("validation.txt", "PASSED " + cases + " D3D11 GPU timestamp cases: warm/reset/scene-view branches, per-mip scopes, native pass/pixel parity, recording/camera/session gating, live mode changes between sessions, cleanup and changed-contract rejection.\nLive EFT per-pass costs remain unmeasured.\n");
    }
}
