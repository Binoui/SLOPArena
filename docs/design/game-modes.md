# Game mode exploration

**Status:** exploratory design notes, started 2026-10-13; solo arcade direction
added 2026-10-14; upgrade rounds added 2026-10-17. These record the maintainer's
preferred directions and proposed rules to try; they are not an implementation
specification or a claim of supported features. The
[friends demo](../plans/2026-09-05-playable-demo-reset.md) remains the current
delivery baseline.

## Direction from the discussion

Keep basic three-stock fights available. Explore other reasons to enjoy the same
movement, kits and Knockback through new objectives, distinctive map geometry and
upgrades between rounds.

- **Capture zones:** completing a capture awards points, then a zone appears at
  another location. Free-for-all and teams are equally important. Larger maps for
  four or more players are part of the intended direction.
- **Volleyball:** play in enclosed box courts, inspired by squash. The ball spawns
  in a player's camp, can be hit in any direction, and touching the ground ends
  the round. Own-side combos and a ball whose speed increases with damage percent
  remain part of the direction. This mode needs a dedicated map.
- **Crown:** carrying the crown and escaping pursuers is the attraction. A larger
  map with more movement choices should support that chase.
- **Upgrade rounds:** one PvP Match made of short fights, with upgrades drafted
  between rounds, inspired by League of Legends' Arena. Generic upgrades come
  first; character upgrades are the goal.
- **Moving platforms:** add gameplay geometry and personality to current maps as
  well as considering it for the new modes.
- **Solo arcade:** small replayable minigames with personal records, offering a
  low-stress way to explore kits and movement beyond Training or CPU fights.
  Slopball and combined Character Courses are the selected initial solo release.
  Their records are balls returned and time to complete the course.

Rules below are design proposals. No capture duration, score target, map size,
player maximum or ball-speed value has been selected.

## Capture zones

The match rhythm is **converge → contest → complete a capture → score → relocate**.
Score is awarded on completion, rather than continuously while standing in a zone.

A first rules proposal:

- One zone is active at a time. A sole player in free-for-all, or members of a
  single team, advance that claimant's capture progress.
- An eligible opponent inside the zone pauses progress. Knocking opponents out
  creates an opportunity to finish; a KO is not required to make progress.
- Completing the capture awards points to that player or team and retires the
  zone. The next zone activates at a different authored location.
- Players respawn throughout the match. First to a capture-score target is a
  possible win condition; a timed match is another option.
- Previewing the next location could let players plan their approach, rather than
  rewarding whoever happens to stand nearest a newly revealed zone.

Claim ownership needs a focused decision: what happens to unfinished progress
when the claimant leaves or a different player/team takes control? Test progress
decay and takeover rules; avoid accidentally granting an almost-complete capture
to the last player who steps inside. Also test whether several teammates should
capture faster, or simply make the zone easier to defend.

The larger map should contain distinct capture sites connected by several routes.
Keep approach times short enough for repeated contests. Mix broad fighting ground,
exposed shortcuts and positions with multiple entrances. Size alone can produce
long walks; the useful addition is more decisions during approach and retreat.

## Volleyball

The maintainer's selected direction is an **enclosed box court**: the ball spawns
in a player's camp, that player can hit it in any direction, and ball contact with
the ground ends the round. Own-side combos and percent-based speed escalation
carry forward from the earlier discussion.

The match rhythm is **serve → receive → set up or bank a shot → return → floor
contact**. Normal move choices should matter: an upward hit can set the ball, a
lateral hit can place it, and a strong finisher can accelerate it. Ball response
needs its own tuning; fighter Knockback values alone do not establish a satisfying
rally.

A proposed interpretation is one shared box with camps marked on the floor and
rebounds from its walls and ceiling. This makes wall shots, overhead rebounds and
deceptive return angles part of play. The box layout and ceiling behavior remain
proposals to validate.

A scoring proposal awards a point to the opposing side when the ball touches the
floor in a camp. Ground contact ends the round regardless of who last hit the
ball. Team play allows setup and finishing roles. Combos on one's own side are an
explicit part of the design, so do not impose a real-volleyball touch limit by
default. A proposed serve setup holds the spawned ball above the floor until the
serving player hits it, giving that player a clear opening action.

Ball percent rises during a rally and resets when a point starts the next rally.
Increasing speed should make exchanges more exciting while retaining an
opportunity to read and return the ball. Test a speed ceiling, ball Hitstop and
repeated-contact rules so one lingering hitbox cannot manufacture an entire combo.

