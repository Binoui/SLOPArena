# FightGuy

FightGuy is the reference cooked character package.

## Local cooldown tuning — 2026-10-03

Ground and aliased air special cooldowns: A **120 ticks / 2 s**, E **240 / 4 s**,
R **240 / 4 s** (previously 360 / 6 s), F **720 / 12 s** (previously 1200 / 20 s).
Cyclone Kick and Fist of Fury return sooner without changing their hit reward
or commitment. Normals retain zero cooldown. These are playtest tuning values,
not a match-balance verdict; timers begin on ability completion/cancellation.
Current source compilation and Shared ground/air cooldown-expiry probes passed.
Persisted cooked packages and roster pins are unchanged.

## Local gamefeel tuning — 2026-10-02

FightGuy is the all-round reference for the differentiated roster. Source-only
Editor tuning sets Run to 12 m/s, air cap to 7 m/s, jump/short-hop impulses to
10.8/6.5 m/s, gravity to 28 m/s², and fall/fast-fall caps to 12/18 m/s.
Air acceleration remains 16 + 3.2 m/s².

The source-compiled Shared probe measures full hop **44 ticks / 1.993 m** and
short hop **25 ticks / 0.701 m**. These are flat-arena airborne intervals, not
input-to-landing totals or a human feel verdict.

Normals use approximately 1.15× their previous durations. Contact windows,
IASA and auto-cancel thresholds are retimed with the clips; landing lag,
geometry, damage and specials are unchanged. Grounded recovery has at least
four inactive ticks before another ability, or eight for ground.4.

| Normal | Duration | IASA | Hit trigger / active length |
|---|---:|---:|---|
| ground.1 | 29 | 16 | 7 / 6 |
| ground.2 | 29 | 26 | 6 / 6 |
| ground.3 | 33 | 28 | 8 / 7 |
| ground.4 | 69 | 64 | 12 / 8 |
| air.1 | 38 | 33 | 7 / 6; 18 / 6 |
| air.2 | 48 | 41 | 8 / 6; 14 / 23 |
| air.3 | 51 | 48 | 16 / 7 |
| air.4 | 62 | 57 | 23 / 8 |

All entries are authored 60-Hz ticks. Real Shared hitbox-lifetime and IASA-edge
checks passed. Cooked packages/roster pins remain unchanged; live Lab/Training
alignment verification is pending the retained Editor owner's window.


## Ownership

- Editable source: `client/Unity/Assets/CharacterPackages/fightguy/character.json`
- Cooked runtime package: `content-cooked/fightguy/`
- Cooked roster admission: `content-cooked/roster/manifest.json`
- Generated client catalog: `Resources/Generated/CharacterPackages/fightguy/`
- Rig: package-owned generated catalog binding
- Collision poses: cooked `poses.bin`

The source document is editor input. Runtime consumers do not load raw source JSON,
manual animation configs, C# character factories, or standalone FightGuy skeleton bins.


## Deterministic baseline

The package root is normalized to the lowercase package ID `fightguy`. The cook
dependency hash includes project-relative source and catalog identities, so this
path migration regenerated the manifest, client binding, and roster hashes. The
authored JSON, asset catalog, runtime definition, and pose payload remain unchanged.

## Runtime path

`BuiltInContentResolver` loads the roster requirement and four-file package. A fresh
`MatchContentCatalog` admits the immutable definition, baked poses, package identity,
and hashes. Training, PvP, Ability Lab, and GameServer consume that catalog entry.
`PlayerRenderer` resolves the generated animation catalog and package rig, then plays
semantic clips directly through Animancer.

## Specials

FightGuy specials are cooked timeline and capability bindings:

- A: Ki Shot — public `slop.ability.charged-projectile.v1`, three charge tiers
- E: Rising Dragon
- R: Cyclone Kick
- F: Fist of Fury

Ground and air A (physical Q) hold a stationary charge until release. Tier 2 starts
at 20 held ticks (0.33 s), tier 3 at 45 ticks (0.75 s); charge caps at 60 ticks
without auto-firing. Projectile radii are 0.5, 0.75 and 1.0 m. Release starts the
45-tick firing/recovery timeline and launches one shot at tick 8. Flight speed is
20 m/s (previously 25), with 112 flight ticks preserving approximately the prior
range. Damage remains 6 at every tier.

