# SSR 0.9.0 performance work

## Live evidence motivating this build

Captures under `D:/Tarkov-SPT-4.1/BepInEx/CustomAmbientResearch`:

| Capture | Recorded mode | Mean enclosing SSR GPU interval |
| --- | --- | ---: |
| `20260927-111046-656-scene-gpu-customs-1200-ssr-native` | Native | 0.306221 ms |
| `20260927-105453-395-scene-gpu-customs-1200-ssr-stochasticnative` | StochasticNative, Native reconstruction, direct | 0.510140 ms |
| `20260927-105604-957-scene-gpu-customs-1200-ssr-stochasticnative-raydispatch` | StochasticNative, Native reconstruction, compact | 0.551480 ms |
| `20260927-111156-773-scene-gpu-customs-1200-ssr-stochasticnative` | StochasticNative, Custom reconstruction, direct | 0.733625 ms |

The first capture named `20260927-105334-514-...-ssr-native` actually ran StochasticNative/Native/direct at 0.514187 ms; it is not a vanilla baseline. Native and custom corrected captures have 183/174 valid detailed samples, no invalid layouts/ranges or ambiguous intersections inside SSR, stationary focused cameras, and a 0.188-degree pose difference. DOF settings vary between captures, so whole-camera deltas are not isolated SSR measurements. Nested denoiser/trace/mip intervals are not added to the enclosing interval.

Custom reconstruction alone measured 0.207603 ms in 0.8.0 versus 0.241245 ms in the earlier 0.7.0 capture. Complete custom SSR measured 0.733625 versus 0.825033 ms. The old pose differs by 1.94 m and 1.124 degrees: these are observed improvements, not controlled version A/B results.

## Changes

- Four ARGBHalf history copies are replaced by deferred buffer swaps. Reflection, receiver guide, hit point and geometry buffers become history just before the next trace. Current-frame diagnostic/composition bindings stay intact. Both sides support UAV writes; resource loss, mode changes and resize preserve the previous reset order.
- Reprojection produces two filter-needed bits per 8x8 tile. The prefilter can skip neighbourhood loads when every valid receiver takes upstream's copy-only branch (non-glossy, mirror or zero half-packed variance). Temporal resolve can skip neighbourhood loads for entirely non-glossy tiles. Mixed tiles run the original AMD function. Half packing and padded-lane group synchronization are preserved.
- Classification shares the existing reprojection dispatch and reduction barriers. It does not create another classifier dispatch or indirect list. The additional RHalf tile map is 32 KiB at 1024-square; RFloat fallback is 64 KiB. Four removed copies represent 32 MiB of data / 64 MiB of nominal read-write traffic per frame at that grid, not a claim about physical DRAM traffic.
- `Stochastic > Efficient denoising` enables these changes by default and supports live off/on profiling. Research metadata records the switch. Native reconstruction is unaffected. Compact ray dispatch remains disabled by default.

No rays are removed and no filtering weights or native material rules change. Reprojection still processes the complete reflection signal, including misses; skipping black-confidence pixels would violate the native composition contract. Adaptive rays and full AMD indirect denoiser tile lists remain unimplemented.

## Validation

Unity 2022.3.43f1 / D3D11. Exact reflection RGBA and hit-record parity against the SHA256-pinned 0.8.0 package on initial and accumulated-history frames for both general and mirror fixtures. Optimized/reference scheduling also matches temporal RGB, native alpha and history weight across 48 changing-input frames, native cutoff zero and standalone cutoff 0.8, mirror/active/rejected/mixed tiles, odd extents, live filter/optimization toggles, explicit history reset, texture loss and pending-swap resize. The existing animated-water, camera-motion, native/optic isolation, ten-phase capture and native composition suite passes.

The tests caught and corrected a resource-loss ordering issue: resetting the sampling sequence before tracing differed from the previous path's post-trace history rebuild. Both paths now retain the same ordering.

Final local GPU timestamp benchmarks (medians of six alternating batches after two warmup rounds; 16 renders per batch, camera 1280x720, trace 1024-square):

| Trace + custom reconstruction fixture | 0.8.0 | 0.9.0 | Reduction |
| --- | ---: | ---: | ---: |
| General material fixture | 0.3839 ms | 0.3726 ms | 2.9% |
| Mirror material fixture | 0.5848 ms | 0.5799 ms | 0.8% |

These exclude native hit resolve/material passes and final composition. Earlier general-fixture runs showed 6.8% and 10.9% lower time; the variation prevents claiming a stable percentage. The full-game 0.9.0 outcome remains unverified. No shader compile, missing-binding or texture-copy errors were found in the final validation log. Release packaging separately verifies source and ZIP hashes.

## One-launch comparison

Use StochasticNative / Custom / Final with accepted quality settings, compact dispatch off, range override off, comparison capture off and NativeMarkersOnly profiling. At one stationary pose, capture Efficient denoising off, then on after a few seconds of warmup. No new vanilla capture is required to isolate this change. Briefly inspect moving water, weapon edges and panning for regressions. Both controls work live after the initial installation restart.
