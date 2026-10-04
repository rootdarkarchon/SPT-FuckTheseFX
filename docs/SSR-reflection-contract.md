# Reflection rendering contract

The current controls and defaults are in [Stochastic SSR](StochasticSSR.md). This reference describes the production NRD renderer on EFT 40743 / SPT 4.1, D3D11. Historical derivations and rejected experiments are preserved in the local archive described in [the cleanup record](SSR-cleanup-20261003.md).

## Ownership and signal

FTFX replaces the main deferred camera's SSR renderer as a unit. Native SSR remains responsible for optics, disabled/unsupported cameras and failure fallback. The replacement uses camera-sized depth and a square trace grid, with full-density GGX launches from original receiver normals.

Trace records distinguish measured geometry, confirmed sky and unresolved/protected misses. Smith visibility is estimator amplitude, separate from hit confidence. Resolve stores RGB radiance premultiplied by coverage and alpha coverage. Neither filtered coverage nor NRD hit-distance estimates establish new geometry or sky. Offscreen, self-hit and budget failures cannot become sky. Completed continuation requires bounded allowances and a clear in-screen far-plane endpoint.

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
| Confirmed sky distance | 65504-metre surrogate normalized by the upstream helper |
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

Confirmed TOD sky retains the established bounded half-receiver/half-sky blend. Only the sky component receives sampled Smith amplitude. Missing reflected geometry remains unavailable; first-rejection foreground colour is diagnostic only.

Below-cutoff and out-of-range receivers skip SSR/fallback and use the environment-removed source scene blended toward incoming lighting by Cutoff environment blend. Range is radial camera-to-receiver distance; reflected source distance is unrestricted. Zero-smoothness probe substitution has its own material-weighted, distance-faded contribution and removes any eligible existing environment term once.

## Water and resource lifetime

The retained pre-ambient surface capture at u5 supplies original GBuffer0 colour for visible-water tint. Missing capture leaves tint neutral. The removed u4 reflection-contribution capture has no production consumer. Water ownership/depth capture supports receiver matching and water-only sky recovery.

Only explicit diagnostics allocate ray/PDF, accepted-hit and first-rejection targets. TraceInfo remains mandatory for continuation sky recovery. Detached textures and native resources remain alive until pending asynchronous readbacks complete. Native frame packets own the resources needed for execution; disposal cancels abandoned packets. No synchronous GPU wait is introduced into production.

Retired settings/debug names migrate through the existing orphan-key convention; surviving debug numeric IDs and saved values remain stable. The current baseline and restoration archives are validation-only and excluded from the assembly, bundles and runtime package.
