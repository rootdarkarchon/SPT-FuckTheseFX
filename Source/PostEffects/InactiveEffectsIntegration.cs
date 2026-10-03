using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityStandardAssets.ImageEffects;

namespace FuckTheseFX.Source.PostEffects
{
    // The native neutral copies advance SSAA's colour pair. Skip the wrapper
    // and its release together, only inside a confirmed current-frame chain.
    internal sealed class InactiveEffectsIntegration : IDisposable
    {
        private const string Owner = "com.rootdarkarchon.fuckthesefx.inactive-effects";
        private static readonly Dictionary<SSAAPropagator, int> chains = new Dictionary<SSAAPropagator, int>();
        private readonly List<MethodBase> patched = new List<MethodBase>();
        private Harmony harmony;

        internal void Initialize()
        {
            try
            {
                InactiveEffectsContracts.Validate();
                DofPropagator = AccessTools.FieldRefAccess<DepthOfField, SSAAPropagator>("_ssaaPropagator");
                DofFocus = AccessTools.FieldRefAccess<DepthOfField, float>("focalDistance01");
                harmony = new Harmony(Owner);
                Patch(typeof(SSAAPropagator), "FillCommandBuffer", nameof(BeginChain), false);
                Patch(typeof(SSAAImpl), "OnRenderImage", nameof(EndChain), false);
                Patch(typeof(DeathFade), "OnRenderImage", nameof(DeathPrefix));
                Patch(typeof(BloodOnScreen), "OnRenderImage", nameof(BloodPrefix));
                Patch(typeof(PostprocessGrayscale), "OnRenderImage", nameof(GrayscalePrefix));
                Patch(typeof(DistortCameraFX), "OnRenderImage", nameof(DistortPrefix));
                Patch(typeof(InfectionEffect), "OnRenderImage", nameof(InfectionPrefix));
                var dof = AccessTools.DeclaredMethod(typeof(DepthOfField), "OnRenderImage");
                if (dof == null) throw new MissingMethodException("DepthOfField.OnRenderImage");
                patched.Add(dof);
                harmony.Patch(dof, transpiler: new HarmonyMethod(typeof(InactiveEffectsIntegration), nameof(DofZeroExit)));
                SceneManager.sceneLoaded += OnSceneLoaded;
                Plugin.MyLog.LogInfo("Inactive screen-effect bypass ready; DoF retains native CoC writing.");
            }
            catch { Dispose(); throw; }
        }

