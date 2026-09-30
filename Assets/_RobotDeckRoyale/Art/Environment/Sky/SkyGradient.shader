Shader "SkyArena/Sky Gradient"
{
    Properties
    {
        _ZenithColor  ("Zenith", Color)          = (0.16, 0.53, 0.88, 1)
        _HorizonColor ("Horizon", Color)         = (0.72, 0.91, 0.99, 1)
        _NadirColor   ("Below Horizon", Color)   = (0.42, 0.74, 0.94, 1)
        _HorizonFalloff ("Horizon Falloff", Range(0.2, 6)) = 1.6
        _Exposure     ("Exposure", Range(0.5, 3)) = 1.0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionOS : TEXCOORD0;
            };

            float4 _ZenithColor;
            float4 _HorizonColor;
            float4 _NadirColor;
            float _HorizonFalloff;
            float _Exposure;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float height = normalize(input.directionOS).y;

                // Two gradients meeting at the horizon. Splitting them keeps the
                // horizon band itself bright, which is what sells the sky as
                // "above the clouds" rather than a flat blue wash.
                float blend = pow(saturate(abs(height)), 1.0 / _HorizonFalloff);
                float3 target = height >= 0.0 ? _ZenithColor.rgb : _NadirColor.rgb;
                float3 colour = lerp(_HorizonColor.rgb, target, blend);

                return half4(colour * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
