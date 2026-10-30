# ADR-0056. CPU combat plans replace per-decision dice

Date. 2026-10-29.
Status. Accepted by the owner ("go for those fixes & 2/3") for implementation. Confirm the number at merge.

## Context

CPU Opponents felt random to fight. After the step 1 movement and kit fixes, `HeuristicBotPolicy.DecideOnce` still
rolled independent dice on every decision tick (30, 14 or 4 ticks for Easy, Normal and Hard): jump, attack, punish,
shield, then retreat or circle. The move came from a seeded pick inside a score band. The action executor
(`BotPlanKind`, `ContinuePlan`) cleared as soon as the simulation accepted a press, so nothing remembered why the CPU
acted. Players could not read or learn its habits, and the tiers differed mostly by probability, not skill, although
`CONTEXT.md` defines CPU Difficulty by reaction speed, move selection, spacing and follow-up reliability.

## Options

- Keep the dice and tune the probabilities. Cheapest, but a better-tuned coin flip still reads as random.
- Continuous utility scoring each tick. Re-decides constantly, and its weights leak into every behavior.
- A behavior tree. Spreads one intention over several nodes and needs node objects on the hot path.
- A flat state machine of combat plans held per CPU, chosen only at semantic boundaries.

## Decision

Use the flat state machine.

1. `BotMemory` holds one value-typed combat plan: Approach (three variants, including Beeline), Space, Punish, Pressure,
   Defend or Edgeguard. A plan lasts until it completes or fails, with a finite guard. It keeps ownership through the
   action's resolution, not only its acceptance. The existing action executor, stage steering, grab, recovery, Mayhem
   escape and the Training Shield and Avoid behaviors stay.
2. Selection happens at plan boundaries and on recognised events, from delayed opponent facts only. Each opponent
   attack, opening or contact is one episode, recognised once. Randomness is drawn at plan entry or once per episode,
   never on a quiet tick. Neutral alternates. The CPU approaches by one of several routes, backs off to the edge of
   the opponent's reach when the exchange ends, watches, then approaches again.
3. Plans ask for moves by purpose (poke, anti-air, punish, kill, approach, follow-up, open shield) through one
   internal query. The query returns a character slot or the universal Grab. Kit priority responses keep their
   precedence inside it. Step 3 derives move purposes from today's move model. Step 4 can later feed them from cooked
   package data without touching the plans.
4. Difficulty keeps the reaction delays (24, 18 and 12 ticks). Tiers differ by recognition and reliability per episode
   (punish, defend, follow-up), hesitation, spacing precision drawn once per plan, and pressure length. Easy stays
   beatable and readable. Hard is skilled, not frame-perfect.
5. One contact recorder with Hit and Block kinds replaces hit-only feedback in Training, the CPU showcase, SelfPlay and
   `tools/CpuFeelBench`, so pressure after a block is possible.

## Consequences

- The seeded input stream changes. Identical seed and content still reproduce identical matches.
- Tests that pinned dice outcomes move to behavior assertions. The kit contracts stay, including
  `ClinchTests` (at least 24 of 32 Hard F presses), grab and throw, recovery, edgeguard restraint and Shield and Avoid.
- `BotDifficultyProfile` loses its attack, retreat, defense and jump probabilities in favour of per-episode
  reliabilities and plan timings. The values are first passes for play feedback.
- `tools/CpuFeelBench` gains scenarios for plan persistence, whiff punishes, approach variety, defense and tier
  separation, measured before and after the swap.
