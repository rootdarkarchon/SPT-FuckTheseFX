using UnityEngine;

public static partial class AmbientValidation
{
 static void CheckReflectionOcclusionSeparation()
 {
  foreach(bool hdr in new[]{false,true})
  foreach(float diffuse in new[]{0f,.25f,.7f,1f})
  foreach(float specular in new[]{diffuse,1f})
  {
   Inputs(.6f,0,.7f,true,.4f);SetAlbedo(diffuse);
   properties.SetFloat("_FTFX_CA_DiffuseCaptured",0);
   var baseline=Draw(fixedAmbient,hdr,1);
   SetAlbedo(specular);
   properties.SetTexture("_FTFX_CA_DiffuseOcclusion",Texture(new Color(diffuse,0,0,1)));
   properties.SetFloat("_FTFX_CA_DiffuseCaptured",1);
   Check(Error(baseline,Draw(fixedAmbient,hdr,1))<.00015,"Reflection AO cannot brighten diffuse/fill HDR="+hdr+" diffuse="+diffuse+" specular="+specular);
   Inputs(.6f,1,.7f,false,1);SetAlbedo(specular);
   properties.SetFloat("_FTFX_CA_DiffuseCaptured",1);
   Check(Error(Draw(native,hdr,0),Draw(fixedAmbient,hdr,1))<.00015,"Reflection remains native for shared specular alpha HDR="+hdr+" alpha="+specular);
  }
  properties.SetFloat("_FTFX_CA_DiffuseCaptured",0);
  // Verify the exact native reduced-reflection branch, then toggle the separate diffuse
  // input while keeping the excluded material alpha identical, as the AO composite does.
  foreach(bool hdr in new[]{false,true})
  foreach(float alpha in new[]{0f,.25f,.7f,1f})
  foreach(float motionDepth in new[]{0f,.49998f,.499995f,.5f,.9f})
  {
   Inputs(.6f,1,.7f,false,1);SetAlbedo(alpha);
   properties.SetTexture("_CameraMotionVectorsDepth",Texture(new Color(motionDepth,0,0,0)));
   Check(Error(Draw(native,hdr,0),Draw(fixedAmbient,hdr,1))<.00015,"Native vegetation reflection parity HDR="+hdr+" alpha="+alpha+" motionDepth="+motionDepth);
   Inputs(.6f,1,.7f,true,.4f);
   properties.SetTexture("_CameraMotionVectorsDepth",Texture(new Color(motionDepth,0,0,0)));
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",alpha==0?Texture2D.blackTexture:Texture2D.whiteTexture);
   properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.35f,10,0,0)));
   properties.SetVector("_FTFX_CA_AODecode",new Vector4(1,1,1,0));
   properties.SetVector("_FTFX_CA_AOFade",new Vector4(100,.01f,0,1));
   var baseline=Draw(fixedAmbient,hdr,.6f);
   properties.SetTexture("_FTFX_CA_DiffuseOcclusion",Texture(new Color(alpha,0,0,1)));
   properties.SetFloat("_FTFX_CA_DiffuseCaptured",1);
   Check(Error(baseline,Draw(fixedAmbient,hdr,.6f))<.00015,"Excluded vegetation retains ambient, fill, foliage AO and reflection HDR="+hdr+" alpha="+alpha+" motionDepth="+motionDepth);
   properties.SetFloat("_FTFX_CA_DiffuseCaptured",0);
  }
 }
}
