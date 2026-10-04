Shader "Hidden/FTFX/StochasticSSR/NrdInputs" {
 SubShader {
  ZTest Always ZWrite Off Cull Off
  Pass {
   HLSLPROGRAM
   #pragma target 5.0
   #pragma vertex Vert
   #pragma fragment Inputs
   #include "RasterState.hlsl"
   Texture2D<float4> _Guide,_Point,_Record,_CameraGBufferTexture1,_CameraGBufferTexture2;
   Texture2D<float2> _CameraMotionVectorsTexture,_WaterCoverage;
   Texture2D<float> _CameraDepthTexture;
   float4 _Size,_Clip,_Eligibility;
   float4x4 _InvProjection;
   float4 _ZBufferParams;
   float _WaterAvailable;
   struct V {float4 p:SV_POSITION;};
   V Vert(float3 p:POSITION){V v;v.p=float4(p.xy,0,1);return v;}
   float3 Decode(float2 p){float3 n=float3(p,1-abs(p.x)-abs(p.y));if(n.z<0)n.xy=(1-abs(n.yx))*float2(n.x>=0?1:-1,n.y>=0?1:-1);return normalize(n);}
   struct Out {float4 normal:SV_Target0;float4 receiver:SV_Target1;float4 motion:SV_Target2;};
   Out Inputs(V v) {
    int2 q=min((int2)v.p.xy,(int2)_Size.zw-1);
    int2 p=min((int2)((q+.5)*_Size.xy/_Size.zw),(int2)_Size.xy-1);
    float4 g=_Guide.Load(int3(q,0)),endpoint=_Point.Load(int3(q,0)),record=_Record.Load(int3(q,0));
    float4 spec=_CameraGBufferTexture1.Load(int3(p,0)),encoded=_CameraGBufferTexture2.Load(int3(p,0));
    float3 original=encoded.xyz*2-1;
    float4 h=mul(SsrRasterMatrix(_InvProjection,4),float4((p+.5)/_Size.xy*2-1,.5,1));
    float3 receiver=h.xyz*(g.y/-h.z);
    bool eligible=g.y>_Clip.x && g.y<_Clip.y*.999 && spec.a>=_Eligibility.x &&
       !all(encoded==0) && dot(original,original)>1e-8 && all(isfinite(g)) && all(isfinite(spec)) && all(isfinite(encoded)) && all(isfinite(endpoint));
    if(_Eligibility.w>0 && spec.a==0 && any(spec.rgb!=0))eligible=false;
    float hit=record.w==1?length(endpoint.xyz-receiver):(record.w==2?65504:0);
    float raw=_CameraDepthTexture.Load(int3(p,0)),eye=rcp(_ZBufferParams.z*raw+_ZBufferParams.w);
    float2 water=_WaterCoverage.Load(int3(p,0));
    float waterID=_WaterAvailable>0 && water.x>0 && abs(eye-water.x)<=max(.005,eye*.0001)?1:0;
    Out o;o.normal=eligible?float4(Decode(g.zw),1-saturate(endpoint.w)):float4(0,0,1,1);
    o.receiver=float4(g.y,hit,eligible?1:0,waterID);
    // Unity's surface MV is current-previous UV. NRD expects previous-current.
    float2 motion=_CameraMotionVectorsTexture.Load(int3(p,0));
    o.motion=float4(all(isfinite(motion))?-motion:float2(0,0),0,0);return o;
   }
   ENDHLSL
  }
 }
}
