using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

// Minimal native ABI fixture. Installed method/field contracts are checked with Cecil.
public sealed class CommandBufferManager
{
    private readonly Dictionary<Camera, CommandBuffer> cameras = new Dictionary<Camera, CommandBuffer>();
    public CommandBuffer Get(Camera camera)
    {
        if (!cameras.TryGetValue(camera, out var buffer))
        {
            buffer = new CommandBuffer { name = "Ambient Highlight fixture" };
            cameras.Add(camera, buffer);
            camera.AddCommandBuffer(CameraEvent.AfterLighting, buffer);
        }
        return buffer;
    }
    public void DestroyBuffers()
    {
        foreach (var pair in cameras) { if (pair.Key != null) pair.Key.RemoveCommandBuffer(CameraEvent.AfterLighting, pair.Value); pair.Value.Dispose(); }
        cameras.Clear();
    }
}

public sealed class AmbientHighlight : MonoBehaviour
{
    private CommandBufferManager _ambientBufferManager = new CommandBufferManager();
    public int RenderCalls, RecordCalls, LightingCalls;
    public SphericalHarmonicsL2 LastLighting;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ManualOnRenderObject(Camera camera)
    {
        RenderCalls++;
        UpdateAmbientBuffer(_ambientBufferManager.Get(camera), camera);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateAmbientBuffer(CommandBuffer buffer, Camera camera)
    {
        RecordCalls++;
        buffer.Clear(); buffer.SetGlobalFloat("_FixtureHighlight", 1);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetSH(SphericalHarmonicsL2 sh) { LightingCalls++; LastLighting = sh; }
    public void OnDestroy() { _ambientBufferManager.DestroyBuffers(); }
}
