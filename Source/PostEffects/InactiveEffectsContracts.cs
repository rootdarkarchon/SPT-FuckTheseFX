using System;
using UnityStandardAssets.ImageEffects;

namespace FuckTheseFX.Source.PostEffects
{
    internal static class InactiveEffectsContracts
    {
        // Copy elision depends on exact neutral predicates and the native
        // current-texture consumer. Do not silently enable it on changed binaries.
        internal static void Validate()
        {
            if (typeof(DeathFade).Module.ModuleVersionId != new Guid("cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7") ||
                typeof(DepthOfField).Module.ModuleVersionId != new Guid("79ed6e2f-9190-49a0-a4ee-138078f70dc9") ||
                typeof(SSAAPropagator).Module.ModuleVersionId != new Guid("bb1716ca-81b5-4288-9a04-95ef431a6ddf"))
                throw new NotSupportedException("Inactive-effect native contracts changed; original rendering retained.");
        }
    }
}
