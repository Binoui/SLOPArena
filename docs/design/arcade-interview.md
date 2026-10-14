# Solo Arcade design interview log

**Status:** Complete — 2026-10-14. All seven question rounds and the final
shared-understanding review are resolved. The [feature brief](arcade.md) and
[implementation plan](arcade-implementation.md) are approved for implementation
handoff. This log preserves the questions, answers and source constraints behind
them; gameplay implementation remains future work.

## Round 1: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **First playable scope:** Slopball plus one pilot Character Course. Expand
   toward one map per character after proving both challenge families.
2. **Slopball loop:** the sender launches a fresh ball after each completed
   return. Round 2 defines a successful return.
3. **Course completion:** break every required target and reach the finish.
   Completion time measures the whole combined challenge.
4. **Kit rules:** normal PvP movement, abilities, cooldowns and commitments.
   Course pacing must respect those limits. Arcade object interactions and
   resets still need explicit rules; this does not settle their semantics.
5. **Randomness:** retry repeats a seeded random shot sequence, allowing
   comparable attempts and learnable patterns. Difficulty progression and
   record categories remain open.

## Round 2: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **Successful Slopball return:** a ball struck by the player clears the net
   into the sender half. Banks off walls or the ceiling count. Each returned
   ball is retired and the sender launches a fresh ball. Round 4 additionally
   permits passive body deflections to satisfy the player-contact requirement.
2. **Missed Slopball shot:** first floor contact on the player's side before a
   valid return ends the attempt. Immediate retry is part of the intended flow.
   Net contact, invalid sender shots and timeouts still need precise handling.
3. **Pilot character:** FightGuy, emphasizing its upward burst, travelling
   attacks and projectile challenges.
4. **Course fall:** respawn at a checkpoint while the timer keeps running.
   Checkpoint progress, resource reset and any scored penalty need follow-up.
5. **Kit parity:** Arcade should match PvP behavior. The maintainer plans to
   remove Wibou Rising Slash's on-hit charge refund because it no longer fits
   the intended move. Arcade should follow that revised PvP behavior, with no
   separate Arcade-only refund. The current source still implements the refund;
   its removal is tracked as a separate gameplay change, not completed work.

## Round 3: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **Slopball roster:** all four current fighters are playable in the first release.
2. **Difficulty progression:** shots become gradually harder as returns accumulate.
   Launch limits, pace and progression parameters still need a bounded profile.
3. **Valid ball contacts:** attacks and projectiles can return the ball. Round 4
   subsequently adds passive body deflection as a valid trick-shot return.
   Shield and grab/throw are not separately selected powered return tools.
4. **Ball launch response:** use the move's launch direction and relative
   strength, tuned for the ball. Moves are not normalized to one outgoing speed.
5. **Course structure:** a main route with ordered checkpoints and shortcuts.
6. **Move requirements:** geometry encourages intended FightGuy moves, but any
   valid route and attack solution counts. No explicit move-specific lesson gates.
7. **Falls:** no additional time penalty. Intentional falls are legal routes,
   including their normal PvP cooldown refresh. The attempt timer keeps running.

The kit/fall policy is recorded in
[ADR-0036](../adr/0036-arcade-pvp-kit-parity-and-legal-fall-resets.md).

### Consequence behind the fall question

Current [PvP respawn](../../src/Shared/ServerSimulation.cs) creates fresh fighter
state: damage, cooldowns and charge spending reset, while jumps and air dodges
refill. It occurs in the death tick. Ordinary landing does not reset cooldowns.
A deliberate fall can therefore be faster than waiting for another ability use.
The maintainer explicitly accepts this route. Course timing measures actual
elapsed simulation time with no added fall penalty.

Launched projectiles also currently survive a PvP death. How target progress and
lingering shots are handled around checkpoints, serves and whole-attempt retries
must remain explicit in later rules. A checkpoint respawn retains ordinary
PvP fighter behavior, including the normal lifetime of launched projectiles.
Whole-attempt retry is a separate reset boundary.

## Round 4: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **Player's court boundary:** the player remains in their half. How this
   boundary is presented and enforced needs to preserve normal movement rules.
2. **Net contact:** the ball rebounds and stays live. It can still be saved
   before own-side floor contact ends the attempt.
3. **Fighter body contact:** the ball physically deflects off the fighter's body,
   without receiving additional velocity or attack launch strength. Such a
   deflection can complete a return, including a difficult block near the net.
   This is an explicit extension of the earlier attack/projectile-only answer.
   The ball's effect on the fighter and defensive-state interactions remain open.
4. **Normal targeting assistance:** balls and course targets are eligible for
   the existing PvP lock/aim assistance rules. This does not add predictive
   projectile aiming, larger capture ranges or altered move behavior.
