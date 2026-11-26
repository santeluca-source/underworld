// Palazzi dello sfondo del menu. Le finestre sono disegnate nello shader (niente texture):
// uv0 = coordinate della facciata in "campate" (x) e "piani" (y); uv1 = (seme, quota di luci accese, calore, tetto).
// Da lontano le finestre si fondono nel colore medio, così la camera che si muove non crea sfarfallii.
Shader "Underworld/UI/SkylineBuildings"
{
    Properties
    {
        _WindowWarm ("Finestre calde", Color) = (1.0, 0.58, 0.26, 1)
        _WindowCool ("Finestre fredde", Color) = (0.66, 0.80, 1.0, 1)
        _WindowIntensity ("Intensità finestre", Float) = 2.2
        _FacadeNight ("Facciata di notte", Color) = (0.030, 0.036, 0.052, 1)
        _RoofNight ("Tetto di notte", Color) = (0.016, 0.019, 0.028, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "SkylineBuildings"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkylineCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WindowWarm;
                float4 _WindowCool;
                float _WindowIntensity;
                float4 _FacadeNight;
                float4 _RoofNight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.data = v.data;
                return o;
            }

            // Palette di giorno (stile "Modern Low Poly" del gioco): facciate calde e neutre.
            float3 DayFacade(float seed)
            {
                float h = UwHash11(seed * 91.7);
                float3 a = float3(0.62, 0.55, 0.45);
                float3 b = float3(0.55, 0.30, 0.22);
                float3 c = float3(0.47, 0.49, 0.52);
                float3 d = float3(0.74, 0.70, 0.62);
                float3 e = float3(0.36, 0.42, 0.48);
                return h < 0.2 ? a : h < 0.4 ? b : h < 0.6 ? c : h < 0.8 ? d : e;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float seed = i.data.x;
                float litRatio = i.data.y;
                float warmth = i.data.z;
                float isRoof = i.data.w;
                float day = _UW_DayMix;
                float3 n = normalize(i.normalWS);
                float3 L = normalize(_UW_LightDir.xyz);
                float ndl = saturate(dot(n, L));

                // Colore base della facciata, con una piccola variazione per palazzo.
                float tintH = UwHash11(seed * 13.1);
                float3 nightFacade = _FacadeNight.rgb * lerp(float3(0.85, 0.9, 1.05), float3(1.2, 1.08, 0.95), tintH);
                float3 facade = lerp(nightFacade, DayFacade(seed), day);

                // Luce: di notte la luna è debole e il riverbero caldo della strada sale dal basso.
                float bounce = exp(-max(i.positionWS.y, 0.0) / 14.0);
                float3 nightLight = facade * (0.75 + 0.45 * ndl) + float3(0.055, 0.032, 0.014) * bounce;
                float3 dayLight = facade * (0.52 + 0.7 * ndl) + float3(0.03, 0.04, 0.06);
                float3 litFacade = lerp(nightLight, dayLight, day);

                if (isRoof > 0.5)
                {
                    float3 roof = lerp(_RoofNight.rgb, float3(0.30, 0.30, 0.32), day);
                    float3 col = roof * (0.85 + 0.15 * UwHash12(floor(i.uv)));
                    return half4(UwApplyFog(col, i.positionWS), 1);
                }

                float2 uv = i.uv;
                float2 cell = floor(uv);
                float2 f = frac(uv);
                float2 fw = max(fwidth(uv), 1e-4);

                bool ground = cell.y < 0.5;
                float2 lo = ground ? float2(0.06, 0.04) : float2(0.17, 0.24);
                float2 hi = ground ? float2(0.94, 0.78) : float2(0.83, 0.80);
                float mask = UwRectMask(f, lo, hi, fw);

                // Luci accese: per finestra, per piano (alcuni piani di uffici tutti accesi), per palazzo.
                float hWin = UwHash12(cell + seed * 61.7);
                float hFloor = UwHash12(float2(cell.y, seed * 17.3));
                float ratio = litRatio * lerp(0.35, 1.6, hFloor);
                float on = step(hWin, ratio);
                on = max(on, step(0.94, hFloor) * step(0.12, hWin));
                if (ground) on = step(0.35, UwHash12(cell + seed * 5.3));

                float hc = UwHash12(cell * 1.7 + seed * 3.1);
                float3 tint = lerp(_WindowCool.rgb, _WindowWarm.rgb, step(hc, warmth));
                float bright = _WindowIntensity * lerp(0.5, 1.2, UwHash12(cell + 7.7)) * (ground ? 0.9 : 1.0);
                float3 offGlass = float3(0.010, 0.013, 0.020);
                float3 nightWin = lerp(offGlass, tint * bright, on);
                float3 dayWin = lerp(float3(0.13, 0.18, 0.25), float3(0.36, 0.46, 0.58), saturate(f.y * 1.2));
                float3 win = lerp(nightWin, dayWin, day);

                // Da lontano (finestre più piccole di un pixel): colore medio, niente sfarfallio.
                float area = (hi.x - lo.x) * (hi.y - lo.y);
                float avgOn = saturate(litRatio * 0.95 + 0.04);
                float3 avgTint = lerp(_WindowCool.rgb, _WindowWarm.rgb, warmth);
                float3 avgNight = lerp(offGlass, avgTint * _WindowIntensity * 0.85, avgOn);
                float3 avgWin = lerp(avgNight, float3(0.22, 0.29, 0.38), day);
                float far = saturate((max(fw.x, fw.y) - 0.3) * 2.2);

                float3 detailed = lerp(litFacade, win, mask);
                float3 averaged = lerp(litFacade, avgWin, area);
                float3 color = lerp(detailed, averaged, far);
                return half4(UwApplyFog(color, i.positionWS), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
