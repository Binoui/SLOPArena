---
key: last_stop
display_name: Last Stop
target: pvp
players: 2-4
variant: static
availability: accepted
competitive_intent: mixed
topology: island platform facing a parked train across an open gap, with a tunnel-rock perch
source_scene: client/Unity/Assets/Stages/last_stop/last_stop.unity
baked_arena: data/arenas/last_stop.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/last_stop.prefab
design_brief: docs/design/stages/last_stop.design.md
human_review:
  owner: project maintainer
  status: accepted 2026-10-17
  process: gameplay-camera orbit and gap-recovery review, then normal PVP at 2 and 4 players
---

# Last Stop: Mind the Gap

The last train has stopped at a summit halt above the clouds, and the gap between
the platform and the train is real. This remake replaces the 2026-10-11 station
first pass, which used a vendor train-station pack, a plain rectangular platform and
two baggage boxes. The maintainer chose the premise, the snowy dusk setting and a
lively ambience, then locked the [design brief](last_stop.design.md) on 2026-10-16.

## Gameplay shell

The main floor is a broad island platform with its outer corners chamfered. Along
its northern edge an open 3-unit gap separates it from a parked train. The gap
drops straight to the lower blast plane: there is no hidden floor, and knockback
into the train's flank can send a fighter down it.

The train's flat roof is a high road along the north side. A running jump from the
platform edge reaches it. The locomotive cab and chimney rise above it at the
western end, and a small end-of-line ledge sits in front of the buffer stop.
Portal Rock, where the train enters its tunnel, closes the north-eastern corner.
Its snowy top is a perch reached from the roof. From the platform floor, only
Bonk's double jump has the height.

All volumes are closed solids that reach the lower blast plane. Spawns are four
inward-facing positions on the platform. The authoring scene holds six stage-owned
collision meshes under `Assets/Stages/last_stop/Collision/`. The baked arena owns
the derived blast lines.

Shared owns grounding, collision, recovery and blast deaths. `LastStopArenaTests`
covers the current FightGuy, Manki, Wibou and Bonk definitions:
- All four spawns hold.
- The platform runs its full length along the gap without losing support.
- The gap is lethal with no hidden floor.
- A running jump from the edge lands on every carriage section.
- One jump from the roof reaches the cab and Portal Rock.
- One air jump from just below the gap lip recovers onto the platform.
- The outer walls stop inward recovery without trapping retreat.

These are deterministic structural checks, not human PVP acceptance.

## Presentation

All art is original and stage-owned; the prefab references no vendor pack.
Headless Blender scripts built the geometry as FBX under
`Assets/Stages/last_stop/Art/`. Numpy generated the textures under `Textures/`:
platform slabs, granite, snow, masonry, sky, cloud tops, moon, a particle sprite
and a Swiss station clock face. Forty URP materials in `Materials/` are remapped
onto the models by name.

- **Platform:** pale stone slabs with a cream coping, a yellow safety line and
  three MIND THE GAP runs. Snow drifts are flush paint on the outer edges. A
  masonry band sits over faceted granite that drops into the clouds.
- **Train:** a red saddle-tank locomotive named SLOPPY, with a black smokebox,
  brass bands, a working chimney, a cab with firebox glow, and wheels with
  coupling rods. Two red-and-cream carriages have lit windows, LAST STOP
  destination boards, snow-capped roofs, bogies and gangways. The track bed and
  rails run under the train. A striped buffer stop carries a red end-of-line lamp.
- **Portal Rock:** a masonry tunnel portal swallows the last carriage. The south
  face carries a carved station-house facade with lit windows, a blue LAST STOP
  nameboard, a wall lantern and a Swiss station clock.
- **World:** the split pinnacle rises from a billowing cloud sea, with cumulus
  mounds around its base. Snowy rock stacks with pines stand at several depths.
  A curved arched viaduct runs between two tunnel stacks. The Horn is the hero
  peak, with a continuous ridged skyline behind it. A hamlet of chalets with lit
  windows sits on a snowy shoulder, under a dusk sky dome with stars and a moon.

