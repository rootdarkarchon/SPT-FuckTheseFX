# Fuck These FX

Independent controls for EFT's distant shadows, ambient highlight and Ultimate Bloom. Requires SPT 4.1 / EFT 40743, using Direct3D 11.

Open **F12 > Fuck These FX**:

- **Distant Shadows:** enabled by default at **Half** resolution. Quarter looks softer; Full gives sharper edges. Disable to restore vanilla distant shadows. Advanced settings retain per-resolution filter counts (**25 / 12 / 8**) for both the main view and optics. A count of 25 retains native filtering.
- **Ambient Highlight:** off by default, removing the extra ambient glow on weapons, hands and selected objects. Enable to restore the native highlight.
- **Ultimate Bloom:** enabled by default with **Bloom input scale = 0.67** (range **0.25-1**) and **Use native resolution = false**. Lower scale softens glow and flares while keeping the scene sharp. Disable to restore vanilla bloom.

All controls apply live without reloading the map. AO Replacer is not required.

Close the game and extract the ZIP into the SPT directory. If upgrading from AO Replacer 1.3.3 with these controls, also install AO Replacer 1.3.4 to remove the old hooks. Existing FX preferences are imported from its config on the first launch; subsequent changes are saved in `BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg`.

Build with `scripts/Build-Package.ps1`. Shader and hook validation uses Unity 2022.3.43f1; see [distant shadows](docs/DistantShadows.md). Run `scripts/Test-BloomResolution.ps1` for bloom validation. Automated checks do not establish live-game image quality or FPS gains.
