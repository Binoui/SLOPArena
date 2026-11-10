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

The [variety pass](#variety-pass) below replaces this step.

## Risks

| Risk | Mitigation |
| --- | --- |
| A channel's snapshot misses a field | The restore-equivalence run covers every registered channel at every tier. |
| A forbidden move slips past the gate | Enforcement at four points, plus the reset at every Call; tests press every forbidden control raw, buffered and held. |
| A blast lands on the deadline tick | Channels schedule their last impact so the blast ends before the deadline; a test checks it per tier. |
| A kit can't pass a channel | The per-fighter pass table runs the whole cooked roster. |
| Shoves throw fighters off the deck | Shoves are bounded horizontal pushes; the pass table includes shove cases. |
| Unity breaks while Shared changes | Step 2 lands the Unity migration and the plugin copy in one Editor lease. |

## Variety pass

Added 2026-11-05 for [ADR-0063](../adr/0063-manki-mayhem-variety.md) and the brief's
[variety pass](manki-mayhem.md#variety-pass). Each unit lands its Shared and Unity work together
and ends at a check. Units 2, 3, 6 and 9 end with the maintainer playing in Local Play. Online
follows the coordinated release, as in step 4.

| Unit | Work | Check | Built status |
| --- | --- | --- | --- |
| 1 · Airings | `MayhemAiring`, `ChannelDraw` and twist rows; the deal over every regular; bosses PRIME TIME!, PRIME TIME!, GET HIM! once it exists; `ChannelSetup` and `ChannelRestore` gain the draw and tier; `MayhemShowView` and replay carry the airing; `MayhemEnumLimits`; a validated Unity copy table. Every channel has one twist: today's pattern. Before any CPU change, record each channel's CPU pass rate per difficulty. | Shared and Server suites; plan tests for the deal; restore at every tick over every airing shape; the baseline file. | Shared and Unity source built. Owner play pending. |
| 2 · Twists for today's channels | DODGE! Bomb rain and Big one; JUMP! Crossfire and Tall wave (CPU press token); BREAK OUT! Any wall and Double cage (32 walls); SPOT! Drifting and Musical (analytic zones); NO RUNNING! Fake-out and Rapid (watch kinds); BREAK THE BOARDS! Scattered. Unity draws the twist line on the CRT and HUD, moving spotlights, double cages and the new watch beats. | Restore and roster tests per twist; the CPU guard; Local Play captures of each twist. The maintainer plays. | Shared and Unity source built. Owner play pending. |
| 3 · PRIME TIME! remixes | The pair-row wrapper, contact routing by owner, the verdict fold, the verb fold, `SafeStep` and two pairs: BREAK OUT! plus DODGE!, and SPOT! plus JUMP!. The pair deck for acts one and two. Unity shows the pair on the remix card. | Certification across every twist combination of both pairs; routing and fold tests; captures. The maintainer plays. | Shared and Unity source built. Owner play pending. |
| 4 · SUMO! and SMILE! | Two goal channels on analytic zones, with SelfieFrame as a rectangle. Pairs SUMO! plus JUMP! and SMILE! plus DODGE! failed certification and are excluded. | Channel, restore and roster tests; certification; captures. | Shared and Unity source built. Owner play pending. |
| 5 · GLITCH! and BONK! | GLITCH! as a hazard on panel zones. BONK! on target windows with decoys. Pair BONK! plus GLITCH! failed certification and is excluded. | The same, plus a first-bonker test. | Shared and Unity source built. Owner play pending. |
| 6 · GET HIM! | The finale: a `DizzyHost` target with per-seat hit sets, the wake-up slam or tantrum, and Manki drawn lying on the deck. Act three closes with it. | A one-swing-counts-once test; a full-show capture. The maintainer plays the arc. | Shared and Unity source built. Owner play pending. |
| 7 · QUICK DRAW! | Swing edges from `AttackSequence`; DRAW! and fake-word cues; Manki hosts. Pair QUICK DRAW! plus DODGE! is not admitted. | A misfire test; captures. | Shared and Unity source built. Owner play pending. |
| 8 · HOT POTATO! | Bump passes, `FighterHit` from the shove branch, `CarryCue`, timed return locks and retirement, and replay carry records. | A pass-on-a-new-touch test, under both friendly-fire settings; captures. | Shared and Unity source built. Owner play pending. |
| 9 · BLOCK! | The Shield verb, barrage cues judged on `Shielding`, Block or jump rings. Thirteen regulars: no repeats. | A shield-stops-barrage-but-not-ring test; the deal test at thirteen; a full-show capture. The maintainer plays. | Shared and Unity source built. Owner play pending. |

### Who does what

- **Gates first.** Unit 1 gates everything. The CPU baseline is recorded inside unit 1, before any
  CPU code moves.
- **One owner per unit.** The registry, the cue records, `MayhemShow` and the Unity props are shared
  by every unit, so units run one after another. The Shared part of a unit runs in its own worktree.
  The Unity part lands Shared and Unity together in the main checkout under the owner's own Editor
  lease.
- **Shared state.** The main checkout and the canonical Editor are shared with other threads. Work
  stays uncommitted until the maintainer says to commit, and commits name their own paths.
- **Why not parallel.** Each new channel touches the registry, `MayhemTypes.cs`, the replay codecs
  and `MankiMayhemPresentation.Props.cs`. Parallel owners would merge-conflict on every unit for a
  small time saving.



### Built status for units 1 to 9

All nine units are implemented in Shared and integrated into the main Unity source checkout.
Offline compilation and automated checks cover code behavior. The owner's Local Play check of the full show is pending.
Online deployment still needs the coordinated release.

The deal has thirteen regular channels, twelve unique regular slots, two distinct PRIME TIME! remixes and a GET HIM! finale.
Units 1 to 3 supply immutable airings, all original-channel twists and the classic remix rows.
Units 4 and 5 supply SUMO!, SMILE!, GLITCH! and BONK! with analytic zones and timed heads.
Their proposed SUMO!/JUMP!, SMILE!/DODGE! and BONK!/GLITCH! pairs failed certification and remain excluded.
Units 6 to 9 supply per-seat finale hit quotas, timed draw words, carried potatoes and Shield barrages.
SMILE!/JUMP! certified as the third remix row; BREAK THE BOARDS!/JUMP! failed and stays excluded.
QUICK DRAW!/DODGE! is not admitted.

Unity has copy, looks, failure stamps, Shield chips, moving props and capture triggers for the new airings.
GET HIM! moves the same host from booth to deck until the verdict ends. HUD rows show each seat's quota.
QUICK DRAW! hides future words on both CRTs and HUD. HOT POTATO! follows live holders, fuse ends and timed return locks,
and draws a retirement fizzle. BLOCK! warns and blasts across the deck even with a zero-radius cue.
These visuals have offline compiler coverage; rendered readability and gamefeel await the next Editor session.

The second review fixed shared-target hit credit, shielded shoves, channel-first aim correction, rival targeting under Off,
directed potato passes, 60-tick return locks and bang grace, surplus retirement, Duel withdrawal ordering,
GLITCH!'s one/two/one-column Sweep, decoy multi-attacker failures and seeded Whack layouts.
[ADR-0063](../adr/0063-manki-mayhem-variety.md#units-6-to-9-and-the-second-review) records the as-built rules.
