using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FuckTheseFX.Source.Occlusion;

public static partial class OcclusionValidation
{
 private static void ColorBleedSampling(GpuAssets assets)
 {
  var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Editor/ColorBleedSamplingProbe.shader");
  Require(shader!=null && shader.isSupported,"sampling probe unavailable");
  var material=new Material(shader);
  foreach(int samples in new[]{4,6,8,16,32})
  {
   var target=GpuAssets.Target("sampling probe",samples*128,32,GraphicsFormat.R32G32B32A32_SFloat);
   material.SetInt("_Samples",samples);
   Color[] Run(int frame,bool temporal=true)
   {
    material.SetInt("_Frame",frame);material.SetInt("_Temporal",temporal?1:0);
    var cb=new CommandBuffer();cb.SetRenderTarget(target);cb.DrawMesh(assets.Quad,Matrix4x4.identity,material,0,0);
    Graphics.ExecuteCommandBuffer(cb);cb.Dispose();return BleedPixels(target);
   }
   var first=Run(0);var second=Run(1);
   Require(first.Zip(Run(0),(a,b)=>a==b).All(v=>v),"same sampling inputs are not deterministic");
   foreach(int frame in new[]{64,4096,16777217,int.MaxValue,int.MinValue})
    Require(first.Zip(Run(frame),(a,b)=>Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)>.00001f).Count(v=>v)>first.Length*.99f,"sampling repeats/loses counter precision at "+frame);
   var bins=new int[16];
   for(int k=0;k<first.Length;k++)
   {
    var c=first[k];int tap=k%samples;
    Require(c.b>=tap/(float)samples-.000001f && c.b<=(tap+1f)/samples+.000001f,"sample outside its equal-area annulus");
    int bin=(int)(Mathf.Repeat(Mathf.Atan2(c.g,c.r)/(2*Mathf.PI),1)*bins.Length);bins[bin]++;
   }
   Require(Math.Abs(first.Average(c=>(double)c.b)-.5)<.01 && Math.Abs(first.Average(c=>(double)c.r))<.02 && Math.Abs(first.Average(c=>(double)c.g))<.02,"disk samples biased in area or direction");
   Require(bins.All(n=>Math.Abs(n-first.Length/16.0)<first.Length/16.0*.2),"angular distribution has major gaps");
   Vector2 Motion(int k)
   {
    var a=first[k];var b=second[k];float norm=Mathf.Sqrt(a.b*b.b);
    return new Vector2(a.r*b.r+a.g*b.g,a.r*b.g-a.g*b.r)/Mathf.Max(norm,1e-8f);
   }
   double coherence=0;int pairs=0;
   for(int y=0;y<32;y++)for(int x=0;x<127;x++)
   { int k=(y*128+x)*samples;coherence+=Vector2.Dot(Motion(k),Motion(k+samples));pairs++; }
   Require(Math.Abs(coherence/pairs)<.08,"neighboring pixels share a rotating temporal pattern");
   var fixedPattern=Run(0,false);
   Require(fixedPattern.Zip(Run(64,false),(a,b)=>a==b).All(v=>v),"temporal-off pattern changed with frame");
   results.Add("PASS: sampling "+samples+" taps; disk coverage, deterministic replay, no 64-frame repeat, full integer frame precision; adjacent angular-motion correlation="+(coherence/pairs).ToString("F3")+"; temporal-off fixed.");
   target.Release();UnityEngine.Object.DestroyImmediate(target);
  }
  UnityEngine.Object.DestroyImmediate(material);
 }
}
