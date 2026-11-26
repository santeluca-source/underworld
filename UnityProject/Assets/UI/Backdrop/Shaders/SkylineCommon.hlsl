// Funzioni comuni degli shader dello sfondo del menu (città notturna).
// Valori globali impostati da MenuBackdrop.cs con Shader.SetGlobal*.
#ifndef UW_SKYLINE_COMMON_INCLUDED
#define UW_SKYLINE_COMMON_INCLUDED

float4 _UW_FogColor;     // rgb = colore della foschia
float4 _UW_FogParams;    // x = densità, y = altezza base, z = calo con l'altezza, w = massimo (0..1)
float4 _UW_LightDir;     // xyz = direzione verso la luna / il sole
float  _UW_DayMix;       // 0 = notte, 1 = giorno
float  _UW_MinGlowAngle; // dimensione minima delle luci (radianti), per non farle sparire da lontano

float UwHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float UwHash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

// Foschia esponenziale che si dirada con l'altezza: le torri alte restano nitide contro il cielo.
float UwFogAmount(float3 positionWS)
{
    float dist = distance(positionWS, GetCameraPositionWS());
    float heightK = exp(-max(positionWS.y - _UW_FogParams.y, 0.0) * _UW_FogParams.z);
    float fog = 1.0 - exp(-dist * _UW_FogParams.x * heightK);
    return saturate(fog) * _UW_FogParams.w;
}

float3 UwApplyFog(float3 color, float3 positionWS)
{
    return lerp(color, _UW_FogColor.rgb, UwFogAmount(positionWS));
}

// Copertura di un rettangolo [lo, hi] dentro la cella, con bordo morbido largo un pixel (niente scalette).
float UwRectMask(float2 f, float2 lo, float2 hi, float2 fw)
{
    float2 a = smoothstep(lo - fw, lo + fw, f);
    float2 b = 1.0 - smoothstep(hi - fw, hi + fw, f);
    return a.x * a.y * b.x * b.y;
}

#endif
