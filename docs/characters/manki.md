---
id: "manki"
name: "Manki"
title: "The Mad Bomber Monkey"
status: "Package-native cooked character"
archetype: "Flying bomber. Take off, drop bombs, fire homing rockets and commit to Kamikaze."
source_image: "manki_action.png"
palette:
  fur: "#D84315"
  face: "#E53935"
  overalls: "#30363A"
  accents: "#9B9D9E"
  horns: "#1A1A1A"
kit:
  - slot: "LMB"
    name: "Monkey Combo"
    type: "melee"
    description: "3 hits: punch -> kick -> fire uppercut. Launcher on final hit."
  - slot: "Air LMB"
    name: "Air Kick"
    type: "melee"
    description: "2-hit air kick combo. First kick lunges toward target, second kick has higher knockback."
  - slot: "RMB"
    name: "Aerosol + Lighter"
    type: "charge"
    description: "Shake aerosol (hold) -> cone flame (release). tap = quick burst (8 dmg), hold >45 ticks = charged version (14 dmg)."
  - slot: "Air RMB"
    name: "Knuckle Spike"
    type: "melee"
    description: "Slow windup (16 tick startup) downward spike punch. High knockback, spikes opponents straight down."
  - slot: "Q"
    name: "Proximity Mines"
    type: "projectile"
    description: "Three charges. Set a mine at your feet or drop one straight down in air. Arms after 30 ticks, triggers within 2 m, warns for 12 ticks and blasts within 4 m. Never hits Manki. Three live mines maximum."
  - slot: "E"
    name: "Jetpack Flight"
    type: "recovery"
    description: "Three-tick ignition, then 180 fuel ticks at 12 m/s. Stick steers, neutral holds heading and jump bursts lift. Q/R keep flight. Normal, hit, E again or empty fuel ends it. Refuel on landing; 600-tick cooldown from exit."
  - slot: "R"
    name: "Bazooka"
    type: "artillery"
    description: "Capture a target and face it during a 40-tick charge, then fire a homing rocket. Flying retains its movement heading. A hit cancels charge. Existing homing and 4 self-damage remain."
  - slot: "F"
    name: "Kamikaze"
    type: "finisher"
    description: "Capture the target, brace for 16 vulnerable ticks, then travel at 18 m/s for up to 54 ticks into a 24-damage blast. Ground startup is rooted; airborne startup falls normally. Without a target run along facing. Self-push has no damage or stun."
---

# Manki — Mad Bomber Monkey

> **Three normals (2026-10-26, [ADR-0042](../adr/0042-controller-layout-three-normals-homing-dash.md)):** Monkey Punch and Air Kick stay on normal 1; Straight Punch and Air Swing (normal 2) are retired. Normal-2 rows below are history.

> **Identity (owner, 2026-10-17):** a chaotic character who fights with gadgets and
> explosions. He is also everyone's favourite: everybody the owner knows loves him,
> because his is the most unique design in the roster. His effects should show off the
> chaos without hiding the design people love.

> Status: Package-native authoring source cooked and rostered. The canonical kit contract is the package-native 14-entry grid: grounded and aerial variants of `1 / 3 / 4 / A / E / R / F`. `LMB` and `RMB` are camera controls, not persisted move identities.
> Package source: `client/Unity/Assets/CharacterPackages/manki/`; cooked runtime: `content-cooked/manki/`.
> Inspired by: Ziggs (LoL) × rushdown brawler — pyromaniac mad inventor monkey

## Combat presentation, 2026-10-31

The approved follow-up keeps Manki chaotic with short, separate effects and visible gaps.
The existing builders generate the assets. The animation authoring task remains separate.

| Move | Presentation and timing |
| --- | --- |
| Q, Proximity Mines | The 1.2 m Masked Classic body has dark unarmed lenses, a lit-fuse armed wink, then wide pale trigger eyes and a tongue. Falling mines use the unarmed variant. Phase variants come from the predicted mine each update. Lamps stay steady, including ReducedFlashing. The existing trigger beep remains. |
| Q, floor | A still scorched seam with sparse raised tabs follows the saved 4 m blast radius. Its interior stays open. Straight-drop projection follows the floor and hides the mark over pits. No pulse or chase. |
| R, charged lock | Open corner brackets close over the existing 40-tick charge. A small iris sits at the muzzle for the charge; the bazooka prop stays visible through charge and cast. Shared faces the captured target each tick while Flying keeps its separate movement heading. The current `anim.manki.gr` remains a placeholder. |
| F, startup | Sixteen vulnerable ticks precede powered motion and contact sweeps. Ground startup is rooted; airborne startup falls normally. A small cough and pack announcement precede launch ignition. The existing hunch clip stretches across the 71-tick stage pending the separate animation task. |
| F, travel | Two short banded closed flame shapes start at the authoritative launch. Measured nozzle anchors replace the old comet offset. Each jet has at most four hot chips and no smoke ribbon. Travel remains 18 m/s with its full 54-tick allowance. |
| F, blast | Seven closed soot clods surround an open centre. A small curved wink, crooked teeth and tongue face the viewing camera. Clods separate after six ticks and clear by sixteen; chips clear by twenty-four. Sparse tongues imply the unchanged 4 m blast reach. Damage, push and Hitstop retain their existing values. |

