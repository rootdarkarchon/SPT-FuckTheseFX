# Fuck These FX (FTFX)

FTFX gives you control over Tarkov's lighting and post-processing in **SPT 4.1**. Keep a familiar vanilla look with rendering optimizations, or enable improved ambient occlusion, reflections and contact lighting.

## Features

- **Vanilla rendering controls:** toggle the ambient highlight, vintage and sharpen filters; adjust bloom and exposure sampling; skip unnecessary passes when supported effects are inactive.
- **Distant shadows:** choose shadow resolution and filtering to balance detail, smoothness and GPU work.
- **XeGTAO ambient occlusion:** replace native AO, with separate actor shading and controls for ambient and foliage lighting.
- **Stochastic GGX reflections:** replace main-camera screen-space reflections with NRD-denoised reflections. Optics keep native SSR.
- **Screen Space Contact Global Illumination (SSCGI):** add short-range colour bounce from nearby surfaces.
- **Customs water:** optional surface and ripple controls for supported Customs water, including optic views.

The default **FTFX Vanilla+** settings keep XeGTAO, FTFX reflections and SSCGI off. Performance and appearance depend on the scene, hardware and settings.

## Requirements

- **SPT 4.1 / EFT build 40743**
- **Direct3D 11**
- **BepInEx ConfigurationManager** to access settings through F12

## Installation

1. Close the game.
2. Download a release ZIP from [GitHub Releases](https://github.com/rootdarkarchon/SPT-FuckTheseFX/releases).
3. Extract it into your SPT installation folder, merging the `BepInEx` folder.
4. Start the game and open **F12 > Fuck These FX**.

When updating, replace the complete `BepInEx/plugins/FuckTheseFX` folder so the DLLs and shader assets match. Keep your configuration file to retain saved settings. Remove the standalone **FTFX SSR Prototype** if you used it previously.

## Choose a preset

| Preset | What it changes |
| --- | --- |
| **Vanilla** | Restores the vanilla baseline controls and disables FTFX fixes and replacement effects. |
| **FTFX Vanilla+ (Default)** | Keeps the vanilla baseline appearance with minor rendering optimizations. |
| **FTFX HQ Visuals** | Enables XeGTAO and FTFX reflections, with ambient highlight, vintage and sharpen off. |
| **FTFX HQ+ Visuals** | Uses HQ Visuals and additionally enables SSCGI. |

A normal click changes only the preset's listed controls and preserves your other tuning. **Hold Shift while clicking** to restore all shipped defaults before applying the preset. This also resets advanced settings and debug views.

Settings apply live and save to `BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg`. Enable **Advanced Settings** in ConfigurationManager for additional tuning. See the [configuration reference](docs/Configuration.md) for the full preset comparison and every setting.

## Using the replacement effects

- **XeGTAO:** keep AO enabled in Tarkov's graphics settings. Setting game AO to Off also disables FTFX's AO and actor pass.
- **FTFX reflections:** keep SSR enabled in Tarkov's graphics settings. The replacement runs on supported main cameras; optics retain native SSR.
- **SSCGI:** requires XeGTAO and a supported main HDR deferred camera. It can run with FTFX reflections off.
- **Custom water:** select **Advanced Settings > 5. Custom Water > Mode > FTFX**. Native is the default; other maps keep their native water.
- **Glass culling fix:** applies only to Streets of Tarkov.

Turn debug views back to **Normal** and disable captures before comparing performance. Older configuration keys are not migrated; Shift-click **FTFX Vanilla+ (Default)** when you want the current defaults.

## Documentation and support

The [documentation index](docs/README.md) links to effect guides, troubleshooting comparisons and developer build instructions.

For a bug report, include your SPT/EFT and FTFX versions, map, graphics settings, selected preset or changed settings, and steps to reproduce. Attach the relevant BepInEx log and screenshots when they help show the issue. Submit reports through [GitHub Issues](https://github.com/rootdarkarchon/SPT-FuckTheseFX/issues).

## License and credits

FTFX's own code is available under the [MIT License](LICENSE).

FTFX uses Intel's XeGTAO, AMD's SSSR sampling and blue noise, and NVIDIA's NRD denoiser. These components retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md). The runtime package includes the applicable license texts.
