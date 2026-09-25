#ifndef FTFX_BENT_NORMALS_INCLUDED
#define FTFX_BENT_NORMALS_INCLUDED
// Intel's packed RGB direction; visibility occupies the high byte.
float3 FTFXBentWorld(uint packed, float4x4 toWorld, float3 fallback)
{
    float3 n = float3(packed & 255, (packed >> 8) & 255, (packed >> 16) & 255) * (2.0/255.0) - 1;
    float lengthSquared = dot(n,n);
    if (!(lengthSquared > .01)) return fallback;
    n = mul((float3x3)toWorld,n * rsqrt(lengthSquared));
    lengthSquared = dot(n,n);
    if (!(lengthSquared > .01)) return fallback;
    n *= rsqrt(lengthSquared);
    // Reject invalid/opposite-hemisphere estimates at discontinuities.
    return dot(n,fallback) > 0 ? n : fallback;
}
#endif
