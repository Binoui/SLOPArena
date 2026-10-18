---
key: fightguy_pier
display_name: Fight Night Pier
purpose: arcade
variant: static
intent: playful
---

# Fight Night Pier production contract

Follow the locked [design brief](fightguy_pier.design.md) for FightGuy's solo course. Built on
2026-10-18 without a review stop (the owner's pick): the playable shell with its course
markers, the Shared multi-hit targets, the dressed pier, the course look and sounds, and the
Arcade entry. Playable layout, difficulty and visual acceptance wait for the owner's play
verdict.

Collision source: `Assets/Stages/fightguy_pier/fightguy_pier.unity`, rebuilt from
[`tools/blender/fightguy_pier/layout.py`](../../../tools/blender/fightguy_pier/layout.py) by
`sloparena.stage.shell-apply` (the shell's `course` block writes the `ArcadeStageAuthoring`
and its target, checkpoint, respawn and finish markers; a bonus target's `hits` lands on
its `Hits`). Runtime artifact: `data/arenas/fightguy_pier.arena` (arena section 6, because
the course has multi-hit targets). Cosmetic prefab: `Assets/Resources/Stages/fightguy_pier.prefab`,
built by [`tools/blender/fightguy_pier/build.py`](../../../tools/blender/fightguy_pier/README.md)
and `sloparena.stage.art-apply` (art in `Assets/Stages/fightguy_pier/Presentation/Pier`).

Course look and sounds: `Assets/Resources/Courses/fightguy_pier.asset`, built in Edit Mode by
`sloparena.presentation.course.pier.build` (`PierCoursePresentationBuilder`). Its prefabs and
textures live in `Assets/Stages/fightguy_pier/Presentation/Course`, and it reuses FightGuy's
own effect materials. The bonus targets' models come from
`tools/blender/fightguy_pier/bonus.py` (`FightNightPierBonus.fbx` in the same folder, on the
pier's materials and atlas), and the lettering masks are in `Course/Source`. The clips come
from `python3 tools/sfx/pier_course_clips.py`, tracked in `Resources/Audio/SFX/CoursePier`.
Gameplay scene: the shared course scene `Assets/Scenes/Arcade_FightGuyCourse.unity`. Arcade
gives FightGuy the first baked course in `CharacterCourseMatch.Courses` that admits him: the
pier, with Sanctuary as the fallback.

This is a solo Arcade fixture, not a selectable PvP stage. It uses one grounded `Spawn_01`,
the normal cooked FightGuy kit and profile `fightguy_pier_v1` (admits `fightguy`). Every
solid stands on the KO plane (y = −0.2) just under the visual sea (y = −0.05): a fall off
the pier is a fall out. Shared owns target hits and breaks, bonus hit counts, progress, the
cumulative 10/16/22 saves, falls, timing and completion.

## Verification

- `tests/Shared.Tests/FightGuyPierRouteTests.cs` drives the cooked kit on the baked arena.
  The specials route breaks every group with its special (3133 ticks, using A, E, R and F).
  Jumps and punches alone finish too (6188 ticks). The wheel tower rejects a double jump and
  E alone and accepts Jump → E and double jump → E; the collapsed middle stops a Cyclone Kick
  at three timings; one Fist of Fury breaks each bonus target. `SLOPARENA_COURSE_SHELL=<shell
  json>` runs the same tests on the offline `CourseShellArena`, before a bake.
- `CharacterCourseTests` covers the multi-hit targets: hit counts, the kept lock-on,
  checkpoint gating until the break, section 6 against section 5 and record fingerprints.
- `scenarios/arcade/fightguy-pier-specials.json` replays the specials route in Unity with
  gameplay-camera captures (completed, 28 targets, 0 falls, 3 saves on 2026-10-18).
- `python3 tools/blender/fightguy_pier/layout.py` checks the bake against the layout and
  refuses unplanned overlaps, markers off their pieces, invalid hit counts and a required
  target over its checkpoint's sensor. The generator refuses to export against a different
  bake or when any part leaves its role (`build-report.json` lists each check): walkable
  tops flush on their boxes; side dressing at most 26 cm proud and never above the top;
  props on tops within 26 cm of the footprint and at most 2 m tall; lamps on the decks;
  fixtures never inside a target, and never inside a walkable deck's fighter band (1 m in
  from its edges, 2 m up) unless declared (the striker pole, ring posts, bag frame, carousel
  house and finger, derrick, wheel, car pen and booths); the water flat at −0.05; every
  background face outside the blast corridor. The art is 93k triangles of a planned 240k;
  the bonus targets add 12k.
- The review replays (`tmp/fightguy-pier/review/*.json`, 2026-10-18) kept 28 targets, 0 falls
  and 3 saves, and played every course clip: 24 pops, 28 organ notes, 7 hits and a break per
  bonus target, three saves, the all-clear and the finish.

Scripted routes are reachability evidence for those inputs, not human difficulty, camera
readability or feel. Those belong to the owner's play review.

Geometry or marker changes edit `layout.py`, then shell-apply, bake, the route tests and the
art rebuild. Art-only changes keep the source and arena hashes. A change to the atlas cells
rebuilds both the pier art and `bonus.py`'s props, then the course presentation.
