// EFT 40743 TOD math, reconstructed from pinned SSR resolve DXBC and its binding metadata.
// Sky evaluation is separate from the native distance-dependent blend toward that sky.
float3 TOD_SunSkyColor,TOD_MoonSkyColor,TOD_GroundColor,TOD_MoonHaloColor;
float3 TOD_LocalSunDirection,TOD_LocalMoonDirection,TOD_kBetaMie;
float4 TOD_kSun,TOD_k4PI,TOD_kRadius,TOD_kScale;
float TOD_Contrast,TOD_ScatteringBrightness,TOD_Fogginess,TOD_MoonHaloPower;
float4x4 TOD_World2Sky;
float TodScale(float cosine)
{
    float x=1-cosine;
    return exp(-.00287+x*(.459+x*(3.83+x*(-6.8+x*5.25))));
}
bool TodAvailable() { return TOD_kRadius.x>0 && TOD_kRadius.w>TOD_kRadius.y && TOD_kScale.x>0; }
float3 TodAtmosphere(float3 dir,float scatteringAmount)
{
    // Preserve EFT's soft horizon and four atmosphere samples. Phase functions
    // use the original direction; integration uses its soft-clamped elevation.
    float elevation=dir.y>.03125?dir.y:(dir.y>-.03125?8*(dir.y+.03125)*(dir.y+.03125):0);
    float3 ray=float3(dir.x,elevation,dir.z);
    float startHeight=TOD_kRadius.x+TOD_kScale.w;
    float segment=(sqrt(max(0,TOD_kRadius.w+TOD_kRadius.y*elevation*elevation-TOD_kRadius.y))-TOD_kRadius.x*elevation)*.25;
    float startOffset=exp(-TOD_kScale.w*TOD_kScale.z)*TodScale(elevation);
    float3 samplePoint=float3(0,startHeight,0)+ray*(segment*.5);
    float3 integral=0;
    [unroll] for(int j=0;j<4;j++)
    {
        float h=max(length(samplePoint),1e-6);
        float density=exp(TOD_kScale.z*(TOD_kRadius.x-h));
        float scatter=.25*(startOffset+density*(TodScale(dot(TOD_LocalSunDirection,samplePoint)/h)-TodScale(dot(ray,samplePoint)/h)));
        integral+=exp(-scatter*(TOD_k4PI.rgb+TOD_k4PI.w))*density*segment*TOD_kScale.x*scatteringAmount;
        samplePoint+=ray*segment;
    }
    float cosine=dot(TOD_LocalSunDirection,dir);
    float rayleigh=.75*(1+cosine*cosine);
    float mie=TOD_kBetaMie.x*(1+cosine*cosine)/pow(max(TOD_kBetaMie.y+TOD_kBetaMie.z*cosine,1e-6),1.5);
    float3 sky=integral*TOD_SunSkyColor*(TOD_kSun.rgb*rayleigh+TOD_kSun.w*mie);
    sky+=TOD_MoonSkyColor*(1-dir.y);
    sky+=TOD_MoonHaloColor*pow(max(dot(dir,TOD_LocalMoonDirection),0),TOD_MoonHaloPower);
    sky=lerp(sky,dot(sky,1.0.xxx)*.333,TOD_Fogginess);
    float ground=saturate(-.8*dir.y);ground=ground*ground*(3-2*ground);
    sky=lerp(sky,TOD_GroundColor,ground)*TOD_ScatteringBrightness;
    return 2*pow(max(sky,0),TOD_Contrast);
}
float3 TodReflectedSky(float3 worldDirection)
{
    float3 local=mul((float3x3)TOD_World2Sky,worldDirection);
    // An absent TOD transform/model is not permission to invent sky radiance.
    return TodAvailable() && dot(local,local)>1e-8?TodAtmosphere(normalize(local),1):0;
}
