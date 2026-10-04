# Configuration

F12 > Fuck These FX exposes 98 real settings and one preset drawer. Numeric ranges are inclusive. Names ending in (%) store literal integer percentages; 20 means 20%, even when the maximum is 50.

Groups appear in numeric order: Presets, Vanilla Optimizations, XeGTAO, Screen Space Contact Global Illumination, Stochastic GGX SSR, Custom Water, Debug. ConfigurationManager 18.4 preserves registration order rather than sorting section names, so the plugin registers each section in this order before binding the remaining entries.

**0. Presets**

The first row contains **Vanilla → FTFX Vanilla+ (Default)**; the second contains **FTFX HQ Visuals** and **FTFX HQ+ Visuals** side by side. Hover over each button for its description. All buttons apply synchronously and save once. The backing UI entry is not a gameplay setting.

The following note appears above the buttons:

> Hold Shift while pressing on a preset button to restore full defaults. FTFX provides more settings than shown when enabling "Advanced Settings", the current advanced settings, presets and defaults are tuned and authored for visuals and performance.

**Vanilla**, **FTFX Vanilla+ (Default)**, **FTFX HQ Visuals**, and **FTFX HQ+ Visuals** change only the eleven controls below on a normal click. All other values, including SSR history, water mode, tuning, and diagnostics, are preserved by these selective presets.

Holding Shift while clicking any preset restores every shipped default first, then applies the selected preset's controls. For example, Shift-clicking FTFX HQ Visuals resets advanced settings, water mode, diagnostics and SSR history before applying HQ visuals. The reset and overrides form one operation and save once. HQ+ uses the HQ Visuals baseline and additionally enables SSCGI; its tooltip is "FTFX HQ Visuals, but with all effects enabled." For now SSCGI is its only additional override.

| Setting in Vanilla Optimizations (unless noted) | Vanilla | FTFX Vanilla+ | FTFX HQ Visuals | FTFX HQ+ Visuals |
|---|---|---|---|---|
| Additional Ambient Highlight | true | true | false | false |
| Streets of Tarkov Glass Culling Fix | false | true | true | true |
| Skip Neutral Effects Passes | false | true | true | true |
| Prism Auto Exposure Resolution (%) | 100 | 66 | 66 | 66 |
| Sharpen Filter | true | true | false | false |
| Vintage Filter | true | true | false | false |
| Ultimate Bloom Input Scale (%) | 100 | 50 | 50 | 50 |
| Distant Shadows Resolution Fix | false | true | true | true |
| 2. XeGTAO / Enabled | false | false | true | true |
| 3. Screen Space Contact Global Illumination / Enabled | false | false | false | true |
| 4. Stochastic GGX SSR / Enabled | false | false | true | true |

Existing configuration sections are not migrated or explicitly cleaned. Changed shipped defaults do not overwrite saved values; Shift-click FTFX Vanilla+ (Default) to restore the new defaults.

**1. Vanilla Optimizations**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| Streets of Tarkov Glass Culling Fix | true | false, true | bool | No |
| Additional Ambient Highlight | true | false, true | bool | No |
| Vintage Filter | true | false, true | bool | No |
| Sharpen Filter | true | false, true | bool | No |
| Skip Neutral Effects Passes | true | false, true | bool | No |
| Prism Auto Exposure Resolution (%) | 66 | 1 – 100 | int | No |
| Ultimate Bloom Input Scale (%) | 50 | 25 – 100 | int | No |
| Distant Shadows Resolution Fix | true | false, true | bool | No |
| Distant Shadows Resolution | Half | `DistantShadowMaskResolution` choices below | enum | No |
| Distant Shadows Adjust Filter Samples | true | false, true | bool | Yes |
| Distant Shadows Quarter Filter Samples | 25 | 1 – 25 | int | Yes |
| Distant Shadows Half Filter Samples | 12 | 1 – 25 | int | Yes |
| Distant Shadows Full Filter Samples | 8 | 1 – 25 | int | Yes |

