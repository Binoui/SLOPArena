# ADR-0048: Bashie's Rescue Ring, a tether as a paired interaction

**Status:** Accepted for the Training prototype — 2026-10-28. The maintainer chose the Rescue Ring over Hose Down
("Ring") with the behaviour below. The values under [Prototype defaults](#prototype-defaults) are the
coordinator's; the maintainer's Training verdict on them, on 2026-10-29, was GOOD.
**Extends:** [ADR-0045](0045-bashie-shell-state.md) (Bashie's shell) and
[ADR-0039](0039-full-world-rollback.md) (World Snapshots)

## Context

Bashie wins up close: her shell normals are short and launch upward, and her R and E bring her to the fight. Q's job
is the opponent who keeps away. The approved move:

- Q, ground and air, throws her life ring straight at where her current target is at throw time. No homing, no
  camera or stick aim. With no target it flies along her facing.
- The first fighter it meets is caught and reeled to her feet, stuck just long enough for one normal. She is free to
  act when they arrive.
- A miss (range, terrain, a shield) means reeling the empty ring back. That reel is the punish.
- In the air it works the same; an offstage catch pulls the victim toward her.
- From the shell she pops out and throws (ADR-0045's default policy). Hose Down retires.

The engine cannot deliver a fighter to a point today:
- A knockback pull is ballistic, braked and DI-rotated. ADR-0019 records it over-pulling Nilus's victims.
- The grab is ground-only, places the victim within a fixed reach and turns every slot press into a throw.
- Ordinary projectiles pass through stage walls; only returning projectiles check the stage.
- Ordinary Hitstun adds launch gravity in the air and an input-directed shift when it expires, and
  `ApplyFixedHitstun` only overrides a stun that is already running.

## Decision

**A tether is a new kind of paired interaction.** The catch writes the existing pair fields on both fighters
(`InteractionId`, `InteractionPartnerId`, `InteractionTick`) with a new `DefenseInteractionPhase.Reeling`, and the
victim enters a new `ActionState.Reeled`. Grabs and tethers share one representation, so death, removal, every mode
reset, damaging hits and the "already held" rule end or refuse a tether through code that exists today.

**Her side is one public capability, `slop.ability.tether.v1`.** She stays `Attacking` from throw to finish, so
clips, cooldown, interruption and armour follow ordinary activation rules. Its phases are Windup, Out, Reel or
Retract, then Done. The ring in flight is an ordinary projectile of her activation (`CancelWithAbility`), aimed by
the existing auto-target projectile aim. Each tick of Out she checks the ring's travel against the stage
(`SpellResolver.MeetsStage`, shared with returning projectiles); a stage hit, range expiry, shield or other contact
starts the retract. The empty ring has no gameplay body: its fixed retract time is decided at the miss and drawn
from a presentation event.

**One writer moves the victim.** `ReelTether`, called once per tick from her capability, steps the victim toward an
anchor in front of her feet with the defense-body mover (walls slide; height and ground distance close separately,
so a victim below a ledge lip is lifted over it). The victim's own movement step and the pushbox pass skip `Reeled`.
Too little progress in a tick (a head-on wall) or the reel time running out snaps the line.

**Arrival uses `Reeled` itself.** On arrival the pair ends on both sides, the victim stays `Reeled` with a
`StateTicks` countdown of `arrivalStunTicks`, facing her, with ordinary gravity and no launch, DI or expiry shift,
and returns to Idle when it expires. A hit during that window gives its ordinary reaction. Her move finishes the
same tick, so she acts on the next one. One state covers both the pull (paired) and the lock (unpaired).

**A validator instead of cleanup branches.** `TetherHolds` checks both sides of every Reeling pair in the existing
pair pass: she is `Attacking` with a tether in Reel on that interaction, and the victim is `Reeled` with the matching
partner. Anything else interrupts the pair. A path that forgets to clean up converges on the next pass.

**Subtract first.** The inline `Grabbing or Grabbed or Throwing` lists become `ActionStates.InPairedInteraction`
and `ActionStates.MovedByPartner` with no behaviour change, before `Reeled` joins them.

**Package contract.** One `meleeCombo` stage, IASA zero, one tick-zero tether capability with presentation
operations only beside it, `aimMode: none`, no `shellPolicy: tuck`. The ring is the existing projectile spec with
positive speed, radius and flight time, zero gravity and no `return` block. Runtime API `1.13.0`. Compiler and
cooked loader enforce the same rules.

## Prototype defaults

- **Ring damage:** small and named. A damaging contact is stopped by Mayhem walls and the Slopball net under the
  existing resolver rule; a zero-damage one would pass through. A ball, course target or wall contact is a miss.
- **Bashie hit while reeling:** the line drops, the victim goes free where they are, with no stun.
- **Victim hit by a third fighter:** the line breaks, the victim takes that hit normally, and she retracts.
- **Invincibility:** the ring passes through an air dodge and can catch someone behind.
- **Two Bashies:** catches in the same resolver batch both bounce; otherwise the first catch wins.
- **Already held** (grabbed or on another line): the ring bounces.
- **Grabbing Bashie mid-reel:** refused, as for anyone in a pair. Hits still punish her.
- **Armour:** does not resist the catch, as for grabs.
- **The empty ring** catches nobody on the way back.
- **Arrival window:** her jab started on her first free tick connects; her slower normals can be shielded. Set by a
  test, then by play.

## Consequences

- New byte values `ActionState.Reeled = 27` and `DefenseInteractionPhase.Reeling = 5`; the presentation source
  `TetherRetract`. `CharacterState` and `Hitbox` gain no field. The ability writes its own phase state. The change
  is listed under "Unreleased" in `SimulationProtocol.cs`; release prepare does the one protocol bump.
- Bashie drops `charged-stream` (FightGuy's fixture keeps its coverage) and `BashieRingBounceBuilder` retires; the
  life ring prop stays.
- Risk: the stage stop runs in her tick, one tick before the resolver moves the ring. It relies on her never being
  frozen with the ring out without the move ending. An armoured or hitstop-on-throw variant would need a
  resolver-side stop flag.
- Risk: the tick order. A hit on the victim must win over arrival, and the victim must move exactly once per tick.

The design packages are local in `tmp/bashie-ring/` (`sketch-opus/` is the base, `sketch-codex/` the competitor).
