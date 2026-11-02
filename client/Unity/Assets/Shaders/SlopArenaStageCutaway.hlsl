#ifndef SLOPARENA_STAGE_CUTAWAY_INCLUDED
#define SLOPARENA_STAGE_CUTAWAY_INCLUDED

// Camera-facing sight holes keep fighters visible without moving the orbit camera closer.
// Walkable tops within jump height stay solid so players always see the stage they move on.
// Feet height preserves supporting floors; the depth fade, centred on the fighter, preserves geometry
// behind fighters while still cutting boxes and walls they stand right against.
float4 _StageSightTargets[4];   // xyz capsule centre, w feet height
float4 _StageSightColors[4];    // seat colour of each target
float4 _StageSightAmounts;      // per-target strength 0..1 (eased in C# while the fighter is blocked)
float _StageSightCount;
float4 _StageSightShape;        // x radius, y soft edge, z opacity inside, w tube (0 cone toward the camera, 1 same width all along)
float4 _StageSightStyle;        // x vertical stretch, y rim width (m), z rim strength, w seat tint inside
float _StageSightKeepTops;      // tops (upward faces seen from above) up to this many metres above a fighter's feet stay solid

// Clips the hole and returns the seat-colour overlay (rgb colour, a strength) for the kept pixels.
half4 StageCutawayClipRim(float3 positionWS, float4 positionCS)
{
    half4 rim = half4(0, 0, 0, 0);
    // Face normal from screen derivatives works in every pass, including those without a normal interpolator.
    float3 faceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (_StageSightCount <= 0.0) return rim;
    bool top = abs(faceNormal.y) > 0.7 && positionWS.y < _WorldSpaceCameraPos.y;
    float coverage = 1.0;
    for (int i = 0; i < 4; i++)
    {
        if (i >= _StageSightCount) break;
        float amount = _StageSightAmounts[i];
        if (amount <= 0.001) continue;
        float4 target = _StageSightTargets[i];
        if (positionWS.y <= target.w + 0.1) continue;
        // Walkable tops within jump height stay solid (FighterXRay shows the fighter); taller tops are cut like walls.
        if (top && positionWS.y - target.w < _StageSightKeepTops) continue;
        float3 toTarget = target.xyz - _WorldSpaceCameraPos;
        float len = length(toTarget);
        if (len <= 0.0001) continue;
        float3 dir = toTarget / len;
        float3 rel = positionWS - _WorldSpaceCameraPos;
        float along = dot(rel, dir);
        if (along <= 0.0) continue;
        float scale = lerp(along / len, 1.0, _StageSightShape.w);
        float radius = _StageSightShape.x * scale * lerp(0.6, 1.0, amount);
        float soft = _StageSightShape.y * scale;
        float3 perpVec = rel - dir * along;
        float3 upInPlane = float3(0, 1, 0) - dir * dir.y;
        float upLen = length(upInPlane);
        float vertical = upLen > 0.001 ? dot(perpVec, upInPlane / upLen) : 0.0;
        float horizontal = length(perpVec - (upLen > 0.001 ? upInPlane / upLen : 0) * vertical);
        float stretch = max(_StageSightStyle.x, 0.1);
        float dist = sqrt(horizontal * horizontal + (vertical / stretch) * (vertical / stretch));
        // Depth past the fighter, measured horizontally: fighters stand upright, so wall high up behind them
        // stays whole even where a steep camera ray would call it nearer. A camera straight above uses the ray.
        float2 flat = dir.xz;
        float flatLen = length(flat);
        float behind = flatLen > 0.2 ? dot(positionWS.xz - target.xz, flat / flatLen) : along - len;
        float depthFade = smoothstep(0.3, -0.1, behind);
        float hole = (1.0 - smoothstep(radius - soft, radius, dist)) * depthFade * amount;
        // Ghost, tint and edge only near the fighter (the wall it is behind); foreground nearer the camera opens fully.
        float near = smoothstep(-4.0, -2.5, behind);
        coverage = min(coverage, lerp(1.0, _StageSightShape.z * near, hole));
        float rimWidth = max(_StageSightStyle.y * scale, 0.0001);
        // The edge only marks geometry clearly in front of the fighter, not walls it stands against.
        float band = saturate(1.0 - abs(dist - radius) / rimWidth) * _StageSightStyle.z * smoothstep(-0.2, -0.7, behind) * near;
        float tint = hole * _StageSightStyle.w * near;
        float strength = max(band, tint) * depthFade * amount;
        if (strength > rim.a) rim = half4(_StageSightColors[i].rgb, strength);
    }
    const float4x4 bayer = float4x4(0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    uint2 pixel = uint2(positionCS.xy) & 3u;
    float threshold = (bayer[pixel.y][pixel.x] + 0.5) / 16.0;
    clip(coverage - threshold);
    return rim;
}

void StageCutawayClip(float3 positionWS, float4 positionCS)
{
    StageCutawayClipRim(positionWS, positionCS);
}

// Seat-colour edge and tint for colour passes.
half3 StageCutawayApplyRim(half3 color, half4 rim)
{
    return lerp(color, rim.rgb, saturate(rim.a));
}

// For passes without a world-position interpolator (the URP Lit copy): rebuild it from the pixel's depth.
void StageCutawayClipScreen(float4 positionCS)
{
    if (_StageSightCount <= 0.0) return;
    float3 positionWS = ComputeWorldSpacePosition(GetNormalizedScreenSpaceUV(positionCS), positionCS.z, UNITY_MATRIX_I_VP);
    StageCutawayClip(positionWS, positionCS);
}

#endif
