# Intel XeGTAO source

Upstream: https://github.com/GameTechDev/XeGTAO

Pinned commit: `a5b1686c7ea37788eeb3576b5be47f7c03db532c` (also recorded in `REVISION.txt`).

`XeGTAO.h` and `XeGTAO.hlsli` originate from `Source/Rendering/Shaders` at that revision. `LICENSE` is the upstream MIT license and is copied into the plugin output as `Licenses/XeGTAO.txt`.

Unity 2022's shader compiler rejects four upstream integer literal swizzles. Compatibility edits to `XeGTAO.hlsli` replace `2.xx`, `4.xx`, `8.xx` with equivalent `uint2` constructors and `1.xxxx` with `float4(1, 1, 1, 1)`. A conditional `XE_GTAO_DYNAMIC_SAMPLE_COUNTS` loop annotation allows the custom-count kernel to use runtime step counts; preset kernels retain the original unroll annotation. Algorithm code and defaults are otherwise unchanged. `XeGTAO.h` is unchanged.

General projection edit (`XE_GTAO_GENERAL_PROJECTION`, defined by the adapter; without it the upstream code is unchanged). Upstream assumes a diagonal projection (`NDCToViewMul`/`Add`, `CameraTanHalfFOV`), which breaks for the EFT optic camera when FuckTheseOptics rolls or obliquely mixes the lens projection (`m01`, `m10`, `m03`, `m13` non-zero). Three places in `XeGTAO.hlsli` branch on the macro and read adapter globals named by `XE_GTAO_NDC_TO_VIEW_MAT` (row-major 2x2 `M`) and `XE_GTAO_NDC_TO_VIEW_ADD` (`A.xy`, `C.zw`) instead of `GTAOConstants` (`XeGTAO.h` stays unchanged and the replaced fields are left zero):

- `XeGTAO_ComputeViewspacePosition`: `xy = (M * screenPos + A) * depth + C`. With Unity projection `P`, viewZ = -depth, `Q = [m00 m01; m10 m11]`, `o = (m02, m12)`, `t = (m03, m13)`, `W = diag(1,-1) * Q^-1` (XeGTAO view space is Unity's with Y and Z reflected) and `screenPos` = texture UV (ndc = 2*uv - 1): `M = 2W`, `A = W*(o - 1)`, `C = -W*t`. For a diagonal `P` this is exactly `M = diag(2/m00, -2/m11)`, `A = ((m02-1)/m00, (1-m12)/m11)`, `C = 0`, i.e. the previous `NDCToViewMul`/`Add`.
- `pixelDirRBViewspaceSizeAtCenterZ`: `depth * |column 0 of M| * ViewportPixelSize.x` (viewspace length of one pixel step in x). Equals `depth * NDCToViewMul.x * ViewportPixelSize.x` for a diagonal projection with `m00 > 0` and does not divide by zero for a 90-degree rolled projection (`m00 = m11 = 0`).
- Slice `directionVec`: the screen direction `(cosPhi, -sinPhi)` lifted to viewspace as `normalize(M * diag(ViewportPixelSize) * (cosPhi, -sinPhi))`. For a diagonal square-pixel projection this is exactly `(cosPhi, sinPhi)`; for a rolled projection it keeps the horizon plane, the screen-space samples and the bent normal frame consistent.

The sibling `XeGTAO.compute` is the Unity adapter. It supplies camera/depth/normal inputs, live tuning constants, fixed Hilbert/R2 noise, quality/custom-count kernels, bounds checks, and sky handling. It uses FP32 arithmetic/depth for D3D11 compatibility and sets `XE_GTAO_USE_DEFAULT_CONSTANTS=0` so radius, falloff, sample distribution, and thin-occluder compensation use the upstream runtime parameter paths. The adapter calls the upstream depth prefilter, main pass, and denoiser directly. FuckTheseFX adds actor tile classification and a conservative neighboring-tile halo without changing Intel's estimator or depth pyramid. `Composite.shader` resolves primary visibility/depth, applies deferred AO, and composites actor AO directly from the filtered integer output.
