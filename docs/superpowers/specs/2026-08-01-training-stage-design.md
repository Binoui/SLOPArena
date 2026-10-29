# Training Box Stage — Design Spec

**Date:** 2026-08-01
**Status:** Historical grey-lab and two-wall design. The 2026-10-29 Crash-Test Lab rebuild is governed by the [locked design brief](../../design/stages/training.design.md) and [production brief](../../design/stages/training.md). Maintainer acceptance is pending.
**Owner:** binoui (new to Unity — spec doubles as a build tutorial)

## Two-wall gameplay boundary — 2026-10-11

- Training has solid north/east walls and open south/west edges. The cosmetic prefab already showed this corner; the authoritative source still contained four walls, including disabled objects that the baker intentionally includes. Remove unwanted source geometry rather than merely disabling its renderer or GameObject.
- Removed the south/west source walls and enabled the retained north/east source walls. Native rebake/inspection match: 72 collision triangles, four unchanged spawns, SHA-256 `43bc4a534bfb5230407968a70148562a00642bb92af599d8614255b709232eb8`. Floor/platform geometry, blast bounds and the practice-lab presentation are preserved.
- Regression coverage checks capsule exits through both open edges, blocking by both retained walls, and actual Shared knockback deaths through west/south for all four fighters. Before the correction, all 16 open-edge/kill cases failed; afterward, the 24 Training cases and 160 existing arena/collision/blast-zone cases pass. Evidence: `.stage-authoring-cache/training/two-walls-20261011/`. A live Editor match was not run for this boundary correction.

## Quiet practice lab presentation — 2026-10-11 (before the boundary correction)

- Cosmetic refinement of the existing room: warm mid-grey measurement floor, muted sage platforms, softer ambient fill, shallow wall panels, diffuser strips, an observation window, storage panels and `SLOP ARENA / TRAINING` signage. North/east markers, a thin floor perimeter and origin ticks help orientation without cluttering the fight floor.
- The existing metre grid, stronger 5 m lines and red axes remain. Larger platform labels show 1.0, 2.0 and 3.5 m; top numbers identify the three blocks. Paint surfaces have a small offset to remove grid flicker.
- Wall presentation is one-sided toward the room so orbiting cameras retain a clear cutaway. Wall decorations stay shallow and cast no large shadows across the floor. No new lights or Unity colliders were added.
- Layout remains 40 × 40 m with 20 m walls and the same three 4 × 4 m platforms. The authoritative Training scene and baked arena are byte-identical to the pre-pass snapshot: 96 collision triangles, four spawns, SHA-256 `fb9f9b3a93770790f4f052b07931c50ad1dc29e199fbe556d06719126e25d266`. All other stage prefabs/arenas and the saved Frontend/Ability Lab scenes are unchanged.
- Finish meshes and material copies are owned by `Assets/Resources/Stages/training.prefab`; all eight existing external materials were checked unchanged. Native inspection reports 32 renderers, 3,996 cosmetic triangles, 13 materials, one existing local light, zero Colliders and no missing references or diagnostics. The Training thumbnail is refreshed at 16:9.
- Reviewed 24 native orbit views, eight low views and a hero view with four posed package fighters. Evidence: `.ability-lab-cache/training-practice-20261011/pass-04/` and `.stage-authoring-cache/training/practice-lab-20261011/`. Preview setup restored the previously open scene, selection and gizmo preferences; the final audit found a clean, stopped Ability Lab and zero native console errors. These are Edit Mode presentation checks; live gameplay/PVP and performance acceptance were not rerun for this cosmetic pass.

## Grey lab rebuild — 2026-09-08 (historical)

