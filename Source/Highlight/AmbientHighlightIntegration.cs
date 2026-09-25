using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Highlight
{
    // Suppress only highlight-specific preparation and draws, not the shared sky SH job.
    internal sealed class AmbientHighlightIntegration : IDisposable
    {
        private static AmbientHighlightIntegration active;
        private Harmony harmony;
        private FieldInfo managerField;
        private readonly HashSet<AmbientHighlight> detached = new HashSet<AmbientHighlight>();
        private readonly Dictionary<AmbientHighlight, SphericalHarmonicsL2> pending = new Dictionary<AmbientHighlight, SphericalHarmonicsL2>();
        private readonly List<AmbientHighlight> scratch = new List<AmbientHighlight>();
        private bool wasEnabled = true;
        private int cleanupFrame;

        internal void Initialize()
        {
            try
            {
                managerField = AccessTools.Field(typeof(AmbientHighlight), "_ambientBufferManager");
                if (managerField == null || managerField.FieldType != typeof(CommandBufferManager))
                    throw new MissingFieldException("AmbientHighlight._ambientBufferManager");
                var render = Require("ManualOnRenderObject", typeof(Camera));
                var record = Require("UpdateAmbientBuffer", typeof(CommandBuffer), typeof(Camera));
                var lighting = Require("SetSH", typeof(SphericalHarmonicsL2));
                harmony = new Harmony("com.rootdarkarchon.fuckthesefx.ambient-highlight");
                harmony.Patch(render, prefix: new HarmonyMethod(typeof(AmbientHighlightIntegration), nameof(BeforeRender)) { priority = Priority.First });
                harmony.Patch(record, prefix: new HarmonyMethod(typeof(AmbientHighlightIntegration), nameof(BeforeRecord)) { priority = Priority.First });
                harmony.Patch(lighting, prefix: new HarmonyMethod(typeof(AmbientHighlightIntegration), nameof(BeforeLighting)) { priority = Priority.First });
                active = this;
                Update();
                Plugin.MyLog.LogInfo("Ambient Highlight control ready.");
            }
            catch (Exception error) { Fail(error); }
        }

        private static MethodInfo Require(string name, params Type[] parameters)
        {
            var method = AccessTools.DeclaredMethod(typeof(AmbientHighlight), name, parameters);
            if (method == null || method.IsStatic || method.ReturnType != typeof(void)) throw new MissingMethodException("AmbientHighlight." + name);
            return method;
        }

        private void Detach(AmbientHighlight owner)
        {
            if (owner == null || detached.Contains(owner)) return;
            // Native cleanup removes buffers from every owning camera and empties manager caches.
            // Native OnRenderObject recreates them after re-enabling.
            (managerField.GetValue(owner) as CommandBufferManager)?.DestroyBuffers();
            detached.Add(owner);
        }

        private void RestoreLighting(AmbientHighlight owner)
        {
            detached.Remove(owner);
            if (!pending.TryGetValue(owner, out var sh)) return;
            pending.Remove(owner);
            if (owner != null) owner.SetSH(sh);
        }

        private static bool BeforeRender(AmbientHighlight __instance)
        {
            var self = active;
            if (self == null) return true;
            try
            {
                if (Plugin.AmbientHighlightEnabled.Value) { self.RestoreLighting(__instance); return true; }
                self.Detach(__instance);
                return false;
            }
            catch (Exception error) { self.Fail(error); return true; }
        }

        private static bool BeforeRecord(AmbientHighlight __instance, CommandBuffer __0)
        {
            if (active == null || Plugin.AmbientHighlightEnabled.Value) return true;
            // Also cover direct callers that bypass ManualOnRenderObject.
            __0?.Clear();
            return false;
        }

        private static bool BeforeLighting(AmbientHighlight __instance, SphericalHarmonicsL2 __0)
        {
            var self = active;
            if (self == null) return true;
            if (Plugin.AmbientHighlightEnabled.Value) { self.pending.Remove(__instance); return true; }
            self.pending[__instance] = __0;
            return false;
        }

        internal void Update()
        {
            if (active != this) return;
            try
            {
                bool enabled = Plugin.AmbientHighlightEnabled.Value;
                if (enabled != wasEnabled)
                {
                    if (!enabled)
                    {
                        foreach (var owner in UnityEngine.Object.FindObjectsOfType<AmbientHighlight>(true)) Detach(owner);
                    }
                    else
                    {
                        scratch.Clear(); scratch.AddRange(pending.Keys);
                        foreach (var owner in scratch) RestoreLighting(owner);
                        scratch.Clear(); detached.Clear();
                    }
                    wasEnabled = enabled;
                }
                if (++cleanupFrame % 300 != 0) return;
                detached.RemoveWhere(owner => owner == null);
                scratch.Clear();
                foreach (var owner in pending.Keys) if (owner == null) scratch.Add(owner);
                foreach (var owner in scratch) pending.Remove(owner);
                scratch.Clear();
            }
            catch (Exception error) { Fail(error); }
        }

        private void Fail(Exception error)
        {
            Plugin.MyLog.LogError("Ambient Highlight control unavailable; restoring native behavior. " + error);
            Dispose();
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony?.UnpatchSelf(); harmony = null;
            foreach (var pair in pending) if (pair.Key != null) pair.Key.SetSH(pair.Value);
            pending.Clear(); detached.Clear(); scratch.Clear();
        }
    }
}
