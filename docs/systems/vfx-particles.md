# VFX — Combat Presentation

> Client-side presentation driven by accepted server-simulation events. Shared impact
> meshes communicate gameplay strength; particle trails and future character effects add flavor.

---

## Architecture

### Shared impact grammar

```
ServerSimulation.LastTickHits
         │ accepted hits: contact, direction, damage, force, hitstop
         ▼
ISimulationBridge.LastTickHits
         │
         ▼
CombatFeedback.OnTick()
         │ classify Light / Medium / Heavy / Launch
         ▼
GraphicHitEffect.Spawn()
         └─ pooled GameObject + one dynamic mesh + one shared material
```

**Key files:**

- `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- `client/Unity/Assets/Scripts/Runtime/Combat/GraphicHitEffect.cs`
- `src/Shared/SpellResolver.cs`

Training and PvP call the same `CombatFeedback` path after advancing their simulation bridge.
The rollback bridge snapshots current-tick hits before reconciliation can replay local history.
Ignored invincibility contacts and countered contacts are not emitted as accepted hit events.

### Bone Trail VFX

```
AbilitySpec.BoneTrails[]    ← per-ability data (BoneTrailDef struct)
         │
         ▼
  PlayerRenderer.UpdateAnimationState()
         │
         ├─ detects attack start → reads BoneTrails from current ability spec
         ├─ GetOrCreateBoneTrail(boneName) → instantiates/caches trail as child of bone transform
         ├─ sets startColor + startSize from BoneTrailDef
         ├─ enables emission (ParticleSystem.emission.enabled = true)
         │
         ├─ on attack end → DisableAllTrails()
         └─ on respawn → destroys all trail GameObjects
