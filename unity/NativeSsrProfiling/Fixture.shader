Shader "Hidden/SSRProfilingFixture"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _SsrFixtureResolve;
        float4 Copy(v2f_img i):SV_Target { return tex2D(_MainTex,i.uv)*.75 + .025; }
        float4 Composite(v2f_img i):SV_Target { return tex2D(_MainTex,i.uv) + tex2Dlod(_SsrFixtureResolve,float4(i.uv,0,1))*.1; }
        ENDCG
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Copy
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Copy
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Copy
        ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Composite
        ENDCG }
    }
}