The charge pose `anim.ki-shot-loop` now binds the existing `fightguy_spell_q_loop`
clip; the firing timeline retains `anim.ki-shot`. Native catalog binding, package
cook/verification and roster refresh passed on 2026-10-11. Runtime visual checks
covered Manki; a live FightGuy charge playtest remains pending.

Shared aims from the launch position toward the current server-selected target's
torso, falling back to the nearest eligible opponent
within 20 m horizontally. With no eligible target, the shot fires level along
combat facing. It does not home after launch.

Ki Shot uses a public typed capability requiring runtime API `1.6.0`. Charge
thresholds, radii, firing startup and projectile tuning are editable in Ability
Lab's **Selected effect** fields. Dragon Beam is retired: its capability,
parameter codecs and runtime factory have been removed. Ground/air F execute
Fist of Fury; unsupported beam IDs fail admission.


Cyclone Kick's longer tuning uses 12 units/s for 60 ticks, then brakes horizontal
velocity once and releases velocity ownership during the remaining 12 recovery ticks.
The outer timeline ends at tick 72 (1.2 s); hitboxes spawn on ticks 7–48, with one
hit per opponent. Ground and air variants share this tuning. Airborne gravity resumes
when propulsion ends, and interruption preserves incoming knockback.
The capability duration bounds propulsion independently of the outer recovery timeline;
bot travel estimates use that bounded duration too. From rest on flat ground with no hits
or further input, the intended live Training trace is approximately 12 units of total
travel.
This is a local feel-test tuning; animation/gamefeel acceptance remains pending.

Fist of Fury uses `fightguy_spell_f_2` for its full two-phase presentation:
seven inward-stunning punches followed by a right-foot knockback finisher.
Punches use horizontal, zero-growth pull so damage percent cannot eject the victim.
Their fixed Hitstun covers the remaining timeline through the kick's active window;
the stronger inward pull keeps late catches in reach despite outward defensive drift.
Only the tick-88 right-foot hit launches outward. Ground and air F share this timeline.
The cooked timeline and hitbox direction are authoritative.

## Ki Shot projectile visual

Ground and air A (physical Q) use the game-owned
`Assets/CharacterPackages/fightguy/Presentation/FightGuyKiShot.prefab`, copied and
fully unpacked from Cartoon FX Remaster's `CFXR3 Iceball A + Ice Trail`.
`Resources/VFXConfigs/ProjectileVisuals.asset` binds both variants at scale 0.5.
Vendor materials, textures, and the effect script remain shared dependencies;
the vendor prefab is unchanged. Automatic clearing and camera shake are disabled
on the owned copy: `ProjectileVFXManager` controls placement and despawn from
Shared hitboxes in Training and server snapshots in PvP. Shared remains authoritative
for each charge tier's collision radius.

`PlayerRenderer` starts that same visual at `bone.right-hand` during Aiming.
The manager follows the animated hand in world space, without inheriting rig
scale, and retains it through firing startup. When Shared/the server supplies
the projectile, ownership transfers to the projectile entry and it follows the
authoritative position instead of the hand. Held and flying visuals scale to the
selected charge tier; cosmetic network snapshots carry the authoritative radius. The authored projectile origin is
unchanged; this is a visual handoff, not a hand-driven gameplay spawn.
Cancel, interruption, stock changes, model replacement, and renderer disable
clear held visuals. Consumed aim records prevent duplicate visuals from late aim
states; a projectile snapshot can transfer the visual before the release-state
packet arrives.

## Ki Shot hit visual

`ground.A` authors `hitPresentationId: presentation.fightguy.ki-shot.hit`;
`air.A` inherits it through its existing alias. The source asset catalog binds
that ID to `Presentation/FightGuyKiShotHit.prefab`, an owned copy of
`CFXR3 Hit Ice A (Air)` at root scale 0.25 with camera shake disabled.

Shared emits a `HitContact` presentation only after accepting the hit, at the
resolved contact point. Training consumes it directly; PvP waits for the server
event and deduplicates repeated delivery. It replaces the default hit graphic
and the projectile's redundant contact burst, not damage, Hitstop, sounds or
controller feedback. Blocks use shield feedback, and expiry retains its existing
burst without emitting an Ice hit. E and F now use their own configured contact effect; other attacks keep their default graphics.

The previously unbound `presentation.cyclone-kick.start` declaration and R
timeline event were removed with user approval. R's gameplay is unchanged.

