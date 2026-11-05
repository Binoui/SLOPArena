# Manki Mayhem feature brief

**Status:** Direction selected 2026-11-04. The maintainer approved the microgame redesign
("this sounds really good. lets go for it") with every recommended pick below. It replaces
the three-card survival show selected on 2026-10-27. That show is built and stays playable
until the first channel slice replaces it. No tuning value is selected: channel lengths,
act sizes, ratings and speeds below are "not selected" or provisional.
[ADR-0061](../adr/0061-manki-mayhem-channels.md) records the architecture, and the
[implementation plan](manki-mayhem-implementation.md) orders the work.

Manki runs a pirate TV game show out of a warehouse, and tonight's contestants are
whoever is standing on his stage. The show is a run of **channels**. Each channel is one
word, the one control it needs, a few seconds on the deck, and a pass or fail for every
contestant. Two to four fighters play every channel at once.

The mode is the door into SLOPArena for people who never played Smash or a fighting
game. A mistake costs one channel, never the show. The moves arrive one channel at a
time, and the guest channels introduce the cast before anyone plays Versus.

## Decisions (2026-11-04)

| Question | Decision |
| --- | --- |
| Loop | Each channel opens with a call, plays for a few seconds and ends on its own clock with a verdict for every contestant. Then the next channel. |
| How you win | **Ratings Race.** A fixed show in three acts. Each act plays channels from a shuffled deck and ends with a boss channel that pays more. Every pass earns ratings. Nobody is ever out. The highest ratings win, and ties share the win. |
| Speed | Each act is faster than the last: shorter calls, quicker hazards. A SPEED UP! beat opens acts two and three. |
| Controls | **Verb of the channel.** Move, jump and hit (any of the three normals) always work. A channel can switch on one more real move: shield, grab, specials or Beeline. Every other control is off. The call shows the buttons. |
| Getting hit | **Splat.** A hazard hit fails the channel on the spot with a comic splat. The fighter stays on the deck until the next channel. There is no launch and no damage percent. Falling off the deck also fails the channel. This replaces the 2026-10-27 rule that hits launch and recovery matters. |
| Friendly fire | Off or **Shoves** (default). A shove pushes with no damage and never fails anyone by itself. "On" no longer exists in Mayhem. |
| First slice | DODGE!, JUMP!, BREAK OUT!, SPOT!, NO RUNNING! (guest Bashie), BREAK THE BOARDS! (guest FightGuy) and the boss PRIME TIME!, offline against CPUs and couch players. |
| Where | Local Play first. Online Rooms follow with one coordinated release, as ADR-0044 did. |
| Stage | Unchanged: the [Pirate Broadcast](stages/manki_mayhem.design.md) deck, booth and CRT walls. |

## Why the show changed

The 2026-10-27 show asks for fighting-game skills. Read from its rules and code, a
newcomer meets five problems:

- Hazards launch like real hits, and a contestant scores only by recovering to the deck.
- A knockout benches the fighter until the next call, and a round lasts until the last
  launched fighter lands.
- Damage percent carries over, so every hit makes the next launch longer.
- Three cards repeat six times each, eighteen rounds in all.
- The fastest way out of BREAK OUT! depends on knowing your fighter's kit.

Nobody has watched a newcomer play Mayhem yet. The first playtest with players who don't
play fighting games tests these five points.

## The show

The rhythm is **call, play, verdict, next channel**, a little faster each act.

| Phase | What happens |
| --- | --- |
| Call | The CRT walls flip through static to the channel's title. Manki shouts it from the booth. The HUD shows the word and the buttons it needs, and the telegraphs appear on the deck. |
| Play | A few seconds. Everyone plays at once on the same deck. |
| Verdict | Every contestant gets **ON AIR** or **CANCELLED**, with the reason: TOASTED, SHOVED OUT, WRITTEN UP. Each pass earns ratings. |
| Break | Splatted fighters get up, props sink, and the next channel is drawn. |

