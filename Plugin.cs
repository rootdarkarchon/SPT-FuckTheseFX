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

namespace FuckTheseFX
{
    [BepInPlugin(Guid, "Fuck These FX", Version)]
    [BepInDependency("com.matsix.sptaoreplacer", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(StochasticSsrIntegration.SsrsixGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rootdarkarchon.fuckthesefx";
        public const string Version = "1.6.0";
        internal static ManualLogSource MyLog;
        internal static ConfigEntry<bool> AmbientHighlightEnabled, BloomUseNativeResolution, DistantShadowSamplingEnabled;
        internal static ConfigEntry<bool> BloomEnabled, DistantShadowsEnabled;
        internal static ConfigEntry<bool> VintageEnabled;
        internal static ConfigEntry<bool> GlassCullingEnabled;
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
        private GlassCullingIntegration glassCulling;
        internal static SsrSettings Ssr;
        private StochasticSsrIntegration ssr;

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
                    "Makes omitted glass follow native culling of vehicle parts, windows and enclosing building sections. Primarily helps performance on Streets of Tarkov. Off restores normal glass rendering.");
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
        }

        private void OnDisable() { glassCulling?.Suspend(); ssr?.Suspend(); }
        private void OnEnable() { ssr?.Resume(); }

        private void OnDestroy()
        {
            glassCulling?.Dispose();
            ssr?.Dispose();
            vintage?.Dispose();
            occlusion?.Dispose(); sampling?.Dispose(); resolution?.Dispose(); highlight?.Dispose(); bloom?.Dispose();
        }
        private void OnGUI() => occlusion?.DrawDiagnostic();
    }
}
