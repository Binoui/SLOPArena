---
key: splash_deck
status: accepted
locked: 2026-10-16
approved_by: maintainer
approval_scope: selected boat hull with the same fight, Mediterranean fishing harbour and sunny morning implementation
accepted: 2026-10-17
blockout:
  - tmp/splash-deck-harbour/tuned-pvp/
  - .ability-lab-cache/splash-deck-harbour-20261016/orbit-01/
---

# Splash Deck — fishing boat in a harbour bay

On 2026-10-16 the maintainer asked to rework Splash Deck with the procedural stage
pipeline: the fight is on a boat in a bay. From the presented options they selected
**Boat hull, same fight**, **Fishing harbour** and **Sunny morning**. Those choices
authorize this bounded rework of the 2026-10-10 moored barge. The implementation scope
is decided. The maintainer accepted the built stage on 2026-10-17 ("the maps are much
better"). The key `splash_deck` and the
display name are unchanged.

## Personality

Four fighters brawl on a fishing boat anchored in a Mediterranean harbour, right under the wheelhouse's NO FIGHTING ON DECK sign.

## Fight premise

The barge's proven fight survives on a real boat. The broad teak deck is the main
combat floor, with the same four diagonal spawns and facings. Two raised spots, one
full jump high, keep their positions: the aft one is now a low wheelhouse and the
forward one a fish-hold hatch. The deck gains a bow that narrows to a point ahead of
the hatch and a square transom astern. The side edges stay 11 m from the centre line.
The hull drops straight from the deck edge to the water, a solid recovery wall with no
pocket beneath the deck, and falling into the water is the knockout. There is no
rocking, buoyancy, swimming or moving collision.

## Composition decisions

Camera yaw below names the direction the gameplay camera looks: yaw 0° looks north
(+Z), 90° east (+X, towards the bow and the bay mouth), 180° south and 270° west
(towards the stern and the village). Coordinates are recorded separately by
`tools/blender/splash_deck/layout.py` and `bay.py`.

1. **Fishing boat.** From every yaw the deck fills the foreground and centre:
   bleached teak planks running bow to stern, caulked seams, and a red margin board
   that follows the authoritative outline. The parabolic bow and rounded-quarter
   transom are the functional silhouette; the deck edge is a flush painted margin
   with a small chamfer, not a rail.
2. **Wheelhouse and fish hatch.** The aft raised spot is a white timber wheelhouse
   with blue trim, windows on every side and the ship's wheel visible through a front
   window. Its forward face carries the NO FIGHTING ON DECK sign towards the main
   deck. SLOP 04 on its roof reads from the main deck. The forward raised spot is a
   red-coamed fish-hold hatch with weathered boards, a heaped green net and a coiled
   line painted flush on top. Both tops are flat and clear.
3. **Painted hull to the water.** From side and recovery views, a luzzu-style hull
   (gunwale cap, white bulwark band, yellow sheer strake, rub rail, cobalt planking,
   portholes, painted bow eyes and SPLASH DECK on both bows and the transom) drops
   straight from the deck edge to a white boot-top at the waterline. The painted
   strakes dip towards midships and the boot-top rises at the bow, so the straight
   deck edge reads as a curved boat. Nothing stands proud of the hull.
4. **Flush working deck.** From higher pitches: rope coils, a flattened net with its
   float line, a few fish and scales by the hatch, wet patches by the hatch and the
   transom, scuppers and fairleads in the margin. All flush; nothing on a walkable
   surface blocks or supports a fighter.
5. **Village, the hero, to the west.** At yaw 270° pastel houses with shutters,
   balconies and terracotta or flat roofs climb the hill above a straight stone quay.
   The bell tower rises from the middle, and the striped PESCHERIA market awning and
   small boats moored bow-in line the quay. Every house is a complete volume, so the
   village holds from the north-west and south-west intermediate yaws.
6. **Breakwater and lighthouse to the north-east.** At yaw 30°–60° a rubble
   breakwater with a concrete walk and parapet runs out from the north shore. It ends
   in a red-and-white lighthouse that closes the bay's north-east side. Small fishing
   boats and a few yachts ride at moorings in the inner harbour behind it.
7. **Olive hills and chapel to the south.** At yaw 180° rocky garrigue slopes with
   outcrops rise to olive terraces and a white chapel with a terracotta dome, reached by
   an avenue of cypresses. A small sandy cove with parasols sits in the south shore.
8. **Bay mouth to the east.** At yaw 90°–120° the bay opens to the sea between the
   lighthouse and a rocky southern headland with stone pines. The morning sun's glitter
   path, yachts at anchor and under sail, and low hazy islands on the horizon fill the
   view. The open water is intentional.
9. **Water, sky and quiet movement.** Clear cobalt water around the boat turns
   turquoise over the coastal shelves. Gentle shader swell, ripples, sun glitter and
   foam lap the hull waterline and the shores. Gulls glide over the bay, clouds drift
   inland, and awnings, laundry, trees and sails sway in the breeze. All movement is
   cosmetic; the boat and all authoritative geometry are static.
10. **Below, above and far away.** Below the deck there is only hull and water, and
    the opaque sea hides everything under its surface. Above, open sky with a few
    clouds and gulls stays clear of the camera envelope. Far away, the hills, headlands
    and horizon islands complete every yaw.

**Busy here / empty here:** detail belongs to the village and quay (west), the inner
harbour and breakwater (north and north-east) and the southern hills. The deck's open
centre, both raised tops, the water ring immediately around the hull, the bay mouth
and the sky above the fight stay comparatively quiet.

## Negative decisions

- No mast, rigging, railing, bulwark, crate, net drum, bucket or other raised prop on
  a walkable surface; no roof clutter on the wheelhouse or hatch.
- No fender, tyre, lifeboat, bowsprit, anchor or other gear standing proud of the
  hull inside the blast corridor (decals at most 2.5 cm proud).
- No pier, quay, buoy or moored boat within the camera envelope or blast corridor, and
  no apparently safe surface near the hull. The visual-only sea is the one declared
  exception inside the corridor, 0.6 m above the KO plane.
- No rocking boat, buoyancy, swimming, water collision or moving geometry.
- No second hero landmark: the bell tower and lighthouse stay subordinate to the boat.
- No vendor (TropicalEnvironment) dependency; original procedural art on the project's
  stage shaders plus the presentation-only `SLOPArena/StageWater`.
- No cosmetic Colliders, Rigidbody components or gameplay scripts.

## Palette & lighting

Sunny late morning. A crisp east-south-east sun at 36° lights the village front-on
and puts a glitter path in the bay mouth. Blue sky light falls over warm ground bounce
with a pale sea haze towards the horizon. The honey teak deck and its red margin carry
the strongest value contrast in the frame. The boat is cobalt, white, yellow and red.
The village is ochre, apricot, rose, lemon and white with terracotta roofs and green or
blue shutters. The hills are dusty olive, garrigue and grey-ochre rock under dark
cypress and stone pines. Stage-owned lighting must read in PvP without Training's
extra lights.

## Camera vantages

- Actual gameplay camera from Spawn_01: eight directions and two higher views, under
  stage-only PvP lighting and Training's scene lights.
- The wheelhouse sign from the main deck; the hatch top from the deck and from above.
- Low recovery views along each side, round the bow and the transom: continuous hull,
  waterline foam, no under-deck pocket.
- Full elevated orbit at 15° intervals and low views at 45° intervals.
- The maintainer accepted the built stage on 2026-10-17.

## History

- **2026-08 to 2026-09:** a floating boardwalk in a tropical vendor-asset demo scene.
- **2026-10-10:** a static moored cargo barge (30 × 22 deck, two cargo hatches,
  closed recovery hull to −12) with a TropicalEnvironment harbour, palm islands and an
  outer island chain. Provisional production was authorized while the maintainer was
  away; the brief stayed draft and the roster rated the fight "Good".
- **2026-10-16:** this rework. It keeps the barge's spawns, facings, raised spots,
  side edges and blast planes; adds the boat outline; and replaces every barge
  presentation root with procedural art. Backups of the barge source scene, arena,
  prefab, briefs and tests are in
  `.stage-authoring-cache/splash_deck/fishing-harbour-20261016/backup/`.
