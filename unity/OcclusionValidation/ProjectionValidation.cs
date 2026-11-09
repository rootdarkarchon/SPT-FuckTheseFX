using System;
using System.Linq;
using FuckTheseFX.Source.Occlusion;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public static partial class OcclusionValidation
{
 private static float[] ReadFloat(RenderTexture texture)
 {
  var request=AsyncGPUReadback.Request(texture,0);request.WaitForCompletion();Require(!request.hasError,"GPU readback failed");return request.GetData<float>().ToArray();
 }
 // Square-grid 90 degree rotations (pixel centres map exactly onto pixel centres): direction 0 sends (i,j) to (n-1-j,i), direction 1 to (j,n-1-i); they are inverses.
 private static T[] Rotate90<T>(T[] source,int n,int direction)
 {
  var result=new T[source.Length];
  for(int j=0;j<n;j++) for(int i=0;i<n;i++) result[(direction==0?i:n-1-i)*n+(direction==0?n-1-j:j)]=source[j*n+i];
  return result;
 }
 // Packed scalar AO holds visibility*255 (values above 255 clamp to neutral); the result is the mean difference in visibility units.
 private static double MeanDelta(uint[] a,uint[] b)
 {
  double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(Math.Min(255.0,a[i])-Math.Min(255.0,b[i]));return sum/a.Length/255;
 }
 // Fraction of pixels whose linear depth differs by more than 1%; robust against huge sky depths at a few rasterisation-tie edge pixels.
 private static double DepthMismatch(float[] a,float[] b)
 {
  int count=0;for(int i=0;i<a.Length;i++)if(Math.Abs((double)a[i]-b[i])>.01*Math.Max(1.0,Math.Abs(a[i])))count++;return (double)count/a.Length;
 }
 // Records with the "recorded" projection, then renders (and optionally refreshes the pipeline) with the "rendered" one.
 private static void RenderProjectionAO(Camera camera,OcclusionPipeline pipeline,Parameters p,int size,Matrix4x4 recorded,Matrix4x4 rendered,bool refresh,out uint[] ao,out float[] depth)
 {
  camera.projectionMatrix=recorded;pipeline.Prepare(size,size,p.Denoise);
  var cb=new CommandBuffer();pipeline.Record(cb,camera,3,p);
  camera.projectionMatrix=rendered;if(refresh)pipeline.UpdateProjection(camera);
  camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);camera.Render();camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);cb.Dispose();
  ao=ReadUInt(pipeline.Result);depth=ReadFloat(pipeline.Depth);
 }
 private static void ProjectionRoll(GpuAssets assets)
 {
  const int n=128;
  // Mean visibility error of the rotated standard map against the rolled render. Noise is pixel-indexed (not rotated) so the maps are not bit-identical;
  // 12 slices x 6 steps and two denoise passes keep that residual to roughly a percent, 0.015 (about 4/255) leaves margin. The result must also be
  // at least 4x closer than both the unrotated and the opposite-direction map, which proves the rotation (and its direction) was honoured.
  const double tolerance=.015,separation=4,staleFloor=.02,depthTolerance=.01;
  var camera=Scene(n,n,out var target,out var geometry);
  // Tilt the scene in view space: view-space normals then carry x/y components, so a wrong position/normal frame changes the AO.
  var pivot=new Vector3(0,0,3.5f);var tilt=Quaternion.Euler(20,-30,10);
  foreach(var go in geometry){go.transform.position=pivot+tilt*(go.transform.position-pivot);go.transform.rotation=tilt*go.transform.rotation;}
  var standard=camera.projectionMatrix;
  var roll=Matrix4x4.identity;roll.m00=0;roll.m01=-1;roll.m10=1;roll.m11=0;
  var rolled=roll*standard;
  Require(rolled.m00==0 && rolled.m11==0 && rolled.m01!=0 && rolled.m10!=0,"roll fixture projection is not a 90 degree roll");
  var summary="";
  using(var plain=new OcclusionPipeline(assets.Compute,false))
  using(var turned=new OcclusionPipeline(assets.Compute,false))
  using(var late=new OcclusionPipeline(assets.Compute,false))
  using(var stale=new OcclusionPipeline(assets.Compute,false))
  {
   foreach(bool normals in new[]{true,false})
   {
    var p=new Parameters{Radius=1,Multiplier=1.457f,Falloff=.615f,Distribution=2.5f,Sharpness=.4f,Power=1.5f,Denoise=2,Normals=normals,Slices=12,Steps=6};
    string label=normals?"G-buffer normals":"depth normals";
    RenderProjectionAO(camera,plain,p,n,standard,standard,false,out var aoA,out var depthA);
    RenderProjectionAO(camera,turned,p,n,rolled,rolled,false,out var aoB,out var depthB);
    Require(aoA.Count(v=>v<250)>n*n/20,"projection fixture did not exercise occlusion ("+label+")");
    double depthIdentity=DepthMismatch(depthA,depthB),depth0=DepthMismatch(Rotate90(depthA,n,0),depthB),depth1=DepthMismatch(Rotate90(depthA,n,1),depthB);
    int direction=depth0<=depth1?0:1;double depthMatched=Math.Min(depth0,depth1);
    Require(depthMatched<depthTolerance,"rolled depth is not the standard depth rotated by 90 degrees: mismatch "+depth0+"/"+depth1+" ("+label+")");
    Require(Math.Min(depthIdentity,Math.Max(depth0,depth1))>.2,"rolled projection did not rotate the rendered image ("+label+")");
    double matched=MeanDelta(Rotate90(aoA,n,direction),aoB),unrotated=MeanDelta(aoA,aoB),opposite=MeanDelta(Rotate90(aoA,n,1-direction),aoB);
    string numbers=label+" rotated="+matched.ToString("0.0000")+" unrotated="+unrotated.ToString("0.0000")+" opposite="+opposite.ToString("0.0000");
    Require(matched<tolerance,"rolled AO differs from the rotated standard AO: "+numbers);
    Require(matched*separation<Math.Min(unrotated,opposite),"rolled AO is not separated from unrotated/opposite maps: "+numbers);
    // Late-projection contract: the matrix changed after Record and refreshed before Render equals recording with that matrix; skipping the refresh must not.
    RenderProjectionAO(camera,late,p,n,standard,rolled,true,out var aoLate,out _);
    Require(aoLate.Length==aoB.Length && Enumerable.Range(0,aoB.Length).All(i=>aoLate[i]==aoB[i]),"UpdateProjection after Record differs from recording the final projection ("+label+")");
    RenderProjectionAO(camera,stale,p,n,standard,rolled,false,out var aoStale,out _);
    double staleDelta=MeanDelta(aoStale,aoB);
    // Depth-reconstructed normals rotate with the stale constants, so only G-buffer normals expose the stale projection.
    if(normals) Require(staleDelta>staleFloor,"stale projection constants were not distinguishable: "+staleDelta);
    summary+=(summary.Length==0?"":"; ")+numbers+(normals?" stale="+staleDelta.ToString("0.0000"):"")+" direction="+direction;
   }
  }
  camera.ResetProjectionMatrix();DestroyScene(camera,target,geometry);
  results.Add("PASS: 90-degree rolled projection (m00=m11=0) AO matches the rotated standard AO at "+n+"x"+n+" ("+summary+", mean visibility error); UpdateProjection after Record is bit-identical to recording the final projection.");
 }
}
