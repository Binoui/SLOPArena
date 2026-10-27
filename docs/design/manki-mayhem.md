# Manki Mayhem feature brief

**Status:** Direction selected 2026-10-27; not an implementation specification. Only the
stage shell exists ([Pirate Broadcast](stages/manki_mayhem.design.md)). Items marked
*proposal* were shown to the maintainer but not confirmed. No tuning value is selected:
round counts, scores, timings and sizes below are "not selected" or provisional. The
architecture is proposed in [ADR-0044](../adr/0044-manki-mayhem-hazard-director.md); the
[implementation plan](manki-mayhem-implementation.md) orders the work and lists the open
decisions.

Manki runs a pirate TV game show out of a warehouse, and tonight's contestants are
whoever is standing on his stage. Each round he shouts a **call card** ("SCATTER!",
"JUMP!", "BREAK OUT!") and throws one pattern at the deck. Two to four fighters dodge it.
It is **competitive survival**: you score by still being on the stage when the round
ends, not by fighting. The round mockups are in `tmp/manki-mayhem-board/index.html`
(ignored, local).

## Decisions (2026-10-27)

| Question | Decision |
| --- | --- |
| Loop | Each round Manki shouts a call card and throws one pattern. The cards come from a shuffled WarioWare-style deck, faster as the Match goes. |
| Match shape | Competitive survival. Survivors score each round. Not co-op, not a fight. |
| Hazard hits | Like normal attacks: damage percent, Knockback and Hitstun. Recovery matters: launched but back on the deck still counts as surviving. |
| Friendly fire | A Room rule: Off, **Shoves** (default) or On. Shoves push other fighters but deal no damage. |
| First slice | SCATTER!, JUMP! and BREAK OUT!, the round loop, scoring, call cards, the big animated Manki in the booth and a fixed show camera. |
| Where | Online Rooms from the start (ARCADE tab), 2–4 fighters, server-authoritative and rollback-safe, admitted by Master like Slopball. |
| Stage | `manki_mayhem` is Mayhem-only: it gains an Arcade section and leaves stock PvP. |

## The show

The match rhythm is **call → dodge → recover → score → next call, a bit faster**.

| Phase | What happens |
| --- | --- |
| Call | Manki shouts the card. It fills the CRT walls and the HUD banner, and the round's telegraphs appear on the deck. |
| Pattern | The hazards fly. Getting hit launches you like any hit. |
| Settle | Launched fighters finish their recovery. The round ends once everyone still in is back on the deck or knocked out. |
| Score | Survivors score; stamps pop over their heads. |
| Break | A short pause. Knocked-out fighters come back, walls sink away, the next card is drawn. |

**The deck.** Cards are drawn without repeats until each one has been played, then
reshuffled. Every pass through the deck is faster: shorter warnings, denser volleys,
patterns in pairs. Mixing two cards in one round comes later.

## Scoring and rules

- **Survive the round, score.** Every fighter not knocked out during the round scores
  (provisional: one point). Getting hit and recovering still counts.
- Knocking someone out never scores for the attacker, and Manki never takes KO credit.
- The Match ends after a fixed number of rounds (not selected); the most points wins.
  Ties share the win, damage percent carries over between rounds until a knockout, and
  every round starts from the four spawns (decided 2026-10-27).
- A disconnected player is withdrawn: they keep their score and leave the deck.
- **Last one standing** (first slice, decided 2026-10-27). When only one fighter is left in a round, the round
  ends once the current throw lands and that fighter gets a bonus.
- **Wait in the booth** (decided 2026-10-27). Knocked-out fighters sit in the booth with Manki for
  the rest of that round and return at the next call. The alternative is to respawn on
  the deck at once, able to shove but unable to score that round.
- *Proposal:* **Manki's favourite.** The points leader wears a target and Manki aims a
  bit more fire at them. This is the catch-up rule.

### Friendly fire

| Setting | Fighter against fighter |
| --- | --- |
| Off | Attacks and grabs pass through other fighters. Pure dodging. |
| **Shoves** (default) | Hits push with their normal Knockback and Hitstun at the victim's current percent, but add no damage. Shove someone into a falling bomb or off the edge. |
| On | Normal hits. |

Manki's hazards hit everyone under every setting. Anyone can hit any wall or crate.

## Rounds

### First slice

**SCATTER!** · *sticky bombs · don't be the one holding it*

- Manki lobs a volley of Round Bombs. Each landing spot shows a ring on the deck before
  the bomb arrives.
- A bomb that touches a fighter sticks to them, shows its fuse ("STUCK! 3…") and then
  explodes, hitting the carrier and anyone close. A bomb that reaches the floor explodes
  there.
- With friendly fire Off it is pure dodging. With Shoves you can bump the carrier toward
  others or push someone under a falling bomb.
- Faster passes: more bombs per volley, shorter rings, shorter fuses.

**JUMP!** · *Double Fist Slam · hop the shockwaves*

- Manki slams his fists and shockwave rings roll outward across the deck from the slam
  point, which flashes first. A ring hits grounded fighters it reaches; jump it.
- Later in the Match the slams come in pairs with a short gap, so the second jump needs
  timing and mashing fails.

