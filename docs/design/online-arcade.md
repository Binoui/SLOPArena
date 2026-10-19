# Online Arcade feature brief

**Status:** Direction selected 2026-10-19; not an implementation specification.
Co-op Slopball online is built (slice 2) and awaits a two-client playtest.
The maintainer named online Character Courses and volleyball as the most important
modes going forward, and moved them ahead of upgrade rounds. The Room flow, the four
decisions below and the format list are selected. Everything marked *proposal* is a
rule to try in play, not a commitment. No score target, miss count or other value has
been selected. Technical options for approval live in the
[implementation options](online-arcade-implementation.md).

Online Arcade brings the two [Solo Arcade](arcade.md) challenges into online Rooms:
Slopball on the Night Court and the Character Courses. It reuses their stages, Shared
objects and presentation. The modes stay friends-first, like Solo Arcade: quick, readable
rounds over rankings.

## Decisions (2026-10-19)

| Question | Decision |
| --- | --- |
| Room flow | The Room picks what to play first, then fighters. Stage Select gains PVP and ARCADE tabs, and Character Select follows the pick. |
| Who plays a course | Every player plays the course's own fighter, for now. Players pick a colour instead of a fighter. |
| Hits across the volleyball net | Anything goes by default: melee at the net and projectiles across it can hit rivals. Ball only stays available as a Room rule. |
| Who serves | Players serve in Versus. The Launcher serves in Co-op. |
| Priority | Online Slopball and Character Courses come before [upgrade rounds](game-modes.md#upgrade-rounds). |

## Room flow

```text
Today:    Room → Character Select → Stage Select → Match → Results → Room
Selected: Room → Stage Select (PVP | ARCADE) → Character Select → Match → Results → Room
```

The pick decides who can play what: a course decides the fighter, and a volleyball
format decides the camps. Smash Ultimate also asks for the stage before fighters.

**Stage Select.** The leader chooses and confirms; members see the same page.

- **PVP tab:** today's stage cards and stock fight rules. Teams and upgrade rounds can
  appear here later.
- **ARCADE tab:** a Slopball card and a Character Courses card. Slopball shows its
  formats (Versus 1v1, Versus 2v2, Co-op vs the Launcher) and their rules. Courses show
  one card per course with its fighter, then the Race and Co-op formats.
- A format that doesn't fit the Room's size is shown but unavailable: Versus 1v1 needs
  two players and Versus 2v2 needs four.
- *Proposal:* members can tap a card to suggest it, which puts their pip on the card.
  The leader still confirms.

**Character Select** takes one of three shapes:

| Shape | Used by | Players choose |
| --- | --- | --- |
| Free pick | PvP, Slopball Co-op | Any admitted fighter; mirrors allowed |
| Camps | Slopball Versus | A camp, Teal or Clay after the court's halves, then any fighter. Members switch camps; the leader can shuffle. |
| Locked | Character Courses | A colour; the course sets the fighter |

Mirrors and camps need **player colours**: a ring, outline or name tag that tells four
Mankis, or two camps, apart. None exist yet.

*Proposal:* **Again** on Results returns the Room to Character Select with the same
pick, and everyone locks in again. Again skips Lobby, so new members can join only when
the Room goes back to Lobby.

## Volleyball (Slopball online)

All formats use the Night Court. Its teal and terracotta halves become the **Teal** and
**Clay** camps.

### Versus: 1v1 and 2v2

The match rhythm is **serve → receive → set, bank or spike → floor contact → point**.

- The net splits the camps. Every fighter stays in its own half, as the solo fighter
  does today.
- Ball contact with a camp's floor scores a point for the other camp. The first camp to
  a score target *(not selected)* wins.
- The ball's percent builds through a rally, so it flies faster, and resets when a point
  is scored.
- Players serve: the ball hangs over the server's spot until the server hits it, so
  each serve is a move choice. The serve order is not selected.
- Anything goes across the net. Melee can trade at the net and projectiles can hit the
  receiver; Ball only switches this off.
- *Proposal:* in 2v2 teammates can't hurt each other. A teammate's hit **boosts**
  instead, popping you up without damage, so a setter can launch the spiker.

Open choices: whether a fighter's own percent resets each point, serve order, and what
a hit does to a fighter inside the enclosed court.

### Co-op vs the Launcher: 2–4 players

Today's Slopball with friends: **serve → anyone receives → pass → return → combo**.

- Everyone shares the teal half. The Launcher serves, with one shared combo, rally heat
  and the same special serves.
- A run ends after a few misses, so the Room reaches Results: team returns and best
  combo. Slice 2 plays three misses provisionally until play picks the count; assists
  remain a proposal.
- *Proposal:* keep four players busy with **called serves** (the Launcher's face turns
  to one player and their ring says YOURS) or **multiball** once the rally is hot.
- *Proposal:* **assists**: a return touched by two teammates gets its own callout and
  is worth more.

### Ideas for later

- **Launcher Boss** (3 players): one player runs the machine, aiming serves and spending
  heat on zingers, moonballs and curveballs against two receivers. Roles rotate each run.
  It gives a three-player Room a versus option without 2v1.
- **Four Square** (4 players, free-for-all): a cross-shaped net makes four camps; the
  ball landing in your square costs you. It returns to the original box-court direction
  in [game modes](game-modes.md#volleyball) and needs its own court floor.

### Character moments

These come from the kits as they are; none is a rule.

- **FightGuy:** Ki Shot as a flat, fast serve; Rising Dragon as the block at the net.
- **Manki:** the Bazooka serve; with anything goes, a Round Bomb lands in the other camp
  just before the ball.
- **Wibou:** a Dash Slash spike from the back line; Skyfall as the slam-down spike off a
  teammate's boost.
- **Bonk:** the greatsword BONK! serve; Blade Storm as a spinning wall at the net.

## Character Courses online

Everyone plays the course's own fighter, the kit the course was built for. Both formats
take two to four players.

### Race

- One course, one fighter, one countdown. The first to clear the targets and reach the
  finish wins.
- *Proposal:* each racer breaks **their own set** of targets and sees only theirs, so a
  leader can't strip the course from the pack.
- Rivals share the world and can be hit and bombed. A knocked-off rival respawns at their
  own checkpoint.
- The HUD shows rivals' target pips. Results show finish order and times.

### Co-op

- *Proposal:* one shared set of targets: broken by anyone, broken for all. One team
  timer.
- *Proposal:* the run completes when every target is down and one fighter reaches the
  finish, so the team splits up to cover both branches.
- *Proposal:* team bests are saved locally per party size.
- *Wild variant, Scramble:* one shared set where each break scores for whoever lands
  it, and the finish opens after the last target.

### Course moments

- **Kaboom Quarry:** Round Bombs knock rivals off the crate islands, since explosions
  already hit every fighter. One TNT chain can catch the whole pack, and the Jetpack
  shortcut becomes a mid-air duel.
- **Fight Night Pier:** in Co-op the eight-hit bonus target becomes a tag-team beating;
  in a Race everyone pounds their own.
- **Sanctuary:** in Co-op each lantern adds a note, so the team plays the section's
  melody together.

## Open choices

- Score target, miss count and serve order, all unselected.
- Whether online Race times count as personal bests, given rival interference.
- Three-player Rooms can play Slopball Co-op and both course formats. A three-player
  volleyball versus waits for Launcher Boss or a 2v1 decision.
- Whether course fighters unlock beyond the course's own once more kits have been tried
  on each course.

## Out of scope for now

More than four players, matchmaking or ranked play, leaderboards, Four Square's court,
online Tutorial and a free fighter pick on courses.

## References

- [Solo Arcade feature brief](arcade.md): the Slopball and course rules these formats
  reuse.
- [Game mode exploration](game-modes.md): the wider multiplayer direction and delivery
  sequence.
- [Implementation options](online-arcade-implementation.md): Room contract, Shared
  formats, rollback coverage and client hosting, with recommendations.
- [Slopball Court](stages/slopball.design.md), [Kaboom Quarry](stages/manki_quarry.design.md),
  [Fight Night Pier](stages/fightguy_pier.design.md).
