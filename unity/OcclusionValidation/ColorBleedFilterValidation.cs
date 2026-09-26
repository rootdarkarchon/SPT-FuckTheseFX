using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FuckTheseFX.Source.Occlusion;

public static partial class OcclusionValidation
{
 private static void ColorBleedFilter(GpuAssets assets)
 {
  const int w=33,h=25;
  var mat=new Material(assets.ColorBleed);
  var output=GpuAssets.Target("filter contract",w,h,GraphicsFormat.R16G16B16A16_SFloat);
  var second=GpuAssets.Target("second filter contract",w,h,GraphicsFormat.R16G16B16A16_SFloat);
  var source=BleedTexture(w,h,(x,y)=>new Color(0,0,0,2));
  // Front-facing oct normal, open AO, representative offset (0,0).
  var guide=BleedTexture(w,h,(x,y)=>new Color(1,1,1,1f/255));
  var input=BleedTexture(w,h,(x,y)=>Color.clear);
  var moments=BleedTexture(w,h,(x,y)=>new Color(.1f,.2f,0,0));
  var state=BleedTexture(w,h,(x,y)=>new Color(2,0,0,512));
  mat.SetTexture("_CBMoments",moments);mat.SetTexture("_CBCurrentState",state);
  void Fill(Texture2D t,Func<int,int,Color> sample)
  {
   var p=new Color[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++)p[y*w+x]=sample(x,y);
   t.SetPixels(p);t.Apply();
  }
  mat.SetVector("_CBSize",new Vector4(w*2,h*2,w,h));
  mat.SetVector("_CBLimits",new Vector4(2,.2f,2,6));
  mat.SetVector("_CBSettings",new Vector4(.75f,1,.02f,25));
  mat.SetVector("_CBDepthUnpack",new Vector4(0,0,100,0));
  mat.SetVector("_CBNDCToView",new Vector4(1,-1,-.5f,.5f));
  mat.SetTexture("_CBSource",source);mat.SetTexture("_CBGuide",guide);mat.SetTexture("_CBBounce",input);
  mat.SetTexture("_CBSpatialInput",input);
  Color[] Run(int passes=2)
  {
   var cb=new CommandBuffer();cb.SetRenderTarget(output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4);
   if(passes>=2)
   {
    var properties=new MaterialPropertyBlock();properties.SetTexture("_CBSpatialInput",output);properties.SetFloat("_CBSpatialStep",2);
    cb.SetRenderTarget(second);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4,properties);
   }
   if(passes==3)
   {
    var properties=new MaterialPropertyBlock();properties.SetTexture("_CBSpatialInput",second);properties.SetFloat("_CBSpatialStep",4);
    cb.SetRenderTarget(output);cb.DrawMesh(assets.Quad,Matrix4x4.identity,mat,0,4,properties);
   }
   Graphics.ExecuteCommandBuffer(cb);cb.Dispose();return BleedPixels(passes==2?second:output);
  }
  Require(Run().All(c=>c==Color.clear),"filter created light from zero input");
  Fill(input,(x,y)=>new Color(.25f,.125f,.0625f,.5f));
  Require(Run().All(c=>Mathf.Abs(c.r-.25f)<.0002f && Mathf.Abs(c.a-.5f)<.0004f),"filter changed constant radiance/reference at edges");
  Fill(input,(x,y)=>x==w/2 && y==h/2?new Color(1,.5f,.25f,.125f):Color.clear);
  var impulse=Run();
  Require(impulse.Max(c=>c.r)<.1f && impulse.Count(c=>c.r>0)<=49 && Mathf.Abs(impulse.Sum(c=>c.r)-1)<.003f,"filter impulse peak="+impulse.Max(c=>c.r)+" count="+impulse.Count(c=>c.r>0)+" sum="+impulse.Sum(c=>c.r));
  Require(impulse.All(c=>Mathf.Abs(c.a-c.r*.125f)<.0001f),"filter RGB/reference weights differ");
  Fill(input,(x,y)=>((x*1973+y*9277+89173)%17)<4?new Color(1,.5f,.25f,.125f):Color.clear);
  var before=input.GetPixels();var after=Run();
  double Variance(Color[] a) { double mean=a.Average(c=>(double)c.r);return a.Average(c=>(c.r-mean)*(c.r-mean)); }
  double ratio=Variance(after)/Variance(before);
  double singleRatio=Variance(Run(1))/Variance(before);
  Require(ratio<singleRatio*.5,"second filter fails to reduce residual variance");
  Require(ratio<.4 && Math.Abs(after.Average(c=>c.r)-before.Average(c=>c.r))<.03,"filter failed speckle variance/mean check: "+ratio);
  Require(after.Zip(Run(),(a,b)=>a==b).All(v=>v),"filter changed on identical repeated input");
  mat.SetInt("_CBUseStatistics",1);
  var adaptive=Run(3);double adaptiveRatio=Variance(adaptive)/Variance(before);
  Require(adaptiveRatio<ratio*.85 && Math.Abs(adaptive.Average(c=>c.r)-before.Average(c=>c.r))<.03,"variance-guided scale fails to reduce noise or preserve mean: "+adaptiveRatio);
  Fill(moments,(x,y)=>new Color(.125f,.015625f,0,0));
  Require(after.Zip(Run(3),(a,b)=>a==b).All(v=>v),"mature zero-variance history receives unnecessary wider filtering");
  Fill(state,(x,y)=>new Color(2,0,0,32));
  Require(Variance(Run(3))<Variance(after),"young history does not receive wider filtering");
  Fill(state,(x,y)=>new Color(2,0,0,512));Fill(moments,(x,y)=>new Color(.1f,.2f,0,0));
  // Light on the right must not cross depth, normal, invalid-guide or AO barriers.
  Fill(input,(x,y)=>x<w/2?Color.clear:new Color(1,.5f,.25f,.125f));
  for(int barrier=0;barrier<4;barrier++)
  {
   Fill(source,(x,y)=>new Color(0,0,0,barrier==0 && x>=w/2?4:2));
   Fill(guide,(x,y)=>x<w/2?new Color(1,1,1,1f/255):
    barrier==1?new Color(.5f,.5f,1,1f/255):barrier==2?Color.clear:barrier==3?new Color(1,1,0,1f/255):new Color(1,1,1,1f/255));
   var edge=Run(3);
   for(int y=0;y<h;y++)for(int x=0;x<w/2;x++)Require(edge[y*w+x].r==0,"filter leaked across barrier "+barrier);
   if(barrier==2)Require(edge.All(c=>c.r==0),"invalid guide received light");
  }
  // Sloped plane: adjacent depths differ but tangent-plane errors are zero.
  Fill(source,(x,y)=>new Color(0,0,0,2/(1-.6f*((x*2+.5f)/(w*2)-.5f))));
  Fill(guide,(x,y)=>new Color(1,13f/16,1,1f/255));
  Fill(input,(x,y)=>x==w/2 && y==h/2?new Color(1,.5f,.25f,.125f):Color.clear);
  var slope=Run();Require(slope.Count(c=>c.r>0)>9 && slope.Max(c=>c.r)<.1f,"filter rejected compatible sloped neighbors");
  UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(guide);UnityEngine.Object.DestroyImmediate(input);
  UnityEngine.Object.DestroyImmediate(moments);UnityEngine.Object.DestroyImmediate(state);
  UnityEngine.Object.DestroyImmediate(mat);GpuAssets.Release(ref output);GpuAssets.Release(ref second);
  results.Add("PASS: spatial filter variance ratio="+ratio.ToString("F3")+" versus single pass="+singleRatio.ToString("F3")+"; constant/zero radiance, impulse energy/reference, repeated inputs, odd dimensions, depth/normal/invalid/AO barriers and sloped-plane smoothing.");
  results.Add("PASS: adaptive third-scale variance ratio="+adaptiveRatio.ToString("F3")+"; mature stable bypass, young-history filtering and all geometry/AO barriers.");
 }
}
