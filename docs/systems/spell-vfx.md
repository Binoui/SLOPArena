# Spell Visual Effects

This page was an early placeholder for a proposed `SpellVFXManager`. That
proposal is superseded by the package presentation pipeline; character ability
effects are implemented through catalog bindings and simulation presentation
events.

Use [VFX — Combat Presentation](vfx-particles.md#adding-character-specific-vfx)
for the current architecture and the
[character VFX pass workflow](../../skills/sloparena-character-vfx-pass/SKILL.md)
to author effects. `TimelinePresentationDispatcher` presents timeline cues,
the admitted character catalog's presentation profile supplies projectile styles,
sound pools and respawn tint, and `CombatFeedback` presents accepted hits. Author
these values in `CharacterAssetCatalog.asset`; cooking carries them through binding
schema 2. These effects do not decide gameplay results.

The [July 2026 PvP roadmap](../plans/2026-07-03-pvp-roadmap.md) preserves the
historical plan.
