Shader "Hidden/Tests/ColorBleedSampling"
{
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  Pass
  {
   HLSLPROGRAM
   #pragma target 5.0
   #pragma only_renderers d3d11
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   #include "../XeGTAO/ColorBleedSampling.hlsl"
   int _Frame, _Samples, _Temporal;
   struct Varyings { float4 position:SV_POSITION; };
   Varyings Vert(appdata_img v)
   {
    Varyings o; o.position=float4(v.vertex.xy*2-1,0,1);
    #if UNITY_UV_STARTS_AT_TOP
     o.position.y=-o.position.y;
    #endif
    return o;
   }
   float4 Frag(Varyings i):SV_Target
   {
    int2 p=(int2)i.position.xy;
    uint seed=BleedSampleSeed(int2(p.x/_Samples,p.y),asuint(_Frame),_Temporal!=0);
    float2 offset=BleedDiskSample(seed,(uint)(p.x%_Samples),(uint)_Samples,_Temporal!=0);
    return float4(offset,dot(offset,offset),0);
   }
   ENDHLSL
  }
 }
}
