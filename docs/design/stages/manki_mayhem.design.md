---
key: manki_mayhem
status: accepted
locked: 2026-10-27
accepted: 2026-10-27
approved_by: maintainer
approval_scope: Manki Mayhem game-show set; picks "game show" setting, "one open floor", "one fixed perch" for Manki; concept take B "pirate broadcast" (TV + DIY look, no pools); procedural build
blockout:
  - tmp/manki-mayhem-concepts/B_pirate_broadcast.png
---

# Manki Mayhem: Pirate Broadcast

On 2026-10-27 the maintainer proposed **Manki Mayhem**, a multiplayer Arcade mode in
which Manki hosts a WarioWare-style show: each round he shouts a call ("BREAK OUT!",
"SCATTER!", "JUMP!") and throws bombs, rockets and axes that the fighters dodge.
Survivors score; knockback and recovery work as in a normal fight. The maintainer wanted
the mode to have its own map, then picked the recommended **game-show** setting, **one
open floor** and **one fixed perch** for Manki. From three painted takes they chose
take B, the **pirate broadcast** ("2 is amazing … I love the TV and DIY look"), and
ruled out pools. Those picks lock this brief. The maintainer judges the built stage in
game; acceptance is pending.

The shell is static geometry with a Mayhem Arcade section. Shared owns the round
loop and SCATTER! hazards in ADR-0044 step 1; Unity presentation and the show camera
follow later. Training can still use the bare deck without the Mayhem rule. The
stage joins `roster.json` in the online integration step after acceptance.

## Personality

Manki runs a pirate TV game show out of a warehouse, and tonight's contestants are
whoever is standing on his stage when the bombs start flying.

## Fight premise

One open, flat deck with nothing on it: all cover and obstacles come from the mode's
rounds (BREAK OUT! walls, crates), which spawn on the floor and sink away. Fighters
get knocked off any edge and recover against the deck's sides with ordinary jumps and
kit mobility, as on Slop Pit's floor. Every throw comes from the host booth behind the
north edge, so danger always arrives from one readable direction. Competitive intent:
**playful** (the mode sets the fight), with a flat, neutral shell.

## Composition decisions

Camera yaw names the direction the gameplay camera looks: yaw 0° looks north (+Z),
toward the booth. Yaw 0° is the **show view**, the one the mode's camera will use.

1. **The deck is the hero of every view.** A flat, raised deck fills the centre from
   the default camera. Its top is worn grey concrete panels with saw cuts, a
   yellow-black hazard band along every edge, and flush stencils: a faint MANKI MAYHEM
   crown print in the centre, ON AIR and STAY ON THE STAGE near the edges, taped cable
   runs. Nothing stands on it. It is the calmest surface on screen.
2. **The deck sides sell the DIY build.** From the edge down to the KO line, the sides
   are scaffolding and plywood hoarding with a hazard-striped kick plate, a calm dark
   band under the edge, then hanging chains, cable bundles and pasted show posters.
3. **The host booth is the one hero landmark.** It's centred behind the north edge
   (top centre of the show view) and raised above deck height: a box built from
   scaffolding, crates and speaker stacks, with a mic stand, a pile of bombs within
   reach, and a hand-painted MANKI MAYHEM banner across its front. It stands beyond the
   blast corridor. It carries an empty, named anchor where the mode will put the
   animated Manki. With no mode running, the booth is "off air" but still dressed.
4. **CRT screen walls flank the booth** (left and right thirds of the show view): a
   large central screen in each, surrounded by stacks of scavenged CRTs. They show the
   MANKI MAYHEM card, Manki's face and static, animated with shader flicker and
   scanlines. Later the mode drives them with the call card ("BREAK OUT!"). They're the
   second-loudest thing on screen, after the deck.
5. **Lighting rig and string lights.** Scaffold trusses frame the set with warm string
   bulbs (chasing), par cans aimed at the deck and floodlights on the booth tower. They
   draw the outline of the set in warm dots against the dark warehouse.
6. **The crowd behind fences, east, west and south.** Scaffold stands beyond the
   camera envelope, behind chain-link fences, packed with low-poly fans who hop and
   pump their fists (StageNeon bob). A few hold glowing phones or hand-made signs.
7. **Side identities.** North: the booth and screen walls. East: an APPLAUSE light box
   that blinks, and the loudest stand. West: the contestants' entrance, a curtain
   doorway under a CONTESTANTS light box, with a chalk scoreboard beside it. South
   (behind the show view): the broadcast desk, with a big TV camera on a dolly, a
   director's table of monitors and a red ON AIR lamp.
