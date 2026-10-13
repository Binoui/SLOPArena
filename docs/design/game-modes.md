# Game mode exploration

**Status:** exploratory design notes, 2026-10-13. These record the maintainer's
preferred directions and proposed rules to try; they are not an implementation
specification or a claim of supported features. The
[friends demo](../plans/2026-09-05-playable-demo-reset.md) remains the current
delivery baseline.

## Direction from the discussion

Keep basic three-stock fights available. Explore other reasons to enjoy the same
movement, kits and Knockback through new objectives and distinctive map geometry.

- **Capture zones:** completing a capture awards points, then a zone appears at
  another location. Free-for-all and teams are equally important. Larger maps for
  four or more players are part of the intended direction.
- **Volleyball:** play in enclosed box courts, inspired by squash. The ball spawns
  in a player's camp, can be hit in any direction, and touching the ground ends
  the round. Own-side combos and a ball whose speed increases with damage percent
  remain part of the direction. This mode needs a dedicated map.
- **Crown:** carrying the crown and escaping pursuers is the attraction. A larger
  map with more movement choices should support that chase.
- **Moving platforms:** add gameplay geometry and personality to current maps as
  well as considering it for the new modes.

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

Implementation architecture options and approval belong to a later design step.
These notes do not select a new framework or authorize an architecture change.

## Useful first experiments

1. Exercise capture scoring and contested progress in both free-for-all and teams
   on simple geometry before investing in a finished map.
2. Graybox the larger route network and check it for capture contests and crown
   interceptions. Include more-than-four play once capacity is supported.
3. Test ball response and rally escalation on a dedicated simple court.
4. Prove one moving-platform behavior, then choose current maps where it improves
   play. Its place in the implementation order depends on the architecture review.

Observe whether players understand the objective, encounter others frequently,
find useful roles across skill levels, and voluntarily want another round.
