using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

// Independent probes of the installed native bytecode, not the prototype oracle.
public static class MaterialResponseAudit
{
    private static readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
    private static readonly List<string> results=new List<string>();
    private static Texture2D Solid(Color color)
    {
        var t=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        t.SetPixel(0,0,color);t.Apply();owned.Add(t);return t;
    }
    private static void Near(float actual,float expected,string label,float tolerance=.00002f)
    {
        if(float.IsNaN(actual) || Math.Abs(actual-expected)>tolerance)
            throw new Exception(label+": actual="+actual+", expected="+expected);
        results.Add(label+": "+actual.ToString("G9")+" (expected "+expected.ToString("G9")+")");
    }
    public static void Run()
    {
        AssetBundle bundle=null;var factory=new PropertySheetFactory();var cb=new CommandBuffer();
        try
        {
            bundle=AssetBundle.LoadFromFile(Path.Combine(File.ReadAllText("game-path.txt").Trim(),"EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"));
            var shader=bundle.LoadAllAssets<Shader>().Single(s=>s.name=="Hidden/PostProcessing/ScreenSpaceReflections");
            var sheet=factory.Get(shader);
            var sampling=factory.Get(AssetDatabase.LoadAssetAtPath<Shader>("Assets/MaterialResponseAudit.shader"));
            var target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();owned.Add(target);
            var source=Solid(new Color(.3f,.4f,.5f,.7f));
            var cube=new Cubemap(1,TextureFormat.RGBAFloat,false);owned.Add(cube);
            foreach(CubemapFace face in Enum.GetValues(typeof(CubemapFace)))if((int)face>=0 && (int)face<6)cube.SetPixel(face,0,0,new Color(.2f,.3f,.4f,1));
            cube.Apply();
            var ramp=new Texture2D(128,128,TextureFormat.RGBAFloat,true,true){filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp};owned.Add(ramp);
            for(int mip=0;mip<ramp.mipmapCount;mip++)ramp.SetPixels(Enumerable.Repeat(new Color(.02f+.01f*mip,.02f+.01f*mip,.02f+.01f*mip,0),Math.Max(1,128>>mip)*Math.Max(1,128>>mip)).ToArray(),mip);
            ramp.Apply(false);
            sheet.properties.SetMatrix("_InverseProjectionMatrix",Matrix4x4.identity);
            sheet.properties.SetMatrix("_InverseViewMatrix",Matrix4x4.identity);
            sheet.properties.SetVector("_Params",new Vector4(0,0,500,ramp.mipmapCount));
            sheet.properties.SetFloat("_CutOffThreshold",0);
            Func<int,float,float,float,Color> draw=(pass,smoothness,normalLength,probeFactor)=>
            {
                cb.Clear();
                cb.SetGlobalTexture("_Test",Texture2D.blackTexture);
                cb.SetGlobalTexture("_CameraDepthTexture",Solid(new Color(.5f,0,0,0)));
                cb.SetGlobalTexture("_CameraGBufferTexture0",Texture2D.whiteTexture);
                cb.SetGlobalTexture("_CameraGBufferTexture1",Solid(new Color(.04f,.04f,.04f,smoothness)));
                cb.SetGlobalTexture("_CameraGBufferTexture2",Solid(new Color(.5f,.5f,.5f-.5f*normalLength,1)));
                cb.SetGlobalTexture("_StencilShadow",Texture2D.blackTexture);
                cb.SetGlobalTexture("_MyGlobalReflectionProbe",cube);
                cb.SetGlobalTexture("_Resolve",ramp);
                cb.SetGlobalVector("_WorldSpaceCameraPos",Vector4.zero);
                cb.SetGlobalVector("_ZBufferParams",new Vector4(0,2,0,1));
                cb.SetGlobalFloat("_ReflectionBottomShade",1);
                cb.SetGlobalFloat("_SSRFactor",probeFactor);
                RuntimeUtilities.BlitFullscreenTriangle(cb,source,target,sheet,pass);
                Graphics.ExecuteCommandBuffer(cb);
                var read=AsyncGPUReadback.Request(target,0,TextureFormat.RGBAFloat);read.WaitForCompletion();
                if(read.hasError)throw new Exception("Readback failed");return read.GetData<Color>()[0];
            };
            // Zero hit validity returns receiver scene colour, even with a coloured probe.
            var miss=draw(1,.9f,1,1);
            Near(miss.r,.3f,"native miss returns source R");Near(miss.g,.4f,"native miss returns source G");Near(miss.b,.5f,"native miss returns source B");Near(miss.a,.7f,"native miss returns source alpha");
            foreach(float s in new[]{.1f,.25f,.5f,.65f,.75f,.8f,.9f,.95f,.98f,1f})
            {
                float q=s*s*s,r=1-q,lod=r+r*r*(ramp.mipmapCount-1);
                float attenuation=1-.28f*Math.Max(r*r,.002f)*r;
                var withoutProbe=draw(3,s,1,0);
                // Calibrate the sample separately: texture/driver filtering can quantize
                // mip transitions, so do not assume perfectly linear fractional LOD.
                sampling.properties.SetFloat("_AuditLod",lod);
                cb.Clear();cb.SetGlobalTexture("_Resolve",ramp);
                RuntimeUtilities.BlitFullscreenTriangle(cb,source,target,sampling,0);
                Graphics.ExecuteCommandBuffer(cb);
                var sampleRead=AsyncGPUReadback.Request(target,0,TextureFormat.RGBAFloat);sampleRead.WaitForCompletion();
                if(sampleRead.hasError)throw new Exception("Sample readback failed");
                float radiance=sampleRead.GetData<Color>()[0].r;
                Near(withoutProbe.r,.3f+.04f*attenuation*radiance,"native s^3 SSR material/mip response s="+s);
                var withProbe=draw(3,s,1,1);
                float envR=1-s,envAttenuation=1-.28f*Math.Max(envR*envR,.002f)*envR;
                Near(withoutProbe.r-withProbe.r,.2f*.04f*envAttenuation,"native 1-s environment subtraction s="+s);
                var shortNormal=draw(3,s,.5f,0);
                float material=.04f+(Math.Min(1,.04f+q)-.04f)*Mathf.Pow(.5f,5);
                Near((shortNormal.r-.3f)/(withoutProbe.r-.3f),material/.04f,"native SSR uses unnormalized normal for Fresnel s="+s,.0001f);
                results.Add("s="+s+" GGX alpha="+((1-s)*(1-s)).ToString("G9")+" native SSR roughness="+r.ToString("G9")+" native LOD (8 mips)="+lod.ToString("G9"));
            }
            File.WriteAllLines("material-response-audit.txt",results);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);throw;}
        finally{cb.Dispose();factory.Release();foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);if(bundle!=null)bundle.Unload(true);}
    }
}
