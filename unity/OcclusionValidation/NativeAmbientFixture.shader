// Stand-in for EFT's native ambient shader; the bridge identifies the native material by this name.
Shader "Hidden/WriteScreenAmbient"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return float4(vertex.xy, 0.5, 1); }
            float4 frag() : SV_Target { return float4(0, 1, 0, 1); }
            ENDCG
        }
    }
}
