# Combat presentation pass: hit feel, specials, audio and deformation

**Status:** in progress. The owner reviewed the direction below on 2026-10-16 and approved
the hit feel, Wibou and FightGuy passes by 2026-10-17; Manki and Bonk are next.
Section 1 (universal hit feel) and the deformation pivot are implemented. After
the review the owner picked "high" (1.6×) for every layer, to be tuned down if needed;
see [Fighter hit feel](../systems/vfx-particles.md#fighter-hit-feel). Wibou's effects and sounds from
section 3 are implemented; on 2026-10-17 the owner kept the hit effects and green pixels,
replaced the sword trail with pixels, dropped the flames and asked for louder kunai. See
[Wibou's combat presentation](../characters/wibou.md#combat-presentation--2026-10-16).
Before the other characters, the Wibou builder was split into a reusable
`PresentationBuilderKit`, with the workflow in the
[character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md) skill and the
review tooling in `tools/presentation_review.py`. FightGuy followed on 2026-10-17 with
"The Champ's Show" (see [FightGuy](../characters/fightguy.md#combat-presentation--2026-10-17)),
replacing the FightGuy ideas in section 3 below. Manki followed the same day with
"Controlled Chaos" (see [Manki](../characters/manki.md#combat-presentation--2026-10-17)):
concepts approved, built and captured; the in-game check is pending. It replaces the Manki
ideas in section 3 below. Bonk followed with "BONK!" (see
[Bonk](../characters/bonk.md#combat-presentation--2026-10-17)): a bucket-red identity, a comic
BONK! on his big hits, the armour read from section 2 and a "?" when armour shrugs off a hit.
Concepts were approved and it is built and captured; the in-game check is pending.
Everything is presentation-only and follows accepted hits
and semantic events, like `CombatFeedback`. The one item that needs a Shared
contract change is listed under [open decisions](#open-decisions) and needs its
own options write-up and approval first.

## Owner direction (2026-10-16)

- **Wanted for all characters:** victim shake during Hitstop, hit flash,
  squash/stretch/lean, limb smear, afterimages, a launch trail that follows the
  victim, and split hit sounds.
- **Not by default:** camera shake and impact frames ("too much anime"). They are
  allowed only on a short, explicit list of moves; see [section 6](#6-shake-and-impact-frames).
- Rework every character's VFX, **Wibou first**.
- Audio is a priority.
- Test procedural deformation for a more cartoony look **without editing `.anim`
  clips**.

An interactive mock of the universal layers was reviewed in the planning
conversation; it is not kept in the repository.

## Current state (at 56023b61)

- **Hit feel:** `PlayerRenderer` pauses the clip during Hitstop. `CombatFeedback`
  spawns the tiered `GraphicHitEffect`, plays tiered impact sounds and an 80 ms
  rumble. There is no hit flash, victim shake or procedural deformation.
- **Camera shake:** nothing reads the Settings **Screen Shake** slider. Only
  `MankiRoundBombExplosion.prefab` actually shook `Camera.main`, through CFXR's
  built-in shake with no distance falloff. The local-only KO burst
  (`Resources/VFX/SlopArena/CFXR4 Wave Explosion Purple.prefab`) has a shake
  configured, but `MovementFeedbackEffect` disables `CFXR_Effect` when it spawns
  movement effects, so that shake never ran. The owner chose to turn both off.
  `CFXR_Effect.GlobalDisableCameraShake` can gate any future CFXR shake.
- **Special hit presentation:** FightGuy uses `presentation.fightguy.empowered.hit`
  on every normal and special, and most of Bonk's specials reuse `steel.hit`, so a
  finisher lands like a jab. Wibou has no presentation IDs at all.
- **Audio:** each fighter has one 2D `AudioSource` (a whoosh on each hitbox
  trigger, plus jump), and `CombatSFX` plays the hit tiers. There is no pitch or
  volume variation. Only six Bonk presentation prefabs carry an `AudioSource`.
  Manki's explosions, jetpack and flamethrower and FightGuy's Ki Shot charge make
  no sound of their own.
- **Armor:** Bonk's `armorWindow` has no client presentation.
- **Distortion:** URP **Opaque Texture** is off in every pipeline asset, so the
  `CFXR ScreenDistortion` prefabs will not render correctly until it is enabled.

## 1. Universal hit feel

These are client-only, apply to all characters, and are driven by the hits
`CombatFeedback` already receives, so prediction and rollback behave like the
existing hit sparks.

- **Victim shake:** offset the model child along the knockback while
  `HitstopTicks > 0`. Amplitude scales by tier and decays across the freeze. The
  simulated position never moves.
- **Hit flash:** two white frames scaled by tier, then a short rim pulse in the
  attacker's color, using `_FlashColor`/`_FlashAmount` and the rim properties on
  `GraphicCharacter.shader` through a `MaterialPropertyBlock`. A flat tint toward
  the attacker's color was tried in review and flattened the fighter into one
  color. Under Reduced Flashing, use the rim pulse only.
- **Squash, stretch and lean:** drive a pivot transform above the model. The
  attacker leans back on startup and lunges into the strike. The victim crushes
  and recoils along the hit, springs back, and stretches along its launch.
- **Limb smear:** a vertex-shader stretch around the striking bone during active
  frames. The bone IDs are already in the `emitPresentation` operations.
- **Afterimages:** pooled `SkinnedMeshRenderer.BakeMesh` snapshots on lunges,
  dashes and launches.
- **Launch trail:** smoke puffs and speed lines that follow the victim and thicken
  with launch speed. Today there is only a one-shot `MovementWindTrails` burst
  where the launch starts.
- **Split hit sound:** play the contact sound at the hit, then a whoosh when
  Hitstop releases on heavy and launch hits. Vary pitch by about ±5% and volume
  slightly.

## 2. Beats every special should have

Normals stay restrained. Every special gets:

1. **Startup tell on tick 0:** a glint on the body and a short sting sound, plus an
   optional voice bark. F specials also get a presentation-only "super flash":
   everything except the user dims for about 10 frames while the aura appears.
2. **Charge steps:** at Ki Shot's and Dash Slash's tier thresholds, show a visible
   pop and step up the pitch of a charge hum. Opponents can read the charge level
   too.
3. **Armor read:** a steel rim pulse while Bonk is armored, and a clank with
   sparks when armor absorbs a hit.
4. **Finisher payoff:** in multi-hit moves, raise the pitch with each hit and give
   the last hit the strongest layer. This needs the finisher identification in
   [open decisions](#open-decisions).
5. **Stage marks:** scorch marks, cracks and lingering smoke that fade over 3–5
   seconds.
6. **Comic text pops:** CFXR `_BOOM_`, `_POW_`, `_SMASH_` and `_SLASH_` already
   use the countdown's particle-text system. Use them only on F finishers, plus a
   custom "BONK!" for Bonk's headbutt.

## 3. Character ideas

### Wibou (ironic mall ninja, #91FF69) — first

- **Identity (owner, 2026-10-17):** an ironic mall-ninja weeaboo, a cringe Neo from *The
  Matrix*. The kitsune and foxfire concept in older documents is retired.
- **Motif:** green "hacker" pixels and Matrix code rain, flickering in choppy,
  low-frame-rate steps; the sword trail is made of pixels.
- **Normals:** a glint at the blade tip before active frames and cut accents,
  following Bonk's pattern. Today Wibou's normals have only the sword trail.
- **Shuriken Toss:** a "shing" and glint on release, flat spinning additive discs
  instead of a modeled spin, a "thunk" on hit, and sparks on block.
- **Rising Slash:** a vertical crescent arc with code rain trailing the rise.
- **Dash Slash:** a slash that lands after Wibou passes through. While charging,
  show a sheath glint and tier pings. During the dash, leave a thin line and
  afterimages. Then near-silence, a sheath click, and the slash lines burst along
  the path. The engine already times the finisher with `finisherLeadTicks`.
- **Skyfall:** vertical speed lines while Wibou follows, a sphere of crescent cuts
  around the airborne victim, and a crater where they land.

### FightGuy (superseded on 2026-10-17 by the Champ's Show)

- **Ki Shot:** particles pulled into the hands while charging, a ring and pitch
  step at each tier, a recoil lean on release, and a distortion ring on a tier-3
  release (needs Opaque Texture).
- **Rising Dragon:** a ground crack and dust ring at takeoff, a ki spiral up the
  body (the Cyclone Kick ribbon assets may be reusable), and afterimages along
  the rise.
- **Cyclone Kick:** a wind ring at the hips, dust kicked outward, and a whoosh
  that wobbles with each rotation.
- **Fist of Fury:** several translucent ghost fists per frame, pitch that climbs
  with each punch, speed lines during the drag-in, and the strongest layer plus a
  shout on the kick finisher.
- Give specials their own hit presentation instead of `empowered.hit`.

### Manki (superseded on 2026-10-17 by Controlled Chaos)

- **Explosions first:** a flash, a fireball, a lingering smoke column, debris, a
  ground ring, a scorch mark, and a boom that gets muffled with distance.
- **Round Bomb:** a fuse sizzle, a blink that speeds up near detonation, and a
  squash plus "boing" on each bounce.
- **Bazooka:** an oversized backblast puff behind Manki and a corkscrew smoke
  trail.
- **Jetpack Boost:** a sputtering exhaust loop that coughs smoke rings at the
  apex, which also signals the drift phase.
- **Aerosol Inferno:** a can rattle, then a "psshht" into a roaring flame loop,
  heat haze, and a brief soot tint on victims.

### Bonk (superseded on 2026-10-17 by BONK!)

- The armor read from section 2, a "BONK!" pop on the Shoulder Bash headbutt, a
  Jump Slam crack that lingers, and a fan of sword afterimages in Blade Storm. The built
  pass keeps the first three; Blade Storm got orbiting smears instead of afterimages.

## 4. Audio

- Vary pitch and volume on every one-shot.
- Put an `AudioSource` on presentation prefabs, as Bonk does. Authoritative
  events spawn them, so the timing is already correct.
- Add signature loops for anything with duration: charge hum, jetpack,
  flamethrower and Blade Storm wind.
- Add voice barks on specials.
- Pan lightly by screen position. Every combat source is 2D today, so a hit in a
  four-player pile can't be placed by ear.
- Duck the music under heavy hits and KOs with the AudioMixer's Duck Volume
  effect.
- Candidate sources are the Sonniss GDC bundles (royalty-free) and Kenney (CC0).
  Record provenance and license per the
  [asset conventions](../contributing/conventions.md).

## 5. Deformation tests

All of these are procedural and run in `LateUpdate` after Animancer, like
`BoneChainSpring`. Don't use clip curves: Humanoid ignores curves on bones
between two mapped bones. Hurtboxes come from baked poses, so this layer never
changes gameplay.

- **Squash, stretch and lean pivot:** shared with section 1.
- **Directional flinch:** an additive spring rotation on the spine, neck and head
  away from the hit, layered over the three hit clips.
- **Impact dent:** the vertex shader pushes vertices near the contact point
  inward, then wobbles them back over about 150 ms.
- **Limb stretch on heavy attacks:** stretch the forearm or shin during active
  frames, but never past the real hitbox tip.
- **Hit impulses:** add an impulse to `BoneChainSpring` on hits and landings.

Review each test as a grid of close-ups at several amplitudes. At normal gameplay
camera distance, a before/after comparison is too subtle to judge.

## 6. Shake and impact frames

These stay off by default. If any are kept, limit them to:

- Wibou's Skyfall spike (16 damage at −90°, the hardest single hit in the roster)
- FightGuy's Fist of Fury finisher kick
- Bonk's Blade Storm finisher
- Manki's Bazooka explosion and Round Bomb explosion
- The KO burst

Also wire up the Screen Shake slider: at 0 it sets
`CFXR_Effect.GlobalDisableCameraShake`, and above 0 it scales the strength of any
custom shake.

## Order

1. Universal hit feel and the deformation tests (client-only). Done.
2. Wibou's VFX and audio, end to end. Done; reusable builder, skill and review tools
   extracted afterwards.
3. FightGuy's VFX. Done; voice lines are deferred.
4. Audio foundation for the roster: variation, Manki's explosions and startup
   stings. Manki's explosions, jetpack, bazooka and spray can have their own clips.
5. Manki and Bonk (both built 2026-10-17, in-game checks pending).

## Open decisions

- Which moves in [section 6](#6-shake-and-impact-frames), if any, get shake or
  impact frames. The existing Round Bomb shake was turned off on 2026-10-16.
- **Finisher identification:** hit presentation is set per Slot
  (`hitPresentationId`). A per-hit finisher payoff needs either a per-hitbox hit
  presentation or a finisher flag on the hit event. That is a Shared/compiler
  contract change, so it needs an options write-up and approval first.
- Whether enabling URP Opaque Texture for distortion effects is worth its cost.
- **Capsule hit contacts:** `SpellResolver.CapsuleCollision` puts the contact on the hitbox
  surface toward the target, so a victim deep inside a large capsule (Manki's tier-5 Aerosol
  Inferno, radius 1.05 m) gets its hit effect up to a metre away. Clamping the offset to the
  axis-to-target distance would fix every capsule; it is a Shared change, so it needs
  approval first.
- **Dash Slash charge pings:** FightGuy's Ki Shot now shows tier pings with the client-only
  `ChargeTierCues`, which reads the replicated charge. Wibou's Dash Slash can reuse it
  without a Shared change.
- Later: once Replay v1 is implemented, a slow-motion replay of the match-ending
  hit.
