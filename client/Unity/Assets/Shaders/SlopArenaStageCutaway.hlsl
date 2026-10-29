#ifndef SLOPARENA_STAGE_CUTAWAY_INCLUDED
#define SLOPARENA_STAGE_CUTAWAY_INCLUDED

// Camera-facing cones keep fighters visible without moving the orbit camera closer.
// Feet height preserves supporting floors; the depth fade, centred on the fighter, preserves geometry
// behind fighters while still cutting boxes and walls they stand right against.
float4 _StageSightTargets[4];
float _StageSightCount;
float4 _StageSightShape;

void StageCutawayClip(float3 positionWS, float4 positionCS)
{
    if (_StageSightCount <= 0.0) return;
    float coverage = 1.0;
    for (int i = 0; i < 4; i++)
    {
        if (i >= _StageSightCount) break;
        float4 target = _StageSightTargets[i];
        if (positionWS.y <= target.w + 0.1) continue;
        float3 toTarget = target.xyz - _WorldSpaceCameraPos;
        float len = length(toTarget);
        if (len <= 0.0001) continue;
        float3 dir = toTarget / len;
        float3 rel = positionWS - _WorldSpaceCameraPos;
        float along = dot(rel, dir);
        if (along <= 0.0) continue;
        float radius = _StageSightShape.x * along / len;
        float soft = _StageSightShape.y * along / len;
        float perp = length(rel - dir * along);
        float hole = (1.0 - smoothstep(radius - soft, radius, perp))
            * smoothstep(len + 0.3, len - 0.1, along);
        coverage = min(coverage, lerp(1.0, _StageSightShape.z, hole));
    }
    const float4x4 bayer = float4x4(0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    uint2 pixel = uint2(positionCS.xy) & 3u;
    float threshold = (bayer[pixel.y][pixel.x] + 0.5) / 16.0;
    clip(coverage - threshold);
}

// For passes without a world-position interpolator (the URP Lit copy): rebuild it from the pixel's depth.
void StageCutawayClipScreen(float4 positionCS)
{
    if (_StageSightCount <= 0.0) return;
    float3 positionWS = ComputeWorldSpacePosition(GetNormalizedScreenSpaceUV(positionCS), positionCS.z, UNITY_MATRIX_I_VP);
    StageCutawayClip(positionWS, positionCS);
}

#endif
