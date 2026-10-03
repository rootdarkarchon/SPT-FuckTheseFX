Shader "Hidden/FTFX/StochasticSSR/Support"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #pragma target 5.0
#include "RasterState.hlsl"
        Texture2D<float4> _MainTex;
        float4 _Size;
        struct V { float4 position:SV_POSITION;float2 uv:TEXCOORD0; };
        V Vert(float3 p:POSITION) { V o;o.position=float4(p.xy,0,1);o.uv=(p.xy+1)*float2(.5,-.5)+float2(0,1);return o; }
        ENDHLSL
        // Exact pixel copy for an aliased native source/destination.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Copy
            float4 Copy(V i):SV_Target
            {
                int2 pixel=min((int2)i.position.xy,(int2)_Size.xy-1);
                return _MainTex.Load(int3(pixel,0));
            }
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            Texture2D<float> _CameraDepthTexture,_FTFXSSR_RawDepth;
            float4 _DepthOptions;
            float4x4 _InverseProjection;
            float Depth(V i):SV_Target
            {
                float2 pixel=i.position.xy;
                if(any(pixel>=_Size.xy))return _DepthOptions.y;
                float raw=_DepthOptions.w>.5?_FTFXSSR_RawDepth.Load(int3((int2)pixel,0)):_CameraDepthTexture.Load(int3((int2)pixel,0));
                if(_DepthOptions.z>.5?raw<.0000001:raw>.9999999)return _DepthOptions.y;
                float2 uv=pixel/_Size.xy;
                float4 h=mul(SsrRasterMatrix(_InverseProjection,4),float4(uv*2-1,raw,1));
                return clamp(-h.z/h.w,_DepthOptions.x,_DepthOptions.y);
            }
            ENDHLSL
        }
    }
}