5. **Next-serve pacing:** fresh balls launch automatically after a short fixed
   gap. Fighter position, move commitment and cooldowns continue between shots.
   The actual gap is a launch-profile tuning value, not yet selected.
6. **FightGuy course access:** FightGuy only in the first release. The pilot
   is authored and validated for his kit.
7. **Target destruction:** one damaging attack or projectile hit breaks a
   course target; no HP threshold or repeated-hit requirement.
8. **Checkpoint activation:** clear the preceding section's required targets
   and reach the checkpoint. Checkpoint traversal/shortcut details follow.
9. **Target progress after a fall:** destroyed targets stay broken until a
   whole-attempt retry. Falling does not undo target progress.

## Round 5: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **Own-side setup hits:** no touch limit. Combos, juggling and recovery saves
   are allowed; each ball still yields at most one return.
2. **Ball percent:** each fresh ball starts at zero. Damaging contacts accumulate
   percent during that shot, strengthening later launches. Incoming serve speed
   follows the selected difficulty profile rather than the previous ball's percent.
3. **Effect on the fighter:** passive ball-body contact causes no damage or
   Knockback to the fighter and adds no speed to the ball. Normal kit self-damage
   remains governed by the shared PvP behavior.
4. **Serve reception:** a fresh ball must first enter the player half, then be
   redirected by a qualifying player contact and clear the net into the sender
   half. Pre-reception projectile hits do not score or arm a return; normal
   contact effects can still occur. This makes the receive-then-return order
   precise without making early shots pass through projectiles.
5. **Ball flight:** gravity-driven arcs between rebounds.
6. **Roster difficulty:** one shared shot/difficulty profile for all four
   fighters, with separate character records. Validate reachable, readable shots
   against every kit; authoring intent alone does not prove playability.
7. **Difficulty ceiling:** ramp to a challenging bounded maximum, then continue
   there. The actual envelope/ramp values need greybox tuning and playtests.
8. **Checkpoint-skipping shortcuts:** checkpoints provide optional respawn saves.
   Every required target and the finish remain mandatory, but visiting every
   checkpoint is not a completion requirement.
9. **Pilot geometry scope:** static platforms, targets and falls. Moving-platform
   support remains a separate planned feature rather than a pilot requirement.
10. **FightGuy course identity:** martial-arts rooftops and serene mountains,
    inspired by WoW's Peak of Serenity/monk setting. The maintainer specifically
    wants pole parkour, floating islands reachable with E/R, ropes and rooftops.
    Round 6 confirms static traversal/scenery for ropes and poles; concrete
    route dimensions still need greybox authoring and kit validation.

### FightGuy mountain sanctuary direction

Use the selected setting to express the character through traversal: elevated
martial-arts roofs, narrow pole tops and static floating islands linked by
upward and horizontal routes, with target opportunities along the way. Encourage
E/R and projectile use through geometry while accepting any valid solution.
The mountain setting and floating islands are maintainer-selected design motifs;
they do not establish any new movement ability or mandatory move gate.

