# Declined SSR implementations: archive before NRD standardization

> Historical record from 2026-10-03. See the [documentation index](../README.md) for current guides.

Archived on 2026-10-03 before removal. These implementations were functional research paths, declined for production after the user preferred NRD's live visual results. This document records their intended behavior and restoration contracts, not a claim that they were universally ineffective.

The exact pre-removal sources, shaders, validation sources and entry scripts are in `unity/StochasticSsrValidation/Baseline/BeforeNrdStandard/implementation.zip`, with SHA-256 hashes in `manifest.json`. Source/shader copies support regression comparison. They are excluded from the production assembly and runtime package. Detailed historical reports are entries under `docs/` in the verified local research archive described in [the cleanup record](SSR-cleanup-20261003.md). Do not restore old defaults, vendor/game assemblies or unrelated AO changes when reusing them.

## Confidence temporal denoising

Purpose: reduce stochastic GGX radiance variation without inventing reflected geometry. `ConfidenceSsrPipeline` resolved measured geometry and confirmed TOD sky into radiance premultiplied by coverage. `Reflection.shader` reprojected through motion vectors with late raster jitter compensation, sampled a 2x2 history footprint, and validated receiver plane, normal, material, water ownership and reflected-source geometry/classification. Geometry anchors were transformed into the current view and checked against visible depth; sky directions also required angular agreement.

The confidence path used neighborhood colour bounds and mean/variance clipping, luminance innovation, effective history age and a configurable accumulation cap. Its resulting coverage was capped by current coverage, preventing historical radiance from creating stronger current reflection coverage. Rejection bits and luminance moments supported debug captures. This made sparse misses hard to integrate and did not suppress the boiling as effectively as NRD in the user's live observations.

## Statistical coverage temporal and display filtering

The separate rough coverage path integrated premultiplied colour and coverage including zero-valued stochastic misses. A retained source anchor tracked the last accepted hit or confirmed sky independently of raw hit authority. Invalid source radiance/moments were discarded while compatible receiver counts could survive, preventing rare hits from repeatedly cold-starting. Smoothness controls faded between this estimator and confidence temporal. The display-only 3x3 coverage filter used receiver-plane, normal, smoothness, water and eligibility constraints, with temporal variance or local variation deciding strength. It did not feed the filtered result back into history. NativeGaussian remained a separate reconstruction option.

Removed controls: Rough reflection coverage accumulation; Rough coverage full-strength smoothness; Rough coverage fade-out smoothness; Coverage reconstruction filter; Coverage filter strength; Coverage filter full-strength smoothness; Coverage filter fade-end smoothness; Coverage filter smooth-surface strength. The backend selection Experimental NRD REBLUR specular is also retired when NRD becomes mandatory. Temporal stabilization and History frames remain meaningful NRD controls; NRD anti-firefly remains independently selectable.

Resources: Temporal/History ARGBHalf; HistoryGuide and HistoryPoint ARGBFloat UAVs; HistoryMaterial ARGBHalf; TemporalStats/HistoryStats ARGBFloat; HistoryPlane ARGBHalf with prior classification packed in z; TemporalAnchor/HistoryAnchor ARGBFloat. Together these dedicated custom allocations occupied about 72 MiB at R768. Five ownership pairs used ping-pong publication; receiver/material contexts were written directly. Full prior records existed only for one-shot diagnostic readback. NRD already released/bypassed these allocations. The duplicate DepthGapFilled measured-signal attachment and ResolveDisplay/TemporalDisplay/CoverageDisplay shader paths were part of the display plumbing, not independent reflection information.

Restoration: restore the archived pipeline, shader functions/passes, options/settings and capture fields together; preserve render-before-publish ordering, UAV capability, resize/reset/toggle behavior and delayed disposal while asynchronous captures are pending. Reuse the archived history ownership/packing, receiver rejection, sparse coverage and jitter tests. Do not treat filtered coverage or anchors as new ray hits.

Historical archive entries: `SSR-coverage-accumulation-20261002.md`, `SSR-coverage-filter-20261003.md`, `SSR-history-ownership-and-packing.md`, `SSR-temporal-stability-validation.md`, `SSR-nrd-reblur-specular.md`.

## Distance-based ray-launch normal stabilization

Purpose: reduce undersampled small normal/ripple detail without changing material shading or hit-surface normals. `PrepareWaterNormals` became a generic all-receiver kernel despite its historical name. It reconstructed local depth positions, estimated a plane, sampled a bounded staggered 5x5 camera-pixel footprint, rejected depth-plane discontinuities, invalid/opposing normals and insufficient support, and blended the original normal toward a normalized weighted mean. A start/end distance smoothstep controlled blend and footprint growth; close receivers used a negative sentinel to preserve original launches exactly. The prepared trace-size ARGBFloat target also stored variance. Trace and continued-sky visibility used the filtered launch normal where valid; NRD guides still used the original receiver normal.

