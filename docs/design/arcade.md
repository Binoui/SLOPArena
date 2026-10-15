# Solo Arcade feature brief

**Status:** Approved scope; implementation and iteration ongoing — 2026-10-15.
The maintainer confirmed the consolidated brief, mountain route and implementation plan.
[Phases 1–4](arcade-implementation.md#implementation-progress) are implemented;
the [FightGuy Course](arcade-phase-4.md) is playable. [Phase 5](arcade-phase-5.md)
includes the Cloudtop first-crossing art sample and the approved wider spacing
revision of the same winding route.
Full-course polish and human acceptance remain ahead.

## Purpose and first release

Give solo players satisfying challenges to replay with the real fighters' kits
and movement. Personal records provide a reason to return; learning useful PvP
mechanics happens through play. Arcade works without an online match or account.

| Challenge | Initial roster and content | Result |
| --- | --- | --- |
| Slopball | All four fighters; one shared underground sports club court | Continuous ball play; a small return-combo counter and local best |
| Character Course | FightGuy only; one mountain martial-arts sanctuary | Time to break every required target and reach the finish |

The longer-term course goal is one map per character. The first release proves
both challenge types before expanding. When Arcade implementation begins,
**Slopball is the first playable slice**. Getting the first working remote match
remains the immediate engineering priority.

## Shared fighter rules

Use normal PvP movement, jumps, move commitments, damage effects, landing recovery,
cooldowns, charges and ability behavior. Challenge geometry must respect them.
Balls and Course Targets are eligible for existing lock-on and aim assistance,
with normal eligibility/range rules and current-position aiming.

Use the normal orbit camera and ability-aim cameras in both challenges. Improve
court framing and occlusion without changing the camera-relative direction controls.
Attacks and projectiles use their real hit timing and launch properties.

Wibou Rising Slash's on-hit charge refund is intended for removal as a separate
PvP change. Arcade follows the actual shared kit; this preparation does not remove
the refund or introduce an Arcade-only resource rule.

## Slopball

### Court and shot loop

A net divides an enclosed room into player and sender halves. Walls and ceiling
allow bank shots. The fighter remains in the player half; balls and projectiles
can cross the net. The sender is a shot launcher, not a CPU opponent.

Each shot begins with a fresh ball at zero percent. A successful return retires
that ball, adds one return, then automatically schedules another shot after a
short fixed gap. Fighter position, commitments, cooldowns and ordinary
attack/projectile lifetimes continue between shots.

Each practice session receives a fresh random seed, and every fresh serve varies
its launcher position, direction, angle and flight time within safe receiving bounds.
The visible launcher repositions during the gap before firing. The first court uses
X ±8 and Z 5..7 at a constant height, with balls still landing in the receiving region. Restarting draws
a new sequence. Shared remains deterministic for a supplied session seed and shot
index. The shot index continues across misses; combo count controls the gentle
capped difficulty progression, resetting to the easy range on a miss. All four fighters share the same profile and progression;
playtesting must establish that its serves are readable and playable for every kit.

### Return and failure rules

A valid return follows this order:

1. The ball enters the player half.
2. The player redirects it with an attack, projectile or passive Body Deflection.
3. It clears the net into the sender half, including via wall or ceiling rebounds.
4. It lands on the sender-side floor. Crossing alone stays live and scores nothing.

Each ball scores at most once. There is no touch limit: setups, combos, juggling
and recovery saves are allowed. Early projectile interceptions can affect a serve,
but cannot arm or score a return before the ball has entered the player half.

Ball contact with the player-side floor resets the combo and difficulty, even
after clearing the net. It quietly schedules a fresh random ball after the normal
short gap. There is no failure screen, forced retry, countdown or fighter reset.
Movement, cooldowns and existing projectiles continue. The session best stays intact.
Net contact rebounds and stays live. An uncredited sender-floor landing also sends
a fresh random ball, without advancing or resetting the combo. Every fresh ball has
a new identity and zero percent. Only player-side floor contact resets the combo.

There is no shot deadline. Keeping a ball live while experimenting or waiting for
normal cooldowns is legal. Invalid or trapped technical states must be prevented
by court/profile validation and collision handling, not a hidden gameplay timeout.

### Ball response

Between contacts the ball follows gravity-driven arcs with linear air resistance.
The current profile uses gravity 6 with quicker opening serves, short/deep landing
targets and a wider lateral fan. It uses drag .35 per second (roughly half of free-flight velocity
remaining after two seconds, excluding gravity) and restitution .8 on room/net
surfaces. Body contacts keep their harmless direction-only reflection. Serves solve
the same discrete damped motion so shots still reach their receiving region. Damaging hits build its
percent during that shot and use the move's direction and relative strength to
launch it. Percent strengthens later attack responses; incoming serve speed is
controlled separately by the shot profile.

Body contact redirects existing ball velocity without adding speed. It causes no
damage or Knockback to the fighter. The body has the same physical shape in every
stance, including Shield and dodge; neither provides a larger or powered deflection.
A body bank over the net can score as a trick shot after reception.

The court must clearly show the floor, halves, net and ball. The approved finish
is a playful underground sports club: warm concrete, teal padding with coral/mint
accents, warm fixtures, sparse court graphics and one smiling-ball club sign.
Its shared sports identity
is shared by the roster, independent of FightGuy's mountain course.

## FightGuy Character Course

### Completion, falls and routing

Break every required target and reach the finish. Targets float and do not block
fighter movement; one damaging attack or projectile hit breaks them. The timer
measures the whole attempt from Go to successful completion.

Use a main route with ordered checkpoints and shortcuts. A checkpoint activates
only when reached after clearing every required target up to its route position,
including earlier sections. Earlier checkpoint visits are not prerequisites.
Reaching a later eligible checkpoint advances the saved respawn location.

Falls respawn at the active checkpoint, or the initial spawn before one is active.
The timer continues, and already broken targets stay broken until a full retry.
Respawn uses normal PvP fighter resets, including cooldown/resource refresh;
intentional falls are legal routes with no added time penalty. Existing launched
projectiles retain normal PvP death behavior.

Geometry encourages FightGuy's tools, but any valid solution counts. Shortcuts
may skip checkpoint visits while still clearing every required target and reaching
the finish. Unfinished targets must remain reachable from the saved spawn.

### Identity and authoring route

Aim for a first clean completion in **1–2 minutes**, with faster routes to discover.
The setting is serene mountains and martial-arts rooftops: a monk sanctuary,
pole parkour, floating islands, rope bridges and training spaces. The maintainer's
reference is World of Warcraft's
[Peak of Serenity monk setting](https://worldofwarcraft.blizzard.com/en-us/news/23310980);
this is a chosen course identity, not established FightGuy home-stage lore.

Following the 2026-10-15 playable review, the approved layout winds up around a
visible sanctuary and descends back toward the starting courtyard. Three signature
forks are separated by simpler transitions. There are no mandatory move gates:

| Area | Main route and faster alternatives | Learning opportunity |
| --- | --- | --- |
| Courtyard and broken bridge | A broad direct crossing using jump → R, or three to four exposed pole jumps | A forgiving introduction to R as movement; read both routes before takeoff |
| Rising terraces | Jump → E skips a climb through offset ledges | Height, air control and changing direction |
| Summit crossing | One advanced jump → E → R crossing, or a narrow island detour | Combining height and distance with a more precise landing |
| Roof descent and finish courtyard | Staggered descending roofs return toward the start | Braking, sideways transfers and deliberate landings |

Required targets occupy shared approaches, landings and connecting surfaces;
players can clear them from solid footing after either branch. Hitting a target
during arrival can save time. No required target forces a risky branch or a return
to skipped geometry. Target placement can be tuned after the overall layout.

All traversal surfaces are static in this release. Bridges and galleries are
ordinary supported walkable surfaces; hanging ropes can be scenery. Poles are jumped onto.
No climbing, swinging or pole-attachment mechanic is part of the course.

Avoid mandatory empty cooldown waits: provide traversal, targets or another valid
route during recovery. Actual E/R reach, landing space and FastFall opportunities
need native gameplay validation before the map is considered playable.

## Entry, attempts and records

A dedicated main-menu Arcade entry opens challenge selection. Slopball offers the
four fighters; the pilot Course selects FightGuy. Objectives and route cues remain
visible. Contextual movement/ability tips are opt-in.

Each full attempt starts from clean simulation and challenge state at a fixed spawn,
with a brief locked countdown. No movement, charging or buffered combat advantage
is possible before Go. The Course timer or first Slopball serve starts at Go.

Pause freezes simulation, ball motion, course time and kit timers. Paused attempts
remain eligible for records. Slopball keeps a small combo/best line on the HUD;
ball play is the focus. Misses never open a results overlay. The Course can show
completion results and a quick Retry or return to challenge selection. A full
retry clears the entire attempt, including projectiles, queued effects, targets,
checkpoint, shot sequence and pending input.

Persist personal bests locally, separately from online identity and settings resets:

- Slopball saves the highest return combo on a miss, voluntary stop or restart;
  multiple combos are never added together.
- Course time is eligible only after all required targets and the finish.
- Compare records by challenge/profile, fighter and compatible gameplay.
- Archive incompatible older bests when meaningful map, rules, shot-profile or
  fighter gameplay changes occur. Presentation-only changes preserve compatibility.

Personal bests are the only initial rewards. Exact cooked content provenance and
record compatibility are distinct: aggregate package hashes include presentation
data and cannot alone decide whether records are comparable.

## Selected architecture and delivery

Use focused Shared ball/target support and Arcade rules, building on the existing
local production simulation. Ball physics, accepted contacts, goals, score, clock
and attempt state are authoritative in Shared. Unity owns input adapters,
presentation, cameras, UI, audio and VFX. Objects have their own identity and state;
they are not dummy fighters.

Expand the baked stage format to bundle static geometry and challenge data in one
artifact. Update stage authoring, compilation, binary loading and validation
together. Preserve existing PvP stage behavior and keep Arcade stages out of normal
PvP selection/admission. The implementation plan specifies the migration details.

Delivery order is common format/contact foundations, **Slopball playable across
the roster**, FightGuy's Course, then content/camera polish and human acceptance.
The first release still contains both challenges.

## Acceptance and remaining authoring work

Implementation is ready for human review when:

- Both challenges can be entered, paused, completed/stopped and retried offline
  without account or match allocation.
- Slopball follows receive/contact/clear ordering, one score per ball, harmless
  body rebounds, live net contacts and fresh random serves after every floor landing.
- Every roster fighter can play the shared capped profile using its normal kit.
- The Course supports a readable ordinary-jump route and faster ability crossings, cumulative
  checkpoint prerequisites, persistent targets and legal fall-reset routes.
- Starts, pause, automatic continuation and full restarts preserve the specified timing.
- Records survive restart/stop/retry, archive incompatible gameplay and retain
  compatibility across cosmetic changes.
- Normal PvP contacts/kits and existing stage loading/freshness remain correct.

Exact platform positions, ball gravity/rebounds/speed, serve gap, countdown duration,
difficulty values and camera occlusion are greybox/content tuning tasks under these
rules. No numerical trajectory or finished map has been approved or tested here.
Moving platforms, more Character Courses, online Arcade/volleyball, medals and
leaderboards are later scopes.

## Preparation references

- [Implementation plan](arcade-implementation.md): boundaries, stage migration,
  content brief, delivery slices and verification.
- [Resolved interview](arcade-interview.md): all seven rounds and factual grounding.
- [ADR-0036](../adr/0036-arcade-pvp-kit-parity-and-legal-fall-resets.md): kit parity
  and legal fall resets.
- [ADR-0037](../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md): focused
  Shared support and unified stage artifact.
- [CONTEXT.md](../../CONTEXT.md): concise domain vocabulary.
