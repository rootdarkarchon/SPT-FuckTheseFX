using BepInEx.Configuration;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal sealed class SsrSettings
    {
        internal readonly ConfigEntry<bool> Enabled, LimitRange, Temporal, Spatial, Adaptive;
        internal readonly ConfigEntry<ReconstructionMode> Sampling;
        internal readonly ConfigEntry<int> Steps, HistoryFrames;
        internal readonly ConfigEntry<float> Distance, Thickness;

        internal SsrSettings(ConfigFile config)
        {
            Enabled = config.Bind("Stochastic SSR", "Enabled", true,
                "Replaces the main camera's screen-space reflection tracing with stochastic sampling inspired by AMD FidelityFX SSSR. Keep SSR enabled in the game's graphics settings. Off restores native SSR; optics remain native. Automatically bypassed when SSRSix is installed. Performance: adds GPU cost, with Native sampling mode cheaper than Custom.");
            Sampling = config.Bind("Stochastic SSR", "Sampling mode", ReconstructionMode.Native,
                "Both modes use stochastic reflection rays. Native uses Tarkov's temporal reconstruction; Custom uses adapted AMD FidelityFX reflection denoising. Only the selected reconstruction runs. Change live.");
            Steps = Advanced(config, "Maximum iterations", 128,
                "Maximum hierarchical traversal steps per reflection ray. Higher values can find more distant reflections at extra GPU cost.", new AcceptableValueRange<int>(32,256));
            Thickness = Advanced(config, "Thickness", .2f,
                "Maximum accepted depth penetration in metres.", new AcceptableValueRange<float>(.01f,1f));
            LimitRange = Advanced(config, "Limit reflection range", false,
                "Use Maximum distance instead of the camera's full reflection range.");
            Distance = Advanced(config, "Maximum distance", 931.9296f,
                "Reflection ray length in metres when Limit reflection range is enabled, capped by the camera's far plane.", new AcceptableValueRange<float>(1,10000));
            Temporal = Advanced(config, "Temporal stabilization", true,
                "Custom sampling mode only: accumulates reflection history with reprojection and outlier rejection.");
            Spatial = Advanced(config, "Spatial filter", true,
                "Custom sampling mode only: reduces reflection noise with variance-guided spatial filtering.");
            HistoryFrames = Advanced(config, "History frames", 8,
                "Custom sampling mode only: maximum effective accumulation weight. Higher values smooth noise but can increase lag.", new AcceptableValueRange<int>(1,16));
            Adaptive = Advanced(config, "Adaptive rays", true,
                "Custom sampling mode only: reduces ray sampling on stable surfaces using reflection history. Requires both filters and at least four history frames. Benefit varies by scene; disable if it introduces instability.");
        }

        private static ConfigEntry<T> Advanced<T>(ConfigFile config, string name, T value, string text, AcceptableValueBase range=null) =>
            config.Bind("Stochastic SSR", name, value, new ConfigDescription(text, range,
                new ConfigurationManagerAttributes { IsAdvanced=true }));

        internal Options Read() => new Options {
            Mode=Enabled.Value?SsrMode.StochasticNative:SsrMode.Native,
            Reconstruction=Sampling.Value==ReconstructionMode.Custom?ReconstructionMode.Custom:ReconstructionMode.Native,
            Divisor=1, Steps=Steps.Value, Distance=Distance.Value, Thickness=Thickness.Value, Smoothness=.5f,
            Temporal=Temporal.Value, Spatial=Spatial.Value, HistoryFrames=HistoryFrames.Value,
            EdgePixels=0, NativeResponse=true, CompactRays=false, LimitNativeRange=LimitRange.Value,
            EfficientDenoising=true, StreamlinedOutput=true, AdaptiveRays=Adaptive.Value
        };
    }
}
