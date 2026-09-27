using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace FuckTheseFX.Research
{
    internal interface ISsrStageSink { void Begin(string name); void End(); }

    // Inserts GPU markers around original calls. Arguments, calls and shader passes remain intact.
    internal static class SsrPassHooks
    {
        internal static Func<object, object, ISsrStageSink> SelectSink;
        [ThreadStatic] private static ISsrStageSink current;
        private static readonly string[] Labels = {
            "raymarch", "resolve-scene-view", "history-seed", "resolve", "temporal",
            "history-copy", "mip-filter", "mip-copy", "composite"
        };
        private static readonly string[] Calls = {
            "BlitFullscreenTriangle", "BlitFullscreenTriangle", "BlitFullscreenTriangle",
            "BlitFullscreenTriangle", "BlitFullscreenTriangle", "CopyTexture",
            "DispatchCompute", "CopyTexture", "BlitFullscreenTriangle"
        };

        internal static void BeforeRender(object __instance, object __0, out ISsrStageSink __state)
        {
            __state = current;
            current = SelectSink?.Invoke(__instance, __0);
        }
        internal static Exception AfterRender(Exception __exception, ISsrStageSink __state)
        {
            current = __state;
            return __exception;
        }
        internal static void BeginStage(string name) => current?.Begin(name);
        internal static void EndStage() => current?.End();

        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var sites = code.Where(IsStageCall).ToArray();
            if (!sites.Select(i => ((MethodInfo)i.operand).Name).SequenceEqual(Calls))
                throw new InvalidOperationException("Native SSR execution call order changed; profiling refused.");
            // Startup additionally pins the installed assembly hash. Reject unexpected exception regions.
            if (sites.Any(i => i.blocks.Count != 0))
                throw new InvalidOperationException("Unexpected exception boundary at an SSR GPU call.");
            var begin = AccessTools.Method(typeof(SsrPassHooks), nameof(BeginStage));
            var end = AccessTools.Method(typeof(SsrPassHooks), nameof(EndStage));
            int stage = 0;
            foreach (var instruction in code)
            {
                if (!IsStageCall(instruction)) { yield return instruction; continue; }
                var marker = new CodeInstruction(OpCodes.Ldstr, "SSR-pass/" + Labels[stage++]);
                marker.MoveLabelsFrom(instruction);
                yield return marker;
                yield return new CodeInstruction(OpCodes.Call, begin);
                yield return instruction;
                yield return new CodeInstruction(OpCodes.Call, end);
            }
        }

        private static bool IsStageCall(CodeInstruction i) =>
            (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && i.operand is MethodInfo m &&
            ((m.DeclaringType.FullName == "UnityEngine.Rendering.PostProcessing.RuntimeUtilities" && m.Name == "BlitFullscreenTriangle") ||
             (m.DeclaringType.FullName == "UnityEngine.Rendering.CommandBuffer" && (m.Name == "CopyTexture" || m.Name == "DispatchCompute")));
    }
}
