using BepInEx.Configuration;
using System.Linq;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal sealed class SsrSettings
    {
        internal readonly ConfigEntry<bool> Enabled,LimitRange,Temporal,Freeze,BypassHitNormal,CaptureContinuation,CaptureTraversal,GlobalContinuedSky;
        internal readonly ConfigEntry<int> Steps,HistoryFrames,ContinuationSamples;
        internal readonly ConfigEntry<float> Distance,Thickness,FallbackStrength,SurfaceColourInfluence,ContinuationPixels,ContinuationDepth,ThicknessConfidenceFloor;
        internal readonly ConfigEntry<ReflectionResolution> Resolution;
        internal readonly ConfigEntry<ReflectionView> View;
        internal readonly ConfigEntry<ReflectionReconstruction> Reconstruction;
        internal readonly ConfigEntry<float> ReceiverCutoff,CutoffEnvironmentBlend;
        internal readonly ConfigEntry<bool> ZeroSmoothnessProbe;
        internal readonly ConfigEntry<bool> NrdAntiFirefly;
        internal readonly ConfigEntry<float> ZeroSmoothnessProbeStrength,ZeroSmoothnessProbeClose,ZeroSmoothnessProbeFadeStart,ZeroSmoothnessProbeFadeEnd;
        internal bool NeedsWaterCapture=>Enabled.Value; // Water-only sky recovery, material tint and receiver ownership.
        internal SsrSettings(ConfigFile config)
        {
            bool save=config.SaveOnConfigSet;config.SaveOnConfigSet=false;
            try {
            RemoveRetired(config);
            Enabled=config.Bind("Stochastic SSR","Enabled",false,"Environment replacement with geometry reflections and confirmed TOD sky blended with native-style receiver colour. Unresolved coverage uses receiver-colour fallback. Keep SSR enabled in game. Off restores native SSR. Optics remain native; SSRSix bypasses this feature.");
            Resolution=Advanced(config,"SSR resolution",ReflectionResolution.R768,"Independent square SSR trace resolution. Camera depth remains at camera resolution. Changes recreate buffers and reset history; native SSR and optics retain game settings.");
            Steps=Advanced(config,"Maximum iterations",350,"Maximum Hi-Z traversal iterations, including continued foreground cells. Experimental upper limit 1024. Higher values can increase GPU trace cost; continuation sample/pixel/depth limits remain independent. Live changes reset history.",new AcceptableValueRange<int>(32,1024));
            Thickness=Advanced(config,"Thickness",1f,"Maximum accepted depth penetration in metres.",new AcceptableValueRange<float>(.01f,1));
            ThicknessConfidenceFloor=Advanced(config,"Thickness hit confidence floor",1f,"Diagnostic comparison for already accepted thickness-only hits (ray does not cross the sampled front depth inside that pixel). 0 preserves original depth confidence; 1 removes depth-confidence attenuation for these hits. Intermediate values lift the confidence floor. Does not accept rejected rays, alter bracketed crossings, or bypass normal/edge checks. High values can expose false thickness matches; this is not a validated fix. Clears history live.",new AcceptableValueRange<float>(0,1));
            BypassHitNormal=Advanced(config,"Bypass hit normal checks",true,"Live comparison: bypass hit-surface normal rejection and its grazing-angle confidence reduction. Off preserves the existing behaviour. On still requires a valid depth intersection and retains self-hit, thickness, screen-edge and receiver checks. Applies to all SSSR receivers, not only water. Changing this clears reflection history; no restart required.");
            ContinuationSamples=Advanced(config,"Occluder continuation samples",350,"Experimental per-ray budget of full-resolution depth cells skipped after foreground-depth or facing rejection, up to 4096. 0 retains immediate rejection. Total skipped distance and maximum depth separation are separately bounded. Maximum iterations still caps total traversal work, including skipped cells. A later bounded geometry hit may be accepted without an intervening return to visible space. Completed rays can recover sky only after clear far-plane endpoint validation, on captured water or all eligible receivers when Allow continued sky on all receivers is enabled. Live changes clear history.",new AcceptableValueRange<int>(0,4096));
            ContinuationPixels=Advanced(config,"Occluder continuation pixels",500f,"Maximum cumulative screen distance skipped per ray, in full-resolution major-axis pixels, up to 8192. Applies only when continuation samples is above 0. Wide allowances permit traversing large weapon silhouettes but can increase tracing cost and leak through hidden geometry. Maximum iterations still applies.",new AcceptableValueRange<float>(0,8192));
            ContinuationDepth=Advanced(config,"Occluder continuation depth separation",200f,"Maximum ray-to-foreground eye-depth separation in metres at any skipped cell. This is not hit thickness. Applies only when continuation samples is above 0.",new AcceptableValueRange<float>(0,1000));
            GlobalContinuedSky=Advanced(config,"Allow continued sky on all receivers",true,"Allow completed, bounded occluder continuation to recover sky on every SSR-eligible receiver, including wet-road puddles. Off retains water-only recovery. Both require a clear in-screen far-plane endpoint and preserve iteration/sample/pixel/depth limits. Does not recover budget failures or offscreen rays. Applies live and resets history.");
            CaptureContinuation=Advanced(config,"Capture continuation diagnostics",false,"One asynchronous capture on switching this on. Writes trace rejection/recovery data, exact sample/distance/depth histograms and coverage stage images under BepInEx/FTFX-SSR-Captures. Switch off then on for another capture. Records one frame; enable Freeze ray sequence for comparisons. Diagnostic readback cost is not representative of normal rendering.");
            CaptureTraversal=Advanced(config,"Capture traversal work",false,"One asynchronous traversal-work capture. Records eligibility, actual loop iterations, work after first continuation, descents, mip-zero visits, visited mip mask and repeated fine-cell visits for every ray and termination. Replaces ray/PDF and depth-hit diagnostics in this capture to stay within DX11 UAV limits. Diagnostic shader cost must not be used to measure normal rendering. Switch off/on for another capture; other visual settings and budgets are unchanged.");
            FallbackStrength=Advanced(config,"Fallback strength",.65f,"Brightness of receiver-colour fallback on unresolved or partially covered receivers. 1 preserves current brightness; 0 removes the miss fallback. Applies to all SSR-eligible materials, including water. Valid geometry and confirmed-sky radiance, reflection confidence and environment subtraction are unchanged.",new AcceptableValueRange<float>(0,1));
            Reconstruction=Advanced(config,"Reconstruction backend",ReflectionReconstruction.Custom,"Custom retains the existing full-resolution reconstruction. NativeGaussian applies the installed Gaussian mip filter and roughness LOD to the same measured reflection signal. Unresolved coverage uses receiver-colour fallback. Changes reset history.");
            ReceiverCutoff=Renamed(config,"Experimental receiver smoothness cutoff","Minimum receiver smoothness",.4f,"Minimum smoothness required to trace and add SSR. Below this threshold, use the environment-removed source scene with Cutoff environment blend; no receiver-colour fallback is added. Rough terrain and trees remain reflected sources. Receiver-colour fallback support keeps the native threshold. 0 retains the installed native default's full receiver coverage. Changes reset history.",new AcceptableValueRange<float>(0,1));
            CutoffEnvironmentBlend=Advanced(config,"Cutoff environment blend",.25f,"Lighting on receivers excluded by Minimum receiver smoothness: 0 shows the Environment removed scene source colour; 1 restores the incoming scene including its environment contribution. Intermediate values blend those endpoints. Uses the native environment-removal calculation shown in that debug view. Adds no SSR or receiver-colour fallback to excluded receivers.",new AcceptableValueRange<float>(0,1));
            ZeroSmoothnessProbe=Advanced(config,"Experimental zero-smoothness probe lighting",true,"Experimental replacement for receivers with smoothness exactly 0 (roughness 1) and non-zero specular RGB. Skips GGX tracing and receiver-colour fallback on those receivers, including those below the receiver cutoff or beyond the SSR range. Uses the roughness-filtered environment probe with material response, bottom shade and stencil visibility, deliberately bypassing GBuffer0 alpha suppression. Replaces any existing environment specular once; diffuse lighting is retained. Off restores ordinary SSR. Changes apply live and reset history.");
            ZeroSmoothnessProbeStrength=Advanced(config,"Zero-smoothness probe contribution",.3f,"Far contribution of experimental probe lighting, reached at the fade end distance. 1 applies the full material-weighted probe without GBuffer0 alpha suppression. Set both this and the close contribution to 0 to remove probe lighting while still bypassing GGX. Requires Experimental zero-smoothness probe lighting. Changes apply live.",new AcceptableValueRange<float>(0,1));
            ZeroSmoothnessProbeClose=Advanced(config,"Zero-smoothness probe contribution close",.1f,"Probe contribution up to the fade start distance. Smoothly transitions to Zero-smoothness probe contribution at the fade end distance. Requires Experimental zero-smoothness probe lighting. Changes apply live.",new AcceptableValueRange<float>(0,1));
            ZeroSmoothnessProbeFadeStart=Advanced(config,"Zero-smoothness probe fade start",5f,"Camera-to-receiver distance in metres where probe lighting starts transitioning from the close contribution to the far contribution. If fade end is at or below fade start, the contribution switches at fade start. Changes apply live.",new AcceptableValueRange<float>(0,1000));
            ZeroSmoothnessProbeFadeEnd=Advanced(config,"Zero-smoothness probe fade end",15f,"Camera-to-receiver distance in metres where the full configured Zero-smoothness probe contribution is reached. Changes apply live.",new AcceptableValueRange<float>(0,1000));
            SurfaceColourInfluence=Advanced(config,"Water reflection surface colour influence",.2f,"Experimental attenuation using the original GBuffer0 RGB sampled by CustomAmbient, before ambient/environment lighting. 0 leaves reflections unchanged; 1 multiplies added sky, geometry and miss fallback by that RGB clamped to 0-1. Only affects captured water, independently of shoreline depth and fade. Requires Custom Ambient Fix; missing capture leaves reflections unchanged.",new AcceptableValueRange<float>(0,1));
            LimitRange=Advanced(config,"Limit reflection range",false,"Limit custom SSR receivers by straight-line distance from the camera using Maximum distance. Excluded receivers skip tracing and use the same source-scene/environment blend as smoothness-cutoff receivers. Nearby receivers can still reflect distant geometry or confirmed TOD sky; reflected sources are not range-limited. Changes reset history.");
            Distance=Advanced(config,"Maximum distance",250f,"Maximum straight-line camera-to-receiver distance in metres when Limit reflection range is enabled. This is not a ray-length or reflected-hit distance limit. Existing saved values retain their number and acquire this receiver-distance meaning.",new AcceptableValueRange<float>(1,10000));
            Temporal=Advanced(config,"Temporal stabilization",true,"Enable NRD temporal accumulation. Off retains NRD spatial denoising with zero accumulated history.");
            NrdAntiFirefly=Advanced(config,"NRD anti-firefly",false,"Suppress NRD radiance outliers. Off preserves isolated small bright reflection details. Changes reset NRD history.");
            HistoryFrames=Advanced(config,"History frames",16,"Maximum temporal accumulation weight. Limits NRD main and stabilized history; fast history is capped at 6 frames. Requires Temporal stabilization.",new AcceptableValueRange<int>(1,16));
            View=Renamed(config,"Debug view","Debug view",ReflectionView.Normal,"Inspect current reflection, coverage, continuation and depth-hit signals. DepthHitKind and DepthHitConfidence activate optional hit diagnostics. Captures activate ray/PDF and depth diagnostics for one frame.");
            Freeze=Advanced(config,"Freeze ray sequence",false,"Fix the stochastic sample index for deterministic comparisons. Does not freeze the world or camera.");
            } finally {config.SaveOnConfigSet=save;if(save)config.Save();}
        }
        private static void RemoveRetired(ConfigFile c)
        {
            foreach(var name in new[]{"Use captured CustomAmbient subtraction","Normal stabilization","Trace normal strength","Normal stabilization start distance","Normal stabilization end distance","Normal filter radius","Experimental NRD REBLUR specular","Rough reflection coverage accumulation","Rough coverage full-strength smoothness","Rough coverage fade-out smoothness","Coverage reconstruction filter","Coverage filter strength","Coverage filter full-strength smoothness","Coverage filter fade-end smoothness","Coverage filter smooth-surface strength","Water normal stabilization","Water trace normal strength","Experimental foreground DDA continuation","Ray direction","Match camera aspect ratio","Water normal variance filtering","Constrain hit sample to depth cell","Missing depth coverage","Source colour blend","Relax occluder return traversal","Hit normal confidence only","Sky after completed continuation","Hit-aware reconstruction","Reflection gap fill strength","Reflection gap fill radius","Foreground occlusion uses TOD","Foreground TOD brightness","Foreground adjacent reflection influence","Foreground gap fill permissiveness","Shallow water reflection fade","Shallow water restoration strength","Denoiser backend","AMD mirror temporal","AMD mirror spatial","AMD exact-hit candidate","AMD history-only recovery","AMD recovery frames","Spatial filter","Sampling mode","Adaptive rays","Normal variance filtering"})c.Remove(c.Bind("Stochastic SSR",name,"").Definition);
        }
        private static ConfigEntry<T> Advanced<T>(ConfigFile c,string name,T value,string text,AcceptableValueBase range=null)=>
            c.Bind("Stochastic SSR",name,value,new ConfigDescription(text,range,new ConfigurationManagerAttributes{IsAdvanced=true}));
        private static ConfigEntry<T> Renamed<T>(ConfigFile c,string legacyName,string name,T value,string text,AcceptableValueBase range=null)
        {
            if(c.TryGetEntry<T>("Stochastic SSR",name,out var existing))return existing;
            bool save=c.SaveOnConfigSet;c.SaveOnConfigSet=false;
            try {
                string oldValue="";
                if(legacyName!=name) {
                    var legacy=c.Bind("Stochastic SSR",legacyName,"");oldValue=legacy.Value;c.Remove(legacy.Definition);
                }
                var current=c.Bind("Stochastic SSR",name,"");string newValue=current.Value;c.Remove(current.Definition);
                var entry=Advanced(c,name,value,text,range);
                string serialized=!string.IsNullOrEmpty(newValue)?newValue:oldValue;
                if(name=="Minimum receiver smoothness") {
                    switch(serialized) {
                        case "Native":case "Cutoff0":serialized="0";break;
                        case "Cutoff010":serialized="0.1";break;
                        case "Cutoff025":serialized="0.25";break;
                        case "Cutoff050":serialized="0.5";break;
                        case "Cutoff065":serialized="0.65";break;
                        case "Cutoff075":serialized="0.75";break;
                    }
                }
                if(typeof(T)==typeof(ReflectionView)) {
                    if(serialized=="EnvironmentFallback" || serialized=="17")serialized="Normal";
                    if(serialized=="WaterNormalStability" || serialized=="NormalStability" || serialized=="40" || new[]{"CaptureNormal","ExactEnvironmentContribution","EnvironmentDifference","ExactRemovalScene","ExactReplacement","ExactRoundTrip","ExactRoundTripError","18","19","20","21","22","23","24"}.Contains(serialized))serialized="Normal";
                    if(serialized=="ForegroundOcclusion" || serialized=="ReuseProvenance" || serialized=="ReuseRejection" || serialized=="37" || serialized=="38" || serialized=="39")serialized="Normal";
                }
                if(!string.IsNullOrEmpty(serialized))entry.SetSerializedValue(serialized);
                return entry;
            } finally {c.SaveOnConfigSet=save;if(save)c.Save();}
        }
        internal Options Read()=>new Options {
            NrdAntiFirefly=NrdAntiFirefly.Value,
            Mode=Enabled.Value?SsrMode.Stochastic:SsrMode.Native,Resolution=Resolution.Value,View=View.Value,Freeze=Freeze.Value,
            Steps=Steps.Value,Distance=Distance.Value,Thickness=Thickness.Value,Smoothness=.5f,FallbackStrength=FallbackStrength.Value,SurfaceColourInfluence=SurfaceColourInfluence.Value,
            Temporal=Temporal.Value,HistoryFrames=HistoryFrames.Value,EdgePixels=0,LimitNativeRange=LimitRange.Value,BypassHitNormal=BypassHitNormal.Value,
            ZeroSmoothnessProbe=ZeroSmoothnessProbe.Value,ZeroSmoothnessProbeStrength=ZeroSmoothnessProbeStrength.Value,
            ZeroSmoothnessProbeClose=ZeroSmoothnessProbeClose.Value,ZeroSmoothnessProbeFadeStart=ZeroSmoothnessProbeFadeStart.Value,ZeroSmoothnessProbeFadeEnd=ZeroSmoothnessProbeFadeEnd.Value,
            ContinuationSamples=ContinuationSamples.Value,ContinuationPixels=ContinuationPixels.Value,ContinuationDepth=ContinuationDepth.Value,GlobalContinuedSky=GlobalContinuedSky.Value,CaptureContinuation=CaptureContinuation.Value,CaptureTraversal=CaptureTraversal.Value,
            ThicknessConfidenceFloor=ThicknessConfidenceFloor.Value,Reconstruction=Reconstruction.Value,ReceiverCutoff=ReceiverCutoff.Value,CutoffEnvironmentBlend=CutoffEnvironmentBlend.Value
        };
    }
}
