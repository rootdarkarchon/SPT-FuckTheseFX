using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FuckTheseFX.Source.Prism
{
    // Patch only the first allocation in the installed exposure reducer.
    // Tone mapping, the scene input, adaptation, and subsequent reductions stay native.
    internal sealed class PrismExposureIntegration : IDisposable
    {
        private Harmony harmony;
        private MethodInfo method;

        internal void Initialize()
        {
            try
            {
                var type = AccessTools.TypeByName("PrismEffects");
                method = type == null ? null : AccessTools.DeclaredMethod(type, "HandleExposure",
                    new[] { typeof(RenderTexture), typeof(RenderTextureFormat) });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                    throw new MissingMethodException("PrismEffects.HandleExposure");
                harmony = new Harmony("com.rootdarkarchon.fuckthesefx.prism-exposure");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(PrismExposureIntegration), nameof(ResizeExposureInput)));
                Plugin.MyLog.LogInfo("Prism exposure resolution control ready.");
            }
            catch { Dispose(); throw; }
        }

        internal static IEnumerable<CodeInstruction> ResizeExposureInput(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var width = AccessTools.PropertyGetter(typeof(Texture), nameof(Texture.width));
            var allocation = AccessTools.Method(typeof(RenderTexture), nameof(RenderTexture.GetTemporary),
                new[] { typeof(int), typeof(int), typeof(int), typeof(RenderTextureFormat) });
            // EFT 40743: source.width / 2, source.width / 2, 0, format.
            // Reject changed contracts instead of resizing another Prism allocation.
            if (code.Count < 11 || code[0].opcode != OpCodes.Ldarg_1 || !code[1].Calls(width) ||
                code[2].opcode != OpCodes.Ldc_I4_2 || code[3].opcode != OpCodes.Div ||
                code[4].opcode != OpCodes.Ldarg_1 || !code[5].Calls(width) ||
                code[6].opcode != OpCodes.Ldc_I4_2 || code[7].opcode != OpCodes.Div ||
                code[8].opcode != OpCodes.Ldc_I4_0 || code[9].opcode != OpCodes.Ldarg_2 ||
                !code[10].Calls(allocation))
                throw new NotSupportedException("Unsupported Prism exposure input allocation");
            code[10].opcode = OpCodes.Call;
            code[10].operand = AccessTools.Method(typeof(PrismExposureIntegration), nameof(GetExposureInput));
            return code;
        }

        internal static RenderTexture GetExposureInput(int width, int height, int depth, RenderTextureFormat format)
        {
            float percent = Plugin.PrismExposurePercent.Value;
            return RenderTexture.GetTemporary(ExposureResolutionPolicy.Scale(width, percent),
                ExposureResolutionPolicy.Scale(height, percent), depth, format);
        }

        public void Dispose()
        {
            var owner = harmony;
            harmony = null;
            if (owner != null && method != null) owner.Unpatch(method, HarmonyPatchType.All, owner.Id);
        }
    }
}
