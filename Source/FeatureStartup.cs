using System;

namespace FuckTheseFX
{
    internal static class FeatureStartup
    {
        internal static bool TryInitialize(string feature, Action initialize)
        {
            try { initialize(); return true; }
            catch (Exception error)
            {
                Plugin.MyLog.LogError(feature + " initialization failed; continuing with the other FX controls. " + error);
                return false;
            }
        }
    }
}
