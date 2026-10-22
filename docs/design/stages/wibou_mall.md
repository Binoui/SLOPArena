---
key: wibou_mall
display_name: Neo Mall
purpose: arcade
variant: static
intent: playful
---

# Neo Mall production contract

Follow the locked [design brief](wibou_mall.design.md) for Wibou's solo course. Built on
2026-10-20 after the owner's "lets go for it": the playable shell with its course markers,
the dressed mall, the course look and sounds, and the Arcade entry. Playable layout,
difficulty and visual acceptance wait for the owner's play verdict.

Collision source: `Assets/Stages/wibou_mall/wibou_mall.unity`, rebuilt from
[`tools/blender/wibou_mall/layout.py`](../../../tools/blender/wibou_mall/layout.py) by
`sloparena.stage.shell-apply` (the shell's `course` block writes the `ArcadeStageAuthoring`
and its target, checkpoint, respawn and finish markers; the bosses' `hits` land on their
`Hits`). Runtime artifact: `data/arenas/wibou_mall.arena` (arena section 6, because the
bosses take several hits). Cosmetic prefab: `Assets/Resources/Stages/wibou_mall.prefab`,
built by [`tools/blender/wibou_mall/build.py`](../../../tools/blender/wibou_mall/README.md)
and `sloparena.stage.art-apply` (art in `Assets/Stages/wibou_mall/Presentation/Mall`).

Course look and sounds: `Assets/Resources/Courses/wibou_mall.asset`, built in Edit Mode by
`sloparena.presentation.course.mall.build` (`MallCoursePresentationBuilder`). Its prefabs and
textures live in `Assets/Stages/wibou_mall/Presentation/Course`, and its cuts reuse Wibou's
own effect materials. The agent mannequins and the four bosses come from
`tools/blender/wibou_mall/targets.py` (`NeoMallTargets.fbx` in the same folder, on the mall's
materials and atlas), and the PWNED! and NAT 20! lettering masks are in `Course/Source`. The
clips come from `python3 tools/sfx/mall_course_clips.py`, tracked in
`Resources/Audio/SFX/CourseMall`. Gameplay scene: the shared course scene
`Assets/Scenes/Arcade_FightGuyCourse.unity`. Arcade gives Wibou the first baked course in
`CharacterCourseMatch.Courses` that admits him: Neo Mall, his only one.

This is a solo Arcade fixture, not a selectable PvP stage. It uses one grounded `Spawn_01`,
the normal cooked Wibou kit and profile `wibou_mall_v1` (admits `wibou`). The galleries and
bridges are 0.8 m slabs over the atrium; the elevator tower, the podium and the envelope
walls stand on the KO plane (y = −0.2) just under the visual ground floor (y = 0): a fall off
a deck is a fall out. Shared owns target hits and breaks, boss hit counts, progress, the
cumulative 8/16/23 saves, falls, timing and completion.

## Verification

- `tests/Shared.Tests/WibouMallRouteTests.cs` drives the cooked kit on the baked arena. The
  specials route breaks every group with its special (2923 ticks, using A, E, R and F). Jumps
  and slashes alone finish too (4289 ticks). The skybridge rejects a double jump and accepts E
  and Jump → E; the elevator tower rejects a double jump, E alone and Jump → E and accepts
  Jump, jump, E; Skyfall plus a close kunai volley breaks each boss. `SLOPARENA_COURSE_SHELL=
  <shell json>` runs the same tests on the offline `CourseShellArena`, before a bake.
- `tests/Server.Tests/ArcadeStageAdmissionTests.cs` keeps the arena out of PvP.
- `scenarios/arcade/wibou-mall-specials.json` replays the specials route in Unity with
  gameplay-camera captures (completed, 31 targets, 0 falls, 3 saves on 2026-10-20).
- `python3 tools/blender/wibou_mall/layout.py` checks the bake against the layout and refuses
  unplanned overlaps, railings off their decks, markers off their pieces, invalid hit counts
  and a required target over its checkpoint's sensor. The generator refuses to export against
  a different bake or when any part leaves its role (`build-report.json` lists each check):
  walkable tops flush on their decks; deck dressing at most 30 cm proud and never above the
  top; railings within their collision railing; wall dressing at most 1 m out (the food
  counters); nothing but declared fixtures (the katana kiosk, the wet floor signs on the
  stubs, the food court tables) inside a deck's fighter band (1 m in from its edges, 2.2 m up);
  no dressing inside a target; the vault and the katana beyond every deck's best climb. The art
  is 27k triangles of a planned 260k; the targets add 11k.

Scripted routes are reachability evidence for those inputs, not human difficulty, camera
readability or feel. Those belong to the owner's play review.

Geometry or marker changes edit `layout.py`, then shell-apply, bake, the route tests and the
art rebuild. Art-only changes keep the source and arena hashes. A change to the atlas cells
rebuilds both the mall art and `targets.py`'s models, then the course presentation.
