using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx.Bootstrap;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace FuckTheseFX.Source.StochasticSSR
{
    internal sealed class StochasticSsrIntegration : IDisposable
    {
        internal const string SsrsixGuid="com.matsix.ssrsix";
        private readonly string path;
        private readonly SsrSettings settings;
        private AssetBundle bundle;
        private Texture2D noise;
        private SsrHooks hooks;
        private Harmony harmony;
        private bool previousEnabled, suspended;
        private int frames;
        internal StochasticSsrIntegration(string path, SsrSettings settings) { this.path=path;this.settings=settings; }

        internal static bool HasConflict() => Chainloader.PluginInfos.ContainsKey(SsrsixGuid);
        internal void Initialize()
        {
            // The soft dependency makes SSRSix's registration available before Awake.
            // Do not load assets or patch SSR at all when another owner is installed.
            if(HasConflict())
            {
                Plugin.MyLog.LogInfo("SSRSix is installed: FTFX Stochastic SSR is bypassed. Other FX remain active.");
                return;
            }
            try
            {
                var renderer=AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.ScreenSpaceReflectionsRenderer") ?? throw new TypeLoadException("Native SSR renderer missing");
                Verify(renderer.Assembly.Location,"B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548");
                Verify(Path.Combine(Application.dataPath,"StreamingAssets","Windows","shaders"),"D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C");
                bundle=AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(path),"Assets","stochasticssr"));
                if(bundle==null)throw new InvalidOperationException("Missing Assets/stochasticssr bundle");
                var compute=bundle.LoadAsset<ComputeShader>("Assets/HiZ.compute");
                var blend=bundle.LoadAsset<Shader>("Assets/Support.shader");
                var amd=bundle.LoadAsset<ComputeShader>("Assets/FidelityDenoiser.compute");
                var bytes=bundle.LoadAsset<TextAsset>("Assets/AMD/BlueNoise.bytes");
                if(compute==null || blend==null || !blend.isSupported || amd==null || bytes==null || bytes.bytes.Length!=32768)
                    throw new InvalidOperationException("Stochastic SSR shader assets unavailable");
                noise=new Texture2D(128,128,TextureFormat.RG16,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
                noise.LoadRawTextureData(bytes.bytes);noise.Apply(false,true);
                hooks=new SsrHooks(renderer,compute,blend,amd,noise) {
                    Settings=()=>suspended?new Options {Mode=SsrMode.Native}:settings.Read(),
                    IsMainCamera=c=>CameraManager.Instance!=null && ReferenceEquals(CameraManager.Instance.Camera,c),
                    Error=s=>Plugin.MyLog.LogError(s)
                };
                SsrHooks.Active=hooks;
                harmony=new Harmony(Plugin.Guid+".stochasticssr");
                var blit=AccessTools.Method(typeof(RuntimeUtilities),"BlitFullscreenTriangle",new[]{typeof(CommandBuffer),typeof(RenderTargetIdentifier),typeof(RenderTargetIdentifier),typeof(PropertySheet),typeof(int),typeof(bool),typeof(Rect?)});
                var render=AccessTools.Method(renderer,"Render");
                if(blit==null || render==null)throw new MissingMethodException("Native SSR render contract missing");
                harmony.Patch(blit,prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.TracePass)));
                harmony.Patch(render,prefix:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.Before)),finalizer:new HarmonyMethod(typeof(SsrHooks),nameof(SsrHooks.After)));
                previousEnabled=settings.Enabled.Value;
                Plugin.MyLog.LogInfo("Stochastic SSR ready. Native reconstruction by default; optics and disabled SSR retain native behavior.");
            }
            catch { Dispose();throw; }
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
            if(HasConflict()){Dispose();return;}
            if(previousEnabled!=settings.Enabled.Value){hooks.Suspend();previousEnabled=settings.Enabled.Value;}
            if(++frames%300==0)hooks.Prune();
        }
        internal void Suspend() { suspended=true;hooks?.Suspend(); }
        internal void Resume() { suspended=false; }
        public void Dispose()
        {
            harmony?.UnpatchSelf();harmony=null;hooks?.Dispose();hooks=null;
            if(noise!=null)UnityEngine.Object.Destroy(noise);noise=null;
            if(bundle!=null)bundle.Unload(true);bundle=null;
        }
    }
}
