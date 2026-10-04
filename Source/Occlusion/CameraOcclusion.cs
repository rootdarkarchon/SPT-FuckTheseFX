using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    internal sealed class CameraOcclusion : IDisposable
    {
        private readonly Camera camera;
        private readonly GpuAssets assets;
        private readonly Material composite;
        private OcclusionPipeline primary, actor;
        private ColorBleedPipeline bleed;
        private FoliageOcclusion foliageAO;
        private bool bleedFailed, bleedUnsupportedWarned;
        private static string lastBleedUnsupportedReason;
        private RenderTexture resolved, originalAlpha, actorMask, debug, rawDebug, diffuseOcclusion;
        private readonly CommandBuffer reset, capture, calculate, actors, cleanup;
        private int width, height;
        private CameraEvent calculationEvent;
        internal int LastQuality { get; private set; }
        internal RenderTexture DebugTexture => debug;
        internal RenderTexture ColorBleedDebugTexture => bleed?.DebugTexture;
        internal RenderTexture ColorBleedHistoryTexture => bleed?.HistoryDiagnosticTexture;
        internal RenderTexture LightingDebugTexture { get; private set; }
        internal FoliageDiagnostics Foliage { get; private set; }
        internal FoliageLive Live { get; private set; }
        internal bool Active { get; private set; }
        internal bool AmbientActive { get; private set; }
        internal const string Prefix = "_FTFX_CA_";

        internal CameraOcclusion(Camera camera, GpuAssets assets)
        {
            this.camera = camera; this.assets = assets;
            composite = new Material(assets.Composite) { hideFlags = HideFlags.HideAndDontSave };
            reset = new CommandBuffer { name = "FTFX AO - reset" };
            capture = new CommandBuffer { name = "FTFX AO - material and actor masks" };
            calculate = new CommandBuffer { name = "FTFX XeGTAO - primary" };
            actors = new CommandBuffer { name = "FTFX XeGTAO - actor pass" };
            cleanup = new CommandBuffer { name = "FTFX AO - release inputs" };
            ResetGlobals(reset); ResetGlobals(cleanup);
            camera.AddCommandBuffer(CameraEvent.BeforeGBuffer,reset);
            camera.AddCommandBuffer(CameraEvent.AfterGBuffer,capture);
            camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,actors);
            camera.AddCommandBuffer(CameraEvent.AfterEverything,cleanup);
            calculationEvent = CalculationEvent;
            camera.AddCommandBuffer(calculationEvent,calculate);
        }

        internal static void ResetGlobals(CommandBuffer cb)
        {
            cb.SetGlobalFloat(Prefix+"DiffuseCaptured",0);
            cb.SetGlobalTexture(Prefix+"DiffuseOcclusion",Texture2D.whiteTexture);
            cb.SetGlobalFloat(Prefix+"BentAvailable",0);
            cb.SetGlobalFloat(Prefix+"MaterialCaptured",0); cb.SetGlobalFloat(Prefix+"SeparateAOAvailable",0);
            cb.SetGlobalTexture(Prefix+"OriginalOcclusion",Texture2D.whiteTexture); cb.SetGlobalTexture(Prefix+"ScreenAO",Texture2D.whiteTexture);
        }
        internal static void ResetGlobals()
        {
            Shader.SetGlobalFloat(Prefix+"DiffuseCaptured",0);
            Shader.SetGlobalTexture(Prefix+"DiffuseOcclusion",Texture2D.whiteTexture);
            Shader.SetGlobalFloat(Prefix+"BentAvailable",0);
            Shader.SetGlobalFloat(Prefix+"MaterialCaptured",0); Shader.SetGlobalFloat(Prefix+"SeparateAOAvailable",0);
            Shader.SetGlobalTexture(Prefix+"OriginalOcclusion",Texture2D.whiteTexture); Shader.SetGlobalTexture(Prefix+"ScreenAO",Texture2D.whiteTexture);
        }

        internal void Record(OcclusionSettings settings, int quality, bool ambientAvailable, bool mainCamera = false)
        {
            Active = false; LightingDebugTexture = null; LastQuality = quality;
            capture.Clear(); calculate.Clear(); actors.Clear();
            bool bleedView = settings.View.Value == OcclusionView.ScreenSpaceContactGI || settings.View.Value == OcclusionView.ScreenSpaceContactGIHistory;
            bool useLive = settings.FoliageLive && mainCamera && camera.allowHDR && ambientAvailable && settings.Ambient.Value && settings.AmbientStrengthFraction > 0;
            bool useFoliage = mainCamera && camera.allowHDR && settings.FoliageExtraAOFraction > 0 &&
                (settings.View.Value == OcclusionView.Normal || settings.FoliageCapture || useLive);
            bool requestBleed = settings.Bleed.Enabled.Value && (settings.Bleed.Strength.Value > 0 || bleedView);
            if (!requestBleed) { bleedFailed = false; bleedUnsupportedWarned = false; }
            bool useBleed = requestBleed && mainCamera && (settings.View.Value == OcclusionView.Normal || bleedView || settings.FoliageCapture || settings.FoliageLive) && !bleedFailed && !(useLive && settings.FoliageBypassGI.Value);
            if (useBleed && !ColorBleedPipeline.Supports(camera))
            {
                useBleed = false;
                if (!bleedUnsupportedWarned)
                {
                    bleedUnsupportedWarned = true;
                    string reason = "path="+camera.actualRenderingPath+", HDR="+camera.allowHDR+", color space="+QualitySettings.activeColorSpace+
                        ", API="+SystemInfo.graphicsDeviceType+", RGBAHalf="+SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat,FormatUsage.Render)+
                        ", RGBA8="+SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm,FormatUsage.Render);
                    if (lastBleedUnsupportedReason != reason)
                    {
                        lastBleedUnsupportedReason = reason;
                        Plugin.MyLog.LogWarning("Screen Space Contact GI unavailable (requires HDR deferred D3D11 and supported targets): "+reason+". Existing AO remains active.");
                    }
                }
            }
            if (!useBleed) { bleed?.Dispose(); bleed = null; }
            if(width != camera.pixelWidth || height != camera.pixelHeight || resolved == null || !resolved.IsCreated())
            {
                ReleaseTargets(); width = camera.pixelWidth; height = camera.pixelHeight;
                resolved = Target("Primary AO",GraphicsFormat.R16G16_SFloat);
            }
            var nextEvent = CalculationEvent;
            if(nextEvent != calculationEvent)
            {
                camera.RemoveCommandBuffer(calculationEvent,calculate); calculationEvent = nextEvent;
                camera.AddCommandBuffer(calculationEvent,calculate);
            }
            var size = new Vector4(width,height,camera.farClipPlane/65504f,settings.Denoise.Value == 0 ? 1.5f : 1);
            composite.SetVector("_Size",size);
            var distanceFade = settings.DistanceFade;
            composite.SetVector("_AOFade",distanceFade);
            composite.SetVector("_ActorSettings",new Vector4(settings.ActorStrength.Value,settings.ActorResponse.Value,settings.ActorCutoff.Value,0));
            composite.SetVector("_Debug",new Vector4((int)settings.View.Value,settings.DepthStart.Value,settings.DepthRange.Value,0));
            AmbientActive = ambientAvailable && settings.Ambient.Value && settings.AmbientStrengthFraction > 0;
            bool reflectionOcclusion = AmbientActive && settings.ReflectionOcclusion.Value;
            composite.SetFloat("_ReflectionOcclusion",reflectionOcclusion ? 1 : 0);
            bool bent = AmbientActive && settings.BentNormals.Value;
            if(useLive)
            {
                if(Live == null) Live = new FoliageLive(camera,assets,width,height);
                Live.BeginFrame(calculate);
            }
            else { Live?.Dispose(); Live = null; }
            composite.SetFloat("_BentNormals",bent ? 1 : 0);
            // Invert LoadNormal's XeGTAO Y/Z reflection before rotating to world space.
            var bentToWorld = camera.cameraToWorldMatrix * Matrix4x4.Scale(new Vector3(1,-1,-1));
            composite.SetMatrix("_BentToWorld",bentToWorld);
            if(settings.FoliageCapture && mainCamera)
            {
                if(Foliage == null) Foliage = new FoliageDiagnostics(camera,width,height);
                Foliage.BeginFrame(calculate,settings);
            }
            else { Foliage?.Dispose(); Foliage = null; }
            if(AmbientActive || useFoliage)
            {
                if(originalAlpha == null) originalAlpha = Target("Original material AO",GraphicsFormat.R8_UNorm);
                capture.SetGlobalTexture("_FTFXMaterialInput",BuiltinRenderTextureType.GBuffer0);
                capture.SetRenderTarget(originalAlpha);
                Draw(capture,7);
                capture.SetGlobalTexture(Prefix+"OriginalOcclusion",originalAlpha);
                capture.SetGlobalFloat(Prefix+"MaterialCaptured",1);
            }
            else GpuAssets.Release(ref originalAlpha);
            bool useActors = settings.Actors.Value && settings.ActorStrength.Value > 0;
            if(useActors || useBleed || useLive || useFoliage)
            {
                if(actorMask == null) actorMask = Target("Actor stencil",GraphicsFormat.R8_UNorm);
                capture.SetRenderTarget(actorMask,BuiltinRenderTextureType.CameraTarget);
                capture.ClearRenderTarget(false,true,Color.black);
                Draw(capture,3);
            }
            capture.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            if(primary == null) primary = new OcclusionPipeline(assets.Compute,false);
            var p = settings.Primary;
            primary.Prepare(width,height,p.Denoise);
            primary.Record(calculate,camera,quality,p,bentNormals:bent);
            Resolve(calculate,primary.Result,resolved,primary.Depth,size);
            if(AmbientActive)
            {
                calculate.SetGlobalTexture(Prefix+"ScreenAO",resolved);
                calculate.SetGlobalVector(Prefix+"AODecode",new Vector4(2,.8f,size.z,0));
                calculate.SetGlobalVector(Prefix+"AOFade",distanceFade);
                calculate.SetGlobalFloat(Prefix+"SeparateAOAvailable",1);
                if(bent)
                {
                    calculate.SetGlobalTexture(Prefix+"BentAO",primary.Result);
                    calculate.SetGlobalVector(Prefix+"BentSize",new Vector4(width,height,0,0));
                    calculate.SetGlobalMatrix(Prefix+"BentToWorld",bentToWorld);
                    calculate.SetGlobalFloat(Prefix+"BentAvailable",1);
                }
            }
            calculate.BeginSample("FTFX Primary composite");
            var primaryProperties = new MaterialPropertyBlock(); primaryProperties.SetTexture("_PrimaryAO",resolved);
            primaryProperties.SetTexture("_XeAO",primary.Result);
            if(AmbientActive)
            {
                // Protect diffuse material*screen AO before later decals can overwrite GBuffer0 alpha.
                // CustomAmbient uses this same input with reflection relief both on and off.
                if(diffuseOcclusion == null) diffuseOcclusion = Target("Diffuse material AO",GraphicsFormat.R8_UNorm);
                calculate.SetGlobalTexture("_FTFXMaterialInput",BuiltinRenderTextureType.GBuffer0);
                calculate.SetRenderTarget(diffuseOcclusion); Draw(calculate,10,primaryProperties);
                calculate.SetGlobalTexture(Prefix+"DiffuseOcclusion",diffuseOcclusion);
                calculate.SetGlobalFloat(Prefix+"DiffuseCaptured",1);
            }
            else GpuAssets.Release(ref diffuseOcclusion);
            if(!camera.allowHDR)
            {
                int emission = Shader.PropertyToID("_FTFX_EmissionCopy");
                calculate.GetTemporaryRT(emission,width,height,0,FilterMode.Point,RenderTextureFormat.ARGB2101010,RenderTextureReadWrite.Linear);
                calculate.Blit(BuiltinRenderTextureType.GBuffer3,emission);
                calculate.SetGlobalTexture("_Emission",emission);
                calculate.SetRenderTarget(new RenderTargetIdentifier[] {BuiltinRenderTextureType.GBuffer0,BuiltinRenderTextureType.GBuffer3},BuiltinRenderTextureType.CameraTarget);
                Draw(calculate,2,primaryProperties);
                calculate.ReleaseTemporaryRT(emission);
            }
            else
            {
                calculate.SetRenderTarget(new RenderTargetIdentifier[] {BuiltinRenderTextureType.GBuffer0,BuiltinRenderTextureType.CameraTarget},BuiltinRenderTextureType.CameraTarget);
                Draw(calculate,1,primaryProperties);
            }
            calculate.EndSample("FTFX Primary composite");
            if(settings.FoliageCapture)
            {
                GpuAssets.Release(ref debug); GpuAssets.Release(ref rawDebug);
                LightingDebugTexture = Foliage?.AO;
            }
            else if(settings.View.Value != OcclusionView.Normal && !bleedView && !settings.FoliageLive)
            {
                if(debug == null) debug = GpuAssets.Target("FTFX AO diagnostics",width,height,GraphicsFormat.R8G8B8A8_UNorm,true);
                if(settings.LightingDebug)
                {
                    LightingDebugTexture = debug;
                    // A real ambient fragment must overwrite this sentinel each frame.
                    calculate.SetRenderTarget(debug); calculate.ClearRenderTarget(false,true,Color.cyan);
                }
                else if(settings.View.Value == OcclusionView.SecondPassAO)
                { calculate.SetRenderTarget(debug); calculate.ClearRenderTarget(false,true,Color.white); }
                else
                {
                    if(settings.View.Value == OcclusionView.BeforeBlur)
                    {
                        if(rawDebug == null) rawDebug = Target("Raw AO diagnostics",GraphicsFormat.R16G16_SFloat);
                        var rawSize = size; rawSize.w = 1.5f;
                        Resolve(calculate,primary.Raw,rawDebug,primary.Depth,rawSize);
                        primaryProperties.SetTexture("_PrimaryAO",rawDebug);
                    }
                    calculate.SetRenderTarget(debug); Draw(calculate,5,primaryProperties);
                }
            }
            else { GpuAssets.Release(ref debug); GpuAssets.Release(ref rawDebug); }
            calculate.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            if(useActors)
            {
                if(actor == null) actor = new OcclusionPipeline(assets.Compute,true);
                actor.Prepare(width,height,1);
                actor.Record(actors,camera,quality,settings.Actor,actorMask,settings.ActorCutoff.Value);
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_XeAO",actor.Result); properties.SetTexture("_XeDepth",actor.Depth); properties.SetTexture("_ActorMask",actorMask);
                actors.BeginSample("FTFX Actor composite");
                actors.SetRenderTarget(BuiltinRenderTextureType.CameraTarget); Draw(actors,4,properties);
                if(settings.View.Value == OcclusionView.SecondPassAO) { actors.SetRenderTarget(debug); Draw(actors,6,properties); }
                actors.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                actors.EndSample("FTFX Actor composite");
            }
            else { actor?.Dispose(); actor = null; if (!useBleed && !useLive && !useFoliage) GpuAssets.Release(ref actorMask); }
            if (useBleed)
            {
                try
                {
                    if (bleed == null) bleed = new ColorBleedPipeline(camera,assets);
                    bleed.Record(settings.Bleed,primary.Depth,resolved,actorMask,settings.View.Value == OcclusionView.ScreenSpaceContactGI,settings.ColorBleedGain.Value,settings.View.Value == OcclusionView.ScreenSpaceContactGIHistory);
                }
                catch (Exception error)
                {
                    bleed?.Dispose(); bleed = null; bleedFailed = true;
                    Plugin.MyLog.LogWarning("Screen Space Contact GI failed; existing AO remains active. Toggle Screen Space Contact GI off/on to retry. " + error);
                }
            }
            Foliage?.RecordStages();
            if(useFoliage)
            {
                if(foliageAO == null) foliageAO = new FoliageOcclusion(camera,assets);
                foliageAO.Record(settings,resolved,originalAlpha,actorMask);
            }
            else { foliageAO?.Dispose(); foliageAO = null; }
            // The live BeforeOpaque snapshot must include the surface AO that feeds native fog.
            Live?.Record(settings,resolved,originalAlpha,actorMask);
            Active = true;
        }
        private void Resolve(CommandBuffer cb, RenderTexture input, RenderTexture output, RenderTexture depth, Vector4 size)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_XeAO",input); properties.SetTexture("_XeDepth",depth); properties.SetVector("_Size",size);
            cb.SetRenderTarget(output); Draw(cb,0,properties);
        }
        private void Draw(CommandBuffer cb,int pass,MaterialPropertyBlock properties = null) => cb.DrawMesh(assets.Quad,Matrix4x4.identity,composite,0,pass,properties);
        private RenderTexture Target(string name, GraphicsFormat format) => GpuAssets.Target("FTFX "+name,width,height,format);
        private static CameraEvent CalculationEvent => GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredReflections) != BuiltinShaderMode.Disabled
            ? CameraEvent.BeforeReflections : CameraEvent.BeforeLighting;
        private void ReleaseTargets()
        {
            Foliage?.Dispose(); Foliage = null;
            Live?.Dispose(); Live = null;
            foliageAO?.Dispose(); foliageAO = null;
            GpuAssets.Release(ref resolved); GpuAssets.Release(ref originalAlpha); GpuAssets.Release(ref actorMask);
            GpuAssets.Release(ref debug); GpuAssets.Release(ref rawDebug);
            GpuAssets.Release(ref diffuseOcclusion);
        }
        public void Dispose()
        {
            Active = AmbientActive = false; LightingDebugTexture = null;
            if(camera != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer,reset); camera.RemoveCommandBuffer(CameraEvent.AfterGBuffer,capture);
                camera.RemoveCommandBuffer(calculationEvent,calculate); camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,actors);
                camera.RemoveCommandBuffer(CameraEvent.AfterEverything,cleanup);
            }
            reset.Dispose(); capture.Dispose(); calculate.Dispose(); actors.Dispose(); cleanup.Dispose();
            bleed?.Dispose(); bleed = null;
            primary?.Dispose(); actor?.Dispose(); ReleaseTargets();
            UnityEngine.Object.DestroyImmediate(composite); ResetGlobals();
        }
    }
}
