# SSR 0.2.2: Customs GPU comparison, 2026-09-26

Inputs:

- Native: `D:\Tarkov-SPT-4.1\BepInEx\CustomAmbientResearch\20260926-204655-660-scene-gpu-customs-1200-ssr-native-nativemarker`
- HiZ: `D:\Tarkov-SPT-4.1\BepInEx\CustomAmbientResearch\20260926-204810-543-scene-gpu-customs-1200-ssr-hiz-nativemarker`

The user confirmed expected reflection behavior with 0.2.2 in this scene. A stronger weapon response remains a quality difference. This is a scene-specific correctness baseline, not general visual acceptance.

## Results

| GPU interval, milliseconds | Native | HiZ 0.2.2 |
|---|---:|---:|
| SSR mean | 0.333852 | 0.486174 |
| SSR median | 0.331776 | 0.486400 |
| SSR p95 (nearest rank) | 0.339968 | 0.494592 |
| Enclosing opaque post-processing mean | 0.369447 | 0.520633 |
| Coarse camera mean | 6.386984 | 6.530318 |
| Detailed camera mean | 6.425205 | 6.592478 |

SSR adds **0.152322 ms / 45.63%** in this pair. The opaque parent adds 0.151186 ms, independently supporting the localized increase. The 0.4 ms initial replacement budget is exceeded by 0.086174 ms, before temporal/roughness quality work.

Native is the inclusive Screen-space Reflections range, already containing its 0.0448 ms SSRComputeMips child. HiZ is each sampled frame's interval from HiZ build begin to Native material composition end. It includes intervening gaps; it does not add overlapping parents.

## Validity and limits

- Detailed samples: 184 native, 181 HiZ; every HiZ frame has all four stages. No invalid GPU rows, invalid layouts/ranges or ambiguous ledger slices intersect either SSR interval. Global ambiguities elsewhere in the frame are 214/204.
- Both use 0.2.2, Final, Automatic, 2560x1440 ARGBHalf, divisor 1, 128 iterations, 100 m, thickness 0.2, minimum smoothness 0.8. Profiling is NativeMarkersOnly. SSR settings do not change within a capture.
- Cameras are stationary within each window, focused, with no mixed detailed frames. The two poses differ slightly (submillimetre translation and a small orientation difference).
- One non-SSR field differs: Prism's `useDof` is true in all native windows and the HiZ control/coarse windows, but false in the HiZ detailed window. This prevents treating detailed whole-camera differences as a fully matched SSR-only experiment. SSR and its opaque parent remain useful local measurements.
- HiZ build averages 0.032191 ms; trace averages 0.000017 ms (178/181 zero); guided resolve is zero in all 181; the following composition-labelled interval averages 0.453943 ms. The compute subdivisions do **not** provide reliable standalone cost attribution. Do not conclude that tracing/resolve are free or that the pixel composite alone costs 0.454 ms.
- Older 0.2.0 captures used different scene/trace conditions; they are not a controlled performance comparison with 0.2.2.

## Next step

Proceed from the accepted projection/composition behavior, with performance work alongside stage 3: reliable compute attribution, half-resolution tracing/reconstruction, roughness-aware filtering and temporal stabilization in one test package. Retain full resolution as the reference. The current full-resolution prototype has no demonstrated speed advantage over native.
