# Reflection rendering contract

The current controls and defaults are in [Stochastic SSR](StochasticSSR.md). This reference describes the production NRD renderer on EFT 40743 / SPT 4.1, D3D11. Historical derivations and rejected experiments are preserved in the local archive described in [the cleanup record](history/SSR-cleanup-20261003.md).

## Ownership and signal

FTFX replaces the main deferred camera's SSR renderer and the exact active ordinary optic camera's SSR renderer as separate transactions. Native SSR remains responsible for disabled/unsupported cameras, night-vision or thermal optics, and camera-local failure fallback. Each replacement uses camera-sized depth and a square trace grid, with full-density GGX launches from original receiver normals. Optics have independent GGX, receiver-fallback and NRD history and R256/R512/R768 tracing capped at the native optic input size. Fresh configuration enables optic SSR with R512 through a normal toggle; HQ Visuals disables it, while HQ+ Visuals enables it. Normal preset clicks preserve optic resolution. Scope eligibility must exclude both the scope's mode flags and active night-vision/thermal effects.

With main-resolution reduction enabled, an eligible optic running GGX SSR lowers the main grid by one supported resolution step, clamped at R256. Native optics do not lower the main grid. Configured resolution is unchanged. Camera-local effective settings must not cause alternating main/optic renders to reset one another's histories. Trace-only changes preserve camera-sized depth and alias targets while resetting size-dependent histories. Full optic scene/depth coverage remains available to reflected rays; no circular receiver mask, main-signal reprojection or frame skipping is used.

Trace records distinguish measured geometry, sky signals and unresolved/protected misses. Smith visibility is estimator amplitude, separate from hit confidence. Resolve stores RGB radiance premultiplied by coverage and alpha coverage. Neither filtered coverage nor NRD hit-distance estimates establish new geometry or sky. With endpoint fallback disabled, offscreen, self-hit and budget failures cannot become sky. Completed continuation requires bounded allowances and a clear in-screen far-plane endpoint.

Endpoint-validated sky fallback is enabled by default and relaxes only foreground continuation failures: an allowance stop (reason 11) or iteration stop (reason 8) with a prior foreground rejection may infer sky from an upward world-space direction and a clear in-screen far-plane endpoint. Continuation must be enabled. Accepted geometry, self hits, side exits, downward directions and blocked endpoints are excluded. A clear endpoint does not prove hidden geometry is absent along the unfinished ray. Sky record kind 2 therefore denotes a sky signal when the feature is enabled; TraceInfo flag 128 distinguishes inferred endpoint fallback from completed-continuation flag 64 and direct sky. Original allowance flags and traversal counts remain. The existing finalization dispatch, bounded sky resolve/fog, NRD sky surrogate and resources are reused; toggles reset history.

## NRD adapter

NRD 4.18.0 and its upstream dependencies are pinned in `native/NRD/UPSTREAM.json`. The native bridge executes unmodified upstream DXBC through D3D11 on Unity's render thread. The local adapters gather/encode signals and unpack output.

| Input | Contract |
|---|---|
| Resolved RGB | Coverage-premultiplied incident radiance |
| Resolved alpha | Coverage replicated as RGB for a second REBLUR_SPECULAR instance |
| Receiver normal/roughness | Original world normal; linear roughness `1 - smoothness`; upstream normal packing |
| Receiver depth | Positive eye depth in metres; excluded receivers use out-of-range depth |
| Motion | Engine UV motion negated to previous-minus-current; no trace-size scaling |
| Geometry hit distance | Measured receiver-to-reflected-endpoint distance |
| Sky signal distance | 65504-metre surrogate normalized by the upstream helper; includes flagged endpoint inference when enabled |
| Unknown miss distance | Zero; no measured endpoint, zero input colour/coverage |
| Camera state | Current/previous nonjittered GPU projection, world-to-view, UV jitter and frame time |

Projection row Y is flipped for NRD's D3D UV convention. Late TAA/DLSS raster state is finalized before native execution. Hit-distance reconstruction is disabled. Deliberate probe/cutoff/range exclusions are excluded before packing and zeroed after unpacking. Water and dry receiver ownership remain distinct.

Two separate denoisers can weight colour and coverage differently, introducing local bias. Output coverage is saturated; RGB is not divided by coverage in the native adapter. Spatial denoising remains active when temporal accumulation is disabled. Resets cover settings, cuts, frame/time gaps, projection/target, resolution and AA changes. This adapter requires live checks for lag, trails, fine detail and geometry/sky transitions.

