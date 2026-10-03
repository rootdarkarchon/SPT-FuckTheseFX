Shader "Hidden/FTFX/StochasticSSR/Reflection"
{
 SubShader {
  ZTest Always ZWrite Off Cull Off
  HLSLINCLUDE
  #pragma target 5.0
#include "RasterState.hlsl"
  #include "TodReflection.hlsl"
  Texture2D<float4> _MainTex,_Raw,_Guide,_Point,_Record,_Signal;
  Texture2D<float4> _UnfilledSignal;
  SamplerState sampler_Signal;
  float4 _NativeReconstruction; // enabled, maximum generated mip
  Texture2D<float4> _TraceInfo,_TraceStop;
  Texture2D<float4> _DepthHit;
  float _ThicknessConfidenceFloor;
  float4 _ContinuationLimits;
  Texture2D<float4> _CapturedReflection,_ExactResidual;
  Texture2D<float4> _ReceiverFallback;
  SamplerState sampler_ReceiverFallback;
  float _FallbackMipCount,_FallbackStrength;
  Texture2D<float2> _WaterCoverage;
  float _WaterAvailable;
  Texture2D<float4> _WaterNormal;
  float _WaterNormalsEnabled;
  Texture2D<float4> _PreAmbientSurface;
  float _SurfaceColourInfluence;
  float _CaptureAvailable,_UseCapturedContribution;
  int _ReplayInput;
  Texture2D<float4> _History,_HistoryGuide,_HistoryPoint,_HistoryMaterial;
  Texture2D<float4> _HistoryStats,_TemporalStats,_HistoryPlane;
  Texture2D<float4> _HistoryAnchor;
  float4 _CoverageTemporal; // enabled, full-strength smoothness, fade-out smoothness
  float4 _CoverageFilter; // strength, full smoothness, fade end, smooth strength
  float _CoverageFilterHistory;
  float4x4 _PreviousInverseProjection;
  Texture2D<float4> _CameraGBufferTexture0,_CameraGBufferTexture1,_CameraGBufferTexture2,_StencilShadow;
  Texture2D<float> _CameraDepthTexture,_Depth;
  Texture2D<float2> _CameraMotionVectorsTexture;
  TextureCube<float4> _MyGlobalReflectionProbe;
  SamplerState sampler_MyGlobalReflectionProbe;
  SamplerState sampler_StencilShadow,sampler_point_clamp;
  float4 _Size,_Options; // camera xy, trace zw; history valid, frames, cutoff, debug
  float2 _ReceiverCutoff; // native support threshold, excluded-receiver environment blend
  float2 _ZeroSmoothnessProbe; // experimental enabled, far contribution
  float3 _ZeroSmoothnessProbeFade; // close contribution, start metres, end metres
  float2 _ReceiverRange; // enabled, maximum camera-to-receiver distance in metres
  float4x4 _InvProjection,_CameraToWorld,_Projection,_PreviousClip,_PreviousToCurrent;
  float _ReflectionBottomShade,_SSRFactor;
  float4 _ZBufferParams;
  struct V { float4 p:SV_POSITION; };
  V Vert(float3 p:POSITION) { V o;o.p=float4(p.xy,0,1);return o; }
  float4 ReplayInput(V i):SV_Target {
   int3 p=int3((int2)i.p.xy,0);
   if(_ReplayInput==1)return _CameraGBufferTexture0.Load(p);
   if(_ReplayInput==2)return _CameraGBufferTexture1.Load(p);
   if(_ReplayInput==3)return _CameraGBufferTexture2.Load(p);
   if(_ReplayInput==4)return float4(_CameraMotionVectorsTexture.Load(p),0,0);
   if(_ReplayInput==5)return float4(_WaterCoverage.Load(p),0,0);
   return _MainTex.Load(p);
  }
  int2 TracePixel(V i) { return min((int2)i.p.xy,(int2)_Size.zw-1); }
  int2 Receiver(int2 p) { return min((int2)((p+.5)*_Size.xy/_Size.zw),(int2)_Size.xy-1); }
  float3 ViewPosition(float2 uv,float depth,float4x4 inverse) { float4 h=mul(inverse,float4(uv*2-1,.5,1));return h.xyz*(depth/-h.z); }
  float3 ViewPosition(float2 uv,float depth) { return ViewPosition(uv,depth,SsrRasterMatrix(_InvProjection,4)); }
  float3 UnitNormal(int2 p) { float3 n=_CameraGBufferTexture2.Load(int3(p,0)).xyz*2-1;return n*rsqrt(max(dot(n,n),1e-12)); }
  float3 Decode(float2 p) {
   float3 n=float3(p,1-abs(p.x)-abs(p.y));
   if(n.z<0)n.xy=(1-abs(n.yx))*float2(n.x>=0?1:-1,n.y>=0?1:-1);
   return normalize(n);
  }
  float3 WorldDirection(int2 pixel,float3 n) {
   return reflect(normalize(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),ViewPosition((pixel+.5)/_Size.xy,1))),n);
  }
  float3 Material(int2 pixel,float3 n) {
   float2 uv=(pixel+.5)/_Size.xy;
   float4 spec=_CameraGBufferTexture1.Load(int3(pixel,0));
   float3 view=normalize(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),ViewPosition((pixel+.5)/_Size.xy,1)));
   float f=pow(saturate(1-abs(dot(n,-view))),5);
   float3 fresnel=lerp(spec.rgb,saturate(max(max(spec.r,spec.g),spec.b)+spec.a),f);
   float r=1-spec.a;
   return fresnel*(1-.28*max(r*r,.002)*r);
  }
  float3 Probe(int2 pixel,float3 direction) {
   float r=1-_CameraGBufferTexture1.Load(int3(pixel,0)).a;
   return _MyGlobalReflectionProbe.SampleLevel(sampler_MyGlobalReflectionProbe,direction,6*r*(1.7-.7*r)).rgb;
  }
  float3 ReflectionMaterial(int2 pixel) {
   // Installed native final pass: reflected-signal response uses s^3 and the
   // original G-buffer normal (normalization belongs to subtraction instead).
   float4 spec=_CameraGBufferTexture1.Load(int3(pixel,0));
   float s=spec.a*spec.a*spec.a,r=1-s;
   float3 n=_CameraGBufferTexture2.Load(int3(pixel,0)).xyz*2-1;
   float3 view=normalize(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),ViewPosition((pixel+.5)/_Size.xy,1)));
   float f=1-abs(dot(n,-view));f=f*f*f*f*f;
   float3 fresnel=lerp(spec.rgb,saturate(max(max(spec.r,spec.g),spec.b)+s),f);
   return fresnel*(1-.28*max(r*r,.002)*r);
  }
  float3 ReceiverRadiance(int2 pixel) {
   float s=_CameraGBufferTexture1.Load(int3(pixel,0)).a;
   float r=1-s*s*s;
   float mip=max(0,r*r*(_FallbackMipCount-1)+r);
   return _ReceiverFallback.SampleLevel(sampler_ReceiverFallback,(pixel+.5)/_Size.xy,mip).rgb;
  }
  float3 ReceiverContribution(int2 pixel) {
   return ReceiverRadiance(pixel)*ReflectionMaterial(pixel);
  }
  float2 WaterResponse(int2 pixel) {
   if(_WaterAvailable<.5)return float2(0,1);
   float2 water=_WaterCoverage.Load(int3(pixel,0));
   float raw=_CameraDepthTexture.Load(int3(pixel,0));
   float eye=rcp(_ZBufferParams.z*raw+_ZBufferParams.w);
   if(water.x<=0 || !all(isfinite(water)) || abs(eye-water.x)>max(.005,eye*.0001))return float2(0,1);
   return float2(1,1);
  }
  float3 SkyRadiance(int2 pixel,float3 direction,float visibility) {
   float3 sky=TodReflectedSky(direction)*saturate(direction.y+_ReflectionBottomShade)*visibility;
   // Confirmed sky uses the native bounded blend. Smith applies only to sky.
   return lerp(ReceiverRadiance(pixel),sky,.5);
  }
  float4 Resolve(V i):SV_Target {
   int2 p=TracePixel(i);float4 record=_Record.Load(int3(p,0));
   float c=_Guide.Load(int3(p,0)).x;
   if(c<=0 || record.w<=0)return 0;
   float4 raw=_Raw.Load(int3(p,0));float3 radiance=raw.rgb*raw.a;
   if(record.w==2) {
    float3 direction=normalize(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),_Point.Load(int3(p,0)).xyz));
    float3 local=mul((float3x3)TOD_World2Sky,direction);
    if(!TodAvailable() || dot(local,local)<1e-8)return 0;
    // Compatibility sky branch: native TOD is a bounded scattering blend,
    // not an unconditional replacement. At infinite sky distance its maximum
    // blend is 0.5. Retain the native receiver signal as the other half; apply
    // Smith only to the sampled sky, never to the retained receiver component.
    // This is an explicit extension, not a claim that native SSR resolves sky.
    radiance=SkyRadiance(Receiver(p),direction,raw.a);
   }
   // Smith visibility is estimator amplitude, never hit confidence.
   return float4(radiance*c,c);
  }
  float3 SurfaceNormal(int2 pixel) {
   // The G-buffer normal includes ripples/normal maps. It is not the plane of
   // the depth surface. Use one-sided depth derivatives, choosing the nearer
   // neighbour in each axis so a shoreline/weapon edge does not tilt the plane.
   int2 hi=(int2)_Size.xy-1;
   int2 l=max(pixel-int2(1,0),0),r=min(pixel+int2(1,0),hi);
   int2 d=max(pixel-int2(0,1),0),u=min(pixel+int2(0,1),hi);
   float z=_Depth.Load(int3(pixel,0));
   float zl=_Depth.Load(int3(l,0)),zr=_Depth.Load(int3(r,0));
   float zd=_Depth.Load(int3(d,0)),zu=_Depth.Load(int3(u,0));
   float3 p=ViewPosition((pixel+.5)/_Size.xy,z);
   float3 dx=(pixel.x>0 && (pixel.x==hi.x || abs(zl-z)<abs(zr-z)))?
       p-ViewPosition((l+.5)/_Size.xy,zl):ViewPosition((r+.5)/_Size.xy,zr)-p;
   float3 dy=(pixel.y>0 && (pixel.y==hi.y || abs(zd-z)<abs(zu-z)))?
       p-ViewPosition((d+.5)/_Size.xy,zd):ViewPosition((u+.5)/_Size.xy,zu)-p;
   float3 n=cross(dx,dy);
   return n*rsqrt(max(dot(n,n),1e-20));
  }
  // Select depth neighbours before transforming them. Keep the same one-sided
  // differences as SurfaceNormal, and reuse the receiver's existing position.
  float3 ReconstructionPlane(int2 pixel,float z,float3 position,float4x4 inverse) {
   int2 hi=(int2)_Size.xy-1;
   int2 l=max(pixel-int2(1,0),0),r=min(pixel+int2(1,0),hi);
   int2 d=max(pixel-int2(0,1),0),u=min(pixel+int2(0,1),hi);
   float zl=_Depth.Load(int3(l,0)),zr=_Depth.Load(int3(r,0));
   float zd=_Depth.Load(int3(d,0)),zu=_Depth.Load(int3(u,0));
   bool left=pixel.x>0 && (pixel.x==hi.x || abs(zl-z)<abs(zr-z));
   bool down=pixel.y>0 && (pixel.y==hi.y || abs(zd-z)<abs(zu-z));
   int2 px=left?l:r,py=down?d:u;
   float3 x=ViewPosition((px+.5)/_Size.xy,left?zl:zr,inverse);
   float3 y=ViewPosition((py+.5)/_Size.xy,down?zd:zu,inverse);
   float3 dx=left?position-x:x-position,dy=down?position-y:y-position;
   float3 n=cross(dx,dy);
   return n*rsqrt(max(dot(n,n),1e-20));
  }
  float ReceiverWeight(int2 pixel,float3 position,float depth,float3 plane,float3 normal,float smoothness,int2 q,out float3 rejected,float4x4 inverse) {
   float4 guide=_Guide.Load(int3(q,0));int2 receiver=Receiver(q);
   float3 offset=position-ViewPosition((receiver+.5)/_Size.xy,guide.y,inverse);
   rejected=0;
   if(abs(dot(plane,offset))>max(.02,depth*.001)) {rejected.r=1;return 0;}
   float nw=smoothstep(.9,.999,dot(normal,Decode(guide.zw)));
   float sw=1-smoothstep(.02,.1,abs(smoothness-_Point.Load(int3(q,0)).w));
   rejected.g=1-nw;rejected.b=nw*(1-sw);
   // Incident radiance is independent of receiver F0; the full-resolution
   // compositor applies F0 once. Gating on its texture punches a trace grid.
   return nw*sw;
  }
  float ReceiverWeight(int2 pixel,float3 position,float depth,float3 plane,float3 normal,float smoothness,int2 q,out float3 rejected) {
   return ReceiverWeight(pixel,position,depth,plane,normal,smoothness,q,rejected,SsrRasterMatrix(_InvProjection,4));
  }
  float NeighbourWeight(int2 p,int2 q) {
   float3 rejected;int2 pixel=Receiver(p);float4 g=_Guide.Load(int3(p,0));
   return ReceiverWeight(pixel,ViewPosition((pixel+.5)/_Size.xy,g.y),g.y,SurfaceNormal(pixel),Decode(g.zw),_Point.Load(int3(p,0)).w,q,rejected);
  }
  float Luminance(float3 c) {return dot(c,float3(.2126,.7152,.0722));}
  // Validate individual history taps before interpolation. Plane distance is
  // evaluated in current view space, not as equal eye depth on a sloped road.
  bool HistoryReceiver(int2 p,int2 old,out uint reason) {
   reason=8;
   int2 pixel=Receiver(p);float4 g=_Guide.Load(int3(p,0)),hg=_HistoryGuide.Load(int3(old,0));
   float4 hp=_HistoryPlane.Load(int3(old,0));
   float2 oldUv=(Receiver(old)+.5)/_Size.xy;
   float4 v=mul(SsrRasterMatrix(_PreviousInverseProjection,24),float4(oldUv*2-1,.5,1));
   float3 oldPosition=mul(SsrRasterMatrix(_PreviousToCurrent,20),float4(v.xyz*(hg.y/-v.z),1)).xyz;
   float3 position=ViewPosition((pixel+.5)/_Size.xy,g.y),plane=SurfaceNormal(pixel);
   float3 oldPlane=mul(transpose((float3x3)SsrRasterMatrix(_CameraToWorld,8)),Decode(hp.xy));
   float tolerance=max(.02,g.y*.001);
   float4 material=_CameraGBufferTexture1.Load(int3(pixel,0)),hm=_HistoryMaterial.Load(int3(old,0));
   return hg.y>0 && all(isfinite(oldPosition)) &&
    abs(dot(plane,oldPosition-position))<=tolerance && abs(dot(oldPlane,oldPosition-position))<=tolerance &&
    abs(dot(plane,oldPlane))>.95 && hp.w==WaterResponse(pixel).x &&
    dot(Decode(g.zw),Decode(hg.zw))>=.98 && abs(_Point.Load(int3(p,0)).w-_HistoryPoint.Load(int3(old,0)).w)<=.02 &&
    all(abs(material.rgb-hm.rgb)<=.02);
  }
  bool HistoryHit(int2 old,float kind,out uint reason) {
   reason=16;if(_HistoryPlane.Load(int3(old,0)).z!=kind)return false;
   reason=32;
   if(kind==1) {
    float3 hit=mul(SsrRasterMatrix(_PreviousToCurrent,20),float4(_HistoryPoint.Load(int3(old,0)).xyz,1)).xyz;
    float4 clip=mul(SsrRasterMatrix(_Projection,0),float4(hit,1));float2 uv=clip.xy/max(clip.w,1e-8)*.5+.5;
    if(clip.w<=0 || any(uv<=0) || any(uv>=1))return false;
    int2 pixel=(int2)(uv*_Size.xy);float z=_Depth.Load(int3(pixel,0));
    return abs(z+hit.z)<=max(.05,z*.01);
   }
   return kind==2;
  }
  struct TemporalOutput {float4 color:SV_Target0;float4 stats:SV_Target1;float4 anchor:SV_Target2;};
  // Statistical coverage is separate from raw trace authority. An anchor is
  // the last accepted source, transformed into this frame, never a new hit.
  bool CoverageAnchor(float4 old,float kind,out float4 anchor,out uint reason) {
   reason=16;anchor=0;
   if(!all(isfinite(old)) || old.w<0)return false;
   if(old.w==0)return true; // A valid history of zero observations.
   float sourceKind=(uint)old.w%4;
   if(kind>0 && sourceKind!=kind)return false;
   reason=32;
   // The source is continuously validated; misses decay the estimate rather
   // than expiring its receiver's observation count after an arbitrary age.
   if(sourceKind==1) {
    anchor=float4(mul(SsrRasterMatrix(_PreviousToCurrent,20),float4(old.xyz,1)).xyz,old.w);
    float4 clip=mul(SsrRasterMatrix(_Projection,0),float4(anchor.xyz,1));
    float2 uv=clip.xy/max(clip.w,1e-8)*.5+.5;
    if(clip.w<=0 || any(uv<=0) || any(uv>=1))return false;
    float depth=_Depth.Load(int3((int2)(uv*_Size.xy),0));
    return abs(depth+anchor.z)<=max(.05,depth*.01);
   }
   if(sourceKind==2) {
    anchor=float4(normalize(mul((float3x3)SsrRasterMatrix(_PreviousToCurrent,20),old.xyz)),old.w);
    float4 clip=mul(SsrRasterMatrix(_Projection,0),float4(anchor.xyz,0));
    float2 uv=clip.xy/max(clip.w,1e-8)*.5+.5;
    if(clip.w<=0 || any(uv<=0) || any(uv>=1))return false;
    float raw=_CameraDepthTexture.Load(int3((int2)(uv*_Size.xy),0));
    return 1/(_ZBufferParams.x*raw+_ZBufferParams.y)>=.999;
   }
   return false;
  }
  TemporalOutput CoverageTemporal(int2 p,int2 pixel,float4 current,float strength) {
   TemporalOutput o=(TemporalOutput)0;o.color=current;
   float kind=current.a>0?_Record.Load(int3(p,0)).w:0;
   if(current.a>0)o.anchor=float4(_Point.Load(int3(p,0)).xyz,kind);
   // Background, invalid normals and excluded receivers cannot collect history.
   if(current.a<=0 && _Record.Load(int3(p,0)).z<2) {o.stats.w=1;return o;}
   float lum=Luminance(current.rgb);o.stats=float4(1,lum,lum*lum,2);
   if(_Options.x<.5 || !SsrRasterHistory())return o;
   float2 oldUv=SsrHistoryUV((pixel+.5)/_Size.xy,_CameraMotionVectorsTexture.Load(int3(pixel,0)));
   if(!all(isfinite(oldUv)) || any(oldUv<=0) || any(oldUv>=1)){o.stats.w=4;return o;}
   float2 grid=oldUv*_Size.zw-.5,f=frac(grid);int2 base=(int2)floor(grid);
   float4 history=0,retainedAnchor=0;float3 statistics=0;float support=0,anchorWeight=0;uint rejected=0;
   [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++) {
    int2 q=base+int2(x,y);if(any(q<0)||any(q>=(int2)_Size.zw)){rejected|=4;continue;}
    float w=(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y);if(w<=1e-6)continue;
    uint why;float4 anchor;
    if(!HistoryReceiver(p,q,why)){rejected|=why;continue;}
    bool sourceValid=CoverageAnchor(_HistoryAnchor.Load(int3(q,0)),kind,anchor,why);
    if(!sourceValid)rejected|=why;
    if(kind==2 && (uint)anchor.w%4==2 && dot(anchor.xyz,normalize(_Point.Load(int3(p,0)).xyz))<.995){rejected|=32;sourceValid=false;}
    if(retainedAnchor.w>0 && anchor.w>0 && (uint)retainedAnchor.w%4!=(uint)anchor.w%4){rejected|=16;sourceValid=false;}
    float4 h=_History.Load(int3(q,0)),stats=_HistoryStats.Load(int3(q,0));
    if(!all(isfinite(h)) || !all(isfinite(stats)) || stats.x<1){rejected|=64;continue;}
    if(!sourceValid || (h.a>0 && anchor.w==0)) {
     // Invalid radiance and its moments are discarded together. A compatible
     // receiver still retains its count, so the next rare hit cannot cold-start.
     h=0;anchor=0;stats.yz=0;
    }
    history+=h*w;statistics+=stats.xyz*w;support+=w;
    // Do not interpolate unrelated source positions into an invented hit.
    if(anchor.w>0 && w*h.a>anchorWeight){anchorWeight=w*h.a;retainedAnchor=anchor;}
   }
   o.stats.w=rejected;if(support<.05)return o;
   history/=support;statistics/=support;
   float age=min(max(1,statistics.x),max(1,_Options.y-1));
   float w=age/(age+1)*saturate(support*2)*strength;
   o.color=lerp(current,history,w);
   o.stats=float4(min(_Options.y,1+statistics.x*saturate(support*2)),lerp(lum,statistics.y,w),lerp(lum*lum,statistics.z,w),rejected);
   if(current.a<=0 && o.color.a>0)o.anchor=retainedAnchor;
   // Retire negligible half-float residuals, retaining the compatible count.
   if(o.color.a<1e-5 && max(max(o.color.r,o.color.g),o.color.b)<1e-5){o.color=0;o.anchor=0;o.stats.yz=0;}
   return o;
  }
  // Stats: effective sample count, luminance mean, second moment, rejection bits.
  TemporalOutput Temporal(V i) {
   int2 p=TracePixel(i),pixel=Receiver(p);float4 current=_Signal.Load(int3(p,0));
   float smoothness=_Point.Load(int3(p,0)).w;
   float roughStrength=_CoverageTemporal.z>_CoverageTemporal.y?
    1-smoothstep(_CoverageTemporal.y,_CoverageTemporal.z,smoothness):(smoothness<=_CoverageTemporal.y?1:0);
   if(_CoverageTemporal.x>.5 && roughStrength>0)return CoverageTemporal(p,pixel,current,roughStrength);
   TemporalOutput o=(TemporalOutput)0;o.color=current;
   if(current.a>0)o.anchor=float4(_Point.Load(int3(p,0)).xyz,_Record.Load(int3(p,0)).w);
   if(current.a<=0){o.stats.w=1;return o;}
   float3 color=current.rgb/current.a;float lum=Luminance(color);
   o.stats=float4(1,lum,lum*lum,2);
   if(_Options.x<.5 || !SsrRasterHistory())return o;
   float2 oldUv=SsrHistoryUV((pixel+.5)/_Size.xy,_CameraMotionVectorsTexture.Load(int3(pixel,0)));
   if(!all(isfinite(oldUv)) || any(oldUv<=0) || any(oldUv>=1)){o.stats.w=4;return o;}
   float2 grid=oldUv*_Size.zw-.5,f=frac(grid);int2 base=(int2)floor(grid);
   float4 history=0;float3 stats=0;float support=0;uint rejected=0;
   float kind=_Record.Load(int3(p,0)).w;
   [unroll]for(int y=0;y<2;y++)[unroll]for(int x=0;x<2;x++) {
    int2 q=base+int2(x,y);if(any(q<0)||any(q>=(int2)_Size.zw)){rejected|=4;continue;}
    float w=(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y);if(w<=1e-6)continue;
    uint why;if(!HistoryReceiver(p,q,why) || !HistoryHit(q,kind,why)){rejected|=why;continue;}
    if(kind==2) {
     float3 oldRay=normalize(mul((float3x3)SsrRasterMatrix(_PreviousToCurrent,20),_HistoryPoint.Load(int3(q,0)).xyz));
     if(dot(oldRay,normalize(_Point.Load(int3(p,0)).xyz))<.995){rejected|=32;continue;}
    }
    float4 h=_History.Load(int3(q,0));if(h.a<=0 || !all(isfinite(h))){rejected|=64;continue;}
    history+=h*w;stats+=_HistoryStats.Load(int3(q,0)).xyz*w;support+=w;
   }
   o.stats.w=rejected;
   if(support<.05)return o;
   history/=support;stats/=support;
   float3 lo=color,hi=color,mean=color,moment=color*color;float count=1;
   [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++) {
    if(x==0&&y==0)continue;int2 q=p+int2(x,y);
    if(any(q<0)||any(q>=(int2)_Size.zw))continue;
    float4 v=_Signal.Load(int3(q,0));float w=NeighbourWeight(p,q);
    if(v.a>0 && _Record.Load(int3(q,0)).w==kind && w>.05) {
     float3 c=v.rgb/v.a;lo=min(lo,c);hi=max(hi,c);mean+=c*w;moment+=c*c*w;count+=w;
    }
   }
   mean/=count;float3 sigma=sqrt(max(0,moment/count-mean*mean));
   float3 oldColor=clamp(history.rgb/history.a,max(lo,mean-2*sigma),min(hi,mean+2*sigma));
   float age=min(max(1,stats.x),max(1,_Options.y-1));
   float innovation=abs(Luminance(oldColor)-lum)/max(.05,lum+sqrt(max(0,stats.z-stats.y*stats.y)));
   float w=min(age/(age+1),1-1/max(_Options.y,1))*saturate(support*2)*rcp(1+innovation*.25);
   history.rgb=oldColor*history.a;
   float4 result=lerp(current,history,w);float coverage=min(current.a,result.a);
   o.color=float4(result.rgb*(coverage/max(result.a,1e-6)),coverage);
   o.stats=float4(min(_Options.y,1+stats.x*saturate(support*2)),lerp(lum,stats.y,w),lerp(lum*lum,stats.z,w),rejected);
   return o;
  }
  bool EligibleAt(int2 pixel,float threshold) {
   float raw=_CameraDepthTexture.Load(int3(pixel,0));
   return _CameraGBufferTexture1.Load(int3(pixel,0)).a>=threshold &&
      1/(_ZBufferParams.x*raw+_ZBufferParams.y)<=.999;
  }
  bool ReceiverInRange(int2 pixel) {
   if(_ReceiverRange.x<.5)return true;
   float3 receiver=ViewPosition((pixel+.5)/_Size.xy,_Depth.Load(int3(pixel,0)));
   return dot(receiver,receiver)<=_ReceiverRange.y*_ReceiverRange.y;
  }
  bool ZeroSmoothnessProbeReceiver(int2 pixel) {
   if(_ZeroSmoothnessProbe.x<.5)return false;
   float4 spec=_CameraGBufferTexture1.Load(int3(pixel,0));
   float4 encoded=_CameraGBufferTexture2.Load(int3(pixel,0));float3 n=encoded.xyz*2-1;
   return all(isfinite(spec)) && spec.a==0 && any(spec.rgb!=0) &&
      !all(encoded==0) && all(isfinite(n)) && dot(n,n)>1e-8 && EligibleAt(pixel,0);
  }
  bool Eligible(int2 pixel) {return EligibleAt(pixel,_Options.z) && ReceiverInRange(pixel) && !ZeroSmoothnessProbeReceiver(pixel);}
  bool EnvironmentEligible(int2 pixel) {return EligibleAt(pixel,_ReceiverCutoff.x);}
