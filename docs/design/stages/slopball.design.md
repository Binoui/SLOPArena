---
key: slopball
display_name: Slopball Court
purpose: arcade
status: locked
variant: static
scope: phase_2_prototype
---

# Slopball Court

This fixture implements the approved neutral sports court in the
[Arcade brief](../arcade.md) and Phase 2 of the
[delivery plan](../arcade-implementation.md). It has no character home-stage lore
or external asset dependency.

1. Use one enclosed static room with a flat supported floor, walls and ceiling.
   A low net divides the player and sender halves. Ball collision follows the
   baked shell and net; Shared constrains the fighter to the player half.
2. Supply one grounded player start and one empty sender ball marker. All four
   normal cooked fighter kits share the same fixed-seed, capped shot profile.
3. Show distinct quiet floor halves, a readable net/tape, court outlines and a
   simple stationary launcher. Use original primitive meshes, URP materials and
   local lighting. Inward-facing shell surfaces leave outside orbit views open.
4. Preserve ordinary orbit and ability-aim cameras. Cosmetics have no Colliders
   and own no gameplay. Stage purpose excludes this court from normal PvP.
5. Floor misses, receive-before-redirect ordering, bank returns, fresh shots and
   neutral same-shot re-serves belong to Shared. This slice has a readable return
   count and a full scene retry; menu/countdown/personal records are Phase 3.

Source: `Assets/Stages/slopball/slopball.unity`. Runtime artifact:
`data/arenas/slopball.arena`. Local presentation:
`Assets/Resources/Stages/slopball.prefab`. Playable fixture:
`Assets/Scenes/Prototypes/Slopball.unity`.

Preflight covers profile/court validation, source/baked freshness, chronological
physics/goals and genuine cooked-kit contacts. Human solo play with every fighter
is still required to accept readability, returning and juggling feel.
