using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Bloom
{
    // Runs at the existing post-upscale hook. Only bloom's working source is reduced;
    // both native combine paths sample the untouched scene at the original output size.
    internal sealed class BloomResolutionIntegration : IDisposable
    {
        private static BloomResolutionIntegration active;
        [ThreadStatic] private static FrameState current;
        private Harmony harmony;
        private MethodInfo method;
        private Type ssaaType;
        private MethodInfo inputWidth, inputHeight;
        private bool warned;

        private sealed class FrameState
        {
            internal FrameState Previous;
            internal RenderTexture Original, Reduced;
        }

        internal void Initialize()
        {
            try
            {
                var type = AccessTools.TypeByName("UltimateBloom");
                method = type == null ? null : AccessTools.DeclaredMethod(type, "OnRenderImageInternal",
                    new[] { typeof(RenderTexture), typeof(RenderTexture) });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) ||
                    !typeof(Component).IsAssignableFrom(type))
                    throw new MissingMethodException("UltimateBloom.OnRenderImageInternal");
                if (Harmony.GetPatchInfo(method)?.Owners.Contains("com.matsix.sptaoreplacer.bloom-resolution") == true)
                {
                    Plugin.MyLog.LogWarning("Bloom adjustments skipped: an older AO Replacer still owns bloom. Install AO Replacer 1.3.4 or newer. Ambient Highlight and distant-shadow controls will still initialize.");
                    return;
                }
                ssaaType = AccessTools.TypeByName("SSAA");
                inputWidth = ssaaType == null ? null : AccessTools.Method(ssaaType, "GetInputWidth", Type.EmptyTypes);
                inputHeight = ssaaType == null ? null : AccessTools.Method(ssaaType, "GetInputHeight", Type.EmptyTypes);
                if (inputWidth == null || inputHeight == null || inputWidth.ReturnType != typeof(int) ||
                    inputHeight.ReturnType != typeof(int) || !typeof(Behaviour).IsAssignableFrom(ssaaType))
                    throw new MissingMethodException("SSAA input dimensions");
                harmony = new Harmony("com.rootdarkarchon.fuckthesefx.bloom-resolution");
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(BloomResolutionIntegration), nameof(BeforeBloom)) { priority = Priority.First },
                    transpiler: new HarmonyMethod(typeof(BloomResolutionIntegration), nameof(PreserveSceneSource)),
                    finalizer: new HarmonyMethod(typeof(BloomResolutionIntegration), nameof(AfterBloom)) { priority = Priority.Last });
                active = this;
                Plugin.MyLog.LogInfo("UltimateBloom input resolution controls ready.");
            }
            catch (Exception error)
            {
                Dispose();
                Plugin.MyLog.LogError("Bloom resolution controls unavailable; keeping native bloom. " + error);
            }
        }

        private static IEnumerable<CodeInstruction> PreserveSceneSource(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int material = 0, properties = 0;
            var restore = AccessTools.Method(typeof(BloomResolutionIntegration), nameof(CompositeSource));
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldsfld || !(code[i].operand is FieldInfo field) ||
                    field.Name != "_colorBuffer" || field.DeclaringType.Name != "UltimateBloom" ||
                    code[i + 1].opcode != OpCodes.Ldarg_1 || !(code[i + 2].operand is MethodInfo call) ||
                    call.Name != "SetTexture" || !call.GetParameters().Select(p => p.ParameterType)
                        .SequenceEqual(new[] { typeof(int), typeof(Texture) })) continue;
                if (call.DeclaringType == typeof(Material)) material++;
                else if (call.DeclaringType == typeof(MaterialPropertyBlock)) properties++;
                else continue;
                code.Insert(i + 2, new CodeInstruction(OpCodes.Call, restore));
                i += 3;
            }
            // Fail closed before installing the prefix if either native composition path changes.
            if (material != 1 || properties != 1)
                throw new InvalidOperationException("Unsupported bloom combine source contract: " + material + "/" + properties);
            return code;
        }

        private static RenderTexture CompositeSource(RenderTexture source) =>
            current != null && ReferenceEquals(current.Reduced, source) ? current.Original : source;

        private static void BeforeBloom(Component __instance, ref RenderTexture __0, out FrameState __state)
        {
            __state = null;
            var integration = active;
            if (integration == null) return;
            var original = __0;
            try
            {
                // Isolate nested calls even when the inner camera uses the unscaled path.
                if (current != null)
                {
                    __state = new FrameState { Previous = current, Original = original };
                    current = __state;
                }
                if (!Plugin.BloomEnabled.Value || original == null || original.dimension != TextureDimension.Tex2D || original.volumeDepth != 1) return;
                bool native = Plugin.BloomUseNativeResolution.Value;
                float scale = Plugin.BloomInputScale.Value;
                int nativeWidth = 0, nativeHeight = 0;
                if (native)
                {
                    var ssaa = __instance.GetComponent(integration.ssaaType) as Behaviour;
                    if (ssaa != null && ssaa.isActiveAndEnabled)
                    {
                        nativeWidth = (int)integration.inputWidth.Invoke(ssaa, null);
                        nativeHeight = (int)integration.inputHeight.Invoke(ssaa, null);
                    }
                }
                BloomResolutionPolicy.Resolve(original.width, original.height, scale, native,
                    nativeWidth, nativeHeight, out int width, out int height);
                if (width == original.width && height == original.height) return;
                if (__state == null)
                {
                    __state = new FrameState { Previous = current, Original = original };
                    current = __state;
                }
                var descriptor = original.descriptor;
                descriptor.width = width;
                descriptor.height = height;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                descriptor.enableRandomWrite = false;
                descriptor.useDynamicScale = false;
                descriptor.memoryless = RenderTextureMemoryless.None;
                __state.Reduced = RenderTexture.GetTemporary(descriptor);
                __state.Reduced.name = "FuckTheseFX Bloom Input";
                __state.Reduced.filterMode = FilterMode.Bilinear;
                __state.Reduced.wrapMode = TextureWrapMode.Clamp;
                var previousTarget = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = __state.Reduced.sRGB;
                    Graphics.Blit(original, __state.Reduced);
                }
                finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; }
                __0 = __state.Reduced;
            }
            catch (Exception error)
            {
                Release(__state);
                __state = null;
                __0 = original;
                if (!integration.warned)
                {
                    integration.warned = true;
                    Plugin.MyLog.LogWarning("Bloom input resize failed; using original input. " + error);
                }
            }
        }

        private static Exception AfterBloom(Exception __exception, ref RenderTexture __0, FrameState __state)
        {
            if (__state != null)
            {
                __0 = __state.Original;
                Release(__state);
            }
            return __exception;
        }

        private static void Release(FrameState state)
        {
            if (state == null) return;
            current = state.Previous;
            // OnRenderImageInternal has executed its command buffer by this point, including on
            // the immediate path. Our temporary never joins the game's own temporary collection.
            if (state.Reduced != null) RenderTexture.ReleaseTemporary(state.Reduced);
            state.Reduced = null;
        }

        public void Dispose()
        {
            if (active == this) active = null;
            var owner = harmony;
            harmony = null;
            try
            {
                // Remove this owner's prefix, transpiler and finalizer in one operation.
                // A failed transpiler must not be rerun between separate unpatch steps.
                if (owner != null && method != null) owner.Unpatch(method, HarmonyPatchType.All, owner.Id);
            }
            catch (Exception error) { Plugin.MyLog.LogWarning("Bloom hook cleanup failed; override is inactive. " + error); }
        }
    }
}
