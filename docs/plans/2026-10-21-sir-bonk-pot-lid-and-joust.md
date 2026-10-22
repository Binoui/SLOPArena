# Sir Bonk: Pot-Lid Throw and The Joust, engine options

**Status:** Approved 2026-10-21 with every recommended answer. The engine part is built and
tested, uncommitted ([below](#built-2026-10-21)). Sir Bonk's Q and R are not wired yet: they wait
for the hobby horse model, the animations and the effects pass.

The owner approved Sir Bonk's kit on 2026-10-21 ([Bonk: Sir Bonk kit](../characters/bonk.md#sir-bonk-kit--2026-10-21)).
E (Leap of Faith) and F (Blade Storm) stay as they are. Q and R need engine work:

- **Q, Pot-Lid Throw** (slot `A`, ground and air). The lid flies out, hits, comes back to him, hits
  again and he catches it. One lid: no second throw while it is out. If he is busy when it comes
  back, it rings off his bucket and he wobbles under a "?". It replaces Shoulder Bash.
- **R, The Joust** (slot `R`, ground and air). He rears and neighs, then gallops a long way, armoured
  and steerable, with the plank as a lance. The first fighter he meets is unhorsed and the gallop
  ends. A miss ends in a skid and a turn. In the air he gallops a few strides on thin air, looks
  down and drops. It replaces Charging Double Slash.

This page compares ways to build each move, what each costs in Shared, netcode and presentation,
and what only the owner can decide. It sets no timing or numbers. Code references are to the
current working tree. Items marked *uncommitted* come from the Sir Bonk E work, which is not
committed yet.

**In short:** build the lid as a returning projectile in the engine (Q, option B) and the gallop as
one new steerable-charge move type (R, option C). Both are public primitives other fighters can
use. They ship together in one coordinated client/GameHost/Master release. See
[Recommendations](#recommendations) and [Open questions](#open-questions).

## What the engine has today

Rules that frame both moves:

- A slot's timeline is fixed and linear. Stages run in order and authoring has no branches
  (`CookedTimelineAbility.cs:186-201`; [ability architecture](../systems/ability-architecture.md)
  line 72). A hit/miss branch can only live in a capability, as the leap's landing and the dash's
  stop do.
- Capabilities live inside the move and end with it (`CookedTimelineAbility.cs:465-475`). Only
  resolver projectiles outlive a move (`ServerSimulation.cs:213-221`, `SpellResolver.cs:163-180`).
- Every runtime value must be in the World Snapshot (ability architecture, step 6;
  [ADR-0039](../adr/0039-full-world-rollback.md)). It holds the fighters' states, the active move
  with its capabilities, and the resolver's hitboxes and projectiles
  (`ServerSimulation.Snapshot.cs:26-81`).
- New creator behaviour is an engine-owned, versioned primitive
  ([ADR-0024](../adr/0024-deterministic-creator-gameplay-primitives.md)). Bonk-only C# would be an
  exception that needs a reason, an owner and a migration path
  ([ADR-0022](../adr/0022-workshop-first-content-architecture.md) line 20). Bonk's E already went
  the other way, from `slop.internal.bonk.targeted-jump-slam.v1` to the public
  `slop.ability.targeted-leap.v1` (`CharacterPackageCompiler.cs:38-40`).

| Piece | What it does | Where |
|---|---|---|
| `spawnProjectile` | A sphere with a speed and optional gravity. On an `aimedProjectile` slot with `aimMode: none` it is aimed at the target at spawn (Wibou's shuriken). It dies at its first contact, keeps flying if the move is interrupted and ends at `maxFlightTicks`. No return, no homing. Without gravity and an explosion it ignores the stage. | `CookedTimelineAbility.cs:307-310`, `ServerAbility.cs:256-302`, `SpellResolver.cs:266-289, 365-516`, `wibou/character.json:933-1046`, [combat systems](../systems/combat-systems.md) lines 16-24 |
| `slop.ability.charged-projectile.v1` | Hold to charge, release one projectile (FightGuy's Ki Shot). | `ChargedProjectileAbility.cs`, `fightguy/character.json:1074-1130` |
| Manki's Round Bomb and Bazooka (`slop.internal.*`) | Lobbed shells with explosions. The bazooka blast can hit Manki (rocket jump) and corrects the damage in its hit hook. | `MankiRoundBomb.cs:69-102`, `MankiBazooka.cs:147-172` |
| `forwardLunge` | Pushes him along the facing he had when it started, for a fixed time. `stopInAttackRange` latches the push off once the next hitbox's cached poses would touch an opponent in front. That is proximity, not a hit, and the move plays on. | `CookedTimelineAbility.cs:344-412`, `ServerSimulation.cs:665-691` |
| `armorWindow` | A fixed window inside its stage. Grabs ignore it. | `ServerAbility.cs:40-73`, `CharacterPackageCompiler.cs:325-328`, combat systems lines 216-219 |
| `setAimState` with mobile aim and `aimMovementSpeedScale` | Walks him under stick control in any direction, at most at his run speed (Blade Storm). | `Simulation.cs:524-544`, `CookedTimelineAbility.cs:311-323`, `bonk/character.json:1137-1138` |
| `gravityWindow`, `setVelocity` | Scale airborne gravity from 0 to 1; set or add a world velocity. | `CookedTimelineAbility.cs:275-300`, `CharacterPackageCompiler.cs:504-505` |
| `slop.ability.charged-directional-dash.v1` | Hold, a direction locked at release, a dash that phases through fighters and stops on walls and shields, then a recovery phase. | `ChargedDirectionalDashAbility.cs` |
| `slop.ability.targeted-leap.v1` | Aim, flight, and at landing a switch to a second clip by raising `ComboStage` (*uncommitted*). | `TargetedLeapAbility.cs:128-151`, `ServerSimulation.cs:637-663` |
| Charge pool | A slot refuses presses while its charges are spent. | `ServerSimulation.cs:961-968` |

Bonk's package today has Shoulder Bash on `ground.A`/`air.A` (`bonk/character.json:678-820`,
`1727-1860`) and Charging Double Slash on `ground.R`/`air.R` (`898-1129`, `1936-2158`). Their air
versions add a slowed-fall `gravityWindow` (`1767-1773`, `1976-1982`).

One existing quirk matters for both moves. Hit, block and object hooks go to the owner's *current*
move, not to the move that spawned the hitbox (`ServerSimulation.cs:2191-2195, 2264-2271`;
`ServerSimulation.Arcade.cs:156-157, 226-227`). A lid hit or block during the Joust would reach the
Joust's hooks. It can already bite today: a Wibou shuriken blocked by a shield during his Dash
Slash stops the dash (`ChargedDirectionalDashAbility.cs:221-225`). The only other hook override I
found is `MankiBazooka`'s, for its own blast, so filtering hooks by activation should change nothing
else in the roster. This is called "the hook fix" below.

## Pot-Lid Throw (Q)

### What is missing

A projectile that outlives the throw, so he can act while it is out. It must turn back, home on
him, hit on both legs, be caught or bonk him depending on what he is doing, and lock Q while it is
out.

### Options

**A. Placeholder from today's operations (no engine work).** A `spawnProjectile` from his left
hand, auto-aimed. The cooldown stands in for "one lid" and a timed cue hides the shoulder. You get
the throw, one hit and the DOOONG. You don't get the return, the return hit, the catch or the
self-hit, and the shoulder reappears on a timer. Content only, no wire change. Useful only to try
the throw clip and its reach early.

**B. A returning projectile (recommended).** `spawnProjectile` gets an optional `return` block. The
lid stays an ordinary resolver projectile, so it outlives the throw, is already in the World
Snapshot and already has a keyed visual online. New engine parts:

- a resolver step that turns it back and steers it at its owner every tick, next to the bone-hitbox
  update that already reads the owners' states (`ServerSimulation.cs:2154`);
- one hit history per leg instead of one in all (`Hitbox.cs:97-102`), and it keeps flying after a
  hit;
- owner contact resolved by Shared as a catch or a self-hit ([rules](#rules-option-b));
- a slot gate in admission beside the charge pool (`ServerSimulation.cs:947-968`), also in the
  CPU's checks (`AI/HeuristicBotPolicy.cs:775-778`);
- removal when he is KO'd or leaves the match (`ServerSimulation.cs:2532`, `171-187`).

It is public and Workshop-admissible: any creator gets a boomerang. It is not a capability, because
capabilities end with the move and the hard part, a returning object in the world, would be the
same. A Bonk-only trusted version would save nothing and would need an exception.

**C. The lid as its own Shared object.** A per-fighter prop simulation like the Arcade ball
(`ArcadeBallSimulation.cs:117-186`, with its own snapshot section,
`ServerSimulation.Snapshot.cs:71-79`) and an explicit lid state. It could bounce off walls and lie
on the floor to be picked up. It needs its own contact pass, snapshot section and visuals outside
`ProjectileVFXManager`. [ADR-0037](../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md)
chose focused seams and "a single mutating attack/projectile pass" over a general world-object
framework (lines 6-9 and 19-21); this would reopen that choice. Worth it only for a lid he can lose
and must pick up.

| | A. Placeholder | B. Returning projectile | C. Own object |
|---|---|---|---|
| Return, return hit, catch, self-hit | No | Yes | Yes |
| True one-lid lock | No (a cooldown) | Yes | Yes |
| Walls | Passes through | Turns back on the way out | Bounces |
| A lost lid to pick up | No | No | Possible |
| Shared work | None | Resolver step, hitbox fields, catch/self-hit step, gate | A new subsystem |
| Wire change | None | World Snapshot format 3, protocol 12, an event source | The same, plus a snapshot section |
| Other fighters can use it | No | Yes, public | Only if made public |

### Rules (option B)

| Situation | Proposed rule | Engine note |
|---|---|---|
| Out | Leaves his left hand at the release tick, aimed at the target like Wibou's shuriken, so the air throw angles down at a grounded target. Today's startup correction still turns him toward it during the wind-up. It slows, swings round and comes back. | Spawn as today (`ServerAbility.cs:256-302`). The slot becomes `aimedProjectile` with `aimMode: none`. |
| Turn-back | At the end of its outbound time, or earlier when it meets solid stage or a Shield (it bounces back). What a hit does on the way out is [question 1](#open-questions). | Stage sweep as the Arcade ball does (`ArenaDefinition.cs:342, 362`; `ArcadeBallSimulation.cs:139-142`). |
| Back | Steers at his current position (chest height) every tick, faster than he can run, so it reaches him. It passes through the stage. | The same resolver step. |
| Hits | Each fighter at most once per leg, so the fighter hit on the way out can be hit again on the way back. Out: knockback away from him. Back: toward him; `towardOwner` already uses his position at the moment of the hit (`ServerSimulation.cs:2216-2233`). Both legs play the slot's hit cue (DOOONG). | Per-leg hit histories. |
| Catch | It touches him while he is free: it is back on his shoulder, with a catch event. Free means Idle, Run, Crouch, Slide or a jump start, on the ground or in the air, with no Hitstun, Hitstop, block stun or landing lag. That is the "could act now" test (`ServerSimulation.cs:768-793`) without its ready-slot clause. | A new step after `ResolveHits`. |
| Self-hit | It touches him during his own action: any move including its cancellable tail and the throw's own recovery, an air dodge, a grab attempt, a shield drop, landing lag. It hits him as an ordinary hit with no launch and a short fixed stun (the wobble), with DOOONG on his bucket and a "?", and is back on his shoulder. Ordinary rules apply: armour takes it (only the "?"), and invulnerability lets it pass so it comes round again. | Owner contact like `CanHitOwner` (`Hitbox.cs:85-86`, `SpellResolver.cs:404`), sent through `ResolveHits`. |
| Shield | His own shield blocks it: no wobble, back on his shoulder ([question 3](#open-questions)). | Block as any projectile. |
| Being hit or in a grab | In Hitstun or a launch, held, thrown, or holding or throwing someone, it doesn't hit him and is back on his shoulder. A hit would overwrite the launch he is taking (`ServerSimulation.cs:2299-2324`) or break the grab (`:2202-2203`). On its way home it can still hit the fighter holding him, which frees him as any damaging hit does ([combat systems](../systems/combat-systems.md) lines 161-162; [question 4](#open-questions)). | — |
| One lid | Q, ground and air, is refused while his lid is out; the press is dropped like a spent charge. Training's no-cooldown switch does not lift it. | Admission gate. |
| Cooldown | Ready the moment it is back: the lid is the cooldown. Today the cooldown starts when the throw ends (`ServerSimulation.cs:223-239`). An extra cooldown from the catch is [question 2](#open-questions). | — |
| Interrupted throw | Before the release no lid leaves and the shoulder stays covered. After it, the lid flies on. | As today. |
| KO or leaving | His lid in flight disappears, and he respawns with it on his shoulder (respawn rebuilds his state, `ServerSimulation.cs:2555-2590`). Today projectiles outlive a KO (`:2532`). | Remove the owner's returning projectiles. |
| Air | `air.A` throws the same lid. A catch in the air counts as free. Specials keep running after landing (`ServerSimulation.cs:726-727`). | — |
| Safety cap | If it cannot reach him within its lifetime, it is back on his shoulder. | `DurationTicks` |
| Arcade | Hits Slopball balls and Course Targets like other projectiles (`ServerSimulation.cs:2172-2181`). | — |

The throw's own recovery counts as busy. With the boomerang shape the compiler can check that the
trip out and back takes longer than the rest of the throw when he stands still, so a plain throw
never bonks him. With the ricochet shape, a point-blank hit brings it back into his own recovery.

### Shared, netcode and rollback (option B)

- **Content:** the `return` block is optional and validated beside today's projectile checks
  (`CharacterPackageCompiler.cs:523-526, 911`; `CharacterPackageSourceCodec.cs:1084`). Packages
  that use it need runtime API 1.8.0 (`CharacterPackageCompiler.cs:204-212`; loader list in
  `CookedCharacterPackageLoader.cs:86`). Without it, canonical bytes and behaviour don't change.
  Ability Lab's projectile fields and the CPU's reach estimate (`AI/HeuristicBotPolicy.cs:1128-1139`)
  learn the return.
- **Snapshot and wire:** the new hitbox fields go into the hitbox codec (`SnapshotCodecs.cs:452-586`),
  so World Snapshot format 2 becomes 3 (`ServerSimulation.Snapshot.cs:13`) and protocol 11 becomes
  12 (`SimulationProtocol.cs`). Catch and self-hit events need a new presentation event source,
  which peers validate (`TimelinePresentationEvent.cs:3-11`, `PresentationEventPacket.cs:224-230`).
  That is one coordinated client/GameHost/Master release
  ([netcode](../systems/netcode-architecture.md) lines 3-11).
- **Rollback:** nothing lives outside the snapshot. The client predicts the lid like any
  projectile. Catch and self-hit events are keyed by tick, owner, the lid's attack sequence and
  source, so the rollback ledger shows each one once.

### Presentation hooks

- **Lid in his hand at the wind-up:** a `WeaponAttach` entry for slot A on his left hand, hidden
  from the release tick (`HideAfterTicks`, `WeaponAttach.cs:349-360`). Data only.
- **Bare shoulder:** the left pauldron must become its own mesh piece (`bonk.md:35-37`). A small
  client rule hides it from the grab tick while his lid is in the resolver: Training's own, online
  the predicted world's, as `ProjectileVFXManager` reads it
  ([netcode](../systems/netcode-architecture.md) lines 635-643).
- **Lid in flight:** a `ProjectileVFXConfig` entry for Bonk slot A, ground and air
  (`ProjectileVFXConfig.cs:16-22`).
- **Catch and self-hit:** Shared events, because projectile removal bursts are Training-only. The
  catch is a clang as it snaps back. The self-hit is DOOONG, his bucket ringing (`SnapToOwnerBone`)
  and the "?" from the BONK! pass (`bonk.md:236-245`). A caught lid must not play the removal burst
  Training shows (`ProjectileVFXManager.cs:113-135, 321`).
- **Wobble:** the light hit reaction until a wobble clip exists. The self-hit event tells the
  renderer which reaction it is.
- **HUD:** Q shows that the lid is out, not a cooldown.

### Tests (option B)

- **Compiler:** the `return` block parses, is bounded, rejects unknown fields, needs runtime API
  1.8.0 and is Workshop-admitted. Without it the bytes don't change (`CharacterPackageCompilerTests`,
  `BonkKitTests`).
- **Behaviour** (`BonkSpecialTests` and a new `ReturningProjectileTests`): turn-back by time, wall
  and Shield; homing on a moving owner; once per fighter per leg, the same fighter on both legs;
  return knockback toward his current position; a catch while free re-enables Q; a self-hit for
  each busy case; armour gives only the "?"; no self-hit while hit or in a grab; a KO removes the lid and
  he respawns with it; Q refused while it is out, ground and air, also for the CPU; an air throw and
  an air catch; interruption before and after the release.
- **Snapshot and wire:** `WorldSnapshotContractTests.HitboxCodecCoversEveryFieldAndRelinksOwnerContent`
  fails until every new field is encoded. `WorldSnapshotTests.FreshSimulationsRestoredMidFightMatchTheContinuousOneEveryTick`
  restores with a lid in flight; `RosterRollbackConvergenceTests`; `PresentationEventPacketTests`,
  `WorldFramePacketTests` and `RollbackLedgerTests` for the new events.
- Run with `dotnet test tests/Shared.Tests/ --nologo -p:SkipUnityPluginCopy=true`
  ([testing](../testing.md) lines 40-43).

## The Joust (R)

### What is missing

A charge that steers, ends the moment the lance hits, ends differently on a miss, holds its height
for a few strides in the air before dropping, and has a rule at stage edges.

### Options

**A. Data only (no engine work).** `startupAimCorrection` during the rear and `emitPresentation`
for NEIGH!, then `armorWindow`, a long `forwardLunge` with `stopInAttackRange` and a lance
`spawnHitbox` on `_weapon_tip` for the gallop. `air.R` adds an absolute `setVelocity` of zero and a
zero `gravityWindow` for the strides; when the window ends he drops. You get the rear, a straight
armoured charge, the lance hit, and the strides and drop in the air. You don't get:

- steering: the lunge keeps the facing it started with (`CookedTimelineAbility.cs:347`);
- an end on the hit: `stopInAttackRange` stops the push on proximity and the move plays on
  (`:390-396`);
- separate endings for a hit and a miss, since timelines have no branches;
- armour and a lance that stop when he stops;
- any rule at edges.

Mobile aim is no substitute: it walks him in any stick direction at up to run speed and fights the
lunge for the velocity (`Simulation.cs:524-544`). Option A is good only for an early feel test.

**B. Extend the charged directional dash (new version).** It already has travel, wall and Shield
stops and a recovery phase (`ChargedDirectionalDashAbility.cs:11-14, 129-225`). But most of what
defines it is the opposite of a joust: hold to charge, a direction locked at release, phasing
through fighters (`:32`), hits that pass through, a finisher seek. A version 2 with a joust mode
would be two moves under one ID. Reuse its code patterns, not its contract.

**C. A new public capability (recommended).** Working name `slop.ability.steered-charge.v1`. The
timeline starts it after the rear, and it owns the gallop and its ending:

- the gallop: a fixed speed, the stick turning the heading within a capped turn rate, and the lance
  hitbox and armour for as long as it lasts;
- the outcome: a lance hit gives a short ending; a miss, a Shield or a wall gives the skid and turn;
  no ground under him gives a few strides on thin air, a look down and a drop;
- each ending plays its own clip by raising `ComboStage`, as the leap's landing does
  (*uncommitted*: `TargetedLeapAbility.cs:141-148`, `ServerSimulation.cs:637-663`);
- it owns the end of the move like the leap does (`ILandingContinuationCapability`,
  `CookedTimelineAbility.cs:98-107, 183-186`), so a gallop of any length and its ending don't have
  to fit a fixed stage.

From the dash it reuses the wall test, the Shield stop and the snapshot pattern.

### Rules (option C)

| Situation | Proposed rule | Engine note |
|---|---|---|
| Startup | Rears and neighs in place; NEIGH! is a timeline cue. Turns toward the target as today's R does. Armour is [question 8](#open-questions). | `startupAimCorrection` ends before the gallop starts. |
| Gallop | A fixed speed. The stick steers within a capped turn rate and a neutral stick goes straight. Lance couched, armour on. Solid against fighters: no phasing. | The capability reads the stick each tick. |
| First fighter met | Unhorsed: a big launch and BONK! (the slot's hit cue). The gallop ends at once, with a short rein-in ending. | Its own hit hook, after the hook fix. |
| Shield | The gallop ends with the skid and turn ([question 11](#open-questions)). | Shield stop as in the dash (`ChargedDirectionalDashAbility.cs:221-225`). |
| Miss | He gallops to the end, skids and turns the horse round: the punish window. He ends facing back the way he came ([question 10](#open-questions)). | Ending clip. |
| Wall | Like a miss. | The dash's blocked-travel test (`:134-143`). |
| Stage edge | He gallops off, keeps going a few strides on thin air, looks down and drops: the air ending. The alternative is a skid to a stop at the edge ([question 9](#open-questions)). | A small step down must not count: probe for floor, as the dash tolerates seams and landings (ability architecture lines 106-108). A stop at the edge needs a floor probe ahead (`ArenaDefinition.cs:524-555`). |
| Air | Rears in the air with the slowed fall today's air R has, gallops level for a few strides, looks down and drops. Hit or miss, it ends with the drop. That makes it a sideways recovery. | Owns vertical motion during the strides, like the dash (`:36`). |
| After the drop | He can act again (jump, E). The engine has no helpless fall (`ActionState.cs`; [question 12](#open-questions)). | — |
| Landing during the air gallop | Specials keep running after landing (`ServerSimulation.cs:726-727`). The rest of the gallop goes on along the ground and ends with the skid. | — |
| Hit while galloping | Armour: he takes the damage without a launch and the "?" pops (combat systems lines 216-219). | Armour must end with the gallop, see below. |
| Grab | A grab ignores armour, captures him and cancels the Joust, as today (`bonk.md:413-415`). | — |
| Interrupted | The lance and the armour end with the move. | `ServerSimulation.cs:213-221` |
| His own lid comes back | He is busy but armoured: only the "?", the lid is back on his shoulder and the gallop goes on. | Needs the hook fix. |
| Reuse in the air | The cooldown is the only limit. Per-flight limits are described (combat systems line 35) but no slot field exists (`CookedCharacterDefinition.cs:189-209`). | — |

### Shared, netcode and rollback (option C)

- **New capability:** a class with the snapshot contract; the registry
  (`InternalCapabilityRegistry.cs:17-58`); public admission and slot rules like the dash's
  (`CharacterPackageCompiler.cs:69-72, 154-162, 281-292, 334-349`); cooked parameters, source codec
  and loader (runtime API 1.8.0); Ability Lab parameters; the CPU's reach estimate
  (`AI/HeuristicBotPolicy.cs:1157-1164`).
- **Two engine fixes:** the hook fix, and armour that ends with the gallop. Today only the
  timeline's own fixed window counts (`ServerAbility.cs:41`, read at `ServerSimulation.cs:2164`,
  ticked at `:422`). The timeline should also report its capability's armour.
- **Snapshot and wire:** the capability's state rides inside the move's snapshot
  (`CookedTimelineAbility.cs:498-504, 529-548`), so the World Snapshot layout doesn't change.
  Ending events (skid dust, the look down) need an event source; a new one is the same wire change
  as the lid's.

### Presentation hooks

- **Hobby horse:** a `WeaponAttach` entry for slot R, shown for the whole move
  (`WeaponAttach.cs:349-360`).
- **NEIGH!:** a timeline cue at the rear, replacing `presentation.bonk.bash.snort`.
- **Gallop cues:** dust, the coconut clip-clop and the armour glow are fixed-length cues today, and
  they end only with the move (`AttackBoundPresentation.cs`; the glow "lasts exactly the authored
  armour window", `bonk.md:236-240`). A gallop that stops early needs cues that end when
  `ComboStage` changes, a small client option.
- **Clips:** the rear and gallop, then the rein-in and the skid and turn as ending clips. Cooked
  stages sort their clip IDs, so ending clip IDs must sort after the gallop's
  (`TargetedLeapAbility.cs:141-143`).
- **Turning round:** either the clip turns him and his facing flips at its end, or the capability
  turns his facing during the clip. Both must agree, because hurtboxes follow the baked pose
  (`ServerSimulation.cs:614-627`).
- **Unhorsing:** the slot's hit cue, the big BONK!.

### Tests (option C)

- **Compiler:** public, versioned and Workshop-admitted; one per slot; competing motion and hitbox
  operations rejected; parameters bounded (`CharacterPackageCompilerTests`, `BonkKitTests`).
- **Behaviour** (`BonkSpecialTests` and a new `SteeredChargeTests`): steering stays within its cap;
  the first hit stops the gallop, launches and picks the short ending; a Shield and a wall pick the
  skid; a whiff picks the skid and the final facing; the edge rule; no vertical drift during the air
  strides, then a normal fall; landing mid-gallop; armour only while galloping; a grab cancels it;
  interruption leaves no lance; a lid hit during the gallop doesn't end it.
- **Snapshot:** `WorldSnapshotContractTests.EveryRosterCapabilityRoundTripsEveryField` and
  `EveryAbilityTypeIsReachableThroughRosterContent`; `WorldSnapshotTests` restores mid-gallop.
- **Replaced:** the Shoulder Bash and Charging Double Slash tests (`BonkSpecialTests.cs:16` onward)
  and the air A/R gravity-window test (`BonkKitTests.cs:63-94`).

## Recommendations

### Pot-Lid Throw: option B

Make the lid a real boomerang in the engine. It flies out, turns, flies back to wherever Bonk is,
and can hit on the way out and on the way back. When it reaches him the game checks what he is
doing: hands free, he catches it; busy, it bonks his bucket and he wobbles under a "?". Q stays
locked while the lid is out. Any future fighter can use the same boomerang.

Option A doesn't deliver the kit. Option C only pays off for a lid he can lose and pick up, and it
reopens ADR-0037. Option B keeps one projectile pass and gets the snapshot, prediction and visuals
the resolver already has. It brings the one wire change (World Snapshot format 3, protocol 12).

### The Joust: option C

Make the gallop one new engine move type: a charge you can steer, which stops the moment the lance
hits someone and otherwise ends in a skid and a turn. Whenever there is no ground under him, in the
air or past an edge, he gallops a few strides on thin air, looks down and drops. Other fighters'
kits can reuse it.

Option A can't steer or tell a hit from a miss. Option B would bend a public contract into a second
meaning. Option C needs no snapshot layout change of its own.

### Order

1. The hook fix, on its own. It only changes the rare shuriken-during-Dash-Slash case above.
2. The Joust capability.
3. The returning lid, with the snapshot and protocol change.
4. Both ship in one coordinated release.

Commit the Sir Bonk E phase-clip support first, since both moves build on it. If the owner wants to
set the gallop's timing before the engine work, option A can be built first as a throwaway test.
After approval, the primitives get sections in [ability architecture](../systems/ability-architecture.md)
and [combat systems](../systems/combat-systems.md), and `bonk.md` records the authored values. No
new ADR is needed unless option C is chosen for the lid.

## Built 2026-10-21

In the plan's order, all in Shared, uncommitted:

1. **Hook fix.** Hit, block and stage-contact hooks reach only the activation whose hitbox made
   the contact (`ContactHookOwnershipTests`).
2. **The Joust:** `slop.ability.steered-charge.v1`, option C, public at runtime API 1.8.0
   (`SteeredChargeTests`, compiler and loader tests). Timelines now forward their capabilities'
   armour and gravity: fighters move before abilities tick, so the air strides switch gravity off.
3. **The lid:** a `return` block on `spawnProjectile`, option B, public at runtime API 1.8.0
   (`ReturningProjectileTests`, compiler, loader and tamper tests). World Snapshot format 3,
   protocol 12, presentation sources `ProjectileCatch` and `ProjectileSelfHit`.

Beyond the plan:

- Hanging on a ledge counts as a catch: a self-hit there would knock him off the stage.
- A throw point-blank into a shield is home inside the throw itself, so it bonks him (the
  ricochet note above, through the shield bounce).
- The HUD keeps the Q key pressed while the lid is out, and the CPU skips the slot, through
  `CharacterState.ReturningProjectileSlot`.
- Self-hit damage (question 5) and the wobble (question 7) stay authored values and presentation
  work for Bonk's package.

The Joust is wired in Bonk's package (2026-10-21, [bonk.md](../characters/bonk.md#the-joust-r--wired-2026-10-21)).
The hit now stops him on the hit tick itself, so the hitstop no longer lets one more stride of gallop speed through.

The Joust's effects are built (2026-10-21, [bonk.md](../characters/bonk.md#the-jousts-effects)). The gallop cues
did not need cues that end when `ComboStage` changes: the gallop's launch cue reads the loop's clip and clock
(`LoopFootfalls`), the skid follows his heels (`FollowOwnerBone`) and the horse's puff plays when the move ends
(`AttackEndCue`). The armour glow is still not shown during the gallop.

Still open: Ability Lab editing for both primitives; a compiler check that a plain standing throw
never bonks him; the lid's presentation hooks and Bonk's Q values; the Master's
`LobbyModels.Protocol = 12` at the release.

## Open questions

These are about shape; timing and numbers stay with the owner. Each has a recommended answer, so
"recommended, except …" is a complete reply.

Pot-Lid Throw:

1. **On the way out:** does it fly its whole path regardless, hitting anyone it passes once each way
   (boomerang), or turn back at the first fighter it hits (ricochet)? With the ricochet the return
   hit lands at once on the same fighter, so the two hits become one pull. *Recommended: boomerang.*
2. **Ready again:** the moment it is back, or after a cooldown that starts at the catch?
   *Recommended: the moment it is back.*
3. **Shield:** if he holds Shield when it comes back, does his shield block it, with no wobble?
   *Recommended: yes.*
4. **Held:** on its way home it can hit a fighter holding him, which frees him, but it never hits
   him while he is held. *Recommended: yes.*
5. **Self-hit damage:** does it hurt him, or only stun him?
6. **Walls:** does it bounce back off walls on the way out and pass through them on the way back?
   *Recommended: yes to both.*
7. **Wobble:** is the light hit reaction fine for the first playtest, or does it need its own clip
   first?

The Joust:

8. **Armour:** during the rear too, or only once he gallops? *Recommended: only the gallop, so the
   rear is the tell.*
9. **Edges:** does he gallop off and do the air ending, or skid to a stop at the edge?
   *Recommended: gallop off.*
10. **After a miss:** does he end up facing back the way he came? *Recommended: yes.* The owner changed this after
    the video references (2026-10-21): no turn, the dramatic skid stop is enough (`turnAroundOnMiss: false`).
11. **Shield:** does a blocked lance get the long skid and turn? *Recommended: yes.*
12. **After the air drop:** can he still jump or use E? *Recommended: yes, no helpless fall.*
