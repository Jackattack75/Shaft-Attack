// Bullet tracers and muzzle flashes (Shaft Attack!). Additive glow, no texture:
//  - lines (Radial 0): bright in the middle of the streak, soft at the edges (uv.y across the line), the colour and
//    fade come from the vertex colour (LineRenderer gradient: invisible tail -> bright head)
//  - flashes (Radial 1): a round soft blob on each quad (uv from the middle)
Shader "ShaftAttack/Tracer"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 3
        _Softness ("Edge softness", Range(0.05, 1)) = 0.7
        [Toggle] _Radial ("Radial (muzzle flash)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Tracer"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
                half _Softness;
                half _Radial;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half edge;
                if (_Radial > 0.5)
                {
                    half r = saturate(length(i.uv * 2.0 - 1.0));
                    edge = saturate((1.0 - r) / _Softness);
                    edge *= edge;
                }
                else
                {
                    half across = abs(i.uv.y * 2.0 - 1.0);
                    edge = saturate((1.0 - across) / _Softness);
                }
                half core = edge * edge * edge;
                half3 c = lerp(i.color.rgb, half3(1.0, 1.0, 1.0), core * 0.65) * edge * i.color.a * _Intensity;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
}
