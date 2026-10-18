---
key: splash_deck
display_name: Splash Deck
target: pvp
players: 2-4
variant: static
authoritative_capability: null
availability: accepted
competitive_intent: mixed
topology: broad fishing-boat deck with an aft wheelhouse and a forward fish hatch, solid hull walls to the KO plane
source_scene: client/Unity/Assets/Stages/splash_deck/splash_deck.unity
baked_arena: data/arenas/splash_deck.arena
cosmetic_prefab: client/Unity/Assets/Resources/Stages/splash_deck.prefab
design_brief: docs/design/stages/splash_deck.design.md
human_review:
  owner: project maintainer
  status: accepted 2026-10-17
  process: gameplay-camera orbit and recovery review, then normal PVP at 2 and 4 players
---

# Splash Deck — Fishing Harbour

Four fighters brawl on a fishing boat anchored in a Mediterranean harbour bay on a
sunny morning. The boat is the stage and the hero landmark; the village, breakwater
and lighthouse, olive hills and bay mouth surround it at a distance. The wheelhouse's
NO FIGHTING ON DECK sign carries the joke.

The maintainer selected the boat hull with the same fight, the fishing harbour and the
sunny morning on 2026-10-16; the [design brief](splash_deck.design.md) records that
implementation scope. This replaces the 2026-10-10 rectangular barge and its vendor
tropical port. Human in-game visual-design and two/four-player PvP acceptance remain
separate from the automated evidence below.

## Gameplay shell

The barge's rated fight is kept and given a boat outline. The deck is flat at Y=0 and
36 units long (X −15.5 to 20.5) by 22 wide (Z ±11). A square transom at X=−15.5
(|Z| ≤ 8) turns through rounded quarters (radius 3) into straight sides at Z=±11,
which run forward to X=3. From there the parabolic bow narrows to the stem at X=20.5:
half-beam = 11 · (1 − u²), with u running from 0 at X=3 to 1 at the stem.

The two raised spots keep the barge positions and are one full jump high for the
whole roster. The aft **wheelhouse** covers X −13.5..−7.5, Z ±2.5 and the forward
**fish hatch** X 7.5..13.5, Z ±2.5. Both have tops at Y=1.5 and rest on the deck.
Four spawn markers keep (−6, 0.85, −5), (6, 0.85, 5), (−6, 0.85, 5) and
(6, 0.85, −5) with their diagonal facings of 45°, 225°, 135° and 315°.

The hull is one closed prism from the deck to Y=−12 on the same outline, so there is
no pocket or overhang under the deck. Wall contact does not add climbing, wall jumps,
sticking or automatic rescue, and ledge grabs remain disabled. The empty
`AuthoringAids/KillPlane` marker at Y=−12 keeps the lower death plane on the hull's
lowest vertex.

| Boundary | Baked value |
| --- | --- |
| Left / right blast | X=−25.5 / +30.5 |
| South / north blast | Z=−21 / +21 |
| Lower / upper death plane | Y=−12 / +21.5 |

The source scene's `GameplayGeometry` holds three static meshes built from
`tools/blender/splash_deck/layout.py`: `SplashDeckHull.asset`,
`SplashDeckWheelhouse.asset` and `SplashDeckFishHatch.asset`. The normal stage baker
produces 236 collision triangles and four spawns. Shared/GameServer and client
prediction load the same `.arena`; Unity presentation never decides collisions,
landings or deaths.

## Presentation

Original procedural art from `tools/blender/splash_deck` replaces every barge
presentation root in the local Resources prefab. Those were the trims, preview
boardwalk, recovery-hull presentation, warm deck fill and the vendor "Distant tropical
port". The prefab no longer references the TropicalEnvironment pack.

- **Deck.** The deck lies exactly on the authoritative top. Its texture holds teak
  planks with staggered butts, a red margin board nibbed along the real outline,
  scuppers, flush rope coils, a net with its float line, fish, wet patches, and painted
  contact shade round both raised spots. A 5 cm chamfer rounds the edge inside the
  outline.
- **Hull.** The hull walls follow the outline from the chamfer to just under the
  water. The painted strip carries the gunwale cap, white bulwark band, yellow sheer
  strake, rub rail, cobalt planking, portholes, luzzu bow eyes, hawse pipes, draft
  marks, SPLASH DECK on both bows and SPLASH DECK / PORTO SLOP on the transom, then a
  boot-top and antifouling. Painted strakes dip towards midships and the boot-top rises
  at the bow, so the straight deck edge reads as a curved boat.
- **Wheelhouse.** White siding with blue trim, windows on every side (the ship's wheel
  shows through one front window), life rings, a door and portholes aft. The sign faces
  the main deck, and SLOP 04 on the roof reads from the main deck. Decals stand at most
  1.2 cm proud and corner posts 1.5 cm.
- **Fish hatch.** A red coaming with steel corner caps and seams under weathered
  hatch boards, a heaped net with orange floats and a coiled line.

The bay is about 300 m across with its mouth facing the morning sun.

- **West: village and quay.** Two hundred terraced houses stand on the hillside with
  shutters, balconies, terracotta or flat roofs and laundry. A bell-tower church rises
  among them. Below, a straight stone quay carries bollards, lamps, crates, nets, the
  striped PESCHERIA fish market and small boats moored bow-in.
- **North-east: breakwater and lighthouse.** A rubble breakwater with armour blocks
  ends in the red-and-white lighthouse. The inner harbour behind it holds moored
  fishing boats and yachts.
- **South: hills and chapel.** Garrigue slopes with rock outcrops, olive terraces and
  maquis rise to a domed white chapel at the end of a cypress avenue. A small sandy
  cove with parasols sits in the shore.
