using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

// Production integration executes unchanged against this small matching ABI.
// The actual installed ABI is checked separately with Cecil.
public class DistantShadow : MonoBehaviour
{
    public enum ResolutionState { QUARTER, HALF, FULL }
    public Camera MasterCamera;
    public ResolutionState CurrentMaskResolution = ResolutionState.QUARTER;
    public ResolutionState FixtureObservedResolution;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Update() { FixtureObservedResolution = CurrentMaskResolution; }
    public bool EnableDistantShadowPCF = true, EnableAdaptiveBias, EnableMultiviewTiles, EnablePCFShift, FixtureAuxiliary;
    public Material ComputeShadowMaskMaterial;
    public Mesh mesh;
    private CommandBuffer _computeMaskCmdBuf;
    private CommandBuffer _computeMaskOpticCmdBuf;
    public void SetBuffer(CommandBuffer cb) { _computeMaskCmdBuf = cb; }
    public void SetOpticBuffer(CommandBuffer cb) { _computeMaskOpticCmdBuf = cb; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void SetupComputeShadowMaskCommandBuffer(CommandBuffer cb, int current, int previous, Vector4 projection,
        Vector4 parameters, Rect lowViewport, Rect fullViewport, ResolutionState resState, RenderTexture depth,
        RenderTexture mask, RenderTexture blur, RenderTexture highMask, RenderTexture highBuffer)
    {
        cb.SetRenderTarget(highMask);
        cb.DrawMesh(mesh, Matrix4x4.identity, ComputeShadowMaskMaterial, 0, 0);
        if (FixtureAuxiliary) cb.DrawMesh(mesh, Matrix4x4.identity, ComputeShadowMaskMaterial, 0, 1);
    }
}
