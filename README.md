# Fuck These FX

Independent controls for EFT's ambient occlusion, stochastic reflections, distant shadows, ambient highlight, vintage filter and Ultimate Bloom. Requires SPT 4.1 / EFT 40743, using Direct3D 11.

Open **F12 > Fuck These FX**:

- **Glass Culling Fix:** enabled by default, active only on Streets of Tarkov. Makes omitted glass follow native culling of vehicle parts, windows and enclosing building sections. Discovery starts once the raid player and owner are initialized, including the Start Raid waiting screen; disabling restores normal glass rendering. Inactive on all other maps. See [details](docs/GlassCulling.md).
- **Stochastic SSR:** off by default, R768 when enabled. Uses full-density GGX tracing, NRD REBLUR_SPECULAR denoising and native environment removal. Requires SSR enabled in the game; optics retain native SSR. Disable to restore vanilla SSR. Range, reconstruction and debug controls are under Advanced. Automatically bypassed with SSRSix. See [SSR behavior and defaults](docs/StochasticSSR.md).

  Advanced **Allow continued sky on all receivers** extends bounded sky recovery to puddles and other eligible reflective surfaces (on by default). Unresolved rays now retain receiver-colour fallback instead of copying the first rejected foreground object's colour. The depth separation slider remains configurable with its 200 m default; see [continuation behavior](docs/StochasticSSR.md).

- **Water Surface:** Native by default. FTFX changes surface normal structure on Customs v3 water for main and optic cameras. Size, strength, fine detail and animation speed apply live; other maps retain native water. See [water controls and validation](docs/Water.md).

- **XeGTAO:** enabled by default. Off restores native AO. The **Quality** slider selects 0 Low, 1 Medium, 2 High or 3 Ultra (default), independently of Tarkov's HBAO quality; graphics AO Off disables XeGTAO and its actor pass. The included CustomAmbient fix makes diffuse ambient and fill lighting respect AO, with foliage handling.
- **XeGTAO > Second Pass Enabled:** enabled by default, adding independent contact shading to hands, weapons and characters. Uses full-resolution sampling restricted to relevant actor regions.
- **Screen Space Contact GI:** experimental and off by default. Adds short-range diffuse color bounce from nearby lit surfaces. Requires FTFX XeGTAO and the main HDR deferred camera; supports Gamma and Linear color space. Starts at quarter resolution with eight samples; strength, radius, quarter resolution and other tuning are under Advanced. Actors, optics and transparent surfaces are excluded. Temporal accumulation and spatial filtering reduce noise; movement and thin geometry can still expose screen-space artifacts.
- **Distant Shadows:** enabled by default at **Half** resolution. Quarter looks softer; Full gives sharper edges. Disable to restore vanilla distant shadows. Advanced settings retain per-resolution filter counts (**25 / 12 / 8**) for both the main view and optics. A count of 25 retains native filtering.
- **Ambient Highlight:** off by default, removing the extra ambient glow on weapons, hands and selected objects. Enable to restore the native highlight.
- **Vintage Filter:** off by default, disabling the vintage camera filter. Enable to restore effects disabled by Fuck These FX. Includes VintageBeGone's behavior; remove the standalone VintageBeGone plugin to let this toggle control the filter.
- **Sharpen Filter:** on by default, retaining Tarkov's CC_Sharpen. Off bypasses the component, including its combined desaturation. Enabling restores only components disabled by FTFX.
- **Prism Exposure:** **Resolution percent = 100** by default (range **1–100**). Scales only the square exposure sampling grid: at 4K, 100% uses 1920×1920 and 50% uses 960×960. The scene and tone mapping retain their original resolution. Lower settings can change how small bright details affect adaptation. See [post-effect investigation and validation](docs/PostEffects.md).
- **Inactive Effects:** **Skip neutral passes** is on by default. Removes neutral status-effect copies inside the current upscaler image chain. Zero-aperture disc DoF keeps its native alpha/CoC write and skips blur allocations/compositing. Active effects and unsupported paths retain native rendering. See [conditions and validation](docs/PostEffects.md).
- **Ultimate Bloom:** enabled by default with **Bloom input scale = 0.67** (range **0.25-1**) and **Use native resolution = false**. Lower scale softens glow and flares while keeping the scene sharp. Disable to restore vanilla bloom.

