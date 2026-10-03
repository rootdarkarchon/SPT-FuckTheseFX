Shader "Hidden/FTFX/SSRMaterialResponseAudit"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _Resolve;
            SamplerState sampler_Resolve;
            float _AuditLod;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.xy,0,1); }
            float4 frag() : SV_Target { return _Resolve.SampleLevel(sampler_Resolve,float2(.5,.5),_AuditLod); }
            ENDHLSL
        }
    }
}
