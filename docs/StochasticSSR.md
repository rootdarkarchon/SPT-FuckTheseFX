# Stochastic GGX SSR

FTFX replaces supported main-camera and ordinary scope-optic screen-space reflections with stochastic GGX tracing and NRD REBLUR_SPECULAR denoising. For retired experiments and restoration records, see [historical implementations](history/SSR-declined-implementations-20261003.md).

FTFX SSR is **off by default**. Keep SSR enabled in Tarkov when using it. Off restores native SSR. The normal Optic SSR enabled toggle defaults On when FTFX SSR is enabled, uses its own R512 trace grid and histories, and excludes night-vision and thermal scopes. Unsupported or disabled optics retain native behavior. NRD initialization or dispatch failure restores native SSR for the affected camera; toggling FTFX SSR off/on retries. Missing runtime files require a restart after installation. The native backend requires D3D11; it has no NVIDIA vendor gate, CUDA or NVAPI dependency. AMD compatibility is intended but has not been tested on AMD hardware here.

## Signal and rendering order

Full-density stochastic GGX Hi-Z tracing at the selected square resolution → current radiance/coverage resolve → actual NRD D3D11/DXBC processing → Custom or NativeGaussian full-resolution reconstruction → environment replacement.

The default trace resolution remains R768 and camera depth stays camera-sized. No reduced ray density is introduced. Ray launches use original receiver normals. NRD receives noisy radiance, original receiver normal/roughness, view Z, motion vectors, measured hit distance and current/previous transforms. Colour and coverage use separate REBLUR_SPECULAR identifiers because NRD has no native SSR coverage channel. This is the existing coverage adapter, not a claim that estimated coverage establishes a new reflection hit. Raw miss/geometry/sky classification remains independent of hit distance. Deliberate probe substitution, receiver cutoff exclusions do not enter NRD as noisy GGX samples. See [the rendering contract](SSR-reflection-contract.md) for mapping and limitations.

**Temporal stabilization** controls NRD accumulation. Off still uses NRD spatial denoising. **History frames**, default 8, caps main/stabilized history; fast history is capped at six. **NRD anti-firefly** defaults off to preserve isolated bright details. Settings, camera cuts, frame/time gaps, projection/target changes, resolution and AA/raster changes reset histories. Raster state is finalized after TAA/DLSS jitter in camera onPreRender. Pending asynchronous captures retain detached resources through resize/disposal.

Both reconstruction options remain: Custom preserves the full-resolution receiver footprint and unsupported-region checks; NativeGaussian applies Tarkov's installed Gaussian filtering/roughness LOD to the NRD signal. The native receiver-colour miss fallback still owns its own native history; this is separate from the removed custom GGX temporal histories.

CloudSix 4.0 sky reflections are automatic when its **Temporal Mode = SkyDome**, **Custom Sky** and **Publish Sky For Reflections** are enabled. FTFX samples the published atmosphere/cloud/sun/moon map for confirmed upward sky rays before NRD. Geometry and unresolved misses retain their existing behavior; the bounded sky blend and water tint remain. Missing/released maps or disabled publication use TOD. Provider/texture changes reset history. The existing TodSkyRadiance diagnostic shows the active sky source, and continuation captures include `cloud-sky.exr` plus provider metadata. No CloudSix assembly dependency or renderer changes are required.

The published map decodes world direction with `u = atan2(X, Z)/(2*pi) + 0.5` and `v = sqrt(Y)`. Both direct sky resolve and the probe bridge use this common sampler. Its orientation is verified against the actual displayed volumetric clouds in capture `20261005-133533-123`, independently of synthetic map fixtures.

While FTFX SSR is actively replacing an eligible native camera renderer, the optional CloudSix bridge also fills the native AmbientLight sky-only cubemap's upper hemisphere from that map. CloudSix hides the TOD atmosphere mesh, while its replacement main/optic camera draws do not reach the native cubemap camera; without this bridge, native probe reflections and FTFX's receiver-colour fallback become dark before SSR resolve. The bridge keeps native update timing and face selection, preserves the lower hemisphere, regenerates roughness mips, and refreshes all faces on provider toggles or probe recreation. It adds no camera or cloud raymarch and does not reclassify unknown SSR misses. Activity requires a successful current/previous-frame replacement and a still-enabled, supported camera and native SSR effect. Disabling or falling back stops cloud drawing and mip generation; one native six-face refresh clears old cloud faces. New captures include `environment-probe.exr` and unweighted `receiver-radiance.exr` alongside the existing weighted fallback view. D3D11 fixtures verify sampling, scheduling and toggles; live raid acceptance remains separate.

