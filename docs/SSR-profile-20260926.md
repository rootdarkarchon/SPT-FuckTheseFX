# Native SSR capture comparison, 2026-09-26

Native SSR costs about 0.314 ms in this stationary Customs view. Raymarch and composition together account for 73.2% of that interval. Proceed with the combined composition-validation and deterministic Hi-Z package; another baseline game restart is unnecessary for choosing the prototype.

## Evidence

Capture directories under `D:\Tarkov-SPT-4.1\BepInEx\CustomAmbientResearch`:

- `20260926-190259-991-scene-gpu-customs-1200-ssr-nativemarkers`
- `20260926-190405-113-scene-gpu-customs-1200-ssr-perpass`

Sources: `gpu-scopes.csv`, `scene-gpu-coverage.csv`, `scene-gpu-ledger.csv`, `scene-deep-summary.csv`, `scene-deep-calls.csv`, `scene-deep-settings.txt`, `performance-frames.csv`, `scene-gpu-summary.txt`, `windows.csv`, and `settings.txt` in each directory. These are actual GPU timestamp intervals, not managed recording times.

Both captures use RTX 4090, D3D11, Unity 2022.3.43f1, 2560x1440 internal rendering and 3840x2160 output. Recorded SSR settings match: High, resolution enum Supersampled, 256 maximum iterations, thickness 6, maximum distance 1000. Actual resolve texture metadata is 1024x1024; use that observed allocation rather than interpreting the enum as a camera-resolution multiplier.

One round per capture: 10-second control, coarse, and detailed windows, with 3-second warmups and one-in-eight query sampling. There are 184 eligible detailed camera samples in NativeMarkersOnly and 183 in PerPass. The camera remains fixed inside each capture; between captures its position differs by approximately 0.001 m and orientation by 0.205 degrees. These are close comparisons, not identical-frame paired measurements.

## Per-pass results

Mean GPU intervals per sampled camera frame in the PerPass detailed window:

| Operation | Mean ms | Share of SSR |
| --- | ---: | ---: |
| Raymarch | 0.129651 | 41.29% |
| Resolve | 0.014560 | 4.64% |
| Temporal | 0.016787 | 5.35% |
| History copy | 0.007711 | 2.46% |
| Complete Gaussian mip chain | 0.043668 | 13.91% |
| Composite | 0.100050 | 31.87% |
| Parent remainder outside those intervals | 0.001550 | 0.49% |
| **Total native SSR** | **0.313976** | **100%** |

SSR median is 0.313344 ms and p95 is 0.320512 ms. Raymarch median/p95 is 0.129024/0.134144 ms; composite is 0.100352/0.102400 ms.

Seven mip dispatch/copy pairs executed every sampled frame. Dispatch scopes sum to 0.002484 ms, copy scopes to 0.041027 ms, and the mip parent remainder is 0.000157 ms. The first copy accounts for 0.020833 ms. Several dispatch scopes consistently quantize to zero. This is a measured command-boundary split, not proof that filtering has no execution cost or that removing copies saves the complete copy interval. Keep 0.043668 ms as the reliable combined mip-chain cost until a prototype measures a different implementation. No separate Hi-Z build exists in this native breakdown.

## Validity and observer comparison

- All raw GPU rows have flags zero. All SSR scopes have complete coverage and zero invalid rows.
- Every PerPass detailed frame contains all 19 expected child ranges: four shader passes, history copy, and seven dispatch/copy pairs. All are inside the SSR parent; positive-duration children do not overlap. History seeding and scene-view resolve are absent, as expected for warm gameplay.
- The global accounting reports 205/209 ambiguous slices in the two captures. None of the 209 PerPass ambiguous slices intersects the SSR interval. Those unrelated accounting warnings do not invalidate this SSR breakdown.
- NativeMarkersOnly total SSR is 0.312198 ms versus PerPass 0.313976 ms: an observed increase of 0.001779 ms, or 0.57%. The enclosing opaque post-processing buffer similarly changes from 0.343853 to 0.345592 ms. This bounds the observed difference here; it is not an isolated estimate of marker overhead.
- Camera GPU coarse/detailed means are 6.443568/6.507259 ms for NativeMarkersOnly and 6.476005/6.547456 ms for PerPass. Detailed-minus-coarse is about 0.064/0.071 ms. Across-capture drift and the single round prevent attributing all differences to instrumentation.
- Detailed wall means are 6.796994/6.828120 ms. PerPass control contains a 142.5 ms wall-time outlier and a 135.7 ms observer-preparation maximum; do not use that control mean to claim a rendering slowdown.

## Next implementation decision

Prioritize the confidence-aware composition contract alongside the first bounded Hi-Z tracer. Composition alone is about one third of SSR, so traversal improvements cannot address the whole cost. Keep live Native / composition validation / deterministic Hi-Z modes, forced confidence zero/one, and native fallback for unsupported cameras. Hold the SSR-enabled CustomAmbient intensity policy constant across modes.

The existing 0.4 ms replacement budget leaves roughly 0.086 ms above this measured native total. It remains a development target, not evidence that adding Hi-Z, stochastic rays and denoising will meet it or run faster. Offline tests should cover composition, history/resource lifetime and mode switching before the next game launch; live motion, material and optic-camera quality remain untested by these stationary captures.
