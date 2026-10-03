// Signal conversion only. All REBLUR algorithms execute upstream DXBC.
#include "NRD.hlsli"
cbuffer AdapterSettings : register(b0) { uint2 size; uint failure; uint unused; }
Texture2D<float4> input0 : register(t0);
Texture2D<float4> input1 : register(t1);
Texture2D<float4> input2 : register(t2);
Texture2D<float4> input3 : register(t3);
RWTexture2D<float4> out0 : register(u0);
RWTexture2D<float> out1 : register(u1);
RWTexture2D<float4> out2 : register(u2);
RWTexture2D<float4> out3 : register(u3);
RWTexture2D<float4> out4 : register(u4);

[numthreads(8,8,1)] void Pack(uint2 p : SV_DispatchThreadID) {
    if(any(p>=size))return;
    float4 signal=input0[p], normal=input1[p], receiver=input2[p];
    // receiver: positive eye depth, measured hit distance, eligibility, water ID.
    // Unknown SSR misses have no measured distance (0). They remain zero
    // coverage observations, not confirmed sky and not skipped trace pixels.
    bool eligible=receiver.z>0;
    float hit=eligible?REBLUR_FrontEnd_GetNormHitDist(receiver.y,receiver.x,float3(3,.1,20),normal.w):0;
    out0[p]=NRD_FrontEnd_PackNormalAndRoughness(normal.xyz,normal.w,receiver.w);
    out1[p]=eligible?receiver.x:1e7;
    out2[p]=float4(input3[p].xy,0,0); // already previous-current UV, no jitter
    out3[p]=REBLUR_FrontEnd_PackRadianceAndNormHitDist(eligible?signal.rgb:0,hit,true);
    out4[p]=REBLUR_FrontEnd_PackRadianceAndNormHitDist(eligible?signal.aaa:0,hit,true);
}

[numthreads(8,8,1)] void Unpack(uint2 p : SV_DispatchThreadID) {
    if(any(p>=size))return;
    if(failure!=0){out0[p]=input3[p];return;}
    if(input2[p].z<=0){out0[p]=0;return;}
    float3 colour=REBLUR_BackEnd_UnpackRadianceAndNormHitDist(input0[p]).rgb;
    float coverage=REBLUR_BackEnd_UnpackRadianceAndNormHitDist(input1[p]).r;
    // No authority is assigned to inferred coverage. Raw Record/Point remain
    // untouched; alpha here is exclusively the display estimator's coverage.
    coverage=saturate(coverage);
    out0[p]=coverage>1e-5?float4(max(colour,0),coverage):0;
}
