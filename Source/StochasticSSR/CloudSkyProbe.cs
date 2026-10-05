using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // CloudSix's main-camera draw cannot appear in AmbientLight's sky-only cubemap.
    // Supply its published upper sky at the native probe's existing update cadence.
    internal sealed class CloudSkyProbe : IDisposable
    {
        internal static CloudSkyProbe Active;
        internal Func<bool> Requested;
        private readonly SsrSkySource source=new SsrSkySource();
        private readonly FieldInfo face,nextTime,delay,cube;
        internal readonly MethodInfo Target;
        private readonly ConditionalWeakTable<object,ProbeState> states=new ConditionalWeakTable<object,ProbeState>();
        private readonly Material material;
        private readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        private readonly Mesh triangle;
        private readonly CommandBuffer commands=new CommandBuffer {name="FTFX CloudSix reflection probe sky"};
        private bool failed,disposed;
        private sealed class ProbeState { internal int Sky,Cube,Size; }
        internal sealed class UpdateState { internal CloudSkyProbe Owner;internal Texture Sky;internal int Mask; }

        internal CloudSkyProbe(Shader reflection,Type ambient)
        {
            face=AccessTools.Field(ambient,"_faceNum");nextTime=AccessTools.Field(ambient,"_nextRenderTime");
            delay=AccessTools.Field(ambient,"RenderDelay");cube=AccessTools.Field(ambient,"_cubeRT");
            Target=AccessTools.Method(ambient,"method_2",Type.EmptyTypes);
            if(face?.FieldType!=typeof(int) || nextTime?.FieldType!=typeof(float) || delay?.FieldType!=typeof(float) ||
               cube?.FieldType!=typeof(RenderTexture) || Target==null || Target.ReturnType!=typeof(void))
                throw new MissingMemberException("Unsupported AmbientLight reflection probe contract");
            material=new Material(reflection){hideFlags=HideFlags.HideAndDontSave};
            triangle=new Mesh {name="FTFX cloud probe triangle",hideFlags=HideFlags.HideAndDontSave};
            triangle.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
            triangle.triangles=new[]{0,1,2};triangle.UploadMeshData(true);
        }

        internal static void Before(object __instance,out UpdateState __state)
        {
            __state=null;var self=Active;if(self==null || self.disposed)return;
            try {
                var sky=!self.failed && self.Requested?.Invoke()==true?self.source.Read():null;
                var rt=self.cube.GetValue(__instance) as RenderTexture;
                var previous=self.states.GetValue(__instance,_=>new ProbeState());
                int id=sky!=null?sky.GetInstanceID():0,rtId=rt!=null?rt.GetInstanceID():0,size=rt!=null?rt.width:0;
                if(previous.Sky!=id || (sky!=null && (previous.Cube!=rtId || previous.Size!=size))) {
                    // Native's negative face index refreshes all six faces. This also restores
                    // native TOD on provider disable, without leaving five stale cloud faces.
                    self.nextTime.SetValue(__instance,float.NegativeInfinity);self.face.SetValue(__instance,-1);
                }
                previous.Sky=id;previous.Cube=rtId;previous.Size=size;
                if(sky==null || ((float)self.nextTime.GetValue(__instance)>=Time.realtimeSinceStartup &&
                   (float)self.delay.GetValue(__instance)!=0))return;
                int f=(int)self.face.GetValue(__instance);
                __state=new UpdateState {Owner=self,Sky=sky,Mask=f<0?63:1<<(f==6?0:f)};
            } catch(Exception e) {self.Fail(e);}
        }

        internal static void After(object __instance,UpdateState __state)
        {
            if(__state==null)return;var self=__state.Owner;
            try {
                var rt=self.cube.GetValue(__instance) as RenderTexture;
                if(rt==null || !rt.IsCreated())return;
                var previous=self.states.GetValue(__instance,_=>new ProbeState());
                previous.Cube=rt.GetInstanceID();previous.Size=rt.width;
                self.commands.Clear();self.Populate(self.commands,rt,__state.Sky,__state.Mask);
                Graphics.ExecuteCommandBuffer(self.commands);
            } catch(Exception e) {self.Fail(e);}
        }

        internal void Populate(CommandBuffer cb,RenderTexture target,Texture sky,int mask)
        {
            if(target==null || !target.IsCreated() || target.dimension!=TextureDimension.Cube || sky==null)return;
            properties.Clear();properties.SetInt("_ReplayInput",7);
            properties.SetTexture("_CloudSky",sky);properties.SetFloat("_CloudSkyAvailable",1);
            properties.SetFloat("_CloudProbeSize",target.width);
            cb.BeginSample("FTFX SSR/CloudSix probe sky");
            for(int f=0;f<6;f++)if((mask&(1<<f))!=0) {
                properties.SetInt("_CloudProbeFace",f);
                cb.SetRenderTarget(target,0,(CubemapFace)f); // Load preserves the native lower hemisphere.
                cb.DrawMesh(triangle,Matrix4x4.identity,material,0,(int)SsrReflectionPipeline.Pass.ReplayInput,properties);
            }
            if(target.useMipMap)cb.GenerateMips(target);
            cb.EndSample("FTFX SSR/CloudSix probe sky");
        }

        private void Fail(Exception error) {if(failed)return;failed=true;Plugin.MyLog.LogWarning("CloudSix reflection probe bridge unavailable; native probe retained. "+error);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(ReferenceEquals(Active,this))Active=null;
            commands.Dispose();if(material!=null)UnityEngine.Object.DestroyImmediate(material);
            if(triangle!=null)UnityEngine.Object.DestroyImmediate(triangle);
        }
    }
}