Open choices include the camp/box arrangement, floor-contact scoring,
player-versus-player damage, court crossing, ceiling rebounds, serve protection
and timeout, ball targeting, team sizes and how different hit directions become
ball trajectories. The dedicated court must make floor contacts, camp ownership
and rebound paths clear from the gameplay camera; walls and the ceiling need
presentation that preserves the view of the ball and fighters.

## Solo arcade

The [Solo Arcade feature brief](arcade.md) now consolidates the selected solo
release: **Slopball for all four fighters plus one FightGuy Character Course**.
It supersedes earlier wall-only returns and separate obstacle/target challenges.
The [resolved interview](arcade-interview.md) preserves the design decisions.

Slopball uses a sender and net in an enclosed neutral sports court. Receive a
ball in the player half, redirect it and clear the net back into the sender half
to score one return, including banks and passive body trick shots. A fresh ball
follows after a fixed gap. Unlimited juggling, per-shot percent and a seeded,
capped difficulty ramp reward experimentation. A player-side floor miss ends
the attempt; earned returns also count on voluntary stop or retry.

FightGuy's pilot merges traversal and targets in serene mountains with martial-arts
rooftops, pole tops, static rope bridges and floating islands. Break every required
target and reach the finish; aim for a first clean completion in 1–2 minutes.
Checkpoints provide optional cumulative-progress saves. Falls preserve broken
targets and continue the timer, with normal PvP respawn and legal cooldown-refresh
routes. Geometry encourages useful moves; any valid solution counts.

Both challenges use normal kits and orbit/ability-aim cameras, a dedicated offline
Arcade entry, opt-in mechanic tips, quick retry and persistent local personal bests.
Archive incompatible gameplay records while preserving cosmetic compatibility.
Enjoying Arcade itself is a valid goal; later trying stock PvP is optional.

The selected architecture is focused Shared ball/target support with challenge
data bundled into an expanded baked stage artifact. **Slopball is the first
playable Arcade slice**, then the Course; see the
[implementation plan](arcade-implementation.md). This preparation does not
reorder the immediate first-working-online-match priority. Additional courses,
multiplayer volleyball, medals and leaderboards are later scopes.

## Crown

The match rhythm is **claim → escape → intercept → steal → escape again**.
Running away successfully is an intended skill expression. Route selection,
movement execution and reading pursuers should help the carrier survive.

A first proposal awards score over time while carrying the crown and drops it
on KO. A hit-triggered drop is another candidate, trading longer chases for more
frequent ownership changes. Select the transfer rule through play rather than
assuming the KO-only version is settled.

Use branching loops, vertical shortcuts and routes where a pursuer can cut ahead
of the carrier. Give the crown a clear position indicator so a large map produces
interceptions. Test route connectivity and kit matchups to avoid an escape circuit
that the fastest fighter can repeat indefinitely. Successful escape should remain
rewarding.

Capture and crown could share a larger map if it offers both contestable sites and
connected chase routes. That is a reuse hypothesis, not a requirement that one map
fit both modes.

## Upgrade rounds

The maintainer's reference is League of Legends' Arena mode: Battlerite-style
round fights with roguelike upgrades drafted between rounds. SLOPArena's version
is a PvP mode where one Match is a series of short fights, and each upgrade
changes how a fighter moves or how its moves behave until the Match ends. It
reuses current stages, Character Kits and stock fights, so unlike the modes
above it needs no dedicated map.

The match rhythm is **fight a round → draft an upgrade → fight again with
changed kits → repeat until one player wins the Match**.

The maintainer has selected:

- One Match contains every round. The Room stays in that Match until the final
  round, so nobody joins after it starts (Rooms admit new members only in
  Lobby). Results follow the final round.
- Generic upgrades come first because they work for every fighter. Character
  upgrades are the more fun direction and should become the heart of the mode.
- A local prototype against CPU Opponents is an acceptable first step; the
  destination is PvP.
- It is the first multiplayer mode experiment, ahead of capture (2026-10-17).
- Balance stays out of scope while the shape of the mode is explored.

A first rules proposal:

- Each player keeps one fighter, locked in Character Select, for the whole
  Match.
- A round is a short one-stock fight. Damage percent resets between rounds. A
  long round goes to overtime, where Knockback rises or the blast zones close in.
- Between rounds, every player picks one of a few offered upgrades. Picks are
  revealed together, so everyone sees the other builds before the next fight.
