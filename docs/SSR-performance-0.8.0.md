# SSR 0.8.0 performance work

## Live 0.7.0 baseline supplied on 2026-09-27

Captures under `D:/Tarkov-SPT-4.1/BepInEx/CustomAmbientResearch`:

| Capture | Mean enclosing SSR GPU interval | Median |
| --- | ---: | ---: |
| `20260927-100134-794-scene-gpu-customs-1200-ssr-native` | 0.334070 ms | 0.332800 ms |
| `20260927-100345-403-scene-gpu-customs-1200-ssr-stochasticnative` | 0.541765 ms | 0.540672 ms |
| `20260927-100457-577-scene-gpu-customs-1200-ssr-stochasticcustom` | 0.825033 ms | 0.824320 ms |

The custom reconstruction switch adds 0.283268 ms to the enclosing interval. Its internal reconstruction marker measures 0.241245 ms; these are not additive. The HiZ trace child is zero-duration in every sample and cannot establish traversal cost independently.

All three use the same 2560x1440 camera and stable settings, with 179/178/168 valid detailed samples. No invalid ranges/layouts or ambiguous ledger intersections occur inside SSR. The native camera angle differs by 4.316 degrees from stochastic/native; stochastic/native and stochastic/custom differ by only 0.063 degrees. Therefore the reconstruction A/B is the better controlled comparison. Non-SSR settings match. Camera totals and wall time are not isolated SSR savings.

## Changes

- Six variance/sample-count textures use RHalf rather than ARGBHalf, with RFloat fallback for unsupported RHalf UAV hardware. This saves 36 MiB at a 1024-square grid on RHalf hardware.
- Swap scalar histories instead of two GPU copies. Skip the custom filtered-output copy that was immediately overwritten by the native-alpha/exclusion finish kernel.
- Final native-downstream output reads the completed composition directly, skipping a full-camera staging copy. Capture/diagnostic stage snapshots remain available.
- Optional SM5 receiver classification, group-local ray compaction and indirect traversal. No adaptive ray reduction, changed sample sequence or CPU count readback. Direct and compact schedules share the compiled traversal kernel to avoid compiler reassociation differences in hit positions. The optional list costs 4 MiB at 1024 square.
- Maximum distance accepts 1–10000 m. Native-downstream paths retain their previous camera range unless the new explicit range-limit switch is enabled.

Compaction is disabled by default because it did not win the isolated workload. Classification and denoiser-stage markers plus the live toggle allow a matched in-game comparison. This is not a complete AMD SSSR port; adaptive rays, denoiser tile scheduling and upstream traversal remain separate work.

## Automated evidence

Unity 2022.3.43f1, D3D11, RTX 4090. The validation suite uses the existing CustomAmbientResearch timestamp helper; no new timing backend is shipped. The 0.7.0 baseline DLL and shaders come from the SHA256-pinned release ZIP. Measurements below are medians of alternating batches after warmup, not live Tarkov timings.

| Final validation workload | Reference | New | Result |
| --- | ---: | ---: | --- |
| Trace scheduling, direct versus compact | 0.1636 ms | 0.1726 ms | Compaction 5.5% slower |
| Trace + custom reconstruction, 0.7.0 versus optimized direct | 0.4498 ms | 0.4003 ms | 11.0% lower GPU time |

Camera fixture 1280x720, trace grid 1024 square, 128 iterations. The scheduling comparison uses 24 renders per batch and sky/foreground exclusions. The release comparison uses 16 renders per batch and excludes native hit/material passes and the final full-camera copy. An earlier valid timestamp run showed 14.7% lower reconstruction-workload time and 35.8% slower compaction; workload/clock/cache variation prevents treating either result as a universal percentage. Initial CPU/readback wall-time experiments are not used as GPU evidence.

Parity checks passed for exact native hit records and nonzero reconstructed RGBA against 0.7.0 on initial and accumulated-history frames. Direct/compact radiance, guides, hit points, native hit records and surface data match exactly across dense, sparse, empty and cutoff-equality fixtures, including odd dimensions and changing density. Tests also cover counter bounds, unique ray indices, D3D11 group-dimension overflow, buffer loss/resize, native/custom temporal routing, alpha/excluded-material preservation, final output without the staging copy, and configured range above 100 m through the actual native hook.

Release build/package validation and live-game testing are separate. Use three same-pose captures (vanilla, native reconstruction, custom reconstruction) to establish live costs; preserve the accepted quality settings and leave the range override off during profiling. The standalone prototype is retired; see [Stochastic SSR](StochasticSSR.md) for the current integration.
