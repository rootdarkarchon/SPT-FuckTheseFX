Shader "Hidden/FuckTheseFX/BloomFixture"
{
    Properties { _MainTex ("Input", 2D) = "black" {} _ColorBuffer ("Scene", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _ColorBuffer;
        float4 glow(v2f_img i) : SV_Target { return float4(tex2D(_MainTex, i.uv).rgb * .25, 0); }
        float4 combine(v2f_img i) : SV_Target { return float4(tex2D(_ColorBuffer, i.uv).rgb + tex2D(_MainTex, i.uv).rgb, 1); }
        v2f_img BloomVertex(appdata_img v)
        {
            v2f_img o; o.pos = v.vertex; o.uv = v.texcoord;
            #if UNITY_UV_STARTS_AT_TOP
            o.uv.y = 1 - o.uv.y;
            #endif
            return o;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment glow
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment combine
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex BloomVertex
            #pragma fragment combine
            ENDCG
        }
    }
}
