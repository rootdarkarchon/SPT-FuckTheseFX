using BepInEx.Configuration;

namespace FuckTheseFX.Source.Occlusion
{
    public enum ColorBleedResolution { Half = 2, Quarter = 4 }

    internal sealed class ColorBleedSettings
    {
        internal readonly ConfigEntry<bool> Enabled, SpatialFilter, Temporal;
        internal readonly ConfigEntry<ColorBleedResolution> Resolution;
        internal readonly ConfigEntry<int> Samples, HistoryFrames;
        internal readonly ConfigEntry<float> Strength, Radius, DepthTolerance, DonorLimit, Distance;
        internal readonly ConfigEntry<int> BounceLimit;

        internal ColorBleedSettings(ConfigFile config) : this(new SettingsCatalog(config)) { }
        internal ColorBleedSettings(SettingsCatalog catalog)
        {
            Enabled = catalog.Bind<bool>(SettingsCatalog.ContactGI, "Enabled");
            Strength = catalog.Bind<float>(SettingsCatalog.ContactGI, "Strength");
            Resolution = catalog.Bind<ColorBleedResolution>(SettingsCatalog.ContactGI, "Resolution");
            SpatialFilter = catalog.Bind<bool>(SettingsCatalog.ContactGI, "Spatial filter");
            Temporal = catalog.Bind<bool>(SettingsCatalog.ContactGI, "Temporal accumulation");
            HistoryFrames = catalog.Bind<int>(SettingsCatalog.ContactGI, "History frames");
            Samples = catalog.Bind<int>(SettingsCatalog.ContactGI, "Samples");
            Radius = catalog.Bind<float>(SettingsCatalog.ContactGI, "Radius");
            DepthTolerance = catalog.Bind<float>(SettingsCatalog.ContactGI, "Depth tolerance");
            DonorLimit = catalog.Bind<float>(SettingsCatalog.ContactGI, "Donor luminance limit");
            BounceLimit = catalog.Bind<int>(SettingsCatalog.ContactGI, "Maximum bounce fraction (%)");
            Distance = catalog.Bind<float>(SettingsCatalog.ContactGI, "Distance cutoff");
        }


        internal float BounceFraction => SettingsCatalog.Fraction(BounceLimit);
    }
}
