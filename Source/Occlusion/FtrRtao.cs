using UnityEngine;

namespace FuckTheseFX.Source.Occlusion
{
    /// <summary>
    /// CPU-side shader-global contract (version 1) with FuckTheseRays' hidden-occluder AO. Every global is written and read
    /// on the main thread through Shader.Set/GetGlobal*, never from command buffers and never by <see cref="CameraOcclusion.ResetGlobals()"/>.
    /// </summary>
    internal static class FtrRtao
    {
        internal const int Version = 1;
        private static readonly int RequestId = Shader.PropertyToID("_FTFX_RtaoRequest");
        private static readonly int StateId = Shader.PropertyToID("_FTR_RtaoState");
        private static readonly int TextureId = Shader.PropertyToID("_FTR_HiddenVisibility");

        /// <summary>Requests ray-traced AO for this frame: (frame, XeGTAO effective radius, RT AO radius, version).</summary>
        internal static void Request(OcclusionSettings settings) =>
            Shader.SetGlobalVector(RequestId, new Vector4(Time.frameCount, settings.EffectiveRadius, settings.RtRadius.Value, Version));

        /// <summary>Withdraws the request; FTR also treats a stale frame stamp as withdrawn.</summary>
        internal static void ClearRequest() => Shader.SetGlobalVector(RequestId, Vector4.zero);

        /// <summary>
        /// Reads FTR's published state for the main camera. Accepted only for a valid, versioned state written this frame or the
        /// previous one, with a live texture; the caller must additionally have an active request.
        /// </summary>
        internal static RtaoInput Accept(OcclusionSettings settings)
        {
            if (!settings.RtRequested) return RtaoInput.None;
            var state = Shader.GetGlobalVector(StateId);
            int age = Time.frameCount - (int)state.x;
            if (state.y != 1f || state.z != Version || age < 0 || age > 1) return RtaoInput.None;
            var texture = Shader.GetGlobalTexture(TextureId);
            if (texture == null) return RtaoInput.None;
            return new RtaoInput(texture, settings.RtStrengthFraction);
        }
    }

    /// <summary>
    /// The per-frame composite input for one camera: FTR's visibility texture and the strength fraction, or white with strength
    /// zero, which makes the composite shader skip the RT term and match the pure XeGTAO curve bit for bit.
    /// </summary>
    internal readonly struct RtaoInput
    {
        private static readonly int TextureId = Shader.PropertyToID("_RtaoVisibility");
        private static readonly int StrengthId = Shader.PropertyToID("_RtaoStrength");
        private readonly Texture texture;
        private readonly float strength;
        internal static RtaoInput None => default;
        internal bool Accepted => texture != null;
        internal RtaoInput(Texture texture, float strength) { this.texture = texture; this.strength = strength; }

        /// <summary>Binds the input to a material whose passes call the composite shader's Primary().</summary>
        internal void Apply(Material material)
        {
            material.SetTexture(TextureId, Accepted ? texture : Texture2D.whiteTexture);
            material.SetFloat(StrengthId, Accepted ? strength : 0f);
        }
    }
}
