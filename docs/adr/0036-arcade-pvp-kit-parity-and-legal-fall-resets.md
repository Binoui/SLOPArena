# Arcade PvP kit parity and legal fall resets

**Status:** Accepted — 2026-10-14

Solo Arcade should teach and reward the fighters' real movement and kits. Its
Character Courses will use normal PvP movement, move commitments, cooldowns,
charges and kit effects. Course falls respawn at a checkpoint while the attempt
timer continues; they use normal PvP fighter respawn behavior, including damage
reset and resource/cooldown refresh. Intentional falls are legal route choices,
and there is no added time penalty.

## Trade-off

The maintainer chose normal kit rules over shortened Arcade cooldowns or refill
stations, and accepted cooldown-refresh routes over an added fall penalty.
This preserves mechanic transfer and permits creative record attempts. Course
layout and personal records depend on that contract, so changing it later needs
an explicit rules revision and consideration of existing records.

## Consequences

- Course completion requires every mandatory target and the finish. It does not
  require visiting every checkpoint or an authored move-specific solution; any
  valid solution counts. Checkpoints provide optional respawn saves.
- Ordinary launched-projectile behavior survives a checkpoint death as it does
  in PvP. A complete attempt retry remains a separate reset boundary.
- Arcade goal/scoring and ball/target behavior have their own rules. This record
  does not make those objects fighters or settle their collision/aim eligibility.
- Wibou Rising Slash's charge-refund removal is a separate intended PvP change.
  It is not implemented by this ADR; Arcade follows the shared kit behavior.

This records the gameplay contract selected during the
[Arcade design interview](../design/arcade-interview.md). The later object and
stage architecture choice is recorded in
[ADR-0037](0037-arcade-shared-objects-and-unified-stage-artifact.md);
the [implementation plan](../design/arcade-implementation.md) covers record storage.
