Shader "Hidden/FTFX/Tests/TodReflection" {
 SubShader { Pass { ZTest Always ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma target 5.0
 #pragma vertex Vert
 #pragma fragment Frag
 #include "TodReflection.hlsl"
 float4 _AuditDirection;float _AuditFog;
 struct V {float4 p:SV_POSITION;};
 V Vert(float3 p:POSITION){V o;o.p=float4(p.xy,0,1);return o;}
 float4 Frag(V i):SV_Target{return float4(TodAtmosphere(normalize(_AuditDirection.xyz),_AuditFog),1);}
 ENDHLSL
 } }
}
