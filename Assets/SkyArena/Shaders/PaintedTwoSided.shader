Shader "SkyArena/Painted Two Sided"
{
    Properties
    {
        _Color ("Paint color", Color) = (1,1,1,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.26
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionColor ("Warm light", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Off
        CGPROGRAM
        #pragma surface surf StandardSpecular fullforwardshadows addshadow
        #pragma target 3.0
        fixed4 _Color;
        half _Glossiness;
        fixed4 _EmissionColor;
        struct Input { float facing : VFACE; };
        void surf (Input IN, inout SurfaceOutputStandardSpecular o)
        {
            o.Albedo = _Color.rgb;
            o.Specular = half3(0.02,0.02,0.02);
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
            o.Emission = _EmissionColor.rgb;
            o.Normal = float3(0,0,IN.facing >= 0 ? 1 : -1);
        }
        ENDCG
    }
    Fallback "Diffuse"
}
