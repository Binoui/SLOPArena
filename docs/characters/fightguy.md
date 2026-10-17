# FightGuy

> **Identity (owner, 2026-10-17):** a classic shoto and a genuinely talented martial
> artist: the underground champion, and proud of it. He is also a bit dumb, egocentric
> and not very bright, but never mean-spirited. His specials should show that
> personality, not only generic ki.

FightGuy is the reference cooked character package.

## Run foot contact — 2026-10-17

FightGuy's size is the roster reference (idle head 1.33 m; see
[roster size](adding-a-new-character.md#roster-size)). His run floated 4.5–17 cm above the
floor; `Art/Characters/fightguy/Animations/fightguy_run.anim` (ignored) now has a root Y
offset of 0.048, so its contacts touch the floor. Poses are unchanged.

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

Shared turns the fighter toward the target at launch, then aims from the launch
position toward that server-selected target's torso, falling back to the nearest
eligible opponent
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

## Combat presentation — 2026-10-17

"The Champ's Show", built for the identity above. Blue ki is his real skill, champion gold
is his ego, and 肉 ("meat", painted on his gi) is his brand: he stamps it on everything
and doesn't know what it means. Chalk dust marks his strikes. The gags only ever
embarrass him, and Fist of Fury stays serious because he is still the champ.
`sloparena.presentation.fightguy.build` (`FightGuyPresentationBuilder.cs`, on the shared
`PresentationBuilderKit`) generates every texture, mesh, material and prefab and binds
them to the asset catalog and the Ki Shot projectile visual. The 肉 comes from
`Presentation/Source/Niku.png`, rendered by `tools/glyph_mask.py` with the font used for
the gi. The workflow is the [character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md).
Only presentation cues and `hitPresentationId` values in `character.json` changed;
damage, hitboxes and timing did not.

| Move | Presentation |
| --- | --- |
| Normals (ground and air 1–4) | A blue toon ribbon behind the striking limb and a puff of chalk off the wraps; heavies use a gold-cored ribbon. Hits use `normal.hit`: a round blue "pow" and speed lines. Ground and air 4 use `normal.heavy-hit`, which adds a gold star and chalk specks. |
| Ki Shot (A) | While charging, ki motes are pulled into his palm. Reaching tier 2 and tier 3 pops a gold ring around the ball and paparazzi camera flashes around him (`ChargeTierCues`). Release: shockwave rings at the palm. The ball is a toon blue sphere with a slowly spinning gold 肉, a comet tail and motes, and grows with the charge tier. Hit: a blue impact with a small gold 肉 spark; a fully charged shot also slaps a red 肉 ink stamp on the victim (`ChargeTierVariant`). |
| Rising Dragon (E) | A blue ki glow on the uppercut fist shedding gold sparkles, and twin blue and gold helices growing up his body. At the top a spotlight finds him and his fist pops into a giant foam "#1" finger with a star glint. Hit: gold behind blue with rising speed lines. |
| Cyclone Kick (R) | Fast twin helices and wind rings around his legs, chalk kicked up from both feet. Gold dizzy stars circle his head through the recovery and vanish exactly when he can act: the gag doubles as a punish window. Hit: a blue impact in a gold swirl with chalk specks. |
| Fist of Fury (F) | He locks in: gold power lines rise around him over a gold ring on the floor, and his eye glints. Each punch throws a blue ghost fist. Punch hits are a small blue impact with a gold 肉; the finisher kick (`DamageScaleVariant`) is a gold-and-blue starburst stamped with a gold 肉 seal, a shockwave ring and speed lines. |

Bone-attached cues put their content under a `PresentationFrame` child, which cancels
the bone scale: FightGuy's rig bones carry a 1.5× import scale. Spirals, rings, the
dizzy stars and the foam finger also stay upright. `ChargeTierCues`,
`ChargeTierVariant` and `DamageScaleVariant` read cosmetic state only: the replicated
charge, the fighter's last fired charge and the hit contact's damage scale.
`TimelinePresentationDispatcher` binds the owner after spawning a cue or hit effect.

The previous Cartoon FX copies (`FightGuyKiShot`, `FightGuyKiShotHit`, `FightGuyKiRelease`,
`FightGuyRisingFist`, `FightGuyStrikeWrap`, `FightGuyPowerAura`, `FightGuyCycloneSpiral`,
`FightGuyEmpoweredHit`) are local-only and no longer bound by this pass. Older commits
still bind them, so delete them only once this pass is on `main`. Voice lines are planned
for later.
