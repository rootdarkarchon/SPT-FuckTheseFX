# Live foliage comparison

Install FuckTheseFX **1.5.7**, including its matching `Assets/occlusion` bundle, and restart once. All controls below then work during the same raid; no capture files or further restarts are needed.

1. Face the tree without aiming through a scope. Keep XeGTAO, game AO and Custom Ambient Fix enabled, with ambient strength above zero. The live test requires HDR.
2. Open Configuration Manager, enable Advanced settings, and choose **9. Debug / View = FoliageLive**. The **Foliage live preview = Scene** default shows normal gameplay with a status overlay.
3. The persistent **2. XeGTAO / Foliage extra AO strength (%)** setting defaults to **20%** and also drives this comparison. Split comparison defaults on: the left side uses current rendering, the right adds experimental AO. Move **Foliage live divider (%)** across the same tree, or disable split to apply the adjustment across the scene. Strength zero restores the baseline.
4. To isolate lighting, leave extra AO at zero and toggle **Foliage live bypass Contact GI** and **Foliage live bypass opaque colour** separately. The opaque bypass replaces the combined colour output of the native `Opaque Only Post-processing` buffer with its input; it includes multiple effects, such as fog and SSR. Native commands still execute. Both bypasses affect both sides of the split. Saved GI and ambient settings remain intact.
5. Optional: choose **BeforeAmbient**, **AfterAmbient**, **BeforeOpaque**, **AfterOpaque** or **AfterTransparency** in **Foliage live preview**. **AmbientDifference** and **OpaqueDifference** show absolute RGB changes between the respective stages; try display gain 8. **ExperimentalMask** shows the surfaces eligible for extra AO. These are live GPU previews, not exports.
6. Choose **9. Debug / View = Normal** to finish. Both bypasses reset; extra AO remains at the saved XeGTAO strength. The diagnostic copies and targets are released on the next camera render. The bypasses are also reset at plugin startup.

The experiment is main-camera only; optic cameras keep their normal rendering. Extra AO also works in Normal view without the diagnostic copies. It multiplies surface colour immediately before native opaque post-processing, so fog is applied afterward. It uses the existing AO distance fade and excludes sky and actor stencil pixels. Its zero-alpha material mask is a compatibility class, not guaranteed foliage identification, so check **ExperimentalMask** for unwanted receivers. It darkens surface lighting before native fog is blended over it. The extra AO slider persists across view changes and restarts. The shared distance fade defaults to 100/250 metres.

**BeforeOpaque** includes extra foliage AO; **AfterOpaque** shows native output before the bypass. Stage previews are raw camera buffers before final grading/upscaling, not calibrated final screenshots. Absolute difference is not a measurement of lost AO. Cyan can indicate a missing stage; the overlay reports an unavailable or ambiguous opaque buffer, in which case opaque bypass and extra AO are disabled. Diagnostic copies add GPU work; return to Normal before measuring performance.

# Optional foliage lighting capture

Use FuckTheseFX 1.5.4 or later, including its matching `Assets/occlusion` bundle.

1. In the raid, face the affected tree without aiming through a scope. Keep XeGTAO and Custom Ambient Fix enabled. Set Custom Ambient Fix Strength and Foliage AO strength to 100% for this capture; keep your other settings as they were when you observed the issue.
2. In Advanced settings, select **9. Debug / View = FoliageLightingCapture**. Hold the camera still. The capture saves automatically after three main-camera renders, briefly pauses while writing files, and returns the view to Normal. No hotkey or repeated slider comparisons are needed.
3. Send the newest complete folder under `BepInEx/FuckTheseFX/FoliageDiagnostics`, preferably zipped. A normal screenshot of the same view helps locate the tree. The game log records the saved path. `COMPLETE.txt` confirms that all files were written.

The capture contains ten PNG previews, eight raw floating-point EXRs, and `capture.txt` with settings, camera information, command-buffer order, pixel-status counts and encoding details. Settings are not changed except returning Debug View to Normal. Existing actor AO and Screen Space Contact GI remain active if enabled. The capture targets and extra stage copies are removed on the next normal camera render.

## Interpreting the files

- **effective-ao.png** shows the multiplier consumed by the actual CustomAmbient fragment: black is fully occluded, white is unoccluded.
- **supplied-ao.png** shows the decoded primary AO sampled by that same fragment. At full strengths these should agree on zero-alpha materials.
- **zero-alpha-materials.png** identifies the materials routed through the foliage compatibility path. This includes some non-foliage materials.
- **ambient-unoccluded.png** and **ambient-removed.png** show diffuse plus fill before scalar AO, and the amount removed by scalar AO. Both previews use the same 4x gain; raw EXRs have no gain or clipping. Bent-normal lighting is held fixed, so these are not a complete bent-on versus bent-off comparison.
- **before-ambient** and **after-ambient** surround the native CustomAmbient draw. Additional captures are taken at BeforeImageEffectsOpaque, AfterForwardAlpha and AfterEverything.
- Cyan in AO/ambient previews means no ambient fragment wrote the pixel. Magenta in AO previews means a required input was missing. Black is therefore distinguishable from a missing capture. Raw `ao-inputs.exr` alpha encodes these statuses; see `capture.txt`.

The stage images are observations of the same running frame, not separate AO-on/off renders. They can identify low ambient contribution or large later changes, but do not by themselves prove that a later pass erased AO. HDR/LDR encodings and later fog, grading and upscaling must be accounted for before comparing numeric values. In particular, the AfterEverything image is the camera target, not a verified final DLSS/SSAA display tap. If multiple CustomAmbient draws occur, the ambient captures describe the last draw at each written pixel, not their sum.

The optional capture is diagnostic instrumentation; it preserves the configured foliage extra AO. Synthetic D3D11 validation does not establish live EFT visual behavior.