8. **The scene world.** Below: the deck's scaffold tower drops into a dark warehouse
   floor far under the KO line, with crates, cable spools and a few work lamps. Above:
   a low, dark warehouse ceiling with beams over the stands and an open dark void over
   the deck. Far: concrete columns, roll-up doors and exit signs. The show sits inside
   a place, never on a platter.
9. **Busy here / empty here.** Busy: the booth, the screen walls, the stands and the
   rig. Empty: the deck (flush detail only), the air above the deck, and the dark void
   below the deck edges, which stays calm so falling fighters read.

## Gameplay shell

```text
              north: HOST BOOTH (Manki) between two CRT screen walls
        +------------------------------------------------+
        |  S3 (-5, 4)                        S2 (5, 4)   |
  west  |                                                |  east
  CONT- |            DECK, flat, top y = 0               |  APPLAUSE
  ESTANTS|                                               |
        |  S1 (-5,-4)                        S4 (5,-4)   |
        +------------------------------------------------+
              south: broadcast desk, TV camera, ON AIR
   deck 24 (x) by 20 (z), walls straight down to the KO line; spawns face the centre
```

The proposed shell is a rectangular 24 × 20 m deck (top y = 0, walls to the KO line),
four spawns at (±5, ±4) facing the centre, and blast planes derived from the bake as
usual. The size is a first pick for four fighters plus round props; it may change after
play. The baked `.arena` is the authority once it exists.

## Negative decisions

- No props, posts, cover, ropes or lights on or above the deck, and no extra platform
  or route. Round props are the mode's job, not the stage's.
- No pools or water anywhere (maintainer, 2026-10-27).
- No background inside the blast corridor, and nothing within the orbit camera's reach
  of the corridor that suggests a false landing.
- No bright sign or busy pattern directly under the deck edges.
- No moving geometry or hazard in gameplay space. Crowd, screens, bulbs and lamps
  animate in StageNeon, beyond the corridor.
- No second hero landmark competing with the booth. No vendor assets and no real brands.

## Palette & lighting

Night broadcast inside a dark warehouse: a near-black warm void, warm tungsten string
lights and par cans, orange-red CRT glow from the screen walls, and a warm-white key on
the deck. Accents: Manki orange, hazard yellow-black, CRT red-orange, and one red ON AIR
lamp. The deck stays neutral grey so fighters, their rims and the mode's telegraphs read
first.

## Camera vantages

