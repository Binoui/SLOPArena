---
key: nyx_manor
display_name: Two-Faced Manor
purpose: arcade
status: locked
locked: 2026-10-30
approved_by: maintainer
approval_scope: "Picks of 2026-10-30: direction B, Two-Faced Manor, from the three-direction board (A Grave Content, B Two-Faced Manor, C Real or Fake?); then layout 3, Up and Over, from the manor layout board (1 Wraparound, 2 The Grounds, 3 Up and Over), and building it (\"Go for 3 ... Go for it\"): the playable gray-box first, decor after the maintainer plays it; then the decor concepts B (dream-house front) and D (gothic back) (\"B looks good\")"
variant: static
blockout:
  - tmp/nyx-course/round2/index.html (layout 3, T3 worktree t3-9855b45f)
---

# Two-Faced Manor: Nyx's Slop Rally course

**Status:** the maintainer approved the playable gray-box and selected concepts B and D on
2026-10-30. The graveyard revision below is approved for implementation. Its installed geometry and art await in-game review. The front and roof collision stay fixed.

The brief (2026-10-30): a course for Nyx that every fighter can race, so not a lesson in her
kit, that still shows off her personality ("personalities haha"). From the first board the
maintainer chose the manor ("B is the best idea") but asked for multiple paths, nothing too
narrow and no indoor parts, because walls and ceilings fight the camera. From the second board
they chose Up and Over over a wraparound climb: "1 would get frustrating because you're being
knocked out of castle and have to climb everything. 3 is better because you can come up from
another path."

## Personality

Nyxxie renovated the front of the family manor for the photos; the back is still Nyx's, so the
race climbs the fake side and drops down the real one.

## Composition decisions

1. **A manor on a cliff at midnight, split front to back.** The front faces the road and is
   Nyxxie's renovation: pink paint, scaffolding with tarps, ring lights and her junk. The back
   hangs over a ravine and is Nyx's: black stone, crumbling balconies, candles, crows. The split
   runs along the roof, east to west, through the turret.
2. **The route goes up the front, over the roof and down the back.** It never goes indoors, no
   path runs under another and nothing overhangs a path. The house stays ahead of you on the way
   up and beside you along each tier; only the drop puts it behind you, where the sight holes
   (ADR-0043 decision 4) keep the fighter visible.
