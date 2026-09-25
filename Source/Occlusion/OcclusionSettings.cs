using BepInEx.Configuration;

namespace FuckTheseFX.Source.Occlusion
{
    public enum OcclusionView { Normal, Depth, BeforeBlur, AfterBlur, CombinedAO = 6, SecondPassAO = 7 }

    // Configuration owns policy; neither the GPU pipeline nor the camera owns saved preferences.
    internal sealed class OcclusionSettings
    {
        internal ConfigEntry<bool> Enabled, Normals, Ambient, Actors;
        internal ConfigEntry<float> Radius, RadiusMultiplier, Falloff, Distribution, Thickness, Sharpness, Power;
        internal ConfigEntry<int> Slices, Steps, Denoise;
        internal ConfigEntry<float> AmbientStrength, Foliage, ActorRadius, ActorStrength, ActorResponse, ActorCutoff, DepthRange, DepthStart;
        internal ConfigEntry<OcclusionView> View;

        internal OcclusionSettings(ConfigFile config)
        {
            Enabled = config.Bind("XeGTAO", "Enabled", true, "Replace Tarkov's AO with XeGTAO. Off restores native AO. Quality follows the game's AO setting; game AO Off disables the replacement and actor pass. If AOReplacer is installed, all FuckTheseFX AO and CustomAmbient features are bypassed.");
            Radius = Float("XeGTAO", "EffectRadius", 1, .01f, 5, "AO radius in metres before RadiusMultiplier.");
            RadiusMultiplier = Float("XeGTAO", "RadiusMultiplier", 1.457f, .1f, 3, "Multiplies radius in the AO calculation and depth prefilter.");
            Falloff = Float("XeGTAO", "FalloffRange", .615f, .05f, 1, "Fraction of the effective radius over which occlusion fades out.");
            Distribution = Float("XeGTAO", "SampleDistributionPower", 2.5f, .5f, 4, "Higher values concentrate samples near the pixel.");
            Slices = Advanced("XeGTAO", "SliceCount", 0, "Search slices. 0 follows game quality (1/2/3/9).", new AcceptableValueRange<int>(0, 9));
            Steps = Advanced("XeGTAO", "SamplesPerSlice", 0, "Samples per side of each slice. 0 follows game quality (2/2/3/3).", new AcceptableValueRange<int>(0, 8));
            Thickness = Float("XeGTAO", "ThicknessHeuristic", 0, 0, 1, "Thin-occluder compensation. Zero disables this heuristic.");
            Denoise = Advanced("XeGTAO", "Denoise passes", 2, "Spatial denoising: 0 off, 1 sharp, 2 medium, 3 soft.", new AcceptableValueRange<int>(0, 3));
            Sharpness = Float("XeGTAO", "DenoiseSharpness", .4f, .1f, 10, "Denoiser center-pixel weight. Higher values retain detail and noise.");
            Power = Float("XeGTAO", "FinalPower", 1.5f, .5f, 5, "Visibility power before denoising. Higher values darken AO.");
            Normals = Advanced("XeGTAO", "UseNormals", true, "Use G-buffer normals including normal maps; off reconstructs geometric normals from depth.");
            Ambient = Advanced("CustomAmbient fix", "Enabled", true, "Make custom diffuse ambient and fill respect XeGTAO. Does not change direct lights or Ambient Highlight.");
            AmbientStrength = Float("CustomAmbient fix", "Strength", .6f, 0, 1, "AO attenuation of custom diffuse ambient and fill. Zero restores native ambient.");
            Foliage = Float("CustomAmbient fix", "Foliage AO strength", .65f, 0, 1, "AO strength on authored zero-alpha materials, including foliage.");
            Actors = config.Bind("Actor second AO pass", "Enabled", true, "Additional independent full-resolution contact AO on hands, held weapons and characters. Requires XeGTAO and game AO enabled.");
            ActorRadius = Float("Actor second AO pass", "Radius", .3f, .01f, 2, "Independent radius in metres, without the primary radius multiplier.");
            ActorStrength = Float("Actor second AO pass", "Strength", 1.5f, 0, 4, "Actor obscurance strength. Zero bypasses the pass.");
            ActorResponse = Float("Actor second AO pass", "Response", .6f, .25f, 2, "Power on actor obscurance; values below one emphasize faint AO.");
            ActorCutoff = Float("Actor second AO pass", "Distance cutoff", 10, .1f, 100, "Stops extra actor AO beyond this distance, fading over the last 20%.");
            View = Advanced("Debug", "View", OcclusionView.Normal, "Full-screen AO diagnostics. Normal restores the scene. Actor view is white when inactive.");
            DepthRange = Float("Debug", "Depth range", 5, .01f, 200, "Depth display window in metres.");
            DepthStart = Float("Debug", "Depth start", 0, 0, 200, "Depth display begins at this distance in metres.");

            if (!System.Enum.IsDefined(typeof(OcclusionView), View.Value)) View.Value = OcclusionView.Normal;

            ConfigEntry<float> Float(string section, string key, float value, float min, float max, string text) =>
                Advanced(section, key, value, text, new AcceptableValueRange<float>(min, max));
            ConfigEntry<T> Advanced<T>(string section, string key, T value, string text, AcceptableValueBase bounds = null) =>
                config.Bind(section, key, value, new ConfigDescription(text, bounds, new ConfigurationManagerAttributes { IsAdvanced = true }));
        }

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
