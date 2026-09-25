using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

public static partial class AmbientValidation
{
 static int checks;
 static int drawPass;
 static float sourceBlend=1, destinationBlend=0;
 static Color initialLighting=Color.clear;
 static Camera camera;
 static Mesh quad;
 static Material native, fixedAmbient;
 static MaterialPropertyBlock properties;
 static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
 static Texture2D albedo;
 static RenderTexture diagnosticTarget;
 static void Check(bool ok, string description) { if(!ok) throw new Exception(description); checks++; Debug.Log("PASS "+description); }
 static Texture2D Texture(Color color) {
  var t=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true); t.SetPixel(0,0,color);t.Apply();owned.Add(t);return t;
 }
 static void SetAlbedo(float ao) { albedo.SetPixel(0,0,new Color(.4f,.55f,.7f,ao));albedo.Apply(); }
 static void Inputs(float visibility, float reflections, float normalX, bool ambient, float sunY) {
  properties.SetFloat("_FTFX_CA_MaterialCaptured",1);
  properties.SetTexture("_FTFX_CA_OriginalOcclusion",Texture2D.whiteTexture);
  properties.SetFloat("_FTFX_CA_SeparateAOAvailable",0);
  properties.SetFloat("_FTFX_CA_FoliageStrength",.65f);
  properties.SetTexture("_CameraDepthTexture", Texture(new Color(.5f,0,0,0)));
  properties.SetTexture("_StencilShadow", Texture(new Color(0,0,0,1-visibility)));
  properties.SetTexture("_CameraGBufferTexture0",albedo);
  properties.SetTexture("_CameraGBufferTexture1",Texture(new Color(.2f,.12f,.08f,.6f)));
  properties.SetTexture("_CameraGBufferTexture2",Texture(new Color(normalX,.9f,.4f,1)));
  properties.SetTexture("_CameraMotionVectorsDepth",Texture(new Color(0,0,0,0)));
  var cube=new Cubemap(4,TextureFormat.RGBAFloat,false);
  foreach(CubemapFace face in new[]{CubemapFace.PositiveX,CubemapFace.NegativeX,CubemapFace.PositiveY,CubemapFace.NegativeY,CubemapFace.PositiveZ,CubemapFace.NegativeZ})
   cube.SetPixels(Enumerable.Repeat(new Color(.3f,.45f,.6f,1),16).ToArray(),face);
  cube.Apply();owned.Add(cube);properties.SetTexture("_MyGlobalReflectionProbe",cube);
  foreach(string name in new[]{"_SHAr","_SHAg","_SHAb"}) properties.SetVector(name,ambient?new Vector4(.03f,.06f,-.02f,.15f):Vector4.zero);
  foreach(string name in new[]{"_SHBr","_SHBg","_SHBb"}) properties.SetVector(name,ambient?new Vector4(.01f,-.02f,.015f,.01f):Vector4.zero);
  properties.SetVector("_SHC",ambient?new Vector4(.02f,.01f,.03f,0):Vector4.zero);
  properties.SetFloat("_ReflectionIntensity",reflections);
  properties.SetFloat("_ReflectionBottomShade",1.4f);
  properties.SetFloat("_AmbientBlur",1.3f);
  properties.SetFloat("TOD_Fogginess",.4f);
  properties.SetFloat("_NightVisionOn",.7f);
  properties.SetVector("_SunDirection",new Vector4(.2f,sunY,.3f,0));
 }
 static Color[] Draw(Material mat,bool hdr,float strength) {
  camera.allowHDR = hdr;
  if(hdr) mat.EnableKeyword("UNITY_HDR_ON");else mat.DisableKeyword("UNITY_HDR_ON");
  properties.SetFloat("_FTFX_CAAmount",strength);
  using(var cb=new CommandBuffer()) {
   if(hdr) cb.EnableShaderKeyword("UNITY_HDR_ON"); else cb.DisableShaderKeyword("UNITY_HDR_ON");
   cb.SetGlobalFloat("_SrcBlend",sourceBlend);cb.SetGlobalFloat("_DstBlend",destinationBlend);
   cb.SetRenderTarget(camera.targetTexture);
   cb.ClearRenderTarget(false,true,initialLighting);
   if(diagnosticTarget != null) { cb.SetGlobalFloat("_FTFX_CA_DebugAvailable",1); cb.SetRandomWriteTarget(1,diagnosticTarget); }
   cb.DrawMesh(quad,Matrix4x4.identity,mat,0,drawPass,properties);
   if(diagnosticTarget != null) { cb.ClearRandomWriteTargets(); cb.SetGlobalFloat("_FTFX_CA_DebugAvailable",0); }
   camera.AddCommandBuffer(CameraEvent.AfterEverything,cb);
   camera.Render();
   camera.RemoveCommandBuffer(CameraEvent.AfterEverything,cb);
  }
  var prev=RenderTexture.active;RenderTexture.active=camera.targetTexture;
  var read=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);
  read.ReadPixels(new Rect(16,16,32,32),0,0);read.Apply();
  Color[] pixels=read.GetPixels();UnityEngine.Object.DestroyImmediate(read);RenderTexture.active=prev;return pixels;
 }
 static double Error(Color[] a,Color[] b) => a.Select((c,i)=>Math.Max(Math.Max(Math.Abs(c.r-b[i].r),Math.Abs(c.g-b[i].g)),Math.Abs(c.b-b[i].b))).Max();
 static Color[] ScaleAmbient(Color[] baseline,bool hdr,float visibility) => baseline.Select(c=>hdr?
  new Color(c.r*visibility,c.g*visibility,c.b*visibility,c.a):
  new Color(Mathf.Pow(c.r,visibility),Mathf.Pow(c.g,visibility),Mathf.Pow(c.b,visibility),c.a)).ToArray();
 static void CheckFoliage() {
  foreach(bool hdr in new[]{false,true}) {
   SetAlbedo(0);Inputs(1,0,.7f,true,.4f);
   var baseline=Draw(native,hdr,0);
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",Texture2D.blackTexture);
   Check(Error(baseline,Draw(fixedAmbient,hdr,1))<.00015,"Authored zero alpha retains native ambient without separate AO HDR="+hdr);
   properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetVector("_FTFX_CA_AODecode",new Vector4(1,1,1,0));
   properties.SetVector("_FTFX_CA_AOFade",new Vector4(0,0,0,1));
   foreach(float ao in new[]{0f,.25f,.7f,1f}) foreach(float foliage in new[]{0f,.65f,1f}) {
    properties.SetFloat("_FTFX_CA_FoliageStrength",foliage);
    properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(ao,2,0,0)));
    float visibility=Mathf.Lerp(1,ao,foliage);
    Check(Error(ScaleAmbient(baseline,hdr,visibility),Draw(fixedAmbient,hdr,1))<.00015,"Foliage preserves variable screen AO HDR="+hdr+" AO="+ao+" strength="+foliage);
   }
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",Texture2D.whiteTexture);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture2D.whiteTexture);
   Check(Error(ScaleAmbient(baseline,hdr,0),Draw(fixedAmbient,hdr,1))<.00015,"Ordinary surface driven to zero by AO stays fully occluded HDR="+hdr);
   properties.SetFloat("_FTFX_CA_MaterialCaptured",0);
   Check(Error(baseline,Draw(fixedAmbient,hdr,1))<.00015,"Missing camera capture fails to native ambient HDR="+hdr);
  }
  // Exercise the actual capture pass: it must copy ALPHA, not red, and preserve a zero marker.
  var source=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true);
  source.SetPixels(new[]{new Color(.8f,.3f,.2f,0),new Color(.1f,.2f,.3f,.75f)});source.Apply();owned.Add(source);
  var capture=new RenderTexture(2,1,0,RenderTextureFormat.R8,RenderTextureReadWrite.Linear);capture.Create();
  Graphics.Blit(source,capture,fixedAmbient,1);
  var previous=RenderTexture.active;RenderTexture.active=capture;
  var read=new Texture2D(2,1,TextureFormat.RGBAFloat,false,true);read.ReadPixels(new Rect(0,0,2,1),0,0);read.Apply();
  Check(read.GetPixel(0,0).r==0 && Math.Abs(read.GetPixel(1,0).r-.75)<.005,"Material capture stores native alpha accurately in R8: "+read.GetPixel(0,0)+" / "+read.GetPixel(1,0));
  RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(read);capture.Release();UnityEngine.Object.DestroyImmediate(capture);
 }



 static void CheckLightingBlend() {
  SetAlbedo(.25f);Inputs(.6f,.4f,.7f,true,.4f);
  foreach(bool hdr in new[]{false,true}) {
   sourceBlend=1;destinationBlend=0;initialLighting=Color.clear;
   var nativeOnly=Draw(native,hdr,0);var fixedOnly=Draw(fixedAmbient,hdr,1);
   initialLighting=new Color(.2f,.3f,.4f,.6f);
   sourceBlend=hdr?1:2;destinationBlend=hdr?1:0;
   var result=Draw(fixedAmbient,hdr,1);
   var expected=fixedOnly.Select(c=>hdr?new Color(c.r+.2f,c.g+.3f,c.b+.4f,.6f):new Color(c.r*.2f,c.g*.3f,c.b*.4f,.6f)).ToArray();
   Check(Error(result,expected)<.00015,"EFT HDR/LDR blend preserves the existing lighting and changes only added ambient HDR="+hdr);
   Check(result.All(c=>Math.Abs(c.a-.6f)<.00015),"EFT ambient blend preserves existing alpha HDR="+hdr);
   var baseline=Draw(native,hdr,0);
   Check(hdr?result.Select((c,i)=>c.r<baseline[i].r).All(v=>v):result.Select((c,i)=>c.r>baseline[i].r).All(v=>v),"Corrected ambient remains visibly occluded after real EFT blend HDR="+hdr);
  }
  sourceBlend=1;destinationBlend=0;initialLighting=Color.clear;
 }
 public static void Run() {
  try {
   var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/CustomAmbient.shader");
   Check(shader != null && !ShaderUtil.ShaderHasError(shader),"Replacement shader compiles");
   var manifest=BuildPipeline.BuildAssetBundles("AssetBundles",new[]{new AssetBundleBuild{assetBundleName="customambient",assetNames=new[]{"Assets/CustomAmbient.shader"}}},BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.ChunkBasedCompression,BuildTarget.StandaloneWindows64);
   Check(manifest!=null,"Standalone shader bundle builds");
   var bundle=AssetBundle.LoadFromFile(Path.Combine(File.ReadAllText("game-path.txt").Trim(),"EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"));
   var nativeShader=bundle.LoadAllAssets<Shader>().Single(s=>s.name=="Hidden/WriteScreenAmbient");
   var resultBundle=AssetBundle.LoadFromFile("AssetBundles/customambient");
   native=new Material(nativeShader);fixedAmbient=new Material(resultBundle.LoadAsset<Shader>("Assets/CustomAmbient.shader"));
   Check(native.shader.isSupported && fixedAmbient.shader.isSupported,"Installed and replacement shaders supported on D3D11");
   camera=new GameObject("Ambient validation camera").AddComponent<Camera>();
   camera.cullingMask=0;camera.renderingPath=RenderingPath.Forward;camera.clearFlags=CameraClearFlags.SolidColor;
   camera.nearClipPlane=.01f;camera.farClipPlane=50;camera.allowMSAA=false;camera.allowHDR=true;
   camera.targetTexture=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);camera.targetTexture.Create();
   quad=new Mesh{vertices=new[]{new Vector3(-1,-1,.1f),new Vector3(1,-1,.1f),new Vector3(-1,1,.1f),new Vector3(1,1,.1f)},triangles=new[]{0,1,3,3,2,0}};
   properties=new MaterialPropertyBlock();albedo=Texture(new Color(.4f,.55f,.7f,1));
   double worst=0;
   foreach(bool hdr in new[]{false,true}) foreach(float ao in new[]{0f,.25f,.7f,1f}) foreach(float visibility in new[]{.1f,.6f,1f}) foreach(float reflections in new[]{0f,.4f,1f}) {
    SetAlbedo(ao);Inputs(visibility,reflections,.7f,true,.4f);
    var original=Draw(native,hdr,0);var passthrough=Draw(fixedAmbient,hdr,0);
    double error=Error(original,passthrough);worst=Math.Max(worst,error);
    Check(error<.00015,"Native parity HDR="+hdr+" AO="+ao+" stencil="+visibility+" reflections="+reflections+" error="+error);
    var corrected=Draw(fixedAmbient,hdr,1);
    if(ao==1) Check(Error(original,corrected)<.00015,"Unoccluded ambient remains native");
    else Check(hdr?corrected.Select((c,i)=>c.r<=original[i].r+.00015).All(v=>v):corrected.Select((c,i)=>c.r>=original[i].r-.00015).All(v=>v),"Occlusion attenuates ambient in correct HDR/LDR encoding");
   }
   foreach(bool hdr in new[]{false,true}) foreach(float ao in new[]{0f,.25f,1f}) {
    SetAlbedo(ao);Inputs(.6f,1,.7f,false,1); // No SH/fill: reflection isolation.
    Check(Error(Draw(native,hdr,0),Draw(fixedAmbient,hdr,1))<.00015,"Existing reflection AO is preserved HDR="+hdr+" AO="+ao);
    Inputs(.6f,0,.7f,false,.4f);
    Check(Error(ScaleAmbient(Draw(native,hdr,0),hdr,ao),Draw(fixedAmbient,hdr,1))<.00015,"Fill receives the same AO multiplier HDR="+hdr+" AO="+ao);
    Inputs(.6f,0,.7f,true,1); // No reflections or fill: diffuse equation.
    var original=Draw(native,hdr,0);
    var corrected=Draw(fixedAmbient,hdr,1);
    var expected=original.Select(c=>hdr?new Color(c.r*ao,c.g*ao,c.b*ao,c.a):new Color(Mathf.Pow(c.r,ao),Mathf.Pow(c.g,ao),Mathf.Pow(c.b,ao),c.a)).ToArray();
    Check(Error(expected,corrected)<.00015,"Only diffuse is multiplied by shared AO HDR="+hdr+" AO="+ao);
   }
   CheckFillAO();
   CheckFoliage();
   CheckLightingBlend();
   CheckBentNormals();
   File.WriteAllText("validation.txt","PASSED "+checks+" D3D11 shader checks. Worst strength-zero native parity error: "+worst+"\nGPU: "+SystemInfo.graphicsDeviceName);
   Debug.Log(File.ReadAllText("validation.txt"));
   UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(quad);
   UnityEngine.Object.DestroyImmediate(native);UnityEngine.Object.DestroyImmediate(fixedAmbient);
   foreach(var obj in owned)UnityEngine.Object.DestroyImmediate(obj);
   resultBundle.Unload(true);bundle.Unload(true);
  } catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
 }
}


