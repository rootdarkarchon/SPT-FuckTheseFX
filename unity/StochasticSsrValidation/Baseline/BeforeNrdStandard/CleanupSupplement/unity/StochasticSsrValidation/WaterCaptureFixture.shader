Shader "Hidden/WaterForSSRv2" {
 SubShader {
  HLSLINCLUDE
  #include "UnityCG.cginc"
  float4 Vert(float4 p:POSITION):SV_POSITION {return UnityObjectToClipPos(p);}
  float4 Frag():SV_Target {return 0;}
  ENDHLSL
  Pass {
   ZWrite On ZTest LEqual Cull Back ColorMask 0
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   ENDHLSL
  }
  Pass {
   ZWrite On ZTest LEqual Cull Back ColorMask 0
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   ENDHLSL
  }
 }
}
