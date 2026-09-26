# Screen Space Contact GI prototype

Opt-in short-range diffuse color transfer, separate from existing AO tuning. Requires
FTFX XeGTAO, D3D11, HDR deferred rendering and CameraManager.Instance.Camera.
Both Gamma (used by the installed EFT PlayerSettings) and Linear color space are supported.
AOReplacer presence and game AO Off bypass it with the FTFX occlusion stack.
CustomAmbient's fix and bent normals are not required. Existing ambient is sampled
as rendered; its lighting and AO behavior are not changed.

## Pipeline

The camera-owned command buffer is first at BeforeImageEffectsOpaque, after native
CustomAmbient at AfterLighting, ahead of opaque fog/SSR and the actor composite.
Registration preserves all other buffers' relative order and is checked again at
camera pre-render. It does not use the downstream UltimateBloom hook.

1. Capture fresh pre-tonemap scene RGB to RGBAHalf, with representative linear depth
   in alpha. Select the nearest actual surface per 2x2 or 4x4 block; average only
   compatible geometry, limiting donor luminance before filtering. An RGBA8 guide
   contains octahedral geometric view normal, resolved XeGTAO visibility, and validity plus the
   representative texel offset. Actors are rejected using the existing stencil mask;
   if actor AO is disabled, only its inexpensive mask capture is retained.
2. Gather 4, 6, 8, 16 or 32 nearby donors within a world-space radius. Use both endpoint
   shading-normal facing terms, squared distance falloff and one projected 3D midpoint depth test.
   Reject sky, off-screen and invalid donors. Divide by configured sample count,
   including rejected samples. Write incident RGB plus a capped donor luminance
   reference to RGBAHalf. The reference is the mean over configured sample count,
   not an uncapped radiance estimate or a statistical median. With temporal enabled,
   pixel/frame/tap hashes independently choose angles and jitter radii within equal-
   area annuli. This removes the shared rotating spiral and 64-render phase reset.
   The render counter remains an integer through shader upload, uses all 32 bits,
   and wraps only after 2^32 renders (or resets with history). This is stratified
   pseudorandom sampling, not a blue-noise sequence. Temporal-off restores the
   original fixed sampling pattern. Recording commands does not advance the counter.
3. Reproject valid receiver positions into the previous camera view. Validate four
   history taps using depth-derived geometric world normals, tangent-plane depth,
   distance and local source lighting changes. The geometric normal uses four full-
   resolution depth neighbors, choosing the smaller discontinuity on each axis;
   capture, temporal validation and spatial reconstruction use geometric normals,
   while transport reads shading normals directly from the G-buffer. History distance
   bounds account for the enlarged pixel footprint at grazing angles (capped at 40x).
   Source-lighting neighborhoods, temporal taps, spatial filtering and upscaling
   share the same angle-aware footprint and two-sided plane checks. Geometry and
   source-lighting confidence fade near their rejection limits; surviving bilinear
   coverage continuously reduces the history blend and effective age. The former
   20% coverage cutoff is removed. Invalid/different surfaces still hard-reject.
   Plane comparisons allow for half-precision depth rounding projected onto the plane;
   capture/current-depth and midpoint-visibility tolerances remain unchanged. Compare previous
   source lighting against a geometry-compatible 3x3 current color envelope, so
   resampling normal maps or texture detail does not by itself discard history.
   Uniform lighting changes outside that envelope still reject immediately.
   Accumulate incident RGB and donor reference with a per-pixel
   history count, capped at 16 by default (4-30 configurable), plus first and second
   luminance moments. These use RG32F so squared dim lighting does not underflow.
   Variance controls spatial filtering, rather than treating random donor changes
   as evidence that otherwise valid history is wrong. History luminance and
   reference are bounded by the configured donor ceiling, preserving hue. Current
   random donor brightness cannot bound an accumulated mean: even a dim nonzero hit
   must not erase previous bright hits. Empty sample neighborhoods use the selected
   history rate too: at 16 frames, accepted history retains 15/16 of its value,
   without a hidden 75% fallback. Persistently absent donors decay at that rate;
   incompatible surfaces/local lighting still reject history immediately.
   Invalid current geometry never receives history. Representative pixel offsets
   remain aligned across frames, avoiding repeated subpixel drift on static surfaces.
