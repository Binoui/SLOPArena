#ifndef SLOPARENA_STAGE_CUTAWAY_INCLUDED
#define SLOPARENA_STAGE_CUTAWAY_INCLUDED

// Published by CameraObstruction when the camera keeps its distance behind a wall:
// xyz is the followed fighter, w the horizontal distance from the camera to the blocking
// geometry (0 when nothing blocks). The cut is a vertical plane facing the fighter, so a wall
// goes from floor to top; a plane square to the downward view ray would keep the wall's base.
// A short dithered band softens the edge where the plane crosses side walls.
float4 _StageCutaway;

void StageCutawayClip(float3 positionWS, float4 positionCS)
{
    if (_StageCutaway.w <= 0.0) return;
    float2 axis = normalize(_StageCutaway.xz - _WorldSpaceCameraPos.xz);
    float depth = dot(positionWS.xz - _WorldSpaceCameraPos.xz, axis);
    const float4x4 bayer = float4x4(0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
    uint2 pixel = uint2(positionCS.xy) & 3u;
    float threshold = (bayer[pixel.y][pixel.x] + 0.5) / 16.0;
    clip((depth - _StageCutaway.w) / 0.35 - threshold);
}

#endif
