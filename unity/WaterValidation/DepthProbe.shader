Shader "Hidden/FTFX/WaterDepthProbe"
{
    SubShader { Pass {
        ZTest LEqual ZWrite Off Cull Back
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        float4 vert(float4 v:POSITION):SV_POSITION {return UnityObjectToClipPos(v);}
        float4 frag():SV_Target {return float4(1,0,0,1);}
        ENDHLSL
    } }
}
