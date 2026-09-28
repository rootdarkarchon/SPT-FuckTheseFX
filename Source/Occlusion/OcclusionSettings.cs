using BepInEx.Configuration;
using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    public enum OcclusionView { Normal, Depth, BeforeBlur, AfterBlur, CombinedAO = 6, SecondPassAO = 7, BentNormals = 8, BentSHDifference = 9, BentLightingDifference = 10, AmbientDirectional = 11, AmbientUpward = 12, AmbientFill = 13, AmbientInteriorTransmission = 14, AmbientShares = 15, ScreenSpaceContactGI = 16, ScreenSpaceContactGIHistory = 17, FoliageLightingCapture = 18, FoliageLive = 19 }
    public enum FoliageLivePreview { Scene, BeforeAmbient, AfterAmbient, BeforeOpaque, AfterOpaque, AfterTransparency, AmbientDifference, OpaqueDifference, ExperimentalMask }

    // Configuration owns policy; neither the GPU pipeline nor the camera owns saved preferences.
    internal sealed class OcclusionSettings
    {
        internal ConfigEntry<bool> Enabled, Normals, Ambient, Actors, BentNormals, ReflectionOcclusion;
        internal ConfigEntry<float> Radius, RadiusMultiplier, Falloff, Distribution, Thickness, Sharpness, Power;
        internal ConfigEntry<float> FadeStart, FadeEnd;
        internal ConfigEntry<int> Slices, Steps, Denoise, Quality;
        internal ConfigEntry<float> AmbientStrength, Foliage, ActorRadius, ActorStrength, ActorResponse, ActorCutoff, DepthRange, DepthStart, BentDifferenceGain, AmbientContributionGain;
        internal ConfigEntry<OcclusionView> View;
        internal ConfigEntry<float> ColorBleedGain;
        internal readonly ColorBleedSettings Bleed;
        internal ConfigEntry<FoliageLivePreview> FoliagePreview;
        internal ConfigEntry<bool> FoliageBypassGI, FoliageBypassOpaque, FoliageSplit;
        internal ConfigEntry<float> FoliageGain, FoliageExtraAO, FoliageSplitPosition;

        internal OcclusionSettings(ConfigFile config)
        {
            Bleed = new ColorBleedSettings(config);
            Enabled = config.Bind("XeGTAO", "Enabled", true, "Replace Tarkov's AO with XeGTAO. Off restores native AO. Quality is selected below; game AO Off disables the replacement and actor pass. If AOReplacer is installed, all FuckTheseFX AO and CustomAmbient features are bypassed. Performance: no impact or an improvement over vanilla HBAO.");
            Quality = config.Bind("XeGTAO", "Quality", 3, new ConfigDescription("Quality for primary and actor XeGTAO: 0 Low (1 slice/2 steps), 1 Medium (2/2), 2 High (3/3), 3 Ultra (9/3). Independent of Tarkov's HBAO quality. Advanced primary sample overrides still apply.", new AcceptableValueRange<int>(0,3)));
            Radius = Float("XeGTAO", "EffectRadius", 1, .01f, 5, "AO radius in metres before RadiusMultiplier.");
            RadiusMultiplier = Float("XeGTAO", "RadiusMultiplier", 1.457f, .1f, 3, "Multiplies radius in the AO calculation and depth prefilter.");
            Falloff = Float("XeGTAO", "FalloffRange", .615f, .05f, 1, "Fraction of the effective radius over which occlusion fades out.");
            Distribution = Float("XeGTAO", "SampleDistributionPower", 2.5f, .5f, 4, "Higher values concentrate samples near the pixel.");
            Slices = Advanced("XeGTAO", "SliceCount", 0, "Search slices. 0 follows the FTFX Quality slider (1/2/3/9).", new AcceptableValueRange<int>(0, 9));
            Steps = Advanced("XeGTAO", "SamplesPerSlice", 0, "Samples per side of each slice. 0 follows the FTFX Quality slider (2/2/3/3).", new AcceptableValueRange<int>(0, 8));
            Thickness = Float("XeGTAO", "ThicknessHeuristic", 0, 0, 1, "Thin-occluder compensation. Zero disables this heuristic.");
            Denoise = Advanced("XeGTAO", "Denoise passes", 2, "Spatial denoising: 0 off, 1 sharp, 2 medium, 3 soft.", new AcceptableValueRange<int>(0, 3));
            Sharpness = Float("XeGTAO", "DenoiseSharpness", .4f, .1f, 10, "Denoiser center-pixel weight. Higher values retain detail and noise.");
            Power = Float("XeGTAO", "FinalPower", 1.5f, .5f, 5, "Visibility power before denoising. Higher values darken AO.");
            FadeStart = Float("XeGTAO", "Distance fade start", 100, 0, 999, "View depth in metres where primary AO starts fading. Shared with the Custom Ambient Fix, foliage and bent normals; the actor second pass keeps its own cutoff.");
            FadeEnd = Float("XeGTAO", "Distance fade end", 250, 1, 1000, "View depth in metres where primary AO reaches zero. Shared with the Custom Ambient Fix. Values below start + 1 use a one-metre fade after the start; saved values are preserved.");
            Normals = Advanced("XeGTAO", "UseNormals", true, "Use G-buffer normals including normal maps; off reconstructs geometric normals from depth.");
            Ambient = Advanced("XeGTAO", "Custom Ambient Fix Enabled", true, "Make custom diffuse ambient and fill respect XeGTAO. Does not change direct lights or Ambient Highlight.");
            ReflectionOcclusion = Advanced("XeGTAO", "Reflection occlusion fix", true, "Reduce screen-space reflection occlusion by up to 50% on smooth surfaces, tapering with smoothness squared to no reduction on fully rough surfaces. Applies at every viewing angle. Excludes the native motion-depth vegetation path to preserve its reflection shading. Shares the corrected material visibility with native reflection probes and SSR, while preserving diffuse AO and authored material occlusion. Requires Custom Ambient Fix enabled with strength above zero. Toggle live to compare; off restores full screen-space reflection AO.");
            BentNormals = Advanced("XeGTAO", "Custom Ambient Fix Bent normals", false, "Use XeGTAO bent normals for directional SH ambient lighting. Requires XeGTAO and the CustomAmbient fix. Adds GPU work; leaves reflections, fill and the actor pass unchanged. Respects Strength, Foliage AO strength and the AO distance fade.");
            AmbientStrength = Float("XeGTAO", "Custom Ambient Fix Strength", .6f, 0, 1, "AO attenuation of custom diffuse ambient and fill. Zero restores native ambient.");
            Foliage = Float("XeGTAO", "Custom Ambient Fix Foliage AO strength", .65f, 0, 1, "AO strength on authored zero-alpha materials, including foliage.");
            Actors = config.Bind("XeGTAO", "Second Pass Enabled", true, "Additional independent full-resolution contact AO on hands, held weapons and characters. Requires XeGTAO and game AO enabled.");
            ActorRadius = Float("XeGTAO", "Second Pass Radius", .3f, .01f, 2, "Independent radius in metres, without the primary radius multiplier.");
            ActorStrength = Float("XeGTAO", "Second Pass Strength", 1.5f, 0, 4, "Actor obscurance strength. Zero bypasses the pass.");
            ActorResponse = Float("XeGTAO", "Second Pass Response", .6f, .25f, 2, "Power on actor obscurance; values below one emphasize faint AO.");
            ActorCutoff = Float("XeGTAO", "Second Pass Distance cutoff", 10, .1f, 100, "Stops extra actor AO beyond this distance, fading over the last 20%.");
            View = Advanced("Debug", "View", OcclusionView.Normal, "FoliageLive opens live stage previews, temporary lighting bypasses and a split-screen extra AO experiment; use the Foliage live controls below. Returning to Normal resets both bypasses; extra AO remains controlled under XeGTAO. FoliageLightingCapture saves one main-camera frame of AO, ambient and later lighting stages under BepInEx/FuckTheseFX/FoliageDiagnostics, then returns to Normal. Hold still; saving briefly pauses the game. Enable Custom Ambient Fix for its inputs. Other views are full-screen diagnostics. Normal restores the scene. Actor view is white when inactive. BentNormals shows world-space directions as RGB; gray when inactive. ScreenSpaceContactGI isolates the final added RGB before fog/tonemap; requires Screen Space Contact GI Enabled. Adjust Screen Space Contact GI gain for display brightness. ScreenSpaceContactGIHistory: green retained, red depth/position rejection, yellow normal rejection, cyan lighting rejection, orange fresh/offscreen history, blue invalid surface, magenta strongly limited. Requires Temporal accumulation; display gain does not apply.");
            BentDifferenceGain = Float("Debug", "Bent difference gain", 32, 1, 1000, "Amplification for BentSHDifference and BentLightingDifference. Black means zero change; brighter means larger absolute RGB change. Cyan means no CustomAmbient capture; red means bent shader disabled; magenta means missing camera inputs. Diagnostics add GPU work.");
            AmbientContributionGain = Float("Debug", "Ambient contribution gain", 1, 1, 1000, "Shared display amplification for AmbientDirectional, AmbientUpward and AmbientFill, after blend, albedo and AO. AmbientShares displays relative luminance: red directional, green upward, blue fill. AmbientInteriorTransmission displays visibility times directional blend weight; black blocks directional SH, white passes it. Shares/transmission ignore gain. Requires the CustomAmbient fix; works with bent normals on or off. Excludes reflections and direct lights.");
            DepthRange = Float("Debug", "Depth range", 5, .01f, 200, "Depth display window in metres.");
            DepthStart = Float("Debug", "Depth start", 0, 0, 200, "Depth display begins at this distance in metres.");
            ColorBleedGain = Float("Debug", "Screen Space Contact GI gain", 16, 1, 128, "Display amplification for Debug View = ScreenSpaceContactGI. Shows only the RGB added by the Screen Space Contact GI composite after strength, limits, receiver albedo, visibility and fades, before downstream fog/tonemap. Does not change scene lighting. Black means zero; cyan unavailable; magenta mismatched input dimensions. Requires Screen Space Contact GI Enabled and XeGTAO. Adds debug-only GPU work.");
            FoliagePreview = Advanced("Debug", "Foliage live preview", FoliageLivePreview.Scene, "Requires View = FoliageLive, main camera, HDR and Custom Ambient Fix. Scene shows normal gameplay. Other modes show live raw stage buffers or absolute RGB differences, before final grading/upscaling. No files are saved. BeforeOpaque includes extra foliage AO; AfterOpaque includes native output BEFORE the temporary bypass.");
            FoliageGain = Float("Debug", "Foliage live display gain", 1, 1, 32, "Brightness of raw stage/difference previews only. Does not affect Scene or experimental AO. Try 8 for differences.");
            FoliageBypassGI = Advanced("Debug", "Foliage live bypass Contact GI", false, "Temporarily omit Contact GI on the main camera in FoliageLive. Preserves the saved GI settings. Resets on leaving the live view.");
            FoliageBypassOpaque = Advanced("Debug", "Foliage live bypass opaque colour", false, "In FoliageLive, restore the scene colour from immediately before Opaque Only Post-processing after that buffer executes. Isolates its combined colour result (including fog/SSR), not a single effect. Native commands still run. Resets on leaving the live view.");
            FoliageExtraAO = Float("XeGTAO", "Foliage extra AO strength", .2f, 0, 1, "Additional scene-colour AO on foliage-compatible zero-alpha opaque materials before native opaque post-processing and fog. Main camera HDR only; excludes actors and sky. Shares the AO distance fade. Zero disables it. Independent of Custom Ambient Fix strength; may also affect non-foliage zero-alpha materials and direct surface lighting.");
            FoliageSplit = Advanced("Debug", "Foliage live split comparison", true, "Apply extra AO only RIGHT of the divider; LEFT retains the current rendering. GI/opaque bypasses affect both sides. Move the divider across the same tree to compare. FoliageLive only.");
            FoliageSplitPosition = Float("Debug", "Foliage live divider", .5f, 0, 1, "Horizontal divider position: 0 extra AO across the scene, 1 no extra AO. Used when split comparison is enabled.");

            if (!System.Enum.IsDefined(typeof(OcclusionView), View.Value)) View.Value = OcclusionView.Normal;

            ConfigEntry<float> Float(string section, string key, float value, float min, float max, string text) =>
                Advanced(section, key, value, text, new AcceptableValueRange<float>(min, max));
            ConfigEntry<T> Advanced<T>(string section, string key, T value, string text, AcceptableValueBase bounds = null) =>
                config.Bind(section, key, value, new ConfigDescription(text, bounds, new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

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

        internal Parameters Primary => new Parameters { Radius = Radius.Value, Multiplier = RadiusMultiplier.Value, Falloff = Falloff.Value,
            Distribution = Distribution.Value, Thickness = Thickness.Value, Sharpness = Sharpness.Value, Power = Power.Value,
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
