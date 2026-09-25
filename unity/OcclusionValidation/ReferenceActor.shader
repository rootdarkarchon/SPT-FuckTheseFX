// Frozen pre-optimization actor composite; validation only.
Shader "Hidden/Tests/ReferenceActor"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #pragma target 3.0
        #include "UnityCG.cginc"
        sampler2D _SecondMask, _SecondAO;
        float4 _SecondSettings; // strength, obscurance response, cutoff metres, packed depth scale
        struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(appdata_img input)
        {
            Varyings o;
            o.position = float4(input.vertex.xy * 2 - 1, 0, 1);
            o.uv = input.texcoord;
            #if UNITY_UV_STARTS_AT_TOP
                o.position.y = -o.position.y;
            #endif
            return o;
        }
        float4 Mask(Varyings i) : SV_Target { return 1; }
        float4 Visibility(Varyings i) : SV_Target
        {
            float2 ao = tex2D(_SecondAO, i.uv).rg;
            float actor = step(.5, tex2D(_SecondMask, i.uv).r);
            float depth = ao.y * _SecondSettings.w;
            float fade = 1 - smoothstep(_SecondSettings.z * .8, _SecondSettings.z, depth);
            // Cinematic-inspired response on obscurance, not a power on visibility.
            // Native's composite uses a 1/2.2 visibility exponent. This is an approximation
            // using a different AO estimator, with a smooth final 20% distance fade.
            float obscurance = saturate(pow(saturate(1 - ao.x), _SecondSettings.y) * _SecondSettings.x);
            float v = lerp(1, pow(max(1 - obscurance, 0), 1.0 / 2.2), actor * fade);
            return float4(v, v, v, 1);
        }
        ENDHLSL
        Pass
        {
            Name "CaptureActorMask"
            Stencil { Ref 0 ReadMask 3 WriteMask 0 Comp NotEqual Pass Keep Fail Keep ZFail Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Mask
            ENDHLSL
        }
        Pass
        {
            Name "MultiplyLitActors"
            Blend DstColor Zero
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Visibility
            ENDHLSL
        }
        Pass
        {
            Name "ActorAOView"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Visibility
            ENDHLSL
        }
    }
    Fallback Off
}
