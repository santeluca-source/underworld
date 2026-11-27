// Luci dello sfondo del menu: lampioni, luci rosse in cima alle torri, fari delle auto, pozze di luce a terra.
// Ogni luce è un quadrato (4 vertici con lo stesso centro). Il vertex shader lo gira verso la camera
// (o lo stende a terra) e lo anima (lampeggio, movimento lungo la strada). Somma additiva: niente ordinamento.
//   uv0 = angolo (-1..1)
//   uv1 = (dimensione in metri, fase 0..1, tipo, velocità del lampeggio)
//         tipo: 0 fissa, 1 lampeggia, 2 si muove, 3 pozza a terra
//   uv2 = (spostamento totale xyz, giri al secondo) per il tipo 2
//   color = colore (rgb) e intensità (a)
Shader "Underworld/UI/SkylineGlow"
{
    Properties
    {
        _Intensity ("Intensità", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SkylineGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkylineCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
                float4 data : TEXCOORD1;
                float4 motion : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
                float kind : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 center = TransformObjectToWorld(v.positionOS.xyz);
                float size = v.data.x;
                float phase = v.data.y;
                float kind = v.data.z;
                float t = _Time.y;
                float energy = 1.0;

                if (kind > 1.5 && kind < 2.5)
                {
                    float k = frac(phase + t * v.motion.w);
                    center += v.motion.xyz * k;
                    // Entra ed esce dolcemente alle estremità del percorso.
                    energy *= smoothstep(0.0, 0.06, k) * (1.0 - smoothstep(0.94, 1.0, k));
                }
                if (kind > 0.5 && kind < 1.5)
                {
                    // Lampi brevi, come le luci per gli aerei.
                    float s = sin((t * v.data.w + phase) * 6.2831853);
                    energy *= 0.08 + 0.92 * saturate(s * 5.0 - 4.0);
                }

                float3 positionWS;
                if (kind > 2.5)
                {
                    positionWS = center + float3(v.corner.x * size, 0.04, v.corner.y * size);
                    o.positionCS = TransformWorldToHClip(positionWS);
                }
                else
                {
                    float3 vs = TransformWorldToView(center);
                    float depth = max(-vs.z, 0.01);
                    float s = max(size, depth * _UW_MinGlowAngle);
                    energy *= (size * size) / (s * s); // stessa energia anche quando la luce è "allargata"
                    vs.xy += v.corner * s;
                    o.positionCS = TransformWViewToHClip(vs);
                    positionWS = center;
                }

                float fade = 1.0 - UwFogAmount(positionWS) * 0.85;
                o.color = v.color.rgb * v.color.a * energy * fade * _Intensity;
                o.corner = v.corner;
                o.kind = kind;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                float a = saturate(1.0 - r2);
                a = a * a * a;
                if (i.kind < 2.5) a += saturate(1.0 - r2 * 14.0) * 0.8; // nucleo luminoso dei punti luce
                return half4(i.color * a, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
