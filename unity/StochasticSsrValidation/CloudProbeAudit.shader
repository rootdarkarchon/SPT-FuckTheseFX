Shader "Hidden/FTFX/CloudProbeAudit" {
 SubShader {
  Pass {
   ZTest Always ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex vert_img
   #pragma fragment Frag
   #include "UnityCG.cginc"
   TextureCube<float4> _Probe;
   SamplerState sampler_Probe;
   float4 _Direction;
   float4 Frag(v2f_img i):SV_Target {return _Probe.SampleLevel(sampler_Probe,normalize(_Direction.xyz),_Direction.w);}
   ENDHLSL
  }
 }
}
