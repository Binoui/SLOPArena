# Character Moveset Structure

## Overview

SLOPArena characters use two complementary layers of attacks:

* **6 normals** on three buttons, with grounded and aerial variants.
* **4 specials** on `A / E / R / F`.

Every fighter also has the universal homing dash and grab; they are engine moves, not kit slots.

Normals provide the character's fundamental fighting-game moveset.

Specials provide character identity, unusual movement, utility, setup, and larger gameplay moments.

A character should be able to participate meaningfully in combat using normals alone. Specials are not intended to replace basic attacks; they are where the character is allowed to bend the normal rules of combat.

---

## Normals — three buttons

The three normals have fixed roles ([ADR-0042](../adr/0042-controller-layout-three-normals-homing-dash.md)):

| Button | Canonical slot | Role |
|---|---|---|
| 1 (controller X) | `1` | jab / all-rounder |
| 2 (controller Y) | `3` | upward |
| 3 (controller B) | `4` | the big one |

The slot ids `3` and `4` predate the merge of the old normals 1 and 2; slot `2` no longer exists.

Each input has:

* one grounded attack;
* one aerial attack.

This gives every character six baseline attacks.

Normals should generally be:

* immediately understandable;
* relatively low-commitment;
* usable frequently;
* driven primarily by hitboxes, knockback, startup, recovery and positioning;
* suitable for neutral, combos, juggling, edgeguarding and kill confirms.

Examples include:

* jabs;
* kicks;
* sweeps;
* launchers;
* anti-airs;
* aerial pokes;
* spikes;
* heavier finishers.

Normals can still be powerful or highly characterful, but they should normally stay within the standard combat system.

### Guideline

> Normals interact with the combat system. Specials are allowed to bend it.

A move probably belongs in the special kit if it involves mechanics such as:

* projectiles with unusual behaviour;
* persistent objects;
* grapples;
* teleports;
* marks or debuffs;
* major movement effects;
* temporary rule changes;
* stage interaction;
* unusual defensive mechanics.

---

# Specials

## `A` — Signature Special

`A` is the most direct expression of the character's unique combat mechanic.

It is usually useful in neutral and should help establish the character's identity quickly.

Possible roles include:

* projectile;
* charge attack;
* stance;
* trap;
* summon;
* mark;
* resource interaction;
* unusual defensive option.

`A` does not need to be the character's strongest move.

It should ideally be a move that makes the character immediately feel different from the rest of the roster.

---

## `E` — Recovery / Mobility Special

`E` is the character's primary recovery-capable move.

The goal is **input consistency without mechanical homogenization**.

Pressing `E` should generally mean:

> "Use my character's special movement/recovery tool."

It does **not** mean that every character receives the equivalent of a traditional vertical Up-B.

Examples:

* rising attack;
* grapple;
* teleport;
* rocket jump;
* air dash;
* hover;
* temporary platform;
* movement toward a placed object.

An `E` can also have offensive or utility applications.

### Design rule

> `E` should be recovery-capable, not recovery-only.

This allows recovery mechanics to become a meaningful part of character identity while keeping the control scheme predictable.

---

## `R` — Playmaking Special

`R` is generally the character's strongest regularly available playmaking tool.

It should create situations that normals alone cannot easily create.

Typical functions include:

* engage;
* displacement;
* combo extension;
* area control;
* strong defensive utility;
* setup;
* high-commitment kill option;
* interaction with the character's signature mechanic.

`R` can have a noticeably longer cooldown than `A` or `E`, but should still appear multiple times during normal gameplay.

---

## `F` — Power Special

`F` is the character's largest or most expressive ability.

It is **not an ultimate** in the MOBA sense.

SLOPArena does not currently use:

* ultimate charge;
* damage-generated meter;
* once-per-match supers;
* comeback meter;
* an ultimate economy shared across the roster.

Instead, `F` is governed primarily through a **longer cooldown**.

The cooldown should be long enough that using `F` is a meaningful decision, but short enough that players expect to use it more than once during a normal match.

As a starting design space, this likely means cooldowns closer to roughly **15–30 seconds** than 60+ second MOBA ultimates.

Exact values remain character-specific and should be determined through playtesting.

### F should not simply mean "buff"

Temporary steroids, rage states and generic stat increases are valid mechanics, but they should not become the default solution for `F`.

A good `F` should ideally express the character fantasy in a way that changes the immediate match situation.

Possible designs include:

* a large movement technique;
* a powerful command grab;
* temporary terrain or object creation;
* a large projectile with unusual properties;
* a character-specific transformation of an existing mechanic;
* a strong repositioning tool;
* a persistent threat;
* a special interaction with marked or prepared opponents;
* a high-risk attack with unusual payoff.

The emphasis is on **distinctive gameplay**, not simply higher numbers.

---

# Cooldown Hierarchy

Cooldowns are part of the identity of specials rather than a universal fixed rule.

A rough expected hierarchy is:

`A` — short / frequently available
`E` — short-to-medium, with recovery availability considered carefully
`R` — medium
`F` — long

This hierarchy can be broken when a character concept requires it.

In particular, recovery design must account for the possibility that `E` is unavailable while the character is offstage. Some characters may therefore require:

* short E cooldowns;
* cooldown refresh rules;
* multiple charges;
* partial recovery options elsewhere in the kit.

These should be solved per character rather than by introducing a universal recovery system.

---

# Kit Design Process

A new character can be designed in the following order:

1. **Normals — How does this character fight?**
   Establish the six basic attacks and make sure the character functions without relying constantly on abilities.

2. **A — What makes this character unusual?**
   Introduce the central gimmick or signature interaction.

3. **E — How does this character move and recover?**
   Turn recovery into part of the character fantasy.

4. **R — What creates the character's big plays?**
   Add an ability capable of strongly changing positioning, pressure or combo situations.

5. **F — What is the most expressive version of the character concept?**
   Add a high-impact ability with a meaningful cooldown, without requiring an ultimate meter.

The objective is consistent controls across the roster while allowing the actual mechanics behind those controls to vary dramatically.

---

## Related References

* `docs/design/melee-feel-guidelines.md` — numeric tuning rules for normals and hit response.
* `docs/characters/fightguy.md` — the active FightGuy kit design.
* `docs/characters/adding-a-new-character.md` — implementation pipeline after kit design is settled.
