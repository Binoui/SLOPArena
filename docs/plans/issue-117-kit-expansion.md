# Issue #117 — Kit Expansion

> **Status: Superseded — 2026-08-22.** The earlier LMB/RMB + Q/E/R/F expansion was a dead design. It is retained only as historical implementation context; it is not the current kit contract.

## Current design contract

SLOPArena characters use two complementary attack layers:

| Layer | Inputs | Variants | Purpose |
|---|---|---|---|
| Normals | `1 / 2 / 3 / 4` | grounded + aerial for every input | Fundamental fighting-game moveset |
| Specials | `A / E / R / F` | character-specific ground/air behavior | Identity, unusual movement, utility, setup and major plays |

This gives every character eight normals and four specials.

`LMB` and `RMB` are camera controls. They are not attack slots, are not part of the kit power budget, and must not host automatic chains or charged attacks.

## Special-slot roles

| Input | Contract |
|---|---|
| `A` | Signature special. Establishes the character's unique mechanic and is usually useful in neutral. |
| `E` | Recovery-capable mobility special. It must be usable on stage as well as off stage; it is not a universal vertical Up-B. |
| `R` | Playmaking special. Creates engage, displacement, setup, conversion or other situations normals cannot easily create. |
| `F` | Power special. The most expressive high-impact move, governed by a long cooldown rather than an ultimate meter. |

See `docs/characters/character-kit-design-principles.md` for the full contract and `docs/characters/fightguy.md` for the active FightGuy design.

## FightGuy design target

FightGuy is the first kit to design against this contract:

### Normals

| Input | Grounded | Aerial | Role |
|---|---|---|---|
| `1` | Low Kick | Double Punch | Fast poke / spacing reset |
| `2` | Straight Punch | Floating Kick | Anti-air / air control |
| `3` | Sweeping Kick | High Kick | Launcher / vertical conversion |
| `4` | Double Kick | Air Smash | Punish / kill confirm |

### Specials

| Input | Move | Role |
|---|---|---|
| `A` | Ki Shot | Shared ground/air signature projectile; no mark, charge state or mandatory follow-up |
| `E` | Rising Dragon | Rising-punch recovery and anti-air; already implemented and accepted |
| `R` | Cyclone Kick | Committed engage with a short-stun follow-up; moderate damage/knockback, punishable on whiff |
| `F` | Dragon Beam | Long-cooldown, shared ground/air beam with fixed telegraphed startup; punishable |

Ki Shot is a pure projectile: no mark, charge state or mandatory follow-up. Open tuning questions remain in `docs/characters/fightguy.md`.

## Migration implications

The implementation currently contains legacy fields and ability classes for LMB/RMB attacks and the older Q/E/R/F layout. Those are implementation debt, not the design target. Before implementing the new kit:

1. Treat FightGuy and Kistu's eight normal concepts as accepted for now.
2. Finish special behavior and number design, starting with FightGuy.
3. Migrate the shared slot/data model so `1-4` route to grounded/aerial normals and `A/E/R/F` route to specials.
4. Remove obsolete automatic-chain and charged-mouse attack paths instead of preserving compatibility shims.
5. Update animation lookup, input routing, tests and character docs together.

Do not extend the old LMB/RMB attack model while designing or implementing the new kits.
