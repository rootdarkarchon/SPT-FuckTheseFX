using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static partial class AmbientValidation
{
 static void CheckBentNormals()
 {
  // Known packed direction allows an independent reference through the scalar shader.
  var packed=new RenderTexture(new RenderTextureDescriptor(1,1){graphicsFormat=GraphicsFormat.R32_UInt,enableRandomWrite=true,depthBufferBits=0,msaaSamples=1});packed.Create();owned.Add(packed);
  var n=new Vector3(.8f,.6f,0).normalized;
  uint bits=(uint)Mathf.RoundToInt((n.x*.5f+.5f)*255) | ((uint)Mathf.RoundToInt((n.y*.5f+.5f)*255)<<8) | ((uint)Mathf.RoundToInt((n.z*.5f+.5f)*255)<<16) | (255u<<24);
  var upload=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Editor/BentUpload.compute");
  upload.SetInt("_Bits",unchecked((int)bits));upload.SetTexture(0,"_Output",packed);upload.Dispatch(0,1,1,1);
  n=new Vector3((bits&255)/127.5f-1,((bits>>8)&255)/127.5f-1,((bits>>16)&255)/127.5f-1).normalized;

  double largestChange=0;
  foreach(bool hdr in new[]{true,false})
  foreach(float strength in new[]{0f,.6f,1f})
  foreach(float foliage in new[]{-1f,0f,.65f,1f})
  foreach(float distance in new[]{10f,67.5f,110f})
  foreach(float yaw in new[]{0f,20f})
  {
   SetAlbedo(.7f);Inputs(.6f,0,.7f,true,.4f);
   var rotation=Matrix4x4.Rotate(Quaternion.Euler(0,yaw,0));
   var toWorld=rotation*Matrix4x4.Scale(new Vector3(1,-1,-1));
   var bent=toWorld.MultiplyVector(n).normalized;
   // Use a known surface in the same hemisphere; rotate both with the camera.
   var surface=rotation.MultiplyVector(new Vector3(.4f,-.8f,.2f));
   properties.SetTexture("_CameraGBufferTexture2",Texture(new Color(surface.x*.5f+.5f,surface.y*.5f+.5f,surface.z*.5f+.5f,1)));
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",foliage<0?Texture2D.whiteTexture:Texture2D.blackTexture);
   properties.SetFloat("_FTFX_CA_FoliageStrength",Mathf.Max(0,foliage));
   properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.7f,distance,0,0)));
   properties.SetVector("_FTFX_CA_AODecode",new Vector4(2,.8f,1,0));
   properties.SetVector("_FTFX_CA_AOFade",new Vector4(35,1f/65,0,1));
   properties.SetTexture("_FTFX_CA_BentAO",packed);
   properties.SetVector("_FTFX_CA_BentSize",Vector4.one);
   properties.SetMatrix("_FTFX_CA_BentToWorld",toWorld);
   properties.SetFloat("_FTFX_CA_BentAvailable",1);
   fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");var baseline=Draw(fixedAmbient,hdr,strength);
   fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");var actual=Draw(fixedAmbient,hdr,strength);
   largestChange=Math.Max(largestChange,Error(actual,baseline));
   fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
   properties.SetTexture("_CameraGBufferTexture2",Texture(new Color(bent.x*.5f+.5f,bent.y*.5f+.5f,bent.z*.5f+.5f,1)));
   var full=Draw(fixedAmbient,hdr,strength);
   float weight=strength*(1-Mathf.Clamp01((distance-35)/65))*(foliage<0?1:foliage);
   var expected=baseline.Select((c,i)=>hdr?Color.Lerp(c,full[i],weight):new Color(Mathf.Pow(c.r,1-weight)*Mathf.Pow(full[i].r,weight),Mathf.Pow(c.g,1-weight)*Mathf.Pow(full[i].g,weight),Mathf.Pow(c.b,1-weight)*Mathf.Pow(full[i].b,weight),c.a)).ToArray();
   Check(Error(actual,expected)<.0002,"Bent SH reference, camera Y/Z transform, strength/foliage/fade HDR="+hdr+" strength="+strength+" foliage="+foliage+" distance="+distance+" yaw="+yaw);
  }
  CheckBentDebug(packed,n);
  Check(largestChange>.001,"Directional SH test actually changes lighting");
  foreach(bool hdr in new[]{true,false})
  {
   SetAlbedo(.7f);Inputs(.6f,1,.7f,false,.4f); // Reflection + fill only: neither may bend.
   properties.SetTexture("_FTFX_CA_BentAO",packed);properties.SetVector("_FTFX_CA_BentSize",Vector4.one);
   properties.SetMatrix("_FTFX_CA_BentToWorld",Matrix4x4.identity);properties.SetFloat("_FTFX_CA_BentAvailable",1);
   properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.7f,10,0,0)));
   properties.SetVector("_FTFX_CA_AODecode",new Vector4(2,.8f,1,0));properties.SetVector("_FTFX_CA_AOFade",new Vector4(35,1f/65,0,1));
   fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");var baseline=Draw(fixedAmbient,hdr,1);
   fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");Check(Error(baseline,Draw(fixedAmbient,hdr,1))<.00015,"Bent normals preserve reflection and fill HDR="+hdr);
   Inputs(.6f,0,.7f,true,.4f);properties.SetFloat("_FTFX_CA_BentAvailable",0);
   var unavailable=Draw(fixedAmbient,hdr,1);fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
   Check(Error(unavailable,Draw(fixedAmbient,hdr,1))<.00015,"Unavailable camera bent input preserves scalar ambient");
   // Invalid vectors and wrong-hemisphere directions must retain the exact native SH normal,
   // including its original G-buffer length rather than silently normalizing that baseline.
   properties.SetFloat("_FTFX_CA_BentAvailable",1);properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",Texture2D.whiteTexture);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.7f,10,0,0)));
   var unchanged=Draw(fixedAmbient,hdr,1);
   foreach(uint invalid in new[]{0xff808080u,0xff800080u})
   {
    upload.SetInt("_Bits",unchecked((int)invalid));upload.Dispatch(0,1,1,1);
    fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");
    Check(Error(unchanged,Draw(fixedAmbient,hdr,1))<.00015,"Invalid/opposite bent direction retains original shading normal HDR="+hdr);
    fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
   }
   upload.SetInt("_Bits",unchecked((int)bits));upload.Dispatch(0,1,1,1);
  }
 }
}