- **East: the bay mouth.** A southern headland with stone pines frames the opening.
  Yachts lie at anchor or under sail, with low hazy islands on the horizon.

The sky dome carries a sun disc on the light's bearing, with drifting clouds and
gulls gliding on looping lanes. All animation is shader-driven: swell, ripples and
foam, cloud and gull travel, and wind on trees, awnings, laundry and sails.

The sea uses the new presentation-only `SLOPArena/StageWater` shader. It provides an
analytic swell that fades with distance and is limited to 12 cm, two scrolling ripple
normal maps, sky reflection by fresnel, sun glitter, received shadows, and lapping
foam where vertex alpha marks the hull waterline and shores. The water is visual-only
at Y=−11.4, 0.6 units above the KO plane, so a fighter visibly drops into it as they
are knocked out. It supplies no collision, buoyancy or swimming.

The prefab owns the morning sun at azimuth 112° and elevation 36° with soft shadows,
a soft sky fill over the deck, and its atmosphere: trilight sky ambient and pale sea
haze fog from 170 to 1500 units. It contains no Colliders or gameplay-owning scripts.

## Rebuilding

The [generator guide](../../../tools/blender/splash_deck/README.md) documents the build
and the shared apply command `sloparena.stage.art-apply --stage splash_deck`, which reads
the generator's `presentation.py`. The accepted generated art, the Resources prefab, the
generator source, layout specification, water shader and Shared tests are all in the
repository. Source/arena hashes in the export stamp must match
before art can be applied, and application preserves the baked data and prefab GUID.

## Verification — 2026-10-16

- `layout.py` rebuilt the collision source in one gateway batch. The native bake wrote
  236 triangles and four spawns, inspection found matching source/baked hashes, and the
  strict Python reader confirms every baked triangle, blast plane, spawn position and
  facing against the specification.
- Focused `SplashDeckArenaTests` and ArenaShipping: **459 passed, zero failures**. The
  402 Splash Deck cases cover:
  - the baked shell, blast planes, spawns and facings
  - deck and raised-top heights, and no surface beyond the bow and quarters
  - hull walls at every third edge midpoint and vertex (stem and transom corners
    included) at Y=−2 and Y=−11 for the whole roster, blocking inward and releasing
    outward movement
  - both raised spots blocking from eight directions
  - one full jump onto each raised spot for each fighter

  The barge-only recovery test was removed from `ArenaShippingTests`.
- Full Shared suite: 2,826 passed, two skipped, zero failed. Server build: zero
  warnings or errors. Both used `SkipUnityPluginCopy=true`; no Shared or server
  gameplay code changed.
- The generator reports about 198k triangles before import, zero clearance problems
  (no background face in the blast corridor, nothing above the envelope ceiling within
  25 units of the stage box, travelling gull lanes checked along their whole path), the
  deck on the top, hull and raised dressing within 2.5 cm, and the sea flat at its
  level. These are structural checks, not measured player-build performance.
- Final native inspection passes with matching source/baked hashes, 236 collision
  triangles, four spawns, 25 renderers, 178,387 imported triangles, nine materials on
  the supported stage shaders, zero cosmetic Colliders, two local lights and no
  diagnostics. Applying the art removed every barge root and 38 orphaned sub-assets. The
  generator reports 202,415 triangles before import, zero clearance problems and a clean
  mesh-health count (no flipped edges, degenerate faces or loose vertices). These are
  structural metrics, not measured player-build performance.
- Composited gameplay-camera captures were reviewed over four passes
  (`tmp/splash-deck-harbour/pass-0{1,2,3}-pvp/`, then `tuned-pvp/` and `tuned-scene/`).
  Each pass's defects were fixed in the generator and recaptured. The fixes were:
  - the AO-greyed wheelhouse, now excluded from the bake
  - the roof number, now read from the main deck
  - the greenhouse-like hatch tarp, now hatch boards with a net heap
  - the box-like hull, given a painted sheer
  - the neon and smeared hills, darkened, with outcrops added
  - clouds lowered into view
- Training's extra key/fill/rim lights brighten the stage much more than PvP's stage-only
  lighting. Lit tints of 0.82–0.9 and a 1.3 sun keep PvP's sunlit look while pulling
  Training back from blow-out; Training remains visibly brighter. Human visual review can
  accept or revise that difference, as for Log Off and Table Manners.
- An isolated Edit Mode orbit (24 elevated views at 15° and 8 low recovery views at 45°)
  shows complete backgrounds in every direction. It also shows the continuous painted hull
  with its boot-top and foam at the waterline, and no gap, pocket or floating dressing
  (`.ability-lab-cache/splash-deck-harbour-20261016/orbit-01/`). Three background
  defects were then fixed and re-checked with close renders (`orbit-02/`, `detail-0{1,2}/`):
  - the sun halo now blends into the dome
  - the breakwater parapet faces were wound into the parapet, so it showed as a floating line
  - aliased terrace lines made pale smudges on the hills

  The temporary scene was discarded and the clean Frontend restored each time.
- The Stage Select thumbnail is a new 1280 × 720 render of the starboard bow and the
  village, with its GUID preserved.
- The Editor closed normally partway through the session. With the maintainer's approval
  it was reopened with `unity open client/Unity`; the remaining steps then ran in
  bounded gateway leases. The final Editor is stopped on the clean Frontend with an
  empty error console and free/settled ownership.

Receipts and the barge backup are local under
`.stage-authoring-cache/splash_deck/fishing-harbour-20261016/`. Accepted by the maintainer on 2026-10-17 after the stage pass ("the maps are much
better"). Frame time and the packaged build were not measured separately.
