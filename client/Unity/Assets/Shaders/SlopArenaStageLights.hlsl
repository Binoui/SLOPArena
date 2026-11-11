#ifndef SLOPARENA_STAGE_LIGHTS_INCLUDED
#define SLOPARENA_STAGE_LIGHTS_INCLUDED

// Look lights: a stage's own pools of light (StageLook) and the flash of a hit (StageLookLights.Flash).
// They are worked out here, not as URP lights: URP gives a renderer at most four extra lights (one on Medium),
// so a lamp added over a large floor is dropped wherever the stage already has its own. Fighters read the same
// lights in GraphicCharacter, where their sum is capped so a lamp cannot bleach them.
#define STAGE_LOOK_LIGHTS 4
float4 _StageLookLightPositions[STAGE_LOOK_LIGHTS];   // xyz position, w 1 / range²
float4 _StageLookLightColors[STAGE_LOOK_LIGHTS];      // rgb colour × intensity, a 1 for a hit flash, 0 for a stage pool
float4 _StageLookLightSpots[STAGE_LOOK_LIGHTS];       // xyz the way the lamp points, w cos(outer half angle); -2 for a point light
float4 _StageLookLightCones[STAGE_LOOK_LIGHTS];       // x 1 / (cos(inner) - cos(outer)); 1 for a point light
float _StageLookLightCount;

// Colour arriving at a point from one look light, and the direction towards it.
half3 StageLookLight(int index, float3 positionWS, out half3 toLight)
{
    float3 delta = _StageLookLightPositions[index].xyz - positionWS;
    float distanceSq = max(dot(delta, delta), 1e-4);
    toLight = (half3)(delta * rsqrt(distanceSq));
    float reach = distanceSq * _StageLookLightPositions[index].w;      // 0 at the lamp, 1 at its range
    // A stage pool falls off like a URP light of the same intensity and range. A flash is a soft pool
    // with no hot centre, because it sits inside the fighter it lights.
    float window = saturate(1.0 - reach * reach);
    float pool = window * window / distanceSq;
    float flash = saturate(1.0 - reach);
    float falloff = lerp(pool, flash * flash, _StageLookLightColors[index].a);
    float cone = saturate((dot(_StageLookLightSpots[index].xyz, -toLight) - _StageLookLightSpots[index].w) * _StageLookLightCones[index].x);
    return (half3)(_StageLookLightColors[index].rgb * (falloff * cone * cone));
}

// Summed look lights on a surface with a wrapped diffuse term (wrap 0 = Lambert): the stage's pools and the
// flashes of hits, kept apart because they are added differently.
void StageLookLights(float3 positionWS, half3 normalWS, half wrap, out half3 pools, out half3 flashes)
{
    pools = half3(0, 0, 0);
    flashes = half3(0, 0, 0);
    int count = (int)_StageLookLightCount;
    for (int i = 0; i < STAGE_LOOK_LIGHTS; i++)
    {
        if (i >= count) break;
        half3 toLight;
        half3 arriving = StageLookLight(i, positionWS, toLight) * saturate((dot(normalWS, toLight) + wrap) / (1.0h + wrap));
        if (_StageLookLightColors[i].a < 0.5) pools += arriving;
        else flashes += arriving;
    }
}

// A flash lifts what is dark and leaves what is already bright, so a pale floor under a lamp does not burn out.
half3 StageLookFlash(half3 color, half3 albedo, half3 flashes)
{
    half headroom = saturate(1.0h - dot(color, half3(0.299h, 0.587h, 0.114h)));
    return color + albedo * flashes * headroom;
}

#endif