World Snapshot v23 saves Kamikaze's Startup/Travel phase. The simulation protocol remains 32 with an Unreleased note.
The generated Grin Mushroom material, texture and obsolete fuse mesh are retired.
New shapes use alpha blending and no screen shake. Existing sounds are reused.

Build and capture evidence is recorded in `tmp/manki-vfx2/build/REPORT.md`.
Final in-game visual acceptance remains with the owner.

## Concept

A pyromaniac/inventor macaque monkey. Always tinkering with explosives — bombs, dynamite, aerosols. Wears overalls stained with gunpowder and hole-ridden work gloves.

## Archetype

Manki takes off, makes a bombing run with Q, fires R in flight and commits to F for a kill threat.
The visible fuel bar and 210-tick cooldown after flight give opponents a grounded punish window.
Any accepted hit knocks him out of Flying. Normals end flight and use their air variant.

Current specials follow [ADR-0049](../adr/0049-manki-flying-bomber.md). Earlier dated design notes
below describe prior iterations. Their tuning does not override the current kit rows.

### Local gamefeel tuning — 2026-10-03

This source-only Editor pass supersedes the September movement values:

- Run 11.5 m/s; air cap 9 m/s; air acceleration 20 + 4 m/s².
- Jump/short-hop impulses 10.4/6.6 m/s; gravity 24 m/s².
- Fall/fast-fall caps 11/18 m/s; squat, jumps and air-dodge tuning unchanged.

The October 3 adjustment partially rolls back excessive floatiness: gravity
18 → 24 and fall/fast-fall caps 9/15 → 11/18, retaining jump impulses so both
height and airtime decrease without another impulse reduction.

Source-compiled Shared measurements: full hop **50 airborne ticks / 2.167 m**
(previously 67 / 2.918); short hop **31 airborne ticks / 0.853 m** (previously
42 / 1.155). Fresh descending Down reaches **-18 m/s**, with full/short-hop
airtime **33/19 ticks**. Both hops take off and land. The initial 6.3 m/s
short-hop candidate snapped back to the floor; the approved 6.6 is retained.

Normals use approximately 1.15× previous durations with matched contact windows,
IASA and auto-cancel thresholds. Landing lag, hit grouping, geometry and reward
are unchanged. Grounded recovery has at least four inactive ticks before another
ability, eight for ground.4. Special timings and the F hold/fire/alias path are untouched.

| Normal | Duration | IASA | Hit trigger / active length |
|---|---:|---:|---|
| ground.1 | 35 | 18 | 9 / 6 |
| ground.2 | 58 | 26 | 9 / 6 |
| ground.3 | 44 | 29 | 9 / 12 |
| ground.4 | 69 | 64 | 12 / 8 |
| air.1 | 38 | 33 | 7 / 6; 18 / 6 |
| air.2 | 48 | 41 | 8 / 6; 14 / 23 |
| air.3 | 51 | 48 | 16 / 7 |
| air.4 | 62 | 57 | 23 / 8 |

All entries are authored 60-Hz ticks. Shared lifetime/IASA probes passed; cooked
content remains unchanged. Live Lab/Training alignment and human feel are not
yet accepted; the retained Editor owner controls that verification window.

## Palette

| Element | Color |
|---------|-------|
| Fur | Burnt orange #D84315 |
| Face/butt | Bright red #E53935 |
| Vest/shorts | Charcoal #30363A |
| Work gloves | Soot brown #4A4338 |
| Bracers | Worn steel #9B9D9E |
| Horns/claws | Soot black #1A1A1A |

### Palette implementation — 2026-10-11

The catalog uses the owned `Presentation/MankiVisualRig.prefab`: existing vest
and shorts regions are charcoal, gloves are dark leather, and the bracer plates
use worn steel. Orange fur and the red face retain their original texture
treatment. This replaces the rejected blue/yellow candidate; the bright accent
comes from the gadgets' fire. The owned `Presentation/Readability/` assets are
`MankiReadabilityBody.asset`, `MankiClothCharcoal.mat`, `MankiWorkGloves.mat` and
`MankiWornMetal.mat`. The two renamed materials retain their GUIDs.

The body keeps **14,029 vertices / 11,590 triangles**, its skeleton, Avatar,
weights and UVs. Renderer count stays **10**; material slots rise **10 → 12**
through two additional body submeshes. Cooked `poses.bin` is unchanged.
The original Art prefab's mesh/material bindings are restored.

### Model — Manki v5 "pyro" (2026-10-28)

Manki v5 replaced v4 in the same files, rig prefab and GUIDs. The owner picked concept D3 "pyro" plus D1's spray-can
jetpack from a Codex concept round (`artifacts/manki-v5-20261028/source/`, local): toothy grin with missing teeth, a
burnt brow, cracked goggles, a lighter on the vest, a holstered TORCH can on his left thigh, a scorched glove on his
right hand and a two-can jetpack. Tripo built the mesh; the owner rigged it in Mixamo.

- **Rebuild:** README steps 0 (`mixamo_package.py`), 1 with `rigid_parts/manki_v5.json` (also rebuilds the finger chains), 2 at scale 1.3834
  (humanScale 0.6194), no face paint, then 4.
- **Worn props:** the jetpack and holster are their own mesh islands. Mixamo skinned the tanks to the arms, so
  `fix_rig.py` pins the jetpack to Spine2 and the holster to LeftUpLeg, and removes a loose shard beside the tail club.