## Composition and continuation

Measured reflection radiance receives the installed native TOD distance/height scattering approximation before NRD. Geometry uses native camera-to-hit distance; confirmed TOD/CloudSix sky uses the measured far-plane endpoint. Fog retains native stencil suppression, minimum density and bounded 50% blend. This is native compatibility, not a new volumetric reflection integration. The existing downstream scene fog still runs after SSR. Unknown misses and receiver-colour fallback remain unchanged, and the shared environment probe stays free of viewer-specific fog. Continuation captures record `fog-parameters.exr` and `stencil-shadow.exr` for inspection.

Environment reflection is still subtracted using the native material/probe equations before SSR replacement. Removing exact CustomAmbient capture does not make reflections additive. Material response, stencil visibility, TOD blending, receiver-colour fallback, water ownership and water tint remain. The main-camera pre-ambient surface-colour capture at u5 remains for water tint; optics use a direct GBuffer0 RGB capture at the native ambient draw boundary when tint is requested. This extra optic capture does not enable optic AO or GI. The full-screen reflection-contribution capture at u4 is gone.

Unresolved coverage uses native-style receiver-colour fallback, default strength 65%. When CloudSix Custom Sky is enabled, the separate CloudSix fallback strength slider overrides this at runtime, defaulting to 100%. This selection follows Custom Sky even when reflection publication or SkyDome mode is unavailable; disabling Custom Sky restores the normal slider's saved value. Rejected foreground colour is diagnostic data only and is not stretched into missing reflections. Hi-Z continuation remains bounded by iterations, skipped samples, camera-pixel distance and eye-depth separation. A completed in-screen continuation with a clear far-plane endpoint can recover TOD sky on all eligible receivers. With endpoint fallback disabled, offscreen exits, exhausted budgets, self hits and unconfirmed misses do not become sky. The depth-separation allowance is ray-to-foreground eye depth, not hit thickness or a proof that hidden geometry is absent.

**Endpoint-validated sky fallback** is enabled by default. After foreground continuation reaches an allowance or iteration limit, an upward world-space reflected ray can use sky when its far-plane endpoint remains inside the screen and that endpoint's depth is background. Geometry hits retain priority. Continuation samples must be above zero; self hits, side exits, downward rays and blocked endpoints retain their existing fallback. This inference does not establish that the unfinished interval is clear and can leak sky through hidden objects. It reuses the existing sky resolve, NRD inputs and finalization dispatch without extending traversal or allocating new resources. Toggling it resets history. Keep the camera fixed and enable Freeze ray sequence for comparisons, then check movement separately.

ContinuationSource shows endpoint sky inference in orange and completed-continuation sky in cyan. Schema-8 captures record inference with flag 128, `endpoint_sky_fallback` in recovered histograms and `sky_endpoint_fallback` in traversal summaries. Flag 8 retains the original allowance failure; `unrecovered_limit_exhausted` counts only failures that remain unresolved.

Receivers below Minimum receiver smoothness use the environment-removed scene blended toward incoming lighting by Cutoff environment blend. They add neither GGX nor receiver-colour fallback.

Zero-smoothness probe lighting remains enabled: smoothness exactly zero with nonzero specular skips GGX and uses roughness-filtered material-weighted probe lighting, deliberately bypassing GBuffer0 alpha suppression. Existing environment reflection is removed once. The close contribution is 10% through 5 m, transitioning by smoothstep to 30% at 15 m. Both slider values remain configurable; the contribution is not solely slider weighted.

## Fresh/reset defaults

Settings use the new **4. Stochastic GGX SSR** header. Percentages are stored as integers with names ending in **(%)**. Only current configuration keys are bound; no legacy configuration migration or retired-key cleanup runs.

