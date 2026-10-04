# Fuck These FX

Independent controls for EFT's ambient occlusion, stochastic reflections, distant shadows, ambient highlight, vintage filter and Ultimate Bloom. Requires SPT 4.1 / EFT 40743, using Direct3D 11.

Open **F12 > Fuck These FX**. See the [complete configuration table](docs/Configuration.md) for all 94 settings, defaults, types, ranges and advanced flags.

- **0. Presets:** **Vanilla** applies vanilla rendering controls; **FTFX Vanilla+ (Default)** applies eleven controls for the vanilla baseline and minor performance fixes; **FTFX HQ Visuals** uses the same controls with ambient highlight/sharpen/vintage off and XeGTAO/SSR on. The three selective presets preserve all other values. Buttons have hover descriptions, apply live and save once. Hold Shift while clicking any preset to restore all shipped defaults before applying it.
- **1. Vanilla Optimizations:** contains Streets glass culling, additional ambient highlight, vintage/sharpen, neutral effect passes, Prism auto exposure, Ultimate Bloom input scale and distant-shadow resolution/filtering. Prism defaults to **66%**; bloom defaults to **50%**. Bloom at 100% uses the original input without a reduced copy. Glass culling remains Streets-only.
- **2. XeGTAO:** disabled by default, with Quality 3 (Ultra) and an independent actor second pass. Custom ambient strength defaults to 60%, foliage ambient AO to 65%, and additional foliage AO to 20%. AO fade defaults to 100/250 metres, with limits 0–250 / 0–500. Game AO Off disables the replacement and actor pass. Optional bent normals remain off by default. See [occlusion](docs/Occlusion.md) and [foliage tests](docs/FoliageLightingTest.md).
- **3. Screen Space Contact Global Illumination:** experimental, off by default; short-range diffuse colour bounce on the main HDR deferred camera. Defaults to quarter resolution, eight samples, strength 3 and sixteen history frames. Maximum bounce fraction is an integer percentage: default 20%, range 1–50%. See [contact GI](docs/ColorBleed.md).
- **4. Stochastic GGX SSR:** off by default, R768 and eight history frames when enabled. Full-density GGX tracing, NRD REBLUR_SPECULAR denoising and native environment removal. SSR must be enabled in game; optics retain native SSR. Bounded continued sky recovery always covers all eligible receivers; source-normal rejection remains bypassed. There is no receiver-distance limit. Reconstruction and diagnostics remain advanced; SSR resolution is a normal setting. See [SSR behavior](docs/StochasticSSR.md).
- **5. Custom Water:** all settings are advanced. Native by default; FTFX changes Customs v3 water normals on main and optic cameras. Surface size/strength, fine ripple strength and animation speed remain floating-point scales with their original ranges. Other maps retain native water. See [water](docs/Water.md).
- **9. Debug:** contains AO, ambient, contact GI and foliage views. View = Normal restores the scene. Diagnostic views/captures add work; return to Normal before benchmarking.

Fractional controls now use integer percentage values and names ending in **(%)**. Physical distances, luminance, gains, and scales allowing values above 1 retain their units. No previous configuration keys are imported or cleaned up; only the new layout is bound.

All controls apply live. XeGTAO, CustomAmbient, Contact GI, bloom and stochastic SSR follow FTFX settings without checking for AOReplacer or SSRSix.

Close the game and extract the ZIP into the SPT directory. Remove the standalone FTFX SSR Prototype when upgrading. Prototype settings are not imported. Settings are saved in `BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg`; AOReplacer settings are not imported.

Build with `scripts/Build-Package.ps1`. Shader and hook validation uses Unity 2022.3.43f1; see [distant shadows](docs/DistantShadows.md). Run `scripts/Test-BloomResolution.ps1` for bloom validation. Automated checks do not establish live-game image quality or FPS gains.

See [occlusion implementation and validation](docs/Occlusion.md). XeGTAO uses [Intel's MIT-licensed implementation](https://github.com/GameTechDev/XeGTAO); its license is included in the package.

AmbientShares separates diffuse/fill luminance: red is directional SH, green upward SH, blue fill. AmbientDirectional, AmbientUpward and AmbientFill show their final contributions with a shared Ambient contribution gain (default 1). AmbientInteriorTransmission shows how much directional SH survives the interior blend and visibility, before albedo and AO (black 0%, white 100%). These views work with bent normals on or off and exclude reflections/direct lights. Shares/transmission ignore gain. Use Normal before measuring performance.

Stochastic SSR retains adapted AMD GGX sampling and blue noise with custom traversal and Tarkov material composition. The AMD reflections denoiser has been removed. Sampling licenses and provenance remain under `Licenses/StochasticSSR`.

See [Build and validation](docs/BuildValidation.md) for native dependencies, shader rebuilds, private replay inputs and package checks.
