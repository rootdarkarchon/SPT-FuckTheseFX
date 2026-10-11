Shader "FogSix/Glass/Transparent Bumped Specular" {
    Properties { _FogSixGlass ("FogSix shader-swap canary", Float) = 1 }
    SubShader { Tags { "Queue"="Transparent" } Pass { Color (1,0,1,1) } }
}
