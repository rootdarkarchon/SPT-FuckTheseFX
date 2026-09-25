using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class AmbientValidation
{
 static Vector3 ReferenceSH(Vector3 n)
 {
  float a=.03f*n.x+.06f*n.y-.02f*n.z+.15f;
  float b=.01f*n.y*n.x-.02f*n.z*n.y+.015f*n.z*n.z+.01f*n.x*n.z;
  float c=n.x*n.x-n.y*n.y;
  return new Vector3(Gamma(a+b+.02f*c),Gamma(a+b+.01f*c),Gamma(a+b+.03f*c));
 }
 static float Gamma(float x) => Mathf.Max(1.055f*Mathf.Pow(Mathf.Max(x,0),1/2.4f)-.055f,0);
 static Color ReadDiagnostic()
 {
  var read=AsyncGPUReadback.Request(diagnosticTarget,0);read.WaitForCompletion();Check(!read.hasError,"Lighting diagnostic GPU readback");
  return read.GetData<Color>()[32*64+32];
 }
 static void CheckBentDebug(RenderTexture packed,Vector3 bent)
 {
  diagnosticTarget=new RenderTexture(new RenderTextureDescriptor(64,64){graphicsFormat=GraphicsFormat.R32G32B32A32_SFloat,enableRandomWrite=true,depthBufferBits=0,msaaSamples=1});diagnosticTarget.Create();
  foreach(bool hdr in new[]{false,true}) foreach(float visibility in new[]{0f,.2f,1f}) foreach(float gain in new[]{1f,32f,1000f})
  {
   SetAlbedo(.7f);Inputs(visibility,0,.7f,true,.4f);
   properties.SetFloat("_FTFX_CA_BentAvailable",1);properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetTexture("_FTFX_CA_BentAO",packed);properties.SetVector("_FTFX_CA_BentSize",Vector4.one);properties.SetMatrix("_FTFX_CA_BentToWorld",Matrix4x4.identity);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.7f,10,0,0)));properties.SetVector("_FTFX_CA_AODecode",new Vector4(2,.8f,1,0));properties.SetVector("_FTFX_CA_AOFade",new Vector4(35,1f/65,0,1));
   fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");var baseline=Draw(fixedAmbient,hdr,.6f);
   fixedAmbient.EnableKeyword("FTFX_BENT_DIAGNOSTICS");
   foreach(int view in new[]{9,10})
   {
    properties.SetVector("_FTFX_CA_DebugInfo",new Vector4(view,gain,64,64));
    var actual=Draw(fixedAmbient,hdr,.6f);Check(Error(actual,baseline)<.00015,"Diagnostic capture preserves real ambient output HDR="+hdr);
    var delta=(ReferenceSH(bent)-ReferenceSH(new Vector3(.4f,.8f,-.2f)))*.6f;
    if(view==10) delta=Vector3.Scale(delta,new Vector3(.4f,.55f,.7f))*(Mathf.Pow(visibility,1.3f)*visibility*.82f);
    float expected=Mathf.Clamp01(Mathf.Max(Mathf.Abs(delta.x),Mathf.Max(Mathf.Abs(delta.y),Mathf.Abs(delta.z)))*gain);
    var color=ReadDiagnostic();Check(Mathf.Abs(color.r-expected)<.00025f && Mathf.Abs(color.g-expected)<.00025f && Mathf.Abs(color.b-expected)<.00025f,"Actual-pass difference matches independent SH equation: HDR="+hdr+" visibility="+visibility+" gain="+gain+" view="+view+" got="+color+" expected="+expected);
   }
   fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");Draw(fixedAmbient,hdr,.6f);var off=ReadDiagnostic();Check(off.r==1 && off.g==0 && off.b==0,"Red indicates bent keyword off");
   fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");properties.SetFloat("_FTFX_CA_BentAvailable",0);Draw(fixedAmbient,hdr,.6f);var missing=ReadDiagnostic();Check(missing.r==1 && missing.g==0 && missing.b==1,"Magenta indicates absent current-camera inputs");
   fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");
  }
  CheckAmbientComponents(packed,bent);
  diagnosticTarget.Release();UnityEngine.Object.DestroyImmediate(diagnosticTarget);diagnosticTarget=null;
  fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
 }
}
