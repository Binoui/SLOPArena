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

Since 2026-10-17 Wibou's weapon uses the generated `WibouPixelTrail` style from
`sloparena.presentation.wibou.build`: a light translucent sweep band whose `Dots` and
`Lines` children shed green and white pixel squares (see
[Wibou](../characters/wibou.md#combat-presentation--2026-10-16)). The TECH description
below still applies to any TECH style selected in Ability Lab.

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

### Bonk smear sweep

Bonk's `BonkWeaponAttachConfig.asset` selects the generated `BonkSmearTrail` style with
`bladeHilt` / `bladeEnd`, 1.0 blade coverage and 0.12-second mesh history. The style's band
leaves only the outer third of the plank: see-through bucket red with a cream streak and a
rust rim. The existing baked `_weapon_hilt` → `_weapon_tip` hitbox windows gate emission.

The shared sweep still accepts SLASH's `Background trail`, `Lines` and `Lines black` layers
and TECH's `Background slash` and dots, which other styles can use. Their circular
emitter/orbit is replaced by world-space emission along the moving blade, and separate hit
windows restart layer timing without joining the ribbons.

`PlayerRenderer.ResolvePresentationBone` resolves `_weapon_hilt` and `_weapon_tip` through
the equipped weapon's live trail anchors. Attached presentation lifetimes accept both
`Attacking` and mobile `Aiming` states for the same attack sequence; Hitstop preserves their
clock and interruption or replacement clears them. The rest of Bonk's look is generated by
`BonkPresentationBuilder`; see [Bonk](../characters/bonk.md#combat-presentation--2026-10-17).

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

The SLOPArena font material uses:

```text
Shader "SLOPArena/Particles/Font"
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

## Fighter hit feel

`FighterHitFeel` adds model-level reactions to every fighter in Play Mode. It follows the
accepted hits `CombatFeedback` already receives and the state `PlayerRenderer` applies.
Training, PvP and replays therefore share the same prediction and rollback behaviour as the
hit sparks. Camera shake and impact frames are deliberately absent; the owner wants hits to
read through the fighters themselves (see the
[combat presentation plan](../plans/2026-10-16-combat-presentation-pass.md)).

| Layer | Behaviour | Hook |
|-------|-----------|------|
| Victim shake | Alternating jitter along the knockback while `HitstopTicks > 0`, decaying across the freeze. Scales by tier; armour halves it. | Deform pivot position |
| Hit flash | Two white frames scaled by tier (light 55% to launch 90%), then a four-frame rim pulse in the attacker's silhouette colour (`MatchVisualStyle.RimColorFor`) that keeps the fighter's own colours. Reduced Flashing keeps only the pulse. | `_FlashColor`/`_FlashAmount` and the rim properties on `GraphicCharacter.shader`, through a `MaterialPropertyBlock` |
| Squash and recoil | The victim compresses along the hit and leans away from it, holds through Hitstop, then springs back with one overshoot. | Deform pivots |
| Attacker coil and strike | Before the first active tick the attacker crouches and leans back. Each active tick snaps forward and settles; the pose holds through Hitstop because attack ticks stop. Weight comes from the hitbox damage. | Deform pivots, driven by the stage's `HitboxEvents` |
| Launch stretch and smoke | Above 8 m/s of hitstun flight the victim stretches along the flight about its centre. Warm-grey `MovementMagicPoof` puffs are emitted along the travelled path, thickening up to 22 m/s, with wind streaks on hard launches. | Deform pivots; `MovementFeedbackEffect.BeginLaunchTrail` |

The deformation uses two transforms between the `PlayerRenderer` root and the model:
`Hit Feel Pivot` (lean × axis rotation, axis scale) and `Hit Feel Counter` (inverse axis
rotation). Together they scale the whole model along one arbitrary axis about a pivot,
`T(p)·L·R·S·R⁻¹·T(−p)`, without editing clips or bones. The simulated root, hurtboxes
and hitboxes never move. `TrySampleBakedWeaponPath` samples the undeformed pose and maps
the blade through the same deformation, so sword ribbons stay on the visible blade.
`EnsureModel` therefore accepts a model below the pivots.

`FighterHitFeel.Strength` holds per-layer multipliers (0 = off). 1 is the per-tier
calibration at the top of `FighterHitFeel.cs`. The shipped default is 1.6 for every layer,
the "high" level the owner picked on 2026-10-16. Tune a layer down in
`HitFeelStrength.Default`, or try values live in Play Mode on a fighter's `FighterHitFeel`
component. Review presets are absolute multipliers, so `high:1.6` reproduces the default.

### Hit-feel review captures

`sloparena.hitfeel.*` replays one deterministic duel per strength preset and writes every
tick from a close and a wide side camera. The attacker sits on the left, so launches travel
right. Presentation-only changes cannot alter the Shared result, so the presets show the
same frames with different effects.

```json
{"commands": [
  ["sloparena.hitfeel.prepare", "--stage", "splash_deck"],
  ["editor_play"],
  ["sloparena.hitfeel.capture", "--output", "tmp/hitfeel/fightguy-g4", "--attacker", "fightguy",
   "--victim", "manki", "--slot", "4", "--distance", "1.2", "--damage", "90",
   "--presets", "off:0,low:0.6,mid:1,high:1.6", "--layers", "all"],
  ["get_console_logs", "--severity", "error", "--limit", "20"]],
 "restore": [["sloparena.hitfeel.stop"], ["sloparena.hitfeel.restore"]]}
```

Run it like the stage-view batch: from stopped Edit Mode, with saved scenes and a Game View
open. `prepare` reuses the stage-view save/restore lifecycle (domain reload off) and
disables Training in memory. `capture` may repeat inside the same Play Mode batch.
Each preset spawns its own stage, so the stage atmosphere re-applies exactly as in Training.
The fighters land and settle for 40 ticks before the press, and the close camera fits their
gameplay capsules. Captures always show the default flash and report the local Reduced
Flashing setting, which otherwise leaves only the rim pulse.
`--layers shake,flash` isolates layers; unlisted layers stay off, and `off:0` is today's
presentation. `--orbit 65` turns both cameras toward the attacker's back, close to the
gameplay camera's angle. Each run reports its accepted hits (tick, tier, Hitstop) and `pressTick`;
a hit's frame is `tick − pressTick`. Output is `<output>/<preset>/{close,wide}/NNN.jpg`,
numbered from the press tick. [`tools/presentation_review.py`](../../tools/presentation_review.py)
writes capture batches, summarizes the run, and builds contact sheets, labelled
side-by-side WebP clips and a review page from the frames. Review with close-ups,
because gameplay-distance comparisons hide these effects.
`--track true` makes the wide camera follow both fighters' capsules each tick, for leaps
and launches that leave the fixed frame. Use `--presets high:1.6` alone to review
character effects at the shipped hit-feel strength.

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

Character effects are generated, package-owned assets. Follow the
[character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md) workflow;
Wibou is the reference.

- `<Name>PresentationBuilder.cs` defines a character's palette, textures and prefabs.
  Its `sloparena.presentation.<package>.build` command regenerates everything under
  `CharacterPackages/<package>/Presentation/` and rebinds it.
- `PresentationBuilderKit` owns the plumbing: the preview scene, textures, materials,
  meshes, prefabs, sounds, and binding to the catalog, the catalog's weapon-trail style
  and the projectile visual config. Rebuilds keep GUIDs and, for unchanged inputs, bytes.
- `PresentationFx` holds particle-system helpers. `ToonShapes` holds signed-distance
  shapes and the dark-rim, accent and white-core bands that keep shapes readable on
  bright stages.
- Timeline `emitPresentation` cues place effects, and each slot's `hitPresentationId`
  replaces the shared impact on accepted hits.
- Bone-attached content goes under a `PresentationFrame` child, which cancels the bone's
  scale (rigs can carry import scales) and can keep shapes upright.
- Effects that implement `IPresentationOwnerReceiver` get their fighter from
  `TimelinePresentationDispatcher` after spawning. `ChargeTierCues` plays children at
  charge tiers (up to five, each with an optional sound, and can grow a `Grow` transform per
  tier), `ChargeTierVariant` shows a child only after a fully charged attack, and
  `DamageScaleVariant` swaps looks by the spawn scale (a hit contact's damage scale or a
  placement's charge scale). `ChargePropFeedback` makes a held prop show the charge: it grows,
  shakes and heats Manki's spray can through `WeaponAttach.PropFor` and restores it on release.
  All of them read cosmetic state only.
- `ArmorGlowPresentation` holds a rim glow on its fighter while an armour-window cue lives
  (`FighterHitFeel.BeginGlow`/`EndGlow`) and plays a detached copy of a child when that armour
  absorbs a hit (`FighterHitFeel.ArmorAbsorbed`, from the accepted hit's `ArmorProtected`).
  `SnapToOwnerBone` moves part of a hit effect onto one of the attacker's bones, such as
  Bonk's bucket ringing on his own head.
- `SnapToFloor` moves a cosmetic child (a blast ring, a scorch, a debris landing plane) onto
  the stage floor from the heightmap `MatchBase` publishes as `PresentationFloor`, or hides it
  when there is no floor below.
- Projectile visuals: children with `LingerAfterProjectile` (smoke trails) detach and fade
  when the projectile goes; `OrientProjectile` turns Wibou's kunai and Manki's rocket
  point-first.
- `tools/glyph_mask.py` renders text such as FightGuy's 肉 into a mask that
  `ToonShapes`' `MaskField` turns into toon-banded textures.

The general rules still apply:

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
| Fighter hit feel (shake, flash, squash/lean, launch smoke) | Implemented; strengths under review | `FighterHitFeel.cs` + `GraphicCharacter.shader` |
| Wibou pixel trail, glints, code rain, crescents, kunai and hit bursts | Implemented; owner-approved 2026-10-17 | `WibouPresentationBuilder.cs`; see [Wibou](../characters/wibou.md#combat-presentation--2026-10-16) |
| FightGuy "Champ's Show": ki, champion gold, 肉 brand, chalk and gags | Implemented; owner-approved 2026-10-17 | `FightGuyPresentationBuilder.cs`; see [FightGuy](../characters/fightguy.md#combat-presentation--2026-10-17) |
| Manki "Controlled Chaos": toon blasts, gadget junk, his grin in the smoke, flame and backfiring gadgets | Implemented 2026-10-17; concepts approved, in-game check pending | `MankiPresentationBuilder.cs`; see [Manki](../characters/manki.md#combat-presentation--2026-10-17) |
| Bonk "BONK!": bucket-red smear, blunt thunks, a comic BONK! on big hits, armour glow, snort, dust, cracks and rubble | Implemented 2026-10-17; concepts approved, in-game check pending | `BonkPresentationBuilder.cs`; see [Bonk](../characters/bonk.md#combat-presentation--2026-10-17) |
| Character-specific hit layers | All four fighters | Per-slot `hitPresentationId` |
| Character Course feel (Sanctuary): paper-lantern targets, breaks, checkpoint frames, missed-target pillars, ALL CLEAR! and the finish | Implemented 2026-10-18; concepts approved, in-game check pending | `SanctuaryCoursePresentationBuilder.cs` + `CharacterCoursePresentation.cs`; see [Phase 4](../design/arcade-phase-4.md#feel-pass--2026-10-18) |

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
