using UnityEngine;

namespace FuckTheseFX.Source.DistantShadows
{
    internal static class DistantShadowSamples
    {
        internal static int Select(int resolution, int quarter, int half, int full) =>
            Mathf.Clamp(resolution == 0 ? quarter : resolution == 1 ? half : full, 1, 25);

        // Stratify over the existing 5x5 footprint, rather than taking the first N
        // grid entries (which would sample only one side of the shadow).
        internal static Vector4[] Taps(int count)
        {
            count = Mathf.Clamp(count, 1, 25);
            var taps = new Vector4[25];
            if (count == 25)
            {
                for (int i = 0; i < 25; i++) taps[i] = new Vector4(i % 5 - 2.5f, i / 5 - 2.5f, 0, 0);
                return taps;
            }
            // Equal-area strata, centered within each stratum; fixed between frames.
            // Keep the native footprint center (-.5,-.5) and width (five texels).
            int rows = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count)));
            int index = 0;
            for (int y = 0; y < rows; y++)
            {
                int columns = count / rows + (y < count % rows ? 1 : 0);
                float start = (float)index / count;
                float height = (float)columns / count;
                for (int x = 0; x < columns; x++)
                    taps[index++] = new Vector4(-3f + 5f * (x + .5f) / columns,
                        -3f + 5f * (start + height * .5f), 0, 0);
            }
            return taps;
        }
    }
}
