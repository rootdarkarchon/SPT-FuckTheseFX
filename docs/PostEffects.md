# Prism exposure, sharpen, and inactive screen effects

## Implemented controls

`1. Vanilla Optimizations / Prism Auto Exposure Resolution (%)` defaults to **66**, with an integer **1–100** range. It scales each side of Prism's first square exposure texture. For the normal post-DLSS input, the native side is `source.width / 2`, where SSAA has already substituted the final output texture. At 3840×2160 output:

| Setting | Exposure texture | Pixels relative to native |
| --- | --- | --- |
| 100% | 1920×1920 | 100% |
| 50% | 960×960 | 25% |
| 25% | 480×480 | 6.25% |

The implementation takes the game's integer half-width and multiplies it by `percent / 100`, rounds down, and clamps reduced dimensions to at least one pixel. At 100% it returns the original dimensions exactly. If Prism's optional median/DOF branch supplies a smaller source, it scales that native grid instead; it never enlarges that branch merely to match `Screen.width`. This preserves existing behavior at 100% for every input path and supports other camera sizes.

Only the first `RenderTexture.GetTemporary(int,int,int,RenderTextureFormat)` in `PrismEffects.HandleExposure` is redirected. The texture format, depth, first sampling shader pass, subsequent reduction passes, histogram target, adaptation texture, and final scene/tone-mapping pass remain native. Lower sampling detail can change the influence of small bright objects on exposure. It does not reduce all of Prism's work by the same fraction. When Prism shares another instance's master exposure, it continues sharing it; the setting affects allocations when the native reducer actually runs.

The patch checks the exact installed allocation instructions and refuses an incompatible method. Feature startup reports the failure while other FTFX features can initialize. The setting applies at the next exposure allocation and requires no restart.

`1. Vanilla Optimizations / Sharpen Filter` defaults to **true**. False disables the `CC_Sharpen` component and removes its camera image pass. True restores only components FTFX disabled, leaving components that were already disabled alone. Setting changes, scene loads, and camera pre-cull handle live toggles and newly created cameras. Disposal removes subscriptions and restores owned components.

CC_Sharpen also combines health/weather/mask desaturation when `DoDesaturate` is enabled. Turning this component off bypasses that combined behavior as well as sharpening; it does not move desaturation to another shader. The existing standalone `DesaturateEffect` state is left under the game's control.

`1. Vanilla Optimizations / Skip Neutral Effects Passes` defaults to **true** and applies live. It removes the five neutral status-effect copies in a confirmed current-frame SSAA output chain. For zero-aperture disc DoF it exits after the native CoC write, before temporary allocations and blur/composite passes. Setting false restores those native passes. No game shader is rewritten or distributed.

## Evidence and measurement boundaries

Capture: `D:\Tarkov-SPT-4.1\BepInEx\CustomAmbientResearch\20261002-150700-649-scene-gpu-customs-1200-ftfx`.

RTX 4090, D3D11, Unity 2022.3.43f1, 3840×2160 final screen, 2560×1440 DLSS Quality input. The detailed window has 187 complete camera samples. Values below come from the exclusive `image_effect` rows in `scene-gpu-breakdown.csv`; they are GPU intervals, not measured savings after these changes.

| Effect | Mean GPU ms per camera sample |
| --- | ---: |
| PrismEffects | 0.315217 |
| CC_Sharpen | 0.081810 |
| UnityStandardAssets.ImageEffects.DepthOfField | 0.151557 |
| BloodOnScreen | 0.028759 |
| PostprocessGrayscale | 0.025979 |
| DeathFade | 0.025873 |
| DistortCameraFX | 0.024931 |
| InfectionEffect | 0.024242 |
| Five status effects combined | 0.129784 |

`scene-image-effect-calls.csv` records the incoming camera argument before the effects replace it through SSAA. Its 2560×1440 dimensions do not prove these effects process the reduced DLSS input. The installed `SSAAPropagator.FillCommandBuffer` allocates its HDR/LDR colour pairs at screen output size, DLSS writes the HDR pair, and these wrappers retrieve the current pair. Prism performs the HDR-to-LDR transition. These effects consequently operate on output-sized textures in this configuration.

`scene-deep-settings.txt` reports Prism exposure, tone mapping, vignette, and noise enabled, with **Prism's own `useDof=False`**. That flag does not disable the separate UnityStandardAssets DoF component: `snapshot-start.txt` reports that component enabled, and it has its own timed interval. The capture does not record its aperture, focal size/distance, blur type, focus visualization, or per-pass draw state, nor the five status-effect neutral predicate values. Therefore the capture cannot prove zero DoF blur or that all five status effects took their neutral branches, despite the reported absence of visible effects.

