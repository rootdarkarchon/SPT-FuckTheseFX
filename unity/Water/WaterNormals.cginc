// Phase two changes only the normal field fed into the validated native surface.
// Keep map textures, authored directions/scales/weights and downstream bumpiness.
float4 _FTFXWaterNormalControls; // size, strength, fine-detail strength, speed (integrated by CPU)
float _FTFXWaterAnimationTime; // accumulated scaled seconds / 20; shared by main and optic
float2 WaterNormalSlope(float4 packed)
{
    // Match EFT's RG/AG normal decoding, then combine slopes instead of multiplying normals.
    float2 horizontal = float2(packed.x * packed.w, packed.y) * 2.0 - 1.0;
    float vertical = sqrt(1.0 - saturate(dot(horizontal, horizontal)));
    return horizontal / max(vertical, 0.35);
}

float2 WaterRotateMedium(float2 value)
{
    // 67.5 degrees: avoid aligned copies of the same cellular texture.
    return float2(0.38268343 * value.x - 0.92387953 * value.y,
                  0.92387953 * value.x + 0.38268343 * value.y);
}

float WaterBandVisibility(float2 uv, float2 dimensions, float fadeStart, float fadeEnd)
{
    float2 texels = max(dimensions, float2(1,1));
    float footprint = max(length(ddx(uv) * texels), length(ddy(uv) * texels));
    return 1.0 - smoothstep(fadeStart, fadeEnd, footprint);
}

float3 WaterMultiscaleNormal(float3 world)
{
    float inverseSize = rcp(max(_FTFXWaterNormalControls.x, 0.25));
    float motionTime = _FTFXWaterAnimationTime;
    float2 broadUV = (world.xz + motionTime * _ValsNormA.xy) * (_ValsNormA.z * 0.45 * inverseSize);
    float2 mediumUV = WaterRotateMedium(world.xz + motionTime * _ValsNormB.xy)
                    * (_ValsNormB.z * 0.85 * inverseSize) + float2(0.37, 0.61);
    float2 detailUV = (world.xz + motionTime * _ValsNormD.xy * 0.60) * (_ValsNormD.z * 0.70 * inverseSize);

    float2 broad = WaterNormalSlope(_BumpMap.SampleBias(sampler_BumpMap, broadUV, 0.5));
    float2 medium = WaterNormalSlope(_BumpMap.SampleBias(sampler_BumpMap, mediumUV, 0.75));
    // Rotate the sampled slopes back into world XZ along with the rotated UV domain.
    medium = float2(0.38268343 * medium.x + 0.92387953 * medium.y,
                   -0.92387953 * medium.x + 0.38268343 * medium.y);
    // Pinned Customs detail texture has native mip bias -2. The local +3 bias
    // softens this read without changing a shared texture or its other users.
    float2 detail = WaterNormalSlope(_BumpMapDetails.SampleBias(sampler_BumpMapDetails, detailUV, 3.0));

    // Fade bands gradually by their own pixel footprint. Broad cellular shapes
    // survive coarser mips than medium breakup and fine ripples. Zoomed optics
    // retain resolved detail without inheriting a main-camera distance cutoff.
    // Query the bound MPB texture; material default texel-size values can refer
    // to a different texture than the native owner's property block supplies.
    uint baseWidth, baseHeight, detailWidth, detailHeight;
    _BumpMap.GetDimensions(baseWidth, baseHeight);
    _BumpMapDetails.GetDimensions(detailWidth, detailHeight);
    float broadVisibility = WaterBandVisibility(broadUV, float2(baseWidth, baseHeight), 16.0, 64.0);
    float mediumVisibility = WaterBandVisibility(mediumUV, float2(baseWidth, baseHeight), 8.0, 32.0);
    float detailVisibility = WaterBandVisibility(detailUV, float2(detailWidth, detailHeight), 2.0, 8.0);
    float3 weights = max(float3(_ValsNormA.w, _ValsNormB.w, _ValsNormD.w), 0.0)
                     * float3(0.65, 1.00, 0.30);
    // Normalize authored weights before fading, never the surviving bands:
    // removing unresolved detail must not amplify broad reflective facets.
    weights /= max(weights.x + weights.y + weights.z, 0.0001);
    float2 slope = broad * (weights.x * broadVisibility) + medium * (weights.y * mediumVisibility)
                 + detail * (weights.z * detailVisibility * _FTFXWaterNormalControls.z);
    slope *= _FTFXWaterNormalControls.y;
    return normalize(float3(slope.x, 1.0, slope.y));
}
