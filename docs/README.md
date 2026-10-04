# FTFX documentation

For installation, requirements and preset selection, start with the [main README](../README.md).

## Settings and effects

| Guide | Contents |
| --- | --- |
| [Configuration](Configuration.md) | Preset comparison, settings, defaults and ranges. |
| [Ambient occlusion](Occlusion.md) | XeGTAO, actor AO, ambient lighting and foliage. |
| [Contact global illumination](ColorBleed.md) | SSCGI controls, colour bounce and comparison guidance. |
| [Reflections](StochasticSSR.md) | Stochastic GGX SSR, denoising, fallback and limitations. |
| [Distant shadows](DistantShadows.md) | Resolution and filter controls. |
| [Post-processing](PostEffects.md) | Exposure, sharpen and inactive-effect optimizations. |
| [Customs water](Water.md) | Optional water surface controls and supported rendering paths. |

These guides include implementation and validation details for readers who want to understand the rendering changes.

## Troubleshooting comparisons

- [Foliage lighting](FoliageLightingTest.md): compare lighting stages and collect a diagnostic capture.
- [Reflection occlusion](ReflectionOcclusionTest.md): compare smooth-surface reflections while checking diffuse AO.

Keep the camera, weather and other graphics settings fixed for comparisons. Return debug views to Normal and turn captures off before measuring performance.

## Development

- [Build and validation](BuildValidation.md): toolchain, pinned native sources, shader validation and canonical packaging.
- [SSR rendering contract](SSR-reflection-contract.md): signal mapping, composition and resource ownership.
- [Third-party notices](../THIRD_PARTY_NOTICES.md): licenses and upstream attribution.

## Historical records

These records describe previous implementations and validation runs. Use the current guides above for installation, defaults and supported behavior.

- [Retired SSR implementations](history/SSR-declined-implementations-20261003.md)
- [SSR and water cleanup record](history/SSR-cleanup-20261003.md)

The cleanup record also identifies a local research archive that is excluded from Git and runtime packages. That archive is not available in a fresh GitHub checkout.
