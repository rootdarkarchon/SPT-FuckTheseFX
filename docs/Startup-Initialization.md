# Startup initialization, 1.8.10

Released as 1.8.10. The live measurements below used the same startup fix in
the 1.8.9 candidate before the release version bump.

## Scope and evidence

This change targets production FTFX before EFT Application Awake. It does not
change the separate CustomAmbient or Live RTGI research plugins.

The existing successful FTLS Startup Timer 0.2.2 capture
`20261015-225610-7c6e4bc4` records these process-age estimates:

| Boundary | Seconds |
| --- | ---: |
| FTFX 1.8.8 loading-message interval | 22.372–31.046 (8.674) |
| EFT Application Awake entry | 34.527 |
| MenuScreen.Show return | 71.236 |
| OnApplicationLoaded return | 71.477 |

The loading-message interval includes assembly/type loading, synchronous Awake,
intervening loader work and logging. It is not exclusive CPU time. Application
Awake entry is taken from `application.awake.start_ms`, not its return marker.
The menu milestones do not establish GPU frame presentation.

## Source trace

| Area | Work and decision |
| --- | --- |
| Assembly/type loading | BepInEx discovers and loads the managed plugin before calling Awake. Its initial metadata scan and Mono JIT/type preparation are outside the plugin's own stopwatch. FTFX has no assembly-wide PatchAll or explicit GetTypes scan. Its constructors mostly allocate small collections and camera-policy state. No separate assembly-loading speedup is claimed. |
| Static initialization | SettingsCatalog constructs definition/UI metadata; other static state is small collections, identifiers and constants. No static constructor was found doing the removed content hashes, loading the renderer bundles or starting native NRD. |
| Settings | Awake binds 103 real settings plus the preset UI entry before initializing integrations. The installed BepInEx ConfigFile.Bind calls Save after each newly bound entry when SaveOnConfigSet is true. BindSettings now suppresses those intermediate writes, saves the complete catalog once if originally enabled, and restores the caller's policy in finally. Existing values and orphan entries are retained. |
| Native content checks | Water hashed sharedassets15.assets; SSR hashed Unity.Postprocessing.Runtime.dll and StreamingAssets/Windows/shaders; distant shadows hashed resources.assets; ambient hashed the same shaders file again. This performed five synchronous reads, roughly 1.1 GB including the duplicate. All are removed from runtime initialization under the user's fixed-game/fixed-assets constraint. |
| Compatibility | Exact installed module MVID gates remain before patching: Assembly-CSharp cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7 for water, shadows and ambient; Unity.Postprocessing.Runtime bb1716ca-81b5-4288-9a04-95ef431a6ddf for SSR. Existing method/field signatures, transpiler match counts and shader-support checks remain. These module checks identify the supported managed ABI; they do not independently hash raw game assets. Full content hashes remain in build/package validation. |
| Reflection | Required method/field resolution and Harmony IL inspection remain synchronous. SSR's internal renderer is now resolved from the known postprocessing assembly. Remaining bounded type-name lookups target installed bloom, Prism, ambient and water types. Optional CloudSix config discovery is later rendering work, not called during integration Awake. |
| Resources | The water, stochasticssr, distantshadows and occlusion FTFX bundles, shader-support checks, blue noise and small meshes still load in their original sequence. Disabled settings remain live-toggleable. No bundle or shader construction was deferred to Start, a worker, a menu hook or a first frame. |
| Harmony and events | All 12 existing feature initializers remain in Awake, including filter subscriptions, glass group hooks, SSAO setting capture and early render-command interception. Their order and per-feature error isolation remain. No early camera/scene/settings event is sacrificed for startup timing. |
| Already deferred | Per-camera render targets, pipelines, native NRD backends/history and raid-gated glass discovery are already created on use. Further splitting disabled-feature bundle loading would require separate camera/settings activation and fallback validation; it is not part of this change. |

Production versions before this change used full-content verification as an asset
compatibility guard. For this fixed EFT installation, it did not need to run on
every launch. No CNG replacement, process cache or background hash task ships.

A standalone Unity 2022.3.43f1 Mono replay of the original five file checks took
10.142, 10.134 and 10.114 seconds (median 10.134) on the installed files. This is
a CPU/I/O fixture, with repeat/warm file-cache effects and no game execution.
It supports the expensive-hashing diagnosis; it is not a measured executable-to-menu
saving and must not be subtracted from the historical capture as a prediction.

## Validation and live comparison