The maintainer named **Peak of Serenity** as a reference for the atmosphere.
[Blizzard's class overview](https://worldofwarcraft.blizzard.com/en-us/news/23310980)
describes it as a place where monks study and train. The particular course props,
route and floating-island layout are this feature's design choices.

Current FightGuy E provides rise; R supplies horizontal travel and holds vertical
velocity at zero during propulsion. E/R own committed motion, so ordinary jumps
and FastFall cannot interrupt those phases. Shape upward/lateral paths separately
and validate the actual arcs, cooldown pacing and landing commitments in-game.
Static support surfaces use the normal capsule/stage geometry. There is no current
rope-climb, rope-swing, pole-attachment or balancing mechanic, and ledge-grab entry
is disabled. The selected pilot uses static pole tops and rope bridges; hanging ropes
can be scenery. New rope motion is outside this pilot's scope.

## Round 6: resolved decisions

The maintainer selected these answers on 2026-10-14.

1. **Rope/pole interaction:** static traversal using normal movement. Jump across
   pole tops and walk rope bridges; hanging ropes can be scenery.
2. **Pilot run length:** approximately 1–2 minutes for a first clean completion,
   with faster routes rewarding later record attempts. This is the authoring goal.
3. **Checkpoint prerequisites:** every required target up to that checkpoint's
   route position must be broken. Earlier checkpoint visits are not required.
   Skipping a marker may still lead to activating a later checkpoint once the
   cumulative target prerequisites are met.
4. **Defensive-state body deflection:** the same solid body in every stance.
   Shielding/dodging add no larger surface, extra velocity or intangible ball phase.
5. **Shot deadline:** none. Juggling, experimentation and waiting for normal
   cooldowns remain legal. Shipping collision/serve authoring must prevent
   unintended stuck or invalid shots; there is no disguised gameplay timeout.
6. **Unreturned sender-side floor contact:** retire that ball and re-serve the
   same authored shot without a point or difficulty/sequence advancement.
   Use a fresh ball at zero percent, with a new contact identity. Fighter state
   and ordinary attack/projectile lifetimes continue through the fixed gap.
   Own-side floor contact still ends the attempt.
7. **Attempt start:** brief locked countdown from a clean, fixed spawn. Course
   timing and the first Slopball serve start at Go. Movement/charging cannot be
   advanced or buffered into an advantage before that boundary.
8. **Pause and records:** pause freezes simulation, including ball motion,
   course time and kit timers. The attempt remains eligible for a personal best.
9. **Seed across attempts:** one fixed seed for the initial Slopball profile.
   New attempts and retries start at the same learnable sequence.
10. **Record scope:** local persistent personal bests, requiring no online account
    or connection. Online leaderboards are outside the first release.

## Round 7: resolved decisions

1. **Entry:** a dedicated main-menu Arcade entry, usable offline without an
   online match or account.
2. **Cameras:** normal orbit and existing ability-aim cameras in both challenges,
   with court framing and wall visibility adapted for readability.
3. **Court identity:** one neutral arcade sports court shared by all four fighters.
4. **Course Target geometry:** floating goals that do not block fighter movement.
   Rooftops, pole tops and islands supply the traversal surfaces.
5. **Hints:** opt-in contextual mechanic tips. Objective and route cues remain
   visible with hints disabled.
6. **Slopball record banking:** keep earned returns even on voluntary stop or
   retry. A Course time still requires a completed run.
7. **Record revisions:** archive older incompatible personal bests; compare only
   compatible gameplay. Presentation-only changes preserve compatibility.
8. **Rewards:** personal bests alone in the first release.
9. **Runtime scope:** focused Shared ball/target support and Arcade rules.
   Reuse those seams later rather than designing a general world-object framework.
10. **Stage contract:** expand the stage format so geometry and challenge data
    are bundled in one artifact, updating authoring, baking, loading and validation.
    The maintainer selected this over the proposed separate cooked definition.
11. **Implementation order:** Slopball first within the agreed release containing
    both challenges. The maintainer selected this over the proposed Course-first
    order, prioritizing roster exploration and future volleyball foundations.

Exact trajectories, serve-gap/countdown ticks and course coordinates remain
greybox tuning work within the selected rules.

## Final confirmation

On 2026-10-14 the maintainer confirmed that the consolidated brief and handoff,
including the proposed mountain route, match the intended feature and selected
**Finalize preparation**. The design session is complete, and the documents are
ready for implementation handoff. This confirms preparation; gameplay implementation
is a subsequent task.

## Verified implementation facts

These are constraints for the interview, not claims that Arcade is implemented.

- The [local bridge](../../client/Unity/Assets/Scripts/Runtime/Simulation/LocalSimulationBridge.cs)
  already runs the production Shared simulation without an online connection.
  Current Solo is a CPU stock fight, and Training is practice; neither is Arcade.
- The [match-rule interface](../../src/Shared/MatchRule.cs) owns elimination and
  winner checks. It does not supply object simulation, checkpoints or records.
- [SpellResolver](../../src/Shared/SpellResolver.cs) has sphere/capsule contact
  geometry and accepted hit data. The current simulation builds targets from
  fighter states, so an Arcade ball or destructible target requires additional
  authoritative behavior. Existing projectiles expire or explode on ground
  contact; they do not already provide the proposed ball rebound behavior.
- Static course geometry can use existing baked arena collision. New objects,
  scoring and completion remain Shared-owned; Unity presents their state.
- Existing fighter pushboxes provide stable body dimensions and positional
  separation without momentum transfer. Shield and dodge invincibility do not
  remove this physical body. Ball-body rebounds and passive return credit are
  new interactions; attack immunity and physical collision are distinct.
- Normal automatic projectile aiming uses the target's current position, without
  calculating an interception point. Ball target eligibility therefore does not
  guarantee that a ballistic projectile will hit the moving ball.
- Shared geometry can block movement, but no current rule confines a fighter to
  one court half. Baked triangles have no object-specific collision masks, so a
  player-only boundary cannot be assumed to work as an ordinary solid wall.
- The normal camera follows a moving fighter on a camera-relative X/Z plane;
  a longer course has a following anchor already. Enclosed court readability
  still needs deliberate camera/occlusion design. Lock selection and indicators
  currently expect fighter renderers even though camera focus accepts a position.
- Ability aiming can temporarily use an independent aim camera or freeze the
  orbit for a ground cursor/vector. Some no-aim-mode abilities still capture the
  ordinary camera yaw, so a permanently fixed court camera changes available
  direction control. Manki R currently uses held yaw/pitch aim and release-to-fire.
- The current `.arena` writer uses v5; its positional reader accepts v1–v5.
  It contains collision, bounds and spawns, but no targets, checkpoints, finish,
  net or sender metadata. There is no existing extensible tagged metadata section.
  The maintained authoring service universally requires a restricted collision
  hierarchy, limited components and four empty spawn markers. The selected unified
  format needs explicit marker support and stage-purpose validation.
- Stage freshness rebuilds and hashes the exact serialized bytes. A writer-version
  change can make unchanged PvP stages appear stale. The new format must preserve
  existing plain-stage freshness/export behavior or compare versions explicitly.
  Training currently assumes at least two fighters; an Arcade runner must support
  one player without inserting a fake opponent.
- Fighter package revisions/hashes already exist. Stage inspection computes
  hashes, but runtime arenas do not have a pinned challenge revision identity.
  Record compatibility must account for the challenge and kit revisions.
- Package manifests separately hash normalized runtime definitions, sampled
  deterministic poses and client bindings. Aggregate cooked/package hashes also
  include presentation data; cosmetic recooks can change them. No existing
  gameplay-only compatibility hash exists, and normalized runtime payloads also
  contain presentation/identity fields. Exact content provenance and personal-best
  compatibility need distinct treatment in the record design.
- Ordinary Stage Select uses an explicit stage allowlist. The maintained baker
  and direct offline arena loader can accept Arcade stage keys without adding
  them to the normal PvP chooser. This is a menu seam, not server admission.
- Shared timing is 60 Hz. FastFall requires a fresh Down press while descending;
  it cannot override a move's owned vertical motion. Ledge grabs remain disabled.
- Arcade record storage and an immediate challenge-retry flow are not currently
  implemented. Existing stock standings do not express these challenge results.
- Existing local settings use a versioned PlayerPrefs JSON payload independently
  of online identity; no Arcade record model or client recording-directory service
  exists. Records need their own reset/persistence policy rather than being swept
  into the general settings-default reset.
- A new LocalSimulationBridge supplies fresh simulation state. Training's partial
  reset preserves some state/clocks, and clearing live hitboxes alone does not clear
  queued explosions. Whole-attempt retry must reset the complete challenge lifecycle.
- Current Solo countdown freezes simulation but still polls some inputs, so it is
  not a ready-made fully locked Arcade start. Existing release gating clears pending
  input edges; local pause freezes simulation, cooldowns and regeneration. Solo's
  result guard captures a snapshot but does not itself freeze further ticking.
- Current local frontend play requires a locally accepted display name, but not
  Master authentication/connectivity. Arcade can use that existing offline route;
  it must not acquire a lobby or wait for an online match to run.

### Character facts relevant to later course questions

Source authoring and runtime code were inspected. These facts do not establish
matching release cooks or human acceptance of proposed course geometry.

- **FightGuy:** E supplies an upward burst and R a forward travelling attack;
  both have 240-tick cooldowns. A can launch a charged projectile along combat
  facing when no eligible combat target exists. His current identity is an unarmed
  martial-arts all-rounder with blue-white ki; no canonical home/dojo is specified.
  Course setting is a design choice rather than existing lore.
  E is upward motion, while R holds vertical velocity at zero through its travelling
  phase. Both have air variants through authored aliases and own committed motion;
  neither grants a rope/climbing interaction or replenishes jumps on use.
- **Manki:** E has a 210-tick cooldown and an initially unsteerable launch;
  R has a 240-tick cooldown and permits a real self-hit rocket jump. Its air
  movement and aimed projectile choices can support distinctive routes.
- **Wibou:** E is Rising Slash with two regenerating charges; R is the charged
  directional Dash Slash. E has no target homing. Its current code still refunds
  a charge on hit, but the maintainer selected removal of that benefit. Arcade
  will follow PvP behavior once the separate removal is implemented.
- **Bonk:** E is an aimed leap with a 240-tick cooldown and 52-tick landing
  recovery. Starting above a destination can affect its landing position.
  R can stop against an opponent, so target semantics can affect traversal.

Current [character authoring documents](../../client/Unity/Assets/CharacterPackages/)
and the runtime are the relevant evidence. Older move tables contain stale
descriptions; use them only after checking the current authoring/code.

## Decision records

The gameplay parity and legal fall-reset policy is recorded in
[ADR-0036](../adr/0036-arcade-pvp-kit-parity-and-legal-fall-resets.md).
The focused Shared object support and unified stage artifact selected in Round 7
are recorded in [ADR-0037](../adr/0037-arcade-shared-objects-and-unified-stage-artifact.md).
Vocabulary lives in the root [CONTEXT.md](../../CONTEXT.md).
