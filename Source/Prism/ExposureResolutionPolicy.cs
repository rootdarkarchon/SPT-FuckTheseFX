using System;

namespace FuckTheseFX.Source.Prism
{
    internal static class ExposureResolutionPolicy
    {
        internal static int Scale(int nativeSide, float percent)
        {
            if (float.IsNaN(percent) || float.IsInfinity(percent)) percent = 100f;
            percent = Math.Max(1f, Math.Min(100f, percent));
            // Preserve the game's integer division exactly at 100%, including
            // its optional median-DOF input. Only the exposure grid is scaled.
            if (percent == 100f) return nativeSide;
            return Math.Max(1, (int)Math.Floor(nativeSide * (double)percent / 100.0));
        }
    }
}
