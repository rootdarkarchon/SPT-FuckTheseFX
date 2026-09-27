# SSR 0.5.0 live comparison: 20260927-015437-714

Source: `D:/Tarkov-SPT-4.1/BepInEx/SSRResearch/20260927-015437-714`.

All six phases completed. Camera projection and rotation remained identical in the recorded metadata; position changed by approximately 0.1 mm. Water animation and weapon movement still differ between phases. Analysis reads linear RGBAHalf EXRs with OpenEXR; measurements and the analysis helper are under `obj/SSRAnalysis-20260927-015437` and `obj/analyze_ssr_capture.py`.

## Findings

1. **NativeReplayDirect matches native RGB exactly in the live frame.** All 2560 x 1440 pixels have zero difference, including weapon and ground. The prototype can reproduce the native final composition when supplied the same resolved reflection and mip chain. The final composition formula is not the outstanding discrepancy in this capture.
2. **NativeReplayFiltered introduces a smaller, separate reconstruction difference.** This same-frame comparison preserves native trace/history but resamples its square resolve to camera resolution and rebuilds the square Gaussian mip chain. Whole-frame mean absolute RGB error is 0.001225; 16.11% of pixels differ by more than 0.001 in at least one channel. In the sampled water patch, mean absolute RGB error is 0.008691 while mean luminance changes only -0.73%. Weapon and nearby-ground errors in the selected patches are approximately 0.000016 and 0.000027. This implicates resampling/mip reconstruction in detail differences, not the large weapon brightness problem; it does not isolate the Gaussian kernel from resampling.
3. **HiZNative restores the broadly native-looking weapon while changing water reflection coverage.** Metadata confirms zero native raymarch calls and one custom trace; native downstream stages remain active. Valid hits occupy 5.60% of the trace grid versus 10.30-10.44% across the three native phases. In the same screen-space water patch, native phases have 29.69-30.56% valid hits versus 17.23% for HiZNative. This is a trace-stage discrepancy that persists without the AMD denoiser. Different animated frames prevent interpreting these numbers as a per-ray causal comparison or an accuracy score.
4. **The standalone path executes without native raymarch.** Both standalone phases report zero native calls and one custom trace. Sampled weapon confidence is zero, but final material composition still substantially darkens its pre-SSR colour. Zero custom confidence therefore does not mean final composition is bypassed. Filtered/unfiltered captures are different frames and cannot establish temporal flicker or exact noise-reduction ratios.

## Settings matter

The native renderer actually ran High / FullSize at a 1024-square trace grid, 48 iterations, maximum distance 1000 and cutoff 0. HiZNative copies these native trace settings while retaining custom intersection/thickness rules. An equal iteration count does not represent equal travel/work for a hierarchical traversal and the native stepping algorithm.

Standalone uses a 2560 x 1440 trace grid, 128 iterations, distance 100 and minimum smoothness 0.8. It therefore excludes receivers and limits distance differently from native. Its settings.txt native-resolution/iteration fields describe settings at Render entry before native preset application; native Render is skipped in this mode, so those fields are not a second executed SSR workload.

The old black weapon confidence view did not establish that native had no weapon hits. The native trace in this capture includes weapon hits, while standalone excludes the sampled weapon patch. Combined with roughness mips and temporal resolve, this is a concrete difference in the reflection input reaching composition.

## Next implementation target

Use HiZNative as the single-tracer correctness baseline: retain native receiver eligibility, resolve, history, mips and composition while bringing custom hit coverage closer to native. Investigate traversal budget, ray starts, rejection rules and hit metadata before changing global reflection strength. Avoid the unnecessary square-to-camera-to-square resampling in the standalone reflection path. Reintroduce stochastic sampling/AMD filtering against that established baseline afterward.

This capture is sufficient to set that direction; no further game launch is needed to reach these conclusions. It does not supply GPU timings, a frame sequence for stability analysis, or depth/normal buffers for offline ray retracing.

## Measurement regions

Rectangles use full-resolution top-left image coordinates, x0,y0,x1,y1: water `(900,880,1400,1040)`, weapon `(1760,900,1870,1040)`, ground `(300,1200,720,1400)`. Hit coverage uses the corresponding normalized rectangle in each trace grid. Linear luminance weights are `(0.2126,0.7152,0.0722)`. Weapon animation prevents precise inter-phase weapon ratios; native replay comparisons are same-frame and unaffected by that limitation.