4. Smooth accumulated RGB and its donor reference together with two 3x3 binomial
   filters at the selected resolution, with one- and two-texel spacing. Reject invalid guides, geometric normal discontinuities,
   tangent-plane depth disagreements and distant neighbors; weight by normal and
   AO similarity. Valid zero-light neighbors retain weight to suppress speckles.
   Each base pass is bounded to a quarter of the world-space gather radius.
   With temporal accumulation, a third pass at four-texel spacing adds wider
   filtering for high relative luminance variance or young history. It uses
   variance-scaled luminance weights and is bounded to half the gather radius.
   Mature zero-variance pixels bypass this wider neighborhood. Maximum combined
   reach is seven low-resolution texels per axis. The spatial result is not saved
   into history, avoiding cumulative blur.
5. Reconstruct with four plane/depth/normal-aware taps. Apply strength with a 32x
   artistic radiance gain to compensate for the sparse weighted gather, then receiver
   diffuse reflectance, maximum bounce fraction, smoothstep(0.05, 0.35, XeGTAO visibility),
   distance and screen-edge fades. Add RGB and preserve destination alpha.

Defaults: quarter of each internal dimension, 8 samples, 0.75 m radius, strength 3,
depth tolerance 0.02 m, donor luminance cap 2 HDR units, maximum bounce fraction 0.2,
and fade from 15 to 25 m. Temporal accumulation and Spatial filter are on by default;
toggle them under Advanced for A/B comparisons. History frames defaults to 16.
All tuning is Advanced; only Enabled is a normal control.
Strength ranges from 0 to 4 (100% = 1); saved strength values are retained but now
produce a stronger effect. The maximum fraction caps the reflected addition after
receiver reflectance and before AO/distance/edge fades. It still uses the donor
reference averaged over all configured taps, so sparse survivors retain their
coverage penalty. Increasing strength eventually reaches this cap. Black receivers
and closed crevices still add zero; persistently unlit donors converge to zero.
No brightness floor is introduced.

There is no ray marching. The three base
low-resolution targets cost 20 bytes per low-resolution pixel. Spatial filtering
adds one low-resolution RGBAHalf target (8 bytes per pixel) and two nine-tap draws,
plus a third adaptive draw when temporal accumulation is enabled;
disabling it releases that target and skips the filter draws. The second pass reuses the
finished gather target; no draw reads and writes the same texture. Geometric normals
also add depth reads during capture and full-resolution reconstruction, while gather
reads shading normals per endpoint. Temporal accumulation adds five RGBAHalf
targets and two RG32F moment targets at the selected resolution (56 bytes per pixel),
one resolve draw with three outputs, and four same-size history copies. They hold current and previous incident
light/surface state plus previous source lighting. Surface state packs depth, world
normal and an exactly representable history-count/representative-offset integer.
Read and write histories never alias. Temporal-off releases all seven targets.
Gamma rendering also
requires one full-resolution RGBAHalf scene snapshot (8 bytes per full-resolution
pixel), taken before adding the effect. Capture decodes donor RGB into linear light;
composition decodes receiver albedo and computes encode(decode(original) + bounce)
minus original, then adds that encoded increment. This preserves the original scene
and its alpha without adding gamma-encoded light directly. The Linear path does not
allocate or copy this full-resolution target. GPU marker: FTFX Bleed gamma snapshot.
XeGTAO's existing full-resolution linear depth and visibility are reused. Its weighted
depth mips are not conservative visibility bounds and are not used for segment tests.
Normal maps still affect transport, but no longer define temporal or filtering
surface normals when valid depth derivatives are available.

## Limits and evaluation

