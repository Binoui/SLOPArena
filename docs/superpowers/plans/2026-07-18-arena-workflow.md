# Arena Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire up the colosseum arena in Unity — reimport assets, build ProBuilder floor, place pillars, set spawns, bake, playtest.

**Architecture:** Already done — CharacterClass.None, TrainingMatch overrides, hardcoded arena entry, asset copies. Remaining work is Unity-side scene assembly.

**Tech Stack:** Unity 6 URP, ProBuilder, SLOPArena ArenaBaker tool

## Global Constraints

- `AnimationPacks/` stays in `.gitignore` — don't touch it
- Arena scene references must point to `Assets/Art/Stages/colosseum/`, not `AnimationPacks/`
- Bake produces `data/arenas/colosseum.arena` — commit it

---

### Task 1: Unity — Reimport Copied Assets

`Art/Stages/colosseum/` has raw FBX/mat/png files without `.meta`. Unity must reimport and generate new GUIDs before scene can reference them.

- [ ] Open Unity project
- [ ] Wait for reimport to complete — check Console for errors
- [ ] Verify new `.meta` files appear under `Art/Stages/colosseum/Models/`, `Materials/`, `Textures/`
- [ ] Drag `Arena_01` from `Art/Stages/colosseum/Models/` into the scene to confirm it renders

### Task 2: Scene Wire — ProBuilder Floor + Spawns

The scene (`Scenes/Arena_Offline.unity`) needs an arena root with collision geometry and spawn points. Either:

**Option A — ProBuilder floor (recommended):**
- Open ProBuilder (`Tools > ProBuilder > ProBuilder Window`)
- New Shape → Cylinder, radius ~11, height 0.5 → position (0, 0, 0)
- Assign temp URP Lit material (gray)
- Name the shape `colosseum`

**Option B — Use existing `Arena_01.fbx` as collision:**
- Drag `Arena_01.fbx` from `Art/Stages/colosseum/Models/` into scene at (0, 0, 0)
- Scale to ~1.5x if needed (~22m diameter)
- Assign `M_LowPolyFantasyArena2_MAIN.mat` in MeshRenderer

**Spawn points (both options):**
- Create two empty children under the arena root
- Name: `respawn_1` → Tag: `SpawnPoint` → Position: (-6, 0.5, 0) → Rotation Y: 0
- Name: `respawn_2` → Tag: `SpawnPoint` → Position: (6, 0.5, 0) → Rotation Y: 180
- Add 2 more at (0, 0.5, -6) and (0, 0.5, 6) for FFA

### Task 3: Add Low Poly Pillars (Visual)

- Drag pillar Ruins FBX from `Art/Stages/colosseum/Models/` into scene as children
- Position in a ring around the floor edge (~8-10m from center)
- Rotate/scale for variety
- They have MeshFilters → will become collision tris in bake automatically

### Task 4: Wire TrainingMatch Component

- Select the `TrainingMatch` GameObject in the Hierarchy
- In Inspector, set `_arenaNameOverride` = "colosseum"
- Set `_playerClassOverride` to Manki (or leave None for FightGuy)
- Set `_npcClass` to the opponent class
 
**⚠️ Animation config check:** The `PlayerRenderer` in the scene may have `_charConfig` pre-assigned to a specific character's config in the Inspector. If the model shows one character but animations are wrong, check this field:
- Select the `PlayerRenderer` GameObject
- In Inspector, verify `_charConfig` matches the player's class, or clear it (set to None) so the code auto-loads `AnimationConfigs/{ClassName}_AnimConfig` by class name

### Task 5: Bake

- `Tools > SlopArena > Bake Arena...`
- Arena Root: drag the arena root from Hierarchy into the field
- Name: `colosseum`
- Kill Height: `-10`
- Hit "Bake Arena"
- Verify output in Console: should show triangle count and bounds

### Task 6: Playtest

- Hit Play in Unity
- Verify:
  - Player spawns at (-6, 0.5, 0) facing right
  - NPC spawns somewhere
  - Can walk on the floor (ground collision works)
  - Fall off edge → die at KillHeight=-10 → respawn
- If spawns wrong: re-check spawn point positions and re-bake

### Task 7: Commit Baked Data + Scene

```bash
git add data/arenas/colosseum.arena
git add client/Unity/Assets/Scenes/Arena_Offline.unity
git add client/Unity/Assets/Art/Stages/colosseum/
git commit -m "feat: colosseum arena scene + baked data"
```
