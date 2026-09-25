# Fuck These FX

Independent controls for EFT's ambient occlusion, distant shadows, ambient highlight and Ultimate Bloom. Requires SPT 4.1 / EFT 40743, using Direct3D 11.

Open **F12 > Fuck These FX**:

- **XeGTAO:** enabled by default. Off restores native AO. Quality follows the game's AO setting; graphics AO Off disables XeGTAO and its actor pass. The included CustomAmbient fix makes diffuse ambient and fill lighting respect AO, with foliage handling.
- **Actor second AO pass:** enabled by default, adding independent contact shading to hands, weapons and characters. Uses full-resolution sampling restricted to relevant actor regions.
- **Distant Shadows:** enabled by default at **Half** resolution. Quarter looks softer; Full gives sharper edges. Disable to restore vanilla distant shadows. Advanced settings retain per-resolution filter counts (**25 / 12 / 8**) for both the main view and optics. A count of 25 retains native filtering.
- **Ambient Highlight:** off by default, removing the extra ambient glow on weapons, hands and selected objects. Enable to restore the native highlight.
- **Ultimate Bloom:** enabled by default with **Bloom input scale = 0.67** (range **0.25-1**) and **Use native resolution = false**. Lower scale softens glow and flares while keeping the scene sharp. Disable to restore vanilla bloom.

Advanced settings retain XeGTAO tuning, CustomAmbient strength/foliage controls, actor radius/strength/response/cutoff, and the AO debug views: Depth, BeforeBlur, AfterBlur, CombinedAO and SecondPassAO. Select Normal to restore the scene; Depth has start/range controls.

All controls apply live. AO Replacer is not required. If it is installed, Fuck These FX bypasses its entire AO/CustomAmbient stack and diagnostics while its other FX remain available. Remove AOReplacer to use Fuck These FX's XeGTAO.

Close the game and extract the ZIP into the SPT directory. Settings are saved in `BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg`; AOReplacer settings are not imported.

Build with `scripts/Build-Package.ps1`. Shader and hook validation uses Unity 2022.3.43f1; see [distant shadows](docs/DistantShadows.md). Run `scripts/Test-BloomResolution.ps1` for bloom validation. Automated checks do not establish live-game image quality or FPS gains.

See [occlusion implementation and validation](docs/Occlusion.md). XeGTAO uses [Intel's MIT-licensed implementation](https://github.com/GameTechDev/XeGTAO); its license is included in the package.
