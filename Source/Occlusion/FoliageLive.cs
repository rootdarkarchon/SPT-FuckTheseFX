using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    // Camera-owned, GPU-only observations. Native buffers retain their order and still execute.
    internal sealed class FoliageLive : IDisposable
    {
        internal const string OpaqueName = "Opaque Only Post-processing";
        private const string BufferPrefix = "FTFX foliage live: ";
        private readonly Camera camera;
        private readonly Material material;
        private readonly Mesh quad;
        private readonly RenderTexture[] stages = new RenderTexture[5];
        private RenderTexture display;
        private readonly CommandBuffer beforeOpaque, afterOpaque, afterAlpha;
        internal bool OpaqueFound { get; private set; }
        internal RenderTexture Display => display;
        internal RenderTexture Stage(int index) => stages[index];

        internal FoliageLive(Camera camera, GpuAssets assets, int width, int height)
        {
            this.camera = camera; quad = assets.Quad;
            material = new Material(assets.Composite) { hideFlags = HideFlags.HideAndDontSave };
            beforeOpaque = new CommandBuffer { name = BufferPrefix + "before opaque" };
            afterOpaque = new CommandBuffer { name = BufferPrefix + "after opaque" };
            afterAlpha = new CommandBuffer { name = BufferPrefix + "after transparency and preview" };
            try
            {
                for(int i=0;i<stages.Length;i++) stages[i] = GpuAssets.Target(BufferPrefix+i,width,height,GraphicsFormat.R16G16B16A16_SFloat);
                display = GpuAssets.Target(BufferPrefix+"preview",width,height,GraphicsFormat.R16G16B16A16_SFloat);
            }
            catch { Dispose(); throw; }
        }

        internal void BeginFrame(CommandBuffer cb)
        {
            // Missing ambient draw must be visible, rather than displaying an old frame.
            foreach(var target in stages) { cb.SetRenderTarget(target); cb.ClearRenderTarget(false,true,Color.cyan); }
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }
        private static void Copy(CommandBuffer cb, RenderTexture target)
        {
            cb.Blit(BuiltinRenderTextureType.CameraTarget,target);
            cb.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }
        internal void BeforeAmbient(CommandBuffer cb) => Copy(cb,stages[0]);
        internal void AfterAmbient(CommandBuffer cb) => Copy(cb,stages[1]);

        internal void Record(OcclusionSettings settings, RenderTexture ao, RenderTexture alpha, RenderTexture actors)
        {
            Detach();
            beforeOpaque.Clear(); afterOpaque.Clear(); afterAlpha.Clear();
            Copy(beforeOpaque,stages[2]);
            Copy(afterOpaque,stages[3]);
            if(settings.FoliageBypassOpaque.Value)
            {
                // Preserve native temporary targets/globals/history; replace only its scene-colour result.
                afterOpaque.Blit(stages[2],BuiltinRenderTextureType.CameraTarget);
                afterOpaque.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
            var buffers = camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque);
            int matches=0;
            foreach(var cb in buffers) if(cb.name == OpaqueName) matches++;
            OpaqueFound = matches == 1;
            if(OpaqueFound)
            {
                foreach(var cb in buffers) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
                foreach(var cb in buffers)
                {
                    if(cb.name == OpaqueName) camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,beforeOpaque);
                    camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
                    if(cb.name == OpaqueName) camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,afterOpaque);
                }
            }
            FoliageOcclusion.Configure(material,camera,settings,ao,alpha,actors);
            Copy(afterAlpha,stages[4]);
            var view=settings.FoliagePreview.Value;
            int index=view==FoliageLivePreview.BeforeAmbient?0:view==FoliageLivePreview.AfterAmbient?1:
                view==FoliageLivePreview.BeforeOpaque?2:view==FoliageLivePreview.AfterOpaque?3:4;
            bool difference=view==FoliageLivePreview.AmbientDifference || view==FoliageLivePreview.OpaqueDifference;
            if(difference) index=view==FoliageLivePreview.AmbientDifference?1:3;
            material.SetTexture("_LiveA",stages[index]);
            material.SetTexture("_LiveB",stages[difference?index-1:index]);
            material.SetVector("_LiveDisplay",new Vector4(settings.FoliageGain.Value,difference?1:0,view==FoliageLivePreview.ExperimentalMask?1:0,0));
            if(view != FoliageLivePreview.Scene)
            {
                afterAlpha.SetRenderTarget(display);
                afterAlpha.DrawMesh(quad,Matrix4x4.identity,material,0,9);
                afterAlpha.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
            camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha,afterAlpha);
        }
        private void Detach()
        {
            if(camera == null) return;
            camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,beforeOpaque);
            camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,afterOpaque);
            camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha,afterAlpha);
        }
        public void Dispose()
        {
            Detach(); beforeOpaque.Dispose(); afterOpaque.Dispose(); afterAlpha.Dispose();
            for(int i=0;i<stages.Length;i++) GpuAssets.Release(ref stages[i]);
            GpuAssets.Release(ref display); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
