Shader "Hidden/FTFX/WaterParity"
{
    // Match native material defaults. Camera/TOD/weather/SH inputs stay globals.
    Properties
    {
        [HideInInspector] _FTFXWaterDiagnostic ("Replacement diagnostic", Float) = 0
        [HideInInspector] _FTFXWaterNormalControls ("Size, strength, detail, speed", Vector) = (1,1,1,1)
        [HideInInspector] _FTFXWaterAnimationTime ("Integrated normal animation time", Float) = 0
        _RippleTex ("_RippleTex", 2D) = "black" {}
        _FoamTex ("_FoamTex", 2D) = "white" {}
        _BumpMap ("_BumpMap", 2D) = "white" {}
        _BumpMapDetails ("_BumpMapDetails", 2D) = "white" {}
        _DepthVals ("_DepthVals", Vector) = (1,1,1,1)
        _Bumpiness ("_Bumpiness", Float) = 1
        _TexturesScales ("_TexturesScales", Vector) = (1,1,1,1)
        _TexturesTimeScales ("_TexturesTimeScales", Vector) = (1,1,1,1)
        _DepthColorDeep ("_DepthColorDeep", Color) = (1,1,1,1)
        _DepthColorShallow ("_DepthColorShallow", Color) = (1,1,1,1)
        _ValsNormA ("_ValsNormA", Vector) = (0,0,0,0)
        _ValsNormB ("_ValsNormB", Vector) = (0,0,0,0)
        _ValsNormD ("_ValsNormD", Vector) = (0,0,0,0)
        _ValsFoamA ("_ValsFoamA", Vector) = (0,0,0,0)
        _ValsFoamB ("_ValsFoamB", Vector) = (0,0,0,0)
        _FoamVals ("_FoamVals", Vector) = (1,1,1,1)
        _FresnelVals ("_FresnelVals", Vector) = (1,1,1,1)
        _FoamColor ("_FoamColor", Color) = (1,1,1,1)
        _RippleScale ("_RippleScale", Float) = 1
        _RippleBumpness ("_RippleBumpness", Float) = 1
        _AdditionalCubemapReflection ("_AdditionalCubemapReflection", Float) = 1
        _ReflectionColor ("_ReflectionColor", Color) = (1,1,1,1)
        _DiffuseColor ("_DiffuseColor", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            Name "NativeGBufferParity"
            Cull Back ZTest LEqual ZWrite On
            Blend 0 SrcAlpha OneMinusSrcAlpha
            Blend 1 SrcAlpha OneMinusSrcAlpha
            Blend 2 SrcAlpha OneMinusSrcAlpha
            Blend 3 SrcAlpha OneMinusSrcAlpha
            Blend 4 One Zero
            HLSLPROGRAM
            #pragma target 4.0
            #pragma only_renderers d3d11
            #pragma vertex WaterVertex
            #pragma fragment WaterFragmentWithDiagnostic
            #pragma multi_compile __ Rain
            #pragma multi_compile __ WINTER_SNOW
            #pragma multi_compile __ UNITY_HDR_ON
            #define FTFX_WATER_MULTISCALE 1
            #include "WaterParity.cginc"
            float _FTFXWaterDiagnostic;
            WaterTargets WaterFragmentWithDiagnostic(WaterVaryings input)
            {
                WaterTargets result = WaterFragment(input);
                if (_FTFXWaterDiagnostic > 0.5)
                {
                    // Emit the marker from the replacement's real surface draw.
                    // Retain native geometry, depth and snow output.
                    result.diffuse = float4(0,0,0,1);
                    result.specular = float4(0,0,0,1);
                    result.normal.a = 1;
                    #if defined(UNITY_HDR_ON)
                    result.emission = float4(2,0,2,1);
                    #else
                    result.emission = float4(exp2(-float3(2,0,2)),1);
                    #endif
                }
                return result;
            }
            ENDHLSL
        }
        Pass
        {
            Name "NativeDepthParity"
            Cull Back ZTest LEqual ZWrite On
            ColorMask A Blend Zero One
            HLSLPROGRAM
            #pragma target 4.0
            #pragma only_renderers d3d11
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #include "UnityCG.cginc"
            float4 DepthVertex(float4 vertex : POSITION) : SV_POSITION
            { return mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, float4(vertex.xyz,1))); }
            float4 DepthFragment() : SV_Target { return 1; }
            ENDHLSL
        }
    }
    Fallback Off
}
