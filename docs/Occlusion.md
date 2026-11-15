# Occlusion

For live foliage comparisons, temporary lighting bypasses and the optional single-frame capture, see [Foliage lighting tests](FoliageLightingTest.md).

**2. XeGTAO / Reflection occlusion fix** (Advanced, default on) reduces screen-space reflection occlusion by up to 50% on smooth surfaces, tapering with smoothness squared to no reduction on fully rough surfaces. It works at every viewing angle, preserves diffuse AO and excludes the native motion-depth vegetation path. Requires the Custom Ambient Fix. See [reflection occlusion testing](ReflectionOcclusionTest.md) for live comparison and integration details.

FuckTheseFX owns XeGTAO without an AOReplacer assembly, Amplify class, shader or render target. The Intel estimator and reviewed EFT 40743 CustomAmbient shader retain their provenance. Camera routing, allocation, deferred composition, fallback and configuration are implemented independently here.

The normal AO controls are **2. XeGTAO / Enabled**, **2. XeGTAO / Quality** (0-3, default 3), **2. XeGTAO / Optic AO Enabled** (default off, see [Optics](#optics)) and **2. XeGTAO / Second Pass Enabled**. XeGTAO tuning, CustomAmbient, actor tuning and debug settings remain available under Advanced. Values come from FuckTheseFX's configuration and defaults; no AOReplacer config is read. The FTFX Quality slider (0-3, default 3) selects the 1/2/3/9-slice presets for both AO passes unless primary sampling counts are overridden.

The occlusion integration initializes independently of other rendering plugins. FTFX settings and the native camera/rendering requirements determine when its effects run.

## Rendering

- EFT's SetSSAO runs normally. Per-camera native enabled states are captured, then native AO is suspended only after the replacement resources and commands are ready. Disabling XeGTAO, graphics AO Off, unsupported cameras, allocation failure and disposal restore native ownership.
- Camera-owned inputs capture the original G-buffer material alpha and actor stencil after the G-buffer. Material alpha is bound explicitly at this event. Deferred primary composition changes only albedo alpha and ambient/emission RGB; LDR composition respects logarithmic encoding and uses a 10-bit emission copy.
- Active CustomAmbient also preserves diffuse material/screen AO before later decals can overwrite the shared G-buffer alpha. This camera-owned R8 input is used with the reflection fix both on and off, so the reflection toggle does not control diffuse AO protection.
- EFT records every camera's CustomAmbient draw with one shared native material. Each eligible camera draws with its own FTFX material variant, and the native material is never modified, so re-recording another camera (for example an optic render-target resize) cannot switch the main camera back to native ambient. Ineligible cameras keep the native material.
- The primary result retains the selected intensity/power and uses the Advanced Distance fade start/end settings (defaults 100/250 metres of view depth). The same camera-owned curve drives deferred composition, CombinedAO, CustomAmbient foliage and bent normals. End values below start + 1 use a one-metre fade without rewriting saved settings. Raw AO views and the actor second-pass cutoff are independent. CustomAmbient uses it for authored zero-alpha foliage while preserving native reflection math. The native shader bundle hash is checked before patching ambient materials; unknown shaders retain native ambient.
- Globals are reset before each camera and after rendering. Camera resources follow render-size changes and are removed on disable/destruction. Diagnostics use the current camera's outputs and leave configuration UI above the overlay.

## Optics

**2. XeGTAO / Optic AO Enabled** (default off; HQ Visuals and HQ+ Visuals enable it, Vanilla and Vanilla+ preserve it) runs the primary XeGTAO pass on EFT's single optic camera while an ordinary scope is active, for vanilla optics and FuckTheseOptics Optic Lock. It requires XeGTAO Enabled and game AO not Off. Nightvision and thermal optics are excluded and keep native rendering. The optic camera has no native AO component, so none is touched. In-game feedback (2026-11-09, 2560x1440 Optic Lock scope): visually correct under lens tilt, roughly 0.1 ms extra.

- Primary only: the actor second pass, Contact GI, foliage extra AO/live/capture and all debug views remain main-camera only.
- The optic camera records its own CustomAmbient variant, reflection occlusion, diffuse occlusion and bent normals through the same per-camera mechanism as the main camera; the main camera's inputs and globals are unaffected.
- The projection is read at `Camera.onPreRender`, after every component `OnPreCull`, so the current frame's TAA/DLSS jitter and FuckTheseOptics Optic Lock's rolled or oblique projection are honoured. XeGTAO reconstructs view positions with the full inverse of the projection's x/y rows, not only its diagonal.
- Resources follow the optic render target. FTO resizes it in steps, so the AO targets are recreated at each step; GPU cost scales with the optic render size.
- Turning the toggle off, XeGTAO off, game AO Off, or switching an active scope to nightvision/thermal disposes the optic effect and restores native ambient for that camera. Leaving the scope keeps the effect allocated on the inactive optic camera so the next aim does not reallocate it. A failure logs once and disables optic AO until XeGTAO or the toggle is switched off/on.

## Ray-traced hidden-occluder AO (FuckTheseRays)

XeGTAO keeps everything screen space can see (detail, grass, contact). With **FuckTheseRays** (FTR) running RTGI, the main camera can additionally darken surfaces occluded by geometry screen space cannot see (car undersides, overhangs, off-screen or behind-the-depth-layer occluders) at any distance within **2. XeGTAO / RT AO Radius**. FTR derives it from its existing RTGI cosine donor rays; FTFX only consumes it. Without FTR, with RTGI off, with **RT AO Enabled** off or **RT AO Strength (%)** at 0, FTFX output is bit-identical to 1.10.0: the composite shader skips the term entirely.

- **RT AO Enabled** (default on) multiplies the XeGTAO visibility by FTR's hidden-occluder visibility (single-channel texture, `R` = V_hid). **RT AO Strength (%)** blends it in (default 100); **RT AO Radius** (default 3 m, 0.5-10) is the ray-traced occlusion distance.
- The term is applied inside the composite shader's `Primary()` after XeGTAO's power/intensity curve, with the same distance fade as XeGTAO (`k * (1 - fade)`, so it vanishes where AO is faded out). Every pass that calls `Primary()` receives it: deferred HDR/LDR composite, the preserved diffuse-occlusion pass feeding CustomAmbient, the CombinedAO debug view and foliage extra AO. SSCGI/ColorBleed, the bent normals and the actor second pass keep reading the raw resolved visibility and are unchanged. The texture is sampled bilinearly at the composite UV, so its size may differ from the camera's.
- Main camera only; optic cameras never request or use it. Game AO Off keeps disabling everything, including the request.
- Interop is CPU-side shader globals (contract version 1), see `AGENTS.md` "Persisted contracts". FTFX writes `_FTFX_RtaoRequest` every main-camera pre-cull while XeGTAO is active for it, RT AO Enabled is on and strength is above 0 (zero otherwise, never from command buffers). During `Record` it reads `_FTR_RtaoState` and `_FTR_HiddenVisibility` and accepts only a valid, versioned state at most one frame old with a live texture; otherwise it binds a white texture and strength 0.
- Validation: `scripts/Test-Occlusion.ps1` checks the term against the CPU formula, zero-strength bit identity, distance-fade removal and state acceptance. In-raid feedback (2026-11-11, FTFX 1.11.0 with FTR 0.33.0): the product combination works well. A Min combination (darker of XeGTAO and the full ray-traced visibility) was tested, was too dark and flashed on quick turns, and was removed.

## Actor optimization

Depth prefiltering remains full resolution and includes the entire scene. Actor radius changes depth-mip weighting, so the primary pyramid is not reused. An 8 x 8 tile classifier finds actors inside the distance cutoff; a full neighboring-tile halo supplies every AO/edge sample required by the single denoiser pass. Horizon sampling skips other tiles. The final denoiser skips inactive core tiles and writes neutral output, preventing stale shading as actors move. No GPU-to-CPU readback is used at runtime.

The actor composite reads filtered integer AO and linear depth directly. It preserves the former RGHalf rounding and strength/response/distance/gamma equations, removing the intermediate full-screen RGHalf actor resolve. Quality, radius and normal input are unchanged. GPU markers distinguish actor depth, tiles, horizons, denoise and composite. This is expected to help when actors cover a small part of the screen; dense actor coverage can reduce the benefit. Live EFT timing remains the performance acceptance check.

## Validation

- `scripts/Test-Occlusion.ps1`: D3D11 bundle compilation (this script rebuilds `Assets/occlusion` from `unity/Assets`), real Harmony routing against an API fixture, native fallback, HDR/LDR composition, full-frame versus tiled actor equivalence, old versus fused actor composite, stencil classes, original-alpha capture, camera resets/resizing, all nine debug views and disposal.
- `scripts/Test-CustomAmbient.ps1`: native-parity and bent-normal shader checks against the installed native ambient shader, including diffuse/fill, reflections, fog/night vision, authored zero-alpha foliage and both HDR encodings.
- `scripts/Build-Package.ps1`: Release compilation, configuration entries, unchanged other-FX contracts, installed EFT AO/ambient/stencil ABI, AO initialization without installed-plugin bypasses, no Amplify dependency, exact ZIP allowlist and entry hashes.

The actor GPU fixtures compare more than 83,000 actor pixels at 256 x 144 and 259 x 147, including sparse, full, edge, moving, empty and distance-rejected masks. Sampled actor output is bit-identical to the full-frame estimator; the fused composite matches the former resolve/composite within 0.000001. These tests establish the tested shader contracts, not live EFT visual or FPS acceptance.

Bent normals are optional (Advanced: 2. XeGTAO / Custom Ambient Fix Bent normals, off by default).
They steer only directional SH diffuse ambient; native upward blending, reflection, fill,
scalar AO and the actor estimator keep their existing roles. Strength, foliage strength
and the shared configurable AO distance fade also scale this directional correction. Requires the CustomAmbient
fix and XeGTAO.
The BentNormals debug view displays world-space RGB directions, or gray when inactive.
The primary estimator and denoiser use Intel's packed direction/visibility variant in
the existing R32 textures. No extra full-screen target or resolve is added. The actor
pass remains scalar. Screen-space direction estimates require in-game visual and timing
comparison, especially foliage and movement with DLSS.

The BentSHDifference and BentLightingDifference views capture an amplified absolute RGB difference from the actual native CustomAmbient draw. The former includes the configured strength, foliage and distance weighting before the interior blend; the latter also includes that blend, albedo and scalar AO. Debug gain defaults to 32. Black means no change; cyan means the ambient draw did not capture; red means the bent-normal keyword is disabled; magenta means current-camera inputs are unavailable. The diagnostic UAV is bound only around the ambient draw, and normal lighting output is preserved. Disable debug views before benchmarking. The research plugin's draw wrapper remains intact.

AmbientShares separates diffuse/fill luminance: red is directional SH, green upward SH, blue fill. AmbientDirectional, AmbientUpward and AmbientFill show their final contributions with a shared Ambient contribution gain (default 1). AmbientInteriorTransmission shows how much directional SH survives the interior blend and visibility, before albedo and AO (black 0%, white 100%). These views work with bent normals on or off and exclude reflections/direct lights. Shares/transmission ignore gain. Use Normal before measuring performance.
