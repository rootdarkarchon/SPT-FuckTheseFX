using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
namespace FuckTheseFX.Source.Occlusion {
 internal sealed class NrdDiffuseBackend : IDisposable {
  [StructLayout(LayoutKind.Sequential)] internal struct Frame {
   internal Matrix4x4 Projection,PreviousProjection,View,PreviousView;
   internal Vector2 Jitter,PreviousJitter; // UV offsets; native bridge converts to NRD input pixels
   internal uint Reset,Frames,Index,AntiFirefly;
   internal float DeltaMs;
   internal uint Version;internal float Radius;internal uint Validation;
  }
  [StructLayout(LayoutKind.Sequential)] internal struct Statistics {
   internal ulong PermanentBytes,TransientBytes,AdapterBytes,Frames,Dispatches,Failures;
   internal int Status,Pending;internal double QueueUs,EventUs;
  }
  [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdDiffuseAbi();
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdDiffuseCreate(uint w,uint h,uint version);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdCallback();
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr FTFX_NrdDiffuseQueue(IntPtr id,IntPtr colour,IntPtr normal,IntPtr depth,IntPtr motion,IntPtr output,IntPtr validation,ref Frame frame);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern ulong FTFX_NrdDiffuseViewBuilds(IntPtr id);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdDiffuseUpdate(IntPtr token,ref Frame frame);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdCancel(IntPtr token);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdCancelOwner(IntPtr id);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdDestroy(IntPtr id);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern int FTFX_NrdStats(IntPtr id,out Statistics stats);
  [DllImport("FTFXNrd",CallingConvention=CallingConvention.Cdecl)] static extern void FTFX_NrdError(IntPtr id,StringBuilder text,uint size);
  internal static string ValidationLibrary {get;set;}
  private static IntPtr library;
  private IntPtr handle,token;
  private readonly RenderTexture[] textures=new RenderTexture[6];
  private readonly IntPtr[] pointers=new IntPtr[6];
  internal ulong NativeTextureQueries {get;private set;}
  internal ulong ViewBuilds=>FTFX_NrdDiffuseViewBuilds(handle);
  private IntPtr Pointer(int slot,RenderTexture texture) {
   if(texture==null){textures[slot]=null;pointers[slot]=IntPtr.Zero;return IntPtr.Zero;}
   if(!texture.IsCreated())throw new InvalidOperationException("NRD diffuse texture lost: "+texture.name);
   if(textures[slot]!=texture){textures[slot]=texture;pointers[slot]=texture.GetNativeTexturePtr();NativeTextureQueries++;}
   return pointers[slot];
  }
  internal Statistics Stats {get{FTFX_NrdStats(handle,out var s);return s;}}
  internal string Error {get{var s=new StringBuilder(512);FTFX_NrdError(handle,s,512);return s.ToString();}}
  internal NrdDiffuseBackend(int w,int h) {
   if(library==IntPtr.Zero)library=LoadLibrary(ValidationLibrary??Path.Combine(Path.GetDirectoryName(typeof(NrdDiffuseBackend).Assembly.Location),"FTFXNrd.dll"));
   if(library==IntPtr.Zero||FTFX_NrdDiffuseAbi()!=Marshal.SizeOf(typeof(Frame)))throw new InvalidOperationException("NRD diffuse library/ABI unavailable");
   handle=FTFX_NrdDiffuseCreate((uint)w,(uint)h,1);if(handle==IntPtr.Zero)throw new InvalidOperationException("NRD diffuse creation failed");
  }
  internal void Cancel(){if(token!=IntPtr.Zero)FTFX_NrdCancel(token);token=IntPtr.Zero;}
  internal void Queue(CommandBuffer cb,RenderTexture colour,RenderTexture normal,RenderTexture depth,RenderTexture motion,RenderTexture output,RenderTexture validation,Frame frame) {
   Cancel();if(Stats.Status<0)throw new InvalidOperationException("NRD diffuse dispatch failed: "+Error);
   token=FTFX_NrdDiffuseQueue(handle,Pointer(0,colour),Pointer(1,normal),Pointer(2,depth),Pointer(3,motion),Pointer(4,output),Pointer(5,validation),ref frame);
   if(token==IntPtr.Zero)throw new InvalidOperationException("NRD diffuse packet failed: "+Error);
   cb.IssuePluginEventAndData(FTFX_NrdCallback(),1,token);
  }
  internal void Update(Frame frame){if(token!=IntPtr.Zero)FTFX_NrdDiffuseUpdate(token,ref frame);}
  public void Dispose(){Cancel();if(handle!=IntPtr.Zero){FTFX_NrdCancelOwner(handle);FTFX_NrdDestroy(handle);handle=IntPtr.Zero;}}
 }
}
