# Stochastic SSR

2026-10-03: NRD REBLUR_SPECULAR is the standard and only FTFX GGX denoiser. The custom confidence/coverage temporal engine, its display filter, ray-launch normal stabilization and captured CustomAmbient subtraction have been removed. Their purpose, algorithms, settings, resources and restoration contracts were archived **before removal** in [Declined SSR implementations](SSR-declined-implementations-20261003.md), alongside exact source snapshots. That archive is not shipped in runtime packages.

FTFX SSR is **off by default**. Keep SSR enabled in Tarkov when using it. Off restores native SSR; optics remain native. SSRSix bypasses the integration before assets/hooks are loaded. NRD initialization or dispatch failure restores native SSR; toggling FTFX SSR off/on retries. Missing runtime files require a restart after installation. The native backend requires D3D11; it has no NVIDIA vendor gate, CUDA or NVAPI dependency. AMD compatibility is intended but has not been tested on AMD hardware here.

## Signal and rendering order

Full-density stochastic GGX Hi-Z tracing at the selected square resolution → current radiance/coverage resolve → actual NRD D3D11/DXBC processing → Custom or NativeGaussian full-resolution reconstruction → environment replacement.

The default trace resolution remains R768 and camera depth stays camera-sized. No reduced ray density is introduced. Ray launches use original receiver normals. NRD receives noisy radiance, original receiver normal/roughness, view Z, motion vectors, measured hit distance and current/previous transforms. Colour and coverage use separate REBLUR_SPECULAR identifiers because NRD has no native SSR coverage channel. This is the existing coverage adapter, not a claim that estimated coverage establishes a new reflection hit. Raw miss/geometry/sky classification remains independent of hit distance. Deliberate probe substitution, receiver cutoff and range exclusions do not enter NRD as noisy GGX samples. See [the rendering contract](SSR-reflection-contract.md) for mapping and limitations.

**Temporal stabilization** controls NRD accumulation. Off still uses NRD spatial denoising. **History frames**, default 16, caps main/stabilized history; fast history is capped at six. **NRD anti-firefly** defaults off to preserve isolated bright details. Settings, camera cuts, frame/time gaps, projection/target changes, resolution and AA/raster changes reset histories. Raster state is finalized after TAA/DLSS jitter in camera onPreRender. Pending asynchronous captures retain detached resources through resize/disposal.

Both reconstruction options remain: Custom preserves the full-resolution receiver footprint and unsupported-region checks; NativeGaussian applies Tarkov's installed Gaussian filtering/roughness LOD to the NRD signal. The native receiver-colour miss fallback still owns its own native history; this is separate from the removed custom GGX temporal histories.

## Composition and continuation

Environment reflection is still subtracted using the native material/probe equations before SSR replacement. Removing exact CustomAmbient capture does not make reflections additive. Material response, stencil visibility, TOD blending, receiver-colour fallback, water ownership and water tint remain. The separate pre-ambient surface-colour capture at u5 remains for water tint; the full-screen reflection-contribution capture at u4 is gone.

Unresolved coverage uses native-style receiver-colour fallback, default strength 65%. Rejected foreground colour is diagnostic data only and is not stretched into missing reflections. Hi-Z continuation remains bounded by iterations, skipped samples, camera-pixel distance and eye-depth separation. A completed in-screen continuation with a clear far-plane endpoint can recover TOD sky on all eligible receivers by default. Offscreen exits, exhausted budgets, self hits and unconfirmed misses do not become sky. The depth-separation allowance is ray-to-foreground eye depth, not hit thickness or a proof that hidden geometry is absent.

Receivers below Minimum receiver smoothness use the environment-removed scene blended toward incoming lighting by Cutoff environment blend. They add neither GGX nor receiver-colour fallback. Optional range gating uses radial camera-to-receiver distance and the same excluded-receiver composition; nearby receivers can still reflect distant sources.

Experimental zero-smoothness probe lighting remains enabled: smoothness exactly zero with nonzero specular skips GGX and uses roughness-filtered material-weighted probe lighting, deliberately bypassing GBuffer0 alpha suppression. Existing environment reflection is removed once. The close contribution is 10% through 5 m, transitioning by smoothstep to 30% at 15 m. Both slider values remain configurable; the contribution is not solely slider weighted.

## Fresh/reset defaults

Surviving saved values are preserved. Retired controls are removed during settings binding, and retired normal/exact-capture debug values reset to Normal. There is no migration of surviving numeric preferences.

| Setting | Default |
|---|---:|
| Enabled | Off |
| SSR resolution | R768 |
| Maximum iterations / continuation samples | 350 / 350 |
| Continuation pixels / depth separation | 500 pixels / 200 m |
| Allow continued sky on all receivers | On |
| Thickness / thickness hit confidence floor | 1 m / 100% |
| Bypass hit normal checks | On |
| Fallback strength | 65% |
| Minimum receiver smoothness / cutoff environment blend | 0.4 / 25% |
| Reconstruction backend | Custom |
| Temporal stabilization / History frames | On / 16 |
| NRD anti-firefly | Off |
| Zero-smoothness probe lighting | On |
| Probe close / far contribution | 10% / 30% |
| Probe fade start / end | 5 m / 15 m |
| Water reflection surface colour influence | 20% |
| Limit reflection range / Maximum distance | Off / 250 m |
| Debug view / Freeze ray sequence | Normal / Off |
| Capture continuation diagnostics / traversal work | Off / Off |
| Screen-edge confidence fade (fixed) | 0 pixels |

There are 29 SSR controls. Removed controls and their former behavior are listed in the archive. The retired black-only EnvironmentFallback debug value (17) resets to Normal; surviving debug IDs retain their numeric values.

The [subsequent source cleanup](SSR-cleanup-20261003.md) makes first-rejection diagnostics optional, removes unused temporal raster uploads and dead helpers, and retires the old loose validation suite. Ordinary tracing does not allocate or write the first-rejection target. Captures and RejectionLocation still enable it, with asynchronous lifetime preservation.

## Validation and packages

`scripts/Test-StochasticSSR.ps1` runs the pinned D3D11 NRD backend, executes the live hooks, compares the renderer against the archived NRD-enabled implementation, checks configuration and lifecycle contracts, and rebuilds `stochasticssr` and `nrdssr`. See [Build and validation](BuildValidation.md) for native-source setup, Python dependencies and the required private captures. `scripts/Test-NrdSSR.ps1` is a convenience entry point for the same suite.

The current regression passed 1,559 checks: bit-exact raw tracing, resolve, NRD output and final composition for both reconstruction modes and temporal on/off; synthetic animated normals; three captured slab/water/crane scenes; reset, resize, delayed readback, lost resource, late raster and native fallback/re-enable behavior. Rejection diagnostics and traversal-work capture preserve exact tracing; optional allocation, capture rearm and resize/readback disposal are checked. The comparison disables normal stabilization and exact captured subtraction in the archived control. Their enabled output is deliberately not preserved. AO/contact GI and pre-ambient water-colour capture validation also passed after removing u4.

No GPU timings were collected during this cleanup. Historical NRD timings are not measurements of this change. Live Tarkov visual acceptance and timing remain separate from fixture parity.

`scripts/Build-Package.ps1` always includes the NRD native DLL, input shader bundle and required licenses. Runtime packages contain no manuals, test sources or historical archives.
