# Distant shadows

In **F12 > Fuck These FX > 1. Vanilla Optimizations**, **Distant Shadows Resolution Fix** defaults to true and **Distant Shadows Resolution** defaults to **Half**. Select Quarter, Half or Full; higher resolutions sharpen shadow edges. Disable to restore the game or Shadow Flicker Fix resolution and native filtering. Changes apply without reloading the map.

Advanced **Distant Shadows Adjust Filter Samples** enables the per-resolution filter controls while distant-shadow adjustments are enabled. Turn on **Advanced settings** to set the sample count for each mask resolution:

| EFT mask resolution | Default filter samples per shadow map |
|---|---:|
| QUARTER | 25 |
| HALF | 12 |
| FULL | 8 |

Each accepts 1–25. **25 uses EFT's original draw**, and disabling the feature uses native rendering for every resolution. Settings apply on the next mask rebuild during a raid. Reduced counts trade shadow smoothness and stability for GPU work; these defaults require visual evaluation in game.

The sample count follows the actual `resState` supplied to `DistantShadow.SetupComputeShadowMaskCommandBuffer`. Disabling distant-shadow adjustments restores the last observed external resolution. FuckTheseFX does not change Shadow Flicker Fix's configuration or require it as a dependency. Sample count and filter positions are recorded into the command buffer immediately before the draw; different recorded cameras/settings cannot overwrite them through a shared material value.

Main-camera and optic-camera mask pass 0 use the replacement for reduced sample counts. Optics retain EFT's native Halton offsets and uniform weights. EFT retains allocation at the selected resolution, depth reduction, auxiliary mask generation, upscale, blur, shadow-map updates, and both 50-step parallax searches. The replacement preserves current/previous-map blending and Gaussian/uniform filtering. Adaptive bias, multiprojection, PCF-shift variants, PCF-disabled rendering, and foreign mask shaders retain native filtering. Missing assets, unsupported installed shader assets, or a changed hook contract also retain native filtering; the resolution control operates independently.

With two maps and parallax active, filter counts 25/12/8 correspond to 150/124/116 shadow-depth samples per pixel. This count alone does not predict frame rate. A limiter, CPU cost, or other rendering work can hide GPU savings.

## Build and validation

Run `scripts/Extract-DistantShadowReference.py` with Python 3.11+ and UnityPy against the pinned EFT 40743 installation. It uses the existing `Assets/distantshadows` bundle as a container template and writes an installed native shader reference under `obj/DistantShadowValidation` for local validation only. Do not distribute this reference bundle.

Run `scripts/Build-DistantShadows.ps1` with Unity 2022.3.43f1, then `scripts/Build-Package.ps1`. The bundle contains only the replacement shader. The D3D11 fixture compares 25-sample output with the installed native shader across parallax, blending, Gaussian/uniform filtering, full/low-resolution depth, and odd sizes; reduced counts are checked for finite bounded output. A small synchronized batch experiment records relative cost, including CPU submission and readback, and is not reported as live GPU timing.

For a live comparison, keep camera/view/weather and mask resolution fixed, compare **25** against **8** or **12** for that resolution, then switch QUARTER/HALF/FULL to check selection. Verify the research capture contains `Hidden/FuckTheseFX/DistantShadowSampling` for reduced main-camera and optic draws and the native shader for 25/off. Inspect shadow edges during camera motion, changing sunlight, and scoped views. Synthetic validation does not establish live image quality or FPS gains.
