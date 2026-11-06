# Manki Mayhem feature brief

**Status:** Direction selected 2026-11-04. The maintainer approved the microgame redesign
("this sounds really good. lets go for it") with every recommended pick below. It replaces
the three-card survival show selected on 2026-10-27. That show is built and stays playable
until the first channel slice replaces it. No tuning value is selected: channel lengths,
act sizes, ratings and speeds below are "not selected" or provisional.
[ADR-0061](../adr/0061-manki-mayhem-channels.md) records the architecture, and the
[implementation plan](manki-mayhem-implementation.md) orders the work. The [variety pass](#variety-pass)
of 2026-11-05 adds seven channels, PRIME TIME! remixes, the GET HIM! finale and twists.

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

## Variety pass

Selected 2026-11-05. The first slice repeated itself: six regular channels filled twelve regular
slots, PRIME TIME! closed every act, and each airing differed from the last only in positions and
speed. The maintainer picked all seven new regular channels below, PRIME TIME! remixes plus a new
boss GET HIM!, and seeded twists for every channel. The rules below are the proposal of
2026-11-05. [ADR-0063](../adr/0063-manki-mayhem-variety.md) records the architecture. Every length,
count, size and speed is not selected.

### A show without repeats

- Thirteen regular channels fill the twelve regular slots from one shuffled deck. A regular
  channel airs at most once per show, and one channel sits out.
- Acts one and two close with a PRIME TIME! remix, each with a different pair. Act three closes
  with GET HIM!, the finale where the contestants finally get their hands on the host.
- Every airing draws one twist of its channel. The call card shows the twist under the title, for
  example DODGE! with "bomb rain" underneath. A twist changes the pattern, never the channel's verb.

### New regular channels

**BONK!** · *guest star Sir Bonk · bonk a cardboard head before it ducks*

- Cardboard Manki heads pop out of the deck a few at a time and duck back down after a moment.
  Sir Bonk referees from the edge.
- Hit a head with any normal and it's yours. A head counts for the first contestant to hit it.
- Fewer heads are up than contestants still looking, in two quick waves, so somebody can miss out.
- Pass: you bonked a head.
- Twists: **Whack** (heads pop anywhere). **Decoys** (some heads are lit bombs in a cardboard
  mask; bonk one and you're TOASTED).

**SMILE!** · *guest star Nyxxie · be in the selfie frame when the flash pops*

- Nyxxie's selfie frame lands on the deck big enough for everyone, then shrinks.
- At the flash, everyone inside the frame is in the shot. Shoving is allowed. Only the flash
  counts, so you can step out and back in.
- Pass: inside the frame at the flash. Stamp: OUT OF FRAME.
- Twists: **Squeeze** (shrinks in place). **Pan** (slides across the deck while it shrinks).

**SUMO!** · *stay in the ring*

- One ring rises around the contestants and shrinks until not everyone fits.
- Step out once and you're out on the spot, in the air or not. Shoving rivals out is the game.
- Pass: still in the ring at the buzzer. Stamp: OUT OF THE RING.
- Twists: **Shrink** (shrinks in place). **Drift** (wanders while it shrinks).

**GLITCH!** · *guest star Wibou · panels glitch, then blink out*

- The deck shows a grid of panels. Some glitch green, then blink out for a moment. Anyone standing
  on a blinking panel drops through: GLITCHED. Anyone in the air over it is safe.
- The floor never changes. Zones judge the verdict.
- Three rounds of glitches, the last ones faster.
- Pass: never on a blinking panel.
- Twists: **Scatter** (random panels). **Sweep** (a band crosses the deck; find the gap).
  **Checker** (the deck flips like a checkerboard).

**QUICK DRAW!** · *Manki hosts until Dusty joins the roster · wait for DRAW!, then hit*

- Everyone stands ready. Manki holds the sign. At a random moment it flips to DRAW!.
- Swing any normal after DRAW! and you pass. Swing before it and you MISFIRE, cancelled on the
  spot. Never swing and you're TOO SLOW at the buzzer. Moving is allowed.
- Twists: **Showdown** (one DRAW!). **Fake-out** (near-miss words such as DRAWER! come first;
  swinging on one is a misfire). **Duel** (the slowest drawer is cancelled too, unless everyone
  drew on the same tick).

**HOT POTATO!** · *the bomb sticks to whoever holds it*

- Manki drops a lit bomb on one contestant. It sticks over their head with a fuse.
- The holder passes it by bumping into another contestant. Under Shoves, the holder's shove passes
  it too. A shove on the holder only shoves. Whoever just passed it can't take it straight back for a
  moment.
- At the bang the holder is TOASTED. Everyone else passes. A holder who leaves just before the bang
  takes the bomb with them, so nobody else pays for it.
- Twists: **One potato**. **Two potatoes** (with three or more contestants, two bombs on two
  different holders; one bomb goes out if only two contestants are left).

**BLOCK!** · *teaches Shield · bombs from every side, hold shield*

- The first channel that switches on shield. The call shows the shield button.
- Manki's barrage covers the whole deck, so there is nowhere to run. A contestant who is shielding
  when a blast lands is safe. Anyone else is TOASTED.
- Twists: **Barrage** (one wave). **Waves** (a few waves with a pause between). **Block or jump**
  (shockwave rings mixed in; a shield doesn't stop a ring, so jump those).

### Bosses

**PRIME TIME!** · *boss · two channels at once*

- Each airing pairs one goal channel with one hazard channel, both at top speed, on one clock and
  with one verdict.
- Goals: SPOT!, SUMO!, SMILE!, BREAK OUT!, BREAK THE BOARDS!, BONK! and QUICK DRAW!. Hazards:
  DODGE!, JUMP! and GLITCH!.
- Only pairs on an allowed list air. A pair joins the list when careful CPUs pass it with every
  twist combination and it reads well in play. The classic BREAK OUT! plus DODGE! stays.
- Both children draw their own twist.
- Pass: both objectives met.
- Never paired: NO RUNNING! (standing still under falling bombs needs its own design), HOT POTATO!
  (its fail is one holder by design) and BLOCK! (the shield already answers bombs).

**GET HIM!** · *boss · finale · Manki is down, land your hits before he wakes*

- Manki tumbles out of the booth onto the middle of the deck, dizzy, stars circling. Everyone
  piles in.
- Each contestant must land their own quota of hits before he wakes. One swing counts once.
- When he wakes he lashes out. Getting caught cancels you even with your hits done.
- Pass: quota landed and not caught.
- Twists: **Slam** (a shockwave slam; jump it). **Tantrum** (bombs around him; run).

### Twists for the first-slice channels

| Channel | Twists |
| --- | --- |
| DODGE! | **Aimed** (bombs near contestants, as today). **Bomb rain** (rows of rings cross the deck, each with one gap). **Big one** (one huge bomb; get out of its ring). |
| JUMP! | **Rings** (as today). **Crossfire** (two slam points; the rings cross). **Tall wave** (one tall ring that needs a double jump). |
| BREAK OUT! | **Cracked wall** (as today). **Any wall** (every wall breaks, but the bomb lands on one side; leave the other way). **Double cage** (an outer ring of walls; break two). |
| SPOT! | **Still** (as today). **Drifting** (the spotlights wander). **Musical** (the spotlights jump to new places mid-channel; only the last places count). |
| NO RUNNING! | **Looks** (as today). **Fake-out** (Bashie starts to turn, then doesn't). **Rapid** (short, quick looks). |
| BREAK THE BOARDS! | **Your stack** (as today). **Scattered** (stacks stand around the deck; find the one in your colour). |

### Later

| Channel | Pattern | Teaches or guest |
| --- | --- | --- |
| HIDE! | Manki's bazooka sweeps the deck from one side. Get behind a crate. Needs hazards that stop at crates. | |
| SHOW OFF! | An applause meter on the deck. Light it with your special. | Specials |
| BEELINE! | Targets pop up across the deck. Beeline to one and hit it. Needs Beeline to lock onto targets. | Beeline |
| TOSS IT! | A cardboard Manki for each contestant. Grab it and throw it in the bin. Needs grabbable objects. | Grab |

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
Wibou's glitching panels and Manki's GET HIM! finale. QUICK DRAW! becomes Dusty's once he is
admitted; Manki hosts it until then.

## Out of scope for now

More than four players, teams, CPUs in online Rooms, leaderboards, booth hecklers (they
belonged to the Last TV Standing option, which was not chosen), Manki's favourite, and twists
weighted by act.

## References

- [ADR-0061](../adr/0061-manki-mayhem-channels.md): the channel core.
- [ADR-0044](../adr/0044-manki-mayhem-hazard-director.md): host identity, hazards, runtime
  walls and the stage's Mayhem section, which channels keep.
- [Stage design](stages/manki_mayhem.design.md) and
  [production contract](stages/manki_mayhem.md).
- [Online Arcade brief](online-arcade.md): the Room flow and formats this mode joins.
- [Game mode exploration](game-modes.md#manki-mayhem).