- The show view: yaw 0° at default zoom, booth top centre, screens left and right.
- The four spawn views across the deck.
- Quarter turns at 10–15° pitch: north (booth), east (APPLAUSE stand), south
  (broadcast desk), west (contestants' entrance), plus intermediate yaws.
- Low recovery views along each side and corner.
- A south-south-east view toward the booth for the stage-select thumbnail.

## Production and verification (2026-10-27)

Implementation follows the numbered decisions above. This is an agent production
review; **status remains locked and maintainer acceptance is pending**. No roster
entry was added, no branch was switched, and no commit was made.

- **Shell — PASS:** `shell-apply --create` created the source scene from
  `tools/blender/manki_mayhem/layout.py`; the typed bake produced 12 outward-wound
  collision triangles. Deck x ±12, z ±10, top y=0, straight sides to KO y=−11;
  side blast planes x ±22 / z ±20 and top y=20. Spawn order is (−5,−4), (5,4),
  (−5,4), (5,−4), all at marker y=0.85, centre-facing (51.340°, 231.340°,
  128.660°, 308.660°). The strict Python check verifies triangles, spawn order/
  height/yaw and every blast plane against the bake. Source/baked inspection
  hashes agree: `8213b2f0a2b232a13a15d0ea928bd8f573fd8693a93a5cb5d0b431f2d8100132`.
- **Simulation — PASS:** 118 `MankiMayhemArenaTests` pass: shell/blasts/spawns,
  49 flat-deck samples, 64 side/corner recovery cases (four roster fighters,
  eight contacts, two depths), and all four spawn groundings for each fighter.
  Full Shared suite: **3,732 passed, zero failures/skips**, using
  `-p:SkipUnityPluginCopy=true`; TRX and logs in `tmp/manki-mayhem-stage/`.
- **Art/inspection — PASS:** final generator 131,032 triangles against a planned
  240,000 budget; Unity inspection counts 126,532 imported triangles, 11 renderers,
  four materials, three local lights, zero Colliders, no missing references or
  unsupported shaders. Mesh health: zero flipped edges, degenerate faces or loose
  vertices. Actual background faces clear both the blast box and a conservative
  orbit-camera envelope (15 m horizontal expansion, y −13..36). Top paint is
  exactly flush; side details stay within the existing 2.5 cm tolerance.
- **Playable dev path — PASS:** existing `photo.prepare --training true` loaded Manki
  on this unrostered stage; the native Training sequence observed 41 Shared ticks
  of running and a jump, then restored the original scene/config. Receipt and
  reviewed capture: `tmp/manki-mayhem-stage/playable-gateway.log` and
  `playable-proof/01-training-dev.png` below the same stage folder. This proves
  the native path, not physical-controller acceptance.
- **Host anchor — PASS:** Unity read-only inspection confirms exactly one
  `MankiHostAnchor`, position `(0, 4.25, 40.5)`, only a Transform, no children.
  The future host/mode is not implemented. Decorative bombs have no gameplay owner.
- **Pass order — PASS:** massing/key-light build was applied and all 27 art captures
  inspected before detailing. Repairs reduced concrete mottle, opened the front
  host window, made scaffold/hoarding framing visible and quieted the lower
  background towers. The detail pass added mic/bombs, extra CRTs, fence mesh,
  804 fans, signs/phones/posters, chains/cables, rig fittings and ground clutter.
  All 27 final art captures and both sets of six structural inspection images
  were inspected. Structural images replace materials and do not judge exposure.
- **Exposure:** no gameplay view is flagged; mean luma 34.9–80.3, maximum clipped
  pixels 2.48% including HUD/lamps. Eleven named close/recovery/below views flag
  dark (mean 6.7–27.6) because their framing emphasizes the locked near-black
  warehouse and unlit recovery void. They were inspected individually; the calm
  edge silhouette is preserved. North/east recovery visibility is a specific
  maintainer playtest consideration, not a human acceptance claim.
- **Performance sample:** Ultra 1920×1080, five seconds/2,938 frames: mean 1.70 ms,
  median 1.59 ms, p95 2.15 ms, worst 22.61 ms. This is Editor timing, not a packaged
  player or remote four-player result. Current error console was empty; Editor
  restored to its original stopped scene, ready, free and settled.

### Numbered decision conformance

| Decision | Verdict | Production evidence |
| --- | --- | --- |
| 1. Deck is the hero | **PASS** | Bare neutral concrete, saw cuts, hazard rim on every edge, faint crown/title, flush ON AIR/STAY ON THE STAGE and taped cables. Calmer mottle after massing review; no standing prop. Show, orbit and all four spawn views reviewed. |
| 2. DIY deck sides | **PASS** | Dark band under the lip, striped kick plate, plywood panels with embedded scaffold framing, cable bundles/chains and roster posters down to KO; scaffold tower continues to the warehouse floor. All eight recovery views reviewed. |
| 3. One north host booth | **PASS** | Raised scaffold/crate booth beyond the corridor and camera reach; speaker stacks, mic, decorative bomb pile, painted burst banner, OFF AIR card and verified empty MankiHostAnchor. Front bracing removed from the host window after massing. |
| 4. Flanking CRT walls | **PASS** | Two large logo screens with small salvaged CRT stacks, Manki portrait and static; StageNeon flicker and phased raster-strip scanlines. Static content only; no mode call is fabricated. |
| 5. Lighting rig and strings | **PASS** | Perimeter scaffold trusses, two warm chasing bulb strings per side, deck-facing par fixtures and booth work/flood fixtures. Complete local light rig; no rig crosses above the deck. |
| 6. Fenced E/W/S crowd | **PASS** | Three scaffold galleries behind chain-link, 804 low-poly fans with existing Slop Pit bob/fist-pump channels, glowing phones and handmade GO MANKI signs. All outside the camera envelope. |
| 7. Side identities | **PASS** | East blinking APPLAUSE and densest audience; west curtain/CONTESTANTS and chalk scoreboard; south TV dolly camera, director's monitor desk and red ON AIR lamp. Quarter turns and intermediates reviewed. |
| 8. Scene world | **PASS** | Scaffold foundations on a dark warehouse floor, crates/spools/work lamps below KO, columns/roll-up doors/exit signs at distance, ceiling/beams only over the galleries and dark open air over the deck. Below and orbit views reviewed. |
| 9. Busy/empty zoning | **PASS** | Detail stays in the booth/screens/stands/rig. Deck, overhead gameplay air and the fall corridor are clear; lower towers fade darker. Face-clipping checks enforce the forbidden regions. |

**Negative decisions — PASS:** no water/pools, vendor art or real brands; no new
platform, usable prop or hazard; no cosmetic Collider; no background within the
blast/camera envelopes; no bright sign under the edge; no second hero landmark.
Crowd, screens and practical lights animate only in presentation. No design
conflict was resolved by changing the contract; **no design DEVIATION is claimed**.

### Files, evidence and maintainer play

Stage source is in `tools/blender/manki_mayhem/` (README, layout, build, palette,
textures, presentation, preview and deck/booth/venue/parts motifs),
`tests/Shared.Tests/MankiMayhemArenaTests.cs`, this design brief and its sibling
production contract `manki_mayhem.md`. Generated source scene/mesh and art are under
`client/Unity/Assets/Stages/manki_mayhem/`; runtime assets are
`data/arenas/manki_mayhem.arena`, `Assets/Resources/Stages/manki_mayhem.prefab` and
`Assets/Resources/UI/Stages/manki_mayhem.png` (plus Unity metadata). Unaccepted art
and prefab remain locally ignored under existing rules; acceptance/closure and
shipping are separate.

Shared files: `tools/blender/stagekit/blender.py` adds an optional empty-anchor
argument to its FBX exporter; existing mesh-only calls retain byte-identical
output. A baseline-vs-current export and repeated anchor export both passed
(`tmp/manki-mayhem-stage/export-regression/report.json`). All twelve procedural
stage generators were also rebuilt before/after with `--no-bake --no-export`;
geometry digests match and all clearance checks are empty. Wibou Mall's unrelated
process-salted wall-banner hash required comparing that pair in one Blender Python
interpreter; the independent-process mismatch and the isolated passing comparison
are preserved in `tmp/manki-mayhem-stage/generator-regression/`. Its source/art was
not changed or applied. The optional Eevee show preview also ran and was inspected.
`TESTING-UNITY.md` adds
only this stage's launch/setup section, preserving its existing five checks.
No shared Unity code, shader, registry or `.gitignore` change was needed.

Final captures, **show view first**:

- `tmp/manki-mayhem-stage/final/gameplay/01-show.png` — yaw 0°, real gameplay camera.
- `tmp/manki-mayhem-stage/final/gameplay/02-northeast.png` through `08-northwest.png`,
  plus `09-level-north.png` and `10-high-north.png`.
- `tmp/manki-mayhem-stage/final/review/thumbnail.png` — SSE toward booth; installed
  by the shared thumbnail command.
- `tmp/manki-mayhem-stage/final/review/` — host/desk, four spawn poses, all four
  side/eight total recovery poses, show-wide and warehouse-below.
- `tmp/manki-mayhem-stage/final/exposure.json`; structural inspection at
  `.stage-authoring-cache/manki_mayhem/final/inspection.json`; build, gateway and
  test logs below `tmp/manki-mayhem-stage/`.

To play without adding the stage to the picker: while stopped, open
`Assets/Scenes/Arena_Offline.unity`, select **TrainingMatch**, set **Arena Name
Override** to `manki_mayhem` without saving the scene, and press **Play**. Stop,
reopen the scene and discard that override afterwards. Alternatively the existing
`sloparena.photo.prepare --stage manki_mayhem --fighter manki --training true`
dev path prepares a real, controllable Training match in memory; press Play,
then Stop and run `sloparena.photo.restore` through the gateway. Exact commands
are in `TESTING-UNITY.md` and the generator README.

Still worth improving after play: tune north/east recovery-wall readability if
fighter silhouettes disappear; judge how large the booth feels at the maintainer's
usual zoom; tune sparse little TV fixtures/paint character if desired. Deck size,
2/4-player spacing, controller feel, four-player readability and aesthetic
acceptance remain the maintainer's decisions. The future mode is separate work.

### Pass 2 — warmth, giant-host tower and quiet deck (2026-10-27)

This subsection supersedes pass 1's art/exposure values and improvement notes above.
The maintainer's pass-1 verdict was **“a bit dark and flat”**; the four requested
presentation revisions authorize the changes below. **Status remains locked**;
this is production verification, not aesthetic or human gameplay acceptance.
The current `main` checkout/branch was preserved and no commit was made.

1. **Warmth/depth — PASS.** Slop Pit decision 7's approach now lights the back wall
   of each east/west/south gallery, with scalloped orange pools and panel seams
   behind the dark fans, braces and chain-link. North has a separate backstage
   wall. Foreground trusses, three strings at different depths, crowd risers,
   quieter far warehouse walls, columns and roll-up doors read as separate layers.
   Strings use larger, mostly steady bulbs with a smaller pulse/chase component,
   rather than having two thirds disappear. Par/flood fixtures carry soft additive
   warm fins on the existing URP particle shader. Analytic CRT/host spill warms
   nearby gantries/crates, and a subtle unshadowed fill in the separate structure
   material makes recovery walls readable without relying on extra-light counts.
   The deck key/fill/material remain unchanged: four plain-deck sample regions
   in the identical show framing are pixel-identical between passes
   (`pass2/deck-exposure.json`). The first iteration's uniformly tan far walls and
   excessively hot backlight were reduced before final captures.
2. **Raised host tower — PASS, with a camera limitation recorded below.** The
   scaffold/crate tower carries a **20 × 10 m platform at y=6.5**, bounded by
   **x −10..10, z 37.5..47.5**. The unobstructed host bay is **9 × 6.5 m**, bounded
   by **x −4.5..4.5, z 38.5..45**, with **8 m headroom**. It accommodates a
   roughly 3–4× fighter-height animated host and his stance/gesture space. The
   exported **MankiHostAnchor is (0, 6.5, 41.5)**, at platform height. A read-only
   Unity check confirms exactly one anchor, only Transform, no children. Physical
   tower faces do not intersect the reserved bay (`pass2/host-clearance.json`).
   Tall speaker stacks flank it, a scaled mic and decorative bomb pile sit beside
   the bay, and the front burst banner has its own bulb line and hero flood rig.
   Front wires split around the host window; the front scaffold terminates below
   his feet. Crate braces were moved behind the banner to remove intersections.
3. **Quieter deck — PASS for the requested revision.** Removed STAY ON THE STAGE,
   ON AIR, tape/cable graphics and the continuous top hazard stripe band. There
   are no large arrows. Only a smaller, very faint crown/MANKI MAYHEM print and
   little edge registration marks remain on the concrete; joints are flush and
   the original side kick-plate marks remain. No warning-ring/drop-line system
   or gameplay behavior is introduced.
4. **Fuller CRT walls — PASS.** Each large screen is now surrounded by **26 small
   CRTs** (52 total): ten in uneven side stacks, twelve in top/bottom rows and four
   low sets. Cabinet sizes, aspect ratios, depths and rolls vary; feeds alternate
   Manki portrait/static, with existing flicker and animated raster strips.
   Warm spill ties them to the supporting gantries rather than isolated glowing
   rectangles. The south production desk retains its four small monitors.

**Shell preservation — PASS.** `layout.py`, the source scene and `.arena` SHA-256s
match the saved pre-pass values (`pass2/preservation.json`); the strict layout
check still passes. No bake, spawn or collision edit was performed. The arena is
still 12 collision triangles with the four original grounded spawns. Generator
checks find zero actual background-face intersections with either the blast box
or the expanded orbit-camera envelope, and no invalid deck/wall vertices.

**Budget/mesh/import — PASS.** Final generator **185,508 / 240,000 triangles**
(54,492 spare); Unity **174,420** imported triangles, 14 renderers, five materials,
three local lights, zero Colliders, missing references or unsupported shaders.
Mesh health has zero flipped edges, degenerate faces or loose vertices. No new
vendor dependency, shared shader, Unity code or stagekit edit was needed in pass 2.
The optional shared exporter change documented above belongs to pass 1.

**Tests/native evidence — PASS.** All **118 MankiMayhemArenaTests passed**, with
zero failures/skips and `-p:SkipUnityPluginCopy=true`. This art-only pass did not
repeat unrelated Shared/server suites. Generation, apply, inspection, gameplay
and named camera capture, thumbnail installation and current console checks used
own bounded gateway leases; long jobs ran in `systemd-run --user --scope`.
The final capture session restored the original stopped Tutorial scene and Game
View/options, with an empty error console and free/settled ownership. The five
second Ultra 1920×1080 Editor sample observed 2,856 frames: mean **1.75 ms**,
median **1.62 ms**, p95 **2.24 ms**, worst **24.11 ms**. These are Editor figures,
not packaged-player or multiplayer performance proof.

**Exposure — PASS for gameplay and recovery.** `tools/capture_exposure.py` checked
all 28 final art captures (10 gameplay, 17 review, one additional host framing):

- Gameplay mean luma **56.8–94.1**, maximum clipped share **1.79%**, no flags.
- Same-angle show view: **60.3 → 77.4** mean luma; clipped share **1.74% → 1.51%**.
- All eight recovery views pass: mean **30.3–50.7**. North improves **6.7 → 31.9**,
  with crushed pixels **87.96% → 0.06%**; east improves **10.3 → 33.6**.
- Named review views clip at most **4.09%** (host close-up). The only remaining
  dark flag is `warehouse-below.png`, mean **21.5**, below the KO plane. Its broad
  quiet floor/support silhouettes were inspected; it is not a recovery-route view.
- The `--against` reports flag intentional mean differences above 20 in northeast
  and level-north, and several review views. The host/show-wide review camera
  framing changed deliberately and is not a like-for-like lighting comparison.
  Exposure script exit 1 reflects those explicit flags, not hidden check failure.

**Numbered locked-decision conformance after the approved revision:**

| Decision | Pass-2 verdict |
| --- | --- |
| 1. Deck hero | **DEVIATION, explicitly requested:** faint centre print/edge ticks replace edge stencils/tape/top striping; the deck is calmer and remains empty. |
| 2. DIY sides | **PASS:** original silhouette/dressing retained, recovery fill added. |
| 3. North booth | **PASS:** larger raised tower, giant-host bay and verified empty anchor. |
| 4. CRT walls | **PASS:** packed mismatched stacks, static/portrait/show feeds. |
| 5. Rig/strings | **PASS:** denser warm bulbs, visible spill/fins, north host opening clear. |
| 6. Fenced crowd | **PASS:** all 804 original fans remain, backlit silhouette separation restored. |
| 7. Side identities | **PASS:** APPLAUSE, entrance/scoreboard and broadcast desk retained. |
| 8. Warehouse world | **PASS:** layered warm risers, quiet far walls/columns/doors and below-KO foundations. |
| 9. Busy/empty zoning | **PASS:** empty deck/air/corridor, brighter recovery walls stay broad and calm. |

**Capture paths, show first:**

- `tmp/manki-mayhem-stage/pass2/gameplay/01-show.png` — same yaw 0° / pitch 15° as
  pass 1, for direct comparison.
- `tmp/manki-mayhem-stage/pass2/gameplay/02-northeast.png` through `08-northwest.png`,
  plus `09-level-north.png` and `10-high-north.png` — same names and angles.
- `tmp/manki-mayhem-stage/pass2/host-framing/01-show-host.png` — additional yaw 0° /
  pitch 8°, with the full tall host bay in view.
- `tmp/manki-mayhem-stage/pass2/review/` — same 17 names as pass 1, including
  `host-booth.png`, `show-wide.png`, four spawns and eight recoveries.
  `thumbnail.png` is installed through `sloparena.stage.thumbnail`.
- `tmp/manki-mayhem-stage/pass2/exposure.json`, `exposure-gameplay-comparison.json`,
  `exposure-review-comparison.json`, `deck-exposure.json`, `verification-receipt.json`,
  `build/build-report.json`, `dry-report.json`, `tests/pass2.trx` and gateway logs.
  Structural inspection: `.stage-authoring-cache/manki_mayhem/pass2/inspection.json`.

**Remaining improvement/limitation:** the standard 15° gameplay show framing crops
part of a future giant host. Keep that original shot for honest pass comparison;
the additional 8° yaw-0 shot and raised/wider show review pose demonstrate the
intended host framing. The future mode needs to choose such framing when it adds
Manki; no gameplay camera code or runtime configuration changed in this art pass.
Actual animated-host scale, gestures and visibility still need checking once his
presentation exists. The painting also has richer hand-painted material/weathering
than these deliberately simple procedural masses. Human 2/4-player readability,
controller acceptance and aesthetic acceptance remain pending.

**Changed scope:** stage generator modules `booth.py`, `build.py`, `parts.py`,
`presentation.py`, `textures.py`, `venue.py`, generator README, generated presentation
FBX/stamp/materials/textures/JSON/prefab and thumbnail, this Production section and
`manki_mayhem.md`. The only shared-file edit is the minimal existing Manki Mayhem
launch/capture guidance update in ignored `TESTING-UNITY.md`; its five unrelated
human checks were preserved. Unrelated dirty files were not staged or edited.

## Acceptance

The maintainer played the stage with the Manki Mayhem rounds (SCATTER!, JUMP!, BREAK OUT!) and accepted it
on 2026-10-27: "ive playted it, good enough. keep going". Camera framing and difficulty tuning are deferred to
later play. The generated art joins the repository when the Mayhem work lands.