## Special VFX

The package owns six additional prefabs under `Presentation/`. Timeline cues are
cosmetic and use canonical slots; the existing air aliases inherit them:

| Slot | Cue | Placement/window |
| --- | --- | --- |
| A / physical Q | `FightGuyKiRelease`, from Plasma Shoot Blue | Right hand, tick 4, 18 ticks; repeated release burst |
| E | `FightGuyRisingFist`, from Ice Shield | Right fist, tick 6, 25 ticks; brighter, denser downward trail; rising body accent from tick 8 for 20 ticks |
| R | `FightGuyCycloneSpiral`, with an owned 3D ribbon and Tornado Straight wind | Hips, tick 6, 42 ticks; upright 2.25-turn spiral wraps legs and torso |
| F | `FightGuyStrikeWrap`, from Ice Shield | Alternating hands at each of the seven authored punch windows; larger right-foot cue at tick 88; soft `FightGuyPowerAura` through tick 100 |

Every FightGuy attack slot, including ground/air normals and Q, binds
`FightGuyEmpoweredHit`, a compact adaptation of Hit Ice A Air, to accepted contacts. Shared carries a cosmetic scale of
`clamp(sqrt(acceptedDamage / 6), 0.6, 1.6)` in the existing placement packet:
F's repeated punches are smaller than its final kick. Blocks keep shield
feedback; whiffs show the limb cues alone. No damage, timing, projectile geometry,
or movement values changed in this pass.

Attached copies opt into `AttackBoundPresentation` so the runtime dispatcher
clears them on attack interruption, sequence replacement, owner disable/removal,
or the authored lifetime. Hitstop preserves the remaining attack ticks.
World contact bursts retain their own lifetime. Vendor prefabs remain shared
source assets; the owned copies disable vendor auto-destruction and lights.
`FightGuyPowerAura` adapts Power Aura Loop into a soft body glow and compact
ascending motes for E/F. R owns its helical mesh and material so its axis is upright
and its shape is independent of camera-facing slash meshes.

The live draft Lab now loads presentation bindings and includes world contact
bursts in its capture hierarchy. Owned preview animation graphs are explicitly
destroyed before script reload, including the inactive dummy's graph. This avoids
leaving native Playables with references to the previous managed domain.

Lab screenshots are a first visual pass; final arena readability and two-client
PvP acceptance remain separate checks.

## Normal VFX

All eight normals use the same short swing marks and moving limb ribbons as
Manki, recolored to FightGuy's blue-white energy palette. The package owns
`FightGuyNormalStrike`, `FightGuyNormalSweep`, and `FightGuyHeavySwing`, with
its own feathered crescent mesh and additive material. Each prefab combines
a small camera-facing arc with a world-space particle ribbon that follows the
animated striking limb. Heavy attacks use a broader arc and thicker ribbon.

| Slot | Limb / effect | Start tick / lifetime |
| --- | --- | --- |
| ground.1 | Right foot / small sweep | 7 / 8 ticks |
| ground.2 | Right hand / strike | 6 / 8 ticks |
| ground.3 | Right foot / sweep | 8 / 9 ticks |
| ground.4 | Right foot / heavy, left foot / smaller sweep | 12 / 10 ticks |
| air.1 | Right hand, then left hand / strike | 7 and 18 / 8 ticks each |
| air.2 | Left foot / sweep, then smaller follow-through | 8 and 14 / 8 ticks each |
| air.3 | Right foot / sweep | 16 / 9 ticks |
| air.4 | Right hand / heavy | 23 / 10 ticks |

The eleven new timeline cues start at the existing hitbox activation ticks.
The established `FightGuyEmpoweredHit` contact effect remains bound to every
normal. These swing copies also use `AttackBoundPresentation` for interruption
and lifetime cleanup. The Manki assets, existing FightGuy effects, gameplay
operations, rig, clips, and cooked pose payload are unchanged.

Verified on 2026-10-11 with accepted inspect/cook/verify/roster refresh, all
eight Lab whiffs, four ground contacts (4/7/7/14 damage), a shield contact,
and four existing Shared normal tests. Native Training exercised all eight
normals on Splash Deck and the heavy ground kick on After Hours; moving limb
ribbons were visible and cleared after recovery. The checks used a temporary
Disable Domain Reload option and restored the original Editor settings and
viewport. This pass does not establish two-client PvP behavior.
