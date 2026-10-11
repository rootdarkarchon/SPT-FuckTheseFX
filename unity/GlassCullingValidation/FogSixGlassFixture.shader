// Classification fixture only; does not model FogSix's unavailable glass rendering passes.
Shader "FogSix/Glass/Transparent Reflective Specular" {
    Properties { _FogSixGlass ("FogSix shader-swap canary", Float) = 1 }
    SubShader { Tags { "Queue"="Transparent" } Pass { Color (1,0,1,1) } }
}
