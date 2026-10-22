---
id: "wibou"
name: "Wibou"
title: "The Kitsune Blade"
status: "Implemented (sim) — art/anim pending"
archetype: "In-your-face spacing duelist. Wins neutral with disjointed blade reach, converts hits into vertical air juggles. Agile, aggressive mid-range control — not a full run-in rushdown."
source_image: "TBD"
inspiration: "Marth (Fire Emblem / Smash) — spacing, reach, punish game, counter, exploitable recovery. Amaterasu (DKO) — fast agile sword, launcher, air-juggle payoff."
palette:
  # Approved presentation accent: Matrix/hacker green; model palette remains open.
  fur: "TBD (white / silver, or classic fox orange)"
  accents: "#91FF69 (Matrix/hacker green, from the existing sword VFX base tint)"
  blade: "TBD"
kit:
  - slot: "LMB"
    name: "Light Slash Combo"
    type: "melee"
    description: "3-4 hit slash chain. Fast, low commit, modest knockback on finisher. Close-range bread-and-butter."
  - slot: "Air LMB"
    name: "Air Slash (3 hits)"
    type: "melee"
    description: "3-hit aerial slash. Low commit, predictable near-neutral/upward KB. Doubles as manual juggle-sustain and a fall-stall (minor recovery aid)."
  - slot: "RMB"
    name: "Charged Spin"
    type: "charge"
    description: "Tap = quick horizontal spacing poke. Hold = charged spinning vertical slice = the KILL move (big horizontal knockback toward blast zone at high %). Slow, telegraphed, high reward."
  - slot: "Air RMB"
    name: "Falling Slash"
    type: "melee"
    description: "Committed downward slash. Strong, predictable DOWNWARD knockback. Edgeguard / off-stage finisher — deliberately the opposite of the juggle."
  - slot: "Q"
    name: "Counter"
    type: "counter"
    description: "Marth-style parry window. If struck during the window, riposte LAUNCHES the attacker (knockback, no lingering stun -> can lead into a juggle). Read-based answer to being rushed inside the blade."
  - slot: "E"
    name: "Charged Dash Slash"
    type: "mobility"
    description: "Forward dash + slash. Tap = short reposition, hold = full gap-close. Distance scales with charge. Primary horizontal recovery. NO stun (differentiates from FightGuy Cyclone Kick)."
  - slot: "R"
    name: "Rising Slash"
    type: "mobility"
    description: "Multi-charge homing rising slash. THE SIGNATURE. Launches grounded enemies, re-launches airborne ones. Charge refunds on hit -> sustains the juggle as long as you keep connecting. Whiffing in empty air gives only capped height -> also serves as (honestly exploitable) vertical recovery-from-below."
  - slot: "F"
    name: "Blade Flurry"
    type: "ult"
    description: "Committed moving multi-slash flurry (forward/rising movement) that ends in a hard launch. Telegraphed startup, dodgeable, heavily punishable on whiff. Kept intentionally simple — a solid finisher, not a showstopper. Foxfire + tails visual."
---

# Wibou — The Kitsune Blade

