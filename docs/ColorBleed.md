# Screen Space Contact GI — NRD implementation

SSCGI traces short cosine-weighted diffuse rays and passes incident radiance to the pinned NRD 4.18.0 `REBLUR_DIFFUSE` backend. It remains main-camera, HDR deferred, opaque contact bounce with a XeGTAO dependency. FTFX SSR does not need to be enabled. There is no sky/environment fallback.

## In-game comparison

1. Replace the entire `BepInEx/plugins/FuckTheseFX` runtime folder from the ZIP, including its DLL, native DLL and asset bundles. Preserve your config. Start with XeGTAO enabled and Debug View = Normal.
2. Enable **Screen Space Contact Global Illumination**. The authored defaults now match the tested in-game SSCGI configuration: Resolution 15%, 4 rays, 128 steps, 16 history frames, radius 1 m, depth tolerance 0.05 m, donor limit 1, Strength 6 and NRD anti-firefly on. Enabled defaults to off. Explicit saved values remain in use; no configuration migration is performed. The Resolution (%) key defaults to 15%.
3. Compare off/on beside a coloured wall or object with a neutral receiver. Hold still, then translate and rotate slowly. Check hue, flicker, contact detail, thin barriers, moving objects and newly exposed surfaces. Compare 10%, 15%, 20%, 25% and 50% resolution, keeping rays, radius and Strength fixed. Percentages apply to each internal camera dimension: at 2560x1440, 10% = 256x144 and 25% = 640x360. Pixel count at 10% is 16% of the 25% setting; full-resolution source capture, depth hierarchy and reconstruction still retain their cost. Test anti-firefly off separately if small colour-transfer details appear suppressed.
4. Repeat with TAA and DLSS, and with FTFX SSR off/on. Inspect foliage, water and actors. Test hideout/raid transitions, resize, graphics changes and toggling GI. Record which settings and scene produced each observation.
5. For diagnostic inspection use the contribution, raw, denoised, hit-distance or trace views. Return to Normal and turn captures off before measuring performance.

The current defaults were reviewed in game and benchmarked on 2026-10-04 at 15% resolution and Strength 6. Synthetic tests continue to check signal contracts and resource behavior; they do not substitute for in-game visual review.


## Controls and cap semantics

Controls include Enabled, Strength, Temporal accumulation, History frames, Radius, Depth tolerance, Donor luminance limit, Maximum bounce fraction (%) and Distance cutoff. Advanced sampling controls are Rays per pixel (1/2/4), Maximum ray steps (16–128) and NRD anti-firefly. Resolution is now an integer **Resolution (%)** control, default 15 and range 10-50. No configuration migration is performed. Dimensions round up, including odd sizes, and changes recreate NRD resources and reset history. Strength ranges from 0-10. Fresh defaults are disabled, 15% resolution, 16 history frames (4-63), 4 rays, 128 steps, radius 1 m, depth tolerance 0.05 m, donor limit 1, distance cutoff 25 m and Strength 6.

The final added luminance ceiling is now **Donor luminance limit × Maximum bounce fraction**. It is applied with hue-preserving scaling after receiver reflectance and Strength, before visibility and fades. It adds no brightness floor and no noisy local donor reference. Donor luminance limit is a ceiling, not a multiplier: raising it beyond the available donor luminance has no effect unless the final bounce cap is also active. Limits above 1 are supported and verified with actual traced HDR donors.

**Reconstruction cutoff** is an Advanced slider from 0 to 0.1, default 0. It measures final added linear luminance after receiver reflectance, Strength, the bounce cap, AO visibility and distance/edge fades. Zero skips only exactly black GI. A conservative upper bound over all four denoised taps rejects faint receivers before their geometric reconstruction; contributions fade in smoothly from the cutoff to twice the cutoff. Contributions above twice the cutoff retain their brightness and hue. This is a composition control: it does not skip rays or alter valid black observations in NRD. Try 0, 0.001, 0.002 and 0.005 at the same view and exposure, checking subtle colour transfer and moving camera transitions.

