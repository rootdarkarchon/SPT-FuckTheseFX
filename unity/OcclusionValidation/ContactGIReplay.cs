using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using FuckTheseFX.Source.Occlusion;
public static partial class OcclusionValidation {
 public static void ReplayContactGI() {
  try {
   NrdDiffuseBackend.ValidationLibrary=Path.GetFullPath("Output/FTFXNrd.dll");
   string folder=File.ReadAllText("contactgi-replay.txt").Trim();var frames=Directory.GetDirectories(folder).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
   Require(frames.Length>1,"multi-frame replay missing");NrdDiffuseBackend backend=null;int previous=-1;double maximum=0;int rendered=0;
   foreach(string frame in frames) {
    Require(File.ReadAllText(Path.Combine(frame,"readback.txt")).StartsWith("complete"),"capture has missing/error/cancelled readbacks: "+frame);
    var d=JsonUtility.FromJson<ContactGICapture.Description>(File.ReadAllText(Path.Combine(frame,"frame.json")));
    Require((d.schema==1||d.schema==2)&&(previous<0||d.frame==previous+1),"capture is incompatible or not consecutive");previous=d.frame;
    int w=d.schema==1?(d.width+d.divisor-1)/d.divisor:d.internalWidth,h=d.schema==1?(d.height+d.divisor-1)/d.divisor:d.internalHeight;
    Require(w>0&&h>0,"capture receiver dimensions");
    if(backend==null)backend=new NrdDiffuseBackend(w,h);
    var radiance=ReplayGITexture(frame,"raw",w,h,GraphicsFormat.R16G16B16A16_SFloat);
    var normal=ReplayGITexture(frame,"packed-normal",w,h,GraphicsFormat.A2B10G10R10_UNormPack32);
    var depth=ReplayGITexture(frame,"viewZ",w,h,GraphicsFormat.R32_SFloat);
    var motion=ReplayGITexture(frame,"motion",w,h,GraphicsFormat.R16G16B16A16_SFloat);
    var output=GpuAssets.Target("diffuse replay",w,h,GraphicsFormat.R16G16B16A16_SFloat,true);
    var f=new NrdDiffuseBackend.Frame{Projection=MatrixGI(d.projection),PreviousProjection=MatrixGI(d.previousProjection),View=MatrixGI(d.view),PreviousView=MatrixGI(d.previousView),Jitter=d.jitter,PreviousJitter=d.previousJitter,Reset=rendered==0?1u:d.reset,Frames=d.temporal?(uint)d.history:0,Index=(uint)d.frame,AntiFirefly=d.antiFirefly?1u:0,DeltaMs=d.deltaMs,Version=1,Radius=d.radius};
    using(var cb=new CommandBuffer()){backend.Queue(cb,radiance,normal,depth,motion,output,null,f);Graphics.ExecuteCommandBuffer(cb);}
    var actual=BleedPixels(output);var expected=ReplayGITexture(frame,"denoised",w,h,GraphicsFormat.R16G16B16A16_SFloat);var e=BleedPixels(expected);
    double error=actual.Zip(e,(a,b)=>Math.Sqrt(RGBDelta(a,b))).Max();maximum=Math.Max(maximum,error);Require(backend.Stats.Status==1,"native replay failed");
    GpuAssets.Release(ref radiance);GpuAssets.Release(ref normal);GpuAssets.Release(ref depth);GpuAssets.Release(ref motion);GpuAssets.Release(ref output);GpuAssets.Release(ref expected);rendered++;
   }
   backend.Dispose();File.WriteAllText("contactgi-replay-result.txt","Frames="+rendered+" Maximum packed-RGB error="+maximum.ToString("R")+". A capture starting with pre-existing history requires warm-up; raw inputs and guides are replayed, not inferred.");EditorApplication.Exit(0);
  } catch(Exception e){Debug.LogException(e);File.WriteAllText("failure.txt",e.ToString());EditorApplication.Exit(1);}
 }
 private static Matrix4x4 MatrixGI(float[] values){Require(values!=null&&values.Length==16,"matrix metadata");var m=new Matrix4x4();for(int i=0;i<16;i++)m[i]=values[i];return m;}
 private static RenderTexture ReplayGITexture(string folder,string name,int w,int h,GraphicsFormat format) {
  byte[] bytes=File.ReadAllBytes(Path.Combine(folder,name+".rgba32f"));Require(bytes.Length==w*h*16,"capture dimensions: "+name);
  var t=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);t.LoadRawTextureData(bytes);t.Apply();
  var rt=GpuAssets.Target("replay "+name,w,h,format,true);Graphics.Blit(t,rt);UnityEngine.Object.DestroyImmediate(t);return rt;
 }
}
