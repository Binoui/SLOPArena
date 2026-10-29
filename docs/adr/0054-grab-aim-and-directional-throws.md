# ADR-0054. Grab aim and directional throws

Date. 2026-10-29.
Status. Accepted for the first implementation pass. Confirm the ADR number at merge.

## Context

The old grounded grab did not correct startup aim or retain a Beeline grab press.
Holding allowed walking and turning. Throw release replaced an ordinary launch
with scripted `Thrown` flight and fixed landing recovery.
The baseline probe found the thrower actionable on tick 58 and the victim on
84 at every measured percent. A victim thrown over a gap stayed locked until KO.
These clocks prevented ordinary recovery and gave the neutral throw no jump chase.

## Options

- Keep scripted flight and tune its fixed clocks. This retains the airborne lock.
- Add throw fields and a snapshot format. Existing serialized phase, heading and
  terminal identity fields already represent the needed state.
- Keep engine-owned grabs, share ordinary queued reaction setup and encode the two
  windups and successful releases in existing fields. This changes behavior without
  a codec or layout migration.

## Decision

Use the third option and the owner's five rules.

1. Grab startup captures one fighter target under the normal aim rule. It acquires
   within 4 m over the full circle, turns at 720 degrees per second up to 90 degrees,
   and corrects only during seven startup ticks. The capture box follows the result.
2. A Beeline retains the latest attack or grab press. Attack wins a same-tick tie.
   A grounded arrival attempts the held grab on the following tick. An airborne
   arrival drops it. A hit, miss or reset clears either press.
3. Throw release is an ordinary formula launch. Clear the victim's old reaction,
   share the ordinary hitstop and queued-launch writer, then use normal SDI, DI,
   Hitstun, flight and action admission. Keep one slot-zero hit receipt and KO credit.
4. Only the throw-start tick's world-space stick chooses the throw. Neutral input
   tosses up. A held stick turns the pair and flings. Turns check placement in
   substeps of at most 15 degrees and stop at the last legal heading. Even a blocked
   first step still throws. The 180-tick timeout reads the stick the same way.
5. Holding takes no input-driven movement or turns. Existing third-party pushbox
   behavior remains unchanged.

Release remains 22 ticks after the press. The first implementation uses toss
4 damage, 90 degrees, base/growth 34/10 and 3 recovery ticks after freeze. Fling
uses 8 damage, 35 degrees, 10/32 and 12 recovery ticks. Mayhem shoves deal zero
and retain ordinary source-damage freeze. Toss recovery started at 8 ticks.
Real installed-aerial tests required reducing it to 3 so Bonk connects before
0% stun ends. No character move, victim lock or launch formula changed for that goal.

Shared throw specifications own windup, release, reaction and recovery values.
`GrabThrow.WasReleased` supplies attributed observations to Tutorial and Ability
Lab. It excludes interruption and preserves a committed outcome through respawn.
`GrabThrow.TrySamplePose` supplies natural clip time to Unity. The thrower holds
its release pose through freeze and ends the clip with recovery. The victim uses
ordinary hit presentation after freeze. Detached throw recovery accepts the
ordinary attack buffer. Jump admission uses existing behavior.

Keep all existing numeric values. Phase 6 belongs to Clinching and 7 to CommandHold
in concurrent work. Add UpTossing 8, FlingReleased 9 and UpTossReleased 10.
Sibling interaction dispatch stays before universal grab validation. Reserve action
wire value 21. Do not change snapshot fields, codecs, format or protocol Version.
Record the semantic change under Unreleased.

## Consequences and verification

The scripted landing clock and hold-walk presentation disappear. No new clips,
pummel, mash-out, per-character throws, carrying, body-hit logic or wall bounce
are introduced. Natural clip truncation needs visual acceptance on the demo rigs.

[`GrabThrowMeasurementTests`](../../tests/Shared.Tests/GrabThrowMeasurementTests.cs)
asserts toss headroom and actual installed roster aerial contact before stun ends.
It records state readiness, accepted input, heights, apex, distances, landing and
ledge agency under neutral and escape DI. The edge fixture reports both 150% and
200% outcomes. These are first-pass measurements, not balance acceptance.
[`GrabThrowConformanceTests`](../../tests/Shared.Tests/GrabThrowConformanceTests.cs)
compares a real ordinary contact under nondefault tuning, percent, weight and DI.
Rollback tests replay windup, freeze, detached recovery, credit and outcomes.
Unity compilation and the presentation/scenario self-tests run in the parent
checkout after the local hunks merge.
