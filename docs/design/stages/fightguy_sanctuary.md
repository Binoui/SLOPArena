---
key: fightguy_sanctuary
display_name: FightGuy Sanctuary
purpose: arcade
variant: static
intent: playful
---

# FightGuy Sanctuary production contract

Follow the locked [design brief](fightguy_sanctuary.design.md) for the solo FightGuy
course. The current approved scope is its wider winding route with varied jumps
and the complete Himalayan sanctuary art pass (design decisions 22–25). The design brief owns route,
objective and visual intent; [Phase 4](../arcade-phase-4.md) and
[Phase 5](../arcade-phase-5.md) record implementation and verification.

Collision source: `Assets/Stages/fightguy_sanctuary/fightguy_sanctuary.unity`.
Runtime artifact: `data/arenas/fightguy_sanctuary.arena`. Cosmetic prefab:
`Assets/Resources/Stages/fightguy_sanctuary.prefab`. The dedicated gameplay scene is
`Assets/Scenes/Arcade_FightGuyCourse.unity`, entered through normal offline Arcade.

This is an explicitly solo Arcade fixture, not a selectable PvP stage. It uses
one grounded tagged `Spawn_01` and the normal cooked FightGuy kit. Empty target,
checkpoint/respawn and finish markers under `AuthoringAids` compile through
`ArcadeStageAuthoring` into the embedded course section. Shared owns target
contacts, progress, optional cumulative saves, falls, timing and completion;
`CharacterCourseMatch` presents the existing local simulation path. Course-only
purpose must remain excluded from normal PvP selection/admission.

The Himalayan treatment uses original generated geometry, textures and stage-local
materials. It replaces the earlier Cloudtop sample, whose historical asset workset
and source register remain linked from Phase 5. Reconstruction and current
verification are documented in the [art source guide](../../../tools/blender/fightguy_sanctuary/README.md).
The cosmetic prefab has no Colliders
or gameplay authority; all apparently usable surfaces match the authoritative shell.

Geometry/marker revisions update the source, matching presentation and cues, then
use the maintained bake/inspect path. Preserve the pavilion and accepted art roots
outside the intended changes. Art-only work preserves source and baked hashes.
The Phase 5 relayout command preserves/refits the art; greybox reconstruction is
an explicit reset, not routine tuning. Runtime records use existing gameplay
compatibility rules rather than hand-edited category keys.

Use [traversal-course guidance](../traversal-courses.md), focused course and
ArenaShipping coverage, and native ordinary/ability-route runs for gameplay
changes. Human review owns route readability, controller difficulty and gamefeel;
normal 2–4-player PvP acceptance does not apply to this approved solo fixture.