This is a bounded scene-color approximation, not diffuse-only or energy-conserving GI.
Specular donor suppression and the luminance cap reduce outliers; they cannot identify
all emission. Existing ambient can double-count some indirect light. One midpoint
cannot prevent every thin-wall leak or recover hidden/off-screen geometry. Quarter
resolution can lose small donors, particularly with low DLSS input resolutions.
Temporal reprojection uses camera matrices, not object motion vectors. Moving props,
coplanar movers and fast lighting changes can still leave short trails. The texture
envelope can also retain history near moving lighting boundaries. Local lighting
rejection and the configured ceiling reduce those errors but cannot identify every
donor change. Surface detail and strict rejection can still expose noise. Hidden or
off-screen donors and genuinely invalid surfaces cannot be recovered by accumulation.

Capture requires matching color/G-buffer/depth dimensions and rejects current-depth
disagreements with deferred geometry. Unsupported camera modes are bypassed with a
warning. Allocation/shader failures disable only this feature until toggled off/on.
Targets are released on disable, camera change/disposal, or other AO diagnostics; resize
reallocates them. The source is overwritten before every gather, preventing recursive
feedback. History is promoted only after an actual camera render and contains no
final composited color, albedo, strength or AO. Other cameras never publish or
consume these targets. History resets on resolution changes, Screen Space Contact GI tuning,
color-space/target changes, camera cuts, large FOV changes, and gaps over 0.5 seconds.
Camera cuts include translations over max(1 m, 2*radius) or orientation changes over
30 degrees. Debug gain changes and switching between Normal, ScreenSpaceContactGI and ScreenSpaceContactGIHistory views
do not reset history; other AO diagnostics still release the Screen Space Contact GI pipeline.

Use F12 > Fuck These FX > Screen Space Contact GI > Enabled. Compare a lit colored object beside
a neutral surface, then shadow the donor; check dark crevices, smooth/emissive objects,
weapons, thin partitions, screen edges and camera movement. Compare half/quarter at
the same internal rendering resolution and 4/6/8/16/32 samples at the same radius/strength.
GPU markers: FTFX Bleed capture, FTFX Bleed gather, FTFX Bleed temporal resolve,
FTFX Bleed spatial filter, FTFX Bleed composite, FTFX Bleed history copy. Disable all
diagnostics for timings. Unity fixtures establish shader contracts, not live EFT
visual acceptance or a measured in-game performance saving.

The dim Gamma fixture uses a 0.075 neutral receiver beside a (0.2, 0.05, 0.02) donor,
0.25 receiver albedo, 0.75 m radius and open visibility. At half resolution/eight taps,
strength 1 in the unfiltered 1.3.2 pass raises mean receiver red by 0.00636 versus
0.000209 in 1.3.1 (about 30x).
This is a controlled shader measurement before downstream effects, not an EFT capture.
The complete temporal + spatial Gamma fixture tests 32 varying real gathers and
compares frame differences over the final 16. Its squared frame-difference ratio
is about 0.009 relative to spatial filtering alone, with mean red addition 0.00628
at half resolution. This stationary fixture does not establish in-game ghosting
or performance. Additional GPU checks cover history rejection, decay, camera
reprojection, 16-frame count/offset packing and camera lifecycle resets.

## Contribution diagnostic

With Screen Space Contact GI enabled, select Advanced > Debug > View = ScreenSpaceContactGI. Screen Space Contact GI
gain defaults to 16x (1-128x) and affects only the display. At 1x the texture contains
the shader's added pre-tonemap scene RGB, after optional temporal/spatial filtering,
strength, maximum bounce,
receiver diffuse reflectance, AO visibility and distance/edge fades. In Gamma mode
this is the encoded RGB increment actually blended into the scene; in Linear mode
it is the linear RGB addition. It is not raw
gathered light and does not include later fog, tonemapping or bloom responses.

The debug pass calls the exact same contribution function with the same immutable
inputs as the additive composite. Scene rendering still receives the normal addition;
the isolated image is drawn over it through the existing GUI diagnostic path, keeping
F12 usable. Only the current main camera supplies this view. A disabled effect shows
black with a disabled label; unavailable capture shows cyan; mismatched input sizes
show magenta. A valid black result means zero addition, including at strength zero.