**Bent normals:** optional and off by default under **Advanced > XeGTAO > Custom Ambient Fix Bent normals**. Steers directional ambient lighting using XeGTAO; requires XeGTAO and the CustomAmbient fix. Adds GPU work.

To tune Screen Space Contact GI, enable it and adjust **Advanced > Screen Space Contact GI > Strength** (0–4; default 3). **Temporal accumulation** and **Spatial filter** are on by default; toggle them for comparison. **History frames** defaults to 16 (4–30); higher values smooth more but react more slowly. Select **Advanced > Debug > View = ScreenSpaceContactGI** to see only the added scene RGB after filtering, strength, albedo, limits, visibility and fades, before fog/tonemap. **Screen Space Contact GI gain** (default 16x) amplifies only the display. Black means zero, cyan unavailable, magenta mismatched inputs. Select **Normal** to return to the scene and before benchmarking.

For persistent Screen Space Contact GI noise, select **Debug > View = ScreenSpaceContactGIHistory**: green means retained history; red depth/position rejection; yellow normal rejection; cyan lighting rejection; orange fresh/offscreen history; blue invalid surfaces; magenta strongly limited history. This view requires Temporal accumulation and ignores display gain.

Advanced settings retain XeGTAO tuning, CustomAmbient strength/foliage controls, actor radius/strength/response/cutoff, and the AO debug views: Depth, BeforeBlur, AfterBlur, CombinedAO, SecondPassAO and BentNormals. BentNormals shows world-space directions, or gray when inactive. Select Normal to restore the scene; Depth has start/range controls. BentSHDifference shows the amplified SH change before the interior blend; BentLightingDifference shows the remaining diffuse change after the blend, albedo and AO. Both capture the actual CustomAmbient draw. Adjust Bent difference gain (default 32x); black means zero change, cyan no capture, red bent shader off, magenta missing inputs.

All controls apply live. AO Replacer is not required. If it is installed, Fuck These FX bypasses its entire AO/CustomAmbient stack and diagnostics while its other FX remain available. Remove AOReplacer to use Fuck These FX's XeGTAO.

Close the game and extract the ZIP into the SPT directory. Remove the standalone FTFX SSR Prototype when upgrading. Prototype settings are not imported. Settings are saved in `BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg`; AOReplacer settings are not imported.

Build with `scripts/Build-Package.ps1`. Shader and hook validation uses Unity 2022.3.43f1; see [distant shadows](docs/DistantShadows.md). Run `scripts/Test-BloomResolution.ps1` for bloom validation. Automated checks do not establish live-game image quality or FPS gains.

See [occlusion implementation and validation](docs/Occlusion.md). XeGTAO uses [Intel's MIT-licensed implementation](https://github.com/GameTechDev/XeGTAO); its license is included in the package.

AmbientShares separates diffuse/fill luminance: red is directional SH, green upward SH, blue fill. AmbientDirectional, AmbientUpward and AmbientFill show their final contributions with a shared Ambient contribution gain (default 1). AmbientInteriorTransmission shows how much directional SH survives the interior blend and visibility, before albedo and AO (black 0%, white 100%). These views work with bent normals on or off and exclude reflections/direct lights. Shares/transmission ignore gain. Use Normal before measuring performance.

Stochastic SSR retains adapted AMD GGX sampling and blue noise with custom traversal and Tarkov material composition. The AMD reflections denoiser has been removed. Sampling licenses and provenance remain under `Licenses/StochasticSSR`.

See [Build and validation](docs/BuildValidation.md) for native dependencies, shader rebuilds, private replay inputs and package checks.