- **Tail:** a shaft island plus club pieces at its tip; all ride the 5-bone chain.
- **Fingers:** Mixamo rigged only the index chains although the mesh has a thumb and three fingers.
  `finger_chains.py` (fix_rig step 5c) rebuilds thumb/index/middle/ring from the three fingertip islands and the
  glove's thumb, and `unity_import.eval.cs` maps missing Mixamo finger bones by name: all 46 bones map.
- **Open:** the thumb stretches a little glove when it curls; the missing-teeth gaps read as brown smudges.
- **Checks:** verify `valid`, full Shared tests pass, Ability Lab captures of ground.1, ground.E, air.3 and ground.4
  keep the props rigid.
- **Rollback to v4:** v4 is committed (7e132082): restore its Manki2 FBX, `.meta`, colour map, `MankiRig.prefab`
  and Manki cooks from Git.

### Model — Manki v4 (2026-10-27, replaced by v5)

Manki v4 replaced v3 in the same files, rig prefab and GUIDs as below. It is a Tripo model
the owner rigged in Mixamo (`newmanki2.fbx`, source and its 4K map in
`artifacts/manki-v4-20261027/source/`, local). Next to v3 it has symmetric open eyes with
no wink, a cream muzzle and chest, smoother painted fur, bigger ears and crest, and
fingerless gloves. It also drops the black leg diamonds and has a paler orange.

- **Rebuild:** the [rig README](../../tools/blender/manki_rig/README.md) steps 1, 2 and 4.
  The Mixamo download already had a rig, so step 0 was skipped. Step 3 (face paint) was
  skipped too: the face is clean. The 2048² colour map is a plain downscale of the 4K map.
- **Source quirks** (handled in `fix_rig.py`): a centimetre, quad mesh bound to a rest pose
  lying along −Y, where only the Mixamo clip stands it up; and a tail that is its own mesh
  island, skinned to the left thigh.
- **Size:** export scale 1.3486 gives humanScale 0.6194, as for v3. That makes him about
  1.35 m to the top of the crest. Foot contact in Training was not measured.
- **Tail:** 0.71 m chain, longer than v3's. In the T-pose its club hangs at floor level.
- **Checks:** `sloparena.character.verify` reports `valid`, and the full Shared tests
  pass on the new cook. Ability Lab captures of ground.1, air.3 and ground.4 deform cleanly.
- **Rollback to v3:** the live v3 files (FBX, colour map, material, `MankiRig.prefab`) are
  in `artifacts/manki-v4-20261027/unity-backup/`. Put the FBX and colour map where
  `unity_import.eval.cs` expects them (its `Src` paths) and run it.

### Model — Manki v3 (2026-10-16, replaced by v4)

The catalog rig is `Presentation/MankiRig.prefab`, rebuilt with
[`tools/blender/manki_rig`](../../tools/blender/manki_rig/README.md) from a re-generated
Manki (v3) rigged in Mixamo. It replaced Manki 2 the same day: same design, cleaner
paint, a 4K source texture and smooth shading. The local art keeps the Manki 2 file names
so the prefab, material and catalog GUIDs are unchanged:
`Art/Characters/manki/{Models/Manki2.fbx, Textures/Manki2_BaseColor.png, Materials/Manki2_Body.mat}`.

- **Size:** export scale 1.24873, chosen so the Humanoid scale (0.6194) matches Manki 2.
  Feet then stay grounded with the unchanged `modelYOffset`/`hipHeight` (lowest vertex
  0.7 cm below the floor in Training idle; 2.9 cm at full height). v3 has longer legs, so
  matching that scale makes him about 4% shorter: 1.26 m, head 0.82 m above the sole in
  idle (Manki 2: 0.85 m).
- **Hurtboxes:** follow the new skeleton. In idle, the hips sit 0.512 m above the sole
  (`hipHeight` is 0.5) and the head is 3 cm lower than Manki 2's. Across all cooked
  clips, bone positions moved 4.6 cm on average (p95 10.6 cm). `character.json` is
  unchanged. Full Shared tests pass on the new cook.
- **Fingers, deformation, tail:** as Manki 2: Humanoid-mapped thumb/index/middle/ring,
  skinned finger copies with constraints under the fist controls, and a five-bone tail
  driven by the presentation-only `BoneChainSpring`.
