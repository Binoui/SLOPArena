# Manki Mayhem implementation plan

**Status:** Channel plan, 2026-11-04, for [ADR-0061](../adr/0061-manki-mayhem-channels.md). The
[feature brief](manki-mayhem.md) owns rules and presentation. This page orders the work and names
the checks. The three-card survival show of 2026-10-27 is built and in git history; its six-step
plan is replaced here. Version numbers are those at the time of writing.

## Where the code stands

| Area | Today | Channels need |
| --- | --- | --- |
| Director | `MankiMayhemSimulation` runs three cards for eighteen rounds; Settle waits for grounded survivors | `MayhemShow`: a seed-derived show plan, a verdict book and one current channel |
| Rounds | `MankiMayhemRounds` builds SCATTER!, JUMP! and BREAK OUT! schedules | One sealed class per channel in `src/Shared/Mayhem/`, listed in `MayhemChannels` |
| Hazard hits | Ordinary hits with damage and knockback; bombs stick | Host contacts fail the contestant; DODGE! bombs explode on impact |
| Falls | Blast deaths respawn, then bench | A fall fails the channel before death processing |
| Controls | Nothing restricts controls | `MayhemControls` at input, buffer, activation and `Simulation.SimulateTick` |
| Scoring | Survival points and a last-standing bonus, enforced by `MatchResultPacket` | Pass masks, one ratings fold |
| CPUs | `MayhemThreats` card scripts that ignore difficulty | Channel intent through one adapter with the reaction delay |
| Presentation | Card switches in `MankiMayhemView`, survival stamps, booth bench | `MayhemShowView`, cue records and one event stream |
| Versions | Protocol 35, snapshot 26, World Frame 8 | New snapshot and World Frame layouts; protocol under Unreleased |

## Delivery

Each step ends at a check. The first two need no further approval. Steps 4 and 5 need the
maintainer's go.

### Step 1 · Shared core and the first slice

| Where | Work |
| --- | --- |
| Shared | `src/Shared/Mayhem/`: `MayhemShowPlan`, `MayhemShow`, `IMayhemChannel` and `MayhemChannels`, `MayhemControls`, the cue records and `MayhemShowView`, the CPU adapter, and the seven channels (DODGE!, JUMP!, BREAK OUT!, SPOT!, NO RUNNING!, BREAK THE BOARDS!, PRIME TIME!). `ServerSimulation.Mayhem.cs` routes host contacts, falls, splats and shoves. Snapshot, World Frame, replay and result codecs move to the new state. `MankiMayhemRounds.cs`, `MankiMayhemSimulation.cs` and `AI/MayhemThreats.cs` are deleted. |
| Tests | Show plan per seed; verdict latching and the deadline tick; the gate at all four enforcement points; splat without damage or launch; falls without respawn; a restore-equivalence run for every registered channel at every tier; a table showing every roster fighter can pass every channel with a scripted input; a registry test; codec and replay round-trips. |
| Check | `dotnet test tests/Shared.Tests -p:SkipUnityPluginCopy=true`, `dotnet test tests/Server.Tests -p:SkipUnityPluginCopy=true`, and a headless report of seeded four-CPU shows (verdicts, ratings, pass rates per channel). |

### Step 2 · Unity presentation and Local Play

| Where | Work |
| --- | --- |
| Unity | `MankiMayhemPresentation`, `MankiMayhemView`, `MankiHostPresenter`, `TrainingMatch`, `PvPMatch`, `ReplayModes` and `MankiMayhemCapture` read the view. Call cards with binding-aware button glyphs, ratings rows, ON AIR and CANCELLED stamps, splat poses, the SPEED UP! card, spotlights, Bashie's watch and board stacks. The Local Play card copy changes. |
| Check | Unity compile and console gate through the gateway, then a scripted Play capture of a full offline show. The maintainer's play check is filed with `tools/factory.py check add`. |

### Step 3 · The maintainer plays

Local Play against CPUs or couch pads. Tuning follows: act size, clocks, ratings, the show camera.

### Step 4 · Online

Master drops the Mayhem On setting. GameHost results carry pass masks. Two clients run a show
through Rooms. The release bumps the protocol once.

### Step 5 · More channels

Later channels land in small batches, each a class, a registry line, its cues and tests.

## Risks

| Risk | Mitigation |
| --- | --- |
| A channel's snapshot misses a field | The restore-equivalence run covers every registered channel at every tier. |
| A forbidden move slips past the gate | Enforcement at four points, plus the reset at every Call; tests press every forbidden control raw, buffered and held. |
| A blast lands on the deadline tick | Channels schedule their last impact so the blast ends before the deadline; a test checks it per tier. |
| A kit can't pass a channel | The per-fighter pass table runs the whole cooked roster. |
| Shoves throw fighters off the deck | Shoves are bounded horizontal pushes; the pass table includes shove cases. |
| Unity breaks while Shared changes | Step 2 lands the Unity migration and the plugin copy in one Editor lease. |
