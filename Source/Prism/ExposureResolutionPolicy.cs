using System;

namespace FuckTheseFX.Source.Prism
{
    internal static class ExposureResolutionPolicy
    {
        internal static int Scale(int nativeSide, int percent)
        {
            percent = Math.Max(1, Math.Min(100, percent));
            // Preserve the game's integer division exactly at 100%, including
            // its optional median-DOF input. Only the exposure grid is scaled.
            if (percent == 100) return nativeSide;
            return Math.Max(1, (int)Math.Floor(nativeSide * (double)percent / 100.0));
        }
    }
}
