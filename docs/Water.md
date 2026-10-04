# Customs water surface

5. Custom Water / Mode defaults to Native. FTFX replaces the Customs v3 water material for main and optic cameras, preserving native geometry, draw order and property blocks. Other maps, water families and unsupported ABI/rendering paths retain native water. The replacement requires the pinned EFT 40743 D3D11/Gamma path.

The replacement changes only the initial normal block in the native shader arithmetic. Depth, absorption, Fresnel, roughness, foam, weather, thermal, reflection and G-buffer operations remain shared. It adds no passes, render targets, scene copies or geometry. The generated `WaterParity.cginc` and its extraction/generation scripts are required maintenance sources; extracted native assets stay under ignored `obj/` and are never packaged.

## Controls and live testing

Select F12 > Fuck These FX > Advanced > 5. Custom Water, then compare Native and FTFX from the same Customs viewpoint. Show replacement tint turns FTFX water magenta to confirm routing; keep it off for appearance comparisons.

| Control | Effect | Default | Range |
|---|---|---:|---:|
| Surface size | Larger surface patterns | 1 | 0.25–4 |
| Surface strength | Overall normal variation; zero flattens it | 1 | 0–2 |
| Fine ripple strength | Fine detail relative to broader structure | 1 | 0–4 |
| Animation speed | Future movement rate; zero freezes the current phase | 1 | 0–2 |

Controls apply live. Main and optic draws share one phase integrated from scaled delta time once per plugin Update; changing speed does not reposition the pattern. Native rain and foam retain their own clocks. Native bypasses these controls; saved values are preserved.

Check ordinary views, magnified optics, rain and night, including freeze/resume and live speed changes. Phase-one parity was accepted in live testing; the changed multiscale surface still needs appearance and performance acceptance. Fewer texture reads alone do not establish an FPS improvement, and changed normals affect downstream SSR.

## Normal model

Three normal reads use the existing authored textures. Decoding retains native RG/AG handling; bounded slopes replace component-wise detail multiplication. Rotated sampling rotates slopes back into world XZ. Native bumpiness and rain ripples remain downstream.

| Layer | Scale multiplier | Movement multiplier | Weight multiplier | Sample bias |
|---|---:|---:|---:|---:|
| Broad A | 0.45 | 1 | 0.65 | +0.5 |
| Medium B, rotated 67.5 degrees | 0.85 | 1 | 1 | +0.75 |
| Detail | 0.70 | 0.60 | 0.30 | +3 |

Weights normalize before footprint fading, so fading detail cannot amplify broad structure. Derivatives fade broad variation over 16–64 texels/pixel, medium over 8–32 and fine over 2–8. Texture dimensions come from the bound property-block textures; each camera's footprint preserves resolved optic detail. Texture bias is local to sampling, without mutating shared textures.

## Validation

`Test-Water.ps1` compares a native-normal baseline with the pinned native shader and exercises weather/HDR variants, unchanged depth/snow output, normal bands, footprints, clock continuity and material/buffer lifetime. `Test-WaterContracts.ps1` checks the exact native draw ABI, detach order and validated source/bundle identities. Build preparation and commands are in [Build and validation](BuildValidation.md). Historical design reviews and measurements are preserved in the local research archive described in [the cleanup record](SSR-cleanup-20261003.md).