- Rarer and stranger upgrades appear as the Match goes on. The loser of a round
  could get an extra choice; in ROUNDS, only the loser gets a card.
- In 1v1, the first player to a target number of round wins takes the Match.
  With three or four players, each player could carry a Match-long health pool
  (working name: hearts) that low placements drain until one player remains.
  Team play waits for the team support that capture also needs.
- Stages rotate between rounds, or the loser of a round picks the next one, as
  in competitive Smash.
- League's gold and item shop stay out at first; the draft is the core.
- Upgrades belong to one Match. A rematch starts without upgrades.

No round count, round length, win target, draft timer, offer count or upgrade
value has been selected.

**Generic upgrades** change universal mechanics, so they work for every fighter
and future package: an extra jump or air dodge, a heavier or lighter fighter,
faster special or Burst cooldowns, a Slide that hits, a shockwave on a FastFall
landing, surviving one blast-zone contact per round by bouncing back, bonus
damage for hitting during an attack's startup, winning Clashes outright, damage
dealt that heals some of the attacker's own percent, launch power that grows
with the fighter's own percent, or a giant or tiny fighter.

**Character upgrades** bend one of the fighter's own moves, usually a special,
in the spirit of Battlerite's battlerites and Rivals of Aether's Abyss runes:

- **FightGuy:** Ki Shot keeps charging while moving; a full Ki Shot splits on
  impact; Rising Dragon works twice before landing; Cyclone Kick pulls
  opponents in.
- **Manki:** Round Bomb sticks to the first fighter it touches; the Bazooka
  blast launches Manki too, for a rocket jump; Jetpack Boost gets a second
  burst; Aerosol Inferno leaves burning ground.
- **Wibou:** shuriken hits mark a target and Dash Slash homes in on the mark;
  Dash Slash resets on hit; Rising Slash leaves an afterimage that slashes
  again; Skyfall's spike lands with a shockwave.
- **Bonk:** Shoulder Bash stuns an opponent driven into a wall; Bonk can move
  during Blade Storm; Jump Slam leaves a delayed aftershock; the greatsword
  grows with each hit in the round.

Wilder directions include **borrowed moves**, which take a special from another
fighter (projectiles without a held prop are the easiest start), and **Hot
Potato**, a bomb attached at round start that passes to whoever its carrier
hits. Optional **curses** could trade a drawback for a stronger offer.

An upgrade that changes a move occupies that move's Slot, and a later upgrade
on the same Slot replaces it, as Hades does with boons. Builds stay readable,
and the HUD can mark the matching move ticket. Each fight card can summarize
the build, and fighters need visible tells when an upgrade changes how a move
looks. One voice for the cards is patch notes: Hotfix, Patch and DLC rarities,
curses as Known Issues and card text written as patch-note lines. The mode's
name is not selected; Patch Day, Early Access and Slop Circuit came up.

Rounds can vary without new maps. A breakable orb that spawns mid-round and
rewards whoever breaks it would give free-for-all fights a meeting point, as in
Battlerite; Arcade's Course Targets are a starting point. Round events such as
low gravity or everyone starting at a high percent are another option.

The CPU prototype plays the same Match structure locally: rounds and drafts
against CPU Opponents that also draft upgrades. It tests whether drafting and
the first upgrades are fun before online round and draft support exists. A solo
ladder of escalating CPU fights is a possible spin-off, not the goal.

Open choices include private offers versus a shared pool where a pick denies
that upgrade to others, what happens when a player leaves mid-Match, and
whether a character upgrade modifies its move or replaces it.

## Moving geometry

Start with one deliberate platform behavior on a suitable map: a ferry between
routes, an elevator giving temporary access to a perch, or an orbiting platform
creating a timed shortcut. Choose the behavior for the decision it creates.

The main fighting area should remain usable while the platform is elsewhere.
Avoid making ordinary traversal depend entirely on waiting for it. Map selection
and scope need their own review: the locked
[Log Off design](stages/log_off.design.md) explicitly excludes moving geometry,
so this general direction does not silently revise that stage's accepted shape.

## Current implementation boundaries

The implemented match rules are stock elimination and indefinite Training
([MatchRule.cs](../../src/Shared/MatchRule.cs)). Match admission and results
currently cap players at four
([MatchStartRequest.cs](../../src/Shared/MatchStartRequest.cs),
[MatchResultPacket.cs](../../src/Shared/MatchResultPacket.cs)). Supporting more than
four is part of the desired direction and requires an explicit capacity change.

