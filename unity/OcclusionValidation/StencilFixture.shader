Shader "Hidden/Tests/FTFXStencil"
{
 Properties { _StencilRef ("Stencil", Int) = 0 }
 SubShader
 {
  Pass
  {
   Cull Off ZWrite Off ZTest Always ColorMask 0
   Stencil { Ref [_StencilRef] WriteMask 3 Comp Always Pass Replace }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   float4 Vert(appdata_img v):SV_POSITION { return float4(v.vertex.xy*2-1,0,1); }
   float4 Frag():SV_Target { return 1; }
   ENDHLSL
  }
 }
}
