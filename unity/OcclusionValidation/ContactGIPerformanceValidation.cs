using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using FuckTheseFX.Source.Occlusion;
public static partial class OcclusionValidation {
 private static void ContactGIHierarchyValidation(GpuAssets assets) {
  var compute=assets.ColorBleedCompute;
  foreach(var size in new[]{new Vector2Int(1,7),new Vector2Int(2,1),new Vector2Int(7,9),new Vector2Int(259,147),new Vector2Int(2560,1440)}) {
   int w=size.x,h=size.y;var pixels=new Color[w*h];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color(1+(i*37%997)*.125f,0,0,0);
   var upload=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);upload.SetPixels(pixels);upload.Apply();
   var input=GpuAssets.Target("hierarchy source",w,h,GraphicsFormat.R32_SFloat,true);Graphics.Blit(upload,input);UnityEngine.Object.DestroyImmediate(upload);
   var levels=new List<RenderTexture>();int tw=w,th=h;while(tw>1||th>1){tw=(tw+1)/2;th=(th+1)/2;levels.Add(GpuAssets.Target("hierarchy level",tw,th,GraphicsFormat.R32_SFloat,true));}
   using(var cb=new CommandBuffer()) {
    var previous=input;
    for(int i=0;i<levels.Count;i+=3) {
     int count=Math.Min(3,levels.Count-i),kernel=compute.FindKernel(count==3?"Reduce3":count==2?"Reduce2":"Reduce");
     cb.SetComputeIntParams(compute,"_ReduceSize",previous.width,previous.height);cb.SetComputeTextureParam(compute,kernel,"_ReduceInput",previous);cb.SetComputeTextureParam(compute,kernel,"_ReduceOutput",levels[i]);
     if(count>=2)cb.SetComputeTextureParam(compute,kernel,"_ReduceSecond",levels[i+1]);if(count==3)cb.SetComputeTextureParam(compute,kernel,"_ReduceThird",levels[i+2]);
     cb.DispatchCompute(compute,kernel,(levels[i].width+7)/8,(levels[i].height+7)/8,1);previous=levels[i+count-1];
    }
    Graphics.ExecuteCommandBuffer(cb);
   }
   foreach(var level in levels) {
    int nw=level.width,nh=level.height;var expected=new Color[nw*nh];
    for(int y=0;y<nh;y++)for(int x=0;x<nw;x++) {
     float minimum=float.MaxValue;for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)minimum=Math.Min(minimum,pixels[Math.Min(y*2+dy,h-1)*w+Math.Min(x*2+dx,w-1)].r);
     expected[y*nw+x]=new Color(minimum,0,0,0);
    }
    var actual=BleedPixels(level);Require(actual.Zip(expected,(a,b)=>a.r==b.r).All(v=>v),"fused conservative hierarchy mismatch "+size+" level "+nw+"x"+nh);
    pixels=expected;w=nw;h=nh;
   }
   GpuAssets.Release(ref input);foreach(var level in levels){level.Release();UnityEngine.Object.DestroyImmediate(level);}
  }
  results.Add("PASS: fused min-depth levels equal independent CPU four-child minima at tiny, odd and 2560x1440 dimensions.");
 }
 // Draw alternative cutoff settings against the same current camera guides and
 // NRD output. CPU checks below independently verify luminance/Gamma contracts.
 private static Action AppendContactGIReconstructionValidation(ColorBleedPipeline p,GpuAssets assets,CameraOcclusion effect) {
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(ColorBleedPipeline);
  var material=(Material)type.GetField("material",flags).GetValue(p);var raster=(ComputeBuffer)type.GetField("raster",flags).GetValue(p);
  var hierarchy=(List<RenderTexture>)type.GetField("hierarchy",flags).GetValue(p);var depth=(RenderTexture)type.GetField("inputDepth",flags).GetValue(p);
  var actors=(RenderTexture)typeof(CameraOcclusion).GetField("actorMask",flags).GetValue(effect);var visibility=(RenderTexture)typeof(CameraOcclusion).GetField("resolved",flags).GetValue(effect);
  var cb=p.Commands;var materials=new List<Material>();var outputs=new List<RenderTexture>();float[] cutoffs={0,.001f,.005f,.02f,.1f};
  foreach(float cutoff in cutoffs) {
   var m=new Material(material.shader);m.CopyPropertiesFromMaterial(material);m.SetBuffer("_GIRaster",raster);m.SetInt("_GIDebugView",16);m.SetFloat("_GIDebugGain",1);m.SetFloat("_GIReconstructionCutoff",cutoff);
   foreach(string name in new[]{"_GISize","_GISettings","_GILimits"})m.SetVector(name,material.GetVector(name));
   m.SetTexture("_GIDepth",depth);m.SetTexture("_GIActors",actors);m.SetTexture("_GIVisibility",visibility);m.SetTexture("_GISource",p.Source);m.SetTexture("_GIOutput",p.Output);m.SetTexture("_GINormal",p.Guide);m.SetTexture("_GIGeometry",p.Geometry);m.SetTexture("_GIViewZ",p.ViewDepth);
   var output=GpuAssets.Target("cutoff validation",p.Source.width,p.Source.height,GraphicsFormat.R16G16B16A16_SFloat,false);materials.Add(m);outputs.Add(output);cb.SetRenderTarget(output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,m,0,1);
  }
  var gammaMaterial=new Material(material.shader);gammaMaterial.CopyPropertiesFromMaterial(materials[0]);var limits=material.GetVector("_GILimits");limits.z=1;gammaMaterial.SetVector("_GILimits",limits);gammaMaterial.SetBuffer("_GIRaster",raster);materials.Add(gammaMaterial);
  var gammaAddition=GpuAssets.Target("Gamma linear addition",p.Source.width,p.Source.height,GraphicsFormat.R16G16B16A16_SFloat,false);
  var gammaComposite=GpuAssets.Target("Gamma composite",p.Source.width,p.Source.height,GraphicsFormat.R16G16B16A16_SFloat,false);
  cb.SetRenderTarget(gammaAddition);cb.DrawMesh(assets.Quad,Matrix4x4.identity,gammaMaterial,0,1);cb.Blit(p.Source,gammaComposite);cb.SetRenderTarget(gammaComposite);cb.DrawMesh(assets.Quad,Matrix4x4.identity,gammaMaterial,0,0);
  return ()=> {
   var uncut=BleedPixels(outputs[0]);double error=uncut.Zip(BleedPixels(p.DebugTexture),RGBDelta).Max();Require(error<1e-9,"cutoff-zero validation not bound to production reconstruction error="+error+" sum="+uncut.Sum(v=>(double)v.r));
   Require(uncut.Any(v=>v.r>0),"reconstruction validation has no illuminated receivers");
   var source=BleedPixels(p.Source);var addition=BleedPixels(gammaAddition);var composite=BleedPixels(gammaComposite);
   for(int i=0;i<composite.Length;i++) {
    for(int c=0;c<3;c++)Require(Mathf.Abs(composite[i][c]-EncodeGI(DecodeGI(source[i][c])+addition[i][c]))<.001f,"Gamma composition disagrees with CPU decode/add/encode");
    Require(composite[i].a==source[i].a,"Gamma destination alpha changed");
   }
   var previous=uncut;int removed=0;
   for(int i=1;i<cutoffs.Length;i++) {
    var current=BleedPixels(outputs[i]);float cutoff=cutoffs[i];
    for(int q=0;q<current.Length;q++) {
     float luminance=uncut[q].r*.2126f+uncut[q].g*.7152f+uncut[q].b*.0722f;
     Require(current[q].r<=previous[q].r+.00002f&&current[q].g<=previous[q].g+.00002f&&current[q].b<=previous[q].b+.00002f,"cutoff increased contribution");
     if(luminance<cutoff*.999f)Require(current[q].r==0&&current[q].g==0&&current[q].b==0,"cutoff retained faint addition");
     if(luminance>cutoff*2.001f)Require(RGBDelta(current[q],uncut[q])<1e-9,"cutoff changed bright addition");
     if(luminance>0&&current[q].r==0&&current[q].g==0&&current[q].b==0)removed++;
     Require(current[q].a==1,"diagnostic alpha changed");
    }
    previous=current;
   }
   Require(removed>0,"cutoff fixture suppressed no faint light");Require(p.HierarchyDispatches==(hierarchy.Count+2)/3,"pipeline did not batch hierarchy");
   results.Add("PASS: current NRD reconstruction, cutoff zero, monotonic faint-light suppression, unchanged bright contributions and Gamma composition against independent CPU decode/add/encode with preserved alpha.");
   foreach(var m in materials)UnityEngine.Object.DestroyImmediate(m);foreach(var t in outputs.Concat(new[]{gammaAddition,gammaComposite})){t.Release();UnityEngine.Object.DestroyImmediate(t);}
  };
 }
}
