using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
namespace FuckTheseFX.Source.Occlusion {
 // Readbacks are ordered in the render buffer. Only diagnostic snapshots own
 // readback resources; resize/disposal of production targets cannot invalidate them.
 internal static class ContactGICapture {
  private sealed class Session {internal string Folder;internal int Count;internal bool Enabled;}
  private sealed class SnapshotData {internal RenderTexture Texture;internal string Name;}
  private sealed class Job {internal string Folder,Metadata;internal Func<string> Describe;internal int Pending;internal bool Failed,Committed;internal readonly List<Task> Writes=new List<Task>();internal readonly List<SnapshotData> Snapshots=new List<SnapshotData>();}
  private static readonly Dictionary<int,Session> sessions=new Dictionary<int,Session>();
  private static readonly Dictionary<int,List<Job>> awaiting=new Dictionary<int,List<Job>>();
  internal static string LastFolder {get;private set;}
  internal static string ValidationRoot {get;set;}
  [Serializable] internal sealed class Description {
   public int schema=2,width,height,resolutionPercent,internalWidth,internalHeight,frame,rays,steps,history,aa;
   public int divisor=0; // Read-only compatibility for schema-1 diagnostic captures.
   public bool gamma,temporal,antiFirefly;
   public float radius,tolerance,donorLimit,bounceFraction,strength,cutoff,reconstructionCutoff;
   public float[] raster,projection,view,previousProjection,previousView;public uint reset;public float deltaMs;
   public Vector2 jitter,previousJitter;
   public ulong textureBytes,nrdPermanentBytes,nrdTransientBytes;
  }
  internal static string Metadata(Camera c,ColorBleedSettings s,Vector4[] rows,uint frame,int aa,NrdDiffuseBackend.Frame native,ulong bytes,NrdDiffuseBackend.Statistics stats) {
   var d=new Description{reset=native.Reset,deltaMs=native.DeltaMs,projection=Values(native.Projection),view=Values(native.View),aa=aa,previousProjection=Values(native.PreviousProjection),previousView=Values(native.PreviousView),jitter=native.Jitter,previousJitter=native.PreviousJitter,width=c.pixelWidth,height=c.pixelHeight,resolutionPercent=s.Resolution.Value,frame=(int)frame,rays=s.Rays.Value,steps=s.RaySteps.Value,history=s.HistoryFrames.Value,gamma=QualitySettings.activeColorSpace==ColorSpace.Gamma,temporal=s.Temporal.Value,antiFirefly=s.AntiFirefly.Value,radius=s.Radius.Value,tolerance=s.DepthTolerance.Value,donorLimit=s.DonorLimit.Value,bounceFraction=s.BounceFraction,strength=s.Strength.Value,reconstructionCutoff=s.ReconstructionCutoff.Value,cutoff=s.Distance.Value,textureBytes=bytes,nrdPermanentBytes=stats.PermanentBytes,nrdTransientBytes=stats.TransientBytes,raster=new float[68]};
   d.internalWidth=ColorBleedPipeline.ScaledDimension(d.width,d.resolutionPercent);d.internalHeight=ColorBleedPipeline.ScaledDimension(d.height,d.resolutionPercent);
   for(int i=0;i<17;i++)for(int j=0;j<4;j++)d.raster[i*4+j]=rows[i][j];return JsonUtility.ToJson(d,true);
  }
  internal static void Record(CommandBuffer cb,Camera c,ColorBleedSettings s,RenderTexture source,RenderTexture depth,RenderTexture normal,RenderTexture geometry,RenderTexture viewZ,RenderTexture motion,RenderTexture raw,RenderTexture output,RenderTexture work,RenderTexture actors,RenderTexture visibility,Func<string> describe) {
   int id=c.GetInstanceID();if(!sessions.TryGetValue(id,out var session)||!session.Enabled){session=new Session{Enabled=true,Folder=Path.Combine(ValidationRoot??Path.Combine(BepInEx.Paths.BepInExRootPath,"FuckTheseFX","ContactGI"),DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")+"-"+id)};sessions[id]=session;}
   if(session.Count>=s.CaptureFrames.Value){session.Enabled=false;s.Capture.Value=false;return;}
   LastFolder=session.Folder;
   string folder=Path.Combine(session.Folder,(session.Count++).ToString("D4"));Directory.CreateDirectory(folder);
   var job=new Job{Folder=folder,Describe=describe};
   if(!awaiting.TryGetValue(id,out var jobs)){jobs=new List<Job>();awaiting[id]=jobs;}jobs.Add(job);
   Snapshot(cb,job,"source",source,source.width,source.height);
   Snapshot(cb,job,"depth",depth,depth.width,depth.height);
   Snapshot(cb,job,"actors",actors,actors.width,actors.height);Snapshot(cb,job,"visibility",visibility,visibility.width,visibility.height);
   Snapshot(cb,job,"packed-normal",normal,normal.width,normal.height);Snapshot(cb,job,"geometric-normal",geometry,geometry.width,geometry.height);
   Snapshot(cb,job,"viewZ",viewZ,viewZ.width,viewZ.height);
   Snapshot(cb,job,"motion",motion,motion.width,motion.height);
   Snapshot(cb,job,"raw",raw,raw.width,raw.height);
   Snapshot(cb,job,"denoised",output,output.width,output.height);
   if(work!=null)Snapshot(cb,job,"trace",work,work.width,work.height);
   Snapshot(cb,job,"_CameraGBufferTexture0",BuiltinRenderTextureType.GBuffer0,source.width,source.height);
   Snapshot(cb,job,"_CameraGBufferTexture1",BuiltinRenderTextureType.GBuffer1,source.width,source.height);
   Snapshot(cb,job,"_CameraGBufferTexture2",BuiltinRenderTextureType.GBuffer2,source.width,source.height);
   Snapshot(cb,job,"_CameraMotionVectorsTexture",BuiltinRenderTextureType.MotionVectors,source.width,source.height);
   Snapshot(cb,job,"composited",BuiltinRenderTextureType.CameraTarget,source.width,source.height);
   if(session.Count>=s.CaptureFrames.Value){session.Enabled=false;s.Capture.Value=false;}
  }
  private static void Snapshot(CommandBuffer cb,Job job,string name,RenderTargetIdentifier source,int width,int height) {
   var snapshot=GpuAssets.Target("GI capture "+name,width,height,GraphicsFormat.R32G32B32A32_SFloat);job.Pending++;
   cb.Blit(source,snapshot);
   job.Snapshots.Add(new SnapshotData{Texture=snapshot,Name=name});
  }
  private static void Readback(Job job,SnapshotData snapshot) {
   AsyncGPUReadback.Request(snapshot.Texture,0,request=>{
    if(request.hasError)job.Failed=true;
    else {byte[] bytes=request.GetData<byte>().ToArray();string path=Path.Combine(job.Folder,snapshot.Name+".rgba32f");job.Writes.Add(Task.Run(()=>File.WriteAllBytes(path,bytes)));}
    Retire(snapshot.Texture);if(--job.Pending==0)Finish(job);
   });
  }
  internal static void Cancel(Camera c) {
   if(!awaiting.TryGetValue(c.GetInstanceID(),out var jobs))return;
   foreach(var job in jobs){foreach(var snapshot in job.Snapshots)Retire(snapshot.Texture);File.WriteAllText(Path.Combine(job.Folder,"readback.txt"),"CANCELLED: frame never rendered");}jobs.Clear();
  }

  internal static void Commit(Camera c) {
   if(!awaiting.TryGetValue(c.GetInstanceID(),out var jobs))return;
   foreach(var job in jobs){job.Metadata=job.Describe();File.WriteAllText(Path.Combine(job.Folder,"frame.json"),job.Metadata);job.Committed=true;foreach(var snapshot in job.Snapshots)Readback(job,snapshot);if(job.Pending==0)Finish(job);}
   jobs.Clear();
  }
  private static float[] Values(Matrix4x4 m){var a=new float[16];for(int i=0;i<16;i++)a[i]=m[i];return a;}
  private static void Finish(Job job) {
   if(!job.Committed)return;
   Task.WhenAll(job.Writes).ContinueWith(t=>File.WriteAllText(Path.Combine(job.Folder,"readback.txt"),job.Failed||t.IsFaulted?"FAILED":"complete; little-endian RGBA32F, row-major; see frame.json dimensions"));
  }
  internal static void Retire(RenderTexture t){if(t!=null){t.Release();UnityEngine.Object.DestroyImmediate(t);}}
 }
}