Audit uses the installed, read-only assemblies. Status effects and Prism are in `Assembly-CSharp.dll`, MVID `cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7`, SHA256 `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`. DoF is in `Assembly-CSharp-firstpass.dll`, MVID `79ed6e2f-9190-49a0-a4ee-138078f70dc9`. SSAA is in `Unity.Postprocessing.Runtime.dll`, MVID `bb1716ca-81b5-4288-9a04-95ef431a6ddf`. The inactive-effect feature checks these exact module identities before patching, and fails back to original rendering if they change. Fresh IL listings are in `obj/InactiveEffectsAudit`; older decompiled source is not the patch contract.

## DoF when blur is invisible

The installed `UnityStandardAssets.ImageEffects.DepthOfField.OnRenderImage` has no early return for zero aperture or an image that happens to be entirely in focus. After resource checks it clamps negative aperture to zero, clamps `maxBlurSize` to **at least 0.1**, computes focal parameters, and executes its selected renderer. Setting blur size to zero therefore does not disable its work.

For example, the non-DX11-bokeh path writes circle-of-confusion information, allocates two half-width/half-height temporary textures, and executes blur/composite passes. Its high-resolution branch still allocates those textures before choosing its output pass. Other bokeh/foreground branches can do additional work. A failed resource check instead copies the source to the destination; even that fallback need not be free.

Thus **yes: visually inactive DoF can cost GPU time**. The measured 0.152 ms interval does not identify its executed branch or establish that its colour output was identical.

The installed D3D11 `Hidden/Dof/DepthOfFieldHdr` shader was extracted for local validation, with raw shader SHA256 `96F78FC1D8E481A37EB059A0EDDA7BE15185E9E58665FA698F275B78E9ED043D`. Its CoC pass writes alpha, and the disc output preserves that CoC alpha even when colour is unchanged. A completely empty return would preserve the incoming alpha instead, which is not equivalent. The package therefore retains **one native CoC draw** and skips the remaining disc blur/composite pipeline when all of these hold:

- Native preprocessing has clamped aperture to exactly zero.
- Focus visualization and near blur are off, and focal size/distance are finite.
- The native disc branch is executing on D3D11 with the expected native shader.
- The current SSAA source is the texture being rendered, both colour-pair textures exist and are created, and the source is ARGB32, ARGBHalf or ARGBFloat.

The guard is inserted immediately after `WriteCoc(source, true)` and before the first disc temporary allocation. Native resource checks, parameter clamping, focus calculation, filter settings, material setup, and the alpha write remain intact. The exit leaves the current SSAA texture/index unchanged because that source now contains the native colour and CoC output. There are no disc temporaries to leak at this point. Both high- and low-resolution disc paths benefit. DX11 bokeh, near blur, focus visualization, nonzero aperture, other shaders/APIs/formats, invalid parameters, and unavailable SSAA chains retain native rendering.

This removes the expensive zero-aperture blur pipeline without rewriting a shader. It is not a guarantee that visually imperceptible **nonzero** aperture can be skipped, nor that all 0.152 ms disappears. Fully eliminating the remaining CoC write requires proving that downstream alpha does not matter; this work stops at preserving that native contract.

## Five neutral status effects

Each already has a native neutral path. The exact predicates below are suitable starting points for eliminating copies; an arbitrary visual threshold is not needed.

| Effect | Native neutral predicate | Native neutral work | State that must keep running |
| --- | --- | --- | --- |
| DeathFade | Both `abs(_closeEyesValue)` and `abs(_fadeValue)` strictly below `Mathf.Epsilon` | `RendererExtensions.BlitOrCopy` | Curve updates, fade and eye-closing transitions |
| BloodOnScreen | `Time.time - _lastHitTime > MaxBloodTime` | `Graphics.Blit` | Hit/bleeding updates and timeout bookkeeping; equality still renders |
| PostprocessGrayscale | `abs(EffectAmount) < Mathf.Epsilon` in base `Posteffect` | `RendererExtensions.BlitOrCopy` | EffectAmount setters/animation; setter clamps to 0–1 |
| DistortCameraFX | `_doNotRender` computed by the current `OnPreCull` | `RendererExtensions.BlitOrCopy` | Renderer selection and command-buffer lifecycle; a nonempty renderer list can still yield no draws |
| InfectionEffect | After assigning `Clamp01(_effectValue)`, value is not greater than zero | `Graphics.Blit` | The clamp assignment itself and effect updates |

The five copies can be targeted without disabling the components' update/event behavior, **provided the image-chain handoff is preserved**. These wrappers call `SSAAPropagator.GetSourceDestination`, render/copy, then `ReleaseSourceDestination`; release advances the current HDR/LDR index unconditionally. Returning from only the internal renderer without writing the destination would advance the chain to stale content.

The implementation skips the entire neutral wrapper, including the release, so the next effect reads the same current texture. It reevaluates the exact predicate on every render, retains InfectionEffect's clamp mutation (including the native NaN comparison behavior), and leaves DistortCameraFX's pre-cull/command-buffer work and all component updates/events enabled. It binds Unity's unused proxy destination without drawing; the real image remains in the current SSAA texture. Consecutive skipped effects do not change the colour-pair index.

