using System;
using System.Globalization;
using UnityEngine;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Commands are recorded before PP configures jitter. This buffer is read at
    // execution and uploaded only after every component's OnPreCull has finished.
    internal sealed class SsrRasterState : IDisposable
    {
        internal sealed class Snapshot
        {
            internal Matrix4x4 Early,Projection,Stable,View,World,PreviousProjection,PreviousView;
            internal Vector2 Jitter,PreviousJitter;
            internal int Mode,Width,Height;
            internal bool Ready,History;
            internal string Metadata() => "\nRasterStateReady="+Ready+"\nAAMode="+Mode+
                "\nRasterInput="+Width+"x"+Height+"\nEarlyProjection="+Text(Early)+
                "\nRasterProjection="+Text(Projection)+"\nNonJitteredProjection="+Text(Stable)+
                "\nRasterPreviousProjection="+Text(PreviousProjection)+"\nRasterPreviousView="+Text(PreviousView)+
                "\nRasterJitterUV="+Jitter.x.ToString("R",CultureInfo.InvariantCulture)+","+Jitter.y.ToString("R",CultureInfo.InvariantCulture)+
                "\nRasterPreviousJitterUV="+PreviousJitter.x.ToString("R",CultureInfo.InvariantCulture)+","+PreviousJitter.y.ToString("R",CultureInfo.InvariantCulture)+
                "\nRasterHistoryCompatible="+History;
        }
        internal readonly ComputeBuffer Buffer=new ComputeBuffer(29,16);
        internal Snapshot Current {get;private set;}
        internal bool Enabled {get;private set;}
        private Snapshot completed;
        private Camera owner;
        private readonly Vector4[] rows=new Vector4[29];
        internal SsrRasterState() { Buffer.SetData(rows); }
        internal void Enable(Camera camera)
        {
            if(Enabled)return;
            owner=camera;Enabled=true;
            Camera.onPreRender+=BeforeRender;Camera.onPostRender+=AfterRender;
        }
        internal bool Record(Camera camera,int mode,int width,int height)
        {
            bool changed=completed!=null && (completed.Mode!=mode || completed.Width!=width || completed.Height!=height);
            Current=new Snapshot {Early=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false),Mode=mode,Width=width,Height=height,
                History=completed!=null && !changed};
            return changed;
        }
        internal void InvalidateHistory() { if(Current!=null)Current.History=false;completed=null; }
        private void BeforeRender(Camera camera) { if(camera==owner)FinalizeRaster(camera); }
        internal void FinalizeRaster(Camera camera)
        {
            var s=Current;if(s==null || s.Ready)return;
            s.Projection=GL.GetGPUProjectionMatrix(camera.projectionMatrix,false);
            s.Stable=GL.GetGPUProjectionMatrix(s.Mode==0?camera.projectionMatrix:camera.nonJitteredProjectionMatrix,false);
            s.View=camera.worldToCameraMatrix;s.World=camera.cameraToWorldMatrix;
            s.Jitter=JitterUV(s.Projection,s.Stable);
            s.PreviousProjection=completed!=null?completed.Projection:s.Projection;
            s.PreviousView=completed!=null?completed.View:s.View;
            s.PreviousJitter=completed!=null?completed.Jitter:s.Jitter;
            Store(0,s.Projection);Store(4,s.Projection.inverse);Store(8,s.World);Store(12,s.View);
            Store(16,s.PreviousProjection*s.PreviousView*s.World);
            Store(20,s.View*s.PreviousView.inverse);Store(24,s.PreviousProjection.inverse);
            var delta=s.PreviousJitter-s.Jitter;
            rows[28]=new Vector4(delta.x,delta.y,s.History?1:0,0);
            Buffer.SetData(rows);s.Ready=true;
        }
        private void AfterRender(Camera camera) { if(camera==owner)Commit(); }
        internal void Commit() { if(Current!=null && Current.Ready)completed=Current; }
        internal static Vector2 JitterUV(Matrix4x4 raster,Matrix4x4 stable) =>
            new Vector2(-.5f*(raster.m02-stable.m02),-.5f*(raster.m12-stable.m12));
        private void Store(int first,Matrix4x4 m) {for(int r=0;r<4;r++)rows[first+r]=m.GetRow(r);}
        private static string Text(Matrix4x4 m) {var v=new string[16];for(int i=0;i<16;i++)v[i]=m[i].ToString("R",CultureInfo.InvariantCulture);return string.Join(",",v);}
        public void Dispose()
        {
            if(Enabled){Camera.onPreRender-=BeforeRender;Camera.onPostRender-=AfterRender;}
            Enabled=false;Current=null;completed=null;Buffer.Dispose();
        }
    }
}
