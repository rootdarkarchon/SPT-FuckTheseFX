using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FuckTheseFX.Source.DistantShadows;

public static class DistantShadowValidation
{
    static Camera camera;
    static Mesh quad;
    static Material native, replacement;
    static MaterialPropertyBlock properties;
    static ComputeBuffer halton;
    static int checks;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static readonly string[] keywords = { "DISTANT_SHADOWS_BLEND", "ENABLE_PARALLAX", "USE_GAUSS_DISTRIBUTION", "LOWRES_DEPTH", "RENDER_FOR_SCOPE" };
    static void Check(bool value, string text)
    {
        if (!value) throw new Exception(text);
        checks++;
    }
    static Texture2D Texture(int width, int height, Func<int, int, float> value)
    {
        var t = new Texture2D(width, height, TextureFormat.RFloat, false, true);
        t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            pixels[y * width + x] = new Color(value(x, y), 0, 0, 1);
        t.SetPixels(pixels); t.Apply(); owned.Add(t); return t;
    }
    static void SetSize(int width, int height)
    {
        if (camera.targetTexture != null) { var old = camera.targetTexture; camera.targetTexture = null; old.Release(); UnityEngine.Object.DestroyImmediate(old); }
        camera.targetTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        camera.targetTexture.Create();
        properties.SetTexture("_CameraDepthTexture", Texture(width, height, (x, y) => .012f + x * .00009f + y * .0001f));
        properties.SetTexture("_LowResDepth", Texture(width, height, (x, y) => .025f + y * .00007f));
    }
    static Color[] Draw(Material material, int count, int repeats = 1)
    {
        properties.SetInt("_FTFX_DSCount", count);
        properties.SetVectorArray("_FTFX_DSTaps", DistantShadowSamples.Taps(count));
        using (var cb = new CommandBuffer())
        {
            cb.SetRenderTarget(camera.targetTexture);
            for (int i = 0; i < repeats; i++) cb.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, properties);
            camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
            camera.Render();
            camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        }
        var previous = RenderTexture.active; RenderTexture.active = camera.targetTexture;
        var read = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, read.width, read.height), 0, 0); read.Apply();
        var result = read.GetPixels(); UnityEngine.Object.DestroyImmediate(read); RenderTexture.active = previous;
        return result;
    }
    static double Error(Color[] a, Color[] b) => a.Select((c, i) => Math.Abs(c.r - b[i].r)).Max();
    static Color[] Read(RenderTexture target)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        var read = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, read.width, read.height), 0, 0); read.Apply();
        var pixels = read.GetPixels(); UnityEngine.Object.DestroyImmediate(read); RenderTexture.active = previous;
        return pixels;
    }
    static void CheckRecordedBudgets()
    {
        SetSize(65, 37);
        var expected8 = Draw(replacement, 8); var expected12 = Draw(replacement, 12);
        Check(Error(expected8, expected12) > .001, "Sample count changes actual filtered pixels");
        var inputs = new MaterialPropertyBlock();
        inputs.SetBuffer("HaltonOffsets", halton);
        foreach (string name in new[] { "GlobalShadowSampled", "GlobalShadowSampled2", "_CameraDepthTexture", "_LowResDepth" }) inputs.SetTexture(name, properties.GetTexture(name));
        foreach (string name in new[] { "GlobalShadowWorldToScreen", "GlobalShadowWorldToScreen2" }) inputs.SetMatrix(name, properties.GetMatrix(name));
        foreach (string name in new[] { "GlobalShadowTexelSize", "GlobalShadowSettings", "GlobalShadowL0", "GlobalShadowL0Old", "GlobalShadowL1", "GlobalShadowProj" }) inputs.SetVector(name, properties.GetVector(name));
        inputs.SetFloat("ParallaxSearchMul", properties.GetFloat("ParallaxSearchMul"));
        var first = new RenderTexture(camera.targetTexture.descriptor); first.Create();
        var second = new RenderTexture(camera.targetTexture.descriptor); second.Create();
        using (var cb = new CommandBuffer())
        {
            cb.SetRenderTarget(first);
            cb.SetGlobalInt("_FTFX_DSCount", 8); cb.SetGlobalVectorArray("_FTFX_DSTaps", DistantShadowSamples.Taps(8));
            cb.DrawMesh(quad, Matrix4x4.identity, replacement, 0, 0, inputs);
            cb.SetRenderTarget(second);
            cb.SetGlobalInt("_FTFX_DSCount", 12); cb.SetGlobalVectorArray("_FTFX_DSTaps", DistantShadowSamples.Taps(12));
            cb.DrawMesh(quad, Matrix4x4.identity, replacement, 0, 0, inputs);
            // Simulate settings/camera state changing after both draws were recorded.
            Shader.SetGlobalInt("_FTFX_DSCount", 1); Shader.SetGlobalVectorArray("_FTFX_DSTaps", DistantShadowSamples.Taps(1));
            camera.AddCommandBuffer(CameraEvent.AfterEverything, cb); camera.Render(); camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        }
        Check(Error(expected8, Read(first)) < .00001, "Recorded 8-sample budget survives later global changes");
        Check(Error(expected12, Read(second)) < .00001, "Second draw receives its own 12-sample budget");
        first.Release(); second.Release(); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
        Check(DistantShadowSamples.Select(0, 7, 11, 17) == 7 && DistantShadowSamples.Select(1, 7, 11, 17) == 11 && DistantShadowSamples.Select(2, 7, 11, 17) == 17, "All three independent resolution budgets");
        Check(DistantShadowSamples.Select(0, 0, 1, 1) == 1 && DistantShadowSamples.Select(2, 1, 1, 99) == 25, "Budget clamps");
    }
    // Exact offsets initialized by the pinned EFT 40743 DistantShadow.Update.
    static Vector2[] NativeOpticOffsets() => new[] {
new Vector2(-0.74528f, 0.10120783f),
new Vector2(-0.34528f, 0.38692212f),
new Vector2(0.05472f, 0.6726364f),
new Vector2(0.45472f, 0.95835066f),
new Vector2(0.85472f, -0.7151187f),
new Vector2(-0.66528f, -0.4294044f),
new Vector2(-0.26528f, -0.14369012f),
new Vector2(0.13472f, 0.14202416f),
new Vector2(0.53472f, 0.42773843f),
new Vector2(0.93472f, 0.71345276f),
new Vector2(-0.96928f, 0.999167f),
new Vector2(-0.56928f, -0.999881f),
new Vector2(-0.16928f, -0.7141667f),
new Vector2(0.23072f, -0.42845243f),
new Vector2(0.63072f, -0.14273815f),
new Vector2(-0.88928f, 0.14297614f),
new Vector2(-0.48928f, 0.42869043f),
new Vector2(-0.08928f, 0.7144047f),
new Vector2(0.31072f, -0.95906466f),
new Vector2(0.71072f, -0.6733504f),
new Vector2(-0.80928f, -0.3876361f),
new Vector2(-0.40928f, -0.10192182f),
new Vector2(-0.00928f, 0.18379247f),
new Vector2(0.39072f, 0.46950674f),
new Vector2(0.79072f, 0.755221f),
    };
    public static void Run()
    {
        try
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/DistantShadowSampling.shader");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compilation");
            var manifest = BuildPipeline.BuildAssetBundles("AssetBundles", new[] { new AssetBundleBuild {
                assetBundleName = "distantshadows", assetNames = new[] { "Assets/DistantShadowSampling.shader" }
            } }, BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            Check(manifest != null, "Bundle build");
            var sourceBundle = AssetBundle.LoadFromFile("native-reference");
            Check(sourceBundle != null, "Local installed-shader reference loads");
            native = new Material(sourceBundle.LoadAllAssets<Shader>().Single(s => s.name == "EFT/DistantShadowComputeMask"));
            var resultBundle = AssetBundle.LoadFromFile("AssetBundles/distantshadows");
            replacement = new Material(resultBundle.LoadAllAssets<Shader>().Single());
            Check(native.shader.isSupported && replacement.shader.isSupported, "D3D11 shaders supported");
            camera = new GameObject("Distant shadow validation").AddComponent<Camera>();
            camera.cullingMask = 0; camera.clearFlags = CameraClearFlags.Nothing;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.allowHDR = true; camera.allowMSAA = false;
            quad = new Mesh { vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one }, triangles = new[] { 0, 1, 3, 3, 2, 0 } };
            properties = new MaterialPropertyBlock();
            halton = new ComputeBuffer(25, 8);
            halton.SetData(NativeOpticOffsets());
            properties.SetBuffer("HaltonOffsets", halton);
            Shader.SetGlobalBuffer("HaltonOffsets", halton);
            properties.SetTexture("GlobalShadowSampled", Texture(64, 64, (x, y) => (x > y + 3 || x % 13 < 3) ? .35f : .65f));
            properties.SetTexture("GlobalShadowSampled2", Texture(64, 64, (x, y) => (y > x - 5 || y % 11 < 2) ? .6f : .3f));
            var projection = Matrix4x4.Scale(new Vector3(.17f, .17f, .001f)); projection.m23 = .5f;
            var oldProjection = projection; oldProjection.m03 = .04f; oldProjection.m13 = -.05f;
            properties.SetMatrix("GlobalShadowWorldToScreen", projection); properties.SetMatrix("GlobalShadowWorldToScreen2", oldProjection);
            properties.SetVector("GlobalShadowTexelSize", new Vector4(1f / 64, 1f / 64, 64, 64));
            properties.SetVector("GlobalShadowSettings", new Vector4(0, .001f, .0003f, .0003f));
            properties.SetVector("GlobalShadowL0", new Vector4(.1f, -.9f, .1f, 0));
            properties.SetVector("GlobalShadowL0Old", new Vector4(.09f, -.905f, .11f, 0));
            properties.SetVector("GlobalShadowL1", new Vector4(.101f, -.899f, .101f, 0));
            properties.SetFloat("ParallaxSearchMul", 1.5f);
            native.EnableKeyword("ENABLE_DISTANT_SHADOW_PCF"); native.EnableKeyword("PRECOMPUTE_MASK");
            double worst = 0;
            int nonuniform = 0;
            foreach (var size in new[] { new Vector2Int(65, 37), new Vector2Int(32, 19) })
            {
                SetSize(size.x, size.y);
                for (int flags = 0; flags < (1 << keywords.Length); flags++)
                {
                    camera.fieldOfView = (flags & 16) != 0 ? 18f : 60f;
                    for (int k = 0; k < keywords.Length; k++)
                    {
                        if ((flags & (1 << k)) != 0) { native.EnableKeyword(keywords[k]); replacement.EnableKeyword(keywords[k]); }
                        else { native.DisableKeyword(keywords[k]); replacement.DisableKeyword(keywords[k]); }
                    }
                    foreach (float blend in new[] { 0f, .37f, 1f })
                    {
                        properties.SetVector("GlobalShadowProj", new Vector4(0, 0, blend, 0));
                        var reference = Draw(native, 25); var same = Draw(replacement, 25);
                        double error = Error(reference, same); worst = Math.Max(worst, error);
                        Check(error < .0003, "Native 25-tap parity flags=" + flags + " size=" + size + " blend=" + blend + " error=" + error);
                        if (reference.Max(c => c.r) - reference.Min(c => c.r) > .2f) nonuniform++;
                        foreach (int count in new[] { 1, 8, 12, 24 })
                        {
                            var reduced = Draw(replacement, count);
                            Check(reduced.All(c => !float.IsNaN(c.r) && !float.IsInfinity(c.r) && c.r >= 0 && c.r <= 1.00001f), "Reduced output bounded " + count);
                        }
                    }
                }
            }
            Check(nonuniform > 20, "Parity exercised nonuniform shadows");
            camera.fieldOfView = 60f;
            foreach (string key in keywords) { native.DisableKeyword(key); replacement.DisableKeyword(key); }
            native.EnableKeyword("DISTANT_SHADOWS_BLEND"); replacement.EnableKeyword("DISTANT_SHADOWS_BLEND");
            native.EnableKeyword("USE_GAUSS_DISTRIBUTION"); replacement.EnableKeyword("USE_GAUSS_DISTRIBUTION");
            CheckRecordedBudgets();
            for (int n = 1; n <= 25; n++)
            {
                var taps = DistantShadowSamples.Taps(n);
                Check(taps.Take(n).Distinct().Count() == n, "Distinct sample positions " + n);
                Check(taps.Take(n).All(v => v.x >= -3 && v.x <= 2 && v.y >= -3 && v.y <= 2), "Native footprint bounds " + n);
            }
            File.WriteAllText("validation.txt", "PASSED " + checks + " D3D11 checks; native parity max error=" + worst + "; nonuniform fixtures=" + nonuniform + "; GPU=" + SystemInfo.graphicsDeviceName);
            // Small synchronized batch experiment, not live GPU timestamp/FPS evidence.
            SetSize(1280, 720);
            foreach (string key in keywords) { native.DisableKeyword(key); replacement.DisableKeyword(key); }
            native.EnableKeyword("DISTANT_SHADOWS_BLEND"); replacement.EnableKeyword("DISTANT_SHADOWS_BLEND");
            native.EnableKeyword("ENABLE_PARALLAX"); replacement.EnableKeyword("ENABLE_PARALLAX");
            native.EnableKeyword("USE_GAUSS_DISTRIBUTION"); replacement.EnableKeyword("USE_GAUSS_DISTRIBUTION");
            var timings = new System.Text.StringBuilder("Synchronized 16-draw batches at 1280x720, including CPU submission and one readback; not isolated GPU milliseconds.\n");
            Draw(native, 25); Draw(replacement, 25); Draw(replacement, 8);
            foreach (int count in new[] { 25, 8 })
            {
                var values = new List<double>();
                for (int i = 0; i < 5; i++) { var timer = Stopwatch.StartNew(); Draw(replacement, count, 16); timer.Stop(); values.Add(timer.Elapsed.TotalMilliseconds); }
                timings.AppendLine("replacement count=" + count + " median batch ms=" + values.OrderBy(v => v).ElementAt(2));
            }
            File.WriteAllText("experiment.txt", timings.ToString());
            DistantShadowHookFixture.Run(camera, quad, native, replacement.shader);
            UnityEngine.Debug.Log(File.ReadAllText("validation.txt"));
            var target = camera.targetTexture; camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(camera.gameObject); UnityEngine.Object.DestroyImmediate(quad);
            UnityEngine.Object.DestroyImmediate(native); UnityEngine.Object.DestroyImmediate(replacement);
            foreach (var obj in owned) UnityEngine.Object.DestroyImmediate(obj);
            halton.Release();
            resultBundle.Unload(true); sourceBundle.Unload(true);
        }
        catch (Exception e) { UnityEngine.Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
