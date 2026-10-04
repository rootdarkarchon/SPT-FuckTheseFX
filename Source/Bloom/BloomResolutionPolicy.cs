using System;

namespace FuckTheseFX.Source.Bloom
{
    internal static class BloomResolutionPolicy
    {
        internal static void Resolve(int sourceWidth, int sourceHeight, float scale, out int width, out int height)
        {
            width = sourceWidth;
            height = sourceHeight;
            if (float.IsNaN(scale) || float.IsInfinity(scale)) scale = 1f;
            scale = Math.Max(.25f, Math.Min(1f, scale));
            width = Math.Max(1, (int)Math.Round(sourceWidth * (double)scale, MidpointRounding.AwayFromZero));
            height = Math.Max(1, (int)Math.Round(sourceHeight * (double)scale, MidpointRounding.AwayFromZero));
        }
    }
}
