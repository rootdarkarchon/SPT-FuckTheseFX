Shader "Hidden/FTFXValidation/FoliageFog"
{
 SubShader
 {
  Pass
  {
   Cull Off ZWrite Off ZTest Always
   Blend SrcAlpha OneMinusSrcAlpha
   ColorMask RGB
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   float4 _Fog;
   float4 Vert(appdata_img v):SV_POSITION
   {
    float4 p=float4(v.vertex.xy*2-1,0,1);
    #if UNITY_UV_STARTS_AT_TOP
     p.y=-p.y;
    #endif
    return p;
   }
   float4 Frag():SV_Target { return _Fog; }
   ENDHLSL
  }
 }
}
