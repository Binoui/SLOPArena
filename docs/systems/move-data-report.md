# Move Data Report

Telemetry tool for SLOPArena move data: runs the **real shared sim** (ADR-0019 knockback + flight law)
for a character's Custom-knockback normal hitboxes at several victim damage percents, and prints authored
frame data + simulated per-hit trajectories (with frame advantage) + a pipeline-parity safety check.

It is the "record the data we want" answer to the workflow where a frame-data question used to spawn a
throwaway xUnit test. The sim is the same `ServerSimulation` + `Simulation.ApplyKnockback` the game server
runs, so the numbers are what the game produces — not a reimplementation.

Primary use: attack duration, active frames, frame advantage, and knockback shape/range at a glance. The
combo matrix/probes are experimental diagnostics, deliberately separated from the core report.

These reports measure authored content against scripted opponents. To measure how a person actually
misses in Training or Solo (distance, height, aim, camera), use the [practice session log](practice-session-log.md).

## Explicit-input route measurements

`--route-search` searches **FightGuy versus FightGuy** using admitted cooked content
and real baked poses. `--route-replay` executes a retained witness without a chase
policy. Neither command modifies gameplay, authoring, cooked packages or roster pins.

```bash
dotnet run --project tools/MoveDataReport -p:SkipUnityPluginCopy=true -- --route-search \
  --budget 3000 --measure-budget 1000 --horizon 240 \
  --out artifacts/combo-routes/fightguy-search.json

# Omit --witness for the first result, or add --witness <id> from the report.
dotnet run --no-build --project tools/MoveDataReport -- \
  --route-replay artifacts/combo-routes/fightguy-search.json \
  --out artifacts/combo-routes/fightguy-replay.json
```

Route commands emit JSON through `--out`; they do not compose with the normal
report's `--truecombos`, `--di`, `--json`, `--html` or `--kbm` options.

| Search option | Default / bound |
|---|---|
| `--budget` | 3000; 1–100000 discovery simulations, including preparations/observations |
| `--measure-budget` | 1000; 1–100000 timing/DI simulations, separate from discovery |
| `--horizon` | 240; 30–600 simulation ticks per candidate |
| `--keep` | 6; 1–32 ranked witnesses, plus exported per-DI bests not already retained |
| `--prefix-width` | 8; 1–32 selected prefixes per DI direction |
| `--timing-radius` | 4; 0–20 ticks either side of a selected press |
| `--pcts` / `--distances` | `0,30,60` pre-hit percent / `0.8,1.2,1.6` metres |
| `--di` | `neutral,in,away,left,right`; up to nine distinct declared directions |

The finite grammar is grounded starter → grounded normal → explicit short/full
jump → aerial normal, with fresh Down/fast-fall and landing-ground continuations.
Prescribed neutral/forward/oblique movement is input, not a target-distance controller.
Successful and partial prefixes are diversified by intended slot path. All requests
still pass through Shared admission, timing, collision, gravity and interruption.
Search phases reserve budget; prefix pruning and integer timing grids are declared
limitations, not exhaustive route discovery. Inspect `grammarAttempts`,
`conditionAttempts`, `failureCounts` and the budget flags before interpreting results.

Each witness contains:

- Exact initial states, a compact **uniform flat arena** definition, package identity
  hashes, Shared/tool build hashes, effective knockback tuning and both complete input
  streams. Editor-only uncooked changes are not included.
- Requested versus actually accepted activation IDs and canonical ground/air variants.
  Contacts carry resolver activation provenance; a starter's later hit cannot count as
  a rejected follow-up. Multi-hit contacts remain distinct from connected activations.
- Damage, contact/activation counts, conversion ticks, longest no-ordinary-action
  interval, action-gap ticks, final positions and observed deaths. Tick indices are
  zero-based; contact `matchTick` is Shared's corresponding tick. A contact tick's
  ordinary defender opportunity is not discarded.
