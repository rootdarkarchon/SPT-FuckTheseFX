# Intel XeGTAO source

Upstream: https://github.com/GameTechDev/XeGTAO

Pinned commit: `a5b1686c7ea37788eeb3576b5be47f7c03db532c` (also recorded in `REVISION.txt`).

`XeGTAO.h` and `XeGTAO.hlsli` originate from `Source/Rendering/Shaders` at that revision. `LICENSE` is the upstream MIT license and is copied into the plugin output as `Licenses/XeGTAO.txt`.

Unity 2022's shader compiler rejects four upstream integer literal swizzles. Compatibility edits to `XeGTAO.hlsli` replace `2.xx`, `4.xx`, `8.xx` with equivalent `uint2` constructors and `1.xxxx` with `float4(1, 1, 1, 1)`. A conditional `XE_GTAO_DYNAMIC_SAMPLE_COUNTS` loop annotation allows the custom-count kernel to use runtime step counts; preset kernels retain the original unroll annotation. Algorithm code and defaults are otherwise unchanged. `XeGTAO.h` is unchanged.

The sibling `XeGTAO.compute` is the Unity adapter. It supplies camera/depth/normal inputs, live tuning constants, fixed Hilbert/R2 noise, quality/custom-count kernels, bounds checks, and sky handling. It uses FP32 arithmetic/depth for D3D11 compatibility and sets `XE_GTAO_USE_DEFAULT_CONSTANTS=0` so radius, falloff, sample distribution, and thin-occluder compensation use the upstream runtime parameter paths. The adapter calls the upstream depth prefilter, main pass, and denoiser directly. FuckTheseFX adds actor tile classification and a conservative neighboring-tile halo without changing Intel's estimator or depth pyramid. `Composite.shader` resolves primary visibility/depth, applies deferred AO, and composites actor AO directly from the filtered integer output.