        private void Patch(Type type, string name, string hook, bool prefix = true)
        {
            var method = AccessTools.DeclaredMethod(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            patched.Add(method);
            var patch = new HarmonyMethod(typeof(InactiveEffectsIntegration), hook);
            if (prefix) harmony.Patch(method, prefix: patch); else harmony.Patch(method, postfix: patch);
        }

        private static void BeginChain(SSAAPropagator __instance) => chains[__instance] = Time.frameCount;
        private static void EndChain(SSAAPropagator ____ssaaPropagator)
        {
            if (!ReferenceEquals(____ssaaPropagator, null)) chains.Remove(____ssaaPropagator);
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => chains.Clear();

        internal static bool HasChain(SSAAPropagator propagator, out RenderTexture source)
        {
            source = null;
            if (!Plugin.SkipInactiveEffects.Value || propagator == null || !propagator.isActiveAndEnabled ||
                !chains.TryGetValue(propagator, out int frame) || frame != Time.frameCount) return false;
            RenderTexture destination = null;
            return propagator.GetSourceDestination(out source, out destination) && source != null && destination != null &&
                source != destination && source.IsCreated() && destination.IsCreated();
        }

        private static bool SkipCopy(SSAAPropagator propagator, RenderTexture unityDestination)
        {
            if (!HasChain(propagator, out _)) return false;
            // The real image is still the current SSAA texture. Bind Unity's
            // unused proxy output without drawing, as required by image callbacks.
            RenderTexture.active = unityDestination;
            return true;
        }

        private static bool DeathPrefix(SSAAPropagator ____ssaaPropagator, float ____closeEyesValue, float ____fadeValue, RenderTexture destination)
            => !(Math.Abs(____closeEyesValue) < Mathf.Epsilon && Math.Abs(____fadeValue) < Mathf.Epsilon && SkipCopy(____ssaaPropagator, destination));

        private static bool BloodPrefix(SSAAPropagator ____ssaaPropagator, float ____lastHitTime, float ___MaxBloodTime, RenderTexture destination)
            => !(Time.time - ____lastHitTime > ___MaxBloodTime && SkipCopy(____ssaaPropagator, destination));

        private static bool GrayscalePrefix(SSAAPropagator ____ssaaPropagator, float ____effectAmount, RenderTexture destination)
            => !(Math.Abs(____effectAmount) < Mathf.Epsilon && SkipCopy(____ssaaPropagator, destination));

        private static bool DistortPrefix(SSAAPropagator ____ssaaPropagator, bool ____doNotRender, RenderTexture destination)
            => !(____doNotRender && SkipCopy(____ssaaPropagator, destination));

        private static bool InfectionPrefix(SSAAPropagator ____ssaaPropagator, ref float ____effectValue, RenderTexture destination)
        {
            float value = Mathf.Clamp01(____effectValue);
            // Match the native unordered comparison, including NaN. Retain its
            // clamp assignment only when replacing the native method.
            if (value > 0f || !SkipCopy(____ssaaPropagator, destination)) return true;
            ____effectValue = value;
            return false;
        }

        private static AccessTools.FieldRef<DepthOfField, SSAAPropagator> DofPropagator;
        private static AccessTools.FieldRef<DepthOfField, float> DofFocus;

        internal static bool CanExitDof(DepthOfField effect, RenderTexture source)
        {
            if (effect.aperture != 0f || effect.visualizeFocus || effect.nearBlur || effect.dofHdrShader == null ||
                effect.dofHdrShader.name != "Hidden/Dof/DepthOfFieldHdr" ||
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11 ||
                !HasChain(DofPropagator(effect), out var current) || current != source) return false;
            // Native CoC must have a writable alpha channel. Other renderers,
            // nonzero blur and invalid parameters always retain the native path.
            var format = source.format;
            float focus = DofFocus(effect);
            return (format == RenderTextureFormat.ARGB32 || format == RenderTextureFormat.ARGBHalf || format == RenderTextureFormat.ARGBFloat) &&
                !float.IsNaN(effect.focalSize) && !float.IsInfinity(effect.focalSize) && !float.IsNaN(focus) && !float.IsInfinity(focus);
        }

        internal static IEnumerable<CodeInstruction> DofZeroExit(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var coc = AccessTools.DeclaredMethod(typeof(DepthOfField), "WriteCoc");
            var getWidth = AccessTools.PropertyGetter(typeof(Texture), nameof(Texture.width));
            // Only the disc branch: WriteCoc(source,true) followed immediately
            // by the first half-resolution allocation. No temps exist yet.
            var sites = Enumerable.Range(3, Math.Max(0, code.Count - 8)).Where(i => code[i].Calls(coc) &&
                code[i-3].opcode == OpCodes.Ldarg_0 && code[i-2].opcode == OpCodes.Ldarg_1 && code[i-1].opcode == OpCodes.Ldc_I4_1 &&
                code[i+1].opcode == OpCodes.Ldarg_1 && code[i+2].Calls(getWidth) && code[i+3].opcode == OpCodes.Ldc_I4_1 && code[i+4].opcode == OpCodes.Shr).ToArray();
            if (sites.Length != 1) throw new NotSupportedException("Unsupported DoF disc allocation boundary");
            int at = sites[0] + 1;
            // Harmony targets Mono's mscorlib Label; use a boxed label at patch
            // startup to avoid its netstandard reference-assembly identity gap.
            object resume = generator.GetType().GetMethod("DefineLabel").Invoke(generator, null);
            var guard = new CodeInstruction(OpCodes.Ldarg_0);
            guard.MoveLabelsFrom(code[at]); guard.MoveBlocksFrom(code[at]);
            ((System.Collections.IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(code[at])).Add(resume);
            code.InsertRange(at, new[] { guard, new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InactiveEffectsIntegration), nameof(CanExitDof))),
                new CodeInstruction(OpCodes.Brfalse, resume), new CodeInstruction(OpCodes.Ret) });
            return code;
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (harmony != null) foreach (var method in patched) harmony.Unpatch(method, HarmonyPatchType.All, Owner);
            harmony = null; patched.Clear(); chains.Clear();
        }
    }
}
