#ifndef FTFX_CONTACT_GI
#define FTFX_CONTACT_GI
#include "UnityCG.cginc"
#define NRD_NORMAL_ENCODING 2
#define NRD_ROUGHNESS_ENCODING 1
#include "NRD/NRD.hlsli"
StructuredBuffer<float4> _GIRaster;
float4 _GISize, _GISettings, _GILimits; // full/low dimensions; radius,strength,tolerance,cutoff; donor cap,bounce fraction,gamma,far
float4x4 RasterMatrix(int start) { return float4x4(_GIRaster[start],_GIRaster[start+1],_GIRaster[start+2],_GIRaster[start+3]); }
float3 GIPosition(float2 uv,float depth) { float4 h=mul(RasterMatrix(4),float4(uv*2-1,.5,1)); return h.xyz*(depth/-h.z); }
float2 GIProject(float3 p) { float4 h=mul(RasterMatrix(0),float4(p,1));return h.xy/h.w*.5+.5; }
float3 GIDecode(float3 c) { return _GILimits.z>0 ? float3(GammaToLinearSpaceExact(c.r),GammaToLinearSpaceExact(c.g),GammaToLinearSpaceExact(c.b)) : c; }
float3 GIEncode(float3 c) {return float3(LinearToGammaSpaceExact(c.r),LinearToGammaSpaceExact(c.g),LinearToGammaSpaceExact(c.b));}
static const float3 GILuma=float3(.2126,.7152,.0722);
float3 GILimit(float3 c,float cap) {if(!all(isfinite(c)))return 0; c=max(0,c);return c*min(1,cap/max(dot(c,GILuma),1e-8));}
float GITolerance(float z) {return max(_GISettings.z,z*.0005);}
int2 GIFullPixel(float2 uv) { return clamp(int2(uv*_GISize.xy),0,int2(_GISize.xy)-1); }
Texture2D<float> _GIDepth,_GIActors;
Texture2D<float4> _CameraGBufferTexture0,_CameraGBufferTexture1,_CameraGBufferTexture2;
float3 GIShading(int2 p) {float3 n=_CameraGBufferTexture2.Load(int3(p,0)).xyz*2-1;return all(isfinite(n)) && dot(n,n)>.25 && dot(n,n)<1.5?normalize(n):0;}
float3 GIGeometric(float2 uv,float z,float3 fallback) {
 int2 p=GIFullPixel(uv), size=int2(_GISize.xy)-1;
 int2 l=max(p-int2(1,0),0),r=min(p+int2(1,0),size),u=max(p-int2(0,1),0),d=min(p+int2(0,1),size);
 float3 c=GIPosition((p+.5)/_GISize.xy,z);
 float3 vl=GIPosition((l+.5)/_GISize.xy,_GIDepth.Load(int3(l,0))),vr=GIPosition((r+.5)/_GISize.xy,_GIDepth.Load(int3(r,0)));
 float3 vu=GIPosition((u+.5)/_GISize.xy,_GIDepth.Load(int3(u,0))),vd=GIPosition((d+.5)/_GISize.xy,_GIDepth.Load(int3(d,0)));
 float3 dx=abs(vl.z-c.z)<abs(vr.z-c.z)?c-vl:vr-c,dy=abs(vu.z-c.z)<abs(vd.z-c.z)?c-vu:vd-c;
 float3 n=cross(dx,dy);if(dot(n,n)<1e-16 || !all(isfinite(n)))return fallback;
 n=normalize(mul((float3x3)RasterMatrix(8),n));return dot(n,fallback)<0?-n:n;
}
bool GIEligible(float2 uv,float z) {
 int2 p=GIFullPixel(uv);
 float3 a=GIDecode(_CameraGBufferTexture0.Load(int3(p,0)).rgb);
 return z>0 && z<min(_GILimits.w*.999,_GISettings.w) && _GIActors.Load(int3(p,0))<.5 && any(a>0) && any(GIShading(p)!=0);
}
#endif
