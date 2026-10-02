// Shaft Attack - hand-painted rock, mapped with world-space TRIPLANAR projection.
//
// Surface Nets terrain has no UVs (making some would mean splitting every shared vertex, which
// breaks smooth shading), so the texture is projected from world position along X, Y and Z and
// blended by the surface normal. The blend is sharp, so each flat fracture face takes essentially
// one projection instead of a blurry double image.
//
// Every wall projection keeps image-up = world-up, so the lit lower lip painted onto each crack
// stays on the lower edge wherever the crack ends up.
//
// Sandstone's sediment bands are NOT in its texture: they come from world height here, so they
// line up across a whole wall like real strata and show as contours on floors. _Strata turns
// them on (1 on the sandstone material, 0 elsewhere).
//
// Two things stop the 6 m texture looking like wallpaper on big walls and floors:
//  - Anti-tiling (_AntiTile): the surface is split into irregular patches, each showing its own
//    copy of the texture - shifted, turned up to _VariantRotation degrees and sometimes mirrored
//    left-right - cross-faded at soft seams. Turning the copies also stops the painted strokes all
//    leaning the same way, which tiled into a faint crosshatch on big flat walls.
//  - Large-scale variation (_MacroStrength): slow light/dark and warm/cool drift from world-space
//    noise that never repeats. The textures themselves are kept flat at that scale on purpose.

