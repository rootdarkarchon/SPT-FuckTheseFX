using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FuckTheseFX.Source.Highlight;
using FuckTheseFX.Source.Bloom;
using FuckTheseFX.Source.DistantShadows;
using FuckTheseFX.Source.Occlusion;
using FuckTheseFX.Source.Vintage;
using FuckTheseFX.Source.GlassCulling;
using FuckTheseFX.Source.StochasticSSR;
using FuckTheseFX.Source.Water;
using FuckTheseFX.Source.Prism;
using FuckTheseFX.Source.Sharpen;
using FuckTheseFX.Source.PostEffects;

namespace FuckTheseFX
{
    [BepInPlugin(Guid, "Fuck These FX", Version)]
    [BepInDependency("com.matsix.sptaoreplacer", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(StochasticSsrIntegration.SsrsixGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rootdarkarchon.fuckthesefx";
        public const string Version = "1.7.0";
        internal static ManualLogSource MyLog;
        internal static ConfigEntry<bool> AmbientHighlightEnabled, BloomUseNativeResolution, DistantShadowSamplingEnabled;
        internal static ConfigEntry<bool> BloomEnabled, DistantShadowsEnabled;
        internal static ConfigEntry<bool> VintageEnabled;
        internal static ConfigEntry<bool> GlassCullingEnabled;
        internal static ConfigEntry<float> BloomInputScale;
        internal static ConfigEntry<float> PrismExposurePercent;
        internal static ConfigEntry<bool> SharpenEnabled;
        internal static ConfigEntry<bool> SkipInactiveEffects;
        internal static ConfigEntry<DistantShadowMaskResolution> DistantShadowResolution;
        internal static ConfigEntry<int> DistantShadowQuarterSamples, DistantShadowHalfSamples, DistantShadowFullSamples;
        private AmbientHighlightIntegration highlight;
        private BloomResolutionIntegration bloom;
        private DistantShadowSamplingIntegration sampling;
        private DistantShadowResolutionIntegration resolution;
        internal static OcclusionSettings Occlusion;
        private XeGTAOIntegration occlusion;
        private VintageFilterIntegration vintage;
        private GlassCullingIntegration glassCulling;
        internal static SsrSettings Ssr;
        private StochasticSsrIntegration ssr;
        internal static ConfigEntry<WaterSurfaceMode> WaterSurface;
        internal static ConfigEntry<bool> WaterSurfaceDiagnosticTint;
        internal static ConfigEntry<float> WaterSurfaceSize, WaterSurfaceStrength, WaterFineRippleStrength, WaterAnimationSpeed;
        private WaterSurfaceIntegration water;
        private PrismExposureIntegration prismExposure;
        private SharpenFilterIntegration sharpen;
        private InactiveEffectsIntegration inactiveEffects;

        private void Awake()
        {
            MyLog = Logger;
            BindSettings(Config);
            highlight = new AmbientHighlightIntegration();
            bloom = new BloomResolutionIntegration();
            sampling = new DistantShadowSamplingIntegration();
            resolution = new DistantShadowResolutionIntegration();
            vintage = new VintageFilterIntegration();
            glassCulling = new GlassCullingIntegration();
            ssr = new StochasticSsrIntegration(Info.Location, Ssr);
            water = new WaterSurfaceIntegration();
            prismExposure = new PrismExposureIntegration();
            sharpen = new SharpenFilterIntegration();
            inactiveEffects = new InactiveEffectsIntegration();
            FeatureStartup.TryInitialize("Inactive screen effects", inactiveEffects.Initialize);
            FeatureStartup.TryInitialize("Prism exposure", prismExposure.Initialize);
            FeatureStartup.TryInitialize("Sharpen filter", sharpen.Initialize);
            FeatureStartup.TryInitialize("Water surface", () => water.Initialize(Info.Location));
            FeatureStartup.TryInitialize("Stochastic SSR", ssr.Initialize);
            FeatureStartup.TryInitialize("Glass culling", glassCulling.Initialize);
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
            RemoveRetiredSettings(config);
            GlassCullingEnabled = BindGlassCulling(config);
            AmbientHighlightEnabled = config.Bind("Ambient Highlight", "Enabled", false,
                "When enabled uses vanilla Tarkovs behavior: Adds extra ambient brightness to weapons, hands and selected objects. Off removes this artificial glow. Performance: no measurable impact.");
            VintageEnabled = config.Bind("Vintage Filter", "Enabled", false,
                "Off disables the vintage camera filter. On restores effects disabled by Fuck These FX. Performance: no measurable impact.");
            SharpenEnabled = config.Bind("Sharpen Filter", "Enabled", true,
                "On retains Tarkov's CC_Sharpen filter. Off removes the filter, including its combined desaturation. Applies live; enabling restores only effects disabled by Fuck These FX.");
            SkipInactiveEffects = config.Bind("Inactive Effects", "Skip neutral passes", true,
                "Skips neutral status-effect copies inside the active upscaler image chain. Zero-aperture disc DoF retains its native alpha/CoC pass and skips blur/compositing. Active effects and ordinary rendering retain native behavior. Applies live.");
            PrismExposurePercent = config.Bind("Prism Exposure", "Resolution percent", 100f,
                new ConfigDescription("Side length of the square exposure sampling grid as a percentage of the game's original half-width grid. At 4K: 100% = 1920x1920, 50% = 960x960. Lower values reduce exposure sampling detail; scene sharpness and tone-mapping resolution are unchanged. 100% retains native behavior.", new AcceptableValueRange<float>(1f, 100f)));
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
            Ssr = new SsrSettings(config);
            WaterSurface = config.Bind("Water Surface", "Mode", WaterSurfaceMode.Native,
                "Native keeps Tarkov water. FTFX uses broader, slower waves and gentler fine ripples on Customs, including optics. Other maps and water families remain native.");
            WaterSurfaceDiagnosticTint = config.Bind("Water Surface", "Show replacement tint", false,
                "Colors FTFX water bright magenta to confirm that the replacement is visible. Native stays normal. Turn off after checking the switch.");
            WaterSurfaceSize = config.Bind("Water Surface", "Surface size", 1f,
                new ConfigDescription("FTFX only. Larger values spread the surface patterns over a larger area. 1 uses the phase 2 default.", new AcceptableValueRange<float>(.25f,4f)));
            WaterSurfaceStrength = config.Bind("Water Surface", "Surface strength", 1f,
                new ConfigDescription("FTFX only. Strength of the surface normal variation. 0 flattens it; native rain ripples remain active. 1 uses the phase 2 default.", new AcceptableValueRange<float>(0f,2f)));
            WaterFineRippleStrength = config.Bind("Water Surface", "Fine ripple strength", 1f,
                new ConfigDescription("FTFX only. Strength of fine detail relative to the broader patterns. 0 removes fine detail. 1 uses the restrained phase 2 default.", new AcceptableValueRange<float>(0f,4f)));
            WaterAnimationSpeed = config.Bind("Water Surface", "Animation speed", 1f,
                new ConfigDescription("FTFX only. Surface pattern movement speed. Changes preserve the current pattern position; 0 freezes it in place. Native foam and rain still animate. 1 uses the phase 2 default.", new AcceptableValueRange<float>(0f,2f)));
        }

        private static void RemoveRetiredSettings(ConfigFile config)
        {
            bool save = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                // Bind/remove also consumes orphaned entries saved by retired experiments.
                config.Remove(config.Bind("Debug", "Foliage live extra AO", "").Definition);
                foreach (var key in new[] { "Stationary geometry and decals", "Experimental shadow culling" })
                    config.Remove(config.Bind("Scene Culling", key, "").Definition);
            }
            finally { config.SaveOnConfigSet = save; if (save) config.Save(); }
        }

        private static ConfigEntry<bool> BindGlassCulling(ConfigFile config)
        {
            if (config.TryGetEntry<bool>("Glass Culling Fix", "Enabled", out var existing)) return existing;
            // Read both names before removing the legacy key; a saved new preference takes priority.
            bool save = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                var legacy = config.Bind("Glass Culling", "Enabled", "");
                string oldValue = legacy.Value;
                config.Remove(legacy.Definition);
                var current = config.Bind("Glass Culling Fix", "Enabled", "");
                string newValue = current.Value;
                config.Remove(current.Definition);
                var entry = config.Bind("Glass Culling Fix", "Enabled", true,
                    "Only active on Streets of Tarkov. Makes omitted glass follow native culling of vehicle parts, windows and enclosing building sections to improve performance. Inactive on all other maps. Off restores normal glass rendering.");
                if (bool.TryParse(newValue, out bool value) || bool.TryParse(oldValue, out value)) entry.Value = value;
                return entry;
            }
            finally { config.SaveOnConfigSet = save; if (save) config.Save(); }
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
            glassCulling?.Update();
            ssr?.Update();
            water?.Update();
        }

        private void OnDisable() { glassCulling?.Suspend(); ssr?.Suspend(); water?.Suspend(); }
        private void OnEnable() { ssr?.Resume(); water?.Resume(); }

        private void OnDestroy()
        {
            glassCulling?.Dispose();
            water?.Dispose();
            ssr?.Dispose();
            vintage?.Dispose();
            sharpen?.Dispose(); prismExposure?.Dispose();
            inactiveEffects?.Dispose();
            occlusion?.Dispose(); sampling?.Dispose(); resolution?.Dispose(); highlight?.Dispose(); bloom?.Dispose();
        }
        private void OnGUI() => occlusion?.DrawDiagnostic();
    }
}
