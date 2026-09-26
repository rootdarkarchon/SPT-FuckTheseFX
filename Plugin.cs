using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FuckTheseFX.Source.Highlight;
using FuckTheseFX.Source.Bloom;
using FuckTheseFX.Source.DistantShadows;
using FuckTheseFX.Source.Occlusion;
using FuckTheseFX.Source.Vintage;

namespace FuckTheseFX
{
    [BepInPlugin(Guid, "Fuck These FX", Version)]
    [BepInDependency("com.matsix.sptaoreplacer", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rootdarkarchon.fuckthesefx";
        public const string Version = "1.3.11";
        internal static ManualLogSource MyLog;
        internal static ConfigEntry<bool> AmbientHighlightEnabled, BloomUseNativeResolution, DistantShadowSamplingEnabled;
        internal static ConfigEntry<bool> BloomEnabled, DistantShadowsEnabled;
        internal static ConfigEntry<bool> VintageEnabled;
        internal static ConfigEntry<float> BloomInputScale;
        internal static ConfigEntry<DistantShadowMaskResolution> DistantShadowResolution;
        internal static ConfigEntry<int> DistantShadowQuarterSamples, DistantShadowHalfSamples, DistantShadowFullSamples;
        private AmbientHighlightIntegration highlight;
        private BloomResolutionIntegration bloom;
        private DistantShadowSamplingIntegration sampling;
        private DistantShadowResolutionIntegration resolution;
        internal static OcclusionSettings Occlusion;
        private XeGTAOIntegration occlusion;
        private VintageFilterIntegration vintage;

        private void Awake()
        {
            MyLog = Logger;
            BindSettings(Config);
            highlight = new AmbientHighlightIntegration();
            bloom = new BloomResolutionIntegration();
            sampling = new DistantShadowSamplingIntegration();
            resolution = new DistantShadowResolutionIntegration();
            vintage = new VintageFilterIntegration();
            FeatureStartup.TryInitialize("Vintage filter", vintage.Initialize);
            FeatureStartup.TryInitialize("Ambient Highlight", highlight.Initialize);
            FeatureStartup.TryInitialize("Bloom", bloom.Initialize);
            FeatureStartup.TryInitialize("Distant shadow sampling", () => sampling.Initialize(Info.Location));
            FeatureStartup.TryInitialize("Distant shadow resolution", resolution.Initialize);
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.matsix.sptaoreplacer"))
                Logger.LogInfo("AOReplacer is installed: FuckTheseFX XeGTAO, actor AO, CustomAmbient and Screen Space Contact GI are bypassed. Other FX remain active.");
            else
            {
                occlusion = new XeGTAOIntegration(Info.Location, Occlusion);
                FeatureStartup.TryInitialize("XeGTAO", occlusion.Initialize);
            }
        }

        internal static void BindSettings(ConfigFile config)
        {
            AmbientHighlightEnabled = config.Bind("Ambient Highlight", "Enabled", false,
                "When enabled uses vanilla Tarkovs behavior: Adds extra ambient brightness to weapons, hands and selected objects. Off removes this artificial glow. Performance: no measurable impact.");
            VintageEnabled = config.Bind("Vintage Filter", "Enabled", false,
                "Off disables the vintage camera filter. On restores effects disabled by Fuck These FX. Performance: no measurable impact.");
            BloomEnabled = config.Bind("Ultimate Bloom", "Enabled", true,
                "Adjusts bloom and lens-flare detail. Off keeps the game's original bloom. Performance: positive impact when reducing input scale.");
            BloomInputScale = config.Bind("Ultimate Bloom", "Bloom input scale", .67f,
                new ConfigDescription("Lower values make glow and flares softer and less detailed while keeping the scene sharp. Ignored with native resolution enabled.", new AcceptableValueRange<float>(.25f, 1f)));
            BloomUseNativeResolution = config.Bind("Ultimate Bloom", "Use native resolution", false,
                "Matches glow and flare detail to the game's resolution before DLSS/FSR upscaling, overriding the scale slider.");
            DistantShadowsEnabled = config.Bind("Distant Shadows", "Enabled", true,
                "Adjusts distant shadow detail and smoothing. Off keeps the game's original distant shadows. Performance: small impact at defaults.");
            DistantShadowResolution = config.Bind("Distant Shadows", "Resolution", Source.DistantShadows.DistantShadowMaskResolution.Half,
                "Higher resolutions sharpen distant shadow edges; lower resolutions look softer.");
            if (!System.Enum.IsDefined(typeof(Source.DistantShadows.DistantShadowMaskResolution), DistantShadowResolution.Value))
                DistantShadowResolution.Value = Source.DistantShadows.DistantShadowMaskResolution.Half;
            DistantShadowSamplingEnabled = BindAdvanced(config, "Distant Shadows", "Adjust filter samples", true,
                "Uses the sample counts below. Off restores the game's shadow filtering.");
            DistantShadowQuarterSamples = BindAdvanced(config, "Distant Shadows", "Quarter filter samples", 25,
                new ConfigDescription("More samples smooth quarter-resolution shadow edges and reduce flicker. 25 uses native filtering.", new AcceptableValueRange<int>(1, 25)));
            DistantShadowHalfSamples = BindAdvanced(config, "Distant Shadows", "Half filter samples", 12,
                new ConfigDescription("More samples smooth half-resolution shadow edges and reduce flicker. 25 uses native filtering.", new AcceptableValueRange<int>(1, 25)));
            DistantShadowFullSamples = BindAdvanced(config, "Distant Shadows", "Full filter samples", 8,
                new ConfigDescription("More samples smooth full-resolution shadow edges and reduce flicker. 25 uses native filtering.", new AcceptableValueRange<int>(1, 25)));
            Occlusion = new OcclusionSettings(config);
        }

        private static ConfigEntry<T> BindAdvanced<T>(ConfigFile config, string section, string key, T value, string description) =>
            BindAdvanced(config, section, key, value, new ConfigDescription(description));

        private static ConfigEntry<T> BindAdvanced<T>(ConfigFile config, string section, string key, T value, ConfigDescription description) =>
            config.Bind(section, key, value, new ConfigDescription(description.Description, description.AcceptableValues,
                new ConfigurationManagerAttributes { IsAdvanced = true }));

        private void Update()
        {
            highlight?.Update(); sampling?.Update(); resolution?.Update();
            occlusion?.Update();
        }

        private void OnDestroy()
        {
            vintage?.Dispose();
            occlusion?.Dispose(); sampling?.Dispose(); resolution?.Dispose(); highlight?.Dispose(); bloom?.Dispose();
        }
        private void OnGUI() => occlusion?.DrawDiagnostic();
    }
}
