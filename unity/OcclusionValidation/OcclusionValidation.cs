using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx.Configuration;
using FuckTheseFX.Source.Occlusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static class OcclusionValidation
{
 private static readonly List<string> results=new List<string>();
 public static void Run()
 {
  try
  {
   typeof(BepInEx.Paths).GetProperty("BepInExConfigPath").GetSetMethod(true).Invoke(null,new object[]{Path.GetFullPath("bepinex.cfg")});
   PlayerSettings.colorSpace=ColorSpace.Linear;
   AssetDatabase.Refresh();
   var shaders=new[]{"Assets/XeGTAO/Composite.shader","Assets/CustomAmbient.shader"};
   foreach(var path in shaders)
   {
    var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
    Require(shader!=null && !ShaderUtil.ShaderHasError(shader),"shader compilation: "+path);
   }
   var build=new AssetBundleBuild { assetBundleName="occlusion",assetNames=shaders.Concat(new[]{"Assets/XeGTAO/XeGTAO.compute"}).ToArray() };
   Require(BuildPipeline.BuildAssetBundles("Output/Assets",new[]{build},BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64)!=null,"bundle build");
   NativeRouting();
   using(var assets=new GpuAssets(Path.GetFullPath("Output/Plugin.dll")))
   {
    Composition(assets);
    ActorEquivalence(assets,256,144);
    ActorEquivalence(assets,259,147);
    CameraLifecycle(assets);
   }
   File.WriteAllLines("validation.txt",results);
   Debug.Log(string.Join("\n",results));
   EditorApplication.Exit(0);
  }
  catch(Exception error) { Debug.LogException(error); File.WriteAllText("failure.txt",error.ToString()); EditorApplication.Exit(1); }
 }
 private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
 private static Material Material(GpuAssets assets) => new Material(assets.Composite);
 private static void Clear(RenderTexture rt,Color color) { var previous=RenderTexture.active; RenderTexture.active=rt; GL.Clear(false,true,color); RenderTexture.active=previous; }
 private static Color Read(RenderTexture rt)
 {
  var previous=RenderTexture.active; RenderTexture.active=rt;
  var texture=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true); texture.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); texture.Apply();
  var color=texture.GetPixel(rt.width/2,rt.height/2); UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active=previous; return color;
 }
 private static void Composition(GpuAssets assets)
 {
  var a=GpuAssets.Target("albedo",32,32,GraphicsFormat.R16G16B16A16_SFloat);
  var e=GpuAssets.Target("emission",32,32,GraphicsFormat.R16G16B16A16_SFloat);
  var ao=GpuAssets.Target("ao",32,32,GraphicsFormat.R16G16_SFloat);
  var material=Material(assets); material.SetVector("_Size",new Vector4(32,32,100f/65504,1));
  Clear(ao,new Color(.8f,655,0,0));
  material.SetTexture("_PrimaryAO",ao);
  float visibility=Mathf.Clamp01(1+(Mathf.Pow(.8f,.8f)-1)*2);
  for(int pass=1;pass<=2;pass++)
  {
   Clear(a,new Color(.2f,.3f,.4f,.6f)); Clear(e,new Color(.5f,.4f,.3f,.7f));
   var source=GpuAssets.Target("emission copy",32,32,GraphicsFormat.R16G16B16A16_SFloat); Graphics.Blit(e,source); material.SetTexture("_Emission",source);
   var cb=new CommandBuffer(); cb.SetRenderTarget(new RenderTargetIdentifier[]{a,e},a); cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,pass);
   Graphics.ExecuteCommandBuffer(cb); cb.Dispose();
   var ca=Read(a); var ce=Read(e);
   Require(Mathf.Abs(ca.r-.2f)<.001f && Mathf.Abs(ca.g-.3f)<.001f && Mathf.Abs(ca.b-.4f)<.001f,"albedo RGB changed");
   Require(Mathf.Abs(ca.a-.6f*visibility)<.002f,"material occlusion blend");
   Require(Mathf.Abs(ce.a-.7f)<.001f,"emission alpha changed");
   Require(Mathf.Abs(ce.r-(pass==1?.5f*visibility:Mathf.Pow(.5f,visibility)))<.002f,"HDR/LDR emission composite");
   GpuAssets.Release(ref source);
  }
  GpuAssets.Release(ref a);GpuAssets.Release(ref e);GpuAssets.Release(ref ao);UnityEngine.Object.DestroyImmediate(material);
  results.Add("PASS: primary HDR/LDR composition, albedo RGB and emission alpha preservation.");
 }
 private static Camera Scene(int width,int height,out RenderTexture target,out GameObject[] geometry)
 {
  var camera=new GameObject("Fixture camera").AddComponent<Camera>(); camera.enabled=false;
  camera.renderingPath=RenderingPath.DeferredShading;camera.allowHDR=true;camera.nearClipPlane=.1f;camera.farClipPlane=100;
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;camera.fieldOfView=60;
  target=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
  geometry=new GameObject[3];
  for(int i=0;i<3;i++)
  {
   var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);geometry[i]=cube;
   cube.transform.position=i==0?new Vector3(0,0,4):i==1?new Vector3(.3f,-.5f,3):new Vector3(-.7f,.2f,2.5f);
   cube.transform.localScale=i==0?new Vector3(8,8,.3f):new Vector3(.8f,1,.8f);
   cube.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard"));
  }
  return camera;
 }
 private static uint[] ReadUInt(RenderTexture texture)
 {
  var request=AsyncGPUReadback.Request(texture,0);request.WaitForCompletion();Require(!request.hasError,"GPU readback failed");return request.GetData<uint>().ToArray();
 }
 private static void ActorEquivalence(GpuAssets assets,int width,int height)
 {
  var camera=Scene(width,height,out var target,out var geometry);
  var mask=GpuAssets.Target("actor mask",width,height,GraphicsFormat.R8_UNorm);
  var upload=new Texture2D(width,height,TextureFormat.RGBA32,false,true);
  var pixels=new Color[width*height];
  var packed=GpuAssets.Target("Reference packed actor",width,height,GraphicsFormat.R16G16_SFloat);
  var expectedImage=GpuAssets.Target("Reference actor composite",width,height,GraphicsFormat.R32G32B32A32_SFloat);
  var actualImage=GpuAssets.Target("Optimized actor composite",width,height,GraphicsFormat.R32G32B32A32_SFloat);
  var composite=Material(assets);
  var referenceMaterial=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ReferenceActor.shader"));
  var size=new Vector4(width,height,100f/65504,1);
  composite.SetVector("_Size",size);
  var p=new Parameters {Radius=.3f,Multiplier=1,Falloff=.615f,Distribution=3,Sharpness=.4f,Power=1,Denoise=1,Normals=true};
  using(var baseline=new OcclusionPipeline(assets.Compute,true))
  using(var optimized=new OcclusionPipeline(assets.Compute,true))
  {
   baseline.Prepare(width,height,1);optimized.Prepare(width,height,1);
   int shaded=0,compared=0;
   for(int test=0;test<5;test++)
   {
    for(int y=0;y<height;y++) for(int x=0;x<width;x++)
    {
     bool active=test==0 || test==4 || test==1 && x>width/3 && x<width/2 && y>height/3 && y<height*2/3 ||
      test==2 && (x<2 || y<2 || x>=width-2 || y>=height-2 || x%8==7 && y%8==0);
     pixels[y*width+x]=active?Color.white:Color.black;
    }
    upload.SetPixels(pixels);upload.Apply();Graphics.Blit(upload,mask);
    var cb=new CommandBuffer();
    float cutoff=test==4?.1f:10;
    baseline.Record(cb,camera,3,p,mask,cutoff,false);optimized.Record(cb,camera,3,p,mask,cutoff,true);
    var properties=new MaterialPropertyBlock();properties.SetTexture("_XeAO",baseline.Result);properties.SetTexture("_XeDepth",baseline.Depth);
    cb.SetRenderTarget(packed);cb.DrawMesh(assets.Quad,Matrix4x4.identity,composite,0,0,properties);
    referenceMaterial.SetTexture("_SecondMask",mask);referenceMaterial.SetTexture("_SecondAO",packed);
    referenceMaterial.SetVector("_SecondSettings",new Vector4(1.5f,.6f,cutoff,size.z));
    cb.SetRenderTarget(expectedImage);cb.DrawMesh(assets.Quad,Matrix4x4.identity,referenceMaterial,0,2);
    properties.SetTexture("_XeAO",optimized.Result);properties.SetTexture("_XeDepth",optimized.Depth);properties.SetTexture("_ActorMask",mask);
    composite.SetVector("_ActorSettings",new Vector4(1.5f,.6f,cutoff,0));
    cb.SetRenderTarget(actualImage);cb.DrawMesh(assets.Quad,Matrix4x4.identity,composite,0,6,properties);
    cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
    camera.Render();camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);cb.Dispose();
    var reference=ReadUInt(baseline.Result);var actual=ReadUInt(optimized.Result);
    for(int i=0;i<pixels.Length;i++)
    {
     if(reference[i]<250) shaded++;
     if(pixels[i].r<.5f || test==4) continue;
     compared++;Require(reference[i]==actual[i],"actor pixel differs test="+test+" index="+i+" expected="+reference[i]+" actual="+actual[i]);
    }
    if(test>=3) Require(actual.All(v=>v==255),"stale actor shading after mask cleared");
    var expectedPixels=AsyncGPUReadback.Request(expectedImage,0);var actualPixels=AsyncGPUReadback.Request(actualImage,0);
    expectedPixels.WaitForCompletion();actualPixels.WaitForCompletion();Require(!expectedPixels.hasError && !actualPixels.hasError,"composite readback failed");
    var expectedColors=expectedPixels.GetData<Color>();var actualColors=actualPixels.GetData<Color>();
    for(int i=0;i<expectedColors.Length;i++) Require(Mathf.Abs(expectedColors[i].r-actualColors[i].r)<.000001f,"fused actor composite differs at "+i+" in case "+test);
   }
   Require(shaded>100 && compared>width*height,"fixture did not exercise occlusion");
   results.Add("PASS: full-frame vs tiled actor AO is bit-identical on "+compared+" actor pixels at "+width+"x"+height+"; moving, empty and edge masks; "+shaded+" non-neutral samples.");
  }
  GpuAssets.Release(ref mask);GpuAssets.Release(ref packed);GpuAssets.Release(ref expectedImage);GpuAssets.Release(ref actualImage);
  UnityEngine.Object.DestroyImmediate(composite);UnityEngine.Object.DestroyImmediate(referenceMaterial);
  UnityEngine.Object.DestroyImmediate(upload);DestroyScene(camera,target,geometry);
  results.Add("PASS: fused actor composite matches the former RGHalf resolve/composite, including empty masks and distance rejection at "+width+"x"+height+".");
 }
 private static void CameraLifecycle(GpuAssets assets)
 {
  var config=new ConfigFile(Path.GetFullPath("fixture.cfg"),false){SaveOnConfigSet=false};
  var settings=new OcclusionSettings(config);
  var camera=Scene(128,96,out var target,out var geometry);
  var writer=new CommandBuffer();
  var stencil=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/StencilFixture.shader"));
  camera.AddCommandBuffer(CameraEvent.AfterGBuffer,writer);
  using(var effect=new CameraOcclusion(camera,assets))
  {
   foreach(int actorClass in new[]{0,1,2,3})
   {
    writer.Clear();writer.SetRenderTarget(BuiltinRenderTextureType.GBuffer0);writer.ClearRenderTarget(false,true,new Color(.1f,.2f,.3f,.75f));
    writer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);stencil.SetInt("_StencilRef",actorClass);
    writer.DrawMesh(assets.Quad,Matrix4x4.identity,stencil,0,0);
    effect.Record(settings,2,true);camera.Render();
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    var original=(RenderTexture)typeof(CameraOcclusion).GetField("originalAlpha",flags).GetValue(effect);
    var actors=(RenderTexture)typeof(CameraOcclusion).GetField("actorMask",flags).GetValue(effect);
    var capturedAlpha=Read(original).r;
    Require(Mathf.Abs(capturedAlpha-.75f)<.005f,"original material alpha capture failed: "+capturedAlpha);
    Require(Mathf.Abs(Read(actors).r-(actorClass==0?0:1))<.001f,"actor stencil classification failed: "+actorClass);
    Require(Shader.GetGlobalFloat("_FTFX_CA_MaterialCaptured")==0 && Shader.GetGlobalFloat("_FTFX_CA_SeparateAOAvailable")==0,"per-camera ambient globals leaked");
   }
   writer.Clear();
   foreach(var view in new[]{OcclusionView.Normal,OcclusionView.Depth,OcclusionView.BeforeBlur,OcclusionView.AfterBlur,OcclusionView.CombinedAO,OcclusionView.SecondPassAO})
   { settings.View.Value=view;effect.Record(settings,2,true);camera.Render();Require(effect.Active,"inactive camera"); }
   settings.Actors.Value=false;settings.Ambient.Value=false;effect.Record(settings,2,true);camera.Render();
   Require(!effect.AmbientActive,"ambient not disabled");
   var replacement=new RenderTexture(131,99,24,RenderTextureFormat.ARGBHalf);replacement.Create();camera.targetTexture=replacement;
   effect.Record(settings,1,true);camera.Render();
   Require(effect.DebugTexture.width==131 && effect.DebugTexture.height==99,"resize did not update diagnostics");
   camera.targetTexture=target;replacement.Release();UnityEngine.Object.DestroyImmediate(replacement);
  }
  camera.RemoveCommandBuffer(CameraEvent.AfterGBuffer,writer);writer.Dispose();UnityEngine.Object.DestroyImmediate(stencil);
  Require(camera.GetCommandBuffers(CameraEvent.BeforeGBuffer).Length==0 && camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==0,"camera command buffers leaked");
  DestroyScene(camera,target,geometry);
  results.Add("PASS: original material capture, stencil classes 0/1/2/3, camera-global reset, all six debug views, actor/ambient toggles, resize and command-buffer cleanup.");
 }
 private static void DestroyScene(Camera camera,RenderTexture target,GameObject[] geometry)
 {
  camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(camera.gameObject);target.Release();UnityEngine.Object.DestroyImmediate(target);
  foreach(var go in geometry){UnityEngine.Object.DestroyImmediate(go.GetComponent<Renderer>().sharedMaterial);UnityEngine.Object.DestroyImmediate(go);}
 }
 private static void NativeRouting()
 {
  var camera=Scene(128,96,out var target,out var geometry);
  var native1=new GameObject("Native primary").AddComponent<Light>();
  var native2=new GameObject("Native actors").AddComponent<Light>();
  var manager=new EFT.CameraControl.CameraManager {Camera=camera,_hbao=native1,_ambientOcclusion=native2};
  var config=new ConfigFile(Path.GetFullPath("routing.cfg"),false){SaveOnConfigSet=false};
  var settings=new OcclusionSettings(config);
  using(var integration=new XeGTAOIntegration(Path.GetFullPath("Output/Plugin.dll"),settings))
  {
   integration.Initialize();
   manager.SetSSAO(EFT.Settings.Graphics.ESSAOMode.HighQuality);camera.Render();
   Require(!native1.enabled && !native2.enabled && camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==1,"replacement does not own AO");
   settings.Enabled.Value=false;integration.Update();
   Require(native1.enabled && native2.enabled && camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==0,"toggle did not restore native AO");
   settings.Enabled.Value=true;integration.Update();camera.Render();
   Require(!native1.enabled && !native2.enabled,"toggle did not re-enable XeGTAO");
   manager.SetSSAO(EFT.Settings.Graphics.ESSAOMode.Off);camera.Render();
   Require(!native1.enabled && !native2.enabled && camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==0,"game AO Off not respected");
   manager.SetSSAO(EFT.Settings.Graphics.ESSAOMode.HighQuality);camera.orthographic=true;camera.Render();
   Require(native1.enabled && native2.enabled,"unsupported camera did not retain native AO");
   camera.orthographic=false;camera.Render();
   Require(!native1.enabled && !native2.enabled,"supported camera did not recover");
  }
  Require(native1.enabled && native2.enabled && camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque).Length==0,"dispose did not restore native");
  UnityEngine.Object.DestroyImmediate(native1.gameObject);UnityEngine.Object.DestroyImmediate(native2.gameObject);
  DestroyScene(camera,target,geometry);
  results.Add("PASS: actual Harmony routing, XeGTAO on/off, graphics AO Off, unsupported-camera fallback/recovery and native restoration on dispose.");
 }
}