float2 EncodeReuseNormal(float3 n) {
 n/=max(abs(n.x)+abs(n.y)+abs(n.z),1e-8);
 return n.z>=0?n.xy:(1-abs(n.yx))*float2(n.x>=0?1:-1,n.y>=0?1:-1);
}
float4 ReceiverContext(V i):SV_Target {
 int2 p=TracePixel(i),pixel=Receiver(p);
  // Z retains the exact raw source class; receiver smoothness remains in Point.w.
  return float4(EncodeReuseNormal(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),SurfaceNormal(pixel))),_Record.Load(int3(p,0)).w,WaterResponse(pixel).x);
}
  float4 FillDepthGap(int2 p,float4 current) {
   // Keep legacy pass/attachment indices, but never promote a rejected source
   // colour to full coverage. Compose supplies receiver colour for every miss.
   return current;
  }
  float4 DepthGapFill(V i):SV_Target {int2 p=TracePixel(i);return FillDepthGap(p,_Signal.Load(int3(p,0)));}
  float SmoothnessFilterStrength(float s) {
   float fade=_CoverageFilter.z>_CoverageFilter.y?smoothstep(_CoverageFilter.y,_CoverageFilter.z,s):(s>_CoverageFilter.y?1:0);
   return saturate(_CoverageFilter.x)*lerp(1,saturate(_CoverageFilter.w),fade);
  }
  float4 CoverageDisplay(V i):SV_Target {
   int2 p=TracePixel(i),pixel=Receiver(p);float4 current=_Signal.Load(int3(p,0));
   float4 g=_Guide.Load(int3(p,0));float s=_Point.Load(int3(p,0)).w;
   float strength=SmoothnessFilterStrength(s);
   if(strength<=0 || g.y<=0 || !Eligible(pixel) || (current.a<=0 && _Record.Load(int3(p,0)).z<2))return current;
   float4x4 inverse=SsrRasterMatrix(_InvProjection,4);
   float3 position=ViewPosition((pixel+.5)/_Size.xy,g.y,inverse);
   float3 plane=ReconstructionPlane(pixel,g.y,position,inverse),normal=Decode(g.zw);
   float water=WaterResponse(pixel).x;
   float4 sum=0;float weight=0,alphaMoment=0,luminanceMoment=0,noise=0;
   // A bounded binomial footprint. Misses remain zero observations; reflected
   // source classes are not receiver boundaries. Never search distant donors.
   [loop]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++) {
    int2 q=p+int2(x,y);if(any(q<0)||any(q>=(int2)_Size.zw))continue;
    float4 value=_Signal.Load(int3(q,0)),record=_Record.Load(int3(q,0));
    if((value.a<=0 && record.z<2) || !all(isfinite(value)))continue;
    // Raw exclusion/invalid-normal records already reject ineligible donors;
    // temporal estimates cannot survive those exits. Avoid repeating the
    // camera-depth/material/range eligibility calculation for every neighbour.
    if(_WaterAvailable>.5 && WaterResponse(Receiver(q)).x!=water)continue;
    float3 rejected;float w=(x==0 && y==0)?4:(x==0?2:1)*(y==0?2:1)*ReceiverWeight(pixel,position,g.y,plane,normal,s,q,rejected,inverse);
    if(w<=.001)continue;
    sum+=value*w;weight+=w;alphaMoment+=value.a*value.a*w;
    float lum=Luminance(value.rgb);luminanceMoment+=lum*lum*w;
    if(_CoverageFilterHistory>.5) {
     float4 stats=_TemporalStats.Load(int3(q,0));
     float variance=max(0,stats.z-stats.y*stats.y);
     // Relative temporal radiance variance works in either temporal branch.
     // Cold/rejected histories also lack enough support to preserve specks.
     float instability=stats.x<2?1:saturate(2*sqrt(variance)/max(.05,abs(stats.y)));
     noise+=w*instability;
    }
   }
   if(weight<=0)return current;
   sum/=weight;
   float coverageVariance=max(0,alphaMoment/weight-sum.a*sum.a);
   float lumMean=Luminance(sum.rgb);
   float lumVariance=max(0,luminanceMoment/weight-lumMean*lumMean);
   float variation=saturate(max(sqrt(coverageVariance)/max(.01,sum.a),sqrt(lumVariance)/max(.05,lumMean)));
   float adaptive=_CoverageFilterHistory>.5?noise/weight:variation;
   // A spatially constant estimate needs no blur even if its rays fluctuate.
   return lerp(current,sum,strength*adaptive*variation);
  }
  // Match the intermediate ARGBHalf store/load before cosmetic filling.
  float4 StoredSignal(float4 value) {return f16tof32(f32tof16(value));}
  struct ResolveDisplayOutput {float4 measured:SV_Target0;float4 display:SV_Target1;};
  ResolveDisplayOutput ResolveDisplay(V i) {
   ResolveDisplayOutput o;o.measured=Resolve(i);
   o.display=FillDepthGap(TracePixel(i),StoredSignal(o.measured));return o;
  }
  struct TemporalDisplayOutput {float4 measured:SV_Target0;float4 stats:SV_Target1;float4 display:SV_Target2;float4 anchor:SV_Target3;};
  TemporalDisplayOutput TemporalDisplay(V i) {
   TemporalOutput t=Temporal(i);TemporalDisplayOutput o;o.measured=t.color;o.stats=t.stats;o.anchor=t.anchor;
   o.display=FillDepthGap(TracePixel(i),StoredSignal(t.color));return o;
  }
  float3 Environment(int2 pixel,float3 direction,float3 material) {
   // Exact native final-pass removal, independent of the new reflection signal.
   float2 uv=(pixel+.5)/_Size.xy;
   return Probe(pixel,direction)*saturate(direction.y+_ReflectionBottomShade)*material*
    _CameraGBufferTexture0.Load(int3(pixel,0)).a*_SSRFactor*
    (1-_StencilShadow.SampleLevel(sampler_StencilShadow,uv,0).a);
  }
  float4 CutoffScene(int2 pixel,float4 scene) {
   // No reconstruction or fallback for excluded receivers. Share the native
   // environment-removal formula with EnvironmentRemovedScene (debug 15).
   if(!EnvironmentEligible(pixel) || _ReceiverCutoff.y>=1)return scene;
   float3 normal=UnitNormal(pixel);
   float3 environment=Environment(pixel,WorldDirection(pixel,normal),Material(pixel,normal));
   return float4(lerp(max(scene.rgb-environment,0),scene.rgb,saturate(_ReceiverCutoff.y)),scene.a);
  }
  float4 ZeroSmoothnessProbeScene(int2 pixel,float4 scene) {
   float3 normal=UnitNormal(pixel),direction=WorldDirection(pixel,normal),material=Material(pixel,normal);
   float3 removal=EnvironmentEligible(pixel)?Environment(pixel,direction,material):0;
   if(_UseCapturedContribution>.5 && _CaptureAvailable>.5) {
    float4 captured=_CapturedReflection.Load(int3(pixel,0));
    if(captured.a==1)removal=captured.rgb;
   }
   // Replace the actual/native environment once. Deliberately omit GBuffer0
   // reflection visibility, which suppresses probe lighting on native foliage.
   float receiverDistance=length(ViewPosition((pixel+.5)/_Size.xy,_Depth.Load(int3(pixel,0))));
   float fade=_ZeroSmoothnessProbeFade.z>_ZeroSmoothnessProbeFade.y?
      smoothstep(_ZeroSmoothnessProbeFade.y,_ZeroSmoothnessProbeFade.z,receiverDistance):
      (receiverDistance>=_ZeroSmoothnessProbeFade.y?1:0);
   float contribution=lerp(saturate(_ZeroSmoothnessProbeFade.x),saturate(_ZeroSmoothnessProbe.y),fade);
   float3 addition=Probe(pixel,direction)*material*saturate(direction.y+_ReflectionBottomShade)*
      _SSRFactor*(1-_StencilShadow.SampleLevel(sampler_StencilShadow,(pixel+.5)/_Size.xy,0).a)*contribution;
   return float4(max(scene.rgb-removal,0)+addition,scene.a);
  }
  float3 TraceTerminationColour(float4 record) {
   if(record.w==1)return float3(0,1,0); // geometry
   if(record.w==2)return float3(0,0,1); // sky
   int reason=(int)record.z;
   if(reason==1)return float3(1,1,1); // invalid receiver normal
   if(reason==2)return float3(.6,0,1); // invalid GGX hemisphere
   if(reason==3)return float3(.45,.2,0); // short/degenerate ray
   if(reason==4)return float3(1,0,0); // self/near-receiver hit
   if(reason==5)return float3(1,0,1); // rejected hit normal
   if(reason==6)return float3(0,1,1); // side/near/offscreen exit
   if(reason==8)return float3(1,1,0); // iteration budget
   if(reason==9)return float3(.5,.5,0); // unresolved depth crossing
   if(reason==10)return float3(0,.3,.5); // background not confirmed
   if(reason==11)return float3(1,.2,.2); // continuation allowance exhausted
   return float3(.2,.2,.2); // excluded receiver
  }
  float4 ReconstructSignal(int2 pixel,float3 normal,Texture2D<float4> inputSignal,out float2 coverage,out float3 loss,out float receiverWater) {
   coverage=0;loss=0;receiverWater=WaterResponse(pixel).x;
   if(_NativeReconstruction.x>.5) {
    float2 uv=(pixel+.5)/_Size.xy;
    coverage=float2(_Guide.SampleLevel(sampler_Signal,uv,0).x,_UnfilledSignal.SampleLevel(sampler_Signal,uv,0).a);
    float s=_CameraGBufferTexture1.Load(int3(pixel,0)).a,r=1-s*s*s;
    float mip=clamp(r*r*(_NativeReconstruction.y-1)+r,0,_NativeReconstruction.y);
    return inputSignal.SampleLevel(sampler_Signal,uv,mip);
   }
   float2 grid=(pixel+.5)*_Size.zw/_Size.xy-.5;
   int2 corner=(int2)floor(grid);float2 f=frac(grid);
   float depth=_Depth.Load(int3(pixel,0));
   float4x4 inverse=SsrRasterMatrix(_InvProjection,4);
   float3 position=ViewPosition((pixel+.5)/_Size.xy,depth,inverse);
   float4 spec=_CameraGBufferTexture1.Load(int3(pixel,0));float4 sum=0;
   float3 plane=ReconstructionPlane(pixel,depth,position,inverse);float support=0;
   // Positive compact footprint. This smooths
   // the undersampled trace grid without introducing coarse hole extrapolation.
   float radius=1;
   // Only these four positions have positive weight at radius one.
   [unroll] for(int y=0;y<2;y++) [unroll] for(int x=0;x<2;x++) {
    int2 q=clamp(corner+int2(x,y),0,(int2)_Size.zw-1);
    float weight=max(0,radius-abs(x-f.x))*max(0,radius-abs(y-f.y));
    if(weight<=0)continue;
    float4 signal=inputSignal.Load(int3(q,0)),guide=_Guide.Load(int3(q,0));
    // Trace texels can straddle a water edge at lower resolution. Keep the
    // water-only estimate off dry full-resolution pixels even when their
    // depth/normal happen to pass the usual receiver compatibility checks.
    if(receiverWater!=WaterResponse(Receiver(q)).x)
     signal=_UnfilledSignal.Load(int3(q,0));
    // Same bilinear footprint for all stages, before compatibility rejection.
    if(x>=0 && x<2 && y>=0 && y<2)
     coverage+=float2(guide.x,_UnfilledSignal.Load(int3(q,0)).a)*(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y);
    float3 rejected;
    float compatible=ReceiverWeight(pixel,position,depth,plane,normal,spec.a,q,rejected,inverse);
    loss+=rejected*signal.a*weight;
    support+=weight*compatible;
    sum+=signal*(weight*compatible);
   }
   // Geometry, sky and inferred contributions share the same continuous
   // display footprint. Interpolate premultiplied colour AND coverage; never
   // renormalize one class to fill another class's missing footprint weight.
   // Normalize the receiver footprint, NOT hit coverage. Compatible misses
   // count in support with zero signal, so real ray failures remain fallback.
   // A neighbouring land/weapon receiver must not dilute valid water coverage.
   return support>1e-6?sum/support:0;
  }
  float4 ReconstructSignal(int2 pixel,float3 normal,Texture2D<float4> inputSignal,out float2 coverage,out float3 loss) {
   float water;return ReconstructSignal(pixel,normal,inputSignal,coverage,loss,water);
  }
  float4 Compose(V i):SV_Target {
   int2 pixel=min((int2)i.p.xy,(int2)_Size.xy-1);
   int2 p=min((int2)((pixel+.5)*_Size.zw/_Size.xy),(int2)_Size.zw-1);
   float4 scene=_MainTex.Load(int3(pixel,0)),signal=_Signal.Load(int3(p,0)),record=_Record.Load(int3(p,0));
   if(_Options.w==40) {
    if(_WaterNormalsEnabled<.5)return float4(0,0,0,1);
    float4 wn=_WaterNormal.Load(int3(p,0));
    if(wn.w<0)return float4(0,0,0,1);
    float angle=acos(clamp(dot(UnitNormal(Receiver(p)),wn.xyz),-1,1));
    return float4(saturate(angle*10),saturate(wn.w*100),1,1);
   }
   if(_Options.w==25)return float4(TraceTerminationColour(record),1);
   float4 traceInfo=_TraceInfo.Load(int3(p,0));int traceFlags=(int)traceInfo.w;
   if(_Options.w==30) {
    float3 c=0;
    if((traceFlags&64)!=0)c=float3(0,1,1); // sky after completed continuation
    else if((traceFlags&4)!=0)c=(traceFlags&3)==3?float3(1,1,1):(traceFlags&2)!=0?float3(1,0,1):float3(0,1,0);
    else if((traceFlags&8)!=0)c=float3(1,0,0);
    else if((traceFlags&3)!=0)c=float3(1,1,0);
    else if((traceFlags&16)!=0)c=float3(1,.5,0);
    return float4(c,1);
   }
   if(_Options.w==31)return float4(saturate(traceInfo.xyz/max(_ContinuationLimits.xyz,1e-5)),1);
   if(_Options.w==32)return float4(traceFlags!=0?_TraceStop.Load(int3(p,0)).xy:0,0,1);
   if(_Options.w==34 || _Options.w==35) {
    float4 dh=_DepthHit.Load(int3(p,0));int flags=(int)dh.x;
    if(record.w!=1 || flags==0)return float4(0,0,0,1);
    // Accepted-only: green bracketed front-depth crossing, orange thickness-
    // only match, magenta candidate UV crossed out of its tested depth cell.
    // Magenta marks the original source UV leaving the tested depth cell.
    if(_Options.w==34)return float4((flags&4)!=0?float3(1,0,1):(flags&1)!=0?float3(0,1,0):float3(1,.4,0),1);
    float applied=(flags&2)!=0?lerp(dh.y,1,saturate(_ThicknessConfidenceFloor)):dh.y;
    return float4(dh.y,applied,_Guide.Load(int3(p,0)).x,1);
   }
   if(_Options.w==0 && ZeroSmoothnessProbeReceiver(pixel))return ZeroSmoothnessProbeScene(pixel,scene);
   if(_Options.w==0 && !Eligible(pixel))return CutoffScene(pixel,scene);
   float3 normal=UnitNormal(pixel),direction=WorldDirection(pixel,normal),material=Material(pixel,normal);
   float3 probe=Probe(pixel,direction),environment=EnvironmentEligible(pixel)?Environment(pixel,direction,material):0;
   float2 coverage;float3 loss;
   signal=ReconstructSignal(pixel,normal,_Signal,coverage,loss);
   float confidence=Eligible(pixel)?saturate(signal.a):0;
   if(_Options.w==36) {
    float2 unusedCoverage;float3 unusedLoss;
    float original=saturate(ReconstructSignal(pixel,normal,_UnfilledSignal,unusedCoverage,unusedLoss).a);
    return float4(Eligible(pixel)?float3(original,max(0,confidence-original),1-confidence):0,1);
   }
   if(_Options.w==26)return float4(Eligible(pixel)?float3(coverage,confidence):0,1);
   if(_Options.w==27)return float4(Eligible(pixel)?loss:0,1);
   // Diagnostic channels separate missing trace coverage, resolve/filter loss,
   // and reconstruction loss. Production reconstruction/composition is unchanged.
   if(_Options.w==33)return float4(Eligible(pixel)?float3(1-saturate(coverage.x),max(0,coverage.x-coverage.y),max(0,coverage.y-confidence)):0,1);
   float4 guide=_Guide.Load(int3(p,0));
   float3 chosen=signal.a>1e-6?signal.rgb/signal.a:0;
   int view=(int)_Options.w;
   bool useCapture=_UseCapturedContribution>.5;
   float4 captured=0;
   if(useCapture || (view>=18 && view<=24))captured=_CaptureAvailable>.5?_CapturedReflection.Load(int3(pixel,0)):0;
   // Production must remain usable while the ambient producer initializes or
   // is unavailable. Explicit capture diagnostics below retain warning colours.
   float3 removal=useCapture && captured.a==1?captured.rgb:environment;
   float3 baseline=Eligible(pixel)?max(scene.rgb-removal,0):scene.rgb;
   // Unmeasured coverage remains available to receiver-colour fallback;
   // rejected foreground source colours never supply synthetic coverage.
   float3 fallback=Eligible(pixel)?saturate(_FallbackStrength)*(1-confidence)*ReceiverContribution(pixel):0;
   float2 water=WaterResponse(pixel);
   if(view==29)return float4(water.x,water.x*water.y,0,1);
   float3 added=confidence*chosen*ReflectionMaterial(pixel)+fallback;
   // Capture is the producer's unlit surface RGB, never post-ambient scene
   // colour or an estimate formed by subtracting lighting from that scene.
   float4 surface=_PreAmbientSurface.Load(int3(pixel,0));
   float influence=water.x*(surface.a>.5?saturate(_SurfaceColourInfluence):0);
   float3 tint=lerp(float3(1,1,1),saturate(surface.rgb),influence);
   added*=tint;fallback*=tint;
   float3 delta=baseline+added-scene.rgb;
   if(view>=18 && view<=24) {
    // Cyan = no producer write; magenta = unsupported multiple producer writes.
    if(captured.a==0)return float4(0,1,1,1);
    if(captured.a!=1)return float4(1,0,1,1);
    if(view==18)return float4(Eligible(pixel)?baseline+added:scene.rgb,scene.a);
    if(view==19)return float4(Eligible(pixel)?captured.rgb:0,1);
    if(view==20)return float4(Eligible(pixel)?.5+captured.rgb-environment:float3(.5,.5,.5),1);
    if(view==21)return float4(Eligible(pixel)?max(scene.rgb-captured.rgb,0):scene.rgb,scene.a);
    if(view==22)return float4(Eligible(pixel)?max(scene.rgb-captured.rgb,0)+added:scene.rgb,scene.a);
    precise float3 restored=_ExactResidual.Load(int3(pixel,0)).rgb+captured.rgb;
    if(view==23)return float4(restored,scene.a);
    if(view==24)return float4(abs(restored-scene.rgb)*10000,1);
   }
   if(view==1)return float4(record.w==1?1:0,record.w==2?1:0,0,1);
   if(view==2)return float4(record.w==1?(record.xy+.5)/_Size.xy:record.xy,0,1);
   if(view==3) {float3 hit=_Point.Load(int3(p,0)).xyz;return record.w==1?float4(-hit.z/100,length(hit-ViewPosition((pixel+.5)/_Size.xy,guide.y))/100,0,1):float4(0,0,0,1);}
   if(view==4)return float4(record.w==1?_Raw.Load(int3(p,0)).rgb:0,1);
   if(view==5) {
    float3 ray=_Point.Load(int3(p,0)).xyz;
    if(record.w==1)ray-=ViewPosition((Receiver(p)+.5)/_Size.xy,guide.y);
    float3 skyDirection=dot(ray,ray)>1e-8?normalize(mul((float3x3)SsrRasterMatrix(_CameraToWorld,8),ray)):direction;
    return float4(TodReflectedSky(skyDirection),1);
   }
   if(view==6)return float4(probe,1);
   if(view==7)return float4(record.w==1?float3(0,1,0):record.w==2?float3(0,0,1):record.w<0?float3(1,0,0):float3(.5,.5,.5),1);
   if(view==8)return float4(confidence.xxx,1);
   if(view==9)return float4(confidence>0?chosen:0,1);
   if(view==10)return float4(length(_CameraGBufferTexture2.Load(int3(pixel,0)).xyz*2-1).xxx,1);
   if(view==11)return float4(normal*.5+.5,1);
   if(view==12)return float4(ReflectionMaterial(pixel),1);
   if(view==13)return float4(environment,1);
   if(view==14)return float4(.5+delta,1); // signed delta around neutral gray
   if(view==15)return float4(EnvironmentEligible(pixel)?max(scene.rgb-environment,0):scene.rgb,scene.a);
   if(view==16)return float4(added,1);
   if(view==17)return float4(0,0,0,1); // Retained enum index for saved debug settings; fallback retired.
   if(view==28)return float4(fallback,1);
   return float4(baseline+added,scene.a);
  }
  // Separate entry point keeps debug-only paths out of normal rendering.
  float4 ComposeNormal(V i):SV_Target {
   int2 pixel=min((int2)i.p.xy,(int2)_Size.xy-1);
   float4 scene=_MainTex.Load(int3(pixel,0));
   if(ZeroSmoothnessProbeReceiver(pixel))return ZeroSmoothnessProbeScene(pixel,scene);
   if(!Eligible(pixel))return CutoffScene(pixel,scene);
   float3 normal=UnitNormal(pixel);float2 coverage;float3 loss;float water;
   float4 signal=ReconstructSignal(pixel,normal,_Signal,coverage,loss,water);
   float confidence=saturate(signal.a);
   float4 captured=0;
   if(_UseCapturedContribution>.5 && _CaptureAvailable>.5)captured=_CapturedReflection.Load(int3(pixel,0));
   float3 removal;
   if(_UseCapturedContribution>.5 && captured.a==1)removal=captured.rgb;
   else removal=Environment(pixel,WorldDirection(pixel,normal),Material(pixel,normal));
   float3 baseline=max(scene.rgb-removal,0);
   float3 chosen=signal.a>1e-6?signal.rgb/signal.a:0;
   float3 response=ReflectionMaterial(pixel);
   float3 fallback=saturate(_FallbackStrength)*(1-confidence)*(ReceiverRadiance(pixel)*response);
   float3 added=confidence*chosen*response+fallback;
   float4 surface=_PreAmbientSurface.Load(int3(pixel,0));
   float influence=water*(surface.a>.5?saturate(_SurfaceColourInfluence):0);
   added*=lerp(float3(1,1,1),saturate(surface.rgb),influence);
   return float4(baseline+added,scene.a);
  }
  float4 MaterialContext(V i):SV_Target {return _CameraGBufferTexture1.Load(int3(Receiver(TracePixel(i)),0));}
  float4 ExactSubtract(V i):SV_Target {
   int2 pixel=min((int2)i.p.xy,(int2)_Size.xy-1);
   float4 scene=_MainTex.Load(int3(pixel,0));
   precise float3 residual=scene.rgb-_CapturedReflection.Load(int3(pixel,0)).rgb;
   return float4(residual,scene.a); // no clamp: round-trip must expose arithmetic loss
  }
  ENDHLSL
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Resolve
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Temporal
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Compose
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment MaterialContext
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment ExactSubtract
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment ReceiverContext
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment ReplayInput
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment DepthGapFill
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment ComposeNormal
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment ResolveDisplay
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment TemporalDisplay
   ENDHLSL }
  Pass { HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment CoverageDisplay
   ENDHLSL }
 }
}
