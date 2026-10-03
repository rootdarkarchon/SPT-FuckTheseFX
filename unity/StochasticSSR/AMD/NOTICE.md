# AMD SSSR sampling attribution

Pinned upstream revisions and blob hashes are recorded in UPSTREAM.json. FTFX retains SSSR GGX VNDF direction sampling and the blue-noise sequence. Hi-Z traversal, temporal reconstruction and EFT integration are local implementations.

- Sampling.hlsl extracts the VNDF, TBN and reflection-direction functions from the pinned Intersect.hlsl, retaining its copyright and MIT notice.
- BlueNoise.bytes is the 128x128 two-channel RG8 table derived from the pinned 1spp sampler arrays. Runtime converts (byte+0.5)/256 and applies the upstream frame rotation.
- The AMD reflections denoiser has been removed. Historical integration audits remain repository documentation, not shipped runtime code.

The retained sampling license is SSSR-LICENSE.txt.