`SSAAPropagator.FillCommandBuffer` marks the current-frame chain open; the final `SSAAImpl.OnRenderImage` closes it. The guard additionally requires an active propagator and created, distinct current/next textures. Scene changes clear tracking, and stale frame stamps cannot enable skipping. It does not assume that old allocated textures imply a current image. The normal HDR-to-LDR transition remains native. Missing/disabled SSAA, a closed/uninitialized chain, or a render path that does not fill that command buffer uses original rendering. In Unity's ordinary image chain, neutral effects still copy because their destination must be written.

Neutral status effects consequently issue **no copy or effect draw in the eligible SSAA chain**; only the small CPU predicate/state guard and target binding remain. The current combined 0.130 ms is an A/B comparison target, not measured savings. No status component is disabled, so activation can resume native rendering on the next callback without waiting for polling or rebuilding components.

## Validation and live test

Release build and configuration validation cover the 94 real settings plus one preset row, defaults/ranges, nondefault saved values, repeated binding, current-layout persistence, and both presets. `scripts/Test-PostEffectsContracts.ps1` checks Prism/sharpen, the three native module identities, five status wrapper handoffs, pure SSAA getters/final consumer, and the exact DoF CoC-to-disc-allocation boundary.

`scripts/Test-PostEffects.ps1` runs **57 checks** in Unity 2022.3.43f1 on D3D11/RTX 4090. It imports the actual installed exposure allocation IL into a small fixture, patches it with the production integration, and observes real Unity temporary textures. Checks cover native 100%, 50%, integer 66%, other input sizes, rounding/minimum, invalid values, live setting changes, unsupported-method rejection and unpatching. Sharpen component fixtures cover selective restoration, native re-enabling, late cameras, destroyed objects and unsubscribing on disposal. These tests do not execute the complete EFT shaders or establish live exposure appearance/GPU savings.

`scripts/Test-InactiveEffects.ps1` runs **537 checks** on the same runtime/GPU. The SSAA getter/release bodies are imported from installed IL into callback surrogates; status wrappers use verified native predicates with a validation-only shader for active-pass ordering. All 32 status combinations in both HDR and LDR match the original copy chain, with no copies in eligible neutral paths. It covers activation, eye closing independent of fade, blood timeout equality, unordered/NaN comparisons, clamp mutation, continued distortion pre-cull, HDR-to-LDR transition, missing/disabled/closed/stale chains, ordinary Unity-chain copying, and owner-only unpatching. Actual Unity camera callbacks validate proxy-target binding without unwritten-output diagnostics.

For DoF, the installed compiled shader runs unchanged in a surrogate of its disc sequence. Native disc output is compared in **RGBA** against the single-CoC exit for both high/low resolution, HDR/LDR, and a depth/focal-distance/focal-size sweep. The reduced path creates no disc temporaries, executes no blur/composite draw, and retains zero CoC alpha. Positive aperture immediately resumes allocation, and unsupported conditions retain the native path. This establishes fixture output equivalence, not complete in-game camera/effect/lifecycle acceptance or live GPU savings. Extracted game shader assets remain under ignored `obj/` and are excluded from the package.

1. Install the test package while EFT is closed, then start with `1. Vanilla Optimizations / Prism Auto Exposure Resolution (%) = 100` and `1. Vanilla Optimizations / Sharpen Filter = true`.
2. Capture the same fixed scene at exposure 100%, 50%, and optionally 25%. Keep all other rendering settings/camera/weather fixed. Compare Prism timings; do not interpret its whole 0.315 ms as exposure-only cost.
3. At each setting, test dark-to-bright and bright-to-dark transitions, small bright objects, ADS/optics, night vision, menus/hideout and scene changes. Check exposure stability, clipping and recovery when returning to 100%.
4. Toggle sharpen off/on live, compare its interval and appearance, and check health/weather desaturation. Repeat after scene changes and with optics.
5. Compare `1. Vanilla Optimizations / Skip Neutral Effects Passes` off/on in the same fixed scene. Check the five status intervals individually and DoF separately. Record actual DoF aperture/blur settings before interpreting a remaining interval; the old benchmark does not contain those values. Expected eligible neutral status draws disappear; eligible zero-aperture DoF retains one CoC draw.
6. Exercise injury/bleeding, grayscale, infection, distortion, death/eye closing and recovery while toggling the optimization. Check rapid activation and combinations, ADS/optics, inventory focus, night/thermal vision, scene changes, and DLSS/FSR/SSAA off. Active effects must remain responsive and the screen must not freeze, flash stale content, or lose colour/alpha continuity.

XeGTAO, BloomAndFlares and DLSS internals remain unchanged. DoF and the five status shaders remain native; only their eligible inactive image paths are shortened.
