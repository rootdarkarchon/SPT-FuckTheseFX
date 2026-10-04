using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    // Runs once per camera render, from fresh opaque lighting. Never consumes its own output.
    internal sealed class ColorBleedPipeline : IDisposable
    {
        private readonly Camera camera;
        private readonly Mesh quad;
        private readonly Material material;
        internal readonly CommandBuffer Commands;
        internal RenderTexture Source, Guide, Bounce, FilteredBounce;
        internal RenderTexture TemporalBounce, TemporalState, HistoryBounce, HistoryState, HistorySource;
        internal RenderTexture TemporalMoments, HistoryMoments;
        internal bool HistoryValid { get; private set; }
        internal int RenderSerial { get; private set; }
        internal bool UsedHistory { get; private set; }
        private int sampleIndex, settingsHash;
        private bool haveSettings, renderPending;
        private Matrix4x4 previousView, previousProjection, renderedView, renderedProjection;
        private float previousRenderTime;
        private ColorBleedSettings currentSettings;
        private RenderTexture debug;
        private RenderTexture historyDiagnostic;
        private RenderTexture gammaScene;
        internal RenderTexture GammaScene => gammaScene;
        internal RenderTexture DebugTexture => recorded ? debug : null;
        internal RenderTexture HistoryDiagnosticTexture => recorded ? historyDiagnostic : null;
        private int width, height, divisor;
        private bool recorded;
        internal static readonly CameraEvent Event = CameraEvent.BeforeImageEffectsOpaque;

        internal ColorBleedPipeline(Camera camera, GpuAssets assets)
        {
            this.camera = camera; quad = assets.Quad;
            if (assets.ColorBleed == null || !assets.ColorBleed.isSupported || assets.ColorBleed.passCount != 6)
                throw new InvalidOperationException("Screen Space Contact GI shader is unavailable; rebuild the occlusion bundle.");
            material = new Material(assets.ColorBleed) { hideFlags = HideFlags.HideAndDontSave };
            Commands = new CommandBuffer { name = "FTFX Screen Space Contact GI [" + camera.GetInstanceID() + "]" };
            Camera.onPreRender += BeforeRender;
            Camera.onPostRender += AfterRender;
        }

        internal static bool Supports(Camera camera) => GpuAssets.Supports(camera) && camera.allowHDR &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, FormatUsage.Render) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.Render);

        internal void Record(ColorBleedSettings settings, RenderTexture depth, RenderTexture visibility, RenderTexture actorMask, bool showContribution = false, float debugGain = 1, bool showHistory = false)
        {
            Commands.Clear(); recorded = false; renderPending = false;
            currentSettings = settings;
            int w = depth.width, h = depth.height, div = (int)settings.Resolution.Value;
            if (w != width || h != height || div != divisor || Source == null || !Source.IsCreated())
            {
                ReleaseTargets(); width = w; height = h; divisor = div;
                int lowW = (w + div - 1) / div, lowH = (h + div - 1) / div;
                Source = GpuAssets.Target("FTFX Bleed lighting and depth",lowW,lowH,GraphicsFormat.R16G16B16A16_SFloat);
                Guide = GpuAssets.Target("FTFX Bleed surface guide",lowW,lowH,GraphicsFormat.R8G8B8A8_UNorm);
                Bounce = GpuAssets.Target("FTFX Bleed incident light",lowW,lowH,GraphicsFormat.R16G16B16A16_SFloat);
            }
            if (settings.SpatialFilter.Value)
            {
                if (FilteredBounce == null) FilteredBounce = GpuAssets.Target("FTFX Bleed filtered incident light",Source.width,Source.height,GraphicsFormat.R16G16B16A16_SFloat);
            }
            else GpuAssets.Release(ref FilteredBounce);
            if (settings.Temporal.Value)
            {
                if (TemporalBounce == null)
                {
                    TemporalBounce = HistoryTarget("temporal incident light"); TemporalState = HistoryTarget("temporal surface state");
                    HistoryBounce = HistoryTarget("previous incident light"); HistoryState = HistoryTarget("previous surface state");
                    HistorySource = HistoryTarget("previous source lighting"); ResetHistory();
                    TemporalMoments = GpuAssets.Target("FTFX Bleed luminance moments",Source.width,Source.height,GraphicsFormat.R32G32_SFloat);
                    HistoryMoments = GpuAssets.Target("FTFX Bleed previous luminance moments",Source.width,Source.height,GraphicsFormat.R32G32_SFloat);
                }
            }
            else ReleaseHistory();
            if (showHistory && TemporalBounce != null)
            {
                if (historyDiagnostic == null) historyDiagnostic = GpuAssets.Target("FTFX Bleed history diagnostic",Source.width,Source.height,GraphicsFormat.R8G8B8A8_UNorm);
            }
            else GpuAssets.Release(ref historyDiagnostic);
            if (showContribution)
            {
                if (debug == null) debug = GpuAssets.Target("FTFX Bleed contribution diagnostic",w,h,GraphicsFormat.R16G16B16A16_SFloat);
            }
            else GpuAssets.Release(ref debug);
            bool gamma = QualitySettings.activeColorSpace == ColorSpace.Gamma;
            int hash = ConfigurationHash(settings,gamma);
            if (!haveSettings || hash != settingsHash) ResetHistory();
            haveSettings = true; settingsHash = hash;
            if (gamma)
            {
                if (gammaScene == null) gammaScene = GpuAssets.Target("FTFX Bleed gamma scene snapshot",w,h,GraphicsFormat.R16G16B16A16_SFloat);
            }
            else GpuAssets.Release(ref gammaScene);
            material.SetFloat("_CBGamma",gamma ? 1 : 0);
            material.SetTexture("_CBGammaScene",gammaScene != null ? (Texture)gammaScene : Texture2D.blackTexture);
            material.SetFloat("_CBDebugGain",debugGain);
            var projection = camera.projectionMatrix;
            material.SetVector("_CBSize", new Vector4(w,h,Source.width,Source.height));
            material.SetVector("_CBNDCToView", new Vector4(2/projection.m00,-2/projection.m11,(projection.m02-1)/projection.m00,(1-projection.m12)/projection.m11));
            material.SetMatrix("_CBWorldToView", camera.worldToCameraMatrix);
            material.SetVector("_CBSettings",new Vector4(settings.Radius.Value,settings.Strength.Value,settings.DepthTolerance.Value,settings.Distance.Value));
            material.SetVector("_CBLimits",new Vector4(settings.DonorLimit.Value,settings.BounceFraction,div,settings.Samples.Value));
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            material.SetVector("_CBDepthUnpack", SystemInfo.usesReversedZBuffer
                ? new Vector4(-near*far/(far-near),-near/(far-near),far,0)
                : new Vector4(near*far/(far-near),far/(far-near),far,0));
            material.SetTexture("_CBDepth",depth); material.SetTexture("_CBVisibility",visibility); material.SetTexture("_CBActors",actorMask);
            material.SetTexture("_CBSource",Source); material.SetTexture("_CBGuide",Guide); material.SetTexture("_CBBounce",Bounce);
            var accumulated = TemporalBounce != null ? TemporalBounce : Bounce;
            material.SetTexture("_CBSpatialInput",accumulated);
            material.SetTexture("_CBFilteredBounce",FilteredBounce != null ? (TemporalBounce != null ? FilteredBounce : Bounce) : accumulated);
            material.SetTexture("_CBHistoryBounce",HistoryBounce); material.SetTexture("_CBHistoryState",HistoryState); material.SetTexture("_CBHistorySource",HistorySource);
            material.SetTexture("_CBHistoryMoments",HistoryMoments); material.SetTexture("_CBMoments",TemporalMoments);
            material.SetTexture("_CBCurrentState",TemporalState); material.SetInt("_CBUseStatistics",TemporalMoments != null ? 1 : 0);
            if (gamma)
            {
                // Add in linear light, then encode the difference against the exact
                // original pixel. Never approximate that base with downsampled donors.
                Commands.BeginSample("FTFX Bleed gamma snapshot");
                Commands.Blit(BuiltinRenderTextureType.CameraTarget,gammaScene);
                Commands.EndSample("FTFX Bleed gamma snapshot");
                Commands.SetGlobalTexture("_FTFX_BleedScene",gammaScene);
            }
            else Commands.SetGlobalTexture("_FTFX_BleedScene",BuiltinRenderTextureType.CameraTarget);
            Commands.SetGlobalTexture("_FTFX_BleedCurrentDepth",BuiltinRenderTextureType.ResolvedDepth);
            Commands.BeginSample("FTFX Bleed capture");
            Commands.SetRenderTarget(new RenderTargetIdentifier[] { Source, Guide },Source);
            Draw(0);
            Commands.EndSample("FTFX Bleed capture");
            Commands.BeginSample("FTFX Bleed gather");
            Commands.SetRenderTarget(Bounce); Draw(1);
            Commands.EndSample("FTFX Bleed gather");
            if (TemporalBounce != null)
            {
                Commands.BeginSample("FTFX Bleed temporal resolve");
                Commands.SetRenderTarget(historyDiagnostic != null
                    ? new RenderTargetIdentifier[] { TemporalBounce, TemporalState, TemporalMoments, historyDiagnostic }
                    : new RenderTargetIdentifier[] { TemporalBounce, TemporalState, TemporalMoments },TemporalBounce); Draw(5);
                Commands.EndSample("FTFX Bleed temporal resolve");
            }
            if (FilteredBounce != null)
            {
                Commands.BeginSample("FTFX Bleed spatial filter");
                Commands.SetRenderTarget(FilteredBounce); Draw(4);
                // A second, dilated pass reduces residual sparse-sample noise.
                // Gather is finished: reuse Bounce without blurring saved history.
                var filter = new MaterialPropertyBlock();
                filter.SetTexture("_CBSpatialInput",FilteredBounce); filter.SetFloat("_CBSpatialStep",2);
                Commands.SetRenderTarget(Bounce);
                Commands.DrawMesh(quad,Matrix4x4.identity,material,0,4,filter);
                if (TemporalMoments != null)
                {
                    // Wider filtering only contributes for young/noisy history.
                    // Stable pixels return the prior pass unchanged in the shader.
                    filter = new MaterialPropertyBlock();
                    filter.SetTexture("_CBSpatialInput",Bounce); filter.SetFloat("_CBSpatialStep",4);
                    Commands.SetRenderTarget(FilteredBounce);
                    Commands.DrawMesh(quad,Matrix4x4.identity,material,0,4,filter);
                }
                Commands.EndSample("FTFX Bleed spatial filter");
            }
            Commands.BeginSample("FTFX Bleed composite");
            Commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget); Draw(2);
            Commands.EndSample("FTFX Bleed composite");
            if (debug != null)
            {
                // The same contribution function and immutable inputs as the additive
                // draw, captured before opaque effects. Display gain never touches it.
                Commands.BeginSample("FTFX Bleed diagnostic");
                Commands.SetRenderTarget(debug); Draw(3);
                Commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                Commands.EndSample("FTFX Bleed diagnostic");
            }
            if (TemporalBounce != null)
            {
                // Save only this frame's uncomposited incident light and guides.
                // Fixed read/write targets avoid swapping history on Record calls
                // that never reach a render, or when one camera renders twice.
                Commands.BeginSample("FTFX Bleed history copy");
                Commands.CopyTexture(TemporalBounce,HistoryBounce);
                Commands.CopyTexture(TemporalState,HistoryState);
                Commands.CopyTexture(Source,HistorySource);
                Commands.CopyTexture(TemporalMoments,HistoryMoments);
                Commands.EndSample("FTFX Bleed history copy");
            }
            Commands.SetGlobalTexture("_FTFX_BleedScene",Texture2D.blackTexture);
            Commands.SetGlobalTexture("_FTFX_BleedCurrentDepth",Texture2D.blackTexture);
            recorded = true;
            EnsureFirst();
        }

        private void Draw(int pass) => Commands.DrawMesh(quad,Matrix4x4.identity,material,0,pass);

        private void BeforeRender(Camera rendering)
        {
            if (rendering != camera || !recorded) return;
            // A late resize must not apply last-size guides. Recording catches up next render.
            if (!Supports(camera) || camera.pixelWidth != width || camera.pixelHeight != height)
            { Commands.Clear(); recorded = false; renderPending = false; ResetHistory(); return; }
            renderedView = camera.worldToCameraMatrix; renderedProjection = camera.projectionMatrix;
            if (HistoryValid && IsCameraCut()) ResetHistory();
            UsedHistory = TemporalBounce != null && HistoryValid;
            material.SetMatrix("_CBWorldToView",renderedView);
            material.SetMatrix("_CBViewToWorld",renderedView.inverse);
            material.SetVector("_CBNDCToView",NDC(renderedProjection));
            material.SetMatrix("_CBPreviousView",HistoryValid ? previousView : renderedView);
            material.SetVector("_CBPreviousNDC",NDC(HistoryValid ? previousProjection : renderedProjection));
            material.SetVector("_CBTemporal",new Vector4(UsedHistory ? 1 : 0,currentSettings.HistoryFrames.Value,0,0));
            material.SetInt("_CBSampleTemporal",TemporalBounce != null ? 1 : 0);
            material.SetInt("_CBSampleFrame",sampleIndex);
            renderPending = true;
            EnsureFirst();
        }

        private void AfterRender(Camera rendering)
        {
            if (rendering != camera || !recorded || !renderPending) return;
            renderPending = false; RenderSerial++;
            if (TemporalBounce == null) return;
            previousView = renderedView; previousProjection = renderedProjection;
            previousRenderTime = Time.realtimeSinceStartup; HistoryValid = true;
            // Keep all 32 bits; no float phase precision loss or short pattern loop.
            sampleIndex = unchecked(sampleIndex+1);
        }

        private static Vector4 NDC(Matrix4x4 p) => new Vector4(2/p.m00,-2/p.m11,(p.m02-1)/p.m00,(1-p.m12)/p.m11);
        private RenderTexture HistoryTarget(string name) => GpuAssets.Target("FTFX Bleed "+name,Source.width,Source.height,GraphicsFormat.R16G16B16A16_SFloat);
        private void ResetHistory() { HistoryValid = false; UsedHistory = false; sampleIndex = 0; }
        private bool IsCameraCut()
        {
            var oldWorld = previousView.inverse; var world = renderedView.inverse;
            return Time.realtimeSinceStartup-previousRenderTime>.5f ||
                Vector3.Distance(oldWorld.GetColumn(3),world.GetColumn(3))>Mathf.Max(1,currentSettings.Radius.Value*2) ||
                Vector3.Dot(oldWorld.GetColumn(2),world.GetColumn(2))<.8660254f ||
                Vector3.Dot(oldWorld.GetColumn(1),world.GetColumn(1))<.8660254f ||
                Mathf.Abs(renderedProjection.m00/previousProjection.m00-1)>.05f ||
                Mathf.Abs(renderedProjection.m11/previousProjection.m11-1)>.05f ||
                Mathf.Abs(renderedProjection.m02-previousProjection.m02)>.1f || Mathf.Abs(renderedProjection.m12-previousProjection.m12)>.1f;
        }
        private int ConfigurationHash(ColorBleedSettings s,bool gamma)
        {
            unchecked
            {
                int h = s.Strength.Value.GetHashCode();
                h=h*31+s.Radius.Value.GetHashCode(); h=h*31+s.DepthTolerance.Value.GetHashCode(); h=h*31+s.DonorLimit.Value.GetHashCode();
                h=h*31+s.BounceLimit.Value.GetHashCode(); h=h*31+s.Distance.Value.GetHashCode(); h=h*31+s.Samples.Value;
                h=h*31+s.HistoryFrames.Value; h=h*31+s.SpatialFilter.Value.GetHashCode(); h=h*31+s.Temporal.Value.GetHashCode();
                h=h*31+gamma.GetHashCode(); h=h*31+camera.nearClipPlane.GetHashCode(); h=h*31+camera.farClipPlane.GetHashCode();
                return h*31+(camera.targetTexture != null ? camera.targetTexture.GetInstanceID() : 0);
            }
        }
        private void ReleaseHistory()
        {
            ResetHistory(); GpuAssets.Release(ref historyDiagnostic); GpuAssets.Release(ref TemporalBounce); GpuAssets.Release(ref TemporalState);
            GpuAssets.Release(ref HistoryBounce); GpuAssets.Release(ref HistoryState); GpuAssets.Release(ref HistorySource);
            GpuAssets.Release(ref TemporalMoments); GpuAssets.Release(ref HistoryMoments);
        }

        internal void EnsureFirst()
        {
            var existing = camera.GetCommandBuffers(Event);
            // Unity returns new managed wrappers from GetCommandBuffers. Reference
            // equality would re-add our own native buffer and recursively apply bounce.
            if (existing.Length > 0 && existing[0].name == Commands.name) return;
            // Preserve every other buffer and its relative order. The native opaque buffer
            // contains fog/SSR, and actors must attenuate the completed bounce only once.
            foreach (var cb in existing) camera.RemoveCommandBuffer(Event,cb);
            camera.AddCommandBuffer(Event,Commands);
            foreach (var cb in existing) if (cb.name != Commands.name) camera.AddCommandBuffer(Event,cb);
        }

        private void ReleaseTargets()
        { ReleaseHistory(); GpuAssets.Release(ref Source); GpuAssets.Release(ref Guide); GpuAssets.Release(ref Bounce); GpuAssets.Release(ref FilteredBounce); GpuAssets.Release(ref debug); GpuAssets.Release(ref gammaScene); }

        public void Dispose()
        {
            Camera.onPreRender -= BeforeRender;
            Camera.onPostRender -= AfterRender;
            if (camera != null) camera.RemoveCommandBuffer(Event,Commands);
            Commands.Clear(); Commands.Dispose(); ReleaseTargets();
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
