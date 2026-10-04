using System;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    internal sealed class GpuAssets : IDisposable
    {
        internal readonly AssetBundle Bundle;
        internal readonly ComputeShader Compute;
        internal readonly Shader Composite, Ambient, ColorBleed;
        internal readonly ComputeShader ColorBleedCompute;
        internal readonly Texture2D ColorBleedNoise;
        internal readonly Mesh Quad;

        internal GpuAssets(string pluginPath)
        {
            try
            {
                Bundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(pluginPath), "Assets", "occlusion"));
                if (Bundle == null) throw new InvalidOperationException("Missing Assets/occlusion bundle.");
                Compute = Bundle.LoadAsset<ComputeShader>("Assets/XeGTAO/XeGTAO.compute");
                Composite = Bundle.LoadAsset<Shader>("Assets/XeGTAO/Composite.shader");
                Ambient = Bundle.LoadAsset<Shader>("Assets/CustomAmbient.shader");
                ColorBleed = Bundle.LoadAsset<Shader>("Assets/XeGTAO/ColorBleed.shader");
                ColorBleedCompute = Bundle.LoadAsset<ComputeShader>("Assets/XeGTAO/ContactGI.compute");
                var noise = Bundle.LoadAsset<TextAsset>("Assets/AMD/BlueNoise.bytes");
                if(noise!=null && noise.bytes.Length==32768) {
                    ColorBleedNoise=new Texture2D(128,128,TextureFormat.RG16,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Repeat};
                    ColorBleedNoise.LoadRawTextureData(noise.bytes);ColorBleedNoise.Apply(false,true);
                }
                if (Compute == null || Composite == null || !Composite.isSupported || Composite.passCount < 11)
                    throw new InvalidOperationException("Unsupported or mismatched XeGTAO bundle.");
                Quad = new Mesh { name = "FTFX Occlusion Quad", hideFlags = HideFlags.HideAndDontSave };
                Quad.vertices = new[] { new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) };
                Quad.uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) };
                Quad.triangles = new[] { 0,1,2,0,2,3 };
                Quad.UploadMeshData(true);
            }
            catch { Dispose(); throw; }
        }

        internal static bool Supports(Camera camera) => camera != null &&
            camera.actualRenderingPath == RenderingPath.DeferredShading && !camera.orthographic && !camera.stereoEnabled &&
            camera.pixelWidth >= 16 && camera.pixelHeight >= 16 && SystemInfo.supportsComputeShaders &&
            SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 && SystemInfo.supportedRenderTargetCount >= 2 &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, FormatUsage.LoadStore) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R32_UInt, FormatUsage.LoadStore) &&
            SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.LoadStore) &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf);

        internal static RenderTexture Target(string name, int width, int height, GraphicsFormat format, bool random = false, bool mips = false)
        {
            var descriptor = new RenderTextureDescriptor(width, height) { graphicsFormat = format, depthBufferBits = 0,
                msaaSamples = 1, enableRandomWrite = random, useMipMap = mips, autoGenerateMips = false, mipCount = mips ? 5 : 1, sRGB = false };
            var target = new RenderTexture(descriptor) { name = name, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (target.Create()) return target;
            UnityEngine.Object.DestroyImmediate(target);
            throw new InvalidOperationException("Cannot allocate " + name);
        }
        internal static void Release(ref RenderTexture target)
        {
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            target = null;
        }
        public void Dispose()
        {
            if(ColorBleedNoise!=null)UnityEngine.Object.DestroyImmediate(ColorBleedNoise);
            if (Quad != null) UnityEngine.Object.DestroyImmediate(Quad);
            if (Bundle != null) Bundle.Unload(true);
        }
    }
}
