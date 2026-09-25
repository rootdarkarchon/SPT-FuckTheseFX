# Occlusion

FuckTheseFX owns XeGTAO without an AOReplacer assembly, Amplify class, shader or render target. The Intel estimator and reviewed EFT 40743 CustomAmbient shader retain their provenance. Camera routing, allocation, deferred composition, fallback and configuration are implemented independently here.

The normal controls are **XeGTAO / Enabled** and **Actor second AO pass / Enabled**. The previous eleven XeGTAO tuning options, three CustomAmbient settings, four actor tuning options, and three debug settings remain available under Advanced. Values come from FuckTheseFX's configuration and defaults; no AOReplacer config is read. Graphics AO quality selects the 1/2/3/9-slice presets unless primary sampling counts are overridden.

AOReplacer is a soft dependency for load ordering only. When its plugin ID is present, no FuckTheseFX occlusion integration, shader bundle, camera hook, CustomAmbient patch or diagnostic overlay is initialized. Bloom, distant shadows and ambient highlight continue independently. The saved occlusion preferences remain available for a later launch without AOReplacer.

## Rendering

- EFT's SetSSAO runs normally. Per-camera native enabled states are captured, then native AO is suspended only after the replacement resources and commands are ready. Disabling XeGTAO, graphics AO Off, unsupported cameras, allocation failure and disposal restore native ownership.
- Camera-owned inputs capture the original G-buffer material alpha and actor stencil after the G-buffer. Material alpha is bound explicitly at this event. Deferred primary composition changes only albedo alpha and ambient/emission RGB; LDR composition respects logarithmic encoding and uses a 10-bit emission copy.
- The primary result retains the selected intensity/power and 35–100 m fade. CustomAmbient uses it for authored zero-alpha foliage while preserving native reflection math. The native shader bundle hash is checked before patching ambient materials; unknown shaders retain native ambient.
- Globals are reset before each camera and after rendering. Camera resources follow render-size changes and are removed on disable/destruction. Diagnostics use the current camera's outputs and leave configuration UI above the overlay.

## Actor optimization

Depth prefiltering remains full resolution and includes the entire scene. Actor radius changes depth-mip weighting, so the primary pyramid is not reused. An 8×8 tile classifier finds actors inside the distance cutoff; a full neighboring-tile halo supplies every AO/edge sample required by the single denoiser pass. Horizon sampling skips other tiles. The final denoiser skips inactive core tiles and writes neutral output, preventing stale shading as actors move. No GPU-to-CPU readback is used at runtime.

The actor composite reads filtered integer AO and linear depth directly. It preserves the former RGHalf rounding and strength/response/distance/gamma equations, removing the intermediate full-screen RGHalf actor resolve. Quality, radius and normal input are unchanged. GPU markers distinguish actor depth, tiles, horizons, denoise and composite. This is expected to help when actors cover a small part of the screen; dense actor coverage can reduce the benefit. Live EFT timing remains the performance acceptance check.

## Validation

- `scripts/Test-Occlusion.ps1`: D3D11 bundle compilation, real Harmony routing against an API fixture, native fallback, HDR/LDR composition, full-frame versus tiled actor equivalence, old versus fused actor composite, stencil classes, original-alpha capture, camera resets/resizing, all six debug views and disposal.
- `scripts/Test-CustomAmbient.ps1`: 276 shader checks against the installed native ambient shader, including diffuse/fill, reflections, fog/night vision, authored zero-alpha foliage and both HDR encodings.
- `scripts/Build-Package.ps1`: Release compilation, 33 configuration entries, unchanged other-FX contracts, installed EFT AO/ambient/stencil ABI, compiled AOReplacer bypass, no Amplify dependency, exact ZIP allowlist and entry hashes.

The actor GPU fixtures compare more than 83,000 actor pixels at 256×144 and 259×147, including sparse, full, edge, moving, empty and distance-rejected masks. Sampled actor output is bit-identical to the full-frame estimator; the fused composite matches the former resolve/composite within 0.000001. These tests establish the tested shader contracts, not live EFT visual or FPS acceptance.
