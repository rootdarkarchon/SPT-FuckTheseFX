using UnityEngine;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal static class SsrCameraPolicy
    {
        internal static bool EligibleOptic(bool matches, bool active, bool hasScope, bool nightScope, bool thermalScope,
            bool nightActive, bool thermalActive, bool supported) =>
            matches && active && hasScope && !nightScope && !thermalScope && !nightActive && !thermalActive && supported;

        internal static ReflectionResolution Lower(ReflectionResolution resolution)
        {
            switch(resolution) {
                case ReflectionResolution.R2048:return ReflectionResolution.R1536;
                case ReflectionResolution.R1536:return ReflectionResolution.R1024;
                case ReflectionResolution.R1024:return ReflectionResolution.R768;
                case ReflectionResolution.R768:return ReflectionResolution.R512;
                case ReflectionResolution.R512:return ReflectionResolution.R256;
                case ReflectionResolution.R256:return ReflectionResolution.R256;
                default:return ReflectionResolution.R768; // Matches TraceDimensions' invalid-value R1024 fallback.
            }
        }
        internal static Options Effective(Options configured, bool optic, bool reduceMain)
        {
            var o=configured;
            if(optic) {
                o.Resolution=o.OpticResolution==ReflectionResolution.R768?ReflectionResolution.R768:
                    o.OpticResolution==ReflectionResolution.R512?ReflectionResolution.R512:ReflectionResolution.R256;
                o.Reconstruction=ReflectionReconstruction.Custom;
            } else if(reduceMain) o.Resolution=Lower(o.Resolution);
            return o;
        }
    }

    // One observation per frame prevents camera render order from changing the budget.
    internal sealed class SsrOpticState
    {
        internal const float RestoreDelay=.25f;
        internal Camera Camera {get;private set;}
        internal bool Active {get;private set;}
        internal int Generation {get;private set;}
        private int frame=-1,sight;
        private bool hadActive,blocked;
        private float lastActive;
        internal void Observe(int currentFrame,float time,Camera camera,int currentSight,bool eligible)
        {
            if(frame==currentFrame)return;
            frame=currentFrame;
            if(Camera!=camera || sight!=currentSight || Active!=eligible)Generation++;
            Camera=camera;sight=currentSight;Active=eligible;
            blocked=currentSight!=0 && !eligible;
            if(eligible){lastActive=time;hadActive=true;}
        }
        internal bool ReduceMain(float time)=>Active || (!blocked && hadActive && time-lastActive<RestoreDelay);
        internal void Reset(){frame=-1;Camera=null;sight=0;Active=hadActive=blocked=false;Generation++;}
    }
}
