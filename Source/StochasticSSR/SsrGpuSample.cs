using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FuckTheseFX.Source.StochasticSSR
{
    // Ordinary Unity GPU markers; no external profiler or capture integration.
    internal readonly struct SsrGpuSample : IDisposable
    {
        // Marker names come from a small fixed set of literals; build each string once.
        private static readonly Dictionary<string,string> MainNames=new Dictionary<string,string>();
        private static readonly Dictionary<string,string> OpticNames=new Dictionary<string,string>();
        private readonly CommandBuffer buffer;
        private readonly string name;
        private SsrGpuSample(CommandBuffer buffer, string name)
        { this.buffer=buffer;this.name=name;buffer.BeginSample(name); }
        private static string Marker(Dictionary<string,string> names,string prefix,string name)
        {
            if(!names.TryGetValue(name,out var marker)){marker=prefix+name;names.Add(name,marker);}
            return marker;
        }
        internal static SsrGpuSample Measure(CommandBuffer buffer, Camera camera, string name) =>
            new SsrGpuSample(buffer,camera!=null && camera.CompareTag("OpticCamera")?Marker(OpticNames,"FTFX Stochastic SSR/Optic/",name):Marker(MainNames,"FTFX Stochastic SSR/",name));
        public void Dispose() => buffer.EndSample(name);
    }
}
