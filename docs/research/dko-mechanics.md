# DKO Mechanics Reference

> Technical systems extracted from DKO (Divine Knockout) official Discord data.
> These are the fundamental combat mechanics of a 3D platform fighter.
> Status: ✅ implemented, 🚧 partial, ❌ not yet implemented.

---

## 1. Attack Distance
The range at which an attack connects — the hitbox length.
- Each ability defines its own attack distance
- Varies per character: short (Ama), medium, long
- Cross-ref: `combat-systems.md §6` — generous hitbox philosophy

**Status:** 🚧 AttackStage defines damage/knockback but no explicit range per stage. LungeForce covers some movement, but hitbox spawn distance is hardcoded in `ResolveAbilityStages`.

---

## 2. Warp Distance
How far the character's body (not the hitbox) moves during an attack.
- The "lunge" or "slide" that occurs during certain moves
- Separate from the hitbox — you can whiff the hitbox but still warp forward
- Cross-ref: `LungeForce` in AttackStage

**Status:** 🚧 LungeForce exists but is applied directly to velocity, not tracked as separate warp state.

---

## 3. Clash
If two attacks of the same type (Light vs Light, Heavy vs Heavy) connect within a small window, they cancel out and both players are pushed back.
- Same type only. Light vs Heavy → Heavy wins (Counter).
- Requires simultaneous hitbox activation

**Status:** ❌ Not implemented. No clash detection or simultaneous-hit resolution.

---

## 4. Counter (Attack Priority)
Three-tier priority system:
- **Ability** beats Heavy
- **Heavy** beats Light
- **Light** loses to Heavy and Ability
- When a higher-priority attack clashes with a lower-priority one, the higher one wins and the lower one is interrupted

**Status:** ❌ Not implemented. No priority tier on AttackStage.

---

## 5. Crit (Sweet Spot)
Some abilities have a sweet spot that deals bonus knockback or extra damage.
- Not every character has crits
- Thor's Mjolnir Attunement and Ama's Sun Secret Dash auto-crit on sweet spot hit
- Some Talents/Perks add crit conditions (e.g., Hercules Boulder crits airborne enemies)

**Status:** ❌ Not implemented. No sweet spot system on AttackStage or hitbox.

---

## 6. Crowd Control (CC)
Status effects that impair the enemy:

| Type | Effect | Example |
|------|--------|---------|
| **Fear** | Forces target to walk in random direction away from caster | Izanami ult |
| **Slow** | Impairs movement speed | Arthur E |
| **Stun** | Unable to do anything | Thor Q |
| **Toss-up** | Knocked up, stationary in air, can't act | Generic knockup |

**Status:** 🚧 StunTicks exist in AttackStage. Slow/fear/toss-up not yet defined.

---

## 7. Damage Reduction
State where the character takes reduced damage.
- Athena gets DR on landing from jump
- Some Talents grant DR (Hercules)
- Example: 20 damage → 11 damage with DR active

**Status:** ❌ Not implemented. No damage reduction modifier on CharacterState.

---

## 8. Dodge (i-frames)
A move that provides invincibility frames.
- Every character has a dodge
- Grants immunity to damage during the active window
- Cross-ref: `combat-systems.md §3` — dash invincibility, MovementComponent.StartDash

**Status:** 🚧 Dash has invincibility ticks (`InvincibilityTicks`), OnHit handler checks `IsInvincible`. No dedicated dodge state separate from dash.

---

## 9. End Lag (nlag/endlag)
The time after a move before you can act again.
- Missing a move leaves you vulnerable to punishment
- Controls the risk/reward of each ability
- Cross-ref: `SelfLockTicks` in AttackStage, `AnimLockTicks` in CharacterState

**Status:** ✅ SelfLockTicks → AnimLockTicks. AttackState waits for lock to expire before returning to movement.

---

## 10. Heal
Reduces damage percentage.
- Thanatos has heal on E and ult
- Heals reduce your % (lower knockback)
- Visible via a "+" indicator

**Status:** ❌ Not implemented. No heal mechanic in simulation or state.

---

## 11. Hitbox
The volumetric area where an attack registers.
- Can be a rectangular prism, sphere, cone, etc.
- Multiple hitboxes can be active per ability stage
- Cross-ref: `combat-systems.md §6`, `SpellResolver`, `Hitbox.cs`

**Status:** 🚧 Basic spherical hitbox spawning exists in `ResolveAbilityStages`. No shape variety (rect/prism/cone). No multi-hitbox-per-stage support.

---

## 12. Immunity
Complete immunity to damage, crowd control, and knockback.
- Visual feedback: character body lights up
- Lasts for the ability duration
- Cross-ref: dash invincibility, super armor

**Status:** 🚧 InvincibilityTicks covers damage immunity during dash. No comprehensive immunity state.

---

