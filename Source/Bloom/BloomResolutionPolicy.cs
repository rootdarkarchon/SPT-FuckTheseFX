using System;

namespace FuckTheseFX.Source.Bloom
{
    internal static class BloomResolutionPolicy
    {
        internal static void Resolve(int sourceWidth, int sourceHeight, float scale, bool native,
            int nativeWidth, int nativeHeight, out int width, out int height)
        {
            width = sourceWidth;
            height = sourceHeight;
            if (native)
            {
                // An unavailable native size never silently re-enables the slider.
                if (nativeWidth <= 0 || nativeHeight <= 0) return;
                width = Math.Min(sourceWidth, nativeWidth);
                height = Math.Min(sourceHeight, nativeHeight);
                return;
            }
            if (float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
            scale = Math.Max(.25f, Math.Min(1f, scale));
            width = Math.Max(1, (int)Math.Round(sourceWidth * (double)scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (int)Math.Round(sourceHeight * (double)scale, MidpointRounding.AwayFromZero));
        }
    }
}