`Test-StartupInitialization.ps1` examines release IL and installed metadata:
each MVID mismatch throws before Harmony patching, all 12 feature calls remain
in Awake, and no runtime SHA provider/file verification remains. Configuration
tests cover automatic-save policy, complete saved catalog, existing nondefault
values, orphan entries and subsequent setting persistence. Renderer fixtures
and normal package checks remain required.

The managed build passed with zero warnings/errors. Current startup/configuration
contracts passed, SSR/NRD validation passed all 2,011 checks, and water validation
passed 72 native/baseline comparisons plus its Unity ownership/restore checks.
Independent source/installed-metadata review found no significant issues under
the fixed-assets constraint. These are not live EFT startup/visual acceptance.

Baseline package `FuckTheseFX-1.8.9-SPT4.1-83b83a5506fd.zip` adds timing only
to the branch's existing 1.8.9 code. It retains runtime hashing and per-bind saves.
Compare the final candidate against this package to isolate this change; the
older 1.8.8 capture also predates the branch's filter scene-scan fix. Keep both
packages' generated filenames intact.

For each condition, exit EFT before extracting the package into the same
installation. Keep profile, graphics, menu background, mods, research plugins,
timer 0.2.2 and server state unchanged. Record first launches separately from
repeat launches; collect at least three comparable runs per condition. Wait at
menu for the timer's five-second tail, then exit. Copy each run's LogOutput.log
into its new BepInEx/FTLS.StartupTimer capture folder before the next launch.
Both builds log `FTFX startup: settings`, every feature and `Awake total` as
inclusive wall milliseconds; these nest and must not be summed with loader spans.

Use `scripts/Compare-StartupCaptures.ps1 -BaselineCapture <paths> -CandidateCapture <paths>`
to report per-run values and medians for process-to-Awake, process-to-menu,
application-loaded, Awake-to-menu and the FTFX loading interval. An optional
`-OutputCsv <path>` exports per-run values. The script rejects incomplete capture
outcomes and missing early coverage. Review the logged per-feature values and
visible menu readiness alongside it.

## First live result

The user's candidate capture `20261015-233007-b6fb19fc` reached `menu-arrived`
with timer 0.2.2, early-preloader coverage and no dropped early/main spans or
observer errors. Its installed FTFX DLL SHA-256 matches the DLL inside candidate
package `FuckTheseFX-1.8.9-SPT4.1-03140fbfcd4d.zip`:
`794EF8FE07EEE027CCB83C8C31514AE471D3D9E5A98174E36DCC7BA111B80B5D`.

| Metric | Original 1.8.8 capture | Candidate 1.8.9 capture | Candidate minus original |
| --- | ---: | ---: | ---: |
| FTFX loading-message interval | 8.674 s | 0.369 s | -8.305 s |
| Process to Application Awake entry | 34.527 s | 26.867 s | -7.660 s |
| Process to MenuScreen.Show return | 71.236 s | 63.796 s | -7.441 s |
| Process to OnApplicationLoaded return | 71.477 s | 64.050 s | -7.426 s |
| Application Awake entry to menu Show return | 36.710 s | 36.929 s | +0.219 s |

The candidate's FTFX log reports Awake total **348.761 ms**, settings
**14.467 ms**, SSR **103.783 ms**, inactive effects **50.169 ms**, bloom
**42.143 ms**, distant-shadow resolution **31.924 ms**, distant-shadow sampling
**26.904 ms**, XeGTAO **24.200 ms** and water **12.859 ms**. All 12 feature timing
rows are present. The current LogOutput.log explicitly records export to this
capture ID, associating those timings with the measured run. A filtered copy is
preserved under ignored `obj/StartupValidation/first-live-ftfx.log`; derived
per-run data is `obj/StartupValidation/first-live-comparison.csv`.

This run shows a much shorter FTFX loading interval and earlier overall menu
arrival, without a comparable stall appearing between Application Awake and
menu. It is one historical/candidate comparison, not a controlled repeated-run
effect estimate. The original is 1.8.8 rather than the supplied timing-only
1.8.9 baseline, and Live RTGI Research changed from 0.12.1 to 0.13.1. Its own
loading interval changed from 0.618 to 0.568 seconds; CustomAmbient Research
0.14.3 changed from 3.120 to 3.049 seconds. Other startup phases and launch/cache
conditions also vary, so the full menu-time difference cannot be assigned
exclusively to FTFX. Matched repeated launches remain useful for a median and
variability estimate; they are not required to recognize the observed result.