## Reconstruction and composition

Custom reconstruction weights receiver support using a depth-derived plane, original shading normals and smoothness. Compatible zero-coverage misses remain in the support denominator. Receiver F0 is applied at full resolution, rather than rejecting incident-radiance donors by F0 texture. NativeGaussian remains an alternative using the installed Gaussian/roughness filtering contract.

For an ordinary eligible receiver, let S be incoming scene RGB, E the existing native environment contribution, c reconstructed coverage, R reconstructed radiance, B native forced-miss receiver radiance, W the reflected-signal material response, f fallback strength and T water tint. The compositor returns:

```text
max(S - E, 0) + T * W * (c * R + f * (1 - c) * B)
```

Scene alpha is preserved. E includes roughness-filtered probe, normalized-normal material response, bottom shade, GBuffer0 alpha, SSR factor and stencil visibility. W preserves native reflected-signal response using smoothness cubed and the original unnormalized G-buffer normal. These are distinct material domains. Environment reflection is removed once; SSR is not added on top of unchanged probe reflection.

Confirmed sky retains the established bounded half-receiver/half-sky blend. Only the sky component receives sampled Smith amplitude. Missing reflected geometry remains unavailable; first-rejection foreground colour is diagnostic only. CloudSix's published upper sky can supply both confirmed sky radiance and the native sky-only probe input through the optional SSR integration. Probe repair runs only after an eligible FTFX renderer has recently replaced native SSR and its camera/effect remain active. Prefix and postfix both gate cloud submission; switching inactive retains a one-time native refresh to clear cloud faces. Probe repair precedes scene lighting and receiver-colour fallback; it does not add measured ray coverage or change environment subtraction.

Resolve restores the installed native SSR's TOD fog approximation before NRD. For camera-to-source world displacement D, optical depth is `min(max(_Density.z, 0.001) * length(D) / (1 + max((D.y + _Density.y) * _Density.x, 0)), 10)`. Opacity is `1 - exp(-optical depth)`. Native TOD atmosphere evaluation scales its integral by that opacity; resolve blends toward it with `0.5 * opacity * (1 - saturate(1.5 * receiver stencil alpha))`. Geometry receives this treatment before sampled Smith amplitude and coverage. This preserves native camera-to-hit semantics, rather than introducing receiver-to-hit volumetric integration. Absent TOD models leave radiance unchanged.

Confirmed sky is an FTFX extension: its measured far-plane endpoint supplies D for the same fog treatment around the existing TOD/CloudSix bounded source. Smith scales the atmospheric addition without turning it into hit confidence. Unknown misses, forced-miss receiver fallback, environment subtraction and the shared sky-only cubemap receive no extra resolve fog. The downstream scene fog pass keeps its native ordering. Diagnostic captures include GPU-sampled `fog-parameters.exr` (`_Density` in RGBA) and `stencil-shadow.exr`.

Below-cutoff and out-of-range receivers skip SSR/fallback and use the environment-removed source scene blended toward incoming lighting by Cutoff environment blend. Range is radial camera-to-receiver distance; reflected source distance is unrestricted. Zero-smoothness probe substitution has its own material-weighted, distance-faded contribution and removes any eligible existing environment term once.

## Water and resource lifetime

The main-camera pre-ambient surface capture at u5 supplies original GBuffer0 colour for visible-water tint. Ordinary optics copy GBuffer0 RGB directly at the native ambient draw boundary without enabling optic AO or GI; this adds one camera-sized material-colour capture draw when tint is requested. Missing capture leaves tint neutral. The removed u4 reflection-contribution capture has no production consumer. Water ownership/depth capture remains camera-local and supports receiver matching and water-only sky recovery.

Only explicit diagnostics allocate ray/PDF, accepted-hit and first-rejection targets. TraceInfo remains mandatory for continuation sky recovery. Detached textures and native resources remain alive until pending asynchronous readbacks complete. Native frame packets own the resources needed for execution; disposal cancels abandoned packets. No synchronous GPU wait is introduced into production.

Retired settings/debug names migrate through the existing orphan-key convention; surviving debug numeric IDs and saved values remain stable. The current baseline and restoration archives are validation-only and excluded from the assembly, bundles and runtime package.
