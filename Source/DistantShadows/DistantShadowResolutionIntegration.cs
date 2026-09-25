using System;
using System.Collections.Generic;
using HarmonyLib;

namespace FuckTheseFX.Source.DistantShadows
{
    public enum DistantShadowMaskResolution { Quarter = 0, Half = 1, Full = 2 }

    internal sealed class DistantShadowResolutionState
    {
        private bool owned;
        private int external, applied;

        internal int Select(int current, int requested)
        {
            if (requested < 0 || requested > 2)
            {
                int restored = owned && current == applied ? external : current;
                owned = false;
                return restored;
            }
            if (!owned || current != applied) external = current;
            owned = true;
            applied = requested;
            return requested;
        }
    }

    internal sealed class DistantShadowResolutionIntegration : IDisposable
    {
        private static DistantShadowResolutionIntegration active;
        private Harmony harmony;
        private readonly Dictionary<DistantShadow, DistantShadowResolutionState> states = new Dictionary<DistantShadow, DistantShadowResolutionState>();
        private readonly List<DistantShadow> dead = new List<DistantShadow>();
        private int cleanupFrame;

        internal void Initialize()
        {
            try
            {
                var update = AccessTools.DeclaredMethod(typeof(DistantShadow), "Update", Type.EmptyTypes);
                if (update == null || update.ReturnType != typeof(void)) throw new MissingMethodException("DistantShadow.Update");
                if ((int)DistantShadow.ResolutionState.QUARTER != 0 || (int)DistantShadow.ResolutionState.HALF != 1 ||
                    (int)DistantShadow.ResolutionState.FULL != 2) throw new NotSupportedException("Distant shadow resolution enum changed.");
                harmony = new Harmony("com.rootdarkarchon.fuckthesefx.distant-shadow-resolution");
                harmony.Patch(update, prefix: new HarmonyMethod(typeof(DistantShadowResolutionIntegration), nameof(BeforeUpdate)) { priority = Priority.Last });
                active = this;
                Plugin.MyLog.LogInfo("Distant shadow resolution control ready.");
            }
            catch (Exception error)
            {
                Dispose();
                Plugin.MyLog.LogError("Distant shadow resolution control unavailable; retaining external selection. " + error);
            }
        }

        private static void BeforeUpdate(DistantShadow __instance)
        {
            var self = active;
            if (self == null || __instance == null) return;
            int requested = Plugin.DistantShadowsEnabled.Value ? (int)Plugin.DistantShadowResolution.Value : -1;
            bool overriding = requested >= 0 && requested <= 2;
            if (!self.states.TryGetValue(__instance, out var state))
            {
                if (!overriding) return;
                state = new DistantShadowResolutionState();
                self.states.Add(__instance, state);
            }
            // Run before EFT checks texture dimensions and records either camera's buffers.
            __instance.CurrentMaskResolution = (DistantShadow.ResolutionState)state.Select((int)__instance.CurrentMaskResolution, requested);
            if (!overriding) self.states.Remove(__instance);
        }

        internal void Update()
        {
            if (++cleanupFrame % 300 != 0) return;
            dead.Clear();
            foreach (var pair in states) if (pair.Key == null) dead.Add(pair.Key);
            foreach (var owner in dead) states.Remove(owner);
            dead.Clear();
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony?.UnpatchSelf();
            harmony = null;
            foreach (var pair in states)
                if (pair.Key != null) pair.Key.CurrentMaskResolution = (DistantShadow.ResolutionState)pair.Value.Select((int)pair.Key.CurrentMaskResolution, -1);
            states.Clear();
        }
    }
}
