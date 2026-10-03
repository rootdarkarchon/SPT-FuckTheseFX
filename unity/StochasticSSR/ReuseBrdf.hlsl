// GGX VNDF outgoing-direction PDF. Fresnel/native material response is applied
// once by the compositor, not baked into borrowed incident radiance.
float ReuseG1(float cosine,float alpha) {
    return cosine>0 ? 2*cosine/(cosine+sqrt(alpha*alpha+(1-alpha*alpha)*cosine*cosine)) : 0;
}
float ReusePdf(float3 v,float3 n,float3 l,float smoothness) {
    float nv=dot(n,v),nl=dot(n,l);
    if(nv<=0 || nl<=0 || dot(v+l,v+l)<1e-10)return 0;
    float alpha=max(1e-4,(1-smoothness)*(1-smoothness));
    float nh=saturate(dot(n,normalize(v+l)));
    float d=nh*nh*(alpha*alpha-1)+1;
    return alpha*alpha/(3.141592654*max(d*d,1e-20))*ReuseG1(nv,alpha)/(4*nv);
}
