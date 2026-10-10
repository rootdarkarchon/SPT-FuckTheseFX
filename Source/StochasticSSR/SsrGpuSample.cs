using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Ordinary Unity GPU markers; no external profiler or capture integration.
    internal sealed class SsrGpuSample : IDisposable
    {
        private readonly CommandBuffer buffer;
        private readonly string name;
        private SsrGpuSample(CommandBuffer buffer, string name)
        { this.buffer=buffer;this.name="FTFX Stochastic SSR/"+name;buffer.BeginSample(this.name); }
        internal static SsrGpuSample Measure(CommandBuffer buffer, Camera camera, string name) =>
            new SsrGpuSample(buffer,camera!=null && camera.tag=="OpticCamera"?"Optic/"+name:name);
        public void Dispose() => buffer.EndSample(name);
    }
}
