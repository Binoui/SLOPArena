---
key: nyx_manor
display_name: Two-Faced Manor
purpose: arcade
variant: static
intent: playful
status: art_pending_play
---

# Two-Faced Manor production contract

Nyx's Slop Rally course lets every roster fighter climb Nyxxie's pink front,
cross the black gothic roof, and race across a ravine to Nyx's graveyard.
The finish is the iron door of the far crypt. The maintainer approved the
[graveyard revision](nyx_manor.design.md) on 2026-10-30. Scripted verification
and art conformance are separate from the pending controller and camera review.

## Gameplay contract

[`layout.py`](../../../tools/blender/nyx_manor/layout.py) owns the exact collision,
17 targets, three five-hit bonuses, four checkpoints, finish and racing line.
The lawn, three front tiers, all front climbs, roof collision, turret, chimneys,
B1, all targets and checkpoints 1 to 3 retain the approved layout.

B2W, B2E, B3 and CryptLedge are retired. The Crypt identity and mesh GUID are
reused at the far end of the graveyard. From B1, the high route crosses three
five-metre-wide aqueduct spans with 2.5 m gaps. The low route hops four 3.2 m
rock tops and climbs a four-metre-wide, 28.07-degree cliff ramp. Its stair inlet
joins the graveyard without overlapping solids. Checkpoint 4 at z 47 catches
both routes and requires all 17 targets. Its respawn is on the west gate apron.

The plateau has two rows of 0.9 m gravestone hurdles, two 2.4 m mausoleum roofs as quick
lines, and the solid crypt at z 62..66. The finish sensor is at z 61. There are
no targets on the back. Every required gap is less than 3 m. At equal Z progress,
the branch landing centres differ by at most 11 m horizontally and 4.5 m vertically.
The design brief records every geometric adjustment and the gate sensor dimensions.

The source scene is `client/Unity/Assets/Stages/nyx_manor/nyx_manor.unity`.
`sloparena.stage.shell-apply` updates its collision assets and empty challenge
markers. `sloparena.stage.bake` writes `data/arenas/nyx_manor.arena`.
The `nyx_manor_v1` profile admits every roster package through an empty package
list. Checkpoint prerequisites are 7, 12, 17 and 17. Shared owns movement, hits,
saves, falls, timing and completion. Unity art adds no gameplay authority.

## Presentation contract

[`build.py`](../../../tools/blender/nyx_manor/build.py) skins the verified bake.
Its polish budget is 180,000 source triangles, checked against before and after Editor frame-time samples. The front has pink stucco,
white trim, heart windows, shutters, marble stair bands, roses, parcels and garden
props. The balloon arch is smaller and in the west garden, outside the normal start view.
The pink makeover stops at S3. The roof deck and top riser use black slate and
gothic stone, steep slate riser planes, candlelit dormer faces, chimney gables and gargoyles inside the
existing solid footprints. The non-route turret has a mansard cap and candlelit dormers. Its front and ring light stay pink.

The back has dressed aqueduct arch openings, fractured cliff faces, faceted sea-rock supports, carved cliff
stair bands, varied violet-grey paving with cracks and edge wear, leaning crosses, slabs and small obelisks,
dirt and grass patches, two hurdle rows, mausoleum doors and slate gable relief,
crow trees, candle lanterns, dead ivy and a barred crypt with candle groups.
Distant cliffs, supported trees, a forest silhouette and faint town lights stay beyond the blast corridor.
The animated StageWater sea and low jagged rocks stay below the kill plane.
The moon has continuous, face-free surface variation and a soft radial halo against a starry gradient sky.
No open walkable edge has a fence or a new blocking prop. There is no text.

[`presentation.py`](../../../tools/blender/nyx_manor/presentation.py) owns the
materials, generated grit and ripple textures, halo import, seven lights, atmosphere and review cameras.
`sloparena.stage.art-apply` installs the export under
`client/Unity/Assets/Stages/nyx_manor/Presentation/Manor` and updates
`client/Unity/Assets/Resources/Stages/nyx_manor.prefab`.
The existing target library, course look builder, sounds and comic effects stay
in use. Their parameters and the 17 target positions are unchanged.

The playable roof deck, sea-rock caps, hurdle silhouettes and mausoleum roof tops remain rectangular to match collision.
Full pitched roofs across the playable deck and mausoleums remain explicit visual deviations.
The turret carries the new mansard silhouette. Faint town lights are installed; a road remains omitted. The installed course is absent from the online stage roster.
No player build, admission, commit or push is part of this revision.

## Verification and review

`NyxManorTests` runs every fighter over both complete crossings with normals,
ordinary jumps and air dodges. It asserts all four checkpoint saves and zero
falls. The tests also cover the sixteen front climbs, both mausoleum roofs,
front knockoffs, roof and balcony drops, missed aqueduct gaps and missed rocks.
`CourseFinishability.Routes` and its direct `RacingSteps` use the high route.
The verified racing steps regenerate `racing_line.json` through the existing
`CourseRacingLineGenerator`.

The [generator guide](../../../tools/blender/nyx_manor/README.md) gives the rebuild
commands. Current art evidence is in `tmp/nyx-course/polish/REPORT.md` and `tmp/nyx-course/polish/board.html`.
The preceding gameplay revision is recorded in `tmp/nyx-course/graveyard/REPORT.md`,
`board.html`, the native replay reports, gateway receipts, exposure measurements,
frame timing and camera sweeps. Strict source-to-bake checking includes all
collision triangles, spawn and blast bounds. The final structural inspection
lives under `.stage-authoring-cache/nyx_manor/graveyard/`.

Check #31 asks the maintainer to race both crossings, try the mausoleum roofs,
fall after the gate, and judge route readability, the polished front, ravine, graveyard, roofs, moon and frame
rate. Scripted completion does not establish controller difficulty, couch
catch-up behavior or human art acceptance. Earlier build and art reports remain
under `tmp/nyx-course/build/` and `tmp/nyx-course/art/` as superseded evidence.
