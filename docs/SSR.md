# SSR replacement plan

Historical research plan. The standalone prototype has been retired; current build and usage details are in [Stochastic SSR](StochasticSSR.md).

Three test packages, with all comparisons available without restarting between modes:

1. **Native profiling (first live capture complete).** Measure native raymarch, resolve, temporal, history copy, each Gaussian mip dispatch/copy and composite. The [2026-09-26 Customs comparison](SSR-profile-20260926.md) measured 0.314 ms total, dominated by raymarch (0.130 ms) and composite (0.100 ms). This is enough to guide the next prototype; broader scene and motion validation comes later.
2. **Composition and deterministic Hi-Z (scene baseline confirmed).** The 0.2.2 prototype corrects vertical projection and native miss fallback. The user confirmed expected behavior in the Customs test scene; stronger weapon response remains a quality difference. [Fresh captures](SSR-prototype-profile-20260926.md) measure 0.486 ms HiZ versus 0.334 ms native (+0.152 ms / 46%) at full internal resolution. Compute substage attribution remains unreliable. This establishes a correctness/cost baseline, not a performance or general quality win.
3. **AMD adaptation and composition isolation (0.6.0 test package).** Stochastic uses pinned AMD SSSR sampling and Reflection Denoiser stages through a D3D11 adapter. The new HiZNative mode replaces only native raymarch; StochasticStandalone skips native SSR entirely. Native replay modes isolate composition and mip filtering. One hotkey captures six comparisons with raw buffers and settings, avoiding repeated game setup. Legacy HiZ/Stochastic still retain both renderers. Live quality and performance of the new modes remain unverified; adaptive scheduling remains unported.

Initial target was visible quality improvement near 0.4 ms total on the RTX 4090 at 2560x1440 internal resolution. The user accepted the measured stage-2 tradeoff above that initial target; stage-3 cost is unmeasured. The prototype adapts actual pinned AMD sampling/denoising code with custom traversal and Tarkov composition; it is not a complete SSSR/backend port.

## First package: native profiling only

The optional `FTFX.NativeSsrProfiling.dll` is a separate BepInEx add-on. It requires the existing **CustomAmbient Research** plugin and uses that plugin's timestamp backend and CSV exports. It does not replace FTFX, modify shaders, toggle native SSR, or initialize another native GPU query backend. Existing glass-culling work is independent.

Install the profiling ZIP into the game directory while the game is closed. One launch is sufficient for all captures below:

1. Keep native SSR enabled. Choose a stationary reflective scene, fixed time/weather, and the intended internal resolution. Close diagnostic overlays before recording.
2. In F12, set **FTFX SSR Profiling > Capture > Mode = NativeMarkersOnly**. In **CustomAmbient Research > Capture**, select **Mode = SceneGpu**, set an identifiable Label, and Start (or use its configured start shortcut, default Alt+numpad Enter). Diagnostics can remain off. Let the complete control/coarse/detailed session finish.
3. Change the add-on mode to **PerPass**, set a different Label, and repeat from the same pose. The starting mode is held for the whole capture. Repeat baseline once if scene drift is a concern. Neither mode changes reflection energy or quality.
4. Additional poses, SSR quality settings and captures can be tested in the same launch; compare performance only between matched settings. Do not change settings during a capture.

Results are under `BepInEx/CustomAmbientResearch/<session>/`. `scene-gpu-coverage.csv` holds inclusive GPU durations; `scene-gpu-breakdown.csv` holds exclusive accounting. Added names contain `SSR-pass/`: raymarch, resolve, temporal, history-copy, mip-filter, mip-copy, composite. Mip ordinals are separate scopes. Scene-view resolve and history seed appear only when actually executed. Missing rows are not zero-cost work.

`scene-deep-settings.txt` records the add-on mode and native SSR settings at Render entry, before native preset application; keep settings stable through warmup. `scene-deep-calls.csv` records the square SSR resolve target dimensions (not each mip's dimensions); ordinal mip 1 is half that size. Its managed elapsed times are recording diagnostics, not GPU timings. Warm gameplay should contain four shader passes, one history copy, and one filter/copy pair per generated mip. Parent SSR and mip scopes overlap their children; never add parent and child times.

Compare coarse/detailed camera totals and matched baseline/per-pass captures for query overhead. Discard incomplete/invalid captures or captures with profiling errors. Sub-microsecond copies may quantize to zero. The helper retains the existing profiler's sampled query and generation/lifetime safeguards.

Build with `scripts/Build-SsrProfiling.ps1`; validate with `scripts/Test-SsrProfiling.ps1`. Automated validation is separate from live EFT GPU measurements.

Validation passed nine real D3D11 timestamp cases in Unity 2022.3.43f1, including unchanged fixture pixels/pass order, history reset, scene-view branching, per-mip coverage, camera/capture gating, mode freezing and cleanup. The installed native assembly hash and pass-call layout were checked separately. Live EFT integration and per-pass costs still require the captures above.



