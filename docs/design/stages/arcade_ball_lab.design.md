---
key: arcade_ball_lab
display_name: Arcade Ball Lab
purpose: arcade
status: locked
variant: static
scope: phase_1_prototype
---

# Arcade Ball Lab

This is the internal single-player fixture authorized by
[Arcade Phase 1](../arcade-phase-1.md) under the
[approved feature brief](../arcade.md) and
[unified-stage decision](../../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md).
Its purpose is testing authoritative ball contacts with the real roster kits.

## Gameplay shell and presentation

Use a simple enclosed static room with a flat supported floor, solid walls and a
ceiling. One grounded player start and one ball marker make repeated experiments
easy. The ball rebounds from all surfaces in this fixture; challenge floor-miss
scoring comes with the later Slopball rules.

Present a neutral sports prototype: a quiet floor, clear court/ceiling outlines,
one midcourt cue and local lighting. The normal orbit and ability-aim cameras stay
available. Collision comes exclusively from the baked shell, and the ball/fighter
responses come from Shared. Cosmetic outlines and lighting own no gameplay.

Use original primitive meshes and URP materials. No sourced asset procurement or
character identity/lore change is part of this fixture. Its key remains separate
from the finished Slopball court and normal PvP stage roster.

## Authoring and acceptance

The fixed source is `Assets/Stages/arcade_ball_lab/arcade_ball_lab.unity`, the runtime
artifact is `data/arenas/arcade_ball_lab.arena`, and the local cosmetic prefab is
`Assets/Resources/Stages/arcade_ball_lab.prefab`. The approved compiler reads the
single `ArcadeStageAuthoring` component on AuthoringAids and its empty ball marker;
normal PvP sources retain their four-spawn validation.

Automated preflight covers source/baked freshness, one valid player start, stage
purpose admission, legacy arena compatibility and authoritative physics/contacts.
Human acceptance is solo play with each roster fighter, readable orbit/aim views,
ordinary attacks/projectiles and immediate fresh-ball experimentation. The
maintainer reviews feel; no multiplayer-stage acceptance is implied by this fixture.
