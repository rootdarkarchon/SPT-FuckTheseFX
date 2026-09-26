Shader "Hidden/FuckTheseFX/ColorBleed"
{
 SubShader
 {
  Cull Off ZWrite Off ZTest Always
  HLSLINCLUDE
  #pragma target 5.0
  #pragma only_renderers d3d11
  #include "UnityCG.cginc"
  #include "ColorBleedSampling.hlsl"
  Texture2D<float4> _FTFX_BleedScene;
  Texture2D<float> _FTFX_BleedCurrentDepth;
  Texture2D<float4> _CameraGBufferTexture0, _CameraGBufferTexture1, _CameraGBufferTexture2;
  Texture2D<float> _CBDepth, _CBActors;
  Texture2D<float2> _CBVisibility;
  Texture2D<float4> _CBSource, _CBGuide, _CBBounce, _CBFilteredBounce;
  Texture2D<float4> _CBSpatialInput, _CBHistoryBounce, _CBHistoryState, _CBHistorySource;
  Texture2D<float2> _CBHistoryMoments, _CBMoments;
  Texture2D<float4> _CBCurrentState;
  int _CBUseStatistics;
  float4x4 _CBViewToWorld, _CBPreviousView;
  float4 _CBPreviousNDC, _CBTemporal; // valid, max history frames
  int _CBSampleFrame, _CBSampleTemporal;
  float4 _CBSize; // full width/height, low width/height
  float4 _CBNDCToView;
  float4x4 _CBWorldToView;
  float4 _CBSettings; // radius, strength, depth tolerance, distance cutoff
  float4 _CBLimits; // donor luminance cap, bounce fraction, divisor, samples
  float4 _CBDepthUnpack; // mul, add, far, unused
  float _CBDebugGain;
  float _CBSpatialStep;
  float _CBGamma;
  Texture2D<float4> _CBGammaScene;
  float3 DecodeColor(float3 c)
  {
   return _CBGamma>.5 ? float3(GammaToLinearSpaceExact(c.r),GammaToLinearSpaceExact(c.g),GammaToLinearSpaceExact(c.b)) : c;
  }
  float3 EncodeColor(float3 c)
  {
   return float3(LinearToGammaSpaceExact(c.r),LinearToGammaSpaceExact(c.g),LinearToGammaSpaceExact(c.b));
  }
  static const float3 Luma = float3(.2126,.7152,.0722);
  struct Varyings { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
  Varyings Vert(appdata_img v)
  {
   Varyings o; o.position=float4(v.vertex.xy*2-1,0,1); o.uv=v.texcoord;
   #if UNITY_UV_STARTS_AT_TOP
    o.position.y=-o.position.y;
   #endif
   return o;
  }
  int2 FullPixel(float2 uv) { return clamp((int2)(uv*_CBSize.xy),int2(0,0),(int2)_CBSize.xy-1); }
  float2 UV(int2 p) { return (p+.5)/_CBSize.xy; }
  float3 Position(int2 p, float z) { return float3((UV(p)*_CBNDCToView.xy+_CBNDCToView.zw)*z,z); }
  float2 SignNZ(float2 v) { return float2(v.x>=0?1:-1,v.y>=0?1:-1); }
  float2 PackNormal(float3 n)
  {
   n/=abs(n.x)+abs(n.y)+abs(n.z);
   return (n.z>=0?n.xy:(1-abs(n.yx))*SignNZ(n.xy))*.5+.5;
  }
  float3 UnpackNormal(float2 p)
  {
   p=p*2-1; float3 n=float3(p,1-abs(p.x)-abs(p.y));
   n.xy += SignNZ(n.xy)*-saturate(-n.z);
   return normalize(n);
  }
  float3 Normal(int2 p)
  {
   float3 n=_CameraGBufferTexture2.Load(int3(p,0)).xyz*2-1;
   float len=dot(n,n);
   if(!all(isfinite(n)) || len<.25 || len>1.5) return 0;
   return normalize(mul((float3x3)_CBWorldToView,n)*float3(1,-1,-1));
  }
  float Tolerance(float z) { return max(_CBSettings.z,z*.0005); }
  float RoundedPlaneError(float3 p,float3 q,float3 n)
  {
   // Each RGBAHalf depth can lose up to one relative half-precision ULP.
   // Project that uncertainty onto the tested plane, rather than widening every
   // surface/depth test by a fixed world-space distance.
   float rounding=(abs(dot(p,n))+abs(dot(q,n)))*(1.0/1024.0);
   return max(0,abs(dot(q-p,n))-rounding);
  }
  float SurfaceFootprint(float3 p,float3 n,float4 ndc,float step)
  {
   float pixels=p.z*max(abs(ndc.x)/_CBSize.x,abs(ndc.y)/_CBSize.y)*_CBLimits.z*3*step;
   return max(pixels/max(.025,abs(dot(n,normalize(p)))),_CBSettings.z*2);
  }
  float GeometryConfidence(float3 p,float3 q,float3 n,float3 tn,float distanceLimit,float normalLimit)
  {
   float alignment=dot(n,tn);
   float error=max(RoundedPlaneError(p,q,n),RoundedPlaneError(p,q,tn));
   float distance=length(q-p),tolerance=Tolerance(p.z);
   if(alignment<=normalLimit || error>=tolerance || distance>=distanceLimit) return 0;
   return smoothstep(normalLimit,min(1,normalLimit+.08),alignment)*
    (1-smoothstep(tolerance*.5,tolerance,error))*(1-smoothstep(distanceLimit*.75,distanceLimit,distance));
  }
  float3 TemporalNormal(int2 pixel,float z,float3 fallback);
  bool ValidDepth(float z) { return z>0 && z<_CBDepthUnpack.z*.999 && z<_CBSettings.w+_CBSettings.x; }
  bool CurrentSurface(int2 p, float z)
  {
   float current=_CBDepthUnpack.x/(_CBDepthUnpack.y-_FTFX_BleedCurrentDepth.Load(int3(p,0)));
   return abs(current-z)<Tolerance(z);
  }
  float3 Limited(float3 color)
  {
   // No channel-wise clipping/desaturation and no donor albedo multiplication.
   if(!all(isfinite(color))) return 0;
   color=max(color,0);
   color=DecodeColor(color);
   return color*min(1,_CBLimits.x/max(dot(color,Luma),1e-6));
  }
  float DonorConfidence(int2 p)
  {
   float4 spec=_CameraGBufferTexture1.Load(int3(p,0));
   spec.rgb=DecodeColor(saturate(spec.rgb));
   return (1-smoothstep(.65,.95,spec.a))*(1-saturate(max(spec.r,max(spec.g,spec.b))));
  }
  int2 Representative(int2 low, float4 guide)
  {
   uint offset=(uint)round(guide.a*255)-1;
   return low*(int)_CBLimits.z+int2(offset&3,offset>>2);
  }
  bool InputDimensionsMatch()
  {
   uint sceneW,sceneH,depthW,depthH,gbW,gbH,currentW,currentH;
   _FTFX_BleedScene.GetDimensions(sceneW,sceneH); _CBDepth.GetDimensions(depthW,depthH);
   _CameraGBufferTexture0.GetDimensions(gbW,gbH);
   _FTFX_BleedCurrentDepth.GetDimensions(currentW,currentH);
   return sceneW==(uint)_CBSize.x && sceneH==(uint)_CBSize.y && depthW==sceneW && depthH==sceneH && gbW==sceneW && gbH==sceneH && currentW==sceneW && currentH==sceneH;
  }
  struct Captured { float4 source:SV_Target0; float4 guide:SV_Target1; };
  Captured Capture(Varyings i)
  {
   Captured o; o.source=0; o.guide=0;
   if(!InputDimensionsMatch()) return o;
   int2 low=min((int2)(i.uv*_CBSize.zw),(int2)_CBSize.zw-1);
   int div=(int)_CBLimits.z; int2 origin=low*div, chosen=origin;
   float z=_CBDepthUnpack.z;
   // Select an actual nearest surface, including actors. Do not see through an actor
   // by choosing a more convenient background sample from its downsample block.
   [loop] for(int y=0;y<div;y++) [loop] for(int x=0;x<div;x++)
   {
    int2 p=origin+int2(x,y); if(any(p>=(int2)_CBSize.xy)) continue;
    float d=_CBDepth.Load(int3(p,0)); if(d>0 && d<z) { z=d; chosen=p; }
   }
   if(!ValidDepth(z) || _CBActors.Load(int3(chosen,0))>.5 || !CurrentSurface(chosen,z)) return o;
   float3 shadingNormal=Normal(chosen); if(dot(shadingNormal,shadingNormal)<.5) return o;
   float3 n=TemporalNormal(chosen,z,shadingNormal);
   float3 center=Position(chosen,z), color=0; float count=0;
   [loop] for(int yy=0;yy<div;yy++) [loop] for(int xx=0;xx<div;xx++)
   {
    int2 p=origin+int2(xx,yy); if(any(p>=(int2)_CBSize.xy)) continue;
    float d=_CBDepth.Load(int3(p,0)); float3 sampleNormal=Normal(p);
    if(!ValidDepth(d) || _CBActors.Load(int3(p,0))>.5 || !CurrentSurface(p,d) || dot(sampleNormal,sampleNormal)<.5) continue;
    if(abs(dot(Position(p,d)-center,n))>Tolerance(z)) continue;
    color+=Limited(_FTFX_BleedScene.Load(int3(p,0)).rgb)*DonorConfidence(p); count++;
   }
   if(count==0) return o;
   int2 offset=chosen-origin;
   o.source=float4(color/count,z);
   o.guide=float4(PackNormal(n),saturate(_CBVisibility.Load(int3(chosen,0)).r),(1+offset.x+4*offset.y)/255.0);
   return o;
  }
  bool SegmentVisible(float3 p,float3 q,float3 np,float3 nq)
  {
   float3 mid=(p+q)*.5+(np+nq)*min(_CBSettings.z*.25,.005);
   float2 uv=(mid.xy/mid.z-_CBNDCToView.zw)/_CBNDCToView.xy;
   if(any(uv<=0) || any(uv>=1)) return false;
   float z=_CBDepth.Load(int3(FullPixel(uv),0));
   return z>=mid.z-Tolerance(mid.z);
  }
  float4 Gather(Varyings i):SV_Target
  {
   int2 low=min((int2)(i.uv*_CBSize.zw),(int2)_CBSize.zw-1);
   float4 guide=_CBGuide.Load(int3(low,0)), source=_CBSource.Load(int3(low,0));
   if(guide.a==0 || source.a>=_CBSettings.w) return 0;
   int2 receiver=Representative(low,guide);
   float3 p=Position(receiver,source.a), np=Normal(receiver);
   if(dot(np,np)<.5) return 0;
   // The frame counter advances only on actual camera renders. Pixel/frame/tap
   // hashing breaks shared rotation and radial drift without a short phase loop.
   bool temporal=_CBSampleTemporal!=0;
   uint seed=BleedSampleSeed(low,asuint(_CBSampleFrame),temporal);
   float2 projectedRadius=_CBSettings.x/(source.a*abs(_CBNDCToView.xy))*_CBSize.xy;
   int samples=(int)_CBLimits.w;
   float3 light=0; float reference=0;
   [loop] for(int j=0;j<samples;j++)
   {
    float2 uv=UV(receiver)+BleedDiskSample(seed,(uint)j,(uint)samples,temporal)*projectedRadius/_CBSize.xy;
    // Reject, never clamp an off-screen donor into a repeated bright edge sample.
    if(any(uv<=0) || any(uv>=1)) continue;
    int2 donorLow=(int2)(uv*_CBSize.xy/_CBLimits.z);
    if(all(donorLow==low)) continue;
    float4 donorGuide=_CBGuide.Load(int3(donorLow,0)); if(donorGuide.a==0) continue;
    float4 donor=_CBSource.Load(int3(donorLow,0));
    int2 donorPixel=Representative(donorLow,donorGuide);
    float3 q=Position(donorPixel,donor.a);
    float3 offset=q-p; float d2=dot(offset,offset), radius2=_CBSettings.x*_CBSettings.x;
    if(d2<.0001 || d2>=radius2) continue;
    float3 direction=offset*rsqrt(d2), nq=Normal(donorPixel);
    if(dot(nq,nq)<.5) continue;
    float facing=saturate(dot(np,direction))*saturate(dot(nq,-direction));
    if(facing<=.0001 || !SegmentVisible(p,q,np,nq)) continue;
    float falloff=saturate(1-d2/radius2);
    light+=donor.rgb*(facing*falloff*falloff);
    reference+=dot(donor.rgb,Luma);
   }
   // Rejections retain their share of the denominator; sparse bright survivors
   // cannot gain energy through normalization at a silhouette or screen edge.
   return float4(light,reference)/samples;
  }
  float3 TemporalNormal(int2 pixel, float z, float3 fallback)
  {
   // Normal-map detail changes with subpixel sampling even on stationary geometry.
   // Keep shading normals for transport; identify history surfaces using depth.
   uint width,height; _CBDepth.GetDimensions(width,height);
   if(width!=(uint)_CBSize.x || height!=(uint)_CBSize.y) return fallback;
   float center=_CBDepth.Load(int3(pixel,0));
   if(!ValidDepth(center) || abs(center-z)>Tolerance(z)) return fallback;
   float3 p=Position(pixel,center), derivatives[2];
   [unroll] for(int axis=0;axis<2;axis++)
   {
    int2 step=axis==0?int2((int)_CBLimits.z,0):int2(0,(int)_CBLimits.z);
    int2 a=pixel-step,b=pixel+step;
    float za=_CBDepth.Load(int3(a,0)),zb=_CBDepth.Load(int3(b,0));
    bool va=all(a>=0) && all(a<(int2)_CBSize.xy) && ValidDepth(za);
    bool vb=all(b>=0) && all(b<(int2)_CBSize.xy) && ValidDepth(zb);
    if(!va && !vb) return fallback;
    // Prefer the side that does not cross a silhouette/depth discontinuity.
    derivatives[axis]=va && (!vb || abs(za-center)<abs(zb-center)) ? p-Position(a,za) : Position(b,zb)-p;
   }
   float3 normal=cross(derivatives[0],derivatives[1]);
   if(dot(normal,normal)<1e-16 || !all(isfinite(normal))) return fallback;
   normal=normalize(normal);
   return dot(normal,p)>0?-normal:normal;
  }
  struct TemporalResult { float4 light:SV_Target0; float4 state:SV_Target1; float2 moments:SV_Target2; float4 diagnostic:SV_Target3; };
  TemporalResult TemporalResolve(Varyings i)
  {
   TemporalResult o; o.light=0; o.state=0; o.moments=0; o.diagnostic=float4(0,0,1,1);
   int2 low=min((int2)(i.uv*_CBSize.zw),(int2)_CBSize.zw-1);
   float4 guide=_CBGuide.Load(int3(low,0)), source=_CBSource.Load(int3(low,0));
   if(guide.a==0 || !ValidDepth(source.a) || source.a>=_CBSettings.w) return o;
   o.diagnostic=float4(1,.5,0,1); // fresh/offscreen/unavailable history
   int2 pixel=Representative(low,guide);
   float3 shadingNormal=UnpackNormal(guide.rg), p=Position(pixel,source.a);
   float3 n=TemporalNormal(pixel,source.a,shadingNormal);
   float3 wn=normalize(mul((float3x3)_CBViewToWorld,n*float3(1,-1,-1)));
   float3 world=mul(_CBViewToWorld,float4(p*float3(1,-1,-1),1)).xyz;
   float4 current=_CBBounce.Load(int3(low,0)); o.light=current;
   float luminance=min(_CBLimits.x,max(0,dot(current.rgb,Luma)));
   o.moments=float2(luminance,luminance*luminance);
   // RGBAHalf stores depth, oct world normal, and integer (count*32 + texel
   // offset). Counts 1..30 and offsets 0..15 fit exactly in half precision.
   float offset=round(guide.a*255)-1;
   o.state=float4(source.a,PackNormal(wn),32+offset);
   if(_CBTemporal.x<.5) return o;
   float3 previous=mul(_CBPreviousView,float4(world,1)).xyz*float3(1,-1,-1);
   if(previous.z<=0) return o;
   float2 uv=(previous.xy/previous.z-_CBPreviousNDC.zw)/_CBPreviousNDC.xy;
   if(any(uv<=0) || any(uv>=1)) return o;
   uint representative=(uint)offset;
   float2 texelOffset=float2(representative&3,representative>>2);
   // Anchor interpolation to representative pixels, not block centers. A static
   // surface must reproject to itself instead of drifting a fraction of a texel
   // on every accumulation step. Previous offsets are validated per tap below.
   float2 coord=(uv*_CBSize.xy-.5-texelOffset)/_CBLimits.z, f=frac(coord); int2 origin=(int2)floor(coord);
   float3 pn=normalize(mul((float3x3)_CBPreviousView,wn)*float3(1,-1,-1));
   float4 history=0; float2 moments=0; float total=0, count=0;
   float ageWeight=0,normalWeight=0,geometryWeight=0;
   float maxDistance=SurfaceFootprint(previous,pn,_CBPreviousNDC,1);
   // A different texel of the same textured surface is not a lighting change.
   // Bound the old signature by a small current, geometry-compatible envelope.
   // Uniform newly lit/dark surfaces still reject immediately.
   float3 sourceMin=source.rgb,sourceMax=source.rgb;
   float localDistance=SurfaceFootprint(p,n,_CBNDCToView,1);
   [unroll] for(int yy=-1;yy<=1;yy++) [unroll] for(int xx=-1;xx<=1;xx++)
   {
    int2 tap=low+int2(xx,yy); if(any(tap<0) || any(tap>=(int2)_CBSize.zw)) continue;
    float4 g=_CBGuide.Load(int3(tap,0)),s=_CBSource.Load(int3(tap,0));
    if(g.a==0 || !ValidDepth(s.a)) continue;
    float3 delta=Position(Representative(tap,g),s.a)-p;
    if(GeometryConfidence(p,p+delta,n,UnpackNormal(g.rg),localDistance,.85)<=0) continue;
    sourceMin=min(sourceMin,s.rgb);sourceMax=max(sourceMax,s.rgb);
   }
   [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++)
   {
    int2 tap=origin+int2(x,y); if(any(tap<0) || any(tap>=(int2)_CBSize.zw)) continue;
    float4 state=_CBHistoryState.Load(int3(tap,0));
    uint packed=(uint)round(max(state.a,0)); float age=packed>>5;
    if(age<1 || !ValidDepth(state.r)) continue;
    float w=(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y);
    ageWeight+=w;
    float3 oldNormal=UnpackNormal(state.gb); if(dot(wn,oldNormal)<.9) continue;
    normalWeight+=w;
    uint oldOffset=packed&31;
    int2 oldPixel=tap*(int)_CBLimits.z+int2(oldOffset&3,oldOffset>>2);
    float3 q=float3((UV(oldPixel)*_CBPreviousNDC.xy+_CBPreviousNDC.zw)*state.r,state.r);
    float3 oldPN=normalize(mul((float3x3)_CBPreviousView,oldNormal)*float3(1,-1,-1)), delta=q-previous;
    float confidence=GeometryConfidence(previous,q,pn,oldPN,maxDistance,.9);
    if(confidence<=0) continue;
    geometryWeight+=w;
    // Reject a changed local lighting signature, including newly lit/dark
    // surfaces. This is not object motion tracking; coplanar movers can remain.
    float3 oldSource=_CBHistorySource.Load(int3(tap,0)).rgb;
    float3 change=max(sourceMin-oldSource,oldSource-sourceMax);
    float scale=max(max(sourceMax.r,max(sourceMax.g,sourceMax.b)),max(oldSource.r,max(oldSource.g,oldSource.b)));
    float lightingLimit=.002+scale*.35;
    confidence*=1-smoothstep(lightingLimit*.5,lightingLimit,max(change.r,max(change.g,change.b)));
    w*=confidence;
    history+=_CBHistoryBounce.Load(int3(tap,0))*w; count+=min(age,_CBTemporal.y)*w; total+=w;
    moments+=_CBHistoryMoments.Load(int3(tap,0))*w;
   }
   if(total<=1e-5)
   {
    // Report the last stage with any usable bilinear support.
    if(ageWeight>1e-5) o.diagnostic=float4(1,1,0,1); // normal
    if(normalWeight>1e-5) o.diagnostic=float4(1,0,0,1); // depth/position
    if(geometryWeight>1e-5) o.diagnostic=float4(0,1,1,1); // lighting signature
    return o;
   }
   history/=total; count/=total; moments/=total;
   // Do not turn a marginal reprojection into either fully trusted history or
   // an abrupt cold start. Coverage/geometry/lighting confidence controls both
   // the blend and effective age continuously; clear disocclusions stay rejected.
   float confidence=saturate(total);
   count=lerp(1,count,confidence);
   // Current random donors cannot bound the historical mean: a dim hit would
   // erase bright samples accumulated on preceding frames. Use the stable donor
   // cap instead. Gather's nonnegative weights sum to <= 1, so both accumulated
   // radiance luminance and reference are bounded by this configured ceiling.
   float weight=min(count/(count+1),1-1/_CBTemporal.y)*confidence;
   float scale=min(1,_CBLimits.x/max(dot(history.rgb,Luma),1e-6));
   float limiting=max(1-scale,1-min(1,_CBLimits.x/max(history.a,1e-6)));
   history.rgb*=scale;
   history.a=min(history.a,_CBLimits.x);
   // No observed donors is a zero-valued sample, not a disocclusion. Blend it
   // at the selected history rate; geometry/lighting changes reject above.
   o.light=lerp(current,history,weight);
   moments=clamp(moments,0,float2(_CBLimits.x,_CBLimits.x*_CBLimits.x));
   o.moments=lerp(o.moments,moments,weight);
   o.moments.y=max(o.moments.y,o.moments.x*o.moments.x);
   o.diagnostic=float4(lerp(float3(1-weight,weight,0),float3(1,0,1),limiting),1);
   // Quantize the age separately so fractional bilinear counts cannot corrupt
   // the packed representative offset on the next frame.
   o.state.a=min(floor(count+.5)+1,_CBTemporal.y)*32+offset;
   return o;
  }
  float4 SpatialFilter(Varyings i):SV_Target
  {
   int2 center=min((int2)(i.uv*_CBSize.zw),(int2)_CBSize.zw-1);
   float4 guide=_CBGuide.Load(int3(center,0));
   float z=_CBSource.Load(int3(center,0)).a;
   if(guide.a==0 || !ValidDepth(z) || z>=_CBSettings.w) return 0;
   float3 n=UnpackNormal(guide.rg), p=Position(Representative(center,guide),z);
   int step=max(1,(int)_CBSpatialStep);
   float maxDistance=min(SurfaceFootprint(p,n,_CBNDCToView,step),_CBSettings.x*(step>=4?.5:.25));
   float4 original=_CBSpatialInput.Load(int3(center,0));
   float amount=1,variance=0;
   if(step>=4)
   {
    if(_CBUseStatistics==0) return original;
    float2 m=_CBMoments.Load(int3(center,0));variance=max(0,m.y-m.x*m.x);
    float age=(uint)round(_CBCurrentState.Load(int3(center,0)).a)>>5;
    float noise=variance/max(variance+m.x*m.x,1e-12);
    amount=max(1-saturate((age-1)/7),smoothstep(.1,.75,noise));
    if(amount<.001) return original;
   }
   float4 sum=0; float total=0;
   // One compact binomial kernel. Valid zero-light neighbors MUST carry weight:
   // ignoring them would preserve bright speckles and amplify sparse survivors.
   [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
   {
    int2 tap=center+int2(x,y)*step; if(any(tap<0) || any(tap>=(int2)_CBSize.zw)) continue;
    float4 g=_CBGuide.Load(int3(tap,0)); if(g.a==0) continue;
    float d=_CBSource.Load(int3(tap,0)).a;
    if(!ValidDepth(d) || d>=_CBSettings.w) continue;
    float3 tn=UnpackNormal(g.rg), delta=Position(Representative(tap,g),d)-p;
    float3 q=p+delta;
    float confidence=GeometryConfidence(p,q,n,tn,maxDistance,.85);
    if(confidence<=0) continue;
    float weight=(x==0?2:1)*(y==0?2:1);
    weight*=confidence*pow(saturate(dot(n,tn)),8);
    weight*=saturate(1-abs(g.b-guide.b)*4);
    if(step>=4)
    {
     float2 m=_CBMoments.Load(int3(tap,0));float tapVariance=max(0,m.y-m.x*m.x);
     float l=dot(original.rgb,Luma),tl=dot(_CBSpatialInput.Load(int3(tap,0)).rgb,Luma);
     float sigma=2*sqrt(variance+tapVariance)+.1*max(l,tl)+1e-5;
     weight*=exp2(-abs(l-tl)/sigma);
    }
    // Filter RGB and its donor reference together, before the existing reflected
    // cap and full-resolution receiver gates. Never filter/composite in place.
    sum+=_CBSpatialInput.Load(int3(tap,0))*weight; total+=weight;
   }
   return total>0 ? lerp(original,sum/total,amount) : original;
  }
  float4 Composite(Varyings i):SV_Target
  {
   int2 p=FullPixel(i.uv); float z=_CBDepth.Load(int3(p,0));
   if(!ValidDepth(z) || z>=_CBSettings.w || _CBActors.Load(int3(p,0))>.5 || !CurrentSurface(p,z)) return 0;
   float3 shadingNormal=Normal(p); if(dot(shadingNormal,shadingNormal)<.5) return 0;
   float3 normal=TemporalNormal(p,z,shadingNormal);
   float3 position=Position(p,z);
   float2 coord=(p+.5)/_CBLimits.z-.5, fraction=frac(coord); int2 origin=(int2)floor(coord);
   float4 light=0; float total=0;
   [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++)
   {
    int2 low=origin+int2(x,y); if(any(low<0) || any(low>=(int2)_CBSize.zw)) continue;
    float4 guide=_CBGuide.Load(int3(low,0)); if(guide.a==0) continue;
    float d=_CBSource.Load(int3(low,0)).a;
    float3 n=UnpackNormal(guide.rg), delta=Position(Representative(low,guide),d)-position;
    // Plane-aware depth comparison preserves sloped surfaces without blending
    // across disconnected foreground/background surfaces or perpendicular walls.
    float confidence=GeometryConfidence(position,position+delta,normal,n,SurfaceFootprint(position,normal,_CBNDCToView,1),.85);
    if(confidence<=0) continue;
    float weight=(x==0?1-fraction.x:fraction.x)*(y==0?1-fraction.y:fraction.y);
    weight*=confidence*pow(saturate(dot(normal,n)),8);
    light+=_CBFilteredBounce.Load(int3(low,0))*weight; total+=weight;
   }
   if(total<.0001) return 0;
   light/=total;
   // Artistic compensation for the sparse, angle/distance-weighted gather.
   // Keep rejected taps in its denominator: gain must never normalize a lone
   // bright survivor to full coverage. 1.0 strength uses 32x incident radiance.
   float3 added=light.rgb*(_CBSettings.y*32);
   added*=DecodeColor(saturate(_CameraGBufferTexture0.Load(int3(p,0)).rgb));
   // Bound the actual reflected addition, not incident light before reflectance.
   // Otherwise dark materials hit the cap before strength can become useful.
   // Zero reflectance still adds zero; AO/distance/edge gates remain downstream.
   added*=min(1,(_CBLimits.y*light.a)/max(dot(added,Luma),1e-6));
   added*=smoothstep(.05,.35,_CBVisibility.Load(int3(p,0)).r);
   added*=1-smoothstep(_CBSettings.w*.6,_CBSettings.w,z);
   float2 edge=min(i.uv,1-i.uv)*_CBSize.xy;
   added*=saturate(min(edge.x,edge.y)/(_CBLimits.z*2));
   if(_CBGamma>.5 && any(added>0))
   {
    float3 original=max(_CBGammaScene.Load(int3(p,0)).rgb,0);
    // Exact scene-encoded increment; adding EncodeColor(added) directly would
    // overbrighten the base and would not implement diffuse radiance addition.
    added=max(EncodeColor(DecodeColor(original)+added)-original,0);
   }
   return float4(added,0);
  }
  float4 DebugContribution(Varyings i):SV_Target
  {
   if(!InputDimensionsMatch()) return float4(1,0,1,1);
   // Share every rejection and receiver gate with the actual composite. Keep HDR
   // values in the debug target; only the display can clip amplified highlights.
   return float4(Composite(i).rgb*_CBDebugGain,1);
  }
  ENDHLSL
  Pass
  {
   Name "Capture"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Capture
   ENDHLSL
  }
  Pass
  {
   Name "Gather"
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Gather
   ENDHLSL
  }
  Pass
  {
   Name "Composite"
   Blend One One
   ColorMask RGB
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Composite
   ENDHLSL
  }
  Pass
  {
   Name "ContributionDebug"
   Blend Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DebugContribution
   ENDHLSL
  }
  Pass
  {
   Name "SpatialFilter"
   Blend Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment SpatialFilter
   ENDHLSL
  }
  Pass
  {
   Name "TemporalResolve"
   Blend Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment TemporalResolve
   ENDHLSL
  }
 }
 Fallback Off
}
