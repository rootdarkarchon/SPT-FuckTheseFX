Shader "Hidden/FTFX/Test/JitterRaster"
{
 SubShader {
  Pass {
   Cull Off ZTest [_DepthTest] ZWrite On
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   float4x4 _RasterVP;
   struct V {float4 pos:SV_POSITION;float3 normal:TEXCOORD0;};
   V Vert(float4 p:POSITION,float3 n:NORMAL) {V o;o.pos=mul(_RasterVP,p);o.normal=n;return o;}
   struct O {float depth:SV_Target0;float4 normal:SV_Target1;float4 spec:SV_Target2;};
   O Frag(V i) {O o;o.depth=i.pos.z;o.normal=float4(normalize(i.normal)*.5+.5,1);o.spec=float4(.04,.04,.04,1);return o;}
   ENDHLSL
  }
  Pass {
   Cull Off ZTest Always ZWrite Off
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex vert_img
   #pragma fragment Probe
   #include "UnityCG.cginc"
   #include "RasterState.hlsl"
   float4 _TestUV;
   float4 Probe(v2f_img i):SV_Target {return float4(SsrHistoryUV(_TestUV.xy,_TestUV.zw),0,1);}
   ENDHLSL
  }
 }
}
