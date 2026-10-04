Shader "Hidden/FuckTheseFX/ColorBleed" {
 SubShader {
  Cull Off ZWrite Off ZTest Always
  HLSLINCLUDE
  #pragma target 5.0
  #pragma only_renderers d3d11
  #include "ContactGICommon.hlsl"
  Texture2D<float4> _GIOutput,_GIRaw,_GINormal,_GITrace,_GIValidation,_GISource,_GIGeometry;
  Texture2D<float> _GIViewZ;
  Texture2D<float2> _GIVisibility;
  float _GIDebugGain,_GIReconstructionCutoff;int _GIDebugView;
  struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
  V Vert(appdata_img v) {V o;o.p=float4(v.vertex.xy*2-1,0,1);o.uv=v.texcoord;
   #if UNITY_UV_STARTS_AT_TOP
   o.p.y=-o.p.y;
   #endif
   return o;}
  float4 ReconstructionTap(int2 q) {
   if(any(q<0)||any(q>=int2(_GISize.zw)))return float4(0,0,0,1e7);
   float z=_GIViewZ.Load(int3(q,0));if(z>=1e6)return float4(0,0,0,z);
   return float4(max(0,REBLUR_BackEnd_UnpackRadianceAndNormHitDist(_GIOutput.Load(int3(q,0))).rgb),z);
  }
  float3 Reconstruct(float2 uv,float z,float3 gn,int2 base,float2 f,float4 taps[4]) {
   float3 sum=0;float total=0;float3 p=GIPosition(uv,z);
   [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++) {
    int index=y*2+x;int2 q=base+int2(x,y);float dz=taps[index].w;if(dz>=1e6)continue;
    float2 quv=(q+.5)/_GISize.zw;float3 ng=_GIGeometry.Load(int3(q,0)).xyz,v=GIPosition(quv,dz)-p;
    float3 wv=mul((float3x3)RasterMatrix(8),v);
    float error=max(abs(dot(wv,gn)),abs(dot(wv,ng))),tol=GITolerance(z);
    float weight=(x?f.x:1-f.x)*(y?f.y:1-f.y)*smoothstep(.8,.95,dot(ng,gn))*(1-smoothstep(tol*.5,tol,error));
    sum+=taps[index].rgb*weight;total+=weight;
   }
   return total>1e-6?sum/total:0;
  }
  float3 Addition(float2 uv) {
   int2 p=GIFullPixel(uv);float z=_GIDepth.Load(int3(p,0));
   float3 albedo=GIDecode(_CameraGBufferTexture0.Load(int3(p,0)).rgb);
   float visibility=saturate(_GIVisibility.Load(int3(p,0)).r);
   if(!GIEligible(uv,z)||_GISettings.y<=0||visibility<=0||!any(albedo>0))return 0;
   float3 spec=GIDecode(_CameraGBufferTexture1.Load(int3(p,0)).rgb);
   float3 reflectance=albedo*(1-saturate(max(spec.r,max(spec.g,spec.b))));if(!any(reflectance>0))return 0;
   float fade=1-smoothstep(_GISettings.w*.6,_GISettings.w,z);
   float edge=saturate(min(min(uv.x,1-uv.x),min(uv.y,1-uv.y))*20),display=visibility*fade*edge;
   float2 xy=uv*_GISize.zw-.5;int2 base=int2(floor(xy));float2 f=frac(xy);
   float4 taps[4];float upper=0;
   [unroll]for(int i=0;i<4;i++) {
    taps[i]=ReconstructionTap(base+int2(i&1,i>>1));
    upper=max(upper,dot(taps[i].rgb*reflectance*_GISettings.y,GILuma));
   }
   // Nonnegative normalized reconstruction is a convex combination of these
   // taps. This bound includes receiver response, cap, visibility and fades.
   upper=min(upper,_GILimits.x*_GILimits.y)*display;
   [branch]if(upper<=_GIReconstructionCutoff)return 0;
   float3 n=GIGeometric(uv,z,GIShading(p)),incident=Reconstruct(uv,z,n,base,f,taps);
   float3 addition=GILimit(incident*reflectance*_GISettings.y,_GILimits.x*_GILimits.y)*visibility*fade*edge;
   if(_GIReconstructionCutoff>0) {
    float luminance=dot(addition,GILuma);
    addition*=smoothstep(_GIReconstructionCutoff,_GIReconstructionCutoff*2,luminance);
   }
   return addition;
  }
  float4 Composite(V v):SV_Target {
   float3 addition=Addition(v.uv);
   if(!any(addition>0))return 0;
   if(_GILimits.z>0){float3 original=_GISource.Load(int3(GIFullPixel(v.uv),0)).rgb;addition=GIEncode(GIDecode(original)+addition)-original;}
   return float4(addition,0);
  }
  float4 Diagnostic(V v):SV_Target {
   int2 q=clamp(int2(v.uv*_GISize.zw),0,int2(_GISize.zw)-1);
   if(_GIDebugView==17)return _GIValidation.Load(int3(q,0));
   if(_GIDebugView==20)return float4(REBLUR_BackEnd_UnpackRadianceAndNormHitDist(_GIRaw.Load(int3(q,0))).rgb*_GIDebugGain,1);
   if(_GIDebugView==21)return float4(REBLUR_BackEnd_UnpackRadianceAndNormHitDist(_GIOutput.Load(int3(q,0))).rgb*_GIDebugGain,1);
   if(_GIDebugView==22){float dist=_GIRaw.Load(int3(q,0)).a;return float4(dist.xxx,1);}
   if(_GIDebugView==23){uint flags=(uint)_GITrace.Load(int3(q,0)).x;return float4(float3((flags&16)?1:((flags&8)?1:0),(flags&1)?1:((flags&2)?1:0),(flags&4)?1:((flags&32)?1:0)),1);}
   return float4(Addition(v.uv)*_GIDebugGain,1);
  }
  ENDHLSL
  Pass {
   Blend One One
   ColorMask RGB
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Composite
   ENDHLSL
  }
  Pass {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Diagnostic
   ENDHLSL
  }
 }
}