- Neutral grey room: 40 × 40 m interior, 20 m walls, a clear centre, and three 4 × 4 m side platforms at 1, 2, and 3.5 m.
- Flat texture markings replace raised bars and checkerboards: 1 m grid, stronger 5 m lines, muted red origin axes, distance labels, and platform-height labels.
- Reduced stage ambient fill and material brightness keep the floor mid-grey rather than near-white.
- Authoritative source: `client/Unity/Assets/Stages/training/training.unity`; bake with `sloparena.stage.bake --stage training`.
- Presentation: `client/Unity/Assets/Resources/Stages/training.prefab`, with materials/textures under `Assets/Art/Stages/GeneratedPrototypes/training/GreyLab/`. Presentation has no Unity colliders.
- The old prototype generator no longer regenerates Training. Edit the source scene and presentation prefab separately.
- Direct offline Play defaults to `training`; explicit stage selection still takes precedence.
- **Known Shared limitation, explicitly deferred by the user:** movement integrates X/Z and snaps to heightmap surfaces; it does not block vertical wall/platform sides. Baked triangles do not make the room a solid enclosure. A proper Shared collision fix is separate work, not a Unity workaround.
- Verification: typed bake/inspection, arena shipping tests, live Training renders, and FightGuy landing/jump-route smoke checks. Wall containment is not claimed.

## Overview

Replace the mismatched visuals in the offline training scene (`Arena_Offline`) with a
dedicated training stage: a 40×40 m closed box with a grey/white checkerboard floor and
walls (1 m squares), plus colored grid lines every 5 m (yellow) and 10 m (red) for
distance judging. Built entirely from Cube primitives so the user learns the editor while
the collision bake reads the exact same geometry — visuals == collision by construction.

## Why

- Current scene shows the `colosseum` fantasy model while `TrainingMatch` loads
  `Island_arena` collision — the two don't match.
- A plain, measurable training room is more useful for ability testing than a themed arena:
  colored grid lines make 5 m/10 m spacing readable at a glance.
- Primitive cubes + a generated checkerboard texture is the most beginner-approachable
  pipeline and requires zero new runtime code.

## Layout (top view, meters, Y-up)

```
              Z=+20  (north wall, solid)
   ┌────────────────────────────────────┐
   │ red ─────┼────┼────┼────┼──── red   │  red lines   = 10 m grid (±10, ±20)
   │  yellow lines at ±5, ±15            │  yellow lines = 5 m grid  (±5, ±15)
   │ floor: 8×8 checkerboard, 1 m squares│
   │ walls: solid cubes, 20 m tall       │
   └────────────────────────────────────┘
              Z=-20  (south wall, solid)
```

### Blocks (all Cube primitives under a `TrainingBox` root)

| Object | Position (x, y, z) | Scale (x, y, z) | Material |
|---|---|---|---|
| Floor | (0, -0.25, 0) | (40, 0.5, 40) | Mat_Checker_Floor |
| Wall_N | (0, 10, 20.25) | (40, 20, 0.5) | Mat_Checker_Wall |
| Wall_S | (0, 10, -20.25) | (40, 20, 0.5) | Mat_Checker_Wall |
| Wall_E | (20.25, 10, 0) | (0.5, 20, 40) | Mat_Checker_Wall |
| Wall_W | (-20.25, 10, 0) | (0.5, 20, 40) | Mat_Checker_Wall |
| 4× yellow strips (5 m grid) | X = ±5, ±15 at Y = 0.02, Z = 0 | (0.15, 0.05, 40) | Mat_Line_5m |
| 4× yellow strips (5 m grid) | Z = ±5, ±15 at Y = 0.02, X = 0 | (40, 0.05, 0.15) | Mat_Line_5m |
| 4× red strips (10 m grid) | X = ±10, ±20 at Y = 0.02, Z = 0 | (0.15, 0.05, 40) | Mat_Line_10m |
| 4× red strips (10 m grid) | Z = ±10, ±20 at Y = 0.02, X = 0 | (40, 0.05, 0.15) | Mat_Line_10m |

Total: 21 cubes (1 floor + 4 walls + 16 strips). Strips sit 2 cm above the floor to avoid
z-fighting; they are 0.15 m wide so they do not visually swallow the 5 m checker squares.

### Spawn points (tagged `SpawnPoint`)

| Marker | Position |
|---|---|
| Player spawn | (8, 0.5, 0) |
| Alt spawn | (-8, 0.5, 0) |

`TrainingMatch` spawns the NPC dummy at a fixed (0, 5, 0), so the player spawn is offset
from center to avoid overlap. The dummy's spawn is not part of the .arena.

## Materials (created as project assets by me)