Every ambient motion is presentation-only. Legacy `Animation` clips drive:
- a small steam train crossing the viaduct roughly every minute, out of one tunnel
  and into the other;
- the station clock, whose second hand pauses at twelve;
- the flicker of the end-of-line lamp;
- slowly drifting mist.

Particle systems provide sparse snowfall, chimney steam, a periodic whistle plume,
the distant train's steam trail, chimney smoke from five chalets and low cloud
wisps under the KO plane. The prefab carries no gameplay scripts or Colliders.
Its only MonoBehaviour is `ArenaAtmosphere`.

Lighting is a low warm western key, a cool moonlight fill from the south-east, a
cloud-bounce trilight ambient and four local point lights:
- two carriage-window spills over the gap;
- the station lantern;
- the flickering red end-of-line lamp.

Its lights sit under the prefab's `Stage Lights` root, the convention every
stage that owns its lighting follows: when the stage spawns, the match switches
off its scene's own directional lights. Without that, Training's three neutral
scene lights washed out the dusk palette. (The stage first shipped with a
separate `replaceSceneSunlight` switch; it was folded into the shared rule on
2026-10-17.)

## Verification

Native bake and inspection pass with matching source and baked hashes, four
grounded spawns, zero cosmetic Colliders and only supported URP shaders.

An exact triangle/box test checked every background triangle whose bounds
overlap the blast corridor, and none intersect it. Inside the corridor's
footprint, cloud vertices stay at least 1.2 units under the KO plane. The prefab's
dependency closure contains only stage-owned assets and stock URP shaders.

Inspection needed a small tooling fix. Its triangle count dereferenced a missing
`MeshFilter` on particle renderers, so it now uses `TryGetComponent`. No stage had
used particles before.

Agent review covered 24 elevated orbit views, 8 low recovery views, 4 spawn views,
a wide hero view and close-ups of the gap, facade and both train sides, in
`.ability-lab-cache/last-stop-remake-20261016/final-02/`.

Live Training on the real stage confirmed:
- the scene's directional lights hand off to the stage;
- snow, steam and smoke run, and the clock ticks;
- the viaduct train crosses with lit windows;
- the console stays empty;
- FightGuy's typed input sequence runs from spawn, jumps the gap and lands
  grounded on the train roof.

Live captures are in `live-03/` and `live-04/` of the same cache. The stage
thumbnail is the `live-04` thumbnail view with both fighters on the platform.

The full Shared suite passes, and the Server builds with zero errors. Authoring
scripts, receipts and backups of the retired stage files are in
`.stage-authoring-cache/last_stop/remake-20261016/`.

Entering Play Mode with a domain reload deadlocked the canonical Editor once. It
was restarted with the maintainer's authorization, and later live checks used
Play Mode without a domain reload. A gateway lease whose release token was lost
also needed one authorized restart. Neither changed stage data.

These checks are structural and agent-camera evidence. They are not the human
gameplay-camera review, human PVP acceptance or a measured performance result.

The collision meshes, FBX art, textures, materials, animation clips, authoring
scene and baked arena are versioned. So are the Resources prefab and the
stage-select thumbnail, re-included in `.gitignore` because they no longer
depend on licensed art, so a fresh clone gets the whole stage.
`tools/check_asset_closure.py` on the prefab and stage folder finds no ignored
or vendor dependency; only URP's own shaders come from a package.

## Test in Unity

1. Select Last Stop in Training or Solo. Check the dusk lighting, snowfall, the
   steaming locomotive and the stage-select thumbnail.
2. Run to the platform edge and jump onto the train roof. Walk west onto the cab
   and east to Portal Rock; confirm the rock can't be reached from the platform.
3. Knock a dummy into the gap and fall in yourself. Below the lip, one air jump
   should recover; deeper falls should KO in the clouds with no hidden floor.
4. Orbit the camera all around. The cloud sea, rock stacks, viaduct and hamlet
   should frame the fight without hiding fighters or suggesting a safe route.
5. Wait about a minute for the small train to cross the viaduct, and watch the
   station clock.
6. Play two- and four-player matches. Review spacing, the roof high road, the
   perch and how often the gap decides stocks.