## 13. Interrupt
When hit during ability casting, the activation is cancelled.
- **Early cast** (before wind-up ends): full cooldown refund
- **Between cast and hitbox activation**: half cooldown refund
- Once the hitbox is active, the ability cannot be interrupted

**Status:** ❌ Not implemented. Abilities always resolve fully once ExecuteSlot is called.

---

## 14. Invisibility
State where the character is invisible to enemies (but visible to teammates).
- Izanami has invisibility on landing from jump
- Breaks on attacking or taking damage

**Status:** ❌ Not implemented. No invisibility state or rendering toggle.

---

## 15. Knockback
Light vs Heavy knockback:
- **Light knockback**: small push, won't KO even at high %
- **Heavy knockback**: large push, KOs at high %
- Knockback scales with damage percentage
- Cross-ref: `KnockbackForce`, `KnockbackUpward` in AttackStage, `DamagePercent` knockback scaling

**Status:** 🚧 KnockbackForce exists but no light/heavy distinction beyond raw force values. Knockback scales with %, but no tiered knockback types.

---

## 16. Knockback Resist
State where knockback taken is reduced.
- Separates knockback type (light/heavy) from the resist mechanic
- A character with knockback resist can survive heavy hits even at high %

**Status:** ❌ Not implemented. No knockback resist modifier.

---

## 17. Super Armor
State where you take damage but NO knockback (except from ultimates and environment).
- Ymir has super armor on E
- Ultimates bypass super armor
- Environment (void, stage hazards) bypass super armor
- Can still be stunned (CC ≠ knockback)

**Status:** ❌ Not implemented. No super armor state. Dash invincibility prevents both damage AND knockback (different from super armor which allows damage).

---

## 18. Vulnerability (%)
The damage percentage determines knockback distance.
- Higher % = further knockback
- Scales knockback multiplicatively
- Core comeback mechanic: low % = safe, high % = death zone
- Cross-ref: `DamagePercent` in CharacterState, knockback scaling in Simulation

**Status:** ✅ DamagePercent exists and knockback scales with it. The formula is in Simulation.ApplyKnockback.

---

## Summary: SLOPArena Coverage

| Implemented | Partial | Not implemented |
|---|---|---|
| End Lag (9) | Attack Distance (1) | Clash (3) |
| Vulnerability (18) | Warp Distance (2) | Counter/Priority (4) |
| | Dodge/i-frames (8) | Crit/Sweet Spot (5) |
| | Hitbox (11) | Fear/Slow in CC (6) |
| | Immunity (12) | Damage Reduction (7) |
| | Knockback (15) | Heal (10) |
| | | Interrupt (13) |
| | | Invisibility (14) |
| | | Knockback Resist (16) |
| | | Super Armor (17) |

---

## Implementation Priority (Suggested)

1. **Clash + Counter** (3, 4) — core combat interaction, changes feel dramatically
2. **Interrupt** (13) — adds depth to ability usage, punishment for bad timing
3. **Super Armor** (17) — key for heavy characters, adds commitment readability
4. **Crit/Sweet Spot** (5) — adds skill expression to abilities
5. **Damage Reduction + Knockback Resist** (7, 16) — adds defensive build variety
6. **CC: Fear, Slow, Toss-up** (6) — expands status effect options
7. **Heal** (10) — specific to certain character kits
8. **Invisibility** (14) — specific to certain character kits

---

## 19. Ability Input Conventions

DKO-specific patterns observed across the 13-character roster.

### Cancel

Aimed spells (skillshot ground indicators, reticle-aimed attacks) can be **cancelled with RMB**. This is a quick cancel — not the charge system. It lets the player abort a mis-aimed ability without committing the cooldown.

**Status:** ❌ Not implemented. No ability cancellation once ExecuteSlot begins.

### Keybind Conventions

| Slot | Role | CD | Note |
|------|------|----|------|
| **Q** | Aimed ability, often recovery | ~21s | Most aimed skillshots on Q. Recovery abilities also favor Q for muscle memory reability. |
| **E** | Mobility / utility | ~16s | Gap closers, self-buffs, movement tech. |
| **R** | Burst / spam tool | ~12s | Lowest CD of the 3 slots. Defines DPS pattern — spammable finisher. |
| **F** | Ultimate | 25-35s | Big impact, long cooldown. |

### Cancel Pattern

- Most aimed Q abilities cancel on RMB press
- No mana cost on cancel
- Cooldown is NOT consumed on cancel (the spell never fires)

**Implies:** Quick-cancel is a separate input pattern from the charge-hold system on RMB. The two systems don't overlap — aimed Q uses one input pattern (press Q → aim → RMB to cancel or LMB to confirm), RMB heavy uses another (hold to charge → release to fire).

### Cooldown Philosophy

Q has the highest CD (21s) because it's the most accessible key and often defines the character's identity — the ability you build around. R has the lowest (12s) because it's a repeatable threat, the bread-and-butter damage tool. E sits in the middle (16s) — powerful enough to be impactful, gated enough to punish misuse.
