# Hurtbox / Visual Model Alignment

> **Superseded for package cooks (2026-10-17).** Cooks now write `_hips_offset`, the drawn
> hips relative to the capsule centre, and Shared places hurtboxes and bone hitboxes from it
> (see [hitbox system](systems/hitbox-system.md#drawn-hips--package-cooks-since-2026-10-17)).
> `HipHeight` and `BoneYToWorldY` below remain the fixed pivot for attack-pose pitch, aim
> correction and legacy bakes without that point.

## The Core Problem

Server hurtboxes (from baked bone data) didn't align with the visual model. The baked data uses a Hips-relative coordinate system (Hips at Y=0, feet at negative Y), but the simulation formula anchored hurtboxes at capsule-bottom. Result: Hips hurtbox sat at ground level while the model's Hips was at hip height — a Y desync up to 0.85m.

## Root Cause

The old formula `wy = py - CapsuleHeight/2 + by` assumes the baked data origin is at capsule-bottom (feet). But baked data origin is at the Hips bone — `by=0` means "at Hips height", not "at feet height." No single constant bridges this because characters have different proportions.

## Solution: `HipHeight` + `BoneYToWorldY`

### `CharacterDefinition.HipHeight`

Per-character Y distance from feet (ground contact) to Hips bone. Derived from `abs(lowest bone Y)` at idle frame 0.

| Character | CapsuleHeight | HipHeight |
|-----------|---------------|-----------|
| Manki     | 1.5           | 0.50      |
| FightGuy  | 1.7           | 0.82      |

### `CharacterDefinition.BoneYToWorldY(capsuleCenterY, boneLocalY)`

```csharp
public float BoneYToWorldY(float capsuleCenterY, float boneLocalY)
    => capsuleCenterY - CapsuleHeight * 0.5f + HipHeight + boneLocalY;
```

When grounded (`capsuleCenterY = CapsuleHeight/2`), this reduces to `HipHeight + boneLocalY`:
- Hips (`by=0`): `HipHeight` — at actual hip height
- Feet (`by≈-HipHeight`): ~0 — at ground
- Head: `HipHeight + head_baked_y` — correct

### `BakedAnimationData.GetMinBoneY()`

Utility to compute HipHeight from baked data. Scans all bones at idle frame 0 for minimum Y. Returns 0 if idle animation not found.

### ModelYOffset

Client model offset places feet at capsule bottom: `ModelYOffset = -CapsuleHeight/2`. Both characters use this convention.

## Call Sites

All bone→world Y conversions use `def.BoneYToWorldY(py, by)` — 4 locations total:
- `ServerSimulation.BuildEntitiesFromState` (hurtboxes)
- `ServerSimulation.Tick` per-entity hurtbox resolve
- `ServerSimulation.SpawnHitboxEvents` (bone-attached hitboxes)
- `ServerAbility` bone-attached hitbox spawn

## Verify

**Manki grounded idle:** `py=0.75, HipHeight=0.50, by=0` → Hips hurtbox Y = `0.75 - 0.75 + 0.50 + 0 = 0.50`
**FightGuy grounded idle:** `py=0.85, HipHeight=0.82, by=0` → Hips hurtbox Y = `0.85 - 0.85 + 0.82 + 0 = 0.82`
