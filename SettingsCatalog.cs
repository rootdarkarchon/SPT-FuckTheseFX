using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace FuckTheseFX
{
    // The only declaration of each setting's default, bounds and display metadata.
    // Enum defaults are names here; Bind<T> resolves them to the owning enum type.
    internal sealed class SettingsCatalog
    {
        internal const string Vanilla = "1. Vanilla Optimizations", XeGTAO = "2. XeGTAO",
            ContactGI = "3. Screen Space Contact Global Illumination", SSR = "4. Stochastic GGX SSR",
            Water = "5. Custom Water", Debug = "9. Debug";
        private sealed class Definition
        {
            internal readonly string Section, Key, Description;
            internal readonly object Default;
            internal readonly bool Advanced;
            internal readonly AcceptableValueBase Bounds;
            internal Definition(string section, string key, object value, string description, bool advanced, AcceptableValueBase bounds = null)
            { Section = section; Key = key; Default = value; Description = description; Advanced = advanced; Bounds = bounds; }
        }
        private static readonly Definition[] Definitions = {
            new Definition("1. Vanilla Optimizations", "Streets of Tarkov Glass Culling Fix", true, "Only active on Streets of Tarkov. Makes omitted glass follow native culling of vehicle parts, windows and enclosing building sections to improve performance. Inactive on all other maps. Off restores normal glass rendering.", false),
            new Definition("1. Vanilla Optimizations", "Additional Ambient Highlight", true, "When enabled uses vanilla Tarkovs behavior: Adds extra ambient brightness to weapons, hands and selected objects. Off removes this artificial glow. Performance: no measurable impact.", false),
            new Definition("1. Vanilla Optimizations", "Vintage Filter", true, "Off disables the vintage camera filter. On restores effects disabled by Fuck These FX. Performance: no measurable impact.", false),
            new Definition("1. Vanilla Optimizations", "Sharpen Filter", true, "On retains Tarkov's CC_Sharpen filter. Off removes the filter, including its combined desaturation. Applies live; enabling restores only effects disabled by Fuck These FX.", false),
            new Definition("1. Vanilla Optimizations", "Skip Neutral Effects Passes", true, "Skips neutral status-effect copies inside the active upscaler image chain. Zero-aperture disc DoF retains its native alpha/CoC pass and skips blur/compositing. Active effects and ordinary rendering retain native behavior. Applies live.", false),
            new Definition("1. Vanilla Optimizations", "Prism Auto Exposure Resolution (%)", 66, "Side length of the square exposure sampling grid as a percentage of the game's original half-width grid. At 4K: 100% = 1920x1920, 50% = 960x960. Lower values reduce exposure sampling detail; scene sharpness and tone-mapping resolution are unchanged. 100% retains native behavior.", false, new AcceptableValueRange<int>(1, 100)),
            new Definition("1. Vanilla Optimizations", "Ultimate Bloom Input Scale (%)", 50, "Bloom input dimensions as an integer percentage. Lower values soften glow and flares while keeping the scene sharp. 100% retains the original bloom input without a reduced copy. Performance: positive impact when reducing input scale.", false, new AcceptableValueRange<int>(25, 100)),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Resolution Fix", true, "Adjusts distant shadow detail and smoothing. Off keeps the game's original distant shadows. Performance: small impact at defaults.", false),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Resolution", "Half", "Higher resolutions sharpen distant shadow edges; lower resolutions look softer.", false),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Adjust Filter Samples", true, "Uses the sample counts below. Off restores the game's shadow filtering.", true),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Quarter Filter Samples", 25, "More samples smooth quarter-resolution shadow edges and reduce flicker. 25 uses native filtering.", true, new AcceptableValueRange<int>(1, 25)),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Half Filter Samples", 12, "More samples smooth half-resolution shadow edges and reduce flicker. 25 uses native filtering.", true, new AcceptableValueRange<int>(1, 25)),
            new Definition("1. Vanilla Optimizations", "Distant Shadows Full Filter Samples", 8, "More samples smooth full-resolution shadow edges and reduce flicker. 25 uses native filtering.", true, new AcceptableValueRange<int>(1, 25)),
            new Definition("2. XeGTAO", "Enabled", false, "Replace Tarkov's AO with XeGTAO. Off restores native AO. Quality is selected below; game AO Off disables the replacement and actor pass. Performance: no impact or an improvement over vanilla HBAO.", false),
            new Definition("2. XeGTAO", "Quality", 3, "Quality for primary and actor XeGTAO: 0 Low (1 slice/2 steps), 1 Medium (2/2), 2 High (3/3), 3 Ultra (9/3). Independent of Tarkov's HBAO quality. Advanced primary sample overrides still apply.", false, new AcceptableValueRange<int>(0, 3)),
            new Definition("2. XeGTAO", "EffectRadius", 1f, "AO radius in metres before RadiusMultiplier.", true, new AcceptableValueRange<float>(0.01f, 5f)),
            new Definition("2. XeGTAO", "RadiusMultiplier", 1.457f, "Multiplies radius in the AO calculation and depth prefilter.", true, new AcceptableValueRange<float>(0.1f, 3f)),
            new Definition("2. XeGTAO", "FalloffRange (%)", 62, "Percentage of the effective radius over which occlusion fades out. 62 means 62% of the radius.", true, new AcceptableValueRange<int>(5, 100)),
            new Definition("2. XeGTAO", "SampleDistributionPower", 2.5f, "Higher values concentrate samples near the pixel.", true, new AcceptableValueRange<float>(0.5f, 4f)),
            new Definition("2. XeGTAO", "SliceCount", 0, "Search slices. 0 follows the FTFX Quality slider (1/2/3/9).", true, new AcceptableValueRange<int>(0, 9)),
            new Definition("2. XeGTAO", "SamplesPerSlice", 0, "Samples per side of each slice. 0 follows the FTFX Quality slider (2/2/3/3).", true, new AcceptableValueRange<int>(0, 8)),
            new Definition("2. XeGTAO", "ThicknessHeuristic (%)", 0, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Thin-occluder compensation. Zero disables this heuristic.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("2. XeGTAO", "Denoise passes", 2, "Spatial denoising: 0 off, 1 sharp, 2 medium, 3 soft.", true, new AcceptableValueRange<int>(0, 3)),
            new Definition("2. XeGTAO", "DenoiseSharpness", 0.4f, "Denoiser center-pixel weight. Higher values retain detail and noise.", true, new AcceptableValueRange<float>(0.1f, 10f)),
            new Definition("2. XeGTAO", "FinalPower", 1.5f, "Visibility power before denoising. Higher values darken AO.", true, new AcceptableValueRange<float>(0.5f, 5f)),
            new Definition("2. XeGTAO", "Distance fade start", 100f, "View depth in metres where primary AO starts fading. Shared with the Custom Ambient Fix, foliage and bent normals; the actor second pass keeps its own cutoff.", true, new AcceptableValueRange<float>(0f, 250f)),
            new Definition("2. XeGTAO", "Distance fade end", 250f, "View depth in metres where primary AO reaches zero. Shared with the Custom Ambient Fix. Values below start + 1 use a one-metre fade after the start; saved values are preserved.", true, new AcceptableValueRange<float>(0f, 500f)),
            new Definition("2. XeGTAO", "UseNormals", true, "Use G-buffer normals including normal maps; off reconstructs geometric normals from depth.", true),
            new Definition("2. XeGTAO", "Custom Ambient Fix Enabled", true, "Make custom diffuse ambient and fill respect XeGTAO. Does not change direct lights or Ambient Highlight.", true),
            new Definition("2. XeGTAO", "Reflection occlusion fix", true, "Reduce screen-space reflection occlusion by up to 50% on smooth surfaces, tapering with smoothness squared to no reduction on fully rough surfaces. Applies at every viewing angle. Excludes the native motion-depth vegetation path to preserve its reflection shading. Shares the corrected material visibility with native reflection probes and SSR, while preserving diffuse AO and authored material occlusion. Requires Custom Ambient Fix enabled with strength above zero. Toggle live to compare; off restores full screen-space reflection AO.", true),
            new Definition("2. XeGTAO", "Custom Ambient Fix Bent normals", false, "Use XeGTAO bent normals for directional SH ambient lighting. Requires XeGTAO and the CustomAmbient fix. Adds GPU work; leaves reflections, fill and the actor pass unchanged. Respects Strength, Foliage AO strength and the AO distance fade.", true),
            new Definition("2. XeGTAO", "Custom Ambient Fix Strength (%)", 60, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. AO attenuation of custom diffuse ambient and fill. Zero restores native ambient.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("2. XeGTAO", "Custom Ambient Fix Foliage AO strength (%)", 65, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. AO strength on authored zero-alpha materials, including foliage.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("2. XeGTAO", "Second Pass Enabled", true, "Additional independent full-resolution contact AO on hands, held weapons and characters. Requires XeGTAO and game AO enabled.", false),
            new Definition("2. XeGTAO", "Second Pass Radius", 0.3f, "Independent radius in metres, without the primary radius multiplier.", true, new AcceptableValueRange<float>(0.01f, 2f)),
            new Definition("2. XeGTAO", "Second Pass Strength", 1.5f, "Actor obscurance strength. Zero bypasses the pass.", true, new AcceptableValueRange<float>(0f, 4f)),
            new Definition("2. XeGTAO", "Second Pass Response", 0.6f, "Power on actor obscurance; values below one emphasize faint AO.", true, new AcceptableValueRange<float>(0.25f, 2f)),
            new Definition("2. XeGTAO", "Second Pass Distance cutoff", 10f, "Stops extra actor AO beyond this distance, fading over the last 20%.", true, new AcceptableValueRange<float>(0.1f, 100f)),
            new Definition("2. XeGTAO", "Foliage extra AO strength (%)", 20, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Additional scene-colour AO on foliage-compatible zero-alpha opaque materials before native opaque post-processing and fog. Main camera HDR only; excludes actors and sky. Shares the AO distance fade. Zero disables it. Independent of Custom Ambient Fix strength; may also affect non-foliage zero-alpha materials and direct surface lighting.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("3. Screen Space Contact Global Illumination", "Enabled", false, "Experimental short-range diffuse color bounce. Requires FTFX XeGTAO and the main HDR deferred camera. Supports Gamma and Linear color space; Gamma uses an extra scene snapshot for correct compositing. Off by default; excludes actors, optics and transparent surfaces. Performance: small-medium impact at defaults.", false),
            new Definition("3. Screen Space Contact Global Illumination", "Strength", 3f, "Brightness of the added bounce, calibrated for sparse sampling. 1 is full strength; up to 4 allows stronger color transfer. Maximum bounce fraction still caps the reflected addition. Debug Screen Space Contact GI gain affects only the diagnostic display.", true, new AcceptableValueRange<float>(0f, 4f)),
            new Definition("3. Screen Space Contact Global Illumination", "Resolution", "Quarter", "Half or quarter of each internal rendering dimension. Quarter is softer and can miss small colored objects.", true),
            new Definition("3. Screen Space Contact Global Illumination", "Spatial filter", true, "Smooths sparse bounce using geometric depth, normal and visibility. With temporal accumulation, young or noisy history receives wider variance-guided filtering. Stable history keeps the narrower filter. Filtered results are not saved into history. Disable for an A/B comparison.", true),
            new Definition("3. Screen Space Contact Global Illumination", "Temporal accumulation", true, "Accumulates varying samples using camera reprojection and surface/history rejection. Reduces speckling; moving objects and changing lighting can still leave short trails. Disable to restore the fixed spatial sample pattern.", true),
            new Definition("3. Screen Space Contact Global Illumination", "History frames", 16, "Maximum effective history length. More frames smooth noise but respond more slowly to changes. History resets on camera cuts, resize and Screen Space Contact GI tuning changes.", true, new AcceptableValueRange<int>(4, 30)),
            new Definition("3. Screen Space Contact Global Illumination", "Samples", 8, "Number of nearby donors; each surviving donor also uses a midpoint depth check. Higher counts reduce sampling noise at increased GPU cost.", true, new AcceptableValueList<int>(4, 6, 8, 16, 32)),
            new Definition("3. Screen Space Contact Global Illumination", "Radius", 0.75f, "Maximum distance between receiving and donating surfaces, in metres.", true, new AcceptableValueRange<float>(0.25f, 1.5f)),
            new Definition("3. Screen Space Contact Global Illumination", "Depth tolerance", 0.02f, "Metres of tolerance for visibility and surface reconstruction. Larger values can leak through thin objects.", true, new AcceptableValueRange<float>(0.002f, 0.1f)),
            new Definition("3. Screen Space Contact Global Illumination", "Donor luminance limit", 2f, "Maximum donor luminance in pre-tonemap HDR units. Limits emissive and reflection outliers without changing hue.", true, new AcceptableValueRange<float>(0.1f, 16f)),
            new Definition("3. Screen Space Contact Global Illumination", "Maximum bounce fraction (%)", 20, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Caps reflected added luminance relative to the local limited donor reference, after receiver reflectance and before AO/distance fades. Raising Strength cannot exceed this cap.", true, new AcceptableValueRange<int>(1, 50)),
            new Definition("3. Screen Space Contact Global Illumination", "Distance cutoff", 25f, "Stops color bounce beyond this camera distance, fading over the last 40 percent.", true, new AcceptableValueRange<float>(5f, 50f)),
            new Definition("4. Stochastic GGX SSR", "Enabled", false, "Environment replacement with geometry reflections and confirmed TOD sky blended with native-style receiver colour. Unresolved coverage uses receiver-colour fallback. Keep SSR enabled in game. Off restores native SSR. Optics remain native.", false),
            new Definition("4. Stochastic GGX SSR", "SSR resolution", "R768", "Independent square SSR trace resolution. Camera depth remains at camera resolution. Changes recreate buffers and reset history; native SSR and optics retain game settings.", false),
            new Definition("4. Stochastic GGX SSR", "Maximum iterations", 350, "Maximum Hi-Z traversal iterations, including continued foreground cells. Experimental upper limit 1024. Higher values can increase GPU trace cost; continuation sample/pixel/depth limits remain independent. Live changes reset history.", true, new AcceptableValueRange<int>(32, 1024)),
            new Definition("4. Stochastic GGX SSR", "Thickness", 1f, "Maximum accepted depth penetration in metres.", true, new AcceptableValueRange<float>(0.01f, 1f)),
            new Definition("4. Stochastic GGX SSR", "Thickness hit confidence floor (%)", 100, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Diagnostic comparison for already accepted thickness-only hits (ray does not cross the sampled front depth inside that pixel). 0 preserves original depth confidence; 100% removes depth-confidence attenuation for these hits. Intermediate values lift the confidence floor. Does not accept rejected rays, alter bracketed crossings, or bypass normal/edge checks. High values can expose false thickness matches; this is not a validated fix. Clears history live.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Occluder continuation samples", 350, "Experimental per-ray budget of full-resolution depth cells skipped after foreground-depth, up to 1000. 0 retains immediate rejection. Total skipped distance and maximum depth separation are separately bounded. Maximum iterations still caps total traversal work, including skipped cells. A later bounded geometry hit may be accepted without an intervening return to visible space. Completed rays can recover sky only after clear far-plane endpoint validation on all eligible receivers. Live changes clear history.", true, new AcceptableValueRange<int>(0, 1000)),
            new Definition("4. Stochastic GGX SSR", "Occluder continuation pixels", 500f, "Maximum cumulative screen distance skipped per ray, in full-resolution major-axis pixels, up to 2000. Applies only when continuation samples is above 0. Wide allowances permit traversing large weapon silhouettes but can increase tracing cost and leak through hidden geometry. Maximum iterations still applies.", true, new AcceptableValueRange<float>(0f, 2000f)),
            new Definition("4. Stochastic GGX SSR", "Occluder continuation depth separation", 200f, "Maximum ray-to-foreground eye-depth separation in metres at any skipped cell. This is not hit thickness. Applies only when continuation samples is above 0.", true, new AcceptableValueRange<float>(0f, 500f)),
            new Definition("4. Stochastic GGX SSR", "Capture continuation diagnostics", false, "One asynchronous capture on switching this on. Writes trace rejection/recovery data, exact sample/distance/depth histograms and coverage stage images under BepInEx/FTFX-SSR-Captures. Switch off then on for another capture. Records one frame; enable Freeze ray sequence for comparisons. Diagnostic readback cost is not representative of normal rendering.", true),
            new Definition("4. Stochastic GGX SSR", "Capture traversal work", false, "One asynchronous traversal-work capture. Records eligibility, actual loop iterations, work after first continuation, descents, mip-zero visits, visited mip mask and repeated fine-cell visits for every ray and termination. Replaces ray/PDF and depth-hit diagnostics in this capture to stay within DX11 UAV limits. Diagnostic shader cost must not be used to measure normal rendering. Switch off/on for another capture; other visual settings and budgets are unchanged.", true),
            new Definition("4. Stochastic GGX SSR", "Fallback strength (%)", 65, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Brightness of receiver-colour fallback on unresolved or partially covered receivers. 100% preserves current brightness; 0 removes the miss fallback. Applies to all SSR-eligible materials, including water. Valid geometry and confirmed-sky radiance, reflection confidence and environment subtraction are unchanged.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Reconstruction backend", "Custom", "Custom retains the existing full-resolution reconstruction. NativeGaussian applies the installed Gaussian mip filter and roughness LOD to the same measured reflection signal. Unresolved coverage uses receiver-colour fallback. Changes reset history.", true),
            new Definition("4. Stochastic GGX SSR", "Minimum receiver smoothness (%)", 40, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Minimum smoothness required to trace and add SSR. Below this percentage threshold, use the environment-removed source scene with Cutoff environment blend; no receiver-colour fallback is added. Rough terrain and trees remain reflected sources. Receiver-colour fallback support keeps the native threshold. 0 retains the installed native default's full receiver coverage. Changes reset history.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Cutoff environment blend (%)", 25, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Lighting on receivers excluded by Minimum receiver smoothness: 0 shows the Environment removed scene source colour; 100% restores the incoming scene including its environment contribution. Intermediate values blend those endpoints. Uses the native environment-removal calculation shown in that debug view. Adds no SSR or receiver-colour fallback to excluded receivers.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Zero-smoothness probe lighting", true, "Replacement for receivers with smoothness exactly 0 (roughness 1) and non-zero specular RGB. Skips GGX tracing and receiver-colour fallback on those receivers, including those below the receiver cutoff or outside the receiver cutoff. Uses the roughness-filtered environment probe with material response, bottom shade and stencil visibility, deliberately bypassing GBuffer0 alpha suppression. Replaces any existing environment specular once; diffuse lighting is retained. Off restores ordinary SSR. Changes apply live and reset history.", true),
            new Definition("4. Stochastic GGX SSR", "Zero-smoothness probe contribution (%)", 30, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Far contribution of experimental probe lighting, reached at the fade end distance. 100% applies the full material-weighted probe without GBuffer0 alpha suppression. Set both this and the close contribution to 0 to remove probe lighting while still bypassing GGX. Requires Zero-smoothness probe lighting. Changes apply live.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Zero-smoothness probe contribution close (%)", 10, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Probe contribution up to the fade start distance. Smoothly transitions to Zero-smoothness probe contribution at the fade end distance. Requires Zero-smoothness probe lighting. Changes apply live.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Zero-smoothness probe fade start", 5f, "Camera-to-receiver distance in metres where probe lighting starts transitioning from the close contribution to the far contribution. If fade end is at or below fade start, the contribution switches at fade start. Changes apply live.", true, new AcceptableValueRange<float>(0f, 50f)),
            new Definition("4. Stochastic GGX SSR", "Zero-smoothness probe fade end", 15f, "Camera-to-receiver distance in metres where the full configured Zero-smoothness probe contribution is reached. Changes apply live.", true, new AcceptableValueRange<float>(0f, 150f)),
            new Definition("4. Stochastic GGX SSR", "Water reflection surface colour influence (%)", 20, "Integer percentage: 100 means 100% (runtime fraction 1.0); 0 means 0%. Experimental attenuation using the original GBuffer0 RGB sampled by CustomAmbient, before ambient/environment lighting. 0 leaves reflections unchanged; 100% multiplies added sky, geometry and miss fallback by that RGB clamped to 0-1. Only affects captured water, independently of shoreline depth and fade. Requires Custom Ambient Fix; missing capture leaves reflections unchanged.", true, new AcceptableValueRange<int>(0, 100)),
            new Definition("4. Stochastic GGX SSR", "Temporal stabilization", true, "Enable NRD temporal accumulation. Off retains NRD spatial denoising with zero accumulated history.", true),
            new Definition("4. Stochastic GGX SSR", "NRD anti-firefly", false, "Suppress NRD radiance outliers. Off preserves isolated small bright reflection details. Changes reset NRD history.", true),
            new Definition("4. Stochastic GGX SSR", "History frames", 8, "Maximum temporal accumulation weight. Limits NRD main and stabilized history; fast history is capped at 6 frames. Requires Temporal stabilization.", true, new AcceptableValueRange<int>(1, 32)),
            new Definition("4. Stochastic GGX SSR", "Debug view", "Normal", "Inspect current reflection, coverage, continuation and depth-hit signals. DepthHitKind and DepthHitConfidence activate optional hit diagnostics. Captures activate ray/PDF and depth diagnostics for one frame.", true),
            new Definition("4. Stochastic GGX SSR", "Freeze ray sequence", false, "Fix the stochastic sample index for deterministic comparisons. Does not freeze the world or camera.", true),
            new Definition("5. Custom Water", "Mode", "Native", "Native keeps Tarkov water. FTFX uses broader, slower waves and gentler fine ripples on Customs, including optics. Other maps and water families remain native.", true),
            new Definition("5. Custom Water", "Show replacement tint", false, "Colors FTFX water bright magenta to confirm that the replacement is visible. Native stays normal. Turn off after checking the switch.", true),
            new Definition("5. Custom Water", "Surface size", 1f, "FTFX only. Larger values spread the surface patterns over a larger area. 1 uses the phase 2 default.", true, new AcceptableValueRange<float>(0.25f, 4f)),
            new Definition("5. Custom Water", "Surface strength", 1f, "FTFX only. Strength of the surface normal variation. 0 flattens it; native rain ripples remain active. 1 uses the phase 2 default.", true, new AcceptableValueRange<float>(0f, 2f)),
            new Definition("5. Custom Water", "Fine ripple strength", 1f, "FTFX only. Strength of fine detail relative to the broader patterns. 0 removes fine detail. 1 uses the restrained phase 2 default.", true, new AcceptableValueRange<float>(0f, 4f)),
            new Definition("5. Custom Water", "Animation speed", 1f, "FTFX only. Surface pattern movement speed. Changes preserve the current pattern position; 0 freezes it in place. Native foam and rain still animate. 1 uses the phase 2 default.", true, new AcceptableValueRange<float>(0f, 2f)),
            new Definition("9. Debug", "View", "Normal", "FoliageLive opens live stage previews, temporary lighting bypasses and a split-screen extra AO experiment; use the Foliage live controls below. Returning to Normal resets both bypasses; extra AO remains controlled under XeGTAO. FoliageLightingCapture saves one main-camera frame of AO, ambient and later lighting stages under BepInEx/FuckTheseFX/FoliageDiagnostics, then returns to Normal. Hold still; saving briefly pauses the game. Enable Custom Ambient Fix for its inputs. Other views are full-screen diagnostics. Normal restores the scene. Actor view is white when inactive. BentNormals shows world-space directions as RGB; gray when inactive. ScreenSpaceContactGI isolates the final added RGB before fog/tonemap; requires Screen Space Contact GI Enabled. Adjust Screen Space Contact GI gain for display brightness. ScreenSpaceContactGIHistory: green retained, red depth/position rejection, yellow normal rejection, cyan lighting rejection, orange fresh/offscreen history, blue invalid surface, magenta strongly limited. Requires Temporal accumulation; display gain does not apply.", true),
            new Definition("9. Debug", "Bent difference gain", 32f, "Amplification for BentSHDifference and BentLightingDifference. Black means zero change; brighter means larger absolute RGB change. Cyan means no CustomAmbient capture; red means bent shader disabled; magenta means missing camera inputs. Diagnostics add GPU work.", true, new AcceptableValueRange<float>(1f, 1000f)),
            new Definition("9. Debug", "Ambient contribution gain", 1f, "Shared display amplification for AmbientDirectional, AmbientUpward and AmbientFill, after blend, albedo and AO. AmbientShares displays relative luminance: red directional, green upward, blue fill. AmbientInteriorTransmission displays visibility times directional blend weight; black blocks directional SH, white passes it. Shares/transmission ignore gain. Requires the CustomAmbient fix; works with bent normals on or off. Excludes reflections and direct lights.", true, new AcceptableValueRange<float>(1f, 1000f)),
            new Definition("9. Debug", "Depth range", 5f, "Depth display window in metres.", true, new AcceptableValueRange<float>(0.01f, 200f)),
            new Definition("9. Debug", "Depth start", 0f, "Depth display begins at this distance in metres.", true, new AcceptableValueRange<float>(0f, 200f)),
            new Definition("9. Debug", "Screen Space Contact GI gain", 16f, "Display amplification for Debug View = ScreenSpaceContactGI. Shows only the RGB added by the Screen Space Contact GI composite after strength, limits, receiver albedo, visibility and fades, before downstream fog/tonemap. Does not change scene lighting. Black means zero; cyan unavailable; magenta mismatched input dimensions. Requires Screen Space Contact GI Enabled and XeGTAO. Adds debug-only GPU work.", true, new AcceptableValueRange<float>(1f, 128f)),
            new Definition("9. Debug", "Foliage live preview", "Scene", "Requires View = FoliageLive, main camera, HDR and Custom Ambient Fix. Scene shows normal gameplay. Other modes show live raw stage buffers or absolute RGB differences, before final grading/upscaling. No files are saved. BeforeOpaque includes extra foliage AO; AfterOpaque includes native output BEFORE the temporary bypass.", true),
            new Definition("9. Debug", "Foliage live display gain", 1f, "Brightness of raw stage/difference previews only. Does not affect Scene or experimental AO. Try 8 for differences.", true, new AcceptableValueRange<float>(1f, 32f)),
            new Definition("9. Debug", "Foliage live bypass Contact GI", false, "Temporarily omit Contact GI on the main camera in FoliageLive. Preserves the saved GI settings. Resets on leaving the live view.", true),
            new Definition("9. Debug", "Foliage live bypass opaque colour", false, "In FoliageLive, restore the scene colour from immediately before Opaque Only Post-processing after that buffer executes. Isolates its combined colour result (including fog/SSR), not a single effect. Native commands still run. Resets on leaving the live view.", true),
            new Definition("9. Debug", "Foliage live split comparison", true, "Apply extra AO only RIGHT of the divider; LEFT retains the current rendering. GI/opaque bypasses affect both sides. Move the divider across the same tree to compare. FoliageLive only.", true),
            new Definition("9. Debug", "Foliage live divider (%)", 50, "Horizontal divider position in percent: 0% applies extra AO across the scene, 100% applies none. Used when split comparison is enabled.", true, new AcceptableValueRange<int>(0, 100)),
        };
        private readonly ConfigFile config;
        private readonly List<ConfigEntryBase> settings = new List<ConfigEntryBase>();
        internal IReadOnlyList<ConfigEntryBase> Settings => settings;
        internal SettingsCatalog(ConfigFile config) { this.config = config; }
        // ConfigurationManager 18.4 preserves first-seen section order, not numeric names.
        // Register one real entry per section before constructing the effect settings.
        internal void BindSection<T>(string section)
        {
            Bind<T>(section, Definitions.First(d => d.Section == section).Key);
        }
        internal ConfigEntry<T> Bind<T>(string section, string key)
        {
            var definition = Definitions.Single(d => d.Section == section && d.Key == key);
            var value = typeof(T).IsEnum ? (T)Enum.Parse(typeof(T), (string)definition.Default) : (T)definition.Default;
            var entry = config.Bind(section, key, value, new ConfigDescription(definition.Description, definition.Bounds,
                new ConfigurationManagerAttributes { IsAdvanced = definition.Advanced, ShowRangeAsPercent = false }));
            if (typeof(T).IsEnum && !Enum.IsDefined(typeof(T), entry.Value)) entry.Value = (T)entry.DefaultValue;
            if (!settings.Contains(entry)) settings.Add(entry);
            return entry;
        }
        internal static float Fraction(ConfigEntry<int> entry) => entry.Value / 100f;
        internal void BindPresets()
        {
            config.Bind("0. Presets", "Presets", "", new ConfigDescription("Hover over a preset for details. Hold Shift while clicking to restore all defaults before applying the preset.", null,
                new ConfigurationManagerAttributes { IsAdvanced = false, HideDefaultButton = true, HideSettingName = true, CustomDrawer = DrawPresets }));
        }
        internal void ValidateBindings()
        {
            if (settings.Count != Definitions.Length) throw new InvalidOperationException("Incomplete configuration catalog");
        }
        private static readonly GUIContent VanillaPresetButton = new GUIContent("Vanilla",
            "Disables FTFX fixes and changes, restoring Vanilla appearance. Useful for direct image and performance comparison.");
        private static readonly GUIContent VanillaPlusPresetButton = new GUIContent("FTFX Vanilla+ (Default)",
            "Retains Vanilla baseline appearance and fixes minor performance issues.");
        private static readonly GUIContent HighQualityPresetButton = new GUIContent("FTFX HQ Visuals",
            "Applies targeted FTFX visuals as designed by the author.");
        private const string PresetHelpText = "Hold Shift while pressing on a preset button to restore full defaults. FTFX provides more settings than shown when enabling \"Advanced Settings\". The current advanced settings, presets and defaults are tuned and authored for visuals and performance.";
        private GUIStyle presetHelpStyle;
        internal enum Preset { Vanilla, VanillaPlus, HighQualityVisuals }
        private void DrawPresets(ConfigEntryBase unused)
        {
            // Capture the modifier before GUILayout.Button consumes the click event.
            bool restoreDefaults = Event.current != null && Event.current.shift;
            if (presetHelpStyle == null) presetHelpStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.BeginVertical();
            GUILayout.Label(PresetHelpText, presetHelpStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(VanillaPresetButton)) ApplyPreset(Preset.Vanilla, restoreDefaults);
            if (GUILayout.Button(VanillaPlusPresetButton)) ApplyPreset(Preset.VanillaPlus, restoreDefaults);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(HighQualityPresetButton)) ApplyPreset(Preset.HighQualityVisuals, restoreDefaults);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        internal void ApplyPreset(Preset preset, bool restoreDefaults = false)
        {
            if (!Enum.IsDefined(typeof(Preset), preset)) throw new ArgumentOutOfRangeException(nameof(preset));
            bool save = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                if (restoreDefaults)
                    foreach (var entry in settings) entry.BoxedValue = entry.DefaultValue;

                if (preset == Preset.Vanilla)
                {
                    Set(Vanilla, "Additional Ambient Highlight", true);
                    Set(Vanilla, "Streets of Tarkov Glass Culling Fix", false);
                    Set(Vanilla, "Skip Neutral Effects Passes", false);
                    Set(Vanilla, "Prism Auto Exposure Resolution (%)", 100);
                    Set(Vanilla, "Sharpen Filter", true);
                    Set(Vanilla, "Vintage Filter", true);
                    Set(Vanilla, "Ultimate Bloom Input Scale (%)", 100);
                    Set(Vanilla, "Distant Shadows Resolution Fix", false);
                    Set(ContactGI, "Enabled", false);
                    Set(XeGTAO, "Enabled", false);
                    Set(SSR, "Enabled", false);
                }
                else if (preset == Preset.VanillaPlus || preset == Preset.HighQualityVisuals)
                {
                    bool highQuality = preset == Preset.HighQualityVisuals;
                    Set(Vanilla, "Additional Ambient Highlight", !highQuality);
                    Set(Vanilla, "Distant Shadows Resolution Fix", true);
                    Set(Vanilla, "Prism Auto Exposure Resolution (%)", 66);
                    Set(Vanilla, "Sharpen Filter", !highQuality);
                    Set(Vanilla, "Skip Neutral Effects Passes", true);
                    Set(Vanilla, "Streets of Tarkov Glass Culling Fix", true);
                    Set(Vanilla, "Ultimate Bloom Input Scale (%)", 50);
                    Set(Vanilla, "Vintage Filter", !highQuality);
                    Set(ContactGI, "Enabled", false);
                    Set(XeGTAO, "Enabled", highQuality);
                    Set(SSR, "Enabled", highQuality);
                }
            }
            finally { config.SaveOnConfigSet = save; }
            config.Save();
        }
        private void Set<T>(string section, string key, T value) =>
            settings.Single(e => e.Definition.Section == section && e.Definition.Key == key).BoxedValue = value;
    }
}