**2. XeGTAO**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| Enabled | false | false, true | bool | No |
| Quality | 3 | 0 – 3 | int | No |
| EffectRadius | 1 | 0.01 – 5 | float | Yes |
| RadiusMultiplier | 1.457 | 0.1 – 3 | float | Yes |
| FalloffRange (%) | 62 | 5 – 100 | int | Yes |
| SampleDistributionPower | 2.5 | 0.5 – 4 | float | Yes |
| SliceCount | 0 | 0 – 9 | int | Yes |
| SamplesPerSlice | 0 | 0 – 8 | int | Yes |
| ThicknessHeuristic (%) | 0 | 0 – 100 | int | Yes |
| Denoise passes | 2 | 0 – 3 | int | Yes |
| DenoiseSharpness | 0.4 | 0.1 – 10 | float | Yes |
| FinalPower | 1.5 | 0.5 – 5 | float | Yes |
| Distance fade start | 100 | 0 – 250 | float | Yes |
| Distance fade end | 250 | 0 – 500 | float | Yes |
| UseNormals | true | false, true | bool | Yes |
| Custom Ambient Fix Enabled | true | false, true | bool | Yes |
| Reflection occlusion fix | true | false, true | bool | Yes |
| Custom Ambient Fix Bent normals | false | false, true | bool | Yes |
| Custom Ambient Fix Strength (%) | 60 | 0 – 100 | int | Yes |
| Custom Ambient Fix Foliage AO strength (%) | 65 | 0 – 100 | int | Yes |
| Second Pass Enabled | true | false, true | bool | No |
| Second Pass Radius | 0.3 | 0.01 – 2 | float | Yes |
| Second Pass Strength | 1.5 | 0 – 4 | float | Yes |
| Second Pass Response | 0.6 | 0.25 – 2 | float | Yes |
| Second Pass Distance cutoff | 10 | 0.1 – 100 | float | Yes |
| Foliage extra AO strength (%) | 20 | 0 – 100 | int | Yes |

**3. Screen Space Contact Global Illumination**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| Enabled | false | false, true | bool | No |
| Strength | 6 | 0 – 10 | float | Yes |
| Resolution (%) | 15 | 10 – 50 | int | Yes |
| Reconstruction cutoff | 0 | 0 – 0.1 (linear added luminance) | float | Yes |
| Temporal accumulation | true | false, true | bool | Yes |
| History frames | 16 | 4 – 63 | int | Yes |
| Rays per pixel | 4 | 1, 2, 4 | int | Yes |
| Maximum ray steps | 128 | 16 – 128 | int | Yes |
| NRD anti-firefly | true | false, true | bool | Yes |
| Radius | 1 | 0.25 – 1.5 | float | Yes |
| Depth tolerance | 0.05 | 0.002 – 0.1 | float | Yes |
| Donor luminance limit | 1 | 0.1 – 16 | float | Yes |
| Maximum bounce fraction (%) | 20 | 1 – 50 | int | Yes |
| Distance cutoff | 25 | 5 – 50 | float | Yes |

**4. Stochastic GGX SSR**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| Enabled | false | false, true | bool | No |
| SSR resolution | R768 | `ReflectionResolution` choices below | enum | No |
| Maximum iterations | 350 | 32 – 1024 | int | Yes |
| Thickness | 1 | 0.01 – 1 | float | Yes |
| Thickness hit confidence floor (%) | 100 | 0 – 100 | int | Yes |
| Occluder continuation samples | 350 | 0 – 1000 | int | Yes |
| Occluder continuation pixels | 500 | 0 – 2000 | float | Yes |
| Occluder continuation depth separation | 200 | 0 – 500 | float | Yes |
| Capture continuation diagnostics | false | false, true | bool | Yes |
| Capture traversal work | false | false, true | bool | Yes |
| Fallback strength (%) | 65 | 0 – 100 | int | Yes |
| Reconstruction backend | Custom | `ReflectionReconstruction` choices below | enum | Yes |
| Minimum receiver smoothness (%) | 40 | 0 – 100 | int | Yes |
| Cutoff environment blend (%) | 25 | 0 – 100 | int | Yes |
| Zero-smoothness probe lighting | true | false, true | bool | Yes |
| Zero-smoothness probe contribution (%) | 30 | 0 – 100 | int | Yes |
| Zero-smoothness probe contribution close (%) | 10 | 0 – 100 | int | Yes |
| Zero-smoothness probe fade start | 5 | 0 – 50 | float | Yes |
| Zero-smoothness probe fade end | 15 | 0 – 150 | float | Yes |
| Water reflection surface colour influence (%) | 20 | 0 – 100 | int | Yes |
| Temporal stabilization | true | false, true | bool | Yes |
| NRD anti-firefly | false | false, true | bool | Yes |
| History frames | 8 | 1 – 32 | int | Yes |
| Debug view | Normal | `ReflectionView` choices below | enum | Yes |
| Freeze ray sequence | false | false, true | bool | Yes |