- Measured press-time intervals: only the selected attack edge moves; all other
  attacker and defender inputs remain fixed. Success requires the entire intended
  route to retain accepted, correctly attributed contacts. Bounds and incomplete
  measurements are explicit; touching a sampled boundary is not a proven full window.

`fixedRouteDi` preserves the same attacker stream and regenerates each declared DI
hold during observed defender Hitstop/Hitstun, otherwise neutral movement.
`in`/`away` are initial −Z/+Z; left/right are world −X/+X. Diagonals are
`in-left`, `in-right`, `away-left`, `away-right`. The resulting complete defender
stream is retained. `diConditionedBest` instead selects direction-specific successful
discovery witnesses; its IDs resolve to exported witnesses. These are sampled
DI-informed adaptations, not a universally guaranteed or human-reactable route.

Fast-fall ticks are observed **post-tick latch transitions**, not a second admission
rule. A latch that starts and ends on the same landing tick is not proven by this
observer; such a candidate is conservatively excluded from fast-fall witnesses.

Replay verifies content/build/tuning identity and the retained outcome hash before
writing output. `matchesExpected: true` confirms that observations reproduced; an
unreferenced standalone input replay has `matchesExpected: null`. Changed content,
malformed input, unknown/duplicate options and divergent observations fail rather than
substituting defaults or stale content. Replay JSON includes detailed per-tick states.

**Interpretation:** an executed witness proves that sequence works under its declared
conditions. `true` means no recorded ordinary-action gap between the observed contacts;
`pressure` means a gap existed. Neither label proves counterplay quality or enjoyment.
Best-found payoff is not a ceiling, and failure to find a route is not impossibility.
Use the payoff and DI/timing surface to choose playtest questions, not automatic tuning.

## Hit reliability

`tools/HitReliabilityReport` measures how forgiving each admitted normal is against
idle, standing FightGuy (medium) and Manki (small) targets, from the cooked roster.
`--slots` picks other canonical slots, ground and air, for example `--slots F` for a
special:

```bash
dotnet run --project tools/HitReliabilityReport -p:SkipUnityPluginCopy=true -- [--char all|fightguy|manki|wibou|bonk] \
  [--slots 1,3,4,A,E,R,F] [--out artifacts/hit-reliability]
```

Ability Lab runs this same sweep on the selected unsaved draft. Use **Moves →
Reach**, or `sloparena.lab.reach --action ground.1 --output tmp/reach.json` through
the Unity gateway. The command embeds `HitReliabilityDocument.ToJson()` verbatim
under `data.result.reach`; the optional output is already a standalone report
for `tools/genra/hitarc.py --report tmp/reach.json`.

