using BepInEx.Configuration;

namespace FuckTheseFX.Source.Occlusion
{
    internal sealed class ColorBleedSettings
    {
        internal readonly ConfigEntry<bool> Enabled, Temporal, AntiFirefly, Capture;
        internal readonly ConfigEntry<int> Resolution;
        internal readonly ConfigEntry<int> Rays, RaySteps, HistoryFrames, CaptureFrames;
        internal readonly ConfigEntry<float> Strength, Radius, DepthTolerance, DonorLimit, Distance, ReconstructionCutoff;
        internal readonly ConfigEntry<int> BounceLimit;

        internal ColorBleedSettings(ConfigFile config) : this(new SettingsCatalog(config)) { }
        internal ColorBleedSettings(SettingsCatalog catalog)
        {
            Enabled = catalog.Bind<bool>(SettingsCatalog.ContactGI, "Enabled");
            Strength = catalog.Bind<float>(SettingsCatalog.ContactGI, "Strength");
            Resolution = catalog.Bind<int>(SettingsCatalog.ContactGI, "Resolution (%)");
            ReconstructionCutoff = catalog.Bind<float>(SettingsCatalog.ContactGI, "Reconstruction cutoff");
            Temporal = catalog.Bind<bool>(SettingsCatalog.ContactGI, "Temporal accumulation");
            HistoryFrames = catalog.Bind<int>(SettingsCatalog.ContactGI, "History frames");
            Rays = catalog.Bind<int>(SettingsCatalog.ContactGI, "Rays per pixel");
            RaySteps = catalog.Bind<int>(SettingsCatalog.ContactGI, "Maximum ray steps");
            AntiFirefly = catalog.Bind<bool>(SettingsCatalog.ContactGI, "NRD anti-firefly");
            Capture = catalog.Bind<bool>(SettingsCatalog.Debug, "Contact GI capture sequence");
            CaptureFrames = catalog.Bind<int>(SettingsCatalog.Debug, "Contact GI capture frames");
            Radius = catalog.Bind<float>(SettingsCatalog.ContactGI, "Radius");
            DepthTolerance = catalog.Bind<float>(SettingsCatalog.ContactGI, "Depth tolerance");
            DonorLimit = catalog.Bind<float>(SettingsCatalog.ContactGI, "Donor luminance limit");
            BounceLimit = catalog.Bind<int>(SettingsCatalog.ContactGI, "Maximum bounce fraction (%)");
            Distance = catalog.Bind<float>(SettingsCatalog.ContactGI, "Distance cutoff");
        }


        internal float BounceFraction => SettingsCatalog.Fraction(BounceLimit);
    }
}
