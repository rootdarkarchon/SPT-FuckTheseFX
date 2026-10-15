using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.DistantShadows
{
    // Substitute only ComputeShadowMaskMaterial pass 0 at its native recording site.
    // EFT retains allocation and command ordering for the selected resolution.
    internal sealed class DistantShadowSamplingIntegration : IDisposable
    {
        private static DistantShadowSamplingIntegration active;
        private AssetBundle bundle;
        private Shader shader;
        private Harmony harmony;
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        private readonly Vector4[][] taps = new Vector4[26][];
        private readonly Dictionary<CommandBuffer, Recording> recordings = new Dictionary<CommandBuffer, Recording>();
        private readonly List<Material> dead = new List<Material>();
        private MethodInfo method;
        private FieldInfo mainBuffer, opticBuffer;
        private bool warned;
        private int cleanupFrame;
        private sealed class Recording
        {
            internal DistantShadow Owner;
            internal int Current, Previous;
            internal Vector4 Projection, Parameters;
            internal Rect LowViewport, FullViewport;
            internal DistantShadow.ResolutionState Resolution;
            internal RenderTexture Depth, Mask, Blur, HighMask, HighBuffer;
            internal object[] Arguments(CommandBuffer cb) => new object[] { cb, Current, Previous, Projection,
                Parameters, LowViewport, FullViewport, Resolution, Depth, Mask, Blur, HighMask, HighBuffer };
        }

        internal void Initialize(string pluginPath)
        {
            try
            {
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                    throw new NotSupportedException("Distant shadow sampling requires Direct3D11.");
                // Assets are fixed for this supported game build. Full content hashes
                // belong to build validation, not the executable-to-menu path.
                if (typeof(DistantShadow).Module.ModuleVersionId != new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7"))
                    throw new NotSupportedException("Unsupported EFT distant-shadow ABI; expected EFT 40743.");
                method = AccessTools.DeclaredMethod(typeof(DistantShadow), "SetupComputeShadowMaskCommandBuffer",
                    new[] { typeof(CommandBuffer), typeof(int), typeof(int), typeof(Vector4), typeof(Vector4),
                        typeof(Rect), typeof(Rect), typeof(DistantShadow.ResolutionState), typeof(RenderTexture),
                        typeof(RenderTexture), typeof(RenderTexture), typeof(RenderTexture), typeof(RenderTexture) });
                if (method == null) throw new MissingMethodException("DistantShadow.SetupComputeShadowMaskCommandBuffer");
                mainBuffer = AccessTools.Field(typeof(DistantShadow), "_computeMaskCmdBuf")
                    ?? throw new MissingFieldException("DistantShadow._computeMaskCmdBuf");
                opticBuffer = AccessTools.Field(typeof(DistantShadow), "_computeMaskOpticCmdBuf")
                    ?? throw new MissingFieldException("DistantShadow._computeMaskOpticCmdBuf");
                bundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(pluginPath), "Assets", "distantshadows"));
                shader = bundle != null ? bundle.LoadAsset<Shader>("Assets/DistantShadowSampling.shader") : null;
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Distant shadow shader missing or unsupported.");
                for (int count = 1; count <= 25; count++)
                {
                    taps[count] = DistantShadowSamples.Taps(count);
                }
                harmony = new Harmony("com.rootdarkarchon.fuckthesefx.distant-shadow-sampling");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(DistantShadowSamplingIntegration), nameof(ReplaceMaskDraw)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(DistantShadowSamplingIntegration), nameof(AfterRecording)));
                active = this;
                Plugin.MyLog.LogInfo("Distant shadow filter controls ready; native allocation and 50-step parallax searches retained.");
            }
            catch (Exception error)
            {
                Dispose();
                Plugin.MyLog.LogError("Distant shadow sampling unavailable; retaining EFT rendering. " + error);
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceMaskDraw(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var native = AccessTools.Method(typeof(CommandBuffer), nameof(CommandBuffer.DrawMesh),
                new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int), typeof(int) });
            var replacement = AccessTools.Method(typeof(DistantShadowSamplingIntegration), nameof(SelectMaterial));
            int matches = 0;
            for (int i = 3; i < code.Count; i++)
            {
                if (!code[i].Calls(native) || code[i - 1].opcode != OpCodes.Ldc_I4_0 ||
                    code[i - 2].opcode != OpCodes.Ldc_I4_0 || code[i - 3].opcode != OpCodes.Ldfld ||
                    !(code[i - 3].operand is FieldInfo field) || field.Name != "ComputeShadowMaskMaterial" ||
                    field.DeclaringType != typeof(DistantShadow)) continue;
                // Insert after the material load. Leave DrawMesh itself untouched so
                // other instrumentation can still wrap the actual recorded GPU draw.
                int insert = i - 2;
                code.InsertRange(insert, new[] { new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_S, (byte)8), new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, replacement) });
                i += 4; matches++;
            }
            if (matches != 1) throw new InvalidOperationException("Expected one native shadow-mask pass 0; found " + matches);
            return code;
        }

        private static Material SelectMaterial(Material original, DistantShadow owner,
            DistantShadow.ResolutionState resolution, CommandBuffer cb)
        {
            var integration = active;
            if (integration != null && Plugin.DistantShadowsEnabled.Value && Plugin.DistantShadowSamplingEnabled.Value && owner != null &&
                original != null && original.shader != null && original.shader.name == "EFT/DistantShadowComputeMask" &&
                (int)resolution >= 0 && (int)resolution <= 2 && owner.EnableDistantShadowPCF &&
                !owner.EnableAdaptiveBias && !owner.EnableMultiviewTiles && !owner.EnablePCFShift &&
                !original.IsKeywordEnabled("ADAPTIVE_DEPTH_BIAS") && !original.IsKeywordEnabled("DISTANT_SHADOW_MULTIPROJECTION") &&
                !original.IsKeywordEnabled("ENABLE_PCF_SHIFT"))
            {
                int count = DistantShadowSamples.Select((int)resolution, Plugin.DistantShadowQuarterSamples.Value,
                    Plugin.DistantShadowHalfSamples.Value, Plugin.DistantShadowFullSamples.Value);
                // 25 is an exact native comparison path, not merely equivalent math.
                if (count < 25 && integration.IsMaskBuffer(owner, cb))
                {
                    try
                    {
                        if (!integration.materials.TryGetValue(original, out var material) || material == null)
                        {
                            material = new Material(original) { shader = integration.shader, hideFlags = HideFlags.HideAndDontSave };
                            integration.materials[original] = material;
                        }
                        material.CopyPropertiesFromMaterial(original);
                        // Command-ordered globals belong to this camera's recorded draw,
                        // so later settings/cameras cannot overwrite its selected budget.
                        cb.SetGlobalInt("_FTFX_DSCount", count);
                        cb.SetGlobalVectorArray("_FTFX_DSTaps", integration.taps[count]);
                        return material;
                    }
                    catch (Exception error)
                    {
                        if (!integration.warned) Plugin.MyLog.LogWarning("Distant shadow draw retained natively: " + error);
                        integration.warned = true;
                    }
                }
            }
            return original;
        }

        private bool IsMaskBuffer(DistantShadow owner, CommandBuffer cb)
        {
            return owner != null && owner.MasterCamera != null && cb != null &&
                (ReferenceEquals(mainBuffer.GetValue(owner), cb) || ReferenceEquals(opticBuffer.GetValue(owner), cb));
        }

        private static void AfterRecording(DistantShadow __instance, CommandBuffer __0, int __1, int __2,
            Vector4 __3, Vector4 __4, Rect __5, Rect __6, DistantShadow.ResolutionState __7,
            RenderTexture __8, RenderTexture __9, RenderTexture __10, RenderTexture __11, RenderTexture __12)
        {
            var integration = active;
            if (integration == null) return;
            if (!integration.IsMaskBuffer(__instance, __0)) return;
            // Keep latest native arguments so disposing can remove retained replacement draws.
            if (!integration.recordings.TryGetValue(__0, out var recording))
                integration.recordings[__0] = recording = new Recording();
            recording.Owner = __instance; recording.Current = __1; recording.Previous = __2;
            recording.Projection = __3; recording.Parameters = __4;
            recording.LowViewport = __5; recording.FullViewport = __6; recording.Resolution = __7;
            recording.Depth = __8; recording.Mask = __9; recording.Blur = __10;
            recording.HighMask = __11; recording.HighBuffer = __12;
        }

        internal void Update()
        {
            if (++cleanupFrame < 300) return;
            cleanupFrame = 0;
            dead.Clear();
            foreach (var pair in materials) if (pair.Key == null) dead.Add(pair.Key);
            foreach (var material in dead) { DestroyMaterial(materials[material]); materials.Remove(material); }
            foreach (var cb in recordings.Where(p => p.Value.Owner == null).Select(p => p.Key).ToArray()) recordings.Remove(cb);
        }

        public void Dispose()
        {
            if (active == this) active = null;
            var patchOwner = harmony; harmony = null;
            try { if (patchOwner != null && method != null) patchOwner.Unpatch(method, HarmonyPatchType.All, patchOwner.Id); }
            catch (Exception error) { Plugin.MyLog.LogWarning("Distant shadow hook cleanup failed; override is inactive. " + error); }
            foreach (var pair in recordings)
            {
                if (pair.Value.Owner == null || !IsMaskBuffer(pair.Value.Owner, pair.Key)) continue;
                try { pair.Key.Clear(); method.Invoke(pair.Value.Owner, pair.Value.Arguments(pair.Key)); }
                catch (Exception error) { Plugin.MyLog.LogWarning("Distant shadow native buffer restoration: " + error); }
            }
            recordings.Clear();
            foreach (var material in materials.Values) DestroyMaterial(material);
            materials.Clear();
            if (bundle != null) bundle.Unload(false);
            bundle = null; shader = null;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
