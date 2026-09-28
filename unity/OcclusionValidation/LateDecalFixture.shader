Shader "Hidden/Tests/FTFXLateDecal"
{
 Properties { _Opacity ("Opacity", Float) = 1 }
 SubShader
 {
  Pass
  {
   Cull Off ZWrite Off ZTest Always
   // Native deferred decal alpha blending, isolated from RGB to test AO survival.
   Blend SrcAlpha OneMinusSrcAlpha
   ColorMask A
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   float _Opacity;
   float4 Vert(appdata_img v):SV_POSITION { return float4(v.vertex.xy*2-1,0,1); }
   float4 Frag():SV_Target { return float4(0,0,0,_Opacity); }
   ENDHLSL
  }
 }
}
