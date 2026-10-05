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
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.rootdarkarchon.fuckthesefx";
        public const string Version = "1.8.6";
        internal static ManualLogSource MyLog;
        internal static ConfigEntry<bool> AmbientHighlightEnabled, DistantShadowSamplingEnabled;
        internal static ConfigEntry<bool> DistantShadowsEnabled;
        internal static ConfigEntry<bool> VintageEnabled;
        internal static ConfigEntry<bool> GlassCullingEnabled;
        internal static ConfigEntry<int> BloomInputScale;
        internal static ConfigEntry<int> PrismExposurePercent;
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
            occlusion = new XeGTAOIntegration(Info.Location, Occlusion);
            FeatureStartup.TryInitialize("XeGTAO", occlusion.Initialize);
        }

        internal static SettingsCatalog Settings;
        internal static void BindSettings(ConfigFile config)
        {
            var catalog = new SettingsCatalog(config);
            catalog.BindPresets();
            catalog.BindSection<bool>(SettingsCatalog.Vanilla);
            catalog.BindSection<bool>(SettingsCatalog.XeGTAO);
            catalog.BindSection<bool>(SettingsCatalog.ContactGI);
            catalog.BindSection<bool>(SettingsCatalog.SSR);
            catalog.BindSection<WaterSurfaceMode>(SettingsCatalog.Water);
            catalog.BindSection<OcclusionView>(SettingsCatalog.Debug);
            GlassCullingEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Streets of Tarkov Glass Culling Fix");
            AmbientHighlightEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Additional Ambient Highlight");
            VintageEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Vintage Filter");
            SharpenEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Sharpen Filter");
            SkipInactiveEffects = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Skip Neutral Effects Passes");
            PrismExposurePercent = catalog.Bind<int>(SettingsCatalog.Vanilla, "Prism Auto Exposure Resolution (%)");
            BloomInputScale = catalog.Bind<int>(SettingsCatalog.Vanilla, "Ultimate Bloom Input Scale (%)");
            DistantShadowsEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Distant Shadows Resolution Fix");
            DistantShadowResolution = catalog.Bind<DistantShadowMaskResolution>(SettingsCatalog.Vanilla, "Distant Shadows Resolution");
            DistantShadowSamplingEnabled = catalog.Bind<bool>(SettingsCatalog.Vanilla, "Distant Shadows Adjust Filter Samples");
            DistantShadowQuarterSamples = catalog.Bind<int>(SettingsCatalog.Vanilla, "Distant Shadows Quarter Filter Samples");
            DistantShadowHalfSamples = catalog.Bind<int>(SettingsCatalog.Vanilla, "Distant Shadows Half Filter Samples");
            DistantShadowFullSamples = catalog.Bind<int>(SettingsCatalog.Vanilla, "Distant Shadows Full Filter Samples");
            WaterSurface = catalog.Bind<WaterSurfaceMode>(SettingsCatalog.Water, "Mode");
            WaterSurfaceDiagnosticTint = catalog.Bind<bool>(SettingsCatalog.Water, "Show replacement tint");
            WaterSurfaceSize = catalog.Bind<float>(SettingsCatalog.Water, "Surface size");
            WaterSurfaceStrength = catalog.Bind<float>(SettingsCatalog.Water, "Surface strength");
            WaterFineRippleStrength = catalog.Bind<float>(SettingsCatalog.Water, "Fine ripple strength");
            WaterAnimationSpeed = catalog.Bind<float>(SettingsCatalog.Water, "Animation speed");
            Occlusion = new OcclusionSettings(catalog);
            Ssr = new SsrSettings(catalog);
            catalog.ValidateBindings();
            Settings = catalog;
        }

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
