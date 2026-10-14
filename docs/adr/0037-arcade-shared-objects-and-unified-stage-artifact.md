# Arcade Shared objects and unified stage artifact

**Status:** Accepted — 2026-10-14

Solo Arcade needs real ball/target contacts, aim eligibility and challenge state,
which the current fighter-only combat path does not provide. The maintainer chose
focused Shared ball/target support and Arcade rules over a general world-object/
game-mode framework. Objects enter the existing authoritative combat/target path
with their own identity and state; Unity presents them.

The maintainer also chose expanding the baked stage format over keeping challenge
data in a separate cooked definition. Static geometry, objectives and settings
will form one normalized runtime artifact, with stage authoring, baking, binary
loading and validation updated together. This creates one stage contract for the
two solo challenges and incurs a broader migration than a sidecar.

## Consequences

- Preserve normal package-native fighter execution and a single mutating
  attack/projectile pass. Objects are not dummy fighters; authoritative object
  responses, goals and timing remain in Shared.
- Add explicit stage purpose/profile and marker validation. Keep normal PvP's
  four-spawn checks while admitting a clean one-player Arcade start.
- Version the expanded format and continue loading existing arena versions.
  Exact-byte freshness currently depends on writer layout; preserve unchanged
  plain-stage export/freshness behavior or compare versions explicitly.
- Keep Arcade-only stages out of normal PvP selection/admission. Existing menu
  allowlists alone are not an admission guarantee.
- Pin stage and fighter gameplay for an attempt. Record compatibility uses
  gameplay-relevant content/rules; exact cooked provenance is recorded separately
  because presentation-only changes must retain comparable records.
- These focused seams can support later volleyball and objectives, but the solo
  release does not supply online object replication or a general mode framework.

This decision fixes the foundation and artifact boundary. Binary field layout,
marker UI, concrete class names and numerical tuning remain implementation work.
Slopball is the first playable delivery slice; the first release also includes
FightGuy's Course. See the [feature brief](../design/arcade.md),
[implementation plan](../design/arcade-implementation.md) and
[resolved interview](../design/arcade-interview.md).
