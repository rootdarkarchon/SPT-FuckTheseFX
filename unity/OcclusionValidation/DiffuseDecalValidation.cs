using System.IO;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public static partial class OcclusionValidation
{
 private static void DiffuseAfterDecals(GpuAssets assets)
 {
  var camera=Scene(64,64,out var target,out var geometry);
  foreach(var item in geometry) item.GetComponent<Renderer>().sharedMaterial.SetFloat("_Glossiness",1);
  var settings=new OcclusionSettings(new ConfigFile(Path.GetFullPath("diffuse-decals.cfg"),false){SaveOnConfigSet=false});
  settings.Actors.Value=false;settings.FoliageExtraAO.Value=0;settings.Bleed.Enabled.Value=false;
  var ambient=new Material(assets.Ambient);
  var stencil=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);stencil.SetPixel(0,0,Color.clear);stencil.Apply();
  ambient.SetFloat("_ReflectionIntensity",0);ambient.SetTexture("_StencilShadow",stencil);
  foreach(var name in new[]{"_SHAr","_SHAg","_SHAb"}) ambient.SetVector(name,new Vector4(0,0,0,.2f));
  ambient.SetVector("_SunDirection",new Vector4(0,1,0,0));
  var quad=new Mesh{vertices=new[]{new Vector3(-1,-1,.1f),new Vector3(1,-1,.1f),new Vector3(-1,1,.1f),new Vector3(1,1,.1f)},triangles=new[]{0,1,3,3,2,0}};
  var decal=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/LateDecalFixture.shader"));
  var mask=Material(assets);mask.SetVector("_Size",new Vector4(64,64,1,0));
  var output=GpuAssets.Target("ambient after decal",64,64,GraphicsFormat.R32G32B32A32_SFloat);
  var alpha=GpuAssets.Target("material after decal",64,64,GraphicsFormat.R8_UNorm);
  using(var effect=new CameraOcclusion(camera,assets))
  using(var late=new CommandBuffer { name="Fixture late deferred decal" })
  using(var draw=new CommandBuffer { name="Fixture ambient after decal" })
  {
   // Register after CameraOcclusion so this follows primary AO even in BeforeLighting fallback.
   camera.AddCommandBuffer(CameraEvent.BeforeLighting,late);
   camera.AddCommandBuffer(CameraEvent.AfterLighting,draw);
   int cases=0;
   foreach(bool hdr in new[]{true,false})
   {
    camera.allowHDR=hdr;
    if(hdr) ambient.EnableKeyword("UNITY_HDR_ON");else ambient.DisableKeyword("UNITY_HDR_ON");
    Color[] reference=null;
    foreach(bool reflections in new[]{true,false,true})
    foreach(float opacity in new[]{1f,.5f,0f})
    {
     settings.ReflectionOcclusion.Value=reflections;effect.Record(settings,2,true,true);
     late.Clear();decal.SetFloat("_Opacity",opacity);
     late.SetRenderTarget(BuiltinRenderTextureType.GBuffer0);late.DrawMesh(assets.Quad,Matrix4x4.identity,decal);
     late.SetGlobalTexture("_FTFXMaterialInput",BuiltinRenderTextureType.GBuffer0);
     late.SetRenderTarget(alpha);late.DrawMesh(assets.Quad,Matrix4x4.identity,mask,0,7);
     late.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
     draw.Clear();draw.SetGlobalFloat("_SrcBlend",1);draw.SetGlobalFloat("_DstBlend",0);
     if(hdr)draw.EnableShaderKeyword("UNITY_HDR_ON");else draw.DisableShaderKeyword("UNITY_HDR_ON");
     draw.SetGlobalTexture("_CameraGBufferTexture0",BuiltinRenderTextureType.GBuffer0);
     draw.SetRenderTarget(output);draw.ClearRenderTarget(false,true,Color.clear);
     draw.DrawMesh(quad,Matrix4x4.identity,ambient,0,0);
     draw.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
     ambient.SetFloat("_FTFX_CAAmount",.6f);camera.Render();var actual=BleedPixels(output);
     if(opacity==1) Require(Read(alpha).r==1,"late decal did not overwrite material AO");
     if(reference==null)reference=actual;
     float maxError=0;for(int i=0;i<actual.Length;i++)maxError=Mathf.Max(maxError,Mathf.Abs(actual[i].r-reference[i].r));
     Require(maxError<.0002f,"late decal/reflection toggle changed diffuse ambient HDR="+hdr+" reflection="+reflections+" opacity="+opacity+" error="+maxError);
     // Ensure equality isn't a vacuous result from absent AO or an ambient draw that never ran.
     ambient.SetFloat("_FTFX_CAAmount",0);camera.Render();var unoccluded=BleedPixels(output);
     float maximumAO=0;for(int i=0;i<actual.Length;i++)maximumAO=Mathf.Max(maximumAO,Mathf.Abs(actual[i].r-unoccluded[i].r));
     Require(maximumAO>.01f,"fixture did not apply meaningful diffuse AO HDR="+hdr+" reflection="+reflections+" opacity="+opacity+" delta="+maximumAO+" shaded="+actual[2080]+" unoccluded="+unoccluded[2080]+" alpha="+Read(alpha));
     cases++;
    }
   }
   camera.RemoveCommandBuffer(CameraEvent.BeforeLighting,late);camera.RemoveCommandBuffer(CameraEvent.AfterLighting,draw);
   results.Add("PASS: "+cases+" actual camera AO -> late alpha-blended decal -> CustomAmbient cases; partial/opaque overwrite, reflection off/on recovery, HDR/LDR, unchanged diffuse shading and nonzero AO.");
  }
  foreach(var rt in new[]{output,alpha}){rt.Release();Object.DestroyImmediate(rt);}
  foreach(var obj in new Object[]{ambient,quad,decal,mask,stencil})Object.DestroyImmediate(obj);
  DestroyScene(camera,target,geometry);
 }
}
