using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class AmbientValidation
{
 static RenderTexture CaptureTarget()
 {
  var t=new RenderTexture(new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,enableRandomWrite=true,depthBufferBits=0,msaaSamples=1});t.Create();return t;
 }
 static Color CapturePixel(RenderTexture t)
 {
  var request=AsyncGPUReadback.Request(t,0);request.WaitForCompletion();Check(!request.hasError,"Foliage UAV readback");return request.GetData<Color>()[32*64+32];
 }
 static void CheckFoliageCapture()
 {
  diagnosticTarget=CaptureTarget();captureUnoccluded=CaptureTarget();captureRemoved=CaptureTarget();
  fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
  foreach(bool hdr in new[]{false,true}) foreach(float strength in new[]{0f,.6f,1f})
  foreach(float foliage in new[]{0f,.65f,1f}) foreach(float supplied in new[]{0f,.8f,1f})
  foreach(int status in new[]{1,2,3})
  {
   SetAlbedo(0);Inputs(1,0,.7f,true,.4f);
   properties.SetFloat("_FTFX_CA_MaterialCaptured",status==2?0:1);
   properties.SetFloat("_FTFX_CA_SeparateAOAvailable",status==3?0:1);
   properties.SetTexture("_FTFX_CA_OriginalOcclusion",Texture2D.blackTexture);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(supplied,10,0,0)));
   properties.SetVector("_FTFX_CA_AODecode",new Vector4(1,1,1,0));
   properties.SetVector("_FTFX_CA_AOFade",new Vector4(35,1f/65,0,1));
   properties.SetFloat("_FTFX_CA_FoliageStrength",foliage);
   properties.SetVector("_FTFX_CA_DebugInfo",new Vector4(18,1000,64,64));
   fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");fixedAmbient.DisableKeyword("FTFX_FOLIAGE_CAPTURE");
   var unoccluded=Draw(fixedAmbient,hdr,0);
   var baseline=Draw(fixedAmbient,hdr,strength);
   fixedAmbient.EnableKeyword("FTFX_BENT_DIAGNOSTICS");fixedAmbient.EnableKeyword("FTFX_FOLIAGE_CAPTURE");
   Check(Error(baseline,Draw(fixedAmbient,hdr,strength))<.00015,"Foliage instrumentation preserves HDR/LDR scene output");
   var ao=CapturePixel(diagnosticTarget);var raw=CapturePixel(captureUnoccluded);var removed=CapturePixel(captureRemoved);
   float effective=status==1?1-strength*foliage*(1-supplied):1;
   Check(Math.Abs(ao.r-effective)<.0001 && Math.Abs(ao.g-(status==3?1:supplied))<.0001 && ao.b==1 && ao.a==status,"Effective AO, supplied AO, zero-alpha marker and missing-input status");
   var expected=RGB(unoccluded[0]);
   if(!hdr) expected=new Vector3(-Mathf.Log(expected.x,2),-Mathf.Log(expected.y,2),-Mathf.Log(expected.z,2));
   Check((RGB(raw)-expected).magnitude<.0004f && raw.a==1,"Unoccluded ambient capture is ungained and independent of target encoding");
   Check((RGB(removed)-expected*(1-effective)).magnitude<.0004f && removed.a==1,"Removed ambient captures actual scalar attenuation, including full black and zero strength");
  }
  fixedAmbient.DisableKeyword("FTFX_FOLIAGE_CAPTURE");fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");
  foreach(var t in new[]{diagnosticTarget,captureUnoccluded,captureRemoved}) {t.Release();UnityEngine.Object.DestroyImmediate(t);}
  diagnosticTarget=captureUnoccluded=captureRemoved=null;
 }
}
