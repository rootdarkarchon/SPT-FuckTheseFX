using BepInEx.Configuration;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal sealed class SsrSettings
    {
        internal readonly ConfigEntry<bool> Enabled,Temporal,Freeze,CaptureContinuation,CaptureTraversal;
        internal readonly ConfigEntry<int> Steps,HistoryFrames,ContinuationSamples;
        internal readonly ConfigEntry<float> Thickness, ContinuationPixels, ContinuationDepth;
        internal readonly ConfigEntry<int> FallbackStrength, SurfaceColourInfluence, ThicknessConfidenceFloor;
        internal readonly ConfigEntry<ReflectionResolution> Resolution;
        internal readonly ConfigEntry<ReflectionView> View;
        internal readonly ConfigEntry<ReflectionReconstruction> Reconstruction;
        internal readonly ConfigEntry<int> ReceiverCutoff, CutoffEnvironmentBlend;
        internal readonly ConfigEntry<bool> ZeroSmoothnessProbe;
        internal readonly ConfigEntry<bool> NrdAntiFirefly;
        internal readonly ConfigEntry<float> ZeroSmoothnessProbeFadeStart, ZeroSmoothnessProbeFadeEnd;
        internal readonly ConfigEntry<int> ZeroSmoothnessProbeStrength, ZeroSmoothnessProbeClose;
        internal bool NeedsWaterCapture=>Enabled.Value; // Water material tint and receiver ownership.
        internal SsrSettings(ConfigFile config) : this(new SettingsCatalog(config)) { }
        internal SsrSettings(SettingsCatalog catalog)
        {
            Enabled = catalog.Bind<bool>(SettingsCatalog.SSR, "Enabled");
            Resolution = catalog.Bind<ReflectionResolution>(SettingsCatalog.SSR, "SSR resolution");
            Steps = catalog.Bind<int>(SettingsCatalog.SSR, "Maximum iterations");
            Thickness = catalog.Bind<float>(SettingsCatalog.SSR, "Thickness");
            ThicknessConfidenceFloor = catalog.Bind<int>(SettingsCatalog.SSR, "Thickness hit confidence floor (%)");
            ContinuationSamples = catalog.Bind<int>(SettingsCatalog.SSR, "Occluder continuation samples");
            ContinuationPixels = catalog.Bind<float>(SettingsCatalog.SSR, "Occluder continuation pixels");
            ContinuationDepth = catalog.Bind<float>(SettingsCatalog.SSR, "Occluder continuation depth separation");
            CaptureContinuation = catalog.Bind<bool>(SettingsCatalog.SSR, "Capture continuation diagnostics");
            CaptureTraversal = catalog.Bind<bool>(SettingsCatalog.SSR, "Capture traversal work");
            FallbackStrength = catalog.Bind<int>(SettingsCatalog.SSR, "Fallback strength (%)");
            Reconstruction = catalog.Bind<ReflectionReconstruction>(SettingsCatalog.SSR, "Reconstruction backend");
            ReceiverCutoff = catalog.Bind<int>(SettingsCatalog.SSR, "Minimum receiver smoothness (%)");
            CutoffEnvironmentBlend = catalog.Bind<int>(SettingsCatalog.SSR, "Cutoff environment blend (%)");
            ZeroSmoothnessProbe = catalog.Bind<bool>(SettingsCatalog.SSR, "Zero-smoothness probe lighting");
            ZeroSmoothnessProbeStrength = catalog.Bind<int>(SettingsCatalog.SSR, "Zero-smoothness probe contribution (%)");
            ZeroSmoothnessProbeClose = catalog.Bind<int>(SettingsCatalog.SSR, "Zero-smoothness probe contribution close (%)");
            ZeroSmoothnessProbeFadeStart = catalog.Bind<float>(SettingsCatalog.SSR, "Zero-smoothness probe fade start");
            ZeroSmoothnessProbeFadeEnd = catalog.Bind<float>(SettingsCatalog.SSR, "Zero-smoothness probe fade end");
            SurfaceColourInfluence = catalog.Bind<int>(SettingsCatalog.SSR, "Water reflection surface colour influence (%)");
            Temporal = catalog.Bind<bool>(SettingsCatalog.SSR, "Temporal stabilization");
            NrdAntiFirefly = catalog.Bind<bool>(SettingsCatalog.SSR, "NRD anti-firefly");
            HistoryFrames = catalog.Bind<int>(SettingsCatalog.SSR, "History frames");
            View = catalog.Bind<ReflectionView>(SettingsCatalog.SSR, "Debug view");
            Freeze = catalog.Bind<bool>(SettingsCatalog.SSR, "Freeze ray sequence");
        }

        internal Options Read()=>new Options {
            NrdAntiFirefly=NrdAntiFirefly.Value,
            Mode=Enabled.Value?SsrMode.Stochastic:SsrMode.Native,Resolution=Resolution.Value,View=View.Value,Freeze=Freeze.Value,
            Steps=Steps.Value,Thickness=Thickness.Value,Smoothness=.5f,FallbackStrength=SettingsCatalog.Fraction(FallbackStrength),SurfaceColourInfluence=SettingsCatalog.Fraction(SurfaceColourInfluence),
            Temporal=Temporal.Value,HistoryFrames=HistoryFrames.Value,EdgePixels=0,
            ZeroSmoothnessProbe=ZeroSmoothnessProbe.Value,ZeroSmoothnessProbeStrength=SettingsCatalog.Fraction(ZeroSmoothnessProbeStrength),
            ZeroSmoothnessProbeClose=SettingsCatalog.Fraction(ZeroSmoothnessProbeClose),ZeroSmoothnessProbeFadeStart=ZeroSmoothnessProbeFadeStart.Value,ZeroSmoothnessProbeFadeEnd=ZeroSmoothnessProbeFadeEnd.Value,
            ContinuationSamples=ContinuationSamples.Value,ContinuationPixels=ContinuationPixels.Value,ContinuationDepth=ContinuationDepth.Value,CaptureContinuation=CaptureContinuation.Value,CaptureTraversal=CaptureTraversal.Value,
            ThicknessConfidenceFloor=SettingsCatalog.Fraction(ThicknessConfidenceFloor),Reconstruction=Reconstruction.Value,ReceiverCutoff=SettingsCatalog.Fraction(ReceiverCutoff),CutoffEnvironmentBlend=SettingsCatalog.Fraction(CutoffEnvironmentBlend)
        };
    }
}
