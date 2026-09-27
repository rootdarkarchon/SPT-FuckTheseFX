# SSR 0.10.0 performance work

Both StochasticNative reconstruction backends remain supported. Native reconstruction continues to run native temporal pass 2 without allocating or executing the AMD denoiser. Custom replaces that pass. Neither path runs native raymarch alongside the custom tracer.

## Production-path cleanup

`Streamlined output` defaults on. For Final native-compatible rendering, native pass 3 targets the original destination directly, eliminating the extra full-camera confidence-composite draw. Source/destination aliasing retains the intermediate route. Diagnostics and capture frames retain the intermediate and raw radiance buffers.

Tracing skips unused raw scene-colour loads/stores in that path. Native reconstruction also skips receiver geometry reconstruction and custom confidence/hit-point/geometry guide writes; Custom reconstruction retains its required guides. Actual native hit records, intersections and material passes remain unchanged. Live off/on switching is supported.

## Adaptive rays

`Adaptive rays` defaults off until live quality/performance evaluation. It operates only in StochasticNative / Custom with both filters enabled and a history budget of at least four. The native square grid remains unchanged; the legacy resolution divisor is not repurposed.

A GPU classifier checks each 2x2 receiver quad against reprojected depth, normals, roughness, previous variance/sample count, current camera motion, receiver continuity and previous reflected-hit visibility. Eligible stable quads trace two alternating diagonal samples; other quads trace all four. Each quad refreshes all four every fourth frame, with spatially staggered refreshes. Odd edge quads always retain full sampling. Direct and existing compact scheduling respect the mask. There is no gameplay CPU readback.

Native pass 1 consumes only actual traced hit records. A reconstruction dispatch replaces the native-input copy: omitted receivers reuse the two donors' resolved RGB only when both donors hit. Otherwise they retain their own native miss response. Native alpha remains receiver-local. Hit UVs and traversal metadata are never copied to different receivers. Omitted receivers retain a zero hit point, so their temporal reprojection uses surface history rather than an invented reflected hit distance. Mixed hit/miss history conservatively restores full sampling on subsequent classification.

This is a conservative custom adaptation, not AMD's full SSSR classifier. It adds classification and reconstruction work; ray reduction alone does not guarantee lower total cost. Animated water can remain fully sampled. Ray-count confidence diagnostics show omitted rays as zero even when reconstruction supplies a reflection.

## Validation and remaining live work

Unity D3D11 fixtures cover shader compilation, unchanged full-ray hit/output parity, exact native/custom final RGBA for the streamlined route, diagnostic restoration, periodic sampling, odd extents, history loss, camera cuts, normal/depth/roughness changes, mirrors, receiver-local alpha, hit/miss donor combinations and eleven-phase capture export. Existing native fallback/material, temporal, optic-routing and resource lifecycle checks remain in the suite. See the packaged validation log for the final run.

Real deferred-camera tests additionally compare the classifier's motion threshold with the denoiser's motion input. They caught a stale motion texture when binding the camera's shader-property identifier as a compute render target. Binding `BuiltinRenderTextureType.MotionVectors` instead passed all three moving-camera frames: 985, 990 and 985 above-threshold quads, with zero incorrectly reduced quads. This avoids adding a second motion copy.

The first local timing trial ran while EscapeFromTarkov was using the GPU and produced inconsistent results. Those values are not evidence of a speedup or regression. The runner now skips throughput loops in that situation while retaining previous-release image comparisons. Do not terminate the user's game to obtain a local timing result.

Live evaluation: compare Streamlined output off/on on Native reconstruction, then off/on on Custom reconstruction with Adaptive rays off, then enable Adaptive rays with Streamlined output on. Keep all other accepted settings fixed, use Final/NativeMarkersOnly with capture off, and warm up after each live switch. Alt+F8 separately includes full-ray and adaptive Custom phases and exports an adaptive quad-mask EXR. No second launch is needed to switch any of these options.
