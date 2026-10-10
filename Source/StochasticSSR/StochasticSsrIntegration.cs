using System;
using System.IO;
using System.Security.Cryptography;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal sealed class StochasticSsrIntegration : IDisposable
    {
        private readonly string path;
        private readonly SsrSettings settings;
        private AssetBundle bundle;
        private Texture2D noise;
        private SsrHooks hooks;
        private Harmony harmony;
        private WaterReflectionCapture waterCapture;
        private CloudSkyProbe cloudProbe;
        private readonly SsrOpticState opticState=new SsrOpticState();
        private bool previousEnabled, suspended;
        private int frames;
        internal StochasticSsrIntegration(string path, SsrSettings settings) { this.path=path;this.settings=settings; }

        internal void Initialize()
        {
            try
            {
                var renderer=AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.ScreenSpaceReflectionsRenderer") ?? throw new TypeLoadException("Native SSR renderer missing");
                Verify(renderer.Assembly.Location,"B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548");
                Verify(Path.Combine(Application.dataPath,"StreamingAssets","Windows","shaders"),"D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C");
                bundle=AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(path),"Assets","stochasticssr"));
                if(bundle==null)throw new InvalidOperationException("Missing Assets/stochasticssr bundle");
                var compute=bundle.LoadAsset<ComputeShader>("Assets/HiZ.compute");
                var blend=bundle.LoadAsset<Shader>("Assets/Support.shader");
                var reflection=bundle.LoadAsset<Shader>("Assets/Reflection.shader");
                var bytes=bundle.LoadAsset<TextAsset>("Assets/AMD/BlueNoise.bytes");
                if(compute==null || blend==null || !blend.isSupported || reflection==null || !reflection.isSupported || bytes==null || bytes.bytes.Length!=32768)
                    throw new InvalidOperationException("Stochastic SSR shader assets unavailable");
                noise=new Texture2D(128,128,TextureFormat.RG16,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
                noise.LoadRawTextureData(bytes.bytes);noise.Apply(false,true);
                hooks=new SsrHooks(renderer,compute,blend,reflection,noise) {
                    Settings=()=>suspended?new Options {Mode=SsrMode.Native}:settings.Read(),
                    IsMainCamera=c=>CameraManager.Instance!=null && ReferenceEquals(CameraManager.Instance.Camera,c),
                    IsOpticCamera=IsOpticCamera,
                    CameraOptions=(c,o)=>{RefreshOpticState();return SsrCameraPolicy.Effective(o,IsOpticCamera(c),o.Optics && o.ReduceMainInOptics && !hooks.HasFailed(opticState.Camera) && opticState.ReduceMain(Time.unscaledTime));},
                    CameraGeneration=c=>ReferenceEquals(c,opticState.Camera)?opticState.Generation:0,
                    Error=s=>Plugin.MyLog.LogError(s)
                };
                SsrHooks.Active=hooks;
                try {
                    waterCapture=new WaterReflectionCapture(bundle.LoadAsset<Shader>("Assets/WaterCapture.shader")) {
                        Requested=()=>!suspended && settings.NeedsWaterCapture,
                        IsMainCamera=c=>hooks.IsMainCamera(c) || IsOpticCamera(c)
                    };
                } catch(Exception error) {Plugin.MyLog.LogWarning("Water shoreline capture unavailable; ordinary SSSR retained. "+error);}
                AmbientSurfaceCapture.Requested=()=>!suspended && settings.Enabled.Value && settings.SurfaceColourInfluence.Value>0;
                AmbientSurfaceCapture.IsMainCamera=hooks.IsMainCamera;
                AmbientSurfaceCapture.IsOpticCamera=IsOpticCamera;
                try {AmbientSurfaceCapture.ConfigureOpticCapture(bundle.LoadAsset<Shader>("Assets/WaterCapture.shader"));}
                catch(Exception error){Plugin.MyLog.LogWarning("Optic surface colour capture unavailable; untinted reflections retained. "+error);}
                harmony=new Harmony(Plugin.Guid+".stochasticssr");
                var render=AccessTools.Method(renderer,"Render");
                if(render==null)throw new MissingMethodException("Native SSR render contract missing");
                harmony.Patch(render,prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.Before)),finalizer:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.After)));
                try {
                    cloudProbe=new CloudSkyProbe(reflection,AccessTools.TypeByName("AmbientLight")) {
                        Requested=()=>!suspended && settings.Enabled.Value && hooks.HasActiveRenderer(Time.frameCount)
                    };
                    harmony.Patch(cloudProbe.Target,prefix:new HarmonyMethod(typeof(CloudSkyProbe),nameof(CloudSkyProbe.Before)),
                        postfix:new HarmonyMethod(typeof(CloudSkyProbe),nameof(CloudSkyProbe.After)));
                    CloudSkyProbe.Active=cloudProbe;
                } catch(Exception error) {cloudProbe?.Dispose();cloudProbe=null;Plugin.MyLog.LogWarning("CloudSix native sky probe compatibility unavailable. "+error);}
                previousEnabled=settings.Enabled.Value;
                Plugin.MyLog.LogInfo("Stochastic SSR ready: separate ordinary optic GGX/NRD; scoped main-camera resolution budget; vision optics and disabled SSR retain native behavior.");
            }
            catch { Dispose();throw; }
        }
        private void RefreshOpticState()
        {
            var manager=CameraManager.Instance?.OpticCameraManager;
            var camera=manager?.Camera;var sight=manager?.CurrentOpticSight;var scope=sight!=null?sight.ScopeData:null;
            var night=camera!=null?camera.GetComponent<BSG.CameraEffects.NightVision>():null;
            var thermal=camera!=null?camera.GetComponent<ThermalVision>():null;
            bool eligible=!suspended && settings.Enabled.Value && settings.Optics.Value && SsrCameraPolicy.EligibleOptic(
                camera!=null,camera!=null && camera.isActiveAndEnabled,scope!=null,
                scope!=null && scope.NightVisionData!=null && scope.NightVisionData.NightVision,
                scope!=null && scope.ThermalVisionData!=null && scope.ThermalVisionData.ThermalVision,
                night!=null && night.isActiveAndEnabled && night.On,thermal!=null && thermal.isActiveAndEnabled && thermal.On,
                camera!=null && SsrPipeline.Supports(camera,camera.pixelWidth,camera.pixelHeight));
            opticState.Observe(Time.frameCount,Time.unscaledTime,camera,sight!=null?sight.GetInstanceID():0,eligible);
        }
        private bool IsOpticCamera(Camera camera)
        {
            RefreshOpticState();
            return !suspended && settings.Enabled.Value && settings.Optics.Value && opticState.Active && ReferenceEquals(camera,opticState.Camera);
        }
        private static void Verify(string file, string expected)
        {
            using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=expected)
                    throw new InvalidOperationException("Unsupported SSR ABI: "+file);
        }
        internal void Update()
        {
            if(hooks==null)return;
            AmbientSurfaceCapture.Prune();
            if(frames%300==0)waterCapture?.Prune();
            if(previousEnabled!=settings.Enabled.Value){hooks.Suspend();opticState.Reset();previousEnabled=settings.Enabled.Value;}
            if(++frames%300==0)hooks.Prune();
        }
        internal void Suspend() { suspended=true;hooks?.Suspend();opticState.Reset(); }
        internal void Resume() { suspended=false; }
        public void Dispose()
        {
            AmbientSurfaceCapture.Dispose();
            waterCapture?.Dispose();waterCapture=null;
            harmony?.UnpatchSelf();harmony=null;hooks?.Dispose();hooks=null;
            cloudProbe?.Dispose();cloudProbe=null;
            if(noise!=null)UnityEngine.Object.Destroy(noise);noise=null;
            if(bundle!=null)bundle.Unload(true);bundle=null;
        }
    }
}
