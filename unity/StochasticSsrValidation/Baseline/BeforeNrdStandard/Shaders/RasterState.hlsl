// Row-major values in a camera-owned buffer refreshed after PP configures jitter.
StructuredBuffer<float4> _SsrRasterState;
float _SsrLateRaster;
float4x4 SsrRasterMatrix(float4x4 recorded,int first)
{
#if defined(FTFX_COMPUTE_RASTER) && !defined(FTFX_LATE_RASTER)
 return recorded;
#else
 if(_SsrLateRaster<.5)return recorded;
 return float4x4(_SsrRasterState[first],_SsrRasterState[first+1],_SsrRasterState[first+2],_SsrRasterState[first+3]);
#endif
}
bool SsrRasterHistory() {return _SsrLateRaster<.5 || _SsrRasterState[28].z>.5;}
float2 SsrHistoryUV(float2 uv,float2 motion)
{
 // Built-in motion vectors use non-jittered VP. Our history is raster-aligned.
 return uv-motion+(_SsrLateRaster>.5?_SsrRasterState[28].xy:float2(0,0));
}