**Acts.** Each act draws its channels from the seed without repeats and ends with a boss
channel. Act sizes are not selected. A contestant who fails a channel stands splatted on
the deck until the next call, so nobody waits longer than one channel.

## Scoring and rules

- Pass a channel, earn ratings (provisional: one).
- A boss channel pays more (provisional: double).
- The show ends after the third act's boss. The highest ratings win, and ties share the win.
- Every channel starts from the four spawns.
- A disconnected player is withdrawn. They keep their ratings and leave the deck.
- Knocking someone off the deck never scores for the pusher, and Manki never takes credit.
- *Proposal:* **Manki's favourite.** The leader wears a target and Manki aims a bit more
  fire at them. Decide after the first playtests.

### Controls

Move, jump and hit always work. On a controller that is the left stick, **A** and any of
**X**, **Y** and **B**. On a keyboard it is the movement keys, **Space** and **1** to **3**.
A channel can switch on one more move, and its call shows that button too:

| Move | Controller | Keyboard | Channel that teaches it |
| --- | --- | --- | --- |
| Shield | **RT** | **Left Shift** | BLOCK! |
| Grab | **LB** | **C** | TOSS IT! |
| Specials | right stick | **Q**, **E**, **R**, **F** | SHOW OFF! |
| Beeline | **RB** | **4** | BEELINE! |

The simulation enforces the gate, so a CPU, a couch pad and an online peer all play
under the same rule.

### Friendly fire

| Setting | Fighter against fighter |
| --- | --- |
| Off | Attacks and grabs pass through other fighters. |
| **Shoves** (default) | A hit becomes a short horizontal push with no damage and no launch. Shove someone out of a spotlight or into a falling bomb. A shove never fails anyone by itself. |

Manki's hazards hit everyone under both settings. Anyone can hit any cage wall, but only
your own hits break your own stack of boards.

## Channels

### First slice

**DODGE!** · *bombs land on the rings · don't be there*

- Manki lobs a volley of bombs. A ring on the deck shows where each one lands.
- A bomb that hits a contestant splats them: TOASTED.
- Pass: not hit by the time the channel ends.

**JUMP!** · *Manki slams · hop the shockwaves*

- Manki slams his fists and shockwave rings roll out across the deck. A ring catches
  grounded fighters only.
- Later acts send the rings in pairs, so the second jump needs timing.
- Pass: not hit by a ring.

**BREAK OUT!** · *caged · smash the cracked wall and run*

- A cage pops up around every contestant and a bomb is already in the air toward it.
- One wall per cage is cracked. Every fighter breaks it with a few hits of any normal,
  so the kit no longer decides the channel.
- Pass: outside the blast when it goes off.

**SPOT!** · *be in a spotlight at the buzzer*

- Spotlights appear on the deck, one fewer than contestants.
- Each spotlight holds one contestant. If two stand in one at the buzzer, the one nearer
  its centre keeps it. Shoving is allowed, so it plays like musical chairs.
- Pass: holding a spotlight at the buzzer.

**NO RUNNING!** · *guest star Bashie · move while she looks and you're written up*

- Bashie blows her whistle and turns around. While she watches, anyone who moves fails:
  WRITTEN UP. She never follows her own rule.
- A shove that moves someone while she watches does not fail them.
- Pass: never written up during the channel.

**BREAK THE BOARDS!** · *guest star FightGuy · smash through your own stack*

- A stack of boards stands in front of every contestant.
- Pass: your own stack broken before the gong. FightGuy poses either way.

**PRIME TIME!** · *boss · two channels at once*

- At top speed, BREAK OUT! cages rise while DODGE! bombs fall.
- Pass: both objectives met. A boss pays more.

### Later

