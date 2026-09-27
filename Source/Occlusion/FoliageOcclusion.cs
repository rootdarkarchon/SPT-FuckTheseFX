using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    // Production composition uses the tested live shader pass without allocating stage snapshots.
    internal sealed class FoliageOcclusion : IDisposable
    {
        private readonly Camera camera;
        private readonly Material material;
        private readonly Mesh quad;
        private readonly CommandBuffer commands = new CommandBuffer { name = "FTFX foliage extra AO" };
        internal FoliageOcclusion(Camera camera, GpuAssets assets)
        {
            this.camera = camera; quad = assets.Quad;
            material = new Material(assets.Composite) { hideFlags = HideFlags.HideAndDontSave };
        }
        internal static void Configure(Material material, Camera camera, OcclusionSettings settings, RenderTexture ao, RenderTexture alpha, RenderTexture actors)
        {
            material.SetVector("_Size",new Vector4(ao.width,ao.height,camera.farClipPlane/65504f,0));
            material.SetVector("_AOFade",settings.DistanceFade);
            material.SetTexture("_PrimaryAO",ao);
            material.SetTexture("_LiveAlpha",alpha);
            material.SetTexture("_ActorMask",actors);
            material.SetVector("_LiveSettings",new Vector4(settings.FoliageExtraAO.Value,
                settings.FoliageLive && settings.FoliageSplit.Value ? settings.FoliageSplitPosition.Value : -1,camera.farClipPlane,0));
        }
        internal void Record(OcclusionSettings settings, RenderTexture ao, RenderTexture alpha, RenderTexture actors)
        {
            camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,commands);
            commands.Clear();
            var buffers=camera.GetCommandBuffers(CameraEvent.BeforeImageEffectsOpaque);
            int matches=0;
            foreach(var cb in buffers) if(cb.name==FoliageLive.OpaqueName) matches++;
            if(matches!=1) return;
            Configure(material,camera,settings,ao,alpha,actors);
            commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            commands.DrawMesh(quad,Matrix4x4.identity,material,0,8);
            // Shade the surface colour before the native opaque buffer blends fog over it.
            // Appending to this event would still run after fog; insert before the exact buffer.
            foreach(var cb in buffers) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
            foreach(var cb in buffers)
            {
                if(cb.name==FoliageLive.OpaqueName) camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,commands);
                camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,cb);
            }
        }
        public void Dispose()
        {
            if(camera!=null) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque,commands);
            commands.Dispose(); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