```

**Key file:** `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs`

- One reusable `BoneTrail.prefab` instanced per bone per character
- Cached in `Dictionary<string, ParticleSystem>` — zero per-frame allocation
- World simulation space: particles freeze in place, tracing the bone's motion arc
- Additive blending, short lifetime (0.1-0.3s), emission disabled by default
- Trails are toggled on/off via emission module, not created/destroyed per swing

### Wibou blade sweep

`PlayerRenderer` resolves `_weapon_hilt` and `_weapon_tip` from the baked attack
pose track. `WeaponAttach` captures simulation presentation ticks, including ticks
between rendered frames. Training resolver hitboxes and PvP sword-owner snapshots
gate emission after current character states have been presented; the ribbon never
decides damage or extends the sword's active window.

The trail-enabled renderer samples its attack animation on the baked 60 Hz frame
clock. Weapon positions are hips-relative, so cosmetic projection uses the animated
hips origin and the renderer's model offset. This avoids playback-clock drift and
the fixed-hip-height offset without changing Shared collision geometry.

`SwordSweepTrail` replaces the Cartoon FX prefab's authored ring geometry with
world-fixed blade history. It retains the TECH material, textures, start color,
color/dissolve curves, background slash, and dots. The source radial UV range is
`0..0.5`; longitudinal coordinates stay attached to samples as older sections
expire. Blade orientation is interpolated between recorded poses to round the
ribbon without shortening the blade or moving recorded endpoints. Dissolve is
adapted from whole-arc reveal to section age.
Artwork advances at a fixed scale based on half the source effect duration,
bounded below by the mesh history lifetime. This exposes more texture detail
without refitting surviving samples as the tail expires; the existing section-age
dissolve, width, and particle treatment are unchanged.

The prefab supplies appearance, not the generated sweep's width. Weapon entries
expose `TrailBladeWidth` (fraction of blade length inward from the tip) and
`HitboxMotionTime` (mesh history in seconds).
Dots use local size scaling so Wibou's large weapon import scale cannot enlarge
them, but their positions and simulation remain world-space. They are manually
emitted and advanced, without the prefab's circular emitter or automatic stepping.

In Ability Lab, **Assets → Weapon trails → Blade width** edits the selected
package's actual weapon asset, with a slider and numeric input. The change is
saved to that asset and uses the workspace's Undo/Redo; it is not a preview-only
override. Width changes redraw the existing foreground/background meshes live,
including when the preview is paused. Inspector edits are tracked by the same
control. Changing a C# field initializer does not overwrite a loaded asset value.
**Trail style** beside the width control selects a vendor prefab on the same weapon
asset, with Undo/Redo. Switching rebuilds the cosmetic trail and redraws paused
history; it does not replace the sword or use the vendor's authored spiral mesh.
Wibou can compare TECH (360 Thin Spiral) and TECH (360 Spiral) this way.

Editor previews own an animation-catalog snapshot so asset refresh cannot unload
their live animation bindings during trail tuning.

Stopping emission leaves the tail and glints to expire. New attack/stage identity,
respawn, teleport, or character replacement clears old sweep history; separate
active windows are not connected. Ability Lab rebuilds recent baked history and
the longer-lived glints when scrubbing, including frames after the sword window.
The airborne authoring arena places its virtual floor below the character, so a
selected air move is not canceled immediately by landing. Authoritative preview
states drive the displayed model's position and facing.
No publishing cook is required for these presentation-code changes.

### Bonk SLASH sweep

Bonk's `BonkWeaponAttachConfig.asset` selects a package-owned cream/rust/charcoal
copy of **CFXR4 Sword Trail SLASH (360 Spiral)** with `bladeHilt` / `bladeEnd`,
1.0 blade coverage, and 0.12-second mesh history.
The existing baked `_weapon_hilt` → `_weapon_tip` hitbox windows gate emission.

The shared sweep also accepts SLASH's `Background trail`, `Lines`, and `Lines black`
layers. Its background starts after the authored 0.04-second delay; dark line
emission starts after 0.05 seconds. Line layers retain their materials, tint,
size/lifetime curves, and rate-over-time curves. Their circular emitter/orbit is
replaced by world-space emission along the moving blade, with trailing velocity.
Their particles outlive emission and expire naturally; separate hit windows
restart layer timing without joining the ribbons.

TECH keeps its existing `Background slash` and five dots per moving pose sample.
Style and width remain editable through Ability Lab's weapon-trail controls.

The October 11 Bonk pass adds short blade glints, rough cut accents, foot dust,
accepted-hit steel bursts, bash clangs, landing dust/debris and Blade Storm wind.
`PlayerRenderer.ResolvePresentationBone` resolves `_weapon_hilt` and `_weapon_tip`
through the equipped weapon's live trail anchors. Attached presentation lifetimes
accept both `Attacking` and mobile `Aiming` states for the same attack sequence;
Hitstop preserves their clock and interruption or replacement clears them.

Targeted-leap launch/landing presentation IDs are optional package parameters.
Shared emits their phase events at the actual capsule feet; the landing effect
is independent of hit confirmation and is absent on a flight timeout. The
network packet recognizes both phase event sources. Ground and air Blade Storm
use separate bindings so the air version emits wind without foot dust.


---
## Match Text VFX

Match-start broadcasts use Cartoon FX particle text. The particle effect is the only
visible countdown layer; do not pair it with a second UI Toolkit text label.

### Match-start contract

| Mode | Sequence | Authority |
|------|----------|-----------|
| Solo | `READY` (2s) → `1` → `2` → `3` (1s each) → `SLOP IT OUT` | Local match gate |
| PvP | `READY` (2s) → `1` → `2` → `3` (1s each) → `SLOP IT OUT` | GameServer `Countdown` → `Playing` |
| Training | No countdown | Immediate control |

PvP uses `MatchInstance.CountdownDuration = 300` ticks. Solo uses the same
300-tick gate in `TrainingMatch`. `SLOP IT OUT` is shown when the match becomes
playable; the presentation does not grant gameplay control.

**Key files:**

- `client/Unity/Assets/Scripts/Runtime/UI/MatchTextVFX.cs`
- `client/Unity/Assets/Scripts/Runtime/UI/HUDManager.cs`
- `client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs`
- `client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs`
- `src/Server/MatchInstance.cs`

### Static versus dynamic particle text

`CFXR_ParticleText` has two modes:

- **Static (`isDynamic: 0`)** — the editor bakes each letter as particle
  children. Runtime text mutation is invalid.
- **Dynamic (`isDynamic: 1`)** — runtime `UpdateText()` regenerates letter
  children from a template particle system.

Match-start text uses static, authored variants because the MatchTextSmash
style is more controlled and reliable in-game. The variants are generated in
the Unity Editor from `MatchTextSmash.prefab`, not mutated during gameplay:

```text
MatchTextREADY.prefab
MatchText1.prefab
MatchText2.prefab
MatchText3.prefab
MatchTextSLOPITOUT.prefab
```

The editor authoring flow is:

1. Instantiate `MatchTextSmash.prefab` as a non-prefab editor object.
2. Set `CFXR_ParticleText.text`.
3. Call `UpdateText()` in the editor so the letter particle children regenerate.
4. Save a phrase-specific prefab under `Assets/Resources/MatchTextVFX/`.
5. Destroy the temporary instance.

The non-prefab step matters: the Cartoon FX editor guard prevents `UpdateText()`
from rewriting a prefab asset or connected prefab instance.

At runtime, `MatchTextVFX` resolves the phrase-specific prefab, instantiates it,
parents it to the gameplay camera, positions it at camera center, replays its
particle systems with unscaled time, and destroys it after the authored lifetime.
Other messages can use the dynamic prefab fallback.

### Text shader contract

The SlopArena font material uses:

```text
Shader "SlopArena/Particles/Font"
```

Source: `client/Unity/Assets/Shaders/SlopArenaParticleFont.shader`.

The shader is URP transparent particle rendering:

- `Blend SrcAlpha OneMinusSrcAlpha`
- `ZWrite Off`
- `Cull Off`
- `RenderPipeline = UniversalPipeline`

`CFXR_ParticleText` uses packed font texture channels:

| Font texture channel | Meaning |
|----------------------|---------|
| Blue | Background/particle color |
| Green | `Custom1` / text `color1` |
| Red | `Custom2` / text `color2` |
| Alpha | Glyph opacity |

The particle renderer must preserve the custom vertex streams consumed by the
shader: position, color, UV, `Custom1`, and `Custom2`. If `Custom1` or `Custom2`
is missing, the glyph can render with incorrect or missing gradient colors even
when the prefab and texture are valid.

Use `MatchTextFontSlopArena.mat` with the custom font shader. The URP font and
impact materials are separate assets; do not replace the font material with a
generic particle material unless the custom vertex streams and packed texture
contract are also replaced.

### Shader/VFX failure checklist

| Symptom | First check |
|---------|-------------|
| Pink or invisible text | Shader resolves to URP and the material is not missing |
| White/flat text | Particle renderer still has `Custom1` and `Custom2` streams |
| Missing outline/gradient | Font texture channel packing and `MatchTextFontSlopArena.mat` |
| Text appears behind the arena | Transparent queue, camera depth, and particle sorting |
| Text works in editor but not runtime | Static prefab was regenerated in the editor; runtime is not calling `UpdateText()` on `isDynamic: 0` |
| Duplicate countdown text | `HUDManager` must call `MatchTextVFX` without displaying a UI Toolkit label |

The Cartoon FX source package remains a local dependency. Keep source-package
provenance and shader/material changes aligned with
`docs/contributing/conventions.md`.

---

## Shared Impact Tiers

| Tier | Classification | Shape |
|------|----------------|-------|
| Light | Damage below 6 and launch force below 12 | Compact ring, four short rays, small incoming stroke |
| Medium | Damage 6–10 and launch force below 12 | Larger ring, six rays, directional wedge |
| Heavy | Damage 11+ and launch force below 12 | Double core, nine rays, thick directional stroke |
| Launch | Final launch force 12+ | Double ring, eleven rays, long launch-direction streaks |

`SpellResolver.HitResult` carries the collision point. `ServerSimulation` adds the final
attacker-to-target direction, post-hook damage, launch magnitude, and applied hitstop only
after the hit passes invincibility and counter interception.

`GraphicHitEffect` prewarms 16 instances. Every active impact uses one `MeshRenderer`, one
reused `Mesh`, and the same material. Vertex geometry and colors are rebuilt in cached lists;
no child strokes or per-hit materials are allocated. The effect holds through hitstop, then
expands and fades using unscaled time.

The shared tier is deliberately character-neutral. Manki smoke/fire, Wibou blade fragments,
and FightGuy ki must layer over it without changing its gameplay meaning.

## BoneTrail Prefab

**Path:** `Assets/Resources/Prefabs/VFX/BoneTrail.prefab`
**Material:** Reuses `Assets/Art/Materials/HitSpark.mat` (URP Particles/Unlit, Additive blending)

### Particle Module Configuration

| Module | Property | Value |
|--------|----------|-------|
| **Main** | Duration | 1s (continuous) |
| | Looping | true |
| | Start Lifetime | Random 0.1-0.3s |
| | Start Speed | 0 |
| | Start Size | 0.15 (overridden by BoneTrailDef.Width at runtime) |
| | Max Particles | 200 |
| | Simulation Space | World |
| | Play On Awake | false |
| **Emission** | Rate over Time | 120 |
| **Size over Lifetime** | Enabled | true |
| | Curve | 1.0 @ birth → 0.0 @ death |
| **Color over Lifetime** | Enabled | true |
| | Gradient | White (full alpha) → White (transparent), color from BoneTrailDef at runtime |
| **Renderer** | Render Mode | Billboard |
| | Material | HitSpark (URP Particles/Unlit, Additive) |
| **Shape** | Enabled | false (point emission from bone) |

### Behavior

- Prefab is instantiated at runtime as a child of the weapon bone (e.g. `mixamorig:RightHand`)
- Particles emit from the bone position and freeze in world space — the moving bone leaves a stationary trail behind it
- Short lifetime (0.1-0.3s) creates a tight arc, not a long smear
- Rate 120/sec = 2 particles per frame at 60fps
- Color and size set dynamically per ability from `BoneTrailDef`
- Same Additive blending + soft circle texture as HitSpark

### Data Declaration

`BoneTrailDef` struct in `src/Shared/AbilitySpec.cs`:
| Field | Type | Description |
|-------|------|-------------|
| `BoneName` | string | Skeleton bone name (e.g. `mixamorig:RightHand`) |
| `Width` | float | Particle size in meters |
| `R, G, B, A` | float | Trail color (RGBA) |

Per-ability data on `AbilitySpec`:
```csharp
public BoneTrailDef[]? BoneTrails;
```

### Adding a Trail to an Ability

In the character's data file (e.g. `FightGuyData.cs`), add to any ability spec:
```csharp
BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } },
```
No code changes needed — PlayerRenderer picks it up automatically.

---

## Adding Character-Specific VFX

1. Keep `CombatFeedback` tier classification unchanged.
2. Add a character/move presentation layer driven by the accepted hit owner and active slot.
3. Pool reusable effects and share materials.
4. Align directional effects to `HitResult.DirX`, `DirZ`, and `KnockbackAngle`.
5. Keep ordinary effects inside the shared tier's screen coverage; major launch and KO effects
   may briefly exceed it.

---

## Current VFX State

| VFX | Status | File |
|-----|--------|------|
| Shared impact tiers | Implemented | `GraphicHitEffect.cs` + `CombatFeedback.cs` |
| Bone trails | Implemented | `BoneTrail.prefab` + `PlayerRenderer.cs` |
| Wibou baked blade sweep | Implemented | `SwordSweepTrail.cs` + `WeaponAttach.cs` |
| Match-start text broadcasts | Implemented | `MatchTextVFX.cs` + `MatchTextVFX/MatchText*.prefab` |
| Character-specific hit layers | Not implemented | — |

### Authored bone trails

| Character | Ability | Bone | Color | Status |
|-----------|---------|------|-------|--------|
| FightGuy | Authored attack stages | Stage-defined | Blue | Implemented |
| Manki | Authored attack stages | Stage-defined | Orange | Implemented |

### Future

| VFX | Status |
|-----|--------|
| Dash start and trail | Not implemented |
| Jump and landing dust | Not implemented |
| Manki flame and explosions | Not implemented |
| Wibou blade fragments and glints | Not implemented |
| FightGuy ki layer | Not implemented |
| KO and respawn presentation | Not implemented |

---

## References

- `CombatFeedback.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/CombatFeedback.cs`
- `GraphicHitEffect.cs` — `client/Unity/Assets/Scripts/Runtime/Combat/GraphicHitEffect.cs`
- `PlayerRenderer.cs` — `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs`
- BoneTrail prefab — `Assets/Resources/VFX/BoneTrail.prefab`
- `BoneTrailDef` struct — `src/Shared/AttackData.cs`
- Match text controller — `client/Unity/Assets/Scripts/Runtime/UI/MatchTextVFX.cs`
- Match text prefabs — `client/Unity/Assets/Resources/MatchTextVFX/`
- Font shader — `client/Unity/Assets/Shaders/SlopArenaParticleFont.shader`
- Historical prototype spec — `docs/superpowers/specs/2026-07-19-hit-spark-vfx-design.md`