| Setting | Default |
|---|---:|
| Enabled | Off |
| SSR resolution | R768 |
| Optic SSR enabled / resolution | On / R512 |
| Reduce main SSR in optics | On |
| Maximum iterations / continuation samples | 350 / 350 |
| Continuation pixels / depth separation | 500 pixels / 200 m |
| Endpoint-validated sky fallback | On |
| Thickness / thickness hit confidence floor | 1 m / 100% |
| Fallback strength | 65% |
| CloudSix fallback strength | 100% |
| Minimum receiver smoothness / cutoff environment blend | 40% / 25% |
| Reconstruction backend | Custom |
| Temporal stabilization / History frames | On / 8 |
| NRD anti-firefly | Off |
| Zero-smoothness probe lighting | On |
| Probe close / far contribution | 10% / 30% |
| Probe fade start / end | 5 m / 15 m |
| Water reflection surface colour influence | 20% |
| Debug view / Freeze ray sequence | Normal / Off |
| Capture continuation diagnostics / traversal work | Off / Off |
| Screen-edge confidence fade (fixed) | 0 pixels |

There are 30 SSR controls. Hit-normal rejection is permanently bypassed and bounded continued-sky recovery applies to every eligible receiver. Receiver-range settings and checks are removed. History frames accepts 1–32; continuation samples/pixels/depth separation accept 0–1000 / 0–2000 / 0–500. Probe fade start/end accept 0–50 / 0–150 metres. All other defaults and enum IDs remain as listed.

While an eligible optic runs GGX SSR, main SSR uses the next lower supported trace resolution: R768 becomes R512, and R256 remains R256. Native optics do not reduce main SSR. The advanced Reduce main SSR in optics toggle defaults On and normal preset clicks preserve its value. This runtime choice does not change the saved main resolution. Optic SSR resolution is separately selectable as R256, R512 or R768, capped at the native optic input size, and defaults to R512. HQ Visuals disables optic SSR; HQ+ Visuals enables it. All normal preset clicks preserve optic resolution. Vanilla and Vanilla+ also preserve optic enablement. Each rendered optic frame uses the same GGX, resolve, receiver fallback and NRD contracts as the main camera. Camera depth, GBuffer and scene input remain full size; trace-only resizing retains camera-sized resources. Resolution transitions reset the affected history. Reduced trace work is not a measured total GPU saving; full-size depth preparation and composition still run.

First-rejection diagnostics are optional. Ordinary tracing does not allocate or write the first-rejection target. Captures and RejectionLocation still enable it, with asynchronous lifetime preservation.

## Validation and packages

`scripts/Test-StochasticSSR.ps1` runs the pinned D3D11 NRD backend, executes the live hooks, compares the renderer against the archived NRD-enabled implementation, checks configuration and lifecycle contracts, and rebuilds `stochasticssr` and `nrdssr`. See [Build and validation](BuildValidation.md) for native-source setup, Python dependencies and the required private captures. `scripts/Test-NrdSSR.ps1` is a convenience entry point for the same suite.

The configuration-layout regression passed 1,548 checks: bit-exact raw tracing, resolve, NRD output and final composition for both reconstruction modes and temporal on/off; synthetic animated normals; three captured slab/water/crane scenes; reset, resize, delayed readback, lost resource, late raster and native fallback/re-enable behavior. Rejection diagnostics and traversal-work capture preserve exact tracing; optional allocation, capture rearm and resize/readback disposal are checked. The comparison disables normal stabilization and exact captured subtraction in the archived control. Their enabled output is deliberately not preserved. AO/contact GI and pre-ambient water-colour capture validation also passed after removing u4.

No GPU timings were collected during this cleanup. Historical NRD timings are not measurements of this change. Live Tarkov visual acceptance and timing remain separate from fixture parity.

Optic regression coverage exercises two camera hooks with independent native NRD instances, alternating history, effective main-resolution transitions, trace-only resource retention, camera-local failure fallback and exact optic material-colour capture. These controlled inputs do not establish live scope image quality, engine motion-vector ownership or GPU savings. In-game checks must cover ordinary versus night-vision/thermal scopes, rapid ADS, scope swaps, variable zoom, TAA/DLSS, water and the cost of ADS entry/exit as well as sustained scoped rendering.

`scripts/Build-Package.ps1` always includes the NRD native DLL, input shader bundle and required licenses. Runtime packages contain no manuals, test sources or historical archives.