- **Surface:** fur tufts get body-like normals, which stop them reading as dark speckled
  tips under the near-vertical key light. The face is repainted on a 2048² colour map
  (from v3's 4K map): the mask and skin are cleaned, the wink gets a skin-toned lid with
  a lash line, and the mouth is redrawn. v3's painted brows and clean snout are kept.
  See the rig README for why.
- **Backups and rollback:** the Manki 2 assets that were live before v3 are in
  `artifacts/manki2-polish-20261016/unity-backup/` (local); a Manki 2 rebuild with the
  same fur and face fixes is in `artifacts/manki2-polish-20261016/staging/export/`. To
  restore either, copy its FBX to `/tmp/manki-rig-export/Manki2.fbx` and its colour map
  to `/tmp/manki-rig-export/textures/Manki2_BaseColor.png`, then run
  `unity_import.eval.cs`, which regenerates the Avatar and constraints. The older rigs
  (`MankiDeformRig`, `MankiVisualRig`) remain in `Presentation/Legacy/`: point the
  catalog `_rig` at `Legacy/MankiDeformRig.prefab` to use them.

### Run foot contact — 2026-10-17

The run floated 5–17 cm above the floor (median 12 cm): its root Y is baked "based upon
centre of mass at start", and the run's first frame is crouched. Clip `run` in the
ignored `Art/Characters/manki/Animations/run.fbx` now has a root Y offset of 0.084
(import setting), so its lowest contact touches the floor (median 7 cm through the
stride). Poses are unchanged; the cook only records the new clip. Still open: specials
sink 6–10 cm (`ga`, `gr`, `gf`) and the crouch 11 cm, which also sets its hurtboxes;
see the [grounding tool](../../tools/character_sizes/README.md).

## Kit

| Slot | Name | Visual | Mechanic |
|------|------|--------|----------|
| **LMB** | Monkey Combo | 3 hits: punch → kick → fire uppercut | Melee rushdown, launcher on final hit |
| **RMB** | Aerosol + Lighter | Shake aerosol (hold) → cone flame (release) | Two-phase charge: tap = quick burst (8 dmg), hold >45 ticks = charged (14 dmg) |
| **Air LMB** | Air Kick (2 hits) | Two air kicks | 2-hit combo, first kick lunges, second has higher KB |
| **Air RMB** | Knuckle Spike | Double knuckle punch down (hold to charge) | Two-phase charge: tap = quick spike (10 dmg), hold >45 ticks = charged spike (14 dmg). Downward, high KB |
| **Q** | Bombs Away | projectile | Three charges. Flying drops a contact bomb straight down. Otherwise hop backward and leave a one-second contact bomb. Opponent-only damage and zero-damage self-push. |
| **E** | Jetpack Flight | recovery | Three-tick ignition, then 180 fuel ticks at 12 m/s. Stick steers, neutral holds heading and jump bursts lift. Q/R keep flight. Normal, hit, E again or empty fuel ends it. Refuel on landing; 600-tick cooldown from exit. |
| **R** | Bazooka | artillery | Existing press-to-fire homing rocket, usable during flight. Tracking breaks on an invalid target or a pass behind it. Existing 4 self-damage remains. |
| **F** | Kamikaze | finisher | Lock onto the current target and fly into a 24-damage, strong-knockback blast. From flight it dives. Without a target run along facing. Self-push has no damage or stun. |

## Explosions and bomb aim — 2026-10-18

Approved with Manki's [Kaboom Quarry](../design/stages/manki_quarry.design.md) course:

- **Every blast reaches everything in range.** Round Bomb and Bazooka explosions hit each
  fighter, ball or course target inside their radius once, instead of stopping at the first
  entity. A close Bazooka impact can hit both Manki and an opponent beside him.
- **The bomb lands where the cursor points.** The ground cursor follows the stage's own
  collision, so it sits on raised islands, roofs and platforms. The server throw is
  unchanged (fixed 30° lob, 12 m reach at foot height); the client sends the distance whose
  arc passes through the cursor point, so a raised target costs reach (about 8.5 m for an
  island 2.4 m up) and a lower one gains it.

## Local cooldown tuning — 2026-10-03

Round Bomb's ground and aliased air cooldown is **240 ticks / 4 s**, reduced
from 300 / 5 s. The explosive aimed option remains costlier than a plain
two-second projectile without changing its reward, aim hold or release timing.
E/R/F remain **210 / 240 / 600 ticks** (3.5 / 4 / 10 s); normals remain zero.
These are playtest tuning values, not a match-balance verdict. Cooldown begins
on completion/cancellation, so time spent aiming is separate. Source-compiled
Shared ground/air timer and expiry probes passed; persisted cooked content and
roster pins are unchanged.

## Previous design notes
- **E**: Jetpack Boost. On activation, Manki compresses for 3 vulnerable ticks while preserving vertical fall velocity and clearing horizontal velocity. On ignition, the authoritative simulation samples the current movement stick, normalizes it when above 0.001 magnitude, and launches at `VY=15` with horizontal speed `3.5` along that direction. A single owner-centered sphere (radius 1.25, damage 4, 75° angle, base 2, growth 8, stun 8, duration 4) resolves the ignition hit. The ascent is unsteerable; gravity is active immediately, and normal air drift/actions return when `VY <= 0`. Cooldown: 210 ticks. The move is a recovery move and aliases `air.E` to `ground.E`.
- **R**: Bazooka. Press to fire, with no aim hold or camera change. The 20-tick cast spawns one rocket at tick 6, followed by 15 recovery ticks. At spawn it aims at the current soft or locked target's chest, including airborne fighters, balls, and course targets. Without a valid target it fires horizontally along facing. The rocket keeps its launch target and turns toward its current position by at most `homingTurnDegPerTick`, preserving speed before gravity. Tracking ends permanently if the target becomes invalid or passes behind the rocket, so sidesteps and air dodges can beat it. First-pass Training values are speed 30 m/s, turn 2.5 degrees per tick, lifetime 60 ticks, and gravity 15 m/s². The 3 m explosion still has `CanHitOwner=true` and deals 4 self-damage. Aiming at your feet for a rocket jump is no longer available. Cooldown stays 240 ticks.

- **F**: Aerosol Inferno. Hold enters the loop animation and freezes movement. An 18-tick minimum charge applies even to a tap; early release queues the shot, and a longer hold continues aiming until release. Manki follows camera yaw only; vertical aim is ignored. The firing phase starts the flame hitbox and VFX together at tick 8 and sustains both for 44 attack ticks; the move then recovers for 10 ticks (62-tick commitment, owner 2026-10-22, so he lowers the can before he can act). Charge tiers at 18/30/42/54/66 ticks select 10/14/18/22/26 damage and 0.6×/0.8×/1×/1.25×/1.5× flame size. Charge caps at 66 ticks but still waits for release. The facing-aligned capsule and flame grow together from 2.64 m to 6.6 m total length (0.84 m to 2.1 m diameter), anchored at the same near edge; the effect is independent of wrist rotation and ends on interruption. Cooldown: 600 ticks.

## Combat presentation — 2026-10-17

"Controlled Chaos", built for the identity above from concepts the owner approved on
2026-10-17. Toon blasts with chunky smoke that clears fast; gadget junk (bolts, nuts,
springs and gears) that spills out of every boom; and his winking, tongue-out grin rising in
the smoke of his two big booms, the way 肉 is FightGuy's brand. The gags are his gadgets
backfiring on him, never on the opponent. `sloparena.presentation.manki.build`
(`MankiPresentationBuilder.cs`, on the shared `PresentationBuilderKit`) generates every
texture, mesh, material and prefab. It binds them to the asset catalog and to the Round Bomb
and rocket projectile visuals. Only presentation cues, `presentationIds` and
`hitPresentationId` values in `character.json` changed; damage, hitboxes and timing did not.
The workflow is the [character VFX pass](../../skills/sloparena-character-vfx-pass/SKILL.md).

| Move | Presentation |
| --- | --- |
| Normals (ground and air 1–4) | An orange toon swoosh behind the striking limb and a puff of smoke. Hits use `normal.hit`: a soot-rimmed orange "pow" with sparks. Ground and air 4 use `normal.heavy-hit`, which adds a flash star and a nut flying off. |
| Bombs Away (A) | The generated Masked Classic bomb has a faceted black shell, red mask, open eye and wink, missing-tooth grin, curled fuse and crackling star. It faces the camera and follows resolver identity at the existing 1.2 m contact diameter. The orange ground warning is unchanged. Q retains its existing impact/fuse blast. |
| Jetpack Flight (E) | Ignition crossfades into the package-bound 48-tick `anim.manki.fly-loop`. The arms float out and the legs trail behind the pack, with two small thrust bobs per cycle. Q/R override the upper body while the loop continues underneath, then fade back to it. State-owned exhaust and the fuel bar last through Q/R and end with Flying. |
| Bazooka (R) | The accepted 75-tick GenRA gunner brace replaces the FBX take. The moving shoulder hold fires at clip tick 40 and settles through recovery. R retains the Flying upper-body mask. A glint and a clunk-click as he shoulders it (`bazooka.ready`, tick 0). The shot: a flash star and a short flame at the muzzle, and a comically oversized backblast out of the back end that singes his fur, which smokes for a moment (`bazooka.singe`, on his head). The rocket is a red toon rocket that flies point-first, with a wobbly smoke trail that lingers after impact. The explosion is the Round Bomb's, bigger, with the grin. Contacts use `blast.hit`. |
| Kamikaze (F) | The accepted combined 69-tick `anim.manki.gf-kamikaze-windup` plays over the 71-tick stage. Its sixteen-tick crouch, coughs and brace lead into the accepted dive. The startup announcement stays visible through the vulnerable startup, and twin closed toon jets ignite at tick 16. The rendered model pitches toward travel after startup. The accepted blast v2 has six small burning soot puffs, a separate masked grin, short fire tongues and chips. Puffs split by six ticks and clear by sixteen, with sparse chips ending by twenty-four. |

- **Ground 4 ribbon (2026-10-22):** Head Slam's existing heavy swoosh starts at tick 6,
  during the downward swing, with a head-local offset of `(0, 0.24, 0)` and a 12-tick
  lifetime. The old tick-10 cue at `(0, 0, 0.1)` started after most head movement and
  left its ribbon hidden in the model. The trail now follows the crown through the
  strike; the heavy contact burst, animation and gameplay data are unchanged.
- **Floor marks:** the blast rings, scorch and landing junk sit on the stage floor through
  `SnapToFloor`, which samples the heightmap `MatchBase` publishes as `PresentationFloor`.
  A blast with no floor within 3 m (1.4 m for the jetpack) leaves no ring or scorch. Without a
  stage (Ability Lab) they keep their authored offset.
- **Projectiles:** the rocket trail carries `LingerAfterProjectile`.
  `ProjectileVFXManager` detaches it before it destroys the projectile visual, so the smoke
  fades instead of vanishing on impact. It also turns the rocket point-first, as for Wibou's
  kunai. The bomb model is nested at its 80× import scale, so the projectile entry's scale is
  now 1 (it was 80 on the root).
- **Charge tiers:** `ChargeTierCues` handles up to five tiers. It plays an `AudioSource` on a
  tier's effect and scales its `Grow` transform per tier (the pilot flame). `ChargePropFeedback`
  drives the held can from the replicated charge (`ChargeTiers.Progress`) after `WeaponAttach`
  poses it, moves the cue onto the growing nozzle and restores the can on release.
- **Sounds and art:** the clips are CC0 Kenney sounds plus generated hiss, sizzle, sputter and
  whistle, tracked in `Resources/Audio/SFX/Manki/` with their `.meta` files (see
  [provenance](../assets/credits-and-licenses.md)). `tools/sfx/manki_clips.py` rebuilds them
  when a sound changes. The Masked Classic bomb mesh, face texture, fuse and
  Kamikaze mushroom texture are original procedural art generated by the builder
  (2026-10-29). They add no external model, sound or licence dependency.
- **Historical Aerosol Inferno issue (retired from the current kit):** its hit effect could appear up to a metre above a victim who stood deep
  inside a high-tier flame. Shared reports a capsule contact on the hitbox surface toward the
  target (`SpellResolver.CapsuleCollision`), which overshoots inside the 1 m-radius capsule.
  The effect it replaced had the same offset. A fix belongs in Shared and needs its own approval.

The generated prefabs replace the earlier hand-made and Cartoon FX versions in place, keeping
their GUIDs. The sections below up to the Head Slam describe those earlier versions.

## Jetpack presentation — 2026-10-11

> Superseded on 2026-10-17 by [Combat presentation](#combat-presentation--2026-10-17); kept as history.

The E visual pass keeps the current authored movement, ignition hitbox, cooldown,
animation and air alias. The original world-space ignition burst remains separate
from the attached exhaust.

`presentation.manki.jetpack-boost.exhaust` emits at authored attack tick **3**,
on the same simulation tick as ignition. Recorded Lab runs show that as
**MatchTick 3 / frame 2**; recorded frame numbers are not authored cue ticks.
The cue attaches to `bone.hips` at local position `(0, -0.15, -0.2)`, with
identity rotation and scale. Its authored duration is **57 ticks**, the remainder
of the 60-tick stage.

The owned `Presentation/MankiJetpackExhaust.prefab` uses a compact warm flame
directed down, embers and short smoke to sell propulsion. The existing ignition
prefab's root scale is **0.6**, a cosmetic adjustment to the separate burst.
The exhaust's `AttackBoundPresentation` marker
clears the exhaust when E ends at the apex, is interrupted or replaced, or loses
its owner; hitstop retains the remaining attack ticks. The 57-tick upper bound
does not extend movement or keep the effect running after control returns.

Native cook, package verification and roster refresh passed. Lab evidence is
under `.ability-lab-cache/manki-cooler-20261011/pass2/`, including
`front/manki_ground.1_tick-0000.png` and `e-right/manki_ground.E_tick-0020.png`.
Native Lab contact, shield and ground/air whiff runs all emitted ignition and
exhaust at MatchTick 3 without diagnostics. At 1.2 m, Idle accepted one
10-damage hit and Shield accepted a zero-damage block, both with nine ticks of
hitstop; 10 m ground/air runs missed. Inspected frame-20 hit/shield captures
retain the jet; air-whiff frame 45 shows it cleared after control returns.
These captures are under `.ability-lab-cache/manki-cooler-20261011/post-restart/`.

The earlier blue/yellow candidate was checked in live Training at **1920 × 1080**
on **Splash Deck** and **After Hours**, including E ascent at elapsed tick **23**.
Those captures are historical evidence under
`.ability-lab-cache/manki-cooler-20261011/arena/`; the muted palette above
supersedes that candidate.

The visual check temporarily disabled domain reload after the normal Play Mode
reload stalled. Exact Editor preferences were restored afterward; general Play
Mode/script reload instability remains unresolved. Multiplayer verification is
pending. The follow-up below extends the presentation pass to Q/R/F.

## Gadget presentation follow-up — 2026-10-11

> Superseded on 2026-10-17 by [Combat presentation](#combat-presentation--2026-10-17); kept as history.

All four specials use warm flame, embers and short soot smoke. Ground and air
aliases share the same presentation assets. Gameplay parameters and animation
bindings remain unchanged; only presentation cues, placement and assets change.

| Move | Presentation |
|------|--------------|
| Q / Round Bomb | `MankiLitBomb.prefab` retains the existing bomb mesh and scale, adding a small ember glow, fuse sparks and short smoke at the fuse tip. Both the held prop and moving bomb use this owned prefab. Existing contact explosions remain. |
| E / Jetpack Boost | Retains the downward jet, embers, smoke and separate ignition burst from the first pass. |
| R / Bazooka | `MankiBazookaMuzzle.prefab` emits at the existing tick-6 rocket trigger, with flame, sparks and smoke at the imported barrel tip. `MankiBazookaRocket.prefab` has a compact fiery core and world-space smoke/embers. The launcher remains visible through the firing motion. |
| F / Aerosol Inferno | `MankiAerosolInferno.prefab` uses a facing-aligned orange flame column, embers and smoke. Its full-volume ignition burst covers the capsule immediately, using the same charge-tier scale as the hitbox: 0.6× through 1.5× the 4.4 m by 1.4 m baseline. The world placement is relative to the firing event at `(0, 0.25, 0.55)`, independent of wrist rotation. Hitbox and VFX start on firing tick 8, last 44 attack ticks, and end on interruption. |

`MankiWeaponAttachments.asset` is an owned copy of the existing attachment
config. It preserves attachment positions and rotations, uses the lit bomb,
and changes the bazooka's cosmetic hide threshold from 5 to 20 ticks.
The shared projectile visual config changes only Manki's ground/air Q and R
prefab references. No projectile motion or collision logic changes.

R's new `presentation.manki.bazooka.muzzle` cue has an 18-tick upper bound.
Both the muzzle and F stream use `AttackBoundPresentation`, so interruption,
replacement or owner removal clears the attached effect. F's previous sample
effect script and light are removed; the owned effects need no sample scripts,
dynamic lights or physics components.

Native cooking, package verification and roster refresh passed. Focused Shared
coverage passed **10 tests**, including canonical source compilation, E behavior,
R rocket-jump behavior and F contact behavior. Lab captures are under
`.ability-lab-cache/manki-scrappy-20261011/`: `palette-front`, `palette-back`,
`final-r`, `final-f` and `pass1-e`. Recorded 20-tick holds show the R muzzle
at **MatchTick 27 / frame 26** and F at **MatchTick 39 / frame 38**, with
no presentation diagnostics. Cooked pose bytes remain unchanged.
Final F runs at 2.5 m accepted a 15-damage hit and a zero-damage shield block,
both at MatchTick 39. Air F and air R whiffs emitted their matching cues without
diagnostics. Frame-85 F captures show the stream cleared after the action.

Live Training captures at **1920 × 1080** checked the muted outfit on Splash
Deck and After Hours, Q's fuse before and after launch, R's muzzle and moving
rocket, F's flame, and E ascent. The initial F side-view candidate was too faint
from the gameplay camera; the final billboard/material revision is captured in
`.ability-lab-cache/manki-scrappy-20261011/arena-final/f-inferno.png`.
The held and thrown bomb each had live fuse sparks, glow and smoke; R had live
muzzle particles, a rocket core and world-space smoke/embers. F had 30 flame
particles at attack tick 28; E retained its exhaust at attack tick 23.

Verification temporarily disabled domain reload and restored the original
Editor preferences, Game View settings and clean Ability Lab scene. Saved
source hashes matched across both Training checks, the Editor settings file
was restored byte for byte, and the final console had no errors. These checks
completed without a reload hang; the general reload problem remains unresolved.
Verification receipts are in the planning workspace's
`_bmad-output/manki-scrappy-polish-20261011/`.

## Normal attack presentation — 2026-10-11

> Superseded on 2026-10-17 by [Combat presentation](#combat-presentation--2026-10-17); kept as history.

Ground and air normals now use short cream/gold swing marks and world-space
ribbons that follow the striking limb. Heavy attacks get a wider sweep; air
1 accents both hands and air 2 has a smaller follow-through at its sour window.
These are cosmetic cues at the existing hitbox triggers. Attack timings,
collision, damage, movement, rig and cooked pose bytes remain unchanged.

| Slot | Cue tick / attachment | Presentation |
|------|-----------------------|--------------|
| `ground.1` | 9 / right foot | Small sweep, matching the existing kick pose despite the Monkey Punch label |
| `ground.2` | 9 / right hand | Compact strike |
| `ground.3` | 9 / right foot | Kick sweep |
| `ground.4` | 12 / both feet | Heavy right-foot sweep and smaller left-foot accent |
| `air.1` | 7 / right hand; 18 / left hand | Two separate strike accents |
| `air.2` | 8 and 14 / left foot | Sweep followed by a smaller accent |
| `air.3` | 16 / right foot | High-kick sweep |
| `air.4` | 23 / right hand | Heavy swing |

The catalog owns `MankiNormalStrike.prefab`, `MankiNormalSweep.prefab` and
`MankiHeavySwing.prefab`, backed by a feathered arc mesh and
`MankiNormalSwing.mat`. Their `AttackBoundPresentation` marker clears attached
effects on interruption, attack replacement or owner removal. Presentation
windows are bounded to 8–14 ticks. Each prefab has two particle systems,
with no lights, colliders or sample-effect scripts.

All eight normals bind `presentation.manki.normal.hit` to the owned
`MankiNormalHit.prefab`, a compact warm variant of FightGuy's impact burst.
Shared confirmed-hit events place and scale it at contact. Whiffs emit only
the swing cue; shield contacts retain the existing `combat.block` effect.
The contact burst has its own lifetime and does not stay attached to a limb.

Native cook, verification and roster admission passed. Six focused Shared
tests passed: canonical compilation and five ground/air contact/recovery cases.
Lab checked every normal's whiff cue, all four grounded hits at 0.8 m, and a
shield block. Contact damage remains 4/7/7/14; hits occur at MatchTicks
9/9/10/12 respectively. The block deals zero damage and emits no normal-hit
burst. Presentation diagnostics and the final console are empty.

Live Training at 1920 × 1080 checked ground and air accents on Splash Deck,
then the grounded first normal on After Hours. That last run waits 30 input
ticks for grounding before attacking; the initial immediate-spawn slot-1
sample selected the air variant. Read-only runtime probes confirmed visible
particle systems and distinct world-space ribbon positions. After recovery,
the attached effects were gone. Lab captures cannot reconstruct moving ribbon
paths from a single scrubbed pose; the live check covers that behavior.

Captures are under `.ability-lab-cache/manki-normal-vfx-20261011/` in
`final-*`, `contact-*`, `arena` and `arena-final`. Detailed receipts are in
the planning workspace's `_bmad-output/manki-normal-vfx-20261011/`.
Both live batches kept all 260 saved source fingerprints unchanged and restored
Editor preferences byte for byte. The clean, stopped Ability Lab is left on
`ground.4`, frame 13. General Play Mode reload instability remains unresolved;
these cosmetic checks temporarily disabled domain reload and restored it.

## Ground 4: Head Slam — 2026-10-16

Ground `4` now uses a short backward load followed by a forward/downward head
slam, with planted feet, trailing fists and recovery to idle. Contact is at
tick 10, active ticks are 10–14, IASA is 35 and duration is 40. These values
supersede the October 3 ground `4` timing above. The head-attached sphere
replaces the foot capsule; damage 14 and the existing knockback/stun values
are preserved. Its heavy swing cue follows the head, replacing both foot cues.

The package-local animation uses the current Manki rig. Cook, roster admission,
close hit/shield and distant whiff checks passed, along with seven focused Shared
tests. See the [authoring and verification record](../research/2026-10-16-manki-ground4-head-slam.md)
for the GenRA conversion path, playback comparison and remaining human feel check.

## Aerosol Inferno redo — 2026-10-22

The owner wanted a cartoony, frenetic shake (the old hold loop only swayed the can) and a fire
where Manki holds the can and the lighter together in front of him, where the flame comes out,
without flailing arms. Both clips were authored with the GenRA toolkit from image-generated pose
sheets and kept as their key-pose blockings (owner: "the vfx and assets really sell the move here,
the animation can stay basic"):

- **Shake** (`anim.manki.gf-loop`, `Animations/manki_gf_shake_loop.anim`, 36-tick loop): a piston
  pump of the can on his right, one big pump and slam, then three quick strokes. He holds the red
  lighter upright in his left fist, out to his left, from the first shake.
- **Fire** (`anim.manki.gf-atk`, `Animations/manki_gf_fire.anim`, 62 ticks): on release he snaps the
  can upright in front of his face with the lighter at the nozzle (tick 3), recoils as it ignites
  (tick 8), braces through the stream with steady hands, and lowers both back to idle. The nozzle
  sits where the flame and its hitbox start, so the stream comes out of the can.
- **Timing:** the move went from 52 to 62 ticks, a 10-tick recovery after the flame; the flame and
  hitbox are unchanged (ticks 8–52). The lighter is a held slot prop (`MankiLighterHeld`), and its
  release cue burns until the stream ends.
- **Can smear:** `ChargePropFeedback` stretches the can along fast strokes and squashes it when a
  stroke stops (Humanoid clips cannot squash bones; the prop can).

Briefs: `tools/genra/briefs/manki-gf-shake-loop.json` and `manki-gf-fire.json`; evidence and the old
clips' backups in the local `artifacts/manki-f-shake-20261021/`.

## Animation Inventory

| Key | Animation | Maps to |
|-----|-----------|---------|
| `anim.manki.g1` | Grounded monkey punch | Ground `1` |
| `anim.manki.a1` | Air kick | Air `1` |
| `anim.manki.g2` | Straight punch | Ground `2` |
| `anim.manki.a2` | Air swing | Air `2` |
| `anim.manki.g3` | Sweeping kick | Ground `3` |
| `anim.manki.a3` | High kick | Air `3` |
| `anim.manki.g4` | Head Slam | Ground `4` |
| `anim.manki.a4` | Air smash | Air `4` |
| `anim.manki.ga` | Round bomb | Ground `A` |
| `anim.manki.ge` | Jetpack Boost | Ground `E` and Air `E` alias |
| `anim.manki.gr` | GenRA gunner brace (`manki_gr_gunner_brace.anim`), 75-tick one-shot | Ground `R` and Air `R` alias, upper-body layer in Flying |
| `anim.manki.gf-loop` | Aerosol Inferno hold loop: the can's piston shake (`manki_gf_shake_loop`) | Mayhem host via `presentation.mayhemLaugh` |
| `anim.manki.fly-loop` | GenRA flight cruise (`manki-fly-loop-v1.anim`), 48-tick loop | Flying, including beneath Q/R |
| `anim.manki.gf-kamikaze-windup` | GenRA windup plus accepted dive (`manki_gf_kamikaze_windup.anim`), 69-tick one-shot | Ground `F` and Air `F` alias over the 71-tick stage |
| `anim.manki.gf-kamikaze` | Original accepted 56-tick dive asset, unchanged | Retained source clip, no remaining active binding |
| `anim.manki.gf-atk` | Aerosol Inferno release: can and lighter in front (`manki_gf_fire`) | Retained asset, no current kit binding |

## Files
- `client/Unity/Assets/CharacterPackages/manki/package.json` — package identity and license
- `client/Unity/Assets/CharacterPackages/manki/character.json` — canonical authoring source and 14-slot projection
- `client/Unity/Assets/CharacterPackages/manki/CharacterAssetCatalog.asset` — Unity presentation bindings
- `client/Unity/Assets/Scripts/Editor/MankiPresentationBuilder.cs` — generates and binds everything under `Presentation/` except the rigs
- `content-cooked/manki/manifest.json` — cooked package manifest and hashes
- `content-cooked/manki/character.runtime.json` — normalized runtime definition
- `content-cooked/manki/poses.bin` — deterministic cooked pose data
- `content-cooked/manki/client.bindings` — generated client bindings

## Previous Design

This character was initially designed as a **Fire Dancer / rushdown acrobat** (pure fire, full melee). The kit was redesigned in June 2026 to reposition him as a **pyromaniac/mad inventor** with explosives, while keeping a rushdown base. See git history for the old kit version.

See `docs/characters/character-kit-design-principles.md` for the canonical kit contract and `docs/systems/combat-systems.md` for universal combat mechanics.