Lab documents add a `draft` block with the compiled runtime hash, unsaved state,
form, and target cooked-content hashes. `Identity` remains the persisted cook
that supplies the poses. CLI output omits `draft` and retains its existing format.
For a clean package, compare the selected slot after excluding only `draft` and
`generatedUtc`. The decimal representation is portable, but Mono and .NET can
produce geometry coordinates that differ in the last float bit. Reach uses the
verified existing poses and does not rebake an
edited animation. See [Ability Lab reach](ability-lab.md#measure-reach-on-an-unsaved-move)
for the panel, overlay, freshness rules, and output path restrictions.

Each character gets `<char>.md` and `<char>.json`:

- **Playback** — baked 60 Hz animation frames per gameplay tick. Above 1.4× the clip is
  compressed to fit the move timing and the swing crosses the front in very few ticks.
- **Active ticks / touch at 1 m** — active ticks, and how many of them touch a target
  standing 1 m ahead. A long window with few touching ticks spends its time behind or above.
- **Reach** (ground normals) — real-simulation starting distances, 0.30–4.50 m in 5 cm
  steps, that connect, at 0° and 30° bearing; "same" at 30° means startup aim correction
  removes that sideways error.
- **Vertical reach** (aerials) — target feet heights relative to the attacker's feet that
  any active tick touches within 3 m ahead. An aerial that reaches less than 0.5 m below
  its feet only hits grounded targets at the very end of a fall.
- **Multi-hit coverage** (ground moves with several damaging hitboxes) — for each hit, how
  many of 24 bearings around the attacker, at 1 m and 1.5 m, put a standing target where a
  tested pose touches it. The target is never knocked away, so a zero means that hit cannot
  land on that target at all. On 2026-10-18 Bonk's Blade Storm (F) swung over every
  standing head: only its chip hitbox scored, and its four big hits were zero for both targets.
  After the [hip-height spin](../characters/bonk.md#blade-storm-spin--2026-10-18) they touched
  7–15 bearings each, and since the [storm rework](../characters/bonk.md#storm-rework), with
  one window per turn of the spin, each of its three hits touches all 24.
- **Arc** (ground moves) — a side view of where an idle target is touched by any active tick:
  rows are the target's feet height (0–3 m, 25 cm apart), columns its distance along the
  attacker's facing from 2.5 m behind to 3.5 m ahead. For weapon normals the weapon is the
  hitbox, so this is the move (owner, 2026-10-22). Sir Bonk's old Reverse Rising Slash
  touches a full front-to-back rainbow; the new Barn Door hits Manki only in the air, never
  standing. The JSON adds each move's `ActiveHitboxes` (tick, start xyz, end xyz, radius in
  the grounded origin frame) and the targets' idle contact volumes (`targets`), which
  [GenRA's `hitarc.py`](../../tools/genra/README.md#weapon-arcs) uses to judge an arc before
  it is cooked.
- **Bone and weapon sampling** — positions a standing target occupies on the swing path
  between consecutive active ticks (the clip sampled 16 times per tick) that no tested pose
  touches. Tested poses are each tick's pose plus the swept poses Shared tests between
  active ticks, so this stays near zero; a rise means the sweep stopped covering the swing.

Flags mark fast clips, point-blank whiffs, aerials that cannot reach below their feet,
hits of a multi-hit move that never touch a standing target, and sampling loss above 10%.
The attacker stays fixed for the geometry columns, and targets
are idle; human timing and movement belong to the [practice session log](practice-session-log.md).
Compare a character's report before and after a re-authored attack is cooked.

## Aerial experiments

Two opt-in headless tools measure the existing cooked gameplay without changing default
bot behavior, momentum, hitboxes, or package data:

```bash
dotnet run --project tools/AerialApproachReport -p:SkipUnityPluginCopy=true -- --out artifacts/aerial-approach --assert
dotnet run --project tools/AerialProfileReport -p:SkipUnityPluginCopy=true -- --out artifacts/aerial-profiles --selfcheck
```

`AerialApproachReport` sweeps real jump attack timings for all four admitted characters'
air.1–4 against stationary and fixed-path targets. It compares zero drift with running
approaches, straight/oblique directions, four target distances, and small/large admitted
targets. Positions are matched at takeoff. JSON contains actual relative states, active
frames, contact identities, accepted/rejected casts and contiguous successful press
windows; CSV files provide cast, window and Bonk ground.3 coverage tables.
The grid includes deliberately bad timings: its aggregate hit rate is not a player
accuracy estimate. Zero-drift versus running changes the approach trajectory, not an
attack-only momentum setting. Bonk contact identities describe actual cooked operations,
not inferred visual animation parts.

`AerialProfileReport` runs current/current, jump-in/grounded and jump-in/jump-in mirrors
for each admitted character, seeds 42–46 and swapped sides, Normal difficulty, three
stocks and a 10,800-tick cap. `--seeds 42` restricts the seed set. The wrappers preserve
the base policy's attack selection and recovery safeguards; they change voluntary jump
inputs, not aim quality. These are experimental policies, not trained aerial players.
Outputs include raw match JSON, provenance, and aggregate CSV with entity-minute exposure,
offensive/recovery/ambiguous aerial classifications and connected activation counts.
Recovery classification follows move metadata, not inferred player intent. Temporal
jump-in totals use the union of overlapping 90-tick windows per entity; they are
associations, not causal attribution. Self-hit damage is labeled separately.

### Recorded findings and decision — 2026-09-15

These are historical measurements of the cooked packages below, not balance targets.
The commands above reproduce the experiment design against the currently installed
roster; changed content or simulation code can produce different results.

- **Demonstrated and fixed:** FightGuy's bot budgeted Cyclone Kick travel using 40 ticks
  instead of its 70-tick timeline, and could chain an aerial attack preserving outward
  momentum. Bot travel checks were corrected without changing the move. In the same
  15 non-mirror matchups, deaths fell 43→22 and losses 13→3, with timeouts rising 2→12.
  Hit attribution, pre-respawn death context and cumulative damage accounting were also
  corrected; see `docs/plans/policy-ai-self-play-telemetry.md`.
- **Controlled sweep:** 74,752 trials, 38,400 accepted aerial casts. Zero-drift casts
  connected 1,006/19,200 (5.24%); running casts 606/19,200 (3.16%). Four target distances
  (0.75, 1.25, 1.75, 2.25 m), straight/oblique approaches, stationary/fixed-path targets,
  small/large admitted targets and jump timing offsets were swept. Actual positions
  matched at takeoff. This measures whole-trajectory sensitivity, not attack-time
  momentum in isolation; intentionally bad timings make these unsuitable as player
  accuracy estimates. Successful windows were not uniformly narrower at running speed.

| Character | Zero-drift connections / accepted casts | Running connections / accepted casts |
|---|---:|---:|
| FightGuy | 370 / 4,736 (7.81%) | 152 / 4,736 (3.21%) |
| Manki | 232 / 4,608 (5.03%) | 188 / 4,608 (4.08%) |
| Kistu | 120 / 4,736 (2.53%) | 13 / 4,736 (0.27%) |
| Bonk | 284 / 5,120 (5.55%) | 253 / 5,120 (4.94%) |

- **Profile experiment:** 120 matches using the parameters above. Offensive aerial
  activation connection rates in current mirrors versus jump-in mirrors were FightGuy
  17.2%→25.0%, Manki 17.0%→17.8%, Kistu 11.7%→15.8%, and Bonk 6.6%→6.3%.
  This does not show universal failure when both bots jump; attack selection and
  engagement situations remain confounds. Recovery-tagged and ambiguous moves are
  excluded, but authored tags do not establish actual intent.
- **Bonk coverage:** ground.3 resolved to wire slot 8 and contained one authored hitbox
  spawn at tick 17. The stationary grid connected in 23/60 cells. There was no separately
  authored later contact to compare with the first visual animation part. The player's
  reported small-target frontal miss remains a visual/coverage investigation, not a
  demonstrated momentum defect. The separately reported broken slam landing hitbox
  must not be treated as evidence of general aerial difficulty.
- **Verification:** both complete experiment assertion/self-check runs and tool builds
  passed. Current-profile output matched `SelfPlayMatch` for all four characters at seed
  42. These were headless measurements; Unity visual contact and human gamefeel were not
  verified.

**Decision:** park further broad bot/AI experiments. Prefer a recurring human-reported
interaction, ideally with footage and intended action, then use the simulation to
explain the failure and test one bounded change. No global momentum change, homing
commitment, or movement ADR follows from these results.

All measured packages were version `0.0.0-dev`, with cooked definitions and baked poses
loaded. Exact package hashes retained here keep the summary independent of temporary
raw-artifact paths:

| Package | Package hash |
|---|---|
| fightguy | `ed91019bc96fe42db06f75d55c5f531fa6c1df0805b38920036272d96e762722` |
| manki | `234b9cb6c244f8debee3dcf5e40b87d2afb3f58148f55122d37f104a10a6374a` |
| kistu | `fa55982d486e030d74a495895b3b72699680abad4e783454712ae1f8bb179b38` |
| bonk | `c31ec969f28e2a99e41669e10c981ae2c652bbeea7e47de7d4ecf66546922da1` |

The working tree was uncommitted; these content hashes do not pin the simulation/tool
revision. Full raw traces are regenerable and are not required documentation artifacts.

## Usage

```bash
scripts/move-data.sh fightguy --pcts 0,30,60,90,120 \
  --out docs/generated/fightguy-move-data.md
scripts/move-data.sh fightguy --pcts 0,30,60,90,120 \
  --html docs/generated/fightguy-move-data.html
scripts/move-data.sh fightguy --pcts 0,30,60,90,120 \
  --json /tmp/fightguy-move-data.json
scripts/move-data.sh fightguy --example --pcts 0,60,120 \
  --json docs/generated/fightguy-move-data.example.json
```

- `<char>`: `fightguy` (default) | `manki` | `wibou` | `bonk`, resolved through the cooked roster.
- Default markdown output: `docs/generated/<char>-move-data.md`.
- `--example` limits JSON/HTML collection to the representative grounded g2 first hit. Use it only for a
  small committed schema fixture; normal reports still collect every normal hit.
- Collection: every `HitboxEvent` from slots 1–4 + air 1–4 (first stage only), **regardless of knockback
  profile**. Launch resolves as: Custom/Adaptive use their authored base/growth (Adaptive's authored angle
  is used as a representative), named profiles resolve from the `KnockbackProfile` table. Frame data is
  always exact.

For an exact regeneration, run the markdown and HTML commands separately because JSON/HTML output exits
after writing its requested report. Full JSON is intentionally ignored; regenerate it at a temporary path
or another ignored location. `generatedAt` is wall-clock metadata, so deterministic comparisons must remove
that field; scenario ordering, values, and samples are otherwise stable.

## Visual report (`--json` / `--html`)

The tool also emits a **lossless JSON** report and a **self-contained HTML** visual report (no external
deps). The JSON is a per-tick dump (100s of KB–low MB) — **gitignored**, regenerate on demand rather than
committing; the `.html` and `.md` renderings of the same data are small enough to commit under
`docs/generated/`.

```bash
scripts/move-data.sh wibou --json /tmp/wibou-move-data.json
scripts/move-data.sh wibou --html docs/generated/wibou-move-data.html
```

- **`--json`** — structured report: per-move frame data, per-tick trajectory arcs, adv per move×%, kill% +
  blast clearance. The lossless source for any future renderer.
- **`--html`** — three human-readable sections:
  1. **Frame-advantage heatmap** — rows = moves, columns = victim %, cells colored green(+)→red(−) by adv
     (saturating at ±40 ticks); click a column header to sort. The g2 ramp (−15 → +24) reads at a glance.
  2. **Knockback shape gallery** — small-multiple SVG arcs (height vs horizontal travel), one per move × %,
     red apex dot, KV / apex / stun caption. The "shape" the trajectory table hides in numbers.
  3. **KO & blast clearance** — kill % (lowest victim % crossing a blast line, binary search 0→250%) plus
     progress bars for how close each move gets to the top / side blast line at the highest simulated %.

### JSON contract

`metadata` identifies the report character, requested pre-hit buckets, representative victim character
and `CharacterDefinition.Weight`, the grounded idle origin state, 60 Hz tick rate, DI mode, and the
termination rule. Each trajectory also carries its authored angle, actual launch velocity XYZ and
horizontal/vertical components, hitstun-expiry tick and position/distance/height, apex tick, landing
tick, and a termination reason. Each `points` row contains the tick, absolute position XYZ, effective
velocity XYZ, and `inHitstun`, plus the existing height/travel/phase display fields. Nullable expiry,
apex, and landing fields remain null when the trajectory reaches a cap instead.

### Standard trajectory scenario

FightGuy reports use FightGuy as both attacker and representative victim, with `CharacterDefinition.Weight
= 100`. The victim starts at `(0, CapsuleHeight/2, 0) = (0, 0.85, 0)` in `Idle`, grounded, facing yaw
`0`, with no movement or DI/SDI input. Buckets `0,30,60,90,120` are pre-hit damage; the launch uses the
bucket after the hit's damage is added. Hitstop is reported but not stepped. A run ends at landing, a
report-arena blast-line crossing, 1200 sampled points, or 2400 simulation ticks.

### Kill-% geometry

Kill % is arena-relative. The report's own arena (flat 200×200, no bounds) auto-resolves to sides ±∞, so
nothing ever kills on it. The tool therefore computes KO on a **Crossroads-style proxy**: flat 60×60,
`KillHeight −10`, bounds ±30 → auto top +20, sides ±40. Center launch, victim passive (no DI/SDI). The proxy
is labelled in the JSON/HTML so the assumption is explicit.

**Finding (2026-08-17):** no normal KO ≤ 250% on the proxy — the game's knockback magnitudes (apex ≤ ~9 m,
side travel ≤ ~19 m at 250%) are small vs the blast-zone distances. That is a design signal (these are combo
moves, not finishers, and/or blast zones are deep). Use **blast clearance** to read how close each move gets:
e.g. Kistu g3 Up Slash apex = 43% of the way to top blast.

## True-combo reachability (`--truecombos`)

This older opt-in graph runs real contacts under a greedy chase against a passive
defender. Its fixed speed/range gates, sampled starter placement and variant selection
are **policy restrictions**, not an exhaustive search of player inputs. The new
explicit-input route commands above do not use that chase or its damage-delta classifier.

- `true`/`false`/`never` are the old policy's verdicts, not proof of universal combo
  reliability, defender response or impossibility. Do not use their density as a
  gameplay balance target.
- “Tightness” is authored timing-budget arithmetic, not a measured successful press
  interval. The explicit route timing measurements perturb actual input edges instead.
- Historical zero-link output from 2026-08-17 used older content/tuning and this
  heuristic. It does not establish that current kits cannot combo.
- The old `--di` trajectories are separate from its combo graph; composing flags
  does not test each follow-up against defensive DI.

Output: JSON/HTML sections (per-starter reachability tables + density summary), plus a markdown section on
the default path. Flags compose with `--json`/`--html`.

## DI escape-space (`--di`)

How much a victim can bend each send with DI. Each trajectory is re-run with the victim holding each of
the four stick directions during hitstun — `in` (MoveY −1, toward the attacker / opposite the launch
axis), `away` (MoveY +1), `up` (MoveX +1), `down` (MoveX −1), the sim's DIX/DIY convention. The launch is
rotated by the sim's real `Simulation.ApplyDirectionalInfluence` (18° cap, Melee sin² curve — perpendicular
holds bend most; along-axis holds only give the expiry ASDI push) and the stick stays held through stun.

- **Escape magnitude** = max launch-vector deviation across the four holds (degrees). Low = DI-resistant
  (reliable combo/kill tool); high = DI-bendable (escapable). The deviation follows the sim curve: for a
  horizontal launch it is `18° × cos(elevation)` (e.g. 16.7° at a 22° launch).
- Output: the four variant arcs overlaid on the knockback-shape gallery (thin colored arcs, baseline stays
  solid blue), per-figure max deviation, and a markdown table on the default path.

## Authored hitbox reach (`--reach`)

Deterministic, per-move answer to "what does this move cover, and where are the gaps in the kit?" — the
authored complement to the empirical heat spots. For every collected normal (slots g1–g4, a1–a4, first
stage, every `HitboxEvent`), the tool resolves the **real sim hitbox volumes** over the active frames and
renders a side-view overlay per move, a range ladder, coverage gaps (whiff zones between moves), and
uncovered height bands.

- **Geometry**: `HitboxGeometry.ResolvePositions` per active tick (`AttackElapsedTicks = trigger + t`,
  `t ∈ [0, duration)`) at the grounded origin frame — character at `(0, CapsuleHeight/2, 0)`, yaw 0, so
  feet are at y=0 and forward is +Z. Same function `ServerAbility.SpawnHitbox` uses; baked skeleton used
  when present, entity-relative fallback otherwise. Authored geometry only — no buff bonuses.
- **Bands**: thirds of `CapsuleHeight` from the feet — low `[0, H/3)`, mid `[H/3, 2H/3)`, high
  `[2H/3, H + 0.5]` (0.5 m headroom so above-head coverage counts as high). **Reach** = max forward (Z)
  extent of the side-view envelope (X flattened) over the bands.
- **Gaps**: per band, per 0.1 m height row, the covered intervals across moves clamp to `[0, maxReachAtY]`
  (the kit's max reach at that height); holes are whiff zones. Consecutive rows whose extent agrees merge
  into one gap. **Uncovered bands** = height bands no normal reaches at all.
- Output: lossless JSON (`reach` node: per-hit capsules with per-tick endpoints/radius, band extents,
  gaps, uncovered bands) + a self-contained HTML section (side-view SVGs, range ladder, gap list), plus a
  markdown section on the default path. Flags compose with `--json`/`--html`.

```bash
scripts/move-data.sh wibou --reach --html docs/generated/wibou-move-data.html
```

**Reading the ladder**: reach sorts the kit's normals by how far forward they extend — the answer to
"my move whiffs at this spacing" and "what do I use to poke at 1.2 m vs 1.5 m". Multi-hit moves get one
row per hit (their capsules differ). A `—` band cell = the move never covers that height; a gap line
like `- high @ 2.1–2.3 m: nothing covers 0.0–0.8 m` means the kit's high-band coverage starts late
(above-head hits only connect close-in) — reads as an anti-air weakness at range.

## Report sections

### 1. Frame data (authored)

Pure constants from the ability spec, zero simulation:

`move | hit | trigger | active window | dmg | angle | base | growth | stun | IASA | landing lag | AC before | AC after | total duration`

Instant answer to "how long is this attack / when does it hit?" — no sim needed.

### 2. Per-hit trajectories (simulated)

One row per hitbox × per victim %, the knockback shape:

`KV m/s | hitstop | stun | adv | advL | rise@stun | drift@stun | apex | actionable tick | landed tick`

Method: launch a fresh grounded victim at the given % via the real `Simulation.ApplyKnockback` with the
hitbox's authored values (angle, base, growth, damage, stun, weight), then step the sim until landing
(cap 2400 ticks). No DI/SDI input. Hitstop is reported (`ComputeHitstopTicks`, ADR-0019:
`min(12, dmg/3 + 6)`) but not simulated — flight starts at launch. The launch is computed at
`pct + damage` (the game applies damage before the queued launch).

**`adv`** — on-hit frame advantage = `stun − (IASA − trigger)`. Positive = the attacker acts before the
victim leaves hitstun. This is the headline number for follow-up pressure.
**`advL`** (aerials) — the landed follow-up pays `LandingLagTicks` on top (SHFFL-style); land inside an AC
window (`≤ AC bef` / `≥ AC aft`) or chase with an aerial at IASA and the lag is skipped, so the true landed
number sits between `advL` and `adv`.

### 3. Pipeline parity (safety)

The report's rows launch through a direct `ApplyKnockback` call; the game launches through the real path
(input → baked-bone hitbox resolution → `ResolveHits` → hitstop queue → queued launch). This section runs
the real path for each slot's first hitbox at 0% and the last requested %, and compares applied KV/stun/apex.
Any `DIVERGE` means the game behaves differently from the report — investigate before trusting feel tuning.

This exists because the 2026-08-14 x87 float comparison bug silently scaled nothing in the game while .NET
tests were green.

## Opt-in / experimental

- `--combos` — combo matrix (no-travel bound `TA = (IASA − trigger) [+ landing lag] [+ jump squat] +
  follow-up trigger`; `TA < stun` ⇒ frame-true) + movement probes (greedy AI chase policy, real sim,
  verdicts T/C/L/-). This encodes **scripted route strings**, which contradict the freeform-combo design
  goal. Diagnostic only — do not build balance decisions on it.
- `--traj <slot>` — raw per-tick CSV launch trace for one grounded hit
  (`tick,height(m),travel(m),vY,vX,phase`).
- `--shape [step]` — knockback feel surface: the real sampled arc every `step` ticks (default 12 ≈ 0.2s)
  per hit per %, phase markers (H=hitstun / F=flight / A=apex / G=landed), plus a one-line KV + stun
  summary per block.
- `--pipe`, `--dll <path>`, `--parity` — internal diagnostics. `--dll` loads a `SlopArena.Shared.dll` in an
  isolated load context and runs its `ApplyKnockback`, proving what the file on disk does regardless of what
  the Unity editor loaded (stale-DLL detection).

## Caveats

- **Adaptive moves are approximated.** Melee-361 auto-angle (Kistu's Quick Slash / Air Slash / Reverse
  Slash) has no fixed launch angle — the real one varies with hit position. The report simulates them with
  the authored angle as a representative and tags them `adaptive`; frame data is exact. This is why a
  tagged move's direct trajectory may not match its real in-game launch.
- **Multi-hit parity false positive** — a 2-hit starter (e.g. Kistu g2 Double Slash) re-launches the victim
  with hit 2 in the pipeline run, inflating pipeline apex vs the single-hit direct row → `DIVERGE`. Matching
  KV/stun is the signal it's an artifact.
- **Per-character combo routes** — only FightGuy has authored routes (`DefaultRoutes` in `Program.cs`). Add
  a character's designed links to `RoutesFor` to get its combo matrix.
- Hitstop reported but not simulated (flight starts at launch).
- Apex/rise/drift assume the hit connects on the first active frame.

## Flow

1. Build the report with `dotnet build tools/MoveDataReport/ --nologo -p:SkipUnityPluginCopy=true`, or run `scripts/move-data.sh` directly. The project reference builds Shared without copying its plugin into Unity.
2. Run the report with the required options. The wrapper already supplies the offline flag; direct `dotnet run` commands need it unless they use `--no-build`.
3. Read the markdown and compare it with the character's authoring package and admitted cooked content. See `docs/characters/<char>.md` for design context.
4. Commit regenerated `docs/generated/*.md` alongside a behavior change when it should be a changelog
   artifact. `--parity` divergence with in-game feel → check the launch-contract sentinel using
   the Unity CLI/Pipeline workflow in `docs/contributing/unity-cli.md`.

## Key files

| File | Role |
|---|---|
| `tools/MoveDataReport/Program.cs` | the tool (CLI, hit collection, sim runs, markdown) |
| `tools/MoveDataReport/MoveDataReport.csproj` | console project, `net8.0`, refs `SlopArena.Shared` |
| `scripts/move-data.sh` | wrapper that builds/runs MoveDataReport with `SkipUnityPluginCopy=true` |
| `src/Shared/Simulation.cs` | `ApplyKnockback` (launch) |
| `src/Shared/ServerSimulation.cs` | the tick loop + `ComputeHitstopTicks` |
| `client/Unity/Assets/CharacterPackages/<package>/character.json` | editable gameplay source; cook changes before measuring admitted content |
| `content-cooked/roster/manifest.json` and `src/Shared/BuiltInContentResolver.cs` | pinned cooked package revisions loaded by the report |
| `docs/generated/*-move-data.md` | committed report artifacts |