Temporal-off retains NRD spatial processing. Strength, reconstruction-cutoff, bounce-cap and diagnostic-gain changes preserve incident-light history. Transport, guide, AA, projection, target, colour-space and resolution changes reset it, as do camera cuts and render gaps over 0.5 seconds.

## Signal and visibility contract

The command buffer remains first at BeforeImageEffectsOpaque, after ambient lighting and before opaque fog/SSR. Each render captures fresh full-resolution source lighting before adding GI. Donors never use the previous composited scene.

XeGTAO mip-zero linear depth supplies a separate conservative min-depth hierarchy. Each reduction uses four clamped source samples and ceil-sized levels, including odd dimensions. Averaged XeGTAO mips are not used for visibility.

Receivers occupy fixed low-resolution pixel centres. All sampled guides describe that UV. A fixed licensed 128×128 blue-noise texture scrambles a 32-point two-dimensional Sobol sequence; a single global shift changes between cycles. The sequence advances on actual camera renders. It does not use per-frame pixel hashes or a shared rotating spiral.

Rays reject below-geometric-surface directions and traverse conservative cells with bounded work, full-depth crossing refinement and self-hit rejection. The first blocking surface terminates transport. Actors provide blocking geometry but cannot donate light. Gamma donors are decoded to linear light; donor luminance is limited without desaturation, glossy donors are suppressed and squared world-distance falloff bounds transport.

Radiance is averaged over every launched ray, including zero misses. Cosine sampling cancels the receiver cosine/PDF factor. Receiver-to-hit distance is measured separately from brightness: finite accepted endpoints, including black surfaces and actors, contribute to its arithmetic mean. No endpoint means zero distance.

Compute directly writes packed RGBA16F YCoCg radiance/normalized hit distance, upstream-packed RGB10 normals with diffuse roughness 1, R32F view depth and RGBA16F previous-minus-current UV motion. Invalid receivers use out-of-range view depth; valid zero-light receivers remain eligible. The bridge uses one diffuse identifier and no duplicate SSR packing or coverage denoiser. Normalization is A=Radius, B=0, C=1 in both shader and native settings. Checkerboarding and hit-distance reconstruction are off. Initial prepass/min/max blur radii are 8/1/16, with anti-firefly enabled and other upstream defaults. Main/stabilized history follow the configured limit; fast history is capped at 6, history-fix at 3.