Stages currently use baked static collision
([ArenaDefinition.cs](../../src/Shared/ArenaDefinition.cs),
[stage concepts](stage-concepts.md)). Playable moving geometry needs authoritative
simulation support; cosmetic Unity animation alone does not supply it. Objective
state, team rules, the ball and crown also need Shared/GameServer authority and
matching client presentation.

Upgrade rounds need several rounds inside one Match, with resets between them,
and a draft whose choices are part of authoritative simulation history so
prediction, rollback and Replays reproduce them. Per-fighter upgrade state
belongs in the Shared simulation. A Match currently uses one baked stage, so
rotating stages between rounds is new work. Character upgrades need an
authored, cooked and pinned form that keeps match content immutable, such as
alternate move timelines in the package. `IMatchRule` is the existing seam for
new win conditions, and the local Solo match against a CPU Opponent is the
natural host for the prototype.

Multiplayer-mode architecture options and approval belong to a later design step.
Solo Arcade's selected foundation is documented in its own implementation plan;
these broader notes do not select a general framework.

## Proposed multiplayer delivery sequence

**Current observation, 2026-10-13:** the maintainer reports that an online match
has never worked yet, and connection problems remain unresolved. First remote
play is therefore the immediate milestone. Multiplayer mode appeal and player
retention are hypotheses to test after players can reach a working match. Solo
arcade appeal can be tested through the local simulation independently.

1. **Complete a remote stock match and rematch.** Use current characters, an
   existing stage and the packaged join flow. Two players on different networks
   must be able to join, move and fight, reach KO/respawn and results, then start
   another match. Diagnose the actual failing connection
   step and fix its blockers. This milestone proves a usable starting point;
   broader four-player reliability still needs its own check.
2. **Get the first small friend session.** Use the working build to observe
   joining, combat readability and another-round interest. Record concrete
   failures and what players enjoyed. An early session supplies evidence while
   the game is still rough; it does not require a finished casual-mode offering.
3. **Build one bounded upgrade rounds experiment.** It needs no new map: use
   the current stages and stock fights. Prototype the round and draft loop
   against CPU Opponents with generic upgrades, add a first character upgrade
   for each fighter, then play the same Match online. Resolve the necessary
   architecture options before implementation.
4. **Test upgrade rounds with mixed-skill friends.** Play online Matches in 1v1
   and with three or four players. Check whether players understand the draft,
   read each other's builds, stay involved across rounds and choose another
   Match. Fix the problems that prevent that experience, then invite more small
   groups to play the tested offering.
5. **Choose the next addition from observed play.** Capture keeps its bounded
   experiment: free-for-all and teams with equal priority, one existing static
   map or a simple route graybox, unlimited respawns, full-capture scoring and
   zone relocation, plus explicitly scoped capacity work for four or more
   players. Crown can reuse that objective infrastructure and route-map work.
   Volleyball keeps its dedicated ball/court experiment; moving platforms keep
   their separate movement/collision scope. Their order should follow player
   interest and the remaining technical risk.

Connection and match-breaking failures take priority whenever they prevent a
session. Once players can complete and repeat matches, casual-mode experiments
can proceed alongside fixes found in those sessions. Reliability work should
have observable acceptance criteria so it does not become an indefinite polish
phase.

The audience hypothesis is that a clear objective and continuous participation
will make the shared fighting mechanics enjoyable to more people. Measure
voluntary return in either casual modes or stock fights. Players who prefer a
casual mode are a successful audience in their own right; trying stock later is
an optional outcome.

## Mode-specific experiments

1. Exercise capture scoring and contested progress in both free-for-all and teams
   on simple geometry before investing in a finished map.
2. Graybox the larger route network and check it for capture contests and crown
   interceptions. Include more-than-four play once capacity is supported.
3. Test ball response and rally escalation on a dedicated simple court.
4. Prove one moving-platform behavior, then choose current maps where it improves
   play. Its place in the implementation order depends on the architecture review.
5. Follow the selected solo Arcade sequence: prove Slopball across the roster,
   then the static FightGuy Course, with personal bests and quick retry. Test
   whether each challenge is enjoyable and repeated attempts visibly improve.
6. Prototype upgrade rounds against CPU Opponents with generic upgrades, add a
   first character upgrade for each fighter, then play a PvP Match with
   friends. Check whether drafts create memorable moments, players read each
   other's builds and they want another Match.

Observe whether players understand the objective, encounter others frequently,
find useful roles across skill levels, and voluntarily want another round.
