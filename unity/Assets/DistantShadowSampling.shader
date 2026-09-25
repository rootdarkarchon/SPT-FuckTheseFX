Shader "Hidden/FuckTheseFX/DistantShadowSampling"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile __ LOWRES_DEPTH
            #pragma multi_compile __ DISTANT_SHADOWS_BLEND
            #pragma multi_compile __ ENABLE_PARALLAX
            #pragma multi_compile __ USE_GAUSS_DISTRIBUTION
            #pragma multi_compile __ RENDER_FOR_SCOPE
            #include "UnityCG.cginc"

            // EFT 40743 mask pass 0, limited to the explicitly supported PCF path.
            // Other passes and unsupported feature combinations retain EFT's shader.
            Texture2D<float> _CameraDepthTexture, _LowResDepth;
            Texture2D<float> GlobalShadowSampled, GlobalShadowSampled2;
            SamplerState sampler_GlobalShadowSampled, sampler_GlobalShadowSampled2;
            float4x4 GlobalShadowWorldToScreen, GlobalShadowWorldToScreen2;
            float4 GlobalShadowL0, GlobalShadowL1, GlobalShadowL0Old;
            float4 GlobalShadowSettings, GlobalShadowTexelSize, GlobalShadowProj;
            float ParallaxSearchMul;
            int _FTFX_DSCount;
            float4 _FTFX_DSTaps[25];
            #if defined(RENDER_FOR_SCOPE)
                StructuredBuffer<float2> HaltonOffsets;
            #endif

            struct Input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varying { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varying Vert(Input i)
            {
                Varying o;
                o.position = float4(i.vertex.xy, .5, i.vertex.w);
                o.uv = i.uv;
                return o;
            }

            float3 FindOccluder(float3 position, float3 oldLight, float4x4 projection)
            {
                // Preserve the native fixed 50-step search, including use of the current
                // depth texture for BOTH searches. Changing that is a separate behavior fix.
                float3 direction = GlobalShadowL1.xyz * (1 + length(GlobalShadowL1.xyz - oldLight)) - oldLight;
                float3 stepPosition = direction * (ParallaxSearchMul / 50.0);
                float3 searchPosition = position;
                float sum = 0, count = 0;
                [loop] for (int i = 0; i < 50; i++)
                {
                    float4 projected = mul(projection, float4(searchPosition, 1));
                    float3 p = projected.xyz / projected.w;
                    float depth = GlobalShadowSampled.SampleLevel(sampler_GlobalShadowSampled, p.xy * .5 + .5, 0);
                    if (depth < p.z) { sum += p.z - depth; count += 1; }
                    searchPosition += stepPosition;
                }
                return position + direction * (count > 0 ? sum / count : 0);
            }

            float Filter(float3 position, float4x4 projection, bool previous)
            {
                float4 projected = mul(projection, float4(position, 1));
                float2 uv = projected.xy / projected.w * .5 + .5;
                float z = projected.z / projected.w;
                float sum = 0, normalization = 0;
                [loop] for (int i = 0; i < _FTFX_DSCount; i++)
                {
                    float weight = 1;
                    #if defined(RENDER_FOR_SCOPE)
                        // EFT's scoped variant uses these native low-discrepancy offsets,
                        // uniform weights and no Gaussian rotation, even with Gauss enabled.
                        float2 offset = HaltonOffsets[i] * 2.5 * GlobalShadowTexelSize.xy;
                    #else
                        float2 offset = _FTFX_DSTaps[i].xy * GlobalShadowTexelSize.xy;
                    #if defined(USE_GAUSS_DISTRIBUTION)
                        // Native Gaussian rotation; uniform PCF keeps its grid unrotated.
                        offset = float2(.986143 * offset.x - .165896 * offset.y,
                                        .165896 * offset.x + .986143 * offset.y);
                        weight = exp(-.5 * dot(offset, offset) /
                                     (GlobalShadowTexelSize.x * GlobalShadowTexelSize.x));
                    #endif
                    #endif
                    float2 tapUV = uv + offset;
                    float depth;
                    if (previous) depth = GlobalShadowSampled2.SampleLevel(sampler_GlobalShadowSampled2, tapUV, 0);
                    else depth = GlobalShadowSampled.SampleLevel(sampler_GlobalShadowSampled, tapUV, 0);
                    float outside = any(abs(tapUV * 2 - 1) > 1) ? 1 : 0;
                    float visibility = saturate((depth - GlobalShadowSettings.y < z ? 1 : 0) + outside);
                    sum += visibility * weight;
                    normalization += weight;
                }
                return sum / normalization;
            }

            float4 Frag(Varying i) : SV_Target
            {
                #if defined(LOWRES_DEPTH)
                    float depth = _LowResDepth.Load(int3(i.position.xy, 0));
                #else
                    float depth = _CameraDepthTexture.Load(int3(i.position.xy, 0));
                #endif
                float4 clip = float4(float3(i.uv.x, 1 - i.uv.y, 1 - depth) * 2 - 1, 1);
                float4 view = mul(unity_CameraInvProjection, clip);
                view /= view.w;
                view.z = -view.z;
                float3 world = mul(unity_CameraToWorld, view).xyz;
                float3 current = world;
                #if defined(ENABLE_PARALLAX)
                    current = FindOccluder(world, GlobalShadowL0.xyz, GlobalShadowWorldToScreen);
                #endif
                float visibility = Filter(current, GlobalShadowWorldToScreen, false);
                #if defined(DISTANT_SHADOWS_BLEND)
                    float3 previous = world;
                    #if defined(ENABLE_PARALLAX)
                        previous = FindOccluder(world, GlobalShadowL0Old.xyz, GlobalShadowWorldToScreen2);
                    #endif
                    visibility = lerp(Filter(previous, GlobalShadowWorldToScreen2, true), visibility, saturate(GlobalShadowProj.z));
                #endif
                return float4(visibility.xxx, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
