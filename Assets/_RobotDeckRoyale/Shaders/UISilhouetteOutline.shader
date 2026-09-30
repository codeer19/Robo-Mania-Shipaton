// UI image shader that draws a clean, even outline around the opaque silhouette
// of its texture (the robot preview renders into a transparent RenderTexture).
// Only the outer contour is outlined - parts inside the silhouette never get
// their own lines. Samples two rings of 12 taps; cheap enough for WebGL/mobile.
Shader "RoboMania/UI Silhouette Outline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Colour", Color) = (0.03, 0.05, 0.12, 1)
        _OutlineWidth ("Outline Width (texels)", Float) = 4

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float4 _ClipRect;

            v2f vert (appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float2 step = _MainTex_TexelSize.xy * _OutlineWidth;
                float reach = 0;
                // Outer and half-radius rings, 12 directions each (30 degrees apart).
                const float2 dirs[12] = {
                    float2(1, 0), float2(0.866, 0.5), float2(0.5, 0.866), float2(0, 1),
                    float2(-0.5, 0.866), float2(-0.866, 0.5), float2(-1, 0), float2(-0.866, -0.5),
                    float2(-0.5, -0.866), float2(0, -1), float2(0.5, -0.866), float2(0.866, -0.5) };
                for (int k = 0; k < 12; k++)
                {
                    reach = max(reach, tex2D(_MainTex, i.uv + dirs[k] * step).a);
                    reach = max(reach, tex2D(_MainTex, i.uv + dirs[k] * step * 0.5).a);
                }
                float edge = saturate(reach - c.a) * _OutlineColor.a;
                fixed4 result;
                result.a = saturate(c.a + edge);
                // The stage clears to transparent black, so bilinear edge texels are
                // already premultiplied by coverage: add, then un-premultiply.
                result.rgb = (c.rgb + _OutlineColor.rgb * edge) / max(result.a, 0.0001);
                result *= i.color;
                result.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                return result;
            }
            ENDCG
        }
    }
}