> **Identity (owner, 2026-10-17):** Wibou is an ironic mall-ninja weeaboo: a cringe Neo
> from *The Matrix* with a katana, kunai and green "hacker" pixels. The kitsune, fox tails
> and foxfire in the frontmatter and the historical concept below are retired; do not use
> them for presentation. See [Combat presentation](#combat-presentation--2026-10-16).
> **Package-native character.** The canonical kit contract is the package-native 16-entry grid: grounded and aerial variants of `1 / 2 / 3 / 4 / A / E / R / F`. `LMB` and `RMB` are camera controls, not persisted move identities. Authoring source lives under `client/Unity/Assets/CharacterPackages/wibou/`; cooked runtime content is admitted through the Match Content Catalog.
> Status: Included in the cooked roster. Legacy Shared registry definitions are retired; new changes must use the package compiler, asset catalog, cooked package, and Match Content Catalog path.
> Current Q/A is Shuriken Toss, not the Counter in the historical concept below. Ground and air fire three projectiles after 8 startup ticks, with no manual aim, crosshair, hold, or release phase. Shared aims at the current server-selected target's torso at launch, retaining the ±15° spread; it uses the nearest eligible opponent within the existing 20 m horizontal targeting range if the selection is invalid, or fires level along combat facing if none exists. Projectiles do not home. The imported `kunai.fbx` model remains cosmetic; Shared retains all flight and hit authority.
> Current R/Dash Slash uses the public `slop.ability.charged-directional-dash.v1` primitive for ground and air. Hold manual directional aim; release locks direction with no target assistance or later steering. Tier 2 starts at 20 ticks, tier 3 at 45, and charge caps at 60 without auto-release. Distance scales continuously from 1.2 to 6.4 m at 10.5 m/s. A weak hips/torso traversal hit deals 1 damage once per opponent; the independent sword hit deals 6/9/12 damage during the last four travel ticks and seeks animation tick 31. Dash phases through fighter pushboxes only: shields and terrain stop it, attacks can interrupt it, and it grants no invulnerability. Terrain stops it only when a tick keeps less than 60% of its forward travel (walls hit at about 40° or more; shallower grazes slide along), so ground seams, gentle ramps and landing mid-dash no longer end it short of the marker. During recovery, body overlap moves the other fighter, so Wibou ends on the marker even inside the fighter he just cut. Release ends in 24 ticks of committed recovery, including on whiff; charge and dash remain interruptible. While R is held, Wibou plays his own charge loop (`anim.wibou.r-charge`, `wibou_r_charge_loop.anim`, 0.5 s, GenRA option B approved 2026-10-18): a low, wide stance with the blade trailing behind and the left hand beckoning. Before, the hold played the dash clip itself. The loop is presentation only: charge hurtboxes still sample the dash clip's first frame. The beckon points about 77° off the dash direction because it was authored in the toolkit's former hip frame.
> Current E/Rising Slash has two charges that return only through regen. Since 2026-10-20 a hit no longer refunds one (owner: a legacy rule, far too strong); the refund in the historical concept below is retired.
> Current sword VFX: since 2026-10-17 the Wibou weapon config references the generated, package-owned `WibouPixelTrail` style (see [Combat presentation](#combat-presentation--2026-10-16)); it previously referenced `CFXR4 Sword Trail TECH (360 Thick)` for its shader, mask texture, color gradient, and authored dissolve curves. Unity bends those style inputs onto a short-lived blade-sweep ribbon connecting recent `bladeHilt` and `bladeEnd` poses; it does **not** instantiate the prefab's one-shot 360° mesh. Baked blade emission starts at the authored hilt-to-tip hitbox trigger and follows the swing through its animation duration, independently of the damage-window end. Hitstop pauses ribbon/particle aging with the pose, including the final frozen snapshot. A stationary sword produces no new area; closing or interrupting the swing stops emission and lets the existing tail fade over the configured 0.12 s, even when an attack-only weapon hides. Death, teleport, and a new attack/stage identity clear old history. Damage and hitbox lifetimes remain Shared-owned.
> Inspired by: **Marth** (spacing, reach, punish, counter, exploitable recovery) × **Amaterasu** (DKO — fast agile sword, launcher, air-juggle payoff).

## Coat — 2026-10-21

Wibou wears the owner's Tripo coat: an open, short-sleeved coat with a popped collar,
a back vent and a hem 17 cm below the hip joint. It is a second skinned renderer in
`Resources/Characters/Wibou.prefab`, bound to his own bones. The skirt sways on four
`BoneChainSpring` chains under Hips. `wibou.fbx`, the Avatar, the clips and `poses.bin`
are unchanged, so hurtboxes and hitboxes are too. The coat's art is local and ignored
until it is force-added. [tools/blender/wibou_coat](../../tools/blender/wibou_coat/README.md)
rebuilds and rebinds it. In-game look and sway are awaiting the owner's verdict.

## Size — 2026-10-17

Wibou was the tallest-looking fighter: his model is about 20% bigger than FightGuy's (idle
head 1.58 m against 1.33 m), and his hips sat 18 cm above `hipHeight`, so his hurtboxes
were 20–27 cm below his body. He is now FightGuy's height in the
[roster size](adding-a-new-character.md#roster-size) order: `visualScale` 1 → 0.85 and
`modelYOffset` 0.194 → 0.034 (superseding the 2026-09-15 value below). Idle head 1.34 m,
hips 0.85 m against `hipHeight` 0.82. The cook samples poses at `visualScale`, so his
hurtboxes shrank with him. The katana keeps its length, so it looks a little longer on
him, but his hands and grip move in with his body: the blade tip's farthest reach in the
eight normals drops 17–20 cm (about 9%). Every grounded clip still touches the floor
(within 1 cm).

## Hitbox alignment — 2026-10-17

The cook used to rebuild every pose around a fixed hip point, so Wibou's tested sword sat up
to 37 cm from the drawn blade in his normals (Double Slash's hitbox 36 cm above it) and 53 cm
in Skyfall's dive. The cook now bakes the drawn hips (`_hips_offset`), and the sword hitbox
and hurtboxes follow the body as drawn ([hitbox system](../systems/hitbox-system.md#drawn-hips--package-cooks-since-2026-10-17)).
No move data changed, but reach against a standing FightGuy did (hurtboxes paired by name, issue #282):

| Move | Before | After |
|---|---|---|
| ground.1 Quick Slash | 0.30–2.40 m | 0.30–1.95 m |
| ground.2 Double Slash | 0.30–1.60 m | 0.30–2.00 m |
| ground.3 Up Slash | 0.30–2.25 m | 0.30–2.40 m |
| ground.4 Heavy Down Slash | 0.30–2.15 m | 0.30–1.95 m |

Later the same day, sword hitboxes started testing the path the blade sweeps between ticks
and every pose moved to the exact clip time
([clip clock and sweep](../systems/hitbox-system.md#clip-clock-and-swept-bone-hitboxes--since-2026-10-17)).
Five windows were then lined up with the strike as drawn. The maintainer approved hits
behind Wibou for Double Slash's second slash and an earlier Air Up Slash:

| Move | Window | Why |
|---|---|---|
| ground.2 Double Slash | t9–t15 → t9–t16 | The second slash finishes one tick later, behind Wibou. |
| ground.4 Heavy Down Slash | t41–t55 → t41–t47 | The blade rests on the ground from t48. |
| air.2 Reverse Slash | t4–t8 → t4–t11 | The slash keeps moving fast for three more ticks. |
| air.3 Air Up Slash | t10–t16 → t9–t16 | The upswing starts low in front between t9 and t10. Startup aim correction now ends at t9. |
| air.4 Air Heavy Down Slash | t8–t15 → t8–t16 | The strike finishes one tick later, the part that reaches below. |

## Approved presentation accent — 2026-10-02

Wibou's character accent is **Matrix/hacker green**, shared by sword VFX and ability-icon blades/arcs. The current weapon config's trail GUID resolves to `CFXR4 Sword Trail TECH (360 Thick)`, inheriting `(360 Spiral)` with base RGB `(0.5673758, 1, 0.41134745)`; its flat 8-bit icon equivalent is **`#91FF69`**. The particle/material HDR boost is not reproduced as UI glow. This replaces the undecided foxfire accent in the historical concept below; it does not approve a model/costume redesign.

The 2026-10-11 trail pass reduces `wibou_weapon.asset`'s `TrailBladeWidth`
from `0.8` to `0.6`. Both foreground and background ribbons cover the outer
60% of the blade, giving a 25% narrower band with the same tip trajectory.
The green TECH pattern, glints, dissolve and 0.12-second mesh history remain
the existing style. Blade attachment transforms, gameplay, animations and
cooked poses are unchanged. This setting applies to every sword swing,
including aerials and specials. Adjust it in Ability Lab under
**Assets → Weapon trails → Blade width**.

Grounded-normal icon readings: **G1 forward-reaching downward sweep; G2 forward-reaching rising sweep; G3 vertical rising swing; G4 downward hit**. Icons simplify the dominant active portion, omitting return hooks that imply a different action. All selected poses/paths remain inside their authored hitbox windows. Full active-only blade/hitbox measurements remain separate and must not be replaced by these cropped art compositions.

The four transparent 512px icon samples, 50px-interior/70px-frame previews, and source/tick/color provenance are under `.impeccable/review/wibou-grounded-blade-icons-20261002/`. Artwork is individually fitted to a 448px safe area inside each 512px export, preserving proportions: about 44px on its long edge at native HUD size, with a small margin and no slot enlargement. Measurement plots retain their common scale; icon framing is not a reach comparison. These are inspected art samples, not yet wired runtime icons. Shared gameplay, existing VFX assets, normal/special category frames, damage-tier colors, and global menu colors are unchanged.

## Combat presentation — 2026-10-16

This is the first character pass of the
[combat presentation plan](../plans/2026-10-16-combat-presentation-pass.md#wibou-ironic-mall-ninja-91ff69--first),
revised on 2026-10-17 for Wibou's current identity.
`sloparena.presentation.wibou.build` (`WibouPresentationBuilder.cs`, built on the shared
`PresentationBuilderKit`) generates the textures, crescent mesh, materials and prefabs
under `Presentation/`. The [character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md)
skill describes the workflow. It binds them in the
asset catalog, `wibou_weapon.asset` (sword-trail style) and
`Resources/VFXConfigs/ProjectileVisuals.asset` (flying kunai). Saving the catalog cooks the
package. The look is green "hacker" pixels and Matrix code rain with anime-style glints.
Shapes have toon bands: a dark green rim, the `#91FF69` accent and a white core. They are
alpha-blended so they stay readable on bright stages, where pure additive glow washed out
to white. Only the presentation cues and `hitPresentationId` fields in `character.json`
changed; damage, hitboxes and timing did not.

| Move | Presentation |
| --- | --- |
| Sword trail (every swing) | `WibouPixelTrail`: green and white pixel squares shed along the blade over a light, translucent sweep with a crisp edge. It replaces the opaque dark-green TECH ribbon. |
| Normals (ground and air 1–4) | A star on the blade tip 5 ticks before the first hitbox. Hits use `blade.hit`: an impact star, a slash and pixel shards. Ground and air 4 use `blade.heavy-hit`, which adds a second slash and a ring. |
| Shuriken Toss (A) | Each kunai flies point-first, 1.4× larger than before, with a white-to-green streak and a trail of pixels from the ring and an anime shine about four times a second. At release, a hand glint, three streaks and a light draw sound. On contact, `kunai.hit`: a small star, shards and a thunk. |
| Rising Slash (E) | Code rain falls from the hips during the rise, then a vertical crescent at the swing, upward streaks and a slice. |
| Dash Slash (R) | While charging, a hilt glint, rising pixels and a draw sound. On release, a pixel blink where Wibou vanishes, thin streaks behind Wibou, then diagonal cuts along the path that pop 0.17 s after Wibou passes each one, with a click-then-slice sound. |
| Skyfall (F) | At startup, a curtain of code rain around Wibou, a pixel ring from the feet and a draw sting. At the launch, a vertical blade column, speed lines and shards, then code rain through the rise. At the spike, a forward-down crescent and a heavy slice. |

World-placed cues are offsets from the capsule centre, 0.85 m above Wibou's feet. A cue's
`durationTicks` must cover its longest particle and its sound: the dispatcher destroys the
instance, including its `AudioSource`, when the duration ends. The sounds are CC0 Kenney
clips in the ignored `Resources/Audio/SFX/Wibou/` folder (see
[provenance](../assets/credits-and-licenses.md)). Without them the effects are silent, and
the builder lists the missing clips. The flying kunai nest the local-only
`Art/Characters/wibou/Models/kunai.fbx`; without it they fly as effects only. The FBX
stores the kunai diagonally, so the builder turns it with a measured ring-to-tip axis and
ring normal: the point lies on the prefab's +Y, which `ProjectileVFXManager` aligns with
the flight, and the ring faces sideways. Before 2026-10-17 the kunai flew ring-first.

The trail style is a regular `SwordSweepTrail` style prefab: the root's material, tint and
fade draw the sweep band, and its `Dots` and `Lines` children are the pixel squares.
Ability Lab's **Trail style** picker can still switch back to a TECH prefab for comparison;
the next build rebinds the pixel style.
Review changes with `sloparena.hitfeel.capture --attacker wibou --presets high:1.6`, adding
`--track true` for Rising Slash and Skyfall
([review captures](../systems/vfx-particles.md#hit-feel-review-captures)).

## Concept

A **kitsune (fox spirit) swordswoman** — agile, precise, trickster-elegant. Where **FightGuy** is a close-range fists-and-ki execution brawler and **Manki** is an explosive zoner, Wibou owns the **mid-range sword game**: a disjointed blade used aggressively to control space and stay glued to the opponent at the edge of its reach, then convert a clean hit into a vertical air juggle.

Fox-spirit theme gives a distinct roster silhouette (monkey / human / fox) and natural VFX flair — **foxfire** trails on slashes, tails flourishing on spins and the counter riposte.

*Species/gender chosen (female kitsune), name locked (**Wibou**); exact palette and tail count still to refine — see Open Decisions.*

## Archetype

**In-your-face spacing duelist.**
- Neutral: out-space with **disjointed blade reach** and aggressive mid-range pressure. Uniform hit quality — **no positional sweetspot / tipper** (positional sweetspots are unreadable in 3D alongside warp + lunge).
- Payoff: convert a hit into **launch -> air juggle -> finish** (the "fun" of the kit).
- Mobility: agile, for **repositioning and spacing**, not stealth/escape. Not a full run-in rushdown — a pressure-at-range fighter.
- **Designed weakness:** weak once an opponent gets *inside* the blade (classic Marth flaw) and an **honestly exploitable recovery** (see below).

### Local gamefeel tuning — 2026-10-02

Wibou retains the fastest Run at 14 m/s. Air cap remains 7 m/s and air
acceleration 16 + 3.2 m/s². Jump/short-hop impulses remain 12/7.8 m/s;
gravity is reduced to 32 m/s², with fall/fast-fall caps unchanged at 14/22 m/s.
Source-compiled Shared measures full hop **43 ticks / 2.151 m** and short hop
**27 ticks / 0.887 m**.

Normals use approximately 1.05× previous durations, preserving the fast end of
the roster. Contact windows and auto-cancel thresholds follow the stretched
animation; landing lag, geometry, rewards and specials are unchanged.

| Normal | Duration | IASA | Hit trigger / active length |
|---|---:|---:|---|
| ground.1 | 63 | 28 | 13 / 12 |
| ground.2 | 63 | 23 | 9 / 8 |
| ground.3 | 63 | 26 | 14 / 7 |
| ground.4 | 74 | 63 | 41 / 7 |
| air.1 | 27 | 23 | 6 / 8 |
| air.2 | 25 | 21 | 4 / 8 |
| air.3 | 27 | 23 | 9 / 8 |
| air.4 | 36 | 31 | 8 / 9 |

All entries are authored 60-Hz ticks. Ground normals retain at least four inactive
ticks before another ability, eight for ground.4. Shared lifetime/IASA probes
passed; this is source-only Editor tuning, not updated cooked content.
Live Lab/Training alignment verification awaits the retained Editor owner.

### Grounded animation correction — 2026-09-15

The package catalog binds corrected `.anim` assets for idle, Run, grounded normals,
and specials. Idle, Run, and grounded 1 now share the animation pack's recommended
T-pose Avatar, as grounded 2 already did; animated first-frame Avatars are not
interchangeable reference poses.

- Idle's normalized body-height reference is `0.924141`. Preserve authored body Y
  against that common reference instead of recentering each clip's initial crouch.
  E retains its existing, separately centered body curves.
- Grounded 3 is rebuilt from `M_katana_Blade@Attack_4Combo_3_Inplace.FBX`.
  Grounded 4's missing body translation is restored from `M_katana_Blade@Skill_H.FBX`,
  mapped onto its existing edited timeline without changing its other curves.
- A, R, and F regain body translation from the matching `Skill_E`, `Skill_A`, and
  `Skill_G` sources. Authored airborne poses remain airborne.
- Target-rig Humanoid foot IK and sole-clearance corrections are baked into these
  animation assets. Runtime foot IK and root-motion movement remain disabled:
  playback and subsequent pose cooking consume the same corrected source clips.
- Run retains its original `0.533333 s` cycle and corrected foot poses. The initial
  `0.2101266 s` stride-matching attempt looked too frantic in human playtesting and
  was reverted. Gameplay remains `15 m/s`; some sliding is accepted in preference
  to accelerated leg motion. The native playback window covers the full cycle.
- `presentation.modelYOffset` is now `+0.194`, superseding the idle-only `+0.286`
  compensation. Automatic offset stays disabled; collision dimensions and gameplay
  values are unchanged.

Verified through the transient Editor development catalog: all eight grounded slots,
Run → idle, and jump → landing → idle. The corrected bindings and poses are now
included in the cooked Wibou package and its refreshed roster requirement.

### Historical grounded IASA tuning — 2026-09-15

This earlier pass introduced the movement/attack IASA boundary. Its values
remain a historical record; the local source candidate above supersedes them.

| Slot | Previous IASA | Tuned IASA | Intent |
|------|--------------:|-----------:|--------|
| `ground.1` | 21 | 24 | Delay cancellation past the final hitbox |
| `ground.2` | 30 | 22 | Shorten recovery |
| `ground.3` | 28 | 25 | Shorten recovery |
| `ground.4` | 39 | 53 | Delay cancellation past the final hitbox |

The timing pass changes only these IASA values; move durations, hitboxes, aerial data,
and other gameplay fields remain unchanged. The transient Editor development catalog
remains the local iteration path. These values are now included in the cooked package
and admitted roster alongside the grounded animation correction.


## Design Pillars

### Predictable knockback, emergent combos
Every ability has **consistent, learnable knockback** (angle + magnitude, scaling with %). Combos are **discovered by the player**, not scripted into the kit. Smash's philosophy, not DKO's fixed launch-chains. No ability is wired to "feed into" another — R reliably launches up, aerials have honest KB, and the juggle *emerges* because the values are readable.

> This pillar is broader than one character — it describes the game's intended combat feel. Consider promoting it to `docs/systems/combat-systems.md`.

### Recovery is a system, not one move
Recovery is spread across slots and is deliberately **functional-but-exploitable** (the character's signature weakness):
- **E** (charged dash) — horizontal distance, scales with charge.
- **Air LMB** (3-hit) — stall / slight drift, buys airtime.
- **R** (rising slash) — capped vertical height when whiffing in empty air (the flaw); only "extends" when it actually connects with an enemy.

## Kit

| Slot | Name | Role | Mechanic |
|------|------|------|----------|
| **LMB** | Light Slash Combo | Light attack | 3-4 hit slash chain, modest KB finisher |
| **Air LMB** | Air Slash (3 hits) | Aerial / juggle-sustain | 3-hit air slash, predictable upward KB, fall-stall |
| **RMB** | Charged Spin | Heavy / **kill move** | Tap = horizontal poke; Hold = charged spin = big horizontal launch (blast-zone kill) |
| **Air RMB** | Falling Slash | Aerial heavy / spike | Hold to charge: tap = quick slash (9 dmg); charged = heavier slash (13 dmg). Strong downward KB, edgeguard/finisher |
| **Q** | Counter | Counter / read | Parry window -> riposte **launches** attacker (knockback, no lingering stun) |
| **E** | Charged Dash Slash | Mobility / gap-close | Tap = short reposition, Hold = full gap-close; horizontal recovery; no stun |
| **R** | Rising Slash | **Signature** launcher + juggle + vertical recovery | Multi-charge homing uppercut; charge refunds on hit -> sustains juggle; capped self-height on whiff |
| **F** | Blade Flurry | Burst / finisher | Committed moving multi-slash flurry, ends in a hard launch; telegraphed, punishable on whiff |

## Gameplan

Control mid-range with LMB pokes and the disjointed blade -> land a hit or gap-close with E -> **R** to launch -> chase with double-jump/dash + Air LMB, re-launching with R (charges refund on hit) -> cash out with **RMB (charged)** for the kill at high %, or **Air RMB** to spike off-stage. **Q (Counter)** punishes opponents who try to rush inside your range.

## Weaknesses
- **Inside the blade:** loses to characters who get past the reach and pressure up close.
- **Recovery:** functional but exploitable — mostly horizontal (E) + capped vertical (R on whiff). Vulnerable to edgeguarding.
- **Commitment:** RMB charged spin and F are telegraphed; whiffing is heavily punishable.
- **Counter is a read:** whiffing Q leaves a vulnerable window.

## Open Decisions (implementation-time only)

Kit is fully specified. Remaining items are tuning/art, not design:

1. **Palette** — species/gender/name locked (female kitsune, **Wibou**). Open: exact palette (foxfire color, fur color), tail count.
2. **Numbers** — all damage / KB / charge counts / CDs / charge-refund timing TBD; tune in implementation against `character-kit-design-principles.md` baselines.
3. **Q = Counter** — locked as v1 but flagged "a bit lazy"; revisit after playtest depending on how oppressive rushdown feels in practice.

## Animation Needs (soft constraint)

- Grounded and aerial normals `1`–`4` — package slots `ground.1`–`ground.4` and `air.1`–`air.4`.
- `A`, `E`, `R`, and `F` specials — package slots `ground.a`/`air.a`, `ground.e`/`air.e`, `ground.r`/`air.r`, and `ground.f`/`air.f`.
- Movement, hit reactions, and recovery clips remain presentation bindings; gameplay timing comes from the cooked timeline.

## Package files
- `client/Unity/Assets/CharacterPackages/wibou/package.json` — package identity and attribution.
- `client/Unity/Assets/CharacterPackages/wibou/character.json` — gameplay semantics and canonical 16-entry grid.
- `client/Unity/Assets/CharacterPackages/wibou/CharacterAssetCatalog.asset` — package-local Unity bindings.
- `content-cooked/wibou/` — immutable cooked runtime package admitted by the roster manifest.

See `docs/characters/character-kit-design-principles.md` for design patterns and `docs/systems/combat-systems.md` for universal combat mechanics.