**5. Custom Water**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| Mode | Native | `WaterSurfaceMode` choices below | enum | Yes |
| Show replacement tint | false | false, true | bool | Yes |
| Surface size | 1 | 0.25 – 4 | float | Yes |
| Surface strength | 1 | 0 – 2 | float | Yes |
| Fine ripple strength | 1 | 0 – 4 | float | Yes |
| Animation speed | 1 | 0 – 2 | float | Yes |

**9. Debug**

| Setting | Default | Range / choices | Type | Advanced |
|---|---|---|---|---|
| View | Normal | `OcclusionView` choices below | enum | Yes |
| Bent difference gain | 32 | 1 – 1000 | float | Yes |
| Ambient contribution gain | 1 | 1 – 1000 | float | Yes |
| Depth range | 5 | 0.01 – 200 | float | Yes |
| Depth start | 0 | 0 – 200 | float | Yes |
| Contact GI capture sequence | false | false, true | bool | Yes |
| Contact GI capture frames | 64 | 8 – 600 | int | Yes |
| Screen Space Contact GI gain | 16 | 1 – 128 | float | Yes |
| Foliage live preview | Scene | `FoliageLivePreview` choices below | enum | Yes |
| Foliage live display gain | 1 | 1 – 32 | float | Yes |
| Foliage live bypass Contact GI | false | false, true | bool | Yes |
| Foliage live bypass opaque colour | false | false, true | bool | Yes |
| Foliage live split comparison | true | false, true | bool | Yes |
| Foliage live divider (%) | 50 | 0 – 100 | int | Yes |

## Enum choices

- **OcclusionView:** `Normal`, `Depth`, `BeforeBlur`, `AfterBlur`, `CombinedAO`, `SecondPassAO`, `BentNormals`, `BentSHDifference`, `BentLightingDifference`, `AmbientDirectional`, `AmbientUpward`, `AmbientFill`, `AmbientInteriorTransmission`, `AmbientShares`, `ScreenSpaceContactGI`, `ScreenSpaceContactGIHistory`, `FoliageLightingCapture`, `FoliageLive`, `ScreenSpaceContactGIRaw`, `ScreenSpaceContactGIDenoised`, `ScreenSpaceContactGIHitDistance`, `ScreenSpaceContactGITrace`.
- **FoliageLivePreview:** `Scene`, `BeforeAmbient`, `AfterAmbient`, `BeforeOpaque`, `AfterOpaque`, `AfterTransparency`, `AmbientDifference`, `OpaqueDifference`, `ExperimentalMask`.
- **ReflectionReconstruction:** `Custom`, `NativeGaussian`.
- **ReflectionResolution:** `R256`, `R512`, `R768`, `R1024`, `R1536`, `R2048`.
- **ReflectionView:** `Normal`, `HitMask`, `HitUV`, `HitDepthDistance`, `RawHitRadiance`, `TodSkyRadiance`, `EnvironmentProbeRadiance`, `SourceClassification`, `Confidence`, `ChosenRadiance`, `NormalLength`, `NormalizedNormal`, `MaterialWeight`, `EnvironmentContribution`, `ReplacementDelta`, `EnvironmentRemovedScene`, `AddedReflection`, `TraceTermination`, `CoverageStages`, `ReconstructionLoss`, `ReceiverColourFallback`, `WaterCoverage`, `ContinuationSource`, `ContinuationCost`, `RejectionLocation`, `HaloStages`, `DepthHitKind`, `DepthHitConfidence`, `GapFillCoverage`.
- **WaterSurfaceMode:** `Native`, `FTFX`.
- **DistantShadowMaskResolution:** `Quarter`, `Half`, `Full`.

## Live acceptance

Open F12 and check header order, the four button labels and hover descriptions, integer percentage values, and advanced-only water controls. Change normal, advanced, and debug settings; Shift-click FTFX Vanilla+ (Default) and confirm every shipped default is restored, including SSR history = 8. Change water mode, SSR history and tuning again; verify that Vanilla, FTFX Vanilla+, FTFX HQ Visuals and FTFX HQ+ Visuals change only their listed controls and preserve those unrelated values. Shift-click each selective preset and confirm unrelated values reset to their shipped defaults before its overrides apply. Restart and confirm persistence. In a raid, compare bloom 50/100 and exposure 66/100, toggle XeGTAO/SSR through the selective presets, and return debug View to Normal before comparing rendering. Synthetic checks do not establish live EFT appearance or performance.