| Asset | Definition |
|---|---|
| `Mat_Checker_Floor` | Lit material, generated 8×8 checker texture (grey `#909090` / white `#F2F2F2`, Point filter, **Repeat wrap**), tiling (5,5) → 40×40 squares of 1 m on the 40 m floor |
| `Mat_Checker_Wall` | Same texture, tiling (5,2.5) → 40×20 squares of 1 m on a 40×20 m wall face (all four walls share this: every wall has a 40×20 m big face) |
| `Mat_Line_5m` | Lit material, flat yellow `#FFD500` |
| `Mat_Line_10m` | Lit material, flat red `#FF3B30` |

Cube side faces (0.5 m thin) get stretched checker — invisible in practice, no gameplay impact.

## Work Split

### Me (materials, bake, wiring)
1. Generate checkerboard texture + 4 material assets in `Assets/Art/Stages/training_box/`.
2. Bake `TrainingBox` root → `data/arenas/training.arena` (overwrites the stale file from the
   deleted scene) via reflection on `SlopArenaArenaBaker.BakeArena` (driven through the connected
   Unity session). The 16 `Line_` strips are temporarily reparented out of the hierarchy before
   baking so they stay visual-only, then restored.
3. In `Arena_Offline`: delete the `colosseum` visual root, purge the stale `_arenaName` prefab
   override (field renamed to `_arenaNameOverride`), set `_arenaNameOverride` to `training`.

### Execution note (deviations from the split, with user consent)
- The user placed the floor + 4 walls; the 16 grid strips and 2 spawn markers were placed via
  MCP script (`GameObject.CreatePrimitive` + tag) at the user's request after the strips proved
  too tedious to hand-place. Wall height changed from 5 m to 20 m during the build (user choice:
  "real box"; a separate future stage will be wall-less), wall material tiling updated to (8,4).
- The scene's `TrainingMatch` has `_npcAiMode = Idle` (pre-existing override): the dummy stands
  still, which is the intended training behavior.

### User (arranging blocks, placing spawns — coached by me)
1. Create empty root `TrainingBox`.
2. Create the 21 cubes from the table above (GameObject > 3D Object > Cube, set transform,
   drag material from Project view).
3. Create 2 `SpawnPoint`-tagged markers at the table positions.

### Arena key choice
Reuse the existing `training` registry key (`src/Shared/ArenaDefinition.cs`): the baked file
loads with precedence (`loaded ?? ArenaRegistry.Get`), the `training` registry entry already
exists (50×50 bounds, kill height −15, center spawn), so:
- No shared-code changes, no DLL rebuild.
- Stage select already lists "Training Room" — it now points at the real box.

## Verification

1. Bake succeeds → `data/arenas/training.arena` contains floor + 4 walls (5 cubes × 12
   tris = 60 collision triangles) and 2 spawn points.
2. Play `Arena_Offline`: player spawns at (8, 0.5, 0), dummy at center, floor is checkerboard,
   yellow/red grid visible, characters bounce off all 4 walls (can't leave the box).
3. Console shows `[TrainingMatch] Loaded arena from file: .../training.arena`.

## Implementation Notes (discovered during build)

- **Texture wrap must be `Repeat`, not `Clamp`.** With Clamp, any tiling > 1 renders the
  surface solid (UV clamps to the edge texel) — the 1×1 floor tiling masked this until the
  wall/floor tiling exceeded 1. Verified via orthographic top-down renders + pixel analysis
  (40×40 uniform squares, period 1.03 m).
- **Checker grey must be dark enough to survive the lighting.** #C8C8C8 vs #F2F2F2 (16 %
  contrast) is blown out to uniform white by the 1.5-intensity directional light + ACES
  tonemapping in lit areas; #909090 keeps visible contrast in both lit and shadowed regions.
- Vision-model screenshot review is unreliable for grid counts — pixel-level analysis
  (neighbor-diff edge counting, luminance profiles) is the ground truth.

## Out of Scope

- PvP/stage-select changes (the registry entry already covers display).
- Wall decorations, skybox, lighting pass, or art polish.
- Making the strips a separate server collision layer (they are visual-only, 0.05 m tall —
  negligible bump; the checker floor 0.5 m thick is the collision surface).
