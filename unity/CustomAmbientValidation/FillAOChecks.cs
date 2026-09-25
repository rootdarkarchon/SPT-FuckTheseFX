using UnityEngine;

public static partial class AmbientValidation
{
 static void CheckFillAO()
 {
  // Isolate fill against the installed native shader, including ordinary clear nights.
  foreach(bool hdr in new[]{false,true})
  foreach(float ao in new[]{0f,.25f,1f})
  foreach(float strength in new[]{0f,.4f,1f})
  foreach(float fog in new[]{0f,1f})
  foreach(float nightVision in new[]{0f,1f}) {
   SetAlbedo(ao);Inputs(.6f,0,.7f,false,.4f);
   properties.SetFloat("TOD_Fogginess",fog);
   properties.SetFloat("_NightVisionOn",nightVision);
   var baseline=Draw(native,hdr,0);
   var expected=ScaleAmbient(baseline,hdr,Mathf.Lerp(1,ao,strength));
   Check(Error(expected,Draw(fixedAmbient,hdr,strength))<.00015,
    "Fill AO respects strength and native HDR/LDR encoding: HDR="+hdr+" AO="+ao+" strength="+strength+" fog="+fog+" NV="+nightVision);
  }
  foreach(bool hdr in new[]{false,true}) {
   SetAlbedo(0);Inputs(.6f,0,.7f,false,.4f);
   properties.SetFloat("_FTFX_CA_MaterialCaptured",0);
   Check(Error(Draw(native,hdr,0),Draw(fixedAmbient,hdr,1))<.00015,
    "Fill retains native baseline when material capture is unavailable HDR="+hdr);
  }
 }
}
