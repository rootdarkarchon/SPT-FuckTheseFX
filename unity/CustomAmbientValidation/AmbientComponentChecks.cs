using UnityEngine;

public static partial class AmbientValidation
{
 static Vector3 RGB(Color c) => new Vector3(c.r,c.g,c.b);
 static void CheckAmbientComponents(RenderTexture packed, Vector3 bent)
 {
  var albedoRGB=new Vector3(.4f,.55f,.7f);
  var upward=new Vector3(Gamma(.19f),Gamma(.20f),Gamma(.18f));
  var luma=new Vector3(.2126f,.7152f,.0722f);
  foreach(bool hdr in new[]{false,true}) foreach(float visibility in new[]{0f,.2f,1f})
  foreach(bool useBent in new[]{false,true}) foreach(float gain in new[]{1f,32f})
  {
   SetAlbedo(.7f);Inputs(visibility,0,.7f,true,.4f);
   properties.SetFloat("_FTFX_CA_BentAvailable",1);properties.SetFloat("_FTFX_CA_SeparateAOAvailable",1);
   properties.SetTexture("_FTFX_CA_BentAO",packed);properties.SetVector("_FTFX_CA_BentSize",Vector4.one);properties.SetMatrix("_FTFX_CA_BentToWorld",Matrix4x4.identity);
   properties.SetTexture("_FTFX_CA_ScreenAO",Texture(new Color(.7f,10,0,0)));properties.SetVector("_FTFX_CA_AODecode",new Vector4(2,.8f,1,0));properties.SetVector("_FTFX_CA_AOFade",new Vector4(35,1f/65,0,1));
   if(useBent) fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");else fixedAmbient.DisableKeyword("FTFX_BENT_NORMALS");
   fixedAmbient.DisableKeyword("FTFX_BENT_DIAGNOSTICS");var baseline=Draw(fixedAmbient,hdr,.6f);
   fixedAmbient.EnableKeyword("FTFX_BENT_DIAGNOSTICS");
   float transmission=Mathf.Pow(visibility,1.3f)*visibility;
   var directional=ReferenceSH(new Vector3(.4f,.8f,-.2f));
   if(useBent) directional=Vector3.Lerp(directional,ReferenceSH(bent),.6f);
   var d=Vector3.Scale(directional,albedoRGB)*(transmission*.82f);
   var u=Vector3.Scale(upward,albedoRGB)*((visibility-transmission)*.82f);
   float luminance=Vector3.Dot(albedoRGB,luma);
   var f=Vector3.one*(luminance*luminance*(.05f+.05f*.7f)*.6f*.82f);
   var shares=new Vector3(Vector3.Dot(d,luma),Vector3.Dot(u,luma),Vector3.Dot(f,luma));shares/=shares.x+shares.y+shares.z;
   var sum=Vector3.zero;
   foreach(int view in new[]{11,12,13,14,15})
   {
    properties.SetVector("_FTFX_CA_DebugInfo",new Vector4(view,gain,64,64));
    Check(Error(Draw(fixedAmbient,hdr,.6f),baseline)<.00015,"Component diagnostics preserve ambient output");
    var expected=view==11?d:view==12?u:view==13?f:view==14?Vector3.one*transmission:shares;
    if(view<14) expected*=gain;
    expected=new Vector3(Mathf.Clamp01(expected.x),Mathf.Clamp01(expected.y),Mathf.Clamp01(expected.z));
    var captured=RGB(ReadDiagnostic());
    Check((captured-expected).magnitude<.0004f,"Component equation HDR="+hdr+" bent="+useBent+" visibility="+visibility+" gain="+gain+" view="+view+" actual="+captured+" expected="+expected);
    if(view<14 && gain==1) sum+=captured;
   }
   if(gain==1)
   {
    var expected=RGB(baseline[0]);
    if(!hdr) expected=new Vector3(-Mathf.Log(expected.x,2),-Mathf.Log(expected.y,2),-Mathf.Log(expected.z,2));
    Check((sum-expected).magnitude<.0004f,"Captured components sum to actual diffuse/fill output");
   }
  }
  // Component views remain useful without bent-camera inputs, including a completely dark pixel.
  fixedAmbient.EnableKeyword("FTFX_BENT_NORMALS");properties.SetFloat("_FTFX_CA_BentAvailable",0);
  SetAlbedo(.7f);Inputs(1,0,.7f,false,1);
  foreach(int view in new[]{11,12,13,15})
  {
   properties.SetVector("_FTFX_CA_DebugInfo",new Vector4(view,1000,64,64));Draw(fixedAmbient,true,.6f);
   Check(RGB(ReadDiagnostic())==Vector3.zero,"Zero lighting has finite black component/share output without bent inputs, view="+view);
  }
 }
}
