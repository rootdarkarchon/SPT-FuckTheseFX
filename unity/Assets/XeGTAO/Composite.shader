Shader "Hidden/FuckTheseFX/Occlusion"
{
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  HLSLINCLUDE
  #pragma target 5.0
  #pragma only_renderers d3d11
  #include "UnityCG.cginc"
  #include "BentNormals.hlsl"
  float _BentNormals;
  float4x4 _BentToWorld;
  Texture2D<float4> _CameraGBufferTexture2;
  Texture2D<float4> _CameraGBufferTexture1;
  sampler2D _CameraDepthTexture, _CameraMotionVectorsDepth;
  float _ReflectionOcclusion;
  Texture2D<uint> _XeAO;
  Texture2D<float> _XeDepth;
  Texture2D<float> _ActorMask;
  Texture2D<float2> _PrimaryAO;
  Texture2D<float4> _Emission;
  float4 _Size; // width, height, far / 65504, visibility scale
  float4 _AOFade; // start depth, reciprocal fade range, final intensity, final power
  float4 _ActorSettings; // strength, response, cutoff, unused
  float4 _Debug; // view, start, range, unused
  struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
  Varyings Vert(appdata_img v)
  {
   Varyings o; o.position=float4(v.vertex.xy*2-1,0,1); o.uv=v.texcoord;
   #if UNITY_UV_STARTS_AT_TOP
    o.position.y=-o.position.y;
   #endif
   return o;
  }
  int2 Pixel(Varyings i) { return min((int2)(i.uv*_Size.xy),(int2)_Size.xy-1); }
  float2 Resolve(Varyings i):SV_Target
  {
   int2 p=Pixel(i);
   uint packed = _XeAO.Load(int3(p,0));
   uint visibility = _BentNormals > .5 ? packed >> 24 : packed;
   return float2(saturate(visibility*(_Size.w/255)),min(_XeDepth.Load(int3(p,0))/_Size.z,65504));
  }
  float Primary(float2 ao)
  {
   float fade=saturate((ao.y*_Size.z-_AOFade.x)*_AOFade.y);
   return saturate(1+(pow(max(ao.x,0),lerp(.8,_AOFade.w,fade))-1)*lerp(2,_AOFade.z,fade));
  }
  struct Outputs { float4 albedo:SV_Target0; float4 emission:SV_Target1; };
  float ReflectionVisibility(Varyings i,float diffuse)
  {
   if(_ReflectionOcclusion < .5) return diffuse;
   // Match CustomAmbient's native reduced-reflection path for instanced vegetation.
   // Raising its shared alpha also raises SSR's probe subtraction, which lacks that reduction.
   // Use the same UV sampling and raw-depth tolerance as CustomAmbient, not an AO/material guess.
   float depth=tex2D(_CameraDepthTexture,i.uv).r;
   if(tex2D(_CameraMotionVectorsDepth,i.uv).r >= depth-.00001) return diffuse;
   int2 p=Pixel(i);
   float3 normal=_CameraGBufferTexture2.Load(int3(p,0)).xyz*2-1;
   float len=dot(normal,normal);
   if(!all(isfinite(normal)) || len<.25 || len>1.5) return diffuse;
   float smoothness=saturate(_CameraGBufferTexture1.Load(int3(p,0)).a);
   // Partial screen-AO relief for glossy reflections, independent of viewing angle.
   // Retain at least half the screen occlusion; authored material AO is multiplied separately.
   return lerp(diffuse,1,.5*smoothness*smoothness);
  }
  Outputs Deferred(Varyings i)
  {
   float v=Primary(_PrimaryAO.Load(int3(Pixel(i),0)));
   Outputs o; o.albedo=ReflectionVisibility(i,v); o.emission=float4(v,v,v,1); return o;
  }
  Outputs DeferredLog(Varyings i)
  {
   float v=Primary(_PrimaryAO.Load(int3(Pixel(i),0)));
   Outputs o; o.albedo=ReflectionVisibility(i,v);
   // Emission is encoded as exp2(-radiance) in the LDR deferred buffer.
   o.emission=float4(pow(max(_Emission.Load(int3(Pixel(i),0)).rgb,1e-20),v),1);
   return o;
  }
  float4 Mask(Varyings i):SV_Target { return 1; }
  float4 Actor(Varyings i):SV_Target
  {
   int2 p=Pixel(i);
   if(_ActorMask.Load(int3(p,0)) < .5) return 1;
   // Preserve the old RGHalf resolve rounding without allocating/writing that texture.
   float ao=f16tof32(f32tof16(saturate(_XeAO.Load(int3(p,0))/255.0)));
   float depth=f16tof32(f32tof16(min(_XeDepth.Load(int3(p,0))/_Size.z,65504)))*_Size.z;
   float fade=1-smoothstep(_ActorSettings.z*.8,_ActorSettings.z,depth);
   float obsc=saturate(pow(saturate(1-ao),_ActorSettings.y)*_ActorSettings.x);
   float v=lerp(1,pow(max(1-obsc,0),1.0/2.2),fade);
   return float4(v,v,v,1);
  }
  float4 DebugAO(Varyings i):SV_Target
  {
   if (_Debug.x == 8)
   {
    if (_BentNormals < .5) return float4(.5,.5,.5,1);
    int2 p=Pixel(i);
    float3 normal=_CameraGBufferTexture2.Load(int3(p,0)).xyz*2-1;
    normal=dot(normal,normal)>.01 ? normalize(normal) : float3(0,1,0);
    return float4(FTFXBentWorld(_XeAO.Load(int3(p,0)),_BentToWorld,normal)*.5+.5,1);
   }
   float2 ao=_PrimaryAO.Load(int3(Pixel(i),0));
   float v= _Debug.x==1 ? saturate((ao.y*_Size.z-_Debug.y)/_Debug.z) : _Debug.x==6 ? Primary(ao) : ao.x;
   return float4(v,v,v,1);
  }
  Texture2D<float4> _FTFXMaterialInput;
  float4 MaterialAlpha(Varyings i):SV_Target { return _FTFXMaterialInput.Load(int3(Pixel(i),0)).aaaa; }
  float4 DiffuseVisibility(Varyings i):SV_Target { return MaterialAlpha(i)*Primary(_PrimaryAO.Load(int3(Pixel(i),0))); }
  Texture2D<float> _LiveAlpha;
  Texture2D<float4> _LiveA, _LiveB;
  float4 _LiveSettings; // extra strength, divider (-1 full frame), far plane, unused
  float4 _LiveDisplay; // gain, absolute difference, mask, unused
  bool LiveEligible(int2 p)
  {
   float depth=_PrimaryAO.Load(int3(p,0)).y*_Size.z;
   return _LiveAlpha.Load(int3(p,0)) < .5/255.0 && _ActorMask.Load(int3(p,0)) < .5 && depth>0 && depth<_LiveSettings.z*.999;
  }
  float4 LiveComposite(Varyings i):SV_Target
  {
   int2 p=Pixel(i);
   float v=LiveEligible(p) && i.uv.x>=_LiveSettings.y ? lerp(1,Primary(_PrimaryAO.Load(int3(p,0))),saturate(_LiveSettings.x)) : 1;
   return float4(v,v,v,1);
  }
  float4 LiveDisplay(Varyings i):SV_Target
  {
   int2 p=Pixel(i);
   if(_LiveDisplay.z>.5) return float4(LiveEligible(p)?float3(1,1,1):float3(0,0,0),1);
   float3 a=_LiveA.Load(int3(p,0)).rgb;
   float3 b=_LiveB.Load(int3(p,0)).rgb;
   return float4((_LiveDisplay.y>.5?abs(a-b):a)*_LiveDisplay.x,1);
  }
  ENDHLSL
  Pass // 0
  {
   Name "ResolvePrimary"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Resolve
   ENDHLSL
  }
  Pass // 1
  {
   Name "DeferredHDR"
   Blend 0 DstColor Zero
   Blend 1 DstColor Zero
   ColorMask A 0
   ColorMask RGB 1
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Deferred
   ENDHLSL
  }
  Pass // 2
  {
   Name "DeferredLDR"
   Blend 0 DstColor Zero
   Blend 1 One Zero
   ColorMask A 0
   ColorMask RGB 1
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DeferredLog
   ENDHLSL
  }
  Pass // 3
  {
   Name "ActorStencil"
   Stencil { Ref 0 ReadMask 3 WriteMask 0 Comp NotEqual Pass Keep Fail Keep ZFail Keep }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Mask
   ENDHLSL
  }
  Pass // 4
  {
   Name "ActorComposite"
   Blend DstColor Zero
   ColorMask RGB
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Actor
   ENDHLSL
  }
  Pass // 5
  {
   Name "PrimaryDebug"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DebugAO
   ENDHLSL
  }
  Pass // 6
  {
   Name "ActorDebug"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Actor
   ENDHLSL
  }
  Pass // 7
  {
   Name "MaterialOcclusion"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment MaterialAlpha
   ENDHLSL
  }
  Pass // 8
  {
   Name "FoliageLiveComposite"
   Blend DstColor Zero
   ColorMask RGB
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment LiveComposite
   ENDHLSL
  }
  Pass // 9
  {
   Name "FoliageLiveDisplay"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment LiveDisplay
   ENDHLSL
  }
  Pass // 10
  {
   Name "PreserveDiffuseOcclusion"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DiffuseVisibility
   ENDHLSL
  }
 }
 Fallback Off
}
