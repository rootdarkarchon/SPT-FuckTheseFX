using BepInEx.Configuration;

namespace FuckTheseFX.Source.Occlusion
{
    public enum ColorBleedResolution { Half = 2, Quarter = 4 }

    internal sealed class ColorBleedSettings
    {
        internal readonly ConfigEntry<bool> Enabled, SpatialFilter, Temporal;
        internal readonly ConfigEntry<ColorBleedResolution> Resolution;
        internal readonly ConfigEntry<int> Samples, HistoryFrames;
        internal readonly ConfigEntry<float> Strength, Radius, DepthTolerance, DonorLimit, BounceLimit, Distance;

        internal ColorBleedSettings(ConfigFile config)
        {
            const string section = "Screen Space Contact GI";
            Enabled = config.Bind(section, "Enabled", false,
                "Experimental short-range diffuse color bounce. Requires FTFX XeGTAO and the main HDR deferred camera. Supports Gamma and Linear color space; Gamma uses an extra scene snapshot for correct compositing. Off by default; excludes actors, optics and transparent surfaces. Performance: small-medium impact at defaults.");
            Strength = Advanced("Strength", 3f, "Brightness of the added bounce, calibrated for sparse sampling. 1 is full strength; up to 4 allows stronger color transfer. Maximum bounce fraction still caps the reflected addition. Debug Screen Space Contact GI gain affects only the diagnostic display.", new AcceptableValueRange<float>(0, 4));
            Resolution = Advanced("Resolution", ColorBleedResolution.Quarter, "Half or quarter of each internal rendering dimension. Quarter is softer and can miss small colored objects.");
            SpatialFilter = Advanced("Spatial filter", true, "Smooths sparse bounce using geometric depth, normal and visibility. With temporal accumulation, young or noisy history receives wider variance-guided filtering. Stable history keeps the narrower filter. Filtered results are not saved into history. Disable for an A/B comparison.");
            Temporal = Advanced("Temporal accumulation", true, "Accumulates varying samples using camera reprojection and surface/history rejection. Reduces speckling; moving objects and changing lighting can still leave short trails. Disable to restore the fixed spatial sample pattern.");
            HistoryFrames = Advanced("History frames", 16, "Maximum effective history length. More frames smooth noise but respond more slowly to changes. History resets on camera cuts, resize and Screen Space Contact GI tuning changes.", new AcceptableValueRange<int>(4, 30));
            Samples = Advanced("Samples", 8, "Number of nearby donors; each surviving donor also uses a midpoint depth check. Higher counts reduce sampling noise at increased GPU cost.", new AcceptableValueList<int>(4, 6, 8, 16, 32));
            Radius = Advanced("Radius", .75f, "Maximum distance between receiving and donating surfaces, in metres.", new AcceptableValueRange<float>(.25f, 1.5f));
            DepthTolerance = Advanced("Depth tolerance", .02f, "Metres of tolerance for visibility and surface reconstruction. Larger values can leak through thin objects.", new AcceptableValueRange<float>(.002f, .1f));
            DonorLimit = Advanced("Donor luminance limit", 2f, "Maximum donor luminance in pre-tonemap HDR units. Limits emissive and reflection outliers without changing hue.", new AcceptableValueRange<float>(.1f, 16));
            BounceLimit = Advanced("Maximum bounce fraction", .2f, "Caps reflected added luminance relative to the local limited donor reference, after receiver reflectance and before AO/distance fades. Raising Strength cannot exceed this cap.", new AcceptableValueRange<float>(.01f, .5f));
            Distance = Advanced("Distance cutoff", 25f, "Stops color bounce beyond this camera distance, fading over the last 40 percent.", new AcceptableValueRange<float>(5, 50));
            if (!System.Enum.IsDefined(typeof(ColorBleedResolution), Resolution.Value)) Resolution.Value = ColorBleedResolution.Quarter;

            ConfigEntry<T> Advanced<T>(string key, T value, string text, AcceptableValueBase bounds = null) =>
                config.Bind(section, key, value, new ConfigDescription(text, bounds, new ConfigurationManagerAttributes { IsAdvanced = true }));
        }
    }
}