3. **Section 1, the front yard (Nyxxie's).** Start at the gate under a LIVE balloon arch, facing
   the house. An open lawn with an inflatable pool and piles of unopened PR boxes to weave
   through. Nyxxie's junk is the targets. The GIANT INFLATABLE BAT, a bonus target, sits in the
   middle of the lawn.
4. **Section 2, the scaffolding.** Three wide tiers across the whole front, each one storey up.
   At every tier there is a slow, safe way up and a quick, direct way, and the two swap places
   from tier to tier: ramps at the ends with box stacks in the middle, then a central double stair
   with box stacks at the ends, then ramps at the ends with window ledges in the middle. Knocked
   off a tier, you land on the one below and pick any way back up. FLAMINGO FLOATIE, a bonus
   target, stands in the middle of the second tier.
5. **Section 3, the roof.** From the top tier, a central double stair (safe) or the dormers at
   the ends (quick) lead onto the roof. The split turret stands in the middle: pink front with a
   giant ring light, black back with a crow weathervane. Two chimneys are obstacles. CARDBOARD
   NYXXIE, a life-size cutout and the last bonus target, stands in front of the turret on the
   pink half.
6. **Section 4, the drop (Nyx's).** Over the ridge and down the back: the back balcony, a
   terrace whose rotten middle is open to the ravine, a narrower lower terrace and the crypt
   ledge. The drop narrows like a funnel; a drop from too far out or through the rotten middle
   lands in the ravine. The real side has no targets. The finish is the family crypt's door.
7. **One landmark.** The split turret is the landmark, seen from the whole route. No second hero
   landmark.
8. **Scene world.** Below: the ravine, dark water and the kill plane. Above: a full moon. Far,
   front: the road and the lights of a town. Far, back: forest and sea beyond the ravine.
9. **Busy and empty.** The front is busy (junk, tarps, ring lights, scaffolding poles). The back
   is clean and quiet. The roof sits between them.

## Starting layout

Unity world axes: +X east, +Y up, +Z north. Boxes are `x0..x1, z0..z1` with their top. Every
solid stands on the kill plane unless noted. These coordinates come from the approved blockout and
are a starting point: production may move pieces to pass the route tests, within the rules after
the tables, and records each change here.

| Piece | Footprint (x, z) | Top |
| --- | --- | --- |
| Kill plane, the ravine | everywhere | -12 |
| Front lawn | -24..24, -46..-18 | 0 |
| Side lawns | -24..-16 and 16..24, -18..-6 | 0 |
| Tier S1 | -16..16, -18..-14 | 3.6 |
| Tier S2 | -16..16, -14..-10 | 7.2 |
| Tier S3 | -16..16, -10..-6 | 10.8 |
| House and roof deck | -16..16, -6..6 | 14.4 |
| Turret, not a route | -3..3, -3..3 | 22 |
| Chimneys | -10..-8.4 and 8.4..10, -0.8..0.8 | 16.2 |
| Back balcony B1 | -20..20, 6..10 | 10.8 |
| Terrace B2, two halves | -20..-2 and 2..20, 10..14 | 7.2 |
| Lower terrace B3 | -14..14, 14..18 | 3.6 |
| Crypt ledge | -10..10, 18..28 | 0 |
| Crypt, solid | -3..3, 23..27 | 3 |

| Climb | Safe way | Quick way |
| --- | --- | --- |
| Lawn to S1 | ramps at both ends along +Z, x -15..-11 and 11..15, from z -26 to -18 | box stacks in the middle, x -5..-3.4 and 3.4..5, z -19.6..-18, top 1.8 |
| S1 to S2 | double stair in the centre along X on S1, flights from x ∓12 up to ∓3 | box stacks at the ends on S1, x -15.6..-14 and 14..15.6, top 5.4 |
| S2 to S3 | ramps at both ends along X on S2, from x ∓15.6 up to ∓7 | window ledges in the middle on S2, top 9.0 |
| S3 to roof | double stair in the centre along X on S3, flights from x ∓10 up to ∓2 | dormers at the ends, x -15..-13 and 13..15, z -7.6..-6, top 12.6 |

Rules for any change: a required climb is a ramp of 30° or less or steps of 1.8 m or less;
required gaps are 3 m or less; tiers stay at least 4 m deep and paths at least 4 m wide. FightGuy
has the roster's lowest measured double jump (3.25 m, 2026-10-30), so these keep every fighter
able to finish without specials.

### Gray-box adjustments, 2026-10-30

The four-metre-deep starting tiers could not hold a four-metre-wide climb and a clear
walking lane beside it. S1, S2 and S3 are eight metres deep in the playable gray-box.
Their front halves remain clear four-metre lanes. Their back halves hold the climbs.
Heights, X coordinates, climb slopes, target counts and checkpoint prerequisites stay as listed above.

| Piece or marker | Production Z change |
| --- | --- |
| S1 | -18..-10, instead of -18..-14 |
| S2 | -10..-2, instead of -14..-10 |
| S3 | -2..6, instead of -10..-6 |
| S1 double stair and end boxes | +4 m |
| S2 ramps and window ledges | +8 m |
| S3 double stair and dormers | +12 m |
| House, turret, chimneys and every back piece | +12 m |
| S1 targets | unchanged |
| S2 targets and FLAMINGO FLOATIE | +4 m |
| S3 targets and checkpoint 2 | +8 m |
| Roof targets, checkpoint 3 and finish | +12 m |
| Lawn, side lawns, first ramps, first boxes, start and checkpoint 1 | unchanged |

The safe double stairs use continuous ramps with the same end heights. This keeps
all climbs below 30 degrees. The otherwise unspecified quick pieces use 1.6 m depth,
with end boxes at z -11.6..-10 and window ledges at z -3.6..-2. Their tops and X
positions follow the tables. Checkpoint 1 is centred at z -20, checkpoint 2 at z 0,
checkpoint 3 at z 16.5, and the finish at z 33. Safe respawns avoid the climb solids.
Checkpoint 1 spans y 0..5 instead of 0..3 so its sensor catches fighters climbing
the first ramps. Its lawn respawn and Z footprint stay fixed.

`tools/blender/nyx_manor/layout.py` is the exact production SPEC. The ordinary route,
all sixteen standing-start climb variants, front knockoffs and ravine deaths pass
for every roster fighter against its shell before the first bake. Decor remains deferred.

### Targets, checkpoints and finish

Targets in route order. Bonus targets take five hits, like Neo Mall's bosses; the rest take one.

| Section | Targets |
| --- | --- |
| 1 · Front yard | six on the lawn at (∓8, 1, -40), (∓14, 1, -32), (-7, 1, -23), (9, 1, -23); GIANT INFLATABLE BAT at (0, 1.6, -30) |
| 2 · Scaffolding | S1 (∓9, 4.6, -16); S2 (∓12, 8.2, -12); FLAMINGO FLOATIE at (0, 8.6, -12) on S2; S3 (∓12, 11.8, -8) |
| 3 · Roof | (∓10, 15.4, -3); CARDBOARD NYXXIE at (0, 15.8, -4.5) |
| 4 · The drop | none |

| Marker | Where | Requires |
| --- | --- | --- |
| Start | (0, 0, -43), facing north | |
| Checkpoint 1 | across the foot of S1, both ways up | section 1 (7 targets) |
| Checkpoint 2 | across S3 | through FLAMINGO FLOATIE (12) |
| Checkpoint 3 | across the roof's back half | all 17 |
| Finish | the crypt door, z about 21 | all 17 |

A checkpoint sensor never holds a target it requires; S3's own two targets sit inside checkpoint
2's sensor but come after its prefix.

## Negative decisions

- No indoor route, no path under another path, nothing overhanging a path.
- No moving parts and no course-only combat rules.
- No required target on the back, the real side.
- No ability gate. Every required climb fits the roster's lowest double jump; Sir Bonk's and Nyx's
  higher jumps may skip a step.
- No background geometry in the blast corridor. Nothing beside the house reads as a landing that
  is not one.
- No real brand names: the PR boxes and the LIVE sign are parodies.

## Palette and lighting

For the decor pass; the gray-box uses flat zone colours. Midnight under a full moon. The front is
lit by Nyxxie's pink ring lights and fairy lights (hot pink and white) and her pink paint. The back
is cold violet-grey stone with candlelit windows, Nyx's deep violet as its accent. The ravine is
near-black water.

## Art direction: dream-house front, gothic back

Picked on 2026-10-30 from three concept takes painted over the gray-box: **B, Dream house** for
the front ("B looks good") and **D** for the back. The concepts are local, in
`tmp/nyx-course/concepts/` (`B_dreamhouse.png`, `D_back.png`, prompts beside them); the takes not
picked were A, scaffolding renovation, and C, a painted film-set facade.

**The front, Nyxxie's makeover.** The joke is the seam: a glossy pink influencer dream house bolted
onto a black gothic manor, which shows above it.

1. Tier tops are smooth pale-pink stucco terraces with white trim; the vertical faces between tiers
   are deeper pink with heart-shaped windows lit warm from inside and pastel shutters.
2. The end ramps and the double stairs are pink marble-look stairways.
3. The quick step blocks are pink planters with little hedges and blank gift-wrapped parcel boxes
   with bows.
4. A huge pair of white angel wings is painted on the top wall as a selfie spot, flanking the
   turret; neon heart signs; climbing roses on the risers.
5. The turret: the front half glossy pink with a giant ring light, the back half black slate with a
   crow weathervane. Above the pink front the real house shows: black slate mansard roof, gothic
   chimneys, gargoyles and candlelit gothic windows.
6. The lawn: rose bushes and clipped hedges along the sides, lanterns on posts, an iron fence, the
   heart-shaped balloon arch at the start, an inflatable pink paddling pool, and plastic skeletons
   lounging in pink deck chairs as decor.

**The back, Nyx's side (concept D).** Crumbling black and violet-grey stone terraces with only stubs
of broken iron railing; the rotten middle shows as a collapsed floor; a small gothic crypt with a
heavy iron door and two candle lanterns on the lowest ledge; tall gothic arched windows with
candlelight, dead ivy and crows on the gutters; below, a ravine of jagged rocks and black water;
beyond, dead trees and a moonlit sea. Only a faint pink glow comes over the roof from the front.

**Targets are Nyxxie's junk** at the gray-box target positions: small inflatable bats, plastic
Halloween skeletons and pink ring lights on stands. The bonus targets are a GIANT inflatable black
bat with pink heart eyes, a GIANT pink flamingo pool float and a life-size cardboard cutout of
Nyxxie printed with her portrait (`Resources/UI/Portraits/Nyx_Fake.png`).

**Build rules for the art.**

- Collision stays the baked shell. Art adds no colliders and no surface that reads as a landing
  where there is none, and nothing in the blast corridor.
- No railing, fence, light string or prop along the open front edge of a walkable tier, ramp or
  terrace; such things read as walls. Fairy lights, roses and neon go on the risers and the house.
  The back's railing stubs stay short and broken.
- Walkable tops stay the brightest, clearest surfaces; the house and the scenery behind are darker
  and quieter. Night but readable, never murky.
- No text or real logos anywhere; parcels and signs carry hearts and bats only.

## Second half revision: Nyx's graveyard, 2026-10-30

The maintainer, from the gray-box captures: "we definitely [need] a more developed second half
because it feels like you can just jump from the top to the finish and that doesnt feel
interesting." The built back stacked every terrace under the roof, about 15 m from the crypt. From
three options on a board (cliff switchbacks, the graveyard, the bell tower) they picked **the
graveyard** ("2 is nice"). This section replaces composition decision 6 and the back rows of the
layout tables; the front, the roof, their targets and checkpoints 1 to 3 stay as built.

**Shape.** From the back balcony you cross the ravine to Nyx's own cliff-top graveyard, and the
family crypt is at its far end, so it can never be reached by dropping from the roof. Two ways
across meet at the graveyard gate: the ruined aqueduct high up, quick, with broken arches to jump;
or the sea rocks low by the water, safer, then the cliff stairs back up. Then a run through
gravestone rows, with mausoleum roofs as the quicker line, to the crypt.

| Piece | Footprint (x, z) | Top |
| --- | --- | --- |
| Back balcony B1, kept | -20..20, 18..22 | 10.8 |
| Aqueduct spans, high way | -2.5..2.5 at 22..28, 30.5..36, 38.5..44 | 9 |
| Sea rocks, low way, 3.2 m tops | centres (8, 25), (11, 30), (7, 35), (10, 40) | 4.5 to 5.1 |
| Cliff stairs, low way | x 8.5..11.5, from z 41.5 (y 5) up to z 49 (y 9) | ramp |
| Graveyard plateau | -18..18, 46..68 | 9 |
| Gravestone rows, hurdles | across the plateau | 9.9 |
| Mausoleums, quick line | -16..-11, 60..65 and 11..16, 56..61 | 11.4 |
| Crypt, solid | -3..3, 62..66 | 12 |

The old terraces B2, B3, the crypt ledge and its crypt are removed. A fourth checkpoint across the
graveyard gate, about z 47, catches both ways and requires all 17 targets; the finish moves to the
crypt door, about z 61. Same rules as the front: required gaps 3 m or less, climbs fit FightGuy's
double jump, ramps 30° or less, and the two ways stay within couch catch-up range of each other.

**Targets.** None on Nyx's side, as before; the maintainer was asked and did not choose, so the
back stays a pure race. Revisit after play.

**Art.** Concept D's materials carry over: crumbling violet stone, broken railing stubs, crows, dead
ivy, candle lanterns, the crypt with its iron door, the ravine's rocks and black water, dead trees
and the moonlit sea. The aqueduct is ruined gothic arches; the graveyard has leaning headstones,
iron fencing away from walkable edges, a crow tree and the mausoleums.

### Graveyard production adjustments

The existing front, roof collision, B1, all 17 targets and checkpoints 1 to 3
stay byte-for-byte equivalent in the SPEC. Only the back is replaced.

| Item | Production adjustment and reason |
| --- | --- |
| Plateau at the stair inlet | The main plateau is -18..18, z 49..68. Two gate aprons cover x -18..8 and 12..18 at z 46..49. This leaves a four-metre stair inlet instead of burying the ramp inside a solid plateau. |
| Cliff stairs | Widened from x 8.5..11.5 to 8..12. The original run, z 41.5..49, rises from y 5 to 9 at 28.07 degrees. Its cliff foundation reaches the kill plane. |
| Fourth rock | Centre z 39.9 instead of 40, so its 3.2 m top ends exactly at the stair foot, z 41.5. Heights in order are 4.5, 4.7, 4.9 and 5.0. Rock sides taper to fractured supports below the unchanged square tops. |
| Aqueduct structure | The table's footprints and y 9 tops stay fixed. Each span has a 0.9 m deck, two piers and an eight-segment open arch in both collision and presentation. There is no playable route below the arches. |
| Hurdles | Two rows each contain nine 1.1 m wide, 0.9 m high gravestones, with 0.9 m clear intervals, inside x -9..9 at z 52..52.8 and 57..57.8. Carved cross relief identifies them. Leaning headstones in side plots leave the main lanes clear. |
| Checkpoint 4 | Sensor centre 0,10,47, half-size 18,3,1.5. This catches the high landing and the low stair at y about 7.9. Safe respawn is -6,9.85,47 on the west gate apron. All 17 targets are required. |
| Finish | Sensor centre 0,10.5,61, half-size 3,1.5,1. The iron door is on the crypt's south face at z 62. |
| Branch proximity | At matching Z progress the low landing centres are at most 11 m from the high lane and 4.5 m below it. This fits the 20 m horizontal and 10 m below couch limits. |

The gate aprons are three metres deep because they join the main plateau directly;
they are not separate jump platforms. All aqueduct gaps are 2.5 m or less, including
the 2 m gate gap. The largest shortest horizontal separation between successive
rock-top rectangles is about 1.97 m. Mausoleum roofs rise 2.4 m from the plateau.

The start arch moves from centre 0,-44.8 to the west garden at -20,-43.
Its rise shrinks from 4.7 m to 1.6 m and its width from 7 m to 2.7 m.
It stands on the lawn outside the normal north-facing start view. The roof deck and the wall from
S3 to the roof are black slate and gothic masonry, with candlelit arched windows.
The pink terraces stop at S3. The turret front and ring light remain pink; the back
is black slate. Gables and gargoyles remain inside the existing chimney footprints.
The moon is a smooth, uniformly lit sphere that reads as a disc, with a generated
radial transparency halo instead of raised crater blobs. No text is added.

### Graveyard implementation conformance

These verdicts cover the approved implementation. Human controller, camera and
art acceptance remains pending through check #31. Local evidence is in
`tmp/nyx-course/graveyard/REPORT.md` and `board.html`.

| Brief item | Verdict | Installed result and proof |
| --- | --- | --- |
| Preserve the front, roof collision, 17 targets and checkpoints 1 to 3 | PASS | SPEC comparison preserves every protected tuple. Front knockoffs and all sixteen climb variants pass for all seven fighters. |
| Remove the old B2, B3, crypt ledge and old crypt placement | PASS | The scene uses the graveyard shell. Crypt keeps its identity at the new location. Retired terrace mesh copies remain only in local evidence. |
| High aqueduct crossing | PASS | Three five-metre spans, real open arches, gaps at most 2.5 m. Every fighter finishes the high route without specials. |
| Low sea-rock crossing and cliff stairs | PASS | Four 3.2 m tops, faceted supports, a 28.07-degree four-metre stair inlet. Every fighter finishes the low route without specials. |
| Graveyard, hurdles and mausoleum roofs | PASS | Plateau, two rows of nine separate gravestones and both 2.4 m roofs match the adjusted SPEC. All seven fighters can climb each roof. |
| Fourth checkpoint and far crypt finish | PASS | All-fighter tests save all four checkpoints. Native runs check 17 targets, four saves and zero falls. Sensor and respawn coordinates are recorded above. |
| No targets on the back | PASS | All 17 original target tuples remain before the crossing. |
| Prevent the roof-to-finish drop | PASS | Five X lanes from both back edges die without reaching the finish when driven forward without a jump. This is a drop test, not a proof against every possible ability shortcut. |
| Missed aqueduct gaps and sea rocks | PASS | All seven fighters die in the authored ravine probes. |
| Couch proximity | PASS | Matched-progress landing centres stay within 11 m horizontally and 4.5 m below the high lane. Actual couch race feel is pending. |
| Concept D graveyard art | PASS | Violet stone, open gothic aqueduct arches, sea rocks, cliff stairs, headstones, two mausoleums, crow trees, lanterns, internal iron fencing, iron crypt door, crows, ivy, dead trees and moonlit sea. |
| No colliders, false floors, open-edge barriers, corridor scenery or text | PASS | The art skins the actual shell. Background triangle clipping is empty. Inspection reports zero cosmetic colliders and missing references. Fences are in side plots and all forty final sweep poses were inspected. |
| Triangle budget | PASS | The stage remains below 120,000 source triangles. No budget increase. The separate target library is unchanged. |
| Fix a. Start arch | PASS | The small west-garden arch leaves the native north-facing start view clear. Moving it behind the spawn alone was rejected after its first replay. |
| Fix b. Black gothic roof above the makeover | PASS | Black slate deck and top riser, candlelit arches, chimney gable relief and gargoyles, black turret back, pink front and ring light. The rectangular roof silhouette remains the earlier recorded deviation from the painting. |
| Fix c. Moon | PASS | Smooth circular outline and explicit soft transparency halo. Raised crater blobs are removed. |

## Camera vantages

- North-facing start on the lawn: the pink front, the three tiers and the turret above.
- East- and west-facing along each tier.
- North-facing on the roof: the turret on the split, pink before it and black beyond.
- North-facing at B1: the high aqueduct, low sea rocks, cliff stairs and graveyard gate.
- North-facing inside the graveyard: the hurdle rows, mausoleum roofs and far crypt door.

## Open for the maintainer's play

- Couch Scramble pulls a racer more than 20 m from the leader to the leader's recent safe
  ground. The front is 32 m wide, so a racer on the far end from the leader may get pulled across.
  Check it in couch play; the leash is a prototype value (ADR-0047).
- Judge the high and low crossings, hurdle rhythm, mausoleum shortcuts and gate respawn.
  The old funnel and rotten-middle questions are superseded by the graveyard revision.


## First art pass, 2026-10-30, superseded back and roof record

This historical pass predates the graveyard revision above. Its back, roof and start-arch
results are superseded. This table records implementation
conformance, not human art acceptance. Local comparisons and proof are in
`tmp/nyx-course/art/board.html` and `tmp/nyx-course/art/REPORT.md`.

| Art-direction item | Verdict | Installed result |
| --- | --- | --- |
| 1. Pink stucco terraces, trim, heart windows and shutters | PASS | Pale pink clear tops, deeper pink risers, white trim, warm heart panes and pastel shutter relief. |
| 2. Pink marble stairways | PASS | Marble veins and tread bands follow the approved continuous ramp planes. No stair collision changed. |
| 3. Planters, hedges and bowed parcels on quick steps | PASS | Existing blocks carry low clipped hedge crowns or blank gift-wrap faces and bow relief. |
| 4. Angel wings, neon hearts and climbing roses | PASS | White feather fans, heart outlines and roses stay on the roof riser and vertical faces. |
| 5. Split turret and gothic house above | DEVIATION | Pink front, ring light, black slate bands, crow weathervane, gothic windows, chimneys and small bat gargoyles are installed. The roof keeps the baked rectangular silhouette rather than the painting's pitched mansard volume. |
| 6. Garden props | PASS | Side hedges, rose bushes, lantern posts, low iron side fence, heart balloon arch, pool and toy skeletons reclining in pink chairs. |
| Back paragraph | PASS | Violet stone tops, dark masonry, broken corner stubs, rotten planks at the existing gap, barred crypt and candles, arched windows, crows, dead ivy, distant rocks, bare trees, black water, sea and moon. |
| Target paragraph | PASS | Ordinary bats, skeletons and ring lights; giant heart-eyed bat, flamingo float and portrait standee; comic heart and confetti hit/break effects. Existing sound cues remain. |

| Build rule | Verdict | Evidence |
| --- | --- | --- |
| 1. Preserve collision, add no colliders, false landings or corridor scenery | PASS | Source-scene and arena SHA-256 stay unchanged; strict layout check and inspection pass. Background face clipping reports zero corridor intersections. Water is below the KO plane. |
| 2. Keep open front edges clear | PASS | Decoration stays on risers and the house. Garden fencing is on the lawn's lateral sides. Rear railing is limited to isolated short lateral stubs. |
| 3. Bright clear walkable tops | PASS | Pale front tops and brighter violet back paving contrast with dark wall faces. Gameplay drop captures retain the landing sequence. Wide background and below-floor review views trigger dark-exposure flags, recorded in the report for human judgment. |
| 4. No text or real logos | PASS | Props have hearts, bats or blank wrapping. The portrait uses the supplied art. The inherited lettered finish effect was replaced with hearts and confetti. Normal HUD text remains. |

The earlier composition decision 8 also remains simplified. There is no distant
front road or town in this pass. The art uses the moon, ravine, sea and dead trees
from the selected back concept. The maintainer decides whether the roof silhouette
and scene detail meet the intended joke after racing it.

## Art polish, 2026-10-30

The maintainer called the graveyard course "Pretty good!" and requested a bounded art polish toward concepts B and D. Gameplay is frozen for this pass. The source scene, every collision mesh, baked arena, layout, racing line, markers, targets and course asset stay unchanged. The current evidence and phone comparison board are in `tmp/nyx-course/polish/`.

The new meshes remain procedural. Stone uses violet-grey colour variation, cracks, edge wear and fine grit. The graveyard and stair foundations have fractured vertical profiles. The sea uses the installed StageWater shader below the kill plane. The quiet background has jagged sea rocks, supported dead trees, distant cliffs, forest and faint town lights. The non-route turret carries the new mansard silhouette. The front keeps its existing garden, target looks and reclining skeletons, with added trim, wing relief, roses and neon hearts. A soft moon and starry gradient replace the flat disc and black background.

| Polish item | Verdict | Result or bounded deviation |
| --- | --- | --- |
| 1. Cliff faces and ravine | PASS | Fractured rock skins replace planar plateau and stair faces. Their upper rims and all walkable tops keep the collision coordinates. Recessed ledges stay below those rims. |
| 1. Black water, jagged sea rocks and animated horizon | PASS | StageWater has scrolling ripple normals and a restrained swell. Water and nearby jagged scenery stay below killY. Distant rock masses stay beyond the blast corridor. Two fixed-camera captures establish changing water pixels. |
| 1. Ruined gothic aqueduct | DEVIATION | Pier courses, varied voussoir bands, wear, ivy and crows dress the existing openings. The openings retain the baked rounded profile; changing the structural arch silhouette is outside this art pass. |
| 1. Low route rocks and carved cliff stairs | DEVIATION | Faceted stone variation, scars and a fractured stair foundation replace the uniform treatment. The 3.2 m square rock caps and continuous stair ramp remain exact. Rounding the landing caps or cutting physical steps would change gameplay. |
| 1. Leaning graves, crosses, slabs, obelisks, grass and dirt | PASS | Varied leaning side-plot graves and planting frame the clear route. The two playable hurdle rows retain their box collision outlines and use varied carved faces. |
| 1. Mausoleums with gothic roofs and doors | DEVIATION | Door recesses, ironwork, candles, masonry and slate gable relief are installed. Both roof shortcuts remain flat and unobstructed. No pitched volume covers a usable landing. |
| 1. Crow trees, lanterns, warm pools, crypt and distant cliffs | PASS | Branched crow trees, supported background trees, crows, candle groups, lanterns and warm point-light pools are installed. The crypt keeps its iron door and finish position. |
| 2. Gothic house above the pink | DEVIATION | The non-route turret has a mansard cap, dormers and candlelit glazing. Steep slate riser planes, gable relief, chimney trim and gargoyles strengthen the seam. The broad playable roof deck stays rectangular. The jumpable chimney tops remain clear. |
| 3. Pink front | PASS | Added stucco detail, layered trim, angel-wing relief, neon hearts and climbing rose petals. Existing hedges, rose bushes, lantern posts, pool and reclining skeletons remain. Nothing new lines an open walkable front edge. |
| 4. Sky and light | PASS | Continuous face-free moon surface, soft halo, stars, gradient sky, cool moon key, warm rear pools, pink front bounce, distant forest and faint town lights. |
| Frozen gameplay and cosmetic-only installation | PASS | All protected hashes match. Inspection reports zero cosmetic colliders and no missing mesh, material or shader references. No shell apply or bake runs in this pass. |
| Performance budget | PASS | Planned budget increases from 120,000 to 180,000 source triangles with before and after native frame-time checks. The export uses 177,277 source triangles. These Editor samples are diagnostics, not a packaged-player performance claim. |

The full orbit uses 24 elevated poses, eight low rim poses and eight low ravine poses. The low rim ellipse is now 39 m by 82 m. Its former front diagonal probes were inside the lawn volume and are superseded. Each final frame was inspected. The real course replay lifecycle preserves gameplay-camera rendering and restores the Frontend scene; it replaces the skill's temporary additive Edit Mode capture setup for this Arcade-only stage.

Human art acceptance remains separate from the automated hashes, tests and replays. Check #31 carries the polish note and the board for the maintainer's in-game judgment. The retained square landing caps, hurdle outlines and flat mausoleum roofs are deliberate readability constraints, not a claim of exact concept parity.

## Target placement revision, 2026-10-31

The maintainer played check #31. The layout and art are "not too bad", but the targets are off the path: "it feels bad to go to other side just to break a target". The cause is in this brief. Fourteen of the seventeen targets were mirrored west/east pairs on split ways. Every target is required: each checkpoint needs every earlier target, and the finish needs all of them. Picking a side therefore meant doing both sides. [traversal-courses.md](../traversal-courses.md) ("Make the route choice real") warns against this.

The maintainer's direction: "put them in all the map not just the nyxxie part".

1. **Targets sit on the line every way already runs.** Each required target is reachable with an ordinary attack, without leaving the line, from every way through its section. That includes the safe ramps and the quick steps on the front, and the high aqueduct and the low sea rocks on the back. Good spots are meeting points (the lawn before the split, stair tops that converge, the roof crossing, the balcony landing, the gate and cliff-top merge, the graveyard lane) and spots between two parallel ways that both can hit. No mirrored pairs.
2. **Every section has targets.** The lawn, the pink tiers, the roof, the back crossing and the graveyard all get some. The total stays about 16–18. There are three or four five-hit bonus targets, and at least one of them is on Nyx's side.
3. **Two looks.** The front keeps Nyxxie's junk. The back uses Nyx's own gothic things through a separate `CoursePresentationSet` target look keyed by id prefix: for example skull candles as the small targets and a stone gargoyle as the bonus. No text.
4. **Quick steps may move.** If a target can only reach every way when a quick step (box, end box, ledge, dormer) moves a little, moving the step is allowed. A target off a way's line is not.
5. **Checkpoints follow.** Required counts are recounted. No sensor holds a target it requires. The checkpoint 4 and finish positions stay.
6. **The rule is tested.** An automated check fails if any required target is farther than its reach from any of the course's ways.
7. **Pace.** The scripted run should not get longer than the current ~2:11.


### Target revision implementation record, 2026-10-31

Implemented, baked and inspected in the canonical Editor. The 17 objectives are
now **4 lawn, 1 pink-tier, 3 roof, 3 back balcony and 6 graveyard targets**. All
mirror pairs are removed. The four five-hit bonuses are the giant bat, flamingo,
cardboard Nyxxie and Nyx's stone gargoyle. The back's nine targets use prefix-keyed
skull-candle and gargoyle looks with violet stone shards. Course-owned materials
keep targets visible inside the manor's camera cutaway; the front keeps its junk.

The S2 quick ledges move one metre inward. All other collision pieces, ramps,
checkpoint/respawn transforms and the finish stay fixed. Only the ledge skins and
their matching garden trim alter existing scenery meshes. Required checkpoint
counts are **4, 5, 5, 11**; the finish requires all 17. Neither checkpoint nor finish
sensors overlap an objective they require. Graveyard targets sit at its entrance
and crypt approach so both mausoleum ways keep their target access.

The every-way 3D distance rule and stationary real-kit normal attacks pass for all
seven fighters. **52 shell tests** cover 70 complete attempts including both
mausoleum roofs; **240 baked course/arena tests** pass. The slowest scripted course
clock is **1:58.28**. Native **Nyx high: 1:43.23; FightGuy low: 1:39.85**, both with
17 targets, four saves and zero falls. Racing line regenerated; native inputs use
an explicit roof-bat attack rather than relying on a collateral hit.

Deviations: the flamingo radius grows from 1 to 2 m, with its enlarged mesh raised
onto the terrace, to fit the shortest normal attacks from both S2 lines. Pink
objectives concentrate at the S2 meeting rather than adding a target on each
disjoint tier. Human acceptance remains check #31. Evidence and the phone board
are in `tmp/nyx-course/targets/`; no roster admission or Shared rule changed.