Removed controls: Normal stabilization; Trace normal strength; Normal stabilization start distance; Normal stabilization end distance; Normal filter radius, plus historical water-name aliases and NormalStability debug view. At R768 the optional float texture used 9 MiB, plus a compute dispatch and guide preparation. It was not required by NRD and the user requested its retirement; this does not establish that NRD reconstructs absent foreground geometry.

Restoration: restore kernel, bindings, option/UI fields, allocation/lifecycle, original-normal metadata and diagnostic view as a unit. Preserve camera-pixel footprint semantics, receiver range gating only at the filter center, negative-sentinel exact close behavior and pending-readback lifetimes. Reuse archived generic-normal, water and off/on trace parity tests.

Historical archive entries: `SSR-normal-stabilization.md`, `SSR-water-normal-stabilization.md`.

## Captured CustomAmbient environment subtraction

Purpose: remove the exact material-weighted environment reflection already added by CustomAmbient before replacing it with SSR, avoiding numerical/contract differences from native recomputation. `AmbientReflectionCapture` allocated a per-main-camera HDR deferred D3D11 ARGBFloat UAV at camera resolution, cleared it at BeforeLighting every render, and bound it at u4 during the existing ambient draw. RGB accumulated the actual reflection contribution; alpha counted writes. Only a single producer write was accepted. Missing/repeated writes had diagnostic colours; normal rendering used native recomputation when no valid capture existed. Exact subtraction and round-trip debug passes explored contribution/removal differences.

Removed control: Use captured CustomAmbient subtraction. Removed diagnostic views: CaptureNormal, ExactEnvironmentContribution, EnvironmentDifference, ExactRemovalScene, ExactReplacement, ExactRoundTrip and ExactRoundTripError. Removed resources/code: AmbientReflectionCapture, u4 writes and clear buffer, captured-texture bindings, exactResidual, ExactSubtract pass and captured contribution composition branches. The separate AmbientSurfaceCapture at u5 must remain: it supplies pre-ambient GBuffer0 colour for water reflection tint and is not an environment-subtraction capture.

The earlier matched sequential benchmark pair measured approximately +0.1298 ms filtered control and +0.0911 ms coarse camera GPU with captured subtraction enabled. This is limited sequential-pair evidence, not a general FPS claim. The user observed no meaningful visual improvement. Removing capture must retain native environment removal, including material/specularity, roughness-filtered probe, bottom shade, SSR factor and stencil visibility; otherwise reflections become additive or lose energy. Zero-smoothness probe substitution must continue replacing existing environment reflection once.

Restoration: restore capture producer/clear/lifetime, shader keyword and UAV layout, selection and exact diagnostic consumers together. Revalidate CustomAmbient draw ordering, HDR/deferred/main-camera restrictions, initialization, single-write status, resize, disabled capture and native recomputation energy parity. Do not restore any AO/bent-normal or foliage changes from the whole archived bridge/shader as an indiscriminate file replacement.

Historical capture audit: `docs/AmbientReflectionCaptureTest.md` in the local research archive. Current [composition contract](../SSR-reflection-contract.md).

## Retained production contract

Full-density GGX Hi-Z tracing and bounded foreground continuation stay. Raw coverage/miss classification stays separate from hit distance; deliberate probe/cutoff/range exclusions do not enter NRD as noisy samples. NRD's two REBLUR_SPECULAR identifiers continue processing colour and coverage separately with actual upstream D3D11/DXBC shaders. Temporal controls, camera/AA/resize/cut reset rules and asynchronous native resource lifetime remain. No vendor lock is introduced. Native receiver-colour fallback, original material response, TOD recovery, source-scene environment subtraction, experimental zero-smoothness probe lighting, water ownership/tint and Custom/NativeGaussian reconstruction remain. NRD initialization/dispatch failure must restore native SSR rather than resurrect the removed denoiser.

## Post-removal validation record

The isolated NRD-standard suite passed 1,386 checks, including exact output parity against this snapshot with NRD on, normal stabilization off and captured subtraction off. Raw tracing, resolve, NRD output and final composition matched for synthetic sequences and three captured slab/water/crane inputs. Both reconstruction choices, temporal on/off, animated normals, reset/resize/readback, resource recovery and native fallback/re-enable were covered. Separate AO/contact-GI and water-colour capture tests passed. These are regression fixtures, not live GPU performance measurements. See [current SSR documentation](../StochasticSSR.md) for retained controls and runtime packaging.
