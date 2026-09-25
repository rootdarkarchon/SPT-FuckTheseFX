using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.Occlusion
{
    // Camera-local compute state; primary and actor estimators have different depth pyramids.
    internal sealed class OcclusionPipeline : IDisposable
    {
        private readonly ComputeShader compute;
        private readonly int prefilter, main, denoise, last, classify;
        private readonly int[] presets = new int[4];
        private readonly bool actor;
        internal RenderTexture Depth, Raw, Filtered;
        private RenderTexture edges, scratch, tiles;
        private int width, height;
        internal RenderTexture Result { get; private set; }

        internal OcclusionPipeline(ComputeShader shader, bool actor)
        {
            this.actor = actor;
            compute = UnityEngine.Object.Instantiate(shader);
            try
            {
                prefilter = compute.FindKernel("Prefilter"); main = compute.FindKernel("Custom");
                denoise = compute.FindKernel("Denoise"); last = compute.FindKernel("DenoiseLast"); classify = compute.FindKernel("ActorTiles");
                var names = new[] { "Low", "Medium", "High", "Ultra" };
                for(int i=0;i<4;i++) presets[i] = compute.FindKernel(names[i]);
                foreach (int kernel in new[] { prefilter, main, denoise, last, classify })
                    if (!compute.IsSupported(kernel)) throw new InvalidOperationException("Unsupported XeGTAO kernel.");
                foreach(int kernel in presets) if(!compute.IsSupported(kernel)) throw new InvalidOperationException("Unsupported XeGTAO quality kernel.");
            }
            catch { Dispose(); throw; }
        }

        internal void Prepare(int w, int h, int passes)
        {
            if (w != width || h != height || Depth == null || !Depth.IsCreated())
            {
                Release(); width = w; height = h;
                Depth = Target("Depth", GraphicsFormat.R32_SFloat, true);
                Raw = Target("Raw", GraphicsFormat.R32_UInt);
                Filtered = Target("Filtered", GraphicsFormat.R32_UInt);
                edges = Target("Edges", GraphicsFormat.R8_UNorm);
                // Also bind a valid tile resource in the non-actor kernels (D3D11 validation).
                tiles = GpuAssets.Target("FTFX AO tiles", actor ? (w+7)/8 : 1, actor ? (h+7)/8 : 1, GraphicsFormat.R32_UInt, true);
            }
            if (passes > 1 && scratch == null) scratch = Target("Scratch", GraphicsFormat.R32_UInt);
            if (passes <= 1) GpuAssets.Release(ref scratch);
        }
        private RenderTexture Target(string suffix, GraphicsFormat format, bool mips = false) =>
            GpuAssets.Target("FTFX " + (actor ? "Actor " : "Primary ") + suffix, width, height, format, true, mips);

        internal void Record(CommandBuffer cb, Camera camera, int quality, Parameters p, RenderTexture actorMask = null, float cutoff = 10, bool optimize = true)
        {
            float near = camera.nearClipPlane, far = camera.farClipPlane;
            float mul = near * far / (far-near), add = far/(far-near);
            if (SystemInfo.usesReversedZBuffer) { mul = -mul; add = -near/(far-near); }
            var projection = camera.projectionMatrix;
            cb.SetComputeVectorParam(compute, "_XeViewport", new Vector4(width,height,1f/width,1f/height));
            cb.SetComputeVectorParam(compute, "_XeDepthAndRadius", new Vector4(mul,add,p.Radius,p.Sharpness));
            cb.SetComputeVectorParam(compute, "_XeTuning", new Vector4(p.Multiplier,p.Falloff,p.Distribution,p.Thickness));
            cb.SetComputeVectorParam(compute, "_XeSampling", new Vector4(p.Slices > 0 ? p.Slices : quality == 3 ? 9 : quality+1, p.Steps > 0 ? p.Steps : quality < 2 ? 2 : 3,0,0));
            cb.SetComputeVectorParam(compute, "_XeNDCToView", new Vector4(2/projection.m00,-2/projection.m11,(projection.m02-1)/projection.m00,(1-projection.m12)/projection.m11));
            cb.SetComputeMatrixParam(compute, "_XeWorldToView", camera.worldToCameraMatrix);
            cb.SetComputeIntParam(compute, "_XeNormalSource", p.Normals ? 2 : 0);
            cb.SetComputeFloatParam(compute, "_XeFarDepth", far);
            cb.SetComputeFloatParam(compute, "_XeFinalPower", p.Power);
            cb.SetComputeIntParam(compute, "_XeActorOnly", actor && optimize ? 1 : 0);
            cb.SetComputeFloatParam(compute, "_XeActorCutoff", cutoff);
            cb.BeginSample(actor ? "FTFX Actor depth" : "FTFX Primary depth");
            cb.SetComputeTextureParam(compute,prefilter,"_XeRawDepth",BuiltinRenderTextureType.ResolvedDepth);
            for(int mip=0;mip<5;mip++) cb.SetComputeTextureParam(compute,prefilter,"_XeMip"+mip,Depth,mip);
            cb.DispatchCompute(compute,prefilter,(width+15)/16,(height+15)/16,1);
            cb.EndSample(actor ? "FTFX Actor depth" : "FTFX Primary depth");
            if(actor && optimize)
            {
                cb.BeginSample("FTFX Actor tiles");
                cb.SetComputeTextureParam(compute,classify,"_XeActorMask",actorMask);
                cb.SetComputeTextureParam(compute,classify,"_XeDepth",Depth);
                cb.SetComputeTextureParam(compute,classify,"_XeOutTiles",tiles);
                cb.DispatchCompute(compute,classify,(width+7)/8,(height+7)/8,1);
                cb.EndSample("FTFX Actor tiles");
            }
            cb.BeginSample(actor ? "FTFX Actor horizons" : "FTFX Primary horizons");
            int horizons = p.Slices > 0 || p.Steps > 0 ? main : presets[Mathf.Clamp(quality,0,3)];
            cb.SetComputeTextureParam(compute,horizons,"_XeRawDepth",BuiltinRenderTextureType.ResolvedDepth);
            cb.SetComputeTextureParam(compute,horizons,"_XeNormals",BuiltinRenderTextureType.GBuffer2);
            cb.SetComputeTextureParam(compute,horizons,"_XeDepth",Depth);
            cb.SetComputeTextureParam(compute,horizons,"_XeOutAO",Raw);
            cb.SetComputeTextureParam(compute,horizons,"_XeOutEdges",edges);
            cb.SetComputeTextureParam(compute,horizons,"_XeTiles",tiles);
            cb.DispatchCompute(compute,horizons,(width+7)/8,(height+7)/8,1);
            cb.EndSample(actor ? "FTFX Actor horizons" : "FTFX Primary horizons");
            Result = Raw;
            cb.BeginSample(actor ? "FTFX Actor denoise" : "FTFX Primary denoise");
            for(int pass=0;pass<p.Denoise;pass++)
            {
                int kernel = pass == p.Denoise-1 ? last : denoise;
                var output = (pass&1)==0 ? Filtered : scratch;
                cb.SetComputeTextureParam(compute,kernel,"_XeAO",Result);
                cb.SetComputeTextureParam(compute,kernel,"_XeEdges",edges);
                cb.SetComputeTextureParam(compute,kernel,"_XeOutAO",output);
                cb.SetComputeTextureParam(compute,kernel,"_XeTiles",tiles);
                cb.DispatchCompute(compute,kernel,(width+15)/16,(height+7)/8,1);
                Result = output;
            }
            cb.EndSample(actor ? "FTFX Actor denoise" : "FTFX Primary denoise");
        }
        private void Release()
        {
            GpuAssets.Release(ref Depth); GpuAssets.Release(ref Raw); GpuAssets.Release(ref Filtered);
            GpuAssets.Release(ref edges); GpuAssets.Release(ref scratch); GpuAssets.Release(ref tiles); Result = null;
        }
        public void Dispose() { Release(); if(compute != null) UnityEngine.Object.DestroyImmediate(compute); }
    }
}