This mode adds one full-resolution RGBAHalf target and one draw, released on return
to Normal. It adds no diagnostic target/draw during normal rendering. The capture
keeps HDR values; amplified highlights may clip on the display. Use Normal to judge
the final scene and measure performance. GPU tests compare every debug pixel against
the normal additive result at 1x/16x and verify display gain cannot change scene RGB.

## History diagnostic

Advanced > Debug > View = ScreenSpaceContactGIHistory displays the temporal resolve's actual
retention and limiting, independently of contribution display gain. Green means
retained history (mixed with red according to the current/history blend). Rejection
reasons are red for depth/position, yellow for geometric normal, cyan for local
lighting, and orange for fresh/offscreen/unavailable history. Blue means invalid
current geometry; magenta indicates strong history limiting. Rejection colors report
the stage that removed enough bilinear weight to prevent accumulation.
The configured frame count is only an upper limit;
the diagnostic shows what the shader actually retained this render.

This view requires Temporal accumulation. It adds one low-resolution RGBA8 target
(4 bytes per pixel), written as a fourth output of the existing temporal draw. There
is no additional draw; the target is released outside this view. History remains
active when switching between the contribution and history diagnostics.

The sparse-color regression checks mature 16-frame history against a different-hue
sample and a zero-hit sample. Both retain the expected 93.75% history. It also checks
alternating donor colors converge to their mixture, rather than clipping old hues
to the latest sample. These checks do not establish live-game stability.

Stationary front-facing and grazing-plane fixtures vary normal detail or texture
samples independently of geometry, and require retained history plus converged
donor colors at half and quarter resolution. Separate checks reject an actual plane
rotation with unchanged shading normals, a uniform lighting change, and lighting
envelopes spanning a foreground depth boundary. No extra targets or draws are added
for this validation change; the temporal shader performs additional depth/source reads.

A varying-brightness fixture alternates a bright donor sample with seven dim samples.
At 16-frame history its expected final value is 0.020660. The old per-frame reference
clamp reduced it to 0.001634; the stable ceiling produces 0.020508 in RGBAHalf without
limiting valid samples. Real deferred-camera checks also exercise subpixel projection
jitter over a grazing floor near world position (-200, 2.4, -212). These fixtures do
not reproduce or establish the cause of every correlated rejection seen in EFT.

The sampling probe runs the same HLSL helper as the gather. For 4/6/8/16/32 taps it checks
disk area/directional coverage, deterministic replay, frame 64 versus frame 0,
large integer frame indices, and fixed temporal-off output. Neighboring pixels'
angular-motion correlation is about 0.003 in this fixture. Camera tests also cross
frame 64 and signed integer rollover. Hashing adds integer arithmetic but no textures,
targets, passes, or donor taps. Removing coherent motion does not eliminate random
variance, repair history rejection, or establish live EFT stability/performance.

Angle/precision regression: at 1025x577, a jittered shallow plane near world position
(-200, 0.5, -212) lost history on 20.2% of valid samples at half resolution before
the grazing-footprint and rounding fixes, versus 0.2% after. Rotating it to a vertical
plane also yields 0.2%; a front-facing wall around 23 m yields 0.3%. All use the default
0.02 m tolerance. A separate check still rejects a 10 cm depth discontinuity at 20 m.
With shared geometry confidence, the sparse-input variance ratio is 0.024 after
two stages and 0.008 with the adaptive third stage, versus 0.118 for one stage.
The stable-history bypass and depth/normal/invalid/AO barriers pass GPU checks.
Coverage just above/below the old 20% cutoff now retains 0.1692/0.1508 of a unit
history sample instead of resetting below the cutoff. Dim moment precision,
moment reset and varying-donor variance are also checked. These controlled results
are not measurements of DLSS integration, in-game noise reduction, or GPU cost.
