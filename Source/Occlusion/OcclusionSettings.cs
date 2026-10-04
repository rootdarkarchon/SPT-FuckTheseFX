using BepInEx.Configuration;
using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    public enum OcclusionView { Normal, Depth, BeforeBlur, AfterBlur, CombinedAO = 6, SecondPassAO = 7, BentNormals = 8, BentSHDifference = 9, BentLightingDifference = 10, AmbientDirectional = 11, AmbientUpward = 12, AmbientFill = 13, AmbientInteriorTransmission = 14, AmbientShares = 15, ScreenSpaceContactGI = 16, ScreenSpaceContactGIHistory = 17, FoliageLightingCapture = 18, FoliageLive = 19, ScreenSpaceContactGIRaw = 20, ScreenSpaceContactGIDenoised = 21, ScreenSpaceContactGIHitDistance = 22, ScreenSpaceContactGITrace = 23 }
    public enum FoliageLivePreview { Scene, BeforeAmbient, AfterAmbient, BeforeOpaque, AfterOpaque, AfterTransparency, AmbientDifference, OpaqueDifference, ExperimentalMask }

    // Configuration owns policy; neither the GPU pipeline nor the camera owns saved preferences.
    internal sealed class OcclusionSettings
    {
        internal ConfigEntry<bool> Enabled, Normals, Ambient, Actors, BentNormals, ReflectionOcclusion;
        internal ConfigEntry<float> Radius, RadiusMultiplier, Distribution, Sharpness, Power;
        internal ConfigEntry<int> Falloff, Thickness;
        internal ConfigEntry<float> FadeStart, FadeEnd;
        internal ConfigEntry<int> Slices, Steps, Denoise, Quality;
        internal ConfigEntry<float> ActorRadius, ActorStrength, ActorResponse, ActorCutoff, DepthRange, DepthStart, BentDifferenceGain, AmbientContributionGain;
        internal ConfigEntry<int> AmbientStrength, Foliage;
        internal ConfigEntry<OcclusionView> View;
        internal ConfigEntry<float> ColorBleedGain;
        internal readonly ColorBleedSettings Bleed;
        internal ConfigEntry<FoliageLivePreview> FoliagePreview;
        internal ConfigEntry<bool> FoliageBypassGI, FoliageBypassOpaque, FoliageSplit;
        internal ConfigEntry<float> FoliageGain;
        internal ConfigEntry<int> FoliageExtraAO, FoliageSplitPosition;

        internal OcclusionSettings(ConfigFile config) : this(new SettingsCatalog(config)) { }
        internal OcclusionSettings(SettingsCatalog catalog)
        {
            Bleed = new ColorBleedSettings(catalog);
            Enabled = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "Enabled");
            Quality = catalog.Bind<int>(SettingsCatalog.XeGTAO, "Quality");
            Radius = catalog.Bind<float>(SettingsCatalog.XeGTAO, "EffectRadius");
            RadiusMultiplier = catalog.Bind<float>(SettingsCatalog.XeGTAO, "RadiusMultiplier");
            Falloff = catalog.Bind<int>(SettingsCatalog.XeGTAO, "FalloffRange (%)");
            Distribution = catalog.Bind<float>(SettingsCatalog.XeGTAO, "SampleDistributionPower");
            Slices = catalog.Bind<int>(SettingsCatalog.XeGTAO, "SliceCount");
            Steps = catalog.Bind<int>(SettingsCatalog.XeGTAO, "SamplesPerSlice");
            Thickness = catalog.Bind<int>(SettingsCatalog.XeGTAO, "ThicknessHeuristic (%)");
            Denoise = catalog.Bind<int>(SettingsCatalog.XeGTAO, "Denoise passes");
            Sharpness = catalog.Bind<float>(SettingsCatalog.XeGTAO, "DenoiseSharpness");
            Power = catalog.Bind<float>(SettingsCatalog.XeGTAO, "FinalPower");
            FadeStart = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Distance fade start");
            FadeEnd = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Distance fade end");
            Normals = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "UseNormals");
            Ambient = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "Custom Ambient Fix Enabled");
            ReflectionOcclusion = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "Reflection occlusion fix");
            BentNormals = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "Custom Ambient Fix Bent normals");
            AmbientStrength = catalog.Bind<int>(SettingsCatalog.XeGTAO, "Custom Ambient Fix Strength (%)");
            Foliage = catalog.Bind<int>(SettingsCatalog.XeGTAO, "Custom Ambient Fix Foliage AO strength (%)");
            Actors = catalog.Bind<bool>(SettingsCatalog.XeGTAO, "Second Pass Enabled");
            ActorRadius = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Second Pass Radius");
            ActorStrength = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Second Pass Strength");
            ActorResponse = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Second Pass Response");
            ActorCutoff = catalog.Bind<float>(SettingsCatalog.XeGTAO, "Second Pass Distance cutoff");
            View = catalog.Bind<OcclusionView>(SettingsCatalog.Debug, "View");
            BentDifferenceGain = catalog.Bind<float>(SettingsCatalog.Debug, "Bent difference gain");
            AmbientContributionGain = catalog.Bind<float>(SettingsCatalog.Debug, "Ambient contribution gain");
            DepthRange = catalog.Bind<float>(SettingsCatalog.Debug, "Depth range");
            DepthStart = catalog.Bind<float>(SettingsCatalog.Debug, "Depth start");
            ColorBleedGain = catalog.Bind<float>(SettingsCatalog.Debug, "Screen Space Contact GI gain");
            FoliagePreview = catalog.Bind<FoliageLivePreview>(SettingsCatalog.Debug, "Foliage live preview");
            FoliageGain = catalog.Bind<float>(SettingsCatalog.Debug, "Foliage live display gain");
            FoliageBypassGI = catalog.Bind<bool>(SettingsCatalog.Debug, "Foliage live bypass Contact GI");
            FoliageBypassOpaque = catalog.Bind<bool>(SettingsCatalog.Debug, "Foliage live bypass opaque colour");
            FoliageExtraAO = catalog.Bind<int>(SettingsCatalog.XeGTAO, "Foliage extra AO strength (%)");
            FoliageSplit = catalog.Bind<bool>(SettingsCatalog.Debug, "Foliage live split comparison");
            FoliageSplitPosition = catalog.Bind<int>(SettingsCatalog.Debug, "Foliage live divider (%)");
        }



        internal float AmbientStrengthFraction => SettingsCatalog.Fraction(AmbientStrength);
        internal float FoliageFraction => SettingsCatalog.Fraction(Foliage);
        internal float FoliageExtraAOFraction => SettingsCatalog.Fraction(FoliageExtraAO);
        internal float FoliageSplitPositionFraction => SettingsCatalog.Fraction(FoliageSplitPosition);

        internal bool BentDifferenceDebug => View.Value == OcclusionView.BentSHDifference || View.Value == OcclusionView.BentLightingDifference;
        internal bool FoliageCapture => View.Value == OcclusionView.FoliageLightingCapture;
        internal bool FoliageLive => View.Value == OcclusionView.FoliageLive;
        internal void ResetFoliageExperiment()
        {
            FoliageBypassGI.Value = FoliageBypassOpaque.Value = false;

        }
        internal bool LightingDebug => ((int)View.Value >= 9 && (int)View.Value <= 15) || FoliageCapture;
        internal float LightingDebugGain => BentDifferenceDebug ? BentDifferenceGain.Value : AmbientContributionGain.Value;

        // Resolve once per camera recording and upload the same curve to both consumers.
        internal Vector4 DistanceFade => new Vector4(FadeStart.Value, 1f / Mathf.Max(1, FadeEnd.Value - FadeStart.Value), 0, 1);

        internal Parameters Primary => new Parameters { Radius = Radius.Value, Multiplier = RadiusMultiplier.Value, Falloff = SettingsCatalog.Fraction(Falloff),
            Distribution = Distribution.Value, Thickness = SettingsCatalog.Fraction(Thickness), Sharpness = Sharpness.Value, Power = Power.Value,
            Slices = Slices.Value, Steps = Steps.Value, Denoise = Denoise.Value, Normals = Normals.Value };
        internal Parameters Actor => new Parameters { Radius = ActorRadius.Value, Multiplier = 1, Falloff = .615f,
            Distribution = 3, Sharpness = .4f, Power = 1, Denoise = 1, Normals = true };
    }

    internal struct Parameters
    {
        internal float Radius, Multiplier, Falloff, Distribution, Thickness, Sharpness, Power;
        internal int Slices, Steps, Denoise;
        internal bool Normals;
    }
}
