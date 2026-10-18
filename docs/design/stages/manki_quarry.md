---
key: manki_quarry
display_name: Kaboom Quarry
purpose: arcade
variant: static
intent: playful
---

# Kaboom Quarry production contract

Follow the locked [design brief](manki_quarry.design.md) for Manki's solo course. Built
so far: the playable shell with its course markers, Arcade entry through the Course card,
and the art pass the owner asked for on 2026-10-18 (the dressed quarry, Mount Manki and
Manki's crate, explosion and sound set; brief section "Art pass"). Playable layout,
difficulty and visual acceptance wait for the owner's play verdict.

Collision source: `Assets/Stages/manki_quarry/manki_quarry.unity`, rebuilt from
[`tools/blender/manki_quarry/layout.py`](../../../tools/blender/manki_quarry/layout.py)
by `sloparena.stage.shell-apply` (the shell's `course` block writes the
`ArcadeStageAuthoring` and its empty target, checkpoint, respawn and finish markers).
Runtime artifact: `data/arenas/manki_quarry.arena`. Cosmetic prefab:
`Assets/Resources/Stages/manki_quarry.prefab`, built by
[`tools/blender/manki_quarry/build.py`](../../../tools/blender/manki_quarry/README.md)
and `sloparena.stage.art-apply` (art in `Assets/Stages/manki_quarry/Presentation/Quarry`).
Course look and sounds: `Assets/Resources/Courses/manki_quarry.asset`, built in Edit Mode by
`sloparena.presentation.course.quarry.build` (`QuarryCoursePresentationBuilder`, prefabs in
`Assets/Stages/manki_quarry/Presentation/Course`, reusing Manki's explosion materials), and
the clips from `python3 tools/sfx/quarry_course_clips.py`, tracked in
`Resources/Audio/SFX/CourseQuarry`. Gameplay scene: the shared course scene
`Assets/Scenes/Arcade_FightGuyCourse.unity`; Arcade picks the course from
`CharacterCourseMatch.Courses` when Manki is selected.

This is a solo Arcade fixture, not a selectable PvP stage. It uses one grounded
`Spawn_01`, the normal cooked Manki kit and profile `manki_quarry_v1` (admits `manki`).
Every solid stands on the KO plane (y = −0.2, the lowest gameplay vertex and the
`AuthoringAids/KillPlane`) just under the visual water (y = −0.05): a fall into the pit is
a fall out. Shared owns crate contacts, progress, the cumulative 5/10/14 saves, falls,
timing and completion.

The course relies on two kit-wide changes approved on 2026-10-18: explosions hit everything
in their radius once (Shared, PvP too; Slopball records archived by
`ArcadeRecordCompatibility.RulesRevision` 4), and the ground cursor reads the stage's
collision so a bomb lands on the island or roof it points at (client aim only).

## Verification

- `tests/Shared.Tests/MankiQuarryRouteTests.cs` drives the cooked kit on the baked arena:
  shooting from the route with the ordinary bench climb (3058 ticks), the same with
  Jump → E (2842 ticks), jumping out to every island and punching without A, R or E
  (5738 ticks), and the Blast Face (double jump and E alone fall short; Jump → E and
  double jump → E make it). `RoundBombAimTests` and `ExplosionReachTests` cover the two
  kit changes.
- `scenarios/arcade/manki-quarry-jetpack.json` replays the jetpack route in Unity with
  gameplay-camera captures (completed, 18 targets, 0 falls, 3 saves on 2026-10-18).
- `python3 tools/blender/manki_quarry/layout.py` checks the bake against the layout; the
  generator refuses to export against a different bake or when any part leaves its role
  (`build-report.json` lists each): walkable tops flush on their boxes; side dressing at
  most 8 cm proud and never above the top; drill-hole dynamite at most 34 cm out and 20 cm
  below the top; props on tops inside the footprint and at most 1.25 m tall, except the
  declared detonator (1.9 m); the crane inside its declared region (the Last Pillar's
  footprint, the bundle's column above 4.2 m and the sky above 15 m); the water flat at
  −0.05; flotsam at most 0.42 m up and 2.5 m from every piece, the duck at most 1.6 m;
  every background face outside the blast corridor. The art is 74k triangles of a
  planned 170k.
- The review replay with extra captures (`tmp/mq-review/review.json` on 2026-10-18) kept
  18 crates, 0 falls and 3 saves; `tools/capture_exposure.py` measured 3–10% clipped.

Scripted routes are reachability evidence for those inputs, not human difficulty,
camera readability or feel. Those belong to the owner's play review.

Geometry or marker changes edit `layout.py`, then shell-apply, bake, the route tests and
the art rebuild. Art-only changes keep the source and arena hashes.
