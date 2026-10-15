using System;
using System.Diagnostics;
using System.Globalization;

namespace FuckTheseFX
{
    internal static class FeatureStartup
    {
        internal static bool TryInitialize(string feature, Action initialize)
        {
            var timer = Stopwatch.StartNew();
            try { initialize(); return true; }
            catch (Exception error)
            {
                Plugin.MyLog.LogError(feature + " initialization failed; continuing with the other FX controls. " + error);
                return false;
            }
            finally { Report(feature, timer); }
        }

        internal static void Report(string stage, Stopwatch timer) =>
            Plugin.MyLog.LogInfo("FTFX startup: " + stage + " = " +
                timer.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms (inclusive wall time)");
    }
}
