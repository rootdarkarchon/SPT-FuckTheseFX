Shader "Hidden/FuckTheseFX/WriteScreenAmbient"
{
 Properties {
  [HideInInspector] _MainTex ("Capture source", 2D) = "white" {}
  _StencilType ("_StencilType", Float) = 1
  _AmbientBlur ("_AmbientBlur", Float) = 1
  _ReflectionIntensity ("_ReflectionIntensity", Float) = 1
  _FTFX_CA_FoliageStrength ("Foliage AO strength", Range(0,1)) = .65
  _FTFX_CAAmount ("Diffuse occlusion strength", Range(0,1)) = 1
 }
 SubShader {
  CGINCLUDE
   #include "UnityCG.cginc"

   sampler2D _CameraDepthTexture, _StencilShadow;
   sampler2D _CameraGBufferTexture0, _CameraGBufferTexture1, _CameraGBufferTexture2;
   sampler2D _CameraMotionVectorsDepth;
   sampler2D _FTFX_CA_OriginalOcclusion, _FTFX_CA_ScreenAO;
   sampler2D _FTFX_CA_DiffuseOcclusion;
   float _FTFX_CA_DiffuseCaptured;
   float _FTFX_CA_MaterialCaptured, _FTFX_CA_SeparateAOAvailable, _FTFX_CA_FoliageStrength;
   float4 _FTFX_CA_AODecode, _FTFX_CA_AOFade;
   float SeparateVisibility(float2 uv) {
    float2 raw = tex2D(_FTFX_CA_ScreenAO,uv).rg;
    float fade = saturate(max(raw.y * _FTFX_CA_AODecode.z - _FTFX_CA_AOFade.x,0) * _FTFX_CA_AOFade.y);
    float power = lerp(_FTFX_CA_AODecode.y,_FTFX_CA_AOFade.w,fade);
    float intensity = lerp(_FTFX_CA_AODecode.x,_FTFX_CA_AOFade.z,fade);
    return saturate(1 + (pow(max(raw.x,0),power)-1)*intensity);
   }
   #ifdef FTFX_BENT_NORMALS
    #include "XeGTAO/BentNormals.hlsl"
    Texture2D<uint> _FTFX_CA_BentAO;
    float _FTFX_CA_BentAvailable;
    float4 _FTFX_CA_BentSize;
    float4x4 _FTFX_CA_BentToWorld;
   #endif
   #ifdef FTFX_BENT_DIAGNOSTICS
    RWTexture2D<float4> _FTFX_CA_DebugOutput : register(u1);
    float4 _FTFX_CA_DebugInfo; // view, gain, width, height
    float _FTFX_CA_DebugAvailable;
    #ifdef FTFX_FOLIAGE_CAPTURE
     RWTexture2D<float4> _FTFX_CA_UnoccludedOutput : register(u2);
     RWTexture2D<float4> _FTFX_CA_RemovedOutput : register(u3);
    #endif
   #endif
   samplerCUBE _MyGlobalReflectionProbe;
   #ifdef FTFX_SURFACE_CAPTURE
    RWTexture2D<float4> _FTFX_CA_SurfaceColour : register(u5);
    float _FTFX_CA_SurfaceCaptureEnabled;
    float4 _FTFX_CA_SurfaceCaptureSize;
   #endif
   float4 _SHAr, _SHAg, _SHAb, _SHBr, _SHBg, _SHBb, _SHC;
   float _ReflectionBottomShade, _AmbientBlur, _ReflectionIntensity;
   float TOD_Fogginess, _NightVisionOn, _FTFX_CAAmount;
   float3 _SunDirection;
   struct Varyings { float4 pos:SV_POSITION; float4 screen:TEXCOORD0; float3 ray:TEXCOORD1; };
   Varyings Vert(float4 vertex:POSITION) {
    Varyings o;
    float4 clip = mul(glstate_matrix_projection, float4(vertex.xy, -.1, vertex.w));
    o.pos = clip; o.pos.z = .1;
    o.screen = ComputeScreenPos(clip);
    o.ray = mul((float3x3)unity_CameraToWorld, float3(vertex.xy * 10, 1) * _ProjectionParams.z);
    return o;
   }
   float3 ToAmbientGamma(float3 value) {
    return max(1.055 * pow(max(value, 0), 1.0 / 2.4) - .055, 0);
   }
   float3 AmbientSH(float3 normal) {
    float4 n = float4(normal, 1);
    float4 quadratic = normal.yzzx * normal.xyzz;
    float3 result = float3(dot(_SHAr,n),dot(_SHAg,n),dot(_SHAb,n));
    result += float3(dot(_SHBr,quadratic),dot(_SHBg,quadratic),dot(_SHBb,quadratic));
    result += _SHC.rgb * (normal.x * normal.x - normal.y * normal.y);
    return ToAmbientGamma(result);
   }
   float4 Frag(Varyings input):SV_Target {
    float2 uv = input.screen.xy / input.screen.w;
    float depth = tex2D(_CameraDepthTexture,uv).r;
    float3 viewDirection = normalize(input.ray * Linear01Depth(depth));
    float3 normal = tex2D(_CameraGBufferTexture2,uv).xyz * 2 - 1;
    float3 unitNormal = normalize(normal);
    float4 spec = tex2D(_CameraGBufferTexture1,uv);
    float4 albedo = tex2D(_CameraGBufferTexture0,uv);
    #ifdef FTFX_SURFACE_CAPTURE
     if(_FTFX_CA_SurfaceCaptureEnabled>.5) {
      int2 pixel=(int2)input.pos.xy;
      if(all(pixel>=0) && all(pixel<(int2)_FTFX_CA_SurfaceCaptureSize.xy))
       _FTFX_CA_SurfaceColour[pixel]=float4(albedo.rgb,1);
     }
    #endif
    float visibility = 1 - tex2D(_StencilShadow,uv).a;

    // All reflection consumers, including native SSR, share GBuffer0's reflection visibility.
    // Keep native reflection composition intact; diffuse visibility is preserved separately below.
    float fresnel = pow(1 - abs(dot(unitNormal,-viewDirection)),5);
    float3 reflectance = lerp(spec.rgb, saturate(max(max(spec.r,spec.g),spec.b) + spec.a), fresnel);
    float roughness = 1 - spec.a;
    float3 reflectedRay = reflect(viewDirection,unitNormal);
    float lod = 6 * roughness * (1.7 - .7 * roughness);
    float3 reflection = texCUBElod(_MyGlobalReflectionProbe,float4(reflectedRay,lod)).rgb;
    reflection *= saturate(reflectedRay.y + _ReflectionBottomShade);
    reflection *= albedo.a * (1 - .28 * max(roughness * roughness,.002) * roughness);
    reflection *= reflectance * _ReflectionIntensity;
    reflection *= tex2D(_CameraMotionVectorsDepth,uv).r >= depth - .00001 ? .25 : 1;
    reflection *= _ReflectionIntensity == 1 && albedo.a < .5 ? 2 * albedo.a : 1;
    reflection *= visibility;
    float3 upward = ToAmbientGamma(float3(_SHAr.y+_SHAr.w,_SHAg.y+_SHAg.w,_SHAb.y+_SHAb.w)-_SHC.rgb);
    float3 directional = AmbientSH(normal);
    #ifdef FTFX_BENT_DIAGNOSTICS
     float3 originalDirectional = directional;
     float3 debugStatus = float3(1,0,0); // Bent shader keyword is disabled.
     #ifdef FTFX_BENT_NORMALS
      debugStatus = float3(1,0,1); // Bent keyword active, required camera inputs absent.
     #endif
    #endif
    #ifdef FTFX_BENT_NORMALS
    [branch] if (_FTFX_CA_BentAvailable > .5 && _FTFX_CA_MaterialCaptured > .5 && _FTFX_CA_SeparateAOAvailable > .5) {
     float original = tex2D(_FTFX_CA_OriginalOcclusion,uv).r;
     float2 aoSample = tex2D(_FTFX_CA_ScreenAO,uv).rg;
     float fade = saturate((aoSample.y * _FTFX_CA_AODecode.z - _FTFX_CA_AOFade.x) * _FTFX_CA_AOFade.y);
     float weight = saturate(_FTFX_CAAmount) * (1-fade) * (original < (.5/255.0) ? saturate(_FTFX_CA_FoliageStrength) : 1);
     int2 pixel = clamp((int2)(uv * _FTFX_CA_BentSize.xy),int2(0,0),(int2)_FTFX_CA_BentSize.xy-1);
     float3 bent = FTFXBentWorld(_FTFX_CA_BentAO.Load(int3(pixel,0)),_FTFX_CA_BentToWorld,normal);
     directional = lerp(directional,AmbientSH(bent),weight);
     #ifdef FTFX_BENT_DIAGNOSTICS
      debugStatus = 0;
     #endif
    }
    #endif
    // Keep EFT's authored upward-light blend; only the directional SH term bends.
    float3 diffuse = lerp(directional,upward,1-pow(visibility,_AmbientBlur)) * albedo.rgb * visibility;
    float luminance = dot(albedo.rgb,float3(.2126,.7152,.0722));
    float fill = luminance * luminance * (.05 + .05 * max(TOD_Fogginess*1.5,_NightVisionOn)) * (1-abs(_SunDirection.y));

    // Diffuse ambient and fill share the same visibility, strength and material compatibility.
    // Classify BEFORE screen AO, never by the final alpha: real screen occlusion can also be zero.
    // Native zero-alpha materials suppress reflections; that zero is not diffuse ambient visibility.
    float ao = 1;
    if (_FTFX_CA_MaterialCaptured > .5) {
     float original = tex2D(_FTFX_CA_OriginalOcclusion,uv).r;
     float visibilityAO = _FTFX_CA_DiffuseCaptured > .5 ? tex2D(_FTFX_CA_DiffuseOcclusion,uv).r : saturate(albedo.a);
     if (original < (.5 / 255.0))
      visibilityAO = _FTFX_CA_SeparateAOAvailable > .5 ? lerp(1,SeparateVisibility(uv),_FTFX_CA_FoliageStrength) : 1;
     ao = lerp(1,visibilityAO,_FTFX_CAAmount);
    }
    #ifdef FTFX_BENT_DIAGNOSTICS
    [branch] if (_FTFX_CA_DebugAvailable > .5) {
     float3 delta = directional - originalDirectional;
     int mode = (int)_FTFX_CA_DebugInfo.x;
     float transmission = pow(visibility,_AmbientBlur) * visibility;
     float3 display;
     if (mode <= 10) {
      if (mode == 10) delta *= transmission * albedo.rgb * ao;
      float magnitude = saturate(max(max(abs(delta.r),abs(delta.g)),abs(delta.b)) * _FTFX_CA_DebugInfo.y);
      display = any(debugStatus > 0) ? debugStatus : magnitude.xxx;
     } else {
      // These terms decompose the actual diffuse/fill output. Bent inputs are optional.
      float3 directionalLight = directional * transmission * albedo.rgb * ao;
      float3 upwardLight = upward * (1-pow(visibility,_AmbientBlur)) * visibility * albedo.rgb * ao;
      float3 fillLight = (fill * ao).xxx;
      if (mode == 14) display = transmission.xxx;
      else if (mode == 15) {
       float3 luma = float3(.2126,.7152,.0722);
       float3 shares = max(float3(dot(directionalLight,luma),dot(upwardLight,luma),dot(fillLight,luma)),0);
       float total = shares.x + shares.y + shares.z;
       display = total > 0 ? shares / total : 0;
      } else {
       float3 component = mode == 11 ? directionalLight : (mode == 12 ? upwardLight : fillLight);
       display = saturate(component * _FTFX_CA_DebugInfo.y);
      }
     }
     int2 debugPixel = clamp((int2)(uv * _FTFX_CA_DebugInfo.zw),int2(0,0),(int2)_FTFX_CA_DebugInfo.zw-1);
     _FTFX_CA_DebugOutput[debugPixel] = float4(display,1);
     #ifdef FTFX_FOLIAGE_CAPTURE
     if (mode == 18) {
      float original = tex2D(_FTFX_CA_OriginalOcclusion,uv).r;
      float separate = _FTFX_CA_SeparateAOAvailable > .5 ? SeparateVisibility(uv) : 1;
      float status = _FTFX_CA_MaterialCaptured < .5 ? 2 : (_FTFX_CA_SeparateAOAvailable < .5 ? 3 : 1);
      // Unclipped, ungained values from THIS fragment, before HDR/LDR target encoding.
      // The alpha sentinel distinguishes no ambient draw (0) from valid black lighting (1).
      _FTFX_CA_DebugOutput[debugPixel] = float4(ao,separate,original < (.5/255.0) ? 1 : 0,status);
      _FTFX_CA_UnoccludedOutput[debugPixel] = float4(diffuse+fill,1);
      _FTFX_CA_RemovedOutput[debugPixel] = float4((diffuse+fill)*(1-ao),1);
     }
     #endif
    }
    #endif
    float3 ambient = reflection + diffuse * ao + fill * ao;
    #ifdef UNITY_HDR_ON
     return float4(ambient,0);
    #else
     return exp2(-float4(ambient,0));
    #endif
   }
  ENDCG
  Pass {
   Name "Ambient"
   ZTest Always ZWrite Off Cull Off
   Blend [_SrcBlend] [_DstBlend]
   CGPROGRAM
   #pragma target 5.0
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile __ UNITY_HDR_ON
   #pragma multi_compile_local __ FTFX_BENT_NORMALS
   #pragma multi_compile_local __ FTFX_BENT_DIAGNOSTICS
   #pragma multi_compile_local __ FTFX_FOLIAGE_CAPTURE
   #pragma multi_compile_local __ FTFX_SURFACE_CAPTURE
   ENDCG
  }
  Pass {
   Name "CaptureMaterialOcclusion"
   ZTest Always ZWrite Off Cull Off Blend Off
   CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment Capture
   sampler2D _MainTex;
   float4 Capture(v2f_img input):SV_Target { return tex2D(_MainTex,input.uv).aaaa; }
   ENDCG
  }
 }
 Fallback Off
}