| Channel | Pattern | Teaches or guest |
| --- | --- | --- |
| HOT POTATO! | A lit bomb sticks to whoever touches it. Tag someone to pass it on. The holder at the bang fails. | |
| HIDE! | Manki's bazooka sweeps the deck from one side. Get behind a crate. Needs hazards that stop at crates. | |
| SUMO! | The light shrinks to one small ring. Be inside at the buzzer. | |
| BLOCK! | Bombs from every side at once. Hold shield. | Shield |
| DOUBLE JUMP! | One tall shockwave. Jump, then jump again. | Double jump |
| SHOW OFF! | An applause meter on the deck. Light it with your special. | Specials |
| BEELINE! | Targets pop up across the deck. Beeline to one and hit it. Needs Beeline to lock onto targets. | Beeline |
| TOSS IT! | A cardboard Manki for each contestant. Grab it and throw it in the bin. Needs grabbable objects. | Grab |
| BONK! | Cardboard heads pop out of the deck panels. Bonk one before it ducks. | Sir Bonk |
| SMILE! | Nyxxie's selfie frame shrinks on the deck. Be in it when the flash pops. | Nyx |
| GLITCH! | Deck panels glitch green, then blink out. Anyone still on one fails. Judged by zones; the floor never changes. | Wibou |
| QUICK DRAW! | Wait for DRAW!, then hit. Firing early is a misfire. | Salamander, once admitted |
| GET HIM! | Boss: Manki falls dizzy onto the deck. Land your hits before he wakes. Needs Manki as a hittable object. | Manki |

## Presentation

### Manki, the host

- The big animated Manki stays in the booth's host bay at `MankiHostAnchor`. He is
  presentation only and reacts to the simulation's events.
- He shouts each call, throws and slams on the channel's schedule, laughs at splats and
  hypes the crowd at SPEED UP!.

### Call cards

- Each channel opens on its card: the word, a one-line subtitle and the buttons it needs.
  It fills both large CRT screens, and the HUD shows the same card with binding-aware
  button glyphs.
- Guest channels show the guest's portrait on the card.
- Copy is pirate broadcast: caps, one exclamation mark, short subtitles.

### Telegraphs, stamps and ratings

- Landing rings fill as impact nears, slam points flash, cages and boards rise, spotlights
  glow, Bashie's watch is unmistakable. All of them are drawn from the predicted
  simulation state.
- At the verdict every fighter gets an ON AIR or CANCELLED stamp with its reason.
- Each contestant's row shows portrait, name, player colour and ratings.
- A SPEED UP! card opens acts two and three.
- **Show camera (selected 2026-11-04):** the deck shot, yaw 0, pitch 21°, framing the whole
  deck. Fighters measure 56 to 86 px tall at 1080p, against 49 to 75 px at the old 8° shot.
  A camera that frames the fighters and live hazards instead of the whole deck is the next
  idea for making them bigger.

## Where it's played

- **Local Play.** The MANKI MAYHEM mode card opens the four-seat Fighter Select, where
  CPUs or couch pads fill the seats. This is the first place the channels ship.
- **Online Rooms.** The ARCADE tab keeps its Manki Mayhem card. Its friendly-fire chips
  become Off and Shoves. Online needs the coordinated release.

## Guest stars

Each fighter on the roster gets a guest channel, so a newcomer meets them all in one show:
Bashie's whistle, FightGuy's boards, Sir Bonk's cardboard heads, Nyxxie's selfie frame,
Wibou's glitching panels and, once admitted, Salamander's quick draw.

## Out of scope for now

More than four players, teams, CPUs in online Rooms, leaderboards, booth hecklers (they
belonged to the Last TV Standing option, which was not chosen) and Manki's favourite.

## References

- [ADR-0061](../adr/0061-manki-mayhem-channels.md): the channel core.
- [ADR-0044](../adr/0044-manki-mayhem-hazard-director.md): host identity, hazards, runtime
  walls and the stage's Mayhem section, which channels keep.
- [Stage design](stages/manki_mayhem.design.md) and
  [production contract](stages/manki_mayhem.md).
- [Online Arcade brief](online-arcade.md): the Room flow and formats this mode joins.
- [Game mode exploration](game-modes.md#manki-mayhem).
