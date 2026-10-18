---
key: last_stop
status: accepted
locked: 2026-10-16
approved_by: maintainer
approval_scope: Mind the Gap shell, snowy summit dusk composition, lively presentation-only ambience
accepted: 2026-10-17
blockout:
  - .stage-authoring-cache/last_stop/design/design-top.png
  - .stage-authoring-cache/last_stop/design/design-north.png
  - .stage-authoring-cache/last_stop/design/design-east.png
  - .stage-authoring-cache/last_stop/design/design-south.png
  - .stage-authoring-cache/last_stop/design/design-west.png
---

# Last Stop: Mind the Gap

On 2026-10-16 the maintainer asked for a remake of the previous station first pass,
which "was a nice idea but kinda fell flat". The maintainer chose the Mind the Gap
premise, a snowy mountain dusk setting and a lively presentation with weather and
a moving background train. They authorized replacing every asset and the gameplay
geometry. Vendor prefabs are not required. The previous vendor-based presentation and
its rectangular shell are retired. After reviewing the gray-box shell diagram and
blockout views, the maintainer locked this brief on 2026-10-16 ("Lock it, go") and
accepted the built stage on 2026-10-17 ("the maps are much better").

Yaw follows the stage-design capture convention: yaw 0° puts the camera north of the
stage, looking south. Yaw 180° puts the camera south of the platform, looking north
across it at the train. This is the hero view.

## Personality

The last train has stopped at a summit halt above the clouds, and the gap between
the platform and the train is real.

## Fight premise

One broad island platform is the main floor. It faces the parked last train across
an open 3-unit gap that drops straight into the clouds. A running jump from the
platform edge reaches the carriage roofs, the high road along the north side. The
loco cab is one step higher. Portal Rock, where the train enters its tunnel, is the
high perch. It is reachable from the roof, never from the platform floor. Knockback
toward the train risks the gap: mind the gap. Competitive intent is mixed.

## Composition decisions

1. **Platform:** from yaw 180° the platform fills the foreground and lower third.
   Clean stone slabs carry a white coping edge, a yellow safety line and MIND THE
   GAP lettering along the gap. Snow appears only as flush drifts on the outer edges.
   No furniture stands on the floor.
2. **The gap:** an unmistakable dark cleft separates the platform coping from the
   train's flank and stays open all the way down. The train's lower body, bogies
   and wheels descend into it, then raw rock continues into cloud.
3. **The last train:** a chunky saddle-tank steam locomotive occupies the left third
   from yaw 180°. It has a tall chimney, cab and brass details and pulls two carriages
   in a red-and-cream mountain livery with warm-lit windows. Flat roofs carry snow
   caps; they are the high road. The last carriage disappears into the tunnel portal
   at the right.
4. **End of the line:** the loco noses into a red-and-white buffer stop with a red
   end-of-line lamp at the western tip of the rock, above the drop.
5. **Portal Rock:** a craggy granite buttress closes the right (east) end. A masonry
   tunnel portal opens on its west face. A station-house facade is carved into its
   south face, with warm windows, a door lamp, a station clock and the LAST STOP
   nameboard. Its snowy flat top is the perch.
6. **Scene world below:** the whole summit is one split rock pinnacle. Its sheer walls
   plunge into a sea of clouds just below the KO line. There is no ledge, landing or
   cloud surface above the KO plane.
7. **Scene world around and far:** near rock needles rise from the cloud sea at varied
   distances. From yaw 180°, a curved stone viaduct between two cliffs fills the left
   background, and a small train crosses it periodically. One hero peak, the Horn,
   stands in the center background with alpenglow on its face. The right background
   is a snowy shoulder with a hamlet of chalets, lit windows and chimney smoke. A
   quieter ring of ranges with open gaps surrounds the scene.
8. **Scene world above:** the dusk sky runs from a warm sunset glow in the west to
   deep blue-violet overhead and the first stars with a pale moon in the east. Gentle
   snow falls.
9. **Ambient life (presentation only):** chimney steam and occasional whistle plumes;
   snowfall; the distant train on the viaduct; hamlet chimney smoke and windows;
   a flickering end-of-line lamp; a working station clock; slowly drifting clouds.
10. **Other yaws:** yaw 0° looks across the train's outer side to the platform, the
    sunset ranges and the southern needle. Yaw 90° looks past Portal Rock along the
    train toward the buffer stop and sunset. Yaw 270° looks past the loco nose along
    the train toward the tunnel and the alpenglow peaks.

**Busy here / empty here:** the train, Portal Rock's facade and the distant ring carry
the detail. The platform floor, the gap, the roofs, the immediate recovery corridor
and the cloud surface near the pinnacle stay quiet.

## Negative decisions

- No furniture, lamp posts, benches or standing signs on the platform or train roofs.
  Platform markings are flush.
- No bridge, plank or hidden floor across the gap. No cosmetic surface above the KO
  plane in the gap or around the pillars.
- No background geometry inside the blast corridor. Near the stage, clouds stay
  below the KO plane.
- The parked train never moves. Ambient motion is presentation-only and stays away
  from the fighters.
- One hero landmark, the Horn. The viaduct train is the moving accent, not a
  second hero.
- No vendor or licensed prefabs. Meshes, materials and textures are original and
  stage-owned.
- Snowfall and steam stay sparse near the fight and must never hide a fighter.

## Palette & lighting

Dusk just after sunset. A low warm amber-pink key comes from the western glow, with
blue-violet sky light and cool blue snow shadows. Accents: carriage red, brass, warm
window amber, the red end-of-line lamp and alpenglow pink on the Horn. The platform
stone stays lighter and warmer than the surroundings so fighters and edges read
first.

## Camera vantages

- Yaw 180° hero view across the platform and gap to the train, Horn behind.
- The four spawn views.
- Elevated orbit every 15°; low recovery views every 45°.
- Looking down into the gap from the platform edge and from the roof.
- Top view: footprint, gap, spawns and blast corridor.

## Gameplay shell

| Element | Footprint (X, Z) | Top Y |
|---|---|---|
| Platform | X −16..16, Z −12..0, 2.5 chamfers on the outer corners | 0 |
| Gap | Z 0..3, open to the blast plane | — |
| Train body | X −18..16.5, Z 3..7 | 1.8 |
| Loco cab | X −11..−8 | 2.7 |
| Chimney | X −17.2..−16.2, Z 4.5..5.5 | 2.8 |
| Buffer stop ledge | X −18.8..−18 | −0.3 |
| Portal Rock | six-sided buttress, X 16..22.5, Z 2..10.3 | 3.7 |

During production on 2026-10-16 the buffer-stop ledge was lowered from 0.4 to
−0.3, so the buffer beam sits at buffer height and the smokebox face stays visible.
This is the only shell change after LOCK. The solid volumes reach the KO plane at
Y −10. Spawns sit at X ±7, Z −3.5 and −8.5,
facing a point just south of the gap. The baked arena owns the derived boundaries.

## Movement basis

Deterministic Shared checks on the baked shell, for the current FightGuy, Manki,
Wibou and Bonk definitions, establish the following:
- A running jump from the platform edge lands on the carriage roof for every character.
- One jump from the roof reaches the cab and Portal Rock. FightGuy has the smallest margin.
- No standing or air jump reaches Portal Rock from the platform floor. Only Bonk's
  double jump has the height.
- The gap is lethal with no hidden floor.
- One air jump from just below the lip recovers onto the platform.
- The outer walls stop inward recovery and allow a retreat.

These are structural checks, not human PVP acceptance.
