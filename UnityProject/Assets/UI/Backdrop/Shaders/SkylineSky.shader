// Cielo dello sfondo: sfera intorno alla camera con passaggio dal blu notte all'alone caldo della città,
// luna morbida e poche stelle. Disegnata per prima e senza profondità: tutto il resto ci va sopra.
Shader "Underworld/UI/SkylineSky"
{
    Properties
    {
        _ZenithNight ("Zenit di notte", Color) = (0.010, 0.014, 0.030, 1)
        _HorizonNight ("Orizzonte di notte", Color) = (0.11, 0.075, 0.075, 1)
        _ZenithDay ("Zenit di giorno", Color) = (0.25, 0.45, 0.78, 1)
        _HorizonDay ("Orizzonte di giorno", Color) = (0.70, 0.78, 0.86, 1)
        _MoonDir ("Direzione della luna", Vector) = (-0.45, 0.32, 0.83, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }

        Pass
        {
            Name "SkylineSky"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkylineCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithNight;
                float4 _HorizonNight;
                float4 _ZenithDay;
                float4 _HorizonDay;
                float4 _MoonDir;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // Sfera centrata sulla camera, raggio dentro il piano lontano.
                float3 ws = GetCameraPositionWS() + v.positionOS.xyz * (_ProjectionParams.z * 0.8);
                o.positionCS = TransformWorldToHClip(ws);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float day = _UW_DayMix;
                float3 d = normalize(i.dir);
                float up = saturate(d.y);
                // Alone caldo stretto sopra la città, poi il cielo diventa scuro in fretta.
                float k = lerp(1.0 - exp(-up * 18.0), pow(up, 0.42), day);
                float3 zenith = lerp(_ZenithNight.rgb, _ZenithDay.rgb, day);
                // Di notte l'orizzonte ha lo stesso colore della foschia: niente banda netta dove finisce la città.
                float3 horizon = lerp(_UW_FogColor.rgb, _HorizonDay.rgb, day);
                float3 col = lerp(horizon, zenith, k);
                // Sotto l'orizzonte: lo stesso colore della foschia (la città sparisce nella nebbia).
                col = lerp(col, _UW_FogColor.rgb, saturate(-d.y * 8.0));

                // Luna (solo di notte): disco morbido e alone largo.
                float3 md = normalize(_MoonDir.xyz);
                float m = dot(d, md);
                float disc = smoothstep(0.99955, 0.99975, m);
                float halo = pow(saturate(m), 180.0) * 0.18 + pow(saturate(m), 18.0) * 0.035;
                col += (float3(1.0, 0.96, 0.88) * disc * 1.4 + float3(0.55, 0.6, 0.75) * halo) * (1.0 - day);

                // Poche stelle, solo in alto (la luce della città copre le altre).
                float2 g = float2(atan2(d.x, d.z) * 180.0, d.y * 260.0);
                float2 cell = floor(g);
                float h = UwHash12(cell);
                float2 f = frac(g) - 0.5;
                float star = step(0.9965, h) * saturate(1.0 - dot(f, f) * 9.0) * smoothstep(0.18, 0.5, up);
                col += star * 0.55 * (1.0 - day);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
