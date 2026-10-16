# Arena Creation Workflow — Design Spec

**Date:** 2026-07-18
**Status:** Approved, implemented
**Inspired by:** Divine Knockout — 3D platform fighter with low verticality, X/Y play, pillars for chase/cover

## Overview

The arena creation pipeline decouples gameplay iteration from art production. Block out the layout with simple geometry, test immediately, then swap in final visuals.

## Pipeline

```
Unity Scene (ProBuilder greybox) → Bake (.arena binary) → Playtest
                                    ↓
                            Swap FBX meshes for visuals
                                    ↓
                            Re-bake final collision
                                    ↓
                            Save as prefab
```

## Data Flow

```
Scene (Unity)
  ├── MeshFilters → baker reads → CollisionTriangle[] → .arena file
  ├── Terrain → baker samples → heightmap
  └── SpawnPoint-tagged transforms → SpawnPoint[]

.arena file → ArenaBinaryFormat.Deserialize() → ArenaDefinition
  ├── SpawnPoints[] → match spawns players here
  ├── CollisionTriangles[] → server uses for ground/KB collision
  ├── Heightmap → fast ground-surface lookup
  ├── KillHeight → Y below this = blast zone
  └── Bounds (MinX/MaxX/MinZ/MaxZ) → camera framing, mechanics

Match startup (TrainingMatch/PvPMatch):
  data/arenas/<name>.arena exists?
    ├── Yes → load baked file
    └── No → fall back to ArenaRegistry hardcoded entry
```

## Arena Layout Principles (DKO-inspired)

- **Flat stage** — no ramps or platforms. Floor is one continuous surface.
- **Low verticality** — slight elevation changes only with steps, no floating platforms. Smash-style platforms don't work well in 3D.
- **Pillars for cover** — 4-6 pillars placed so players can break line-of-sight and weave around them. Use for turning/chase gameplay.
- **Edge definition** — walls, cliff edges, or pillar rings define the boundary. No invisible walls.
- **Kill zone** — falling off the edge OR knocked below KillHeight. Tuned to stage size (smaller stage = shallower kill = faster matches).

## Using External Assets (e.g. LowPolyFantasyArena2)

The `Assets/AnimationPacks/` folder is gitignored (1.5GB). To use assets from it:

1. **Inside Unity**, Ctrl+D (Duplicate) the FBX models you want
2. Move copies to `Assets/Art/Stages/<name>/Models/`
3. Do the same for materials and textures
4. Unity generates new GUIDs — no dependency on the original folder
5. Drag the new local copies into the scene
6. `AnimationPacks/` stays local-only, scene references committed assets

For quick iteration, **mix collision and visual meshes**:
- ProBuilder shapes provide the collision hull (read by baker via MeshFilter)
- Low Poly FBX pieces can be purely decorative (MeshRenderer only)
- Or both — some FBX pieces become collision geometry too

## TrainingMatch Overrides

Two serialized fields on `TrainingMatch` allow the Inspector to override MatchConfig defaults:

| Field | Default | Purpose |
|-------|---------|---------|
| `_arenaNameOverride` | `"colosseum"` | Arena to load. Empty = use MatchConfig.ArenaName |
| `_playerClassOverride` | `None` | Player character. None = use MatchConfig.PlayerClass |

This makes the Arena_Offline scene self-contained — no menu flow dependency.

## Baking

`Tools > SLOPArena > Bake Arena...` → select arena root → set name → bake

Produces: `data/arenas/<name>.arena` (binary format: magic "AREN" + version + all collision data)

## CharacterClass Enum

```
None = 0    (placeholder, filtered from UI)
Manki = 1
FightGuy = 2
```

The `CharacterRegistry.Get(c)` method indexes by `(int)c`, so the registry array must have a `default` placeholder at index 0.

## File Changes

| File | Change |
|------|--------|
| `src/Shared/ArenaDefinition.cs` | Added `"colosseum"` hardcoded arena |
| `src/Shared/CharacterDefinition.cs` | Added `None` to enum, fixed registry order |
| `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs` | Added arena + player class overrides |
| `client/Unity/Assets/Scripts/Runtime/UI/CharSelectController.cs` | Filter `None` from character grid |
| `client/Unity/Packages/manifest.json` | Added `com.unity.probuilder: "5.2.3"` |
| `Assets/Art/Stages/colosseum/` | Copied FBX + material + texture assets (new, committed) |