Shader "ShaftAttack/Triplanar Rock"
{
    Properties
    {
        [MainTexture] _BaseMap ("Rock Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _TileMeters ("Metres Per Tile", Float) = 6
        _Sharpness ("Triplanar Blend Sharpness", Range(1, 16)) = 6
        _Smoothness ("Smoothness", Range(0, 1)) = 0.04
        _Strata ("Sediment Bands (0 = off)", Range(0, 1)) = 0
        _StrataHeight ("Band Height (m)", Float) = 0.55
        _AntiTile ("Break Up Tiling (0 = off)", Range(0, 1)) = 1
        _VariantScale ("Patch Frequency (per tile)", Float) = 0.45
        _VariantRotation ("Patch Rotation (degrees)", Range(0, 90)) = 35
        _MacroStrength ("Large-Scale Variation", Range(0, 1)) = 0.35
        _MacroScale ("Large-Scale Size (m)", Float) = 11

        // Declared so the Lit fallback's shadow and depth passes find what they expect.
        _Cutoff ("Alpha Clip", Range(0, 1)) = 0.5
        _Cull ("Cull", Float) = 2
        _ZWrite ("ZWrite", Float) = 1
        _Surface ("Surface", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _TileMeters;
                float _Sharpness;
                float _Smoothness;
                float _Strata;
                float _StrataHeight;
                float _AntiTile;
                float _VariantScale;
                float _VariantRotation;
                float _MacroStrength;
                float _MacroScale;
                float _Cutoff;
                float _Cull;
                float _ZWrite;
                float _Surface;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            float SA_Hash11(float n)
            {
                return frac(sin(n * 12.9898) * 43758.5453);
            }

            float SA_Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float SA_Hash31(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
            }

            float SA_Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = SA_Hash21(i);
                float b = SA_Hash21(i + float2(1, 0));
                float c = SA_Hash21(i + float2(0, 1));
                float d = SA_Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float SA_Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float x00 = lerp(SA_Hash31(i),                   SA_Hash31(i + float3(1, 0, 0)), f.x);
                float x10 = lerp(SA_Hash31(i + float3(0, 1, 0)), SA_Hash31(i + float3(1, 1, 0)), f.x);
                float x01 = lerp(SA_Hash31(i + float3(0, 0, 1)), SA_Hash31(i + float3(1, 0, 1)), f.x);
                float x11 = lerp(SA_Hash31(i + float3(0, 1, 1)), SA_Hash31(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            // One copy of the texture: shifted by a random offset, turned by a random angle up to
            // _VariantRotation degrees either way and, half the time, mirrored left-right. Never
            // flipped upside down or turned far, so painted light still comes from above on walls.
            half3 SampleCopy(float2 uv, float2 dx, float2 dy, float id, float salt)
            {
                float2 off = float2(SA_Hash11(id * 1.618 + salt + 0.37), SA_Hash11(id * 2.414 + salt + 5.11));
                float2 m = float2(SA_Hash11(id * 3.303 + salt + 9.73) < 0.5 ? -1.0 : 1.0, 1.0);
                float ang = (SA_Hash11(id * 4.171 + salt + 2.29) * 2.0 - 1.0) * radians(_VariantRotation);
                float sn, cs;
                sincos(ang, sn, cs);
                float2x2 rot = float2x2(cs, -sn, sn, cs);
                float2 uvR = mul(rot, uv * m) + off;
                float2 dxR = mul(rot, dx * m);
                float2 dyR = mul(rot, dy * m);
                return SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvR, dxR, dyR).rgb;
            }

            // Anti-tiling (after Inigo Quilez, "texture repetition", technique 3). A slow noise
            // picks which copy each patch shows; where it steps from one copy to the next, the two
            // are cross-faded, and the seam leans toward the brighter one so it follows the rock's
            // own shapes instead of drawing a line.
            half3 SampleRock(float2 uv, float2 dx, float2 dy, float salt)
            {
                half3 c = half3(0, 0, 0);
                UNITY_BRANCH if (_AntiTile < 0.5)
                {
                    c = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, dx, dy).rgb;
                }
                else
                {
                    float k = SA_Noise2(uv * _VariantScale + salt) * 8.0;
                    float id = floor(k);
                    float f = k - id;
                    half3 a = SampleCopy(uv, dx, dy, id, salt);
                    half3 b = SampleCopy(uv, dx, dy, id + 1.0, salt);
                    half d = (a.r + a.g + a.b) - (b.r + b.g + b.b);
                    c = lerp(a, b, smoothstep(0.38, 0.62, f - 0.12 * d));
                }
                return c;
            }

            // No sign flips per face: they would make the UVs jump at ridges and leave a one-pixel
            // seam from mip selection. The cost is that faces pointing the other way see the texture
            // mirrored, which a rock texture never shows.
            half3 SampleTriplanar(float3 positionWS, float3 n)
            {
                float3 w = pow(abs(n), _Sharpness);
                w /= max(w.x + w.y + w.z, 1e-5);
                w *= step(0.02, w);                     // skip projections that barely show
                w /= max(w.x + w.y + w.z, 1e-5);

                float3 p = positionWS / max(_TileMeters, 0.01);
                float2 uvX = float2(p.z, p.y);
                float2 uvY = float2(p.x, -p.z);
                float2 uvZ = float2(p.x, p.y);
                // Derivatives up front: they aren't valid inside the branches below.
                float2 dxX = ddx(uvX), dyX = ddy(uvX);
                float2 dxY = ddx(uvY), dyY = ddy(uvY);
                float2 dxZ = ddx(uvZ), dyZ = ddy(uvZ);

                half3 c = half3(0, 0, 0);
                UNITY_BRANCH if (w.x > 0.0) c += SampleRock(uvX, dxX, dyX, 0.0) * w.x;
                UNITY_BRANCH if (w.y > 0.0) c += SampleRock(uvY, dxY, dyY, 17.3) * w.y;
                UNITY_BRANCH if (w.z > 0.0) c += SampleRock(uvZ, dxZ, dyZ, 31.9) * w.z;
                return c;
            }

            // Slow brightness and warm/cool drift from 3D world noise - never repeats.
            half3 SA_Macro(float3 p)
            {
                float s = max(_MacroScale, 0.5);
                float n1 = SA_Noise3(p / s);
                float n2 = SA_Noise3(p / (s * 0.29) + 19.7);
                float v = (n1 - 0.5) * 0.9 + (n2 - 0.5) * 0.5;
                float h = (n1 - 0.5) * 0.16 * _MacroStrength;
                return (1.0 + v * _MacroStrength) * half3(1.0 + h, 1.0, 1.0 - h);
            }

            // Irregular horizontal sediment bands from world height, gently wavy, with a faint lighter
            // line along the top of some of them.
            half SA_Strata(float3 p)
            {
                float wob = (SA_Noise2(p.xz * 0.12) - 0.5) * 1.2;
                float yy = p.y + wob;
                float k = yy / max(_StrataHeight, 0.05) + 0.6 * sin(yy * 1.7);
                float band = floor(k);
                float h = SA_Hash11(band) - 0.5;
                float f = k - band;
                float lineMask = step(f, 0.07) * step(-0.1, h);
                return 1.0 + (h * 0.22 + lineMask * 0.16) * _Strata;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                half3 albedo = SampleTriplanar(IN.positionWS, N) * _BaseColor.rgb;
                if (_MacroStrength > 0.001)
                    albedo *= SA_Macro(IN.positionWS);
                if (_Strata > 0.001)
                    albedo *= SA_Strata(IN.positionWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(N);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0.0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0;
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.alpha = 1.0;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.clearCoatMask = 0.0;
                surfaceData.clearCoatSmoothness = 0.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }

    // Supplies the shadow, depth and depth-normals passes. Also the safety net: if the pass above
    // ever fails to compile, Unity falls back to plain Lit rather than drawing magenta.
    Fallback "Universal Render Pipeline/Lit"
}
