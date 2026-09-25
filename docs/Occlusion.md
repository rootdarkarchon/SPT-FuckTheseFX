# Occlusion

FuckTheseFX owns XeGTAO without an AOReplacer assembly, Amplify class, shader or render target. The Intel estimator and reviewed EFT 40743 CustomAmbient shader retain their provenance. Camera routing, allocation, deferred composition, fallback and configuration are implemented independently here.

The normal AO controls are **XeGTAO / Enabled**, **XeGTAO / Quality** (0-3, default 3), and **Actor second AO pass / Enabled**. The previous eleven XeGTAO tuning options, four CustomAmbient settings, four actor tuning options, and three debug settings remain available under Advanced. Values come from FuckTheseFX's configuration and defaults; no AOReplacer config is read. The FTFX Quality slider (0-3, default 3) selects the 1/2/3/9-slice presets for both AO passes unless primary sampling counts are overridden.

AOReplacer is a soft dependency for load ordering only. When its plugin ID is present, no FuckTheseFX occlusion integration, shader bundle, camera hook, CustomAmbient patch or diagnostic overlay is initialized. Bloom, distant shadows and ambient highlight continue independently. The saved occlusion preferences remain available for a later launch without AOReplacer.

## Rendering

- EFT's SetSSAO runs normally. Per-camera native enabled states are captured, then native AO is suspended only after the replacement resources and commands are ready. Disabling XeGTAO, graphics AO Off, unsupported cameras, allocation failure and disposal restore native ownership.
- Camera-owned inputs capture the original G-buffer material alpha and actor stencil after the G-buffer. Material alpha is bound explicitly at this event. Deferred primary composition changes only albedo alpha and ambient/emission RGB; LDR composition respects logarithmic encoding and uses a 10-bit emission copy.
- The primary result retains the selected intensity/power and 35Ã¢â‚¬â€œ100 m fade. CustomAmbient uses it for authored zero-alpha foliage while preserving native reflection math. The native shader bundle hash is checked before patching ambient materials; unknown shaders retain native ambient.
- Globals are reset before each camera and after rendering. Camera resources follow render-size changes and are removed on disable/destruction. Diagnostics use the current camera's outputs and leave configuration UI above the overlay.

## Actor optimization

Depth prefiltering remains full resolution and includes the entire scene. Actor radius changes depth-mip weighting, so the primary pyramid is not reused. An 8Ãƒâ€”8 tile classifier finds actors inside the distance cutoff; a full neighboring-tile halo supplies every AO/edge sample required by the single denoiser pass. Horizon sampling skips other tiles. The final denoiser skips inactive core tiles and writes neutral output, preventing stale shading as actors move. No GPU-to-CPU readback is used at runtime.

The actor composite reads filtered integer AO and linear depth directly. It preserves the former RGHalf rounding and strength/response/distance/gamma equations, removing the intermediate full-screen RGHalf actor resolve. Quality, radius and normal input are unchanged. GPU markers distinguish actor depth, tiles, horizons, denoise and composite. This is expected to help when actors cover a small part of the screen; dense actor coverage can reduce the benefit. Live EFT timing remains the performance acceptance check.

## Validation

- `scripts/Test-Occlusion.ps1`: D3D11 bundle compilation, real Harmony routing against an API fixture, native fallback, HDR/LDR composition, full-frame versus tiled actor equivalence, old versus fused actor composite, stencil classes, original-alpha capture, camera resets/resizing, all nine debug views and disposal.
- `scripts/Test-CustomAmbient.ps1`: native-parity and bent-normal shader checks against the installed native ambient shader, including diffuse/fill, reflections, fog/night vision, authored zero-alpha foliage and both HDR encodings.
- `scripts/Build-Package.ps1`: Release compilation, 36 configuration entries, unchanged other-FX contracts, installed EFT AO/ambient/stencil ABI, compiled AOReplacer bypass, no Amplify dependency, exact ZIP allowlist and entry hashes.

The actor GPU fixtures compare more than 83,000 actor pixels at 256Ãƒâ€”144 and 259Ãƒâ€”147, including sparse, full, edge, moving, empty and distance-rejected masks. Sampled actor output is bit-identical to the full-frame estimator; the fused composite matches the former resolve/composite within 0.000001. These tests establish the tested shader contracts, not live EFT visual or FPS acceptance.

Bent normals are optional (Advanced: CustomAmbient fix / Bent normals, off by default).
They steer only directional SH diffuse ambient; native upward blending, reflection, fill,
scalar AO and the actor estimator keep their existing roles. Strength, foliage strength
and the 35â€“100 m AO fade also scale this directional correction. Requires the CustomAmbient
fix and XeGTAO; AOReplacer presence bypasses this feature with the rest of FTFX AO.
The BentNormals debug view displays world-space RGB directions, or gray when inactive.
The primary estimator and denoiser use Intel's packed direction/visibility variant in
the existing R32 textures. No extra full-screen target or resolve is added. The actor
pass remains scalar. Screen-space direction estimates require in-game visual and timing
comparison, especially foliage and movement with DLSS.

The BentSHDifference and BentLightingDifference views capture an amplified absolute RGB difference from the actual native CustomAmbient draw. The former includes the configured strength, foliage and distance weighting before the interior blend; the latter also includes that blend, albedo and scalar AO. Debug gain defaults to 32. Black means no change; cyan means the ambient draw did not capture; red means the bent-normal keyword is disabled; magenta means current-camera inputs are unavailable. The diagnostic UAV is bound only around the ambient draw, and normal lighting output is preserved. Disable debug views before benchmarking. The research plugin's draw wrapper remains intact.

AmbientShares separates diffuse/fill luminance: red is directional SH, green upward SH, blue fill. AmbientDirectional, AmbientUpward and AmbientFill show their final contributions with a shared Ambient contribution gain (default 1). AmbientInteriorTransmission shows how much directional SH survives the interior blend and visibility, before albedo and AO (black 0%, white 100%). These views work with bent normals on or off and exclude reflections/direct lights. Shares/transmission ignore gain. Use Normal before measuring performance.