The pinned signal specification is [NRD noisy inputs](https://github.com/NVIDIA-RTX/NRD/blob/c3d8999f8b30dca3b1c07d9aeab235b29d9b1a28/README.md#noisy-inputs). Existing SSR exports, 292-byte frame layout and settings remain compatible. The diffuse frame uses its own version-checked 304-byte ABI. Packets retain native COM textures/backend ownership; cancellation and disposal require no synchronous GPU waits.

Depth-plane/geometric-normal-aware reconstruction applies full-resolution receiver reflectance, Strength, cap, AO visibility and distance/edge fades. Actors and exactly zero-contribution receivers are rejected before reconstruction. Gamma composition is exactly `encode(decode(original) + addition) − original`; RGB-only blending preserves destination alpha. Native initialization or dispatch failure suppresses SSCGI, warns once and permits retry by toggling GI.

## Diagnostics, capture and replay

Numeric view IDs 16/17 remain contribution/NRD validation. ID 17 now displays the actual upstream validation panels (normal, depth, motion, history weight and hit distance). IDs 20–23 are raw radiance, denoised radiance, normalized hit distance and trace classification. Raw/denoised views contain incident linear light before material multiplication. Hit-distance grayscale is measured distance divided by Radius, with black for unavailable endpoints.

Trace flags can overlap: bit 1 geometry, 2 range termination, 4 off-screen, 8 rejected self-hit, 16 budget/unsupported crossing and 32 below-geometric surface. Display combines green geometry/range, red self/budget and blue screen/below-surface. Captured trace channels are flags, traversal work, accepted endpoint count and mean distance in metres.

Enable **Debug / Contact GI capture sequence** for an asynchronous sequence (8–600 frames, default 64). It automatically turns off after the requested actual renders. Resources are allocated only on request. Captures live under `BepInEx/FuckTheseFX/ContactGI/<session>/<frame>`. Diagnostic snapshots retain ownership until readback completion, including during production resize/disposal. No runtime GPU wait is introduced.

Files contain row-major little-endian RGBA32F pixels. `frame.json` schema 2 records full/internal dimensions, resolution percentage, settings, AA mode, current/previous camera matrices, jitter, rendered sampling index, reset state, delta time and production/native pool memory. Raster rows are row-major; standalone Matrix4x4 arrays use Unity's column-major indexing. Source, composited scene, full depth, full material/normal/motion guides, actors/AO, packed NRD inputs, the cached geometric-normal guide, raw/denoised output and traversal work are included. Metadata also records reconstruction cutoff. `readback.txt` is written only after all data writes finish. Cancelled or failed frames cannot be accepted for temporal replay.

Stage the Unity harness with `scripts/Test-Occlusion.ps1`, then replay a capture with `scripts/Replay-ContactGI.ps1 -CaptureFolder <session>`. Replay executes the actual native diffuse denoiser on captured inputs and reports packed RGB output error. Schema-1 divisor-based captures remain readable; new captures store actual receiver dimensions. Captures starting with existing history need warm-up before parity assessment. Capture allocation/readback work is excluded from performance comparisons.

## Validation and performance boundaries

Production uses the NRD diffuse backend and contains no custom moments, temporal resolve, spatial-filter passes or history copies. Validation stages only the current implementation.

`Test-Occlusion.ps1` exercises actual native diffuse dispatches, constant/black/bright/colour signals, finite HDR, normalized distance independent of brightness, cosine sampling, cancellation, fresh source/no feedback, alpha, 10–50% odd dimensions, camera/transport resets, Strength/gain/cutoff history retention, spatial-only NRD, optional diagnostics and capture. Existing AO/decal/foliage/actor regressions remain in that suite. A 4096-sample reference and stationary NRD measurements are recorded as diagnostics. Their synthetic metrics and eight-render lighting-step measurement are not visual acceptance gates.

GPU markers separately expose `FTFX GI SourceCapture`, `Hierarchy`, `TraceInputs`, `NRD`, `Reconstruction` and `Total`. Texture memory is recorded separately for production targets and native permanent/transient pools; traversal work is captured per receiver. GPU marker totals are inclusive: do not add Total to its children or turn CPU queue/event statistics into GPU timings.

For future performance changes compare at matched resolution, radius and visible contribution, with repeated warm-up/measured runs and median/tail GPU cost plus hitches reported separately. Cover TAA/DLSS and SSR off/on as applicable. Visual comparisons should retain colour transfer, switched-light response, motion/disocclusion and barrier separation.

## Performance changes in 1.8.3

The accepted lighting configuration remains 15% resolution, Strength 6, 4 rays, 128 maximum steps, 16 history frames, radius 1 m, tolerance 0.05 m, donor limit 1, bounce cap 20%, distance cutoff 25 m and anti-firefly on. Enabled still defaults to off. Reconstruction cutoff is new and defaults to zero. NRD radii, history, anti-firefly and sampling remain unchanged.

The implemented changes are listed below. The original benchmark captures remain under `D:\Tarkov-SPT-4.1\BepInEx\CustomAmbientResearch`; they do not depend on retired source copies.

| Change | Expected work reduction | Preservation / cost |
|---|---|---|
| Conservative denoised-tap cutoff before receiver geometry | Skips geometric reconstruction where the maximum possible final addition is zero or below the selected cutoff. | All four interpolation taps contribute to the bound. Zero preserves the accepted signal; positive values intentionally remove faint colour transfer. Rays and NRD history are unchanged. |
| Separate low-resolution geometric-normal cache | Removes four neighbour geometric-normal calculations per eligible full-resolution reconstruction. | NRD shading normals remain intact. A separate preparation dispatch uses the packed NRD shading guide as its fallback in RGBA32F, avoiding reduced precision. It adds 1,327,104 bytes (1.27 MiB) at 15% of 2560x1440. |
| Cached native texture pointers and D3D11 views | Avoids repeated native-pointer retrieval and seven view creations per ordinary queued render. | New resource generations, resize/recovery and optional validation changes invalidate caches. Packets retain their own COM views/backend ownership. SSR exports, frame layout and settings are unchanged. This primarily targets CPU/driver overhead. |
| Three hierarchy levels per dispatch | Reduces the 2560x1440 chain from twelve dispatches to four. | Every level retains conservative four-child minima, ceil sizes and clamped odd edges. Group-local barriers avoid cross-group dependencies. Sharing with SSR remains deferred. |
| Projected ray origin/direction calculated once per ray | Removes repeated source-level boundary projection calculations during traversal. | Advancement, refinement, tolerance and hit-distance aggregation remain unchanged. Compiler optimization may already remove part of this work; no GPU-time claim is made. |
| Removed eligibility group reduction | Removes two barriers and an atomic count from each trace group. | Invalid receivers still initialize packed inputs/out-of-range depth and return before launching rays. Wholly ineligible groups launch no rays. There is no adaptive ray skipping. |

At 2560x1440 the full-resolution source capture still allocates 29,491,200 bytes; the min-depth chain still contains 1,228,844 pixels (4,915,376 bytes). With the geometric guide the six low-resolution production input/output targets total 3,981,312 bytes at 15%, or 11,059,200 at 25%. Native NRD pools, diagnostics and AO resources are excluded. These are logical allocation/work counts, not physical memory traffic or timings.

Correctness checks compare every fused hierarchy level to independent CPU minima at 1x7, 2x1, 7x9, 259x147 and 2560x1440. Current reconstruction is checked against the actual production diagnostic output. Cutoff checks cover zero, monotonic faint-light suppression, unchanged bright contributions, destination alpha and preserved history. Gamma composition is checked against independent CPU decode/add/encode calculations. Native checks verify pointer/view reuse and invalidation; the shared DLL also passes the existing SSR suite. These checks do not judge in-game visual quality.

## Measured in-game GPU cost

The same-launch on/off pair at `20261004-123131-730-scene-gpu-customs-1200-vanilla` (on) and `20261004-123629-522-scene-gpu-customs-1200-vanilla` (off) uses 2560x1440 internal rendering and nearly identical views. Added coarse camera GPU mean is **0.315 ms** (6.441 on minus 6.126 off); the median difference is **0.300 ms**. Direct SSCGI mean is **0.330 ms**, median 0.328 ms and p95 0.347 ms. The on capture's 154 ms wall hitch was dominated by 147 ms of profiler preparation and is reported separately.

The earlier `20261004-111125-329-scene-gpu-customs-1200-ssgi-on` / `20261004-111222-607-scene-gpu-customs-1200-ssgi-off` pair added **0.467 ms** coarse camera GPU mean. Added cost is therefore **32.6% lower across the tested builds/settings**, supported by a 31.5% direct-scope reduction (0.481 to 0.330 ms). The intervening 1.8.2 baseline at `20261004-115751-889-scene-gpu-customs-1200-vanilla` had 0.391 ms direct SSCGI mean; the optimization-only comparison measured 15.7% lower direct cost, with approximate view alignment. These are sequential scene measurements, not universal FPS claims.

When evaluating positive reconstruction cutoffs, compare 0, 0.001, 0.002 and 0.005 at the same view and settings, with Debug View Normal and captures off. Record coarse camera totals and direct SSCGI separately; do not add inclusive parent scopes to their children. Detailed GI child timings are currently absent from the research CSVs, although the product records separate Unity stage markers.