**BREAK OUT!** · *smash a wall · the bomb is coming* (the maintainer's idea)

- Four walls pop up around every fighter and a bomb is already in the air toward each
  cage. Break a wall and get out of the blast.
- One wall per cage is cracked and breaks faster; the wall facing away from the bomb is
  the safer exit. Read the cage, pick a wall.
- It is the only round where you attack, so each kit has its own fastest answer: Bonk's
  plank, Manki's Head Slam, Wibou's dash.
- Walls block movement and take hits from any fighter's attacks and projectiles. The
  blast wrecks whatever is left of the cage. Whether a fighter may jump or fly out over
  the walls: only with kit mobility, not a plain double jump (decided 2026-10-27).

### Later slices

| Card | Pattern | New piece it needs |
| --- | --- | --- |
| **TAKE COVER!** · *homing Bazooka barrage · hide behind the crates* | Each fighter gets a reticle, then homing rockets come in. Crates block them, but every rocket chews up its crate, so cover runs out and you have to move. | Crates (the BREAK OUT! walls used as cover) and host-owned homing rockets |
| **WEAVE!** · *spinning axes · read the lanes* | Axes curve across the deck on lanes shown for a moment before they fly. Boss props, not Manki's kit: barrels, tyres or a wrecking ball can follow. | Scripted lane motion |
| **GET HIM!** · *Manki is dizzy · hit him!* | The reversal: Manki falls dizzy onto the deck and everyone wails on him. Most hits wins the round. With Shoves or On the scrum gets messy. | Manki as a hittable object with a hit count per fighter |
| Mixing | Two cards at once late in a Match, such as BREAK OUT! while SCATTER! bombs fall. | Overlapping patterns in the director |

## Presentation

### Manki, the host

- A big animated Manki, about three to four times fighter height, stands in the booth's
  host bay at `MankiHostAnchor` (0, 6.5, 41.5). The bay is 9 × 6.5 m with 8 m of headroom.
- He is presentation only. The simulation decides every throw and its timing; he plays
  shout, wind-up, throw, slam and laugh reactions on its events. A wind-up leads each
  throw so his arm and the bomb agree.
- Between rounds he hypes the crowd; he points and laughs at knockouts.
- The first version scales Manki's own model and existing clips. Dedicated host
  reactions follow once the loop plays well.

### Call cards

- Each round opens on its card: a big title and a one-line subtitle, as listed above.
  It fills both large CRT screens beside the booth, the small CRT stacks flip to static
  in the card's colour, and the HUD shows the same banner. Manki shouts it.
- Copy is pirate broadcast: caps, one exclamation mark, short subtitles.

### HUD and stamps

- **Top right:** one row per player with portrait, name, player colour and score. Rows
  of knocked-out players dim and read OUT.
- **Top left:** the round counter and the current card.
- **Stamps** over fighters: "+1" at round end, "STUCK! 3…" on a bomb carrier,
  "LAUNCHED · RECOVER!", "LAST ONE STANDING!" if that proposal is selected.
- **Results:** standings by score, rounds survived and bonuses.

### Telegraphs

Landing rings fill as impact nears; slam points flash before rings roll; walls pop up,
crack per hit, shatter and sink between rounds; a stuck bomb blinks faster as its fuse
runs down. All of them are drawn from the predicted simulation state. The deck stays the
calm hero and the telegraphs are the loudest thing on it.

### Show camera

- One fixed view for everyone: yaw 0, looking north at the booth, at a lower pitch than
  the default so the booth and the giant host stay in frame. The stage art pass found
  that 15° crops the host and 8° frames him; the final pitch, height and field of view
  are not selected and must keep floor rings readable.
- No orbit. Stick directions never change: up is toward the booth. Aimed moves use the
  stick and lock-on, as in couch play ([ADR-0041](../adr/0041-couch-play-in-solo.md),
  [ADR-0043](../adr/0043-steady-camera-default.md)).
- A fighter launched off screen gets the existing edge bubble.

### Knocked out

Knocked-out players keep the same show camera, so they still watch the round. Their row
reads OUT · BACK NEXT ROUND. *Proposal:* their fighter appears small in the booth beside
Manki, cheering or sulking, until the next call. A later idea lets them pick where the
next bomb drops.

## Where it's played

- **Online Rooms.** The ARCADE tab gets a Manki Mayhem card for two to four players.
  Character Select is a free pick: any admitted fighter, mirrors allowed. The friendly
  fire rule is set by the leader before the Match and stays fixed during it.
- **Local Play with CPUs** (*proposal*). The Shared mode and Solo's four seats make an
  offline version cheap. CPUs need only to step out of landing rings, jump rings and hit
  a wall; they don't need to be good.

## Character moments

These come from the kits as they are; none is a rule.

- **Bonk:** the plank through a BREAK OUT! wall; Shoves with a greatsword.
- **Manki:** Head Slam through a wall; the Jetpack as a JUMP! escape.
- **Wibou:** dash out of the cage the moment the wall breaks.
- **Bashie:** Hose Down is already a pure push, so under Shoves she is the bouncer.

## Open choices

The [implementation plan](manki-mayhem-implementation.md#open-decisions-for-the-maintainer)
lists them with options and a recommended default: Match length and ties, damage and
positions between rounds, the three proposals, Shield against hazards, escaping
BREAK OUT! over the top, where the friendly fire rule is set, the Local Play entry and
the show camera toggle.

## Out of scope for now

More than four players, teams, CPUs in online Rooms, leaderboards, round mixing,
knocked-out players acting on the round, and passing a stuck bomb on a hit (Hot Potato).

## References

- [Stage design](stages/manki_mayhem.design.md) and
  [production contract](stages/manki_mayhem.md): the deck, the booth, the CRT walls and
  the host anchor.
- [Online Arcade brief](online-arcade.md): the Room flow and formats this mode joins.
- [Game mode exploration](game-modes.md#manki-mayhem): where the mode sits among the
  multiplayer directions.
