# ADR-0064: Dusty's Lasso Grapple, a self-pull to a stage lip or a fighter

**Status:** Accepted, 2026-11-05. Owner approved the design and all four defaults: shield stops the loop, caught fighters are held, grounded target-first/airborne lip-first, ordinary cooldown without refresh. Numbered 0064 because [ADR-0063](0063-manki-mayhem-variety.md) is Mayhem variety.
**Extends:** [ADR-0048](0048-bashie-rescue-ring-tether.md) (tether pair, `Reeled`),
[ADR-0059](0059-sir-bonk-scoop-joust-and-leap-of-faith.md) (`ActiveMoveView` spot) and
[ADR-0039](0039-full-world-rollback.md) (World Snapshots)

## Context

Dusty's E was the Sombrero Copter (`slop.ability.aimed-recovery.v1`). The owner swapped it for the Lasso Grapple:

- He throws his rope loop up toward the nearest stage edge in reach. It catches, he yanks himself up the rope in a
  quick two-hand slingshot and pops over the edge onto the top.
- If the loop meets a fighter first, he yanks himself to them boots first and kicks them.
- On a miss the loop flops and he keeps falling. Big Iron's recoil is his backup recovery.

The engine cannot do this today:

- The stage has no edge list. Shared has collision triangles, a spatial grid and a heightmap. The ledge grab's
  `FindLedge` is heightmap-only and disabled.
- The tether moves the caught fighter to the thrower. Nothing moves the thrower along a line.
- A ring that meets the stage is a miss.

## Decision

**A new public capability, `slop.ability.grapple.v1`.** It does not get a third mode on `tether.v1`, because the
moving body is inverted, and the move has an anchor search, a haul, a pop and a kick. Its phases are Windup, Out,
Haul, Pop, Zip, Kick, Miss, then Done. Dusty stays `Attacking` throughout, so clips, cooldown, interruption and armour
follow the ordinary activation rules.

**Anchors are lips of walkable tops, found by a deterministic search.** `GrappleAnchors` is a static Shared function
with no allocation. At the throw tick it marches along 16 fixed world headings out to `anchorReach`, and at each
step it drops a probe through the band between `minRise` and `maxRise` above his feet. On each heading, the first
surface it finds is that heading's lip; a bisection refines it, and the point is snapped to a 1 cm grid. A lip
counts only if:

- a standing capsule fits `popInset` inside it, and
- the line from his hand to it is clear.

Walls, Mayhem walls, hazards and moving objects are never anchors. The nearest valid lip wins. A stick held during
the `RecoveryTurn` window keeps only lips on its side. Ties go to the lower heading index. The bot and Ability Lab
call the same function.

**Target order.** In the air: a lip, then his current target, then a miss up and forward along his facing. On the
ground: his current target, then a lip, then a miss.

**The loop is an ordinary projectile of his activation.** Its flight time is fixed at the throw and ends at the
lip. The first fighter it touches is caught, whatever it was aimed at. A shield stops it, and an air dodge passes
through, as for the ring. A stage hit before the lip is a miss.

**The stage catch: a haul, then a pop.** The haul lasts a fixed `haulTicks`. Its speed eases in, so it reads as a
slingshot. The path goes up to a point outside and above the lip, then inside it to the pop point. The ability sets
his velocity and the body mover slides him along walls. A step that gains too little breaks the line. At the pop
point he gets `popVelocity` under ordinary gravity. The move ends when he lands, or after `popTicks`. There is no
ledge hang.

**The fighter catch reuses the tether pair, with the motion inverted.** The catch writes the existing pair fields
with `DefenseInteractionPhase.Reeling`, and the victim enters `Reeled` and is held, not moved. `ZipToPartner` moves
Dusty toward a strike point just short of them, for at most `maxZipTicks`. On arrival the pair ends and an authored
kick hitbox spawns at his boots that same tick. He then bounces back by `kickRecoil` and is free after
`kickRecoveryTicks`. The existing rules for death, resets, third-party hits, "already held", catch clashes and the
`TetherHolds` validator all apply unchanged.

**Subtract first.** `ServerAbility.Tether` returns a small `ITetherCatch` seam: catches, holding and caught. The
tether implements it first, with no behaviour change. Then the grapple joins.

**Availability.** It uses one ordinary slot cooldown at the press, with no per-airtime flag. A miss is a short fixed
fall (`missRecoveryTicks`), so Big Iron stays in reach.

**Package contract.** One `meleeCombo` stage with IASA zero, one tick-zero grapple capability, and presentation
operations only beside it. `aimMode` is `none` and `isRecoveryMove` is true. The loop uses the existing projectile
spec: positive speed and radius, zero gravity, no `return` block. The kick uses the existing hitbox spec. The
runtime API moves to the next minor. The compiler and the cooked loader enforce the same rules.

## Prototype defaults

All of these are first guesses for Training, not decisions: throw tick 8, `anchorReach` 6 m, `maxRise` 5 m,
`minRise` 0.2 m, `loopSpeed` 40 m/s, `haulTicks` 14, `popClearance` 0.6 m, `popInset` 0.6 m, `popTicks` 20,
`zipSpeed` 24 m/s, `maxZipTicks` 24, `kickRecoveryTicks` 12, `missRecoveryTicks` 20, `airGravityScale` 0.3, cooldown
210 ticks. The loop's damage is small and named. The kick's damage and knockback come from the first Training pass.

- **Dusty hit during the haul or the zip:** the line breaks and he takes the hit normally.
- **Victim hit by a third fighter:** the line breaks, the victim takes that hit normally, and Dusty falls.
- **Two Dustys:** catches in the same batch clash. Otherwise the first catch wins.

## Consequences

- No new `ActionState`. The flop is one new `PresentationEventSource.GrappleMiss`. The ability writes its own
  snapshot record: phase, ticks, branch, anchor, haul start, pop point and stick yaw. The World Snapshot version
  steps to 32 (29–31 are the Mayhem airing, channel and finale layouts). The change is listed under "Unreleased" in `SimulationProtocol.cs`, and release prepare does the one
  protocol bump.
- `salamander` drops `aimed-recovery.v1` from its E slots. The capability stays for Nyx.
- `TetherRopePresenter` gains a hand bone per move and reads the slot's own projectile visual instead of slot A.
  The rope ends at `ActiveMoveView.Spot` during the throw and the haul, and at the partner during the zip.
- The bot recovers with E only when `GrappleAnchors` finds a lip, and otherwise uses F.
- Risk: picking a lip is a yes-or-no choice. A float difference between prediction and authority can choose a
  different lip, which shows as a rollback correction. The grid snap and the index tie order make this less likely;
  they do not rule it out.
- Risk: the search costs about 640 drop probes on one tick. Measure it on the densest stage.
- Risk: a held victim makes the kick sure once the loop lands. If play finds it too strong, the alternative is a
  free victim who can shield the kick.
- Risk: course routes get easier for Dusty. Rerun the course finish checks.

## Delivery boundary

This engine PR leaves every authored and cooked package unchanged. The salamander E switch, rope presenter, clips and Ability Lab UI follow in the canonical Unity checkout.
