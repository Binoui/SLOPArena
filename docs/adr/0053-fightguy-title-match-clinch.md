# ADR-0053: FightGuy's Title Match, a clinch as a paired interaction

**Status:** Accepted for the Training prototype, 2026-10-29. The maintainer picked Title Match for FightGuy's F
("i like title match") and approved option (c) ("go for C"). Built 2026-10-29 (see [Build notes](#build-notes)). The
values under [Prototype defaults](#prototype-defaults) were judged GOOD by the maintainer in Training on 2026-10-29 and stay open to tuning.
**Extends:** [ADR-0048](0048-bashie-rescue-ring-tether.md) (paired interactions beyond the grab) and
[ADR-0039](0039-full-world-rollback.md) (World Snapshots)
**Merged:** the clinch is now `slop.ability.command-grab.v1` with a hold stage ([ADR-0057](0057-one-command-grab.md)).

## Context

Fist of Fury is a fixed 120-tick timeline. A punch at tick 10 with a fixed hitstun and a pull toward FightGuy locks
the victim, five more punches and a weak hit follow, and a right-foot kick at tick 88 launches. Measured in the offline
Shared sim on 2026-10-29 (flat ground, FightGuy dummy, `tmp/fightguy-specials/probe-out.md`):

- He does not step in. A target 1.25 m away who walks away or sideways is out of reach before the first punch.
- A shield blocks all of it, and a miss costs the full 120 ticks.
- On a full hit he acts again at tick 171 (2.85 s), because every hitstop pauses the timeline.
- The kick flies 15.8 m at 100% against Double Kick's 17.8 m. On Training it needs 110% to KO from spawn 0, against
  90–95% for Double Kick (`tmp/fightguy-specials/probe3-out.md`).

Read in code, not measured: the hitbox `interruptible` flag has no runtime reader. Every punch hits every fighter in
range (`HitsMultipleOpponents`), so a bystander who walks in is also locked and pulled. Inferred: if FightGuy is hit
out of the move, the victim keeps the rest of its fixed lock and stays free for a third party.

The approved move:
- F, ground and air, is a short lunging grab at FightGuy's current target.
- A catch locks the victim into today's Fist of Fury show, held in front of him, and ends in a launch stronger than
  Double Kick.
- A shield does not stop the catch. A dodge or a miss leaves him grabbing air for a short, readable window instead
  of the full flurry.
- In four-player free-for-all, a third party can still break it by hitting either fighter.

The engine cannot hold a fighter through an attacker's own hits today:
- The shield grab (`StartGrabAttempt`, `ResolveGrabAttempts`, `UpdateCoupledInteractions`) is a movement state, not
  an ability. It is grounded and stationary, throws on any slot press with fixed throw values, and has no pummel.
- The tether's validator (`TetherHolds`) ends the pair when either fighter is in hitstop or hitstun, and a tether's
  contacts only catch. FightGuy's first punch would end the pair.
- Timeline stages advance by duration only. Nothing branches on a hit or a miss.
- `Hitbox.Unblockable` exists at runtime but is not authorable on a cooked hitbox.

## Options

| | (a) Keep the lock, add an unblockable hitbox and a whiff cut | (b) Let a special drive the shield grab | (c) A clinch capability on the pair (proposed) |
|---|---|---|---|
| Beats a shield | The first punch only | Yes | Yes |
| Dodge or miss | Escapes punch 1, then a new "end if nothing was hit" operation stops the move | Yes | Yes, a short recovery decided by the capability |
| Four players | The orphaned lock and the bystander lock stay | Pair cleanup | Pair cleanup; a bystander takes only light punches |
| Air | As today | The grab is ground-only, so a rewrite | Same as ground |
| Engine work | An authorable unblockable flag and a new timeline operation | Rework of the grab every fighter uses | A new public capability, a pair phase and a victim state |
| Main risk | It is not a grab | Regressions in every fighter's grab | Hit order inside one hit batch (below) |

## Decision

**(c). Title Match is a clinch: a new kind of paired interaction, owned by one public capability,
`slop.ability.clinch.v1` (name open).** It follows the tether's shape from ADR-0048, so death, removal, every mode
reset, the "already held" rule and a third party's damaging hit end it through `InterruptInteraction` paths that
exist today.

- **Lunge and catch.** Stage 0 starts the capability. It dashes a short fixed distance toward the current target
  (the Quick Draw pattern, ADR-0052) carrying a catch hitbox. In `ResolveHits` a clinch catch is routed before the
  shield check, like the tether. It refuses a fighter already paired, held or damaged this batch, and two clinches
  that meet in one tick clash as two tethers do.
- **Whiff.** Air-dodge invincibility already makes a hit ignored. A dodge, range or a wall ends the lunge empty, and
  the capability ends the move after a short recovery. No package binds a grab clip, so the whiff pose is chosen in
  the build (inferred: the opening of the F clip, or a new short clip).
- **Hold.** The catch writes the pair fields with a new phase and puts the victim in a new captive state (not
  `Reeled`, which draws Bashie's ring). One writer, called by the capability each tick, keeps the victim at the
  capture anchor in front of him, on the ground or in the air, where gravity is off for both while he holds.
- **Flurry.** The punches stay authored in FightGuy's timeline, with the approved clip and effects. A hit from the
  partner's own activation on its clinched victim deals damage and hitstop without knockback or interruption, like
  `HitHeldFighter`. The clinch validator allows hitstop and rejects hitstun or knockback. The punches lose their pull
  and fixed hitstun.
- **Finisher.** The capability ends the pair one tick before the kick and leaves a short captive lock, as the tether's
  arrival does. The authored kick then launches normally. It is tuned to KO earlier than Double Kick.

## Sweetspot feedback for Rising Dragon

The same change teaches E's sweetspot (built 2026-10-29, see [FightGuy](../characters/fightguy.md)). The maintainer
asked for an effect on a sweetspot hit "to teach players". Today a slot has one hit presentation and the apex "#1"
finger plays on every Dragon.

- An authored hitbox may name its own hit presentation, overriding the slot's. The three sweetspot hitboxes name a
  gold "ego" burst; the sourspot keeps the slot's blue hit.
- A timeline presentation may require that its activation landed a sweetspot hit. The apex "#1" finger carries that
  condition, so a whiff or a sourspot shows no finger.
- Both are optional fields with today's behaviour as the default, so other packages cook unchanged. Art reuses
  FightGuy's approved gold ego and 肉 seal look; no new concept is needed.

Sweet and sour keep one shared hit group, so each fighter still takes one hit. The build picks the smallest way for the
condition to recognise a sweetspot contact (for example, by the hitbox's own hit presentation) and records it here.

**Rule chosen in the build.** A hitbox's optional `hitPresentationId` replaces the slot's at its contacts. An
`emitPresentation` with `condition: "sweetspotHit"` plays only when its activation has already confirmed a hit from a
hitbox that names its own hit presentation: no separate "sweetspot" flag exists, the override is the mark. The
activation remembers that hit in its World Snapshot state, so a rollback keeps the finger. The three sweet hitboxes name
`presentation.fightguy.rising-dragon.sweet-hit`, bound to the Fist of Fury hit prefab: a 14-damage contact scales it to
the gold ego burst with the 肉 seal (its finisher variant). The sourspot keeps the slot's blue hit.

## Prototype defaults

Measured and set in the build, open to the maintainer's play verdict: the lunge length and speed, the catch window,
the whiff recovery, the hold timing (today's flurry), and the finisher's launch. The 720-tick cooldown stays.

| Value | Default | Measured effect (offline Shared sim) |
|---|---|---|
| Lunge | 2.0 m at 20 m/s, 6 travel ticks; lunge stage 7 ticks | catches a standing fighter up to 3.5 m away, whiffs at 3.8 m |
| Catch | root-relative sphere r 0.6 m, 0.6 m ahead, 0.1 m up, 1 damage | catches through a held shield; an air dodge at any of ticks 1, 3, 5 escapes |
| Hold anchor | 0.8 m in front of his feet | all seven held hits land at 0.80 m, each with 6 ticks of hitstop |
| Whiff recovery | 24 ticks, lunge pose held | he acts again 30 ticks after the press |
| Hold timing | today's 120-tick flurry, release on its tick 87, victim locked 6 ticks | on a full hit he acts again at tick 178 (was 171) |
| Kick | 12 damage, 28°, base 12, growth 42 (was 36) | KOs off Training from spawn 0 at 75%, 75% with sideways DI (Double Kick 95% / 90%, old F 110%) |


## Consequences

- Shield stops being an answer to F; spacing, jumping and a dodge are. Any fighter can be given a command grab later
  through the same capability.
- New enum values only. No new `CharacterState` field, as in ADR-0048. The capability snapshots its own phase in the
  World Snapshot without a format change. The sweetspot feedback does change the format: World Snapshot v19 writes a
  hitbox's own hit presentation and the activation's sweetspot contact.
- Admission: a new public capability ID, compiler and loader contract, a runtime API minor bump, and an Unreleased
  entry in `SimulationProtocol.cs`.
- Unity: the held victim plays its hit reactions, as today's flurry victim does (inferred), since no package binds a
  `grabbed` clip. The CPU and Ability Lab learn the capability.
- **Hit order.** If a third party's hit comes earlier in the same hit batch, it ends the pair, and FightGuy's punch
  later in that batch lands as an ordinary hit. Accepted for the prototype.
- Stage 0 has a fixed length, because a timeline cannot jump to the next stage on a catch. An early catch holds the
  victim a few ticks before the first punch.

## Verification

Shared tests:
- A catch through a shield.
- A dodge, a miss and a wall end the move after the whiff recovery.
- A held victim takes every punch and then the launch.
- A third party's hit on either fighter frees both.
- Death, removal and a mode reset clear the pair.
- Air holds work.
- Rollback convergence with a clinch in the World Snapshot.

Then the probe tables in `tmp/fightguy-specials/`, rerun against the new F, and a Training feel check by the
maintainer.

## Build notes

Built 2026-10-29. Measurements are in `tmp/fightguy-specials/title-match-out.md`.

- **Data shape.** `ClinchAbility` is one state machine: Lunge, then Held and Released after a catch, Released alone
  after an object, or Whiff; Done ends it. The pair lives in the interaction fields (`DefenseInteractionPhase.Clinching
  = 6`, victim `ActionState.Clinched = 31`, in `InPairedInteraction`, `Captive` and `MovedByPartner`). One writer,
  `HoldClinch`, places the victim; `ClinchHolds` validates the pair in the pair pass (hitstop allowed, hitstun and
  knockback refused); `ReleaseClinch` ends it as a tether's arrival does. A future fighter reuses it by authoring a
  lunge stage and a hold stage around the capability.
- **Timeline.** F is two stages: the lunge (the clinch at tick 0, `anim.dash`), then the hold (today's flurry, its clip
  and effects unchanged). The capability holds the lunge stage during a whiff, so the flurry never starts empty; a
  catch waits for the lunge stage to end, up to 6 ticks, before the hold stage's opening plays. The punches lost their
  pull and fixed hitstun, and the startup aim correction is gone: the lunge takes its bearing at the press.
- **Whiff pose.** No package binds a grab clip, so the lunge clip (`anim.dash`, a diving reach) holds its last frame
  through the whiff recovery: no new clip and no new binding.
- **A lost pair ends the move at once** (a third party's hit on the victim, a KO, a removal or a reset). Playing the
  flurry into air would hand the third party a free punish. A hit on FightGuy interrupts him as any hit does.
- **Objects.** A lunge that meets a course target, the ball or a Mayhem wall stops and plays the hold stage on it with
  nobody held, so Fight Night Pier's bonus targets still break as they did with Fist of Fury.
- **Held victim.** Unity shows the small-hit pose, replayed from its start over each held hit's hitstop.
- **CPU.** The profile counts the lunge and catch reach; the CPU prefers F when its target is shielding or at 70% or
  more and stands within that reach.
- **Contract.** Runtime API 1.17.0, `ClinchContract` shared by the compiler and the cooked loader, and an Unreleased
  line in `SimulationProtocol.cs`. Other packages cook byte-identically.
