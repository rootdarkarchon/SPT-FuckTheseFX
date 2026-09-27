# AMD reflection adaptation

Pinned upstream revisions and Git blob hashes are in UPSTREAM.json. This integrates the original 2021 standalone FidelityFX Reflection Denoiser and SSSR sampling code, not the current SDK backend or a complete SSSR port.

- Reflection denoising uses the upstream reproject, prefilter and resolve-temporal headers. Local header changes: replace an FXC-incompatible binary integer literal; fix RoundUp8 for odd extents; initialize reprojection out parameters on early rejection. The adapter maps min16float arithmetic to float for Unity D3D11 (required to avoid zero shared-memory radiance on the tested compiler); explicit 16-bit packing remains upstream.
- FidelityDenoiser.compute supplies Unity resource, coordinate and motion callbacks, clamps loads/guards stores for partial tiles, resets history, blends shading and geometry normals only for history validation, and additionally validates old reflected-hit depth. These are documented integration adaptations, not unmodified AMD behavior.
- Sampling.hlsl extracts SampleGGXVNDF, TBN and reflection-direction functions from pinned Intersect.hlsl. SampleRandomVector2D uses the pinned PrepareBlueNoiseTexture frame sequence. HiZ remains our custom traversal. Stochastic no longer applies the native smoothness direction bias.
- BlueNoise.bytes is the 128x128 two-channel RG8 base table derived from the pinned 1spp sampler arrays: for pixel i and dimension d in {0,1}, byte = sobol[d + ranking[d+i*8]*256] XOR scrambling[d+i*8]. Runtime converts (byte+0.5)/256 and applies the upstream golden-ratio frame rotation. No artistic image asset is involved.
- StochasticNative Custom filters the exact native hit-resolve signal, including misses, while preserving resolve alpha and excluded receivers. Native raymarch and native temporal filtering are replaced; native hit response, Gaussian material mips and composition remain. Only legacy comparison modes retain a complete native SSR baseline.
- Optional receiver classification, group-local ray compaction and GPU indirect dispatch are a local SM5 implementation inspired by SSSR scheduling, not copied AMD wave-intrinsic kernels. Pixel-based sampling and every eligible ray are preserved. Conservative local adaptive sampling can reduce stable quads to two rays, with native-resolved colour reuse and periodic full refreshes. AMD indirect denoiser tile lists remain unimplemented. Scalar denoiser resources use RHalf (RFloat fallback); scalar histories swap rather than copy. Reflection and guide histories can also swap at the next trace, keeping current-frame diagnostic buffers intact. Reprojection generates per-tile filter-needed bits; prefilter and temporal kernels can take their exact copy-only path without neighborhood loads. Half packing and padded-lane synchronization are retained. This is local tile fast-path scheduling, not AMD indirect tile-list dispatch. Upstream denoising math is unchanged.

AMD's MIT licenses are included in LICENSE.txt and SSSR-LICENSE.txt. The denoiser common header also includes this Playdead notice for ClipAABB:

Copyright (c) [2015] [Playdead]

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
