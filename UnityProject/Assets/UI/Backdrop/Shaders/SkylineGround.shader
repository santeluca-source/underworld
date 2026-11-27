// Terreno dello sfondo: strade, marciapiedi e linee disegnati nello shader a partire dalla griglia della città.
Shader "Underworld/UI/SkylineGround"
{
    Properties
    {
        _Pitch ("Passo isolato + strada (m)", Float) = 52
        _Block ("Lato isolato (m)", Float) = 38
        _Origin ("Origine griglia (xz)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-10" }

        Pass
        {
            Name "SkylineGround"
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
                float _Pitch;
                float _Block;
                float4 _Origin;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float day = _UW_DayMix;
                float2 p = (i.positionWS.xz - _Origin.xy) / _Pitch;
                float2 local = frac(p) * _Pitch;           // metri dentro la cella
                float2 fw = max(fwidth(i.positionWS.xz), 1e-3);

                // Isolato = [0, _Block], strada = il resto. Marciapiede di 3 m sul bordo dell'isolato.
                float2 inBlock = smoothstep(-fw, fw, local) * (1.0 - smoothstep(_Block - fw, _Block + fw, local));
                float block = inBlock.x * inBlock.y;
                float2 inCore = smoothstep(3.0 - fw, 3.0 + fw, local) * (1.0 - smoothstep(_Block - 3.0 - fw, _Block - 3.0 + fw, local));
                float core = inCore.x * inCore.y;

                float3 asphalt = lerp(float3(0.010, 0.012, 0.017), float3(0.20, 0.21, 0.23), day);
                float3 walk = lerp(float3(0.028, 0.030, 0.038), float3(0.56, 0.54, 0.50), day);
                float3 lot = lerp(float3(0.018, 0.020, 0.026), float3(0.40, 0.52, 0.34), day);
                float3 col = lerp(asphalt, lerp(walk, lot, core), block);

                // Linea tratteggiata al centro delle strade.
                float mid = _Block + (_Pitch - _Block) * 0.5;
                float2 dl = abs(local - mid);
                float2 dash = step(0.5, frac(p.yx * 8.0));
                float lineX = (1.0 - smoothstep(0.12 - fw.x, 0.12 + fw.x, dl.x)) * dash.x * (1.0 - inBlock.y);
                float lineZ = (1.0 - smoothstep(0.12 - fw.y, 0.12 + fw.y, dl.y)) * dash.y * (1.0 - inBlock.x);
                float lines = saturate(lineX + lineZ) * (1.0 - block);
                col = lerp(col, lerp(float3(0.06, 0.055, 0.045), float3(0.85, 0.83, 0.76), day), lines * 0.8);

                return half4(UwApplyFog(col, i.positionWS), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
