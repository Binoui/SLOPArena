---
key: log_off
status: locked
locked: 2026-10-16
approved_by: maintainer
approval_scope: selected split-and-fungus layout, logged-off campsite, and sunset campfire implementation
runtime_camera_review: pending
blockout:
  - .ability-lab-cache/log-off-campsite-20261016/orbit/top.png
---

# Log Off — logged-off campsite

On 2026-10-16 the maintainer selected **Split + fungus shelves**, **Logged-off
campsite**, and **Sunset campfire** from the rework options. Those choices authorize
this bounded rework of the previous split-stump woodland. The implementation scope
is decided; the new captures have not been approved by the maintainer. Human
in-game camera and two/four-player PvP reviews remain pending.

## Personality

Tiny fighters brawl on a camper's chopping stump while its owner has logged off beside the fire, leaving a phone with no service in the leaves.

## Fight premise

The broad stump top remains the main combat space. Jump the northern axe split to
intercept an opponent, or follow the continuous ground route around its southern
tip. Two bracket-fungus shelves below the east and west rim catch low flank exits
and offer a lower position from which to return or ambush. North and south retain
open knockout edges. The shelves are solid, static terrain, including their
undersides; they are not pass-through platforms or automatic rescue surfaces.

## Composition decisions

Camera bearings below name the camera's position around the stump: bearing 0° is
on +Z looking south; bearing 180° is on -Z looking north toward the camp. Gameplay
capture yaw instead names the direction the camera looks: capture yaw 0° looks
north toward the camp.

1. **Chopping stump:** from bearing 0°, the two shoulders frame the split in the
   center; the connected southern combat floor lies beyond its tip. Reduce the
   stump from roughly 32 to 29 units across, keeping an irregular organic outline,
   an uncluttered flat top, and a 3-unit crossing at Z=6. Painted rings and hairline
   cracks describe the wood without introducing physical lips.
2. **Fungus shelves:** from bearings 90° and 270°, one broad bracket fungus sits
   below the foreground rim. Its flat top is 1.5 units below the fighting floor,
   projects about 4 units beyond the bark, and extends about 9 units along it.
   Each shelf is a deliberate recovery/ambush position, reachable from the rim
   with one full jump back up across the current roster. Clear banded tops and
   rounded lips explain the landable silhouette; their undersides remain solid.
3. **Spawns and recovery:** keep four inward-facing spawns at X=±7, Z=±5, with
   diagonal opposites first. The bark and split walls continue to the lower death
   plane. From low views, show a solid trunk and an open split, with no hidden
   bridge, route under the top, or surviving forest floor.
4. **Campsite landmark:** from bearing 180°, the tent fills the left background
   third, fire occupies the middle distance, and a folding chair balances the
   right. A lantern and enamel mug rest on a log seat; boots, backpack, woodpile
   and an axe in a separate chopping round explain the camper's absence. The camp
   replaces the old treehouse and vendor groves. It is the sole background hero,
   built as complete volumes that also hold from side and intermediate views.
5. **Forest and scale:** from all bearings, tree trunks as broad as buildings rise
   into a high canopy, with giant ferns and grass beneath them. Separated near,
   middle and far silhouettes establish an ant-scale forest, leaving an open
   sunset window toward the west-southwest. The quieter southern view has a fallen
   log and understory rather than another camp. Haze and canopy gaps carry depth.
6. **Forest floor and joke:** at the bottom of low views, roots meet a forest floor
   about 24 units below the stump rim, safely below the kill plane. Moss, litter,
   mushrooms, pinecones and acorns describe that ground. A face-up phone south of
   the stump reads NO SERVICE; the camp sign reads NO WI-FI / JUST LOGS. Neither
   prop becomes a playable surface or a competing distant landmark.
7. **Sunset:** the low warm sun and lavender ambient light keep the fighting
   surface readable while campfire, lantern and tent glow begin to show. Sparse
   fireflies and the fire animate through existing presentation shaders. Preserve
   warm wood, muted greens, amber light and cool distant shadows without making
   the fight as dark as a night stage.

**Busy here / empty here:** detail belongs to the stump sides, forest floor and
camp beyond the recovery space. Keep the stump top, split landing shoulders,
shelf tops and immediate recovery corridor clear. Use uneven woodland density and
an open western sky; do not surround the fight with equally loud landmarks.

## Negative decisions

- No log stools or other obstacles on the main fighting floor.
- No small raised bark rim, moss bump or decorative lip that blocks movement.
- No bridge, decorative axe or prop through the lethal split.
- No moving terrain, hazards, bounce pads, wind gameplay or automatic rescue.
- No surviving old treehouse or second hero landmark.
- No cosmetic Colliders, Rigidbody components or scripts that decide gameplay.
- No solid background triangle inside the authoritative blast corridor, and no
  apparently safe floor above the lower death plane.
- No new vendor asset dependencies; use original procedural art and existing
  project shaders.

## Palette & lighting

Sunset campfire: honey and weathered brown wood, cream-and-russet turkey-tail
fungi, subdued fern greens, an orange tent and dark teal chair. Amber fire and
lantern light contrast with lavender sky light and rosy distant haze. The pale
stump top and readable shelf lips take priority over glowing scenery.

## Camera vantages

- Bearing 0°: into the split, both shoulders and the connected ground beyond it.
- Bearing 180°: open foreground floor, split and campsite beyond.
- Bearings 90° / 270°: each shelf, its attachment to the bark and return to the rim.
- Intermediate bearings: full campsite volumes, tree separation and the sunset gap.
- Elevated orbit: every 15°; low recovery views: every 45°.
- Top: shell outline, shelf silhouettes, four spawns and continuous ground route.
- Actual Training orbit: check materials, exposure, bloom, HUD and fighter contrast.

## Authoritative placement

The source scene remains `Assets/Stages/log_off/log_off.unity`; its existing mesh
asset is rebuilt from `tools/blender/log_off/layout.py`. The baked arena owns
collision and blast planes. Stump top is Y=0, shelves Y=-1.5, lower death plane
Y=-10, upper death plane Y=20. Side planes are X=±28.5, Z approximately -23.88
and +24.943. Spawn markers retain their existing positions and facings.

Original art is generated from the verified baked shell. Its report clips actual
background triangles against the blast corridor, checks the nearby camera envelope,
and verifies top, wall and shelf extents. The forest floor lies below the lethal
space. Native inspection and rendered views remain separate from human acceptance.
