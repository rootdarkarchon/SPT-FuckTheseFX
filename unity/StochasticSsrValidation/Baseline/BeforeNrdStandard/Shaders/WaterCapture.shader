Shader "Hidden/FTFX/StochasticSSR/WaterCapture"
{
 SubShader {
  HLSLINCLUDE
  #include "UnityCG.cginc"
  Texture2D<float> _WaterSourceDepth,_WaterBottom;
  float _FTFXWaterBottomCaptured;
  struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float eye:TEXCOORD1;};
  V CopyVertex(float4 p:POSITION){V o;o.p=float4(p.xy,0,1);o.world=0;o.eye=0;return o;}
  float CopyDepth(V i):SV_Target {if(_FTFXWaterBottomCaptured>.5)discard;return LinearEyeDepth(_WaterSourceDepth.Load(int3((int2)i.p.xy,0)));}
  V WaterVertex(float4 p:POSITION){V o;o.p=UnityObjectToClipPos(p);o.world=mul(unity_ObjectToWorld,p).xyz;o.eye=-mul(UNITY_MATRIX_V,float4(o.world,1)).z;return o;}
  float4 Capture(V i):SV_Target {
   float bottom=_WaterBottom.Load(int3((int2)i.p.xy,0));
   if(!isfinite(bottom) || bottom<=0 || i.eye<=0 || bottom<i.eye-max(.002,i.eye*.0001))discard;
   float3 bed=_WorldSpaceCameraPos+(i.world-_WorldSpaceCameraPos)*(bottom/i.eye);
   float depth=bottom>=_ProjectionParams.z*.999?1000:max(0,i.world.y-bed.y);
   // Use raster depth, exactly as the native depth target does. Independently
   // interpolated eye depth drifts from quantized/projected depth at distance.
   return float4(LinearEyeDepth(i.p.z),depth,1,0);
  }
  ENDHLSL
  Pass {
   ZTest Always ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex CopyVertex
   #pragma fragment CopyDepth
   ENDHLSL
  }
  Pass {
   ZTest LEqual ZWrite Off Cull Back Blend Off
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex WaterVertex
   #pragma fragment Capture
   ENDHLSL
  }
 }
}
