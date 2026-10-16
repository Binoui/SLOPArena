---
name: sloparena-move-data-report
description: "Record and report a character's move data — authored frame data (trigger/active/duration/IASA/landing lag), simulated per-hit trajectories with frame advantage, knockback shape, or a full eight-normal animation/hurtbox/role audit — via scripts/move-data.sh → tools/MoveDataReport."
metadata: {"triggers": ["frame data", "move data", "frame advantage", "knockback shape", "record frame data", "true combo", "combo graph", "combo density", "DI escape", "directional influence", "normal audit", "character normal audit", "kit audit", "animation hitbox audit", "hurtbox timing"]}
---

# SLOPArena Move Data Report

`scripts/move-data.sh` wraps `tools/MoveDataReport` (dotnet console, `net8.0`). It runs the **real shared sim**
(`ServerSimulation` + `Simulation.ApplyKnockback`, ADR-0019 flight law) for a character's Custom-knockback
normal hitboxes at several victim damage percents, and writes a markdown report. No xUnit, no assertion, no
golden file — pure telemetry for a human to read. Reference: `docs/systems/move-data-report.md`.

## Aerial/gamefeel measurement safeguards

Use the opt-in `AerialApproachReport` and `AerialProfileReport` commands and schema notes
in `docs/systems/move-data-report.md`; keep experiment results there, not in this skill.

- Start with the player's intended action and recurring frustration, ideally footage.
  The player supplies experience, not a required diagnosis. Simulation explains what
  happened; headless success cannot establish satisfying controls or visually fair contact.
- Normalize policy comparisons by entity-minutes: ticks / 3,600 at 60 Hz. A mirror
  aggregate contains two players; a jump-in side against grounded contains one.
- Count connected attack activations, not individual multi-hit contacts. Separate
  offensive, recovery-tagged and ambiguous moves; metadata is not proof of intent.
- Match positions at actual takeoff and measure actual velocities. Shared normalizes
  nonzero movement input, so reducing stick magnitude does not establish lower speed.
  Distinguish not-yet-airborne, rejected casts, landing, and accepted aerial casts.
- Sweep useful distances as well as press timings; report disjoint contiguous success
  windows separately. A sweep containing intentionally bad opportunities is a coverage
  measurement, not player accuracy. Compare matched conditions before pooling results.
- Zero-drift versus running changes the whole approach. It does not isolate retained
  momentum during an attack or justify braking, homing, or global tuning by itself.
- Separate actual authored contact identities from visual animation parts. Known broken
  moves and target-size coverage issues must not become evidence of global movement flaws.
- Union overlapping temporal windows per entity before aggregating damage/deaths; label
  temporal association and self-hits explicitly. Preserve content hashes and limitations.
- Stop broad collection when it no longer distinguishes actionable hypotheses. Prefer
  one human-reported interaction, one bounded experiment, then human feel validation.

## Usage

```bash
scripts/move-data.sh <char> [--pcts 0,30,60,90,120,150] [--out docs/generated/<char>-move-data.md]
scripts/move-data.sh <char> --json report.json --html report.html   # visual report
scripts/move-data.sh <char> --truecombos --di --html report.html    # + true-combo graph + DI escape-space
```

- `<char>`: `fightguy` (default) | `manki` | `wibou` | `bonk`, resolved through the cooked roster.
- Default markdown output: `docs/generated/<char>-move-data.md`.

### Analysis modes (issue #147)

- `--truecombos` — freeform true-combo reachability graph: for each normal × hit state (grounded/airborne)
  × %, the real sim runs the starter and attempts every follow-up (greedy chase, IASA early-out press).
  **T** = follow-up landed while the victim was still in hitstun, **F** = landed after stun expired,
  `-` = never. Per-edge window tightness (`sim stun − recovery − follow-up trigger`) + per-% combo density.
  **Finding (2026-08-17): both characters currently have zero true combos** — the sim derives hitstun from
  launch speed (`0.5 ×` unscaled KV; authored `StunTicks` is a zero/nonzero gate) and `KbScaleFactor 0.14`
  carries the victim out of reach before any follow-up can activate. Combos are reads/movement, by construction.
- `--di` — DI escape-space: each trajectory re-run with the victim holding each stick direction through
  hitstun (`in`/`away` = MoveY ∓/±1 along the launch axis, `up`/`down` = MoveX ±1 perpendicular), rotated by
  the sim's real `ApplyDirectionalInfluence` (18° cap, Melee sin² curve). Max launch-vector deviation =
  escape magnitude — low = DI-resistant (reliable kill tool), high = DI-bendable (escapable). Overlaid on
  the knockback-shape gallery.

## Visual report (`--json` / `--html`)

The tool also emits a **lossless JSON** report and a **self-contained HTML** visual report (no external
deps). The JSON is a per-tick dump (100s of KB-low MB) — **gitignored**, regenerate on demand; only the
`.html`/`.md` renderings commit under `docs/generated/`. One richer collection feeds both; the markdown
path is unchanged.

- `--json <path>` — structured: per-move frame data, per-tick trajectory arcs, adv per move×%, kill% + blast clearance.
- `--html <path>` — three human-readable sections:
  - **Frame-advantage heatmap** — rows = moves, columns = victim %, cells colored green(+)→red(−) by adv; click a column header to sort.
  - **Knockback shape gallery** — small-multiple SVG arcs (height vs travel) per move × %, apex dot, KV/stun/apex labels.
  - **KO & blast clearance** — kill % (lowest victim % crossing a blast line) + bars for how close each move gets to top/side blast.
- **Kill % geometry**: binary search 0→250%, lowest % at which the launch crosses a blast line on a
  **Crossroads-style 60×60 proxy** (top +20, sides ±40, bottom −10), center launch, no DI. Blast clearance
  is the fraction of the way to top/side blast at the highest simulated % — always populated.
- **Finding (2026-08-17):** no normal KO ≤ 250% on the proxy — knockback magnitudes are small vs blast-zone
  distance. Read clearance to see "up-slash apex is 43% of the way to top blast."

## What it reports (the core — frame advantage lives here)

**1. Frame data (authored)** — pure constants from the ability spec, no sim: trigger, active window,
damage, angle, base KB, growth, stun, IASA, landing lag, auto-cancel before/after, total duration.
Instant answer to "how long is this attack / when does it hit?"

**2. Per-hit trajectories (simulated)** — one row per hitbox × per victim %, the knockback shape:
`KV m/s | hitstop | stun | adv | advL | rise@stun | drift@stun | apex | actionable tick | landed tick`.
- **`adv`** = on-hit frame advantage = `stun − (IASA − trigger)`. Positive = attacker acts before the victim
  leaves hitstun. **This is the headline number.**
- `advL` (aerials) = landed follow-up pays landing lag.
- Method: fresh grounded victim at that %, launch via the real `ApplyKnockback` with the hitbox's authored
  values, step the sim until landing (cap 2400 ticks). No DI/SDI; hitstop reported but not simulated.

**3. Pipeline parity** — safety: runs the *real* hit path (input → baked-bone hitbox → `ResolveHits` →
hitstop queue → queued launch) and compares applied KV/stun/apex vs the direct-formula rows. Any `DIVERGE` =
the game behaves differently from the report. Existing because the 2026-08-14 x87 float bug silently
scaled nothing while .NET tests were green.

## Opt-in / experimental — NOT part of the design loop

- `--combos` — combo matrix (no-travel bound `TA < stun`) + movement probes (greedy AI chase, real sim).
  Encodes **scripted route strings**, which contradict the freeform-combo design goal. Keep it as a
  diagnostic only; do not build balance decisions on it.
- `--traj <slot>` — raw per-tick CSV launch trace for one grounded hit.
- `--shape [step]` — knockback feel surface: sampled arc every ~0.2s per hit per %, phase markers
  (H=hitstun/F=flight/A=apex/G=landed) + one-line KV/stun summary.
- `--pipe`, `--dll <path>`, `--parity` — internal diagnostics (see
  `docs/contributing/unity-cli.md` for the Unity CLI/Pipeline parity check and in-game
  launch-contract sentinel comparison).

## Caveats (read before trusting a report)

- **Every move is included regardless of knockback profile.** Frame data (active / duration / IASA / stun)
  is always exact. Trajectories use the resolved launch: Custom/Adaptive use their authored base/growth;
  named profiles (Light/Medium/…) resolve from the profile table.
- **Adaptive (Melee-361) moves** (e.g. Wibou's Quick Slash / Air Slash / Reverse Slash) are simulated with
  their **authored angle as a representative** — the real launch angle varies with hit position (a level hit
  sends flatter). Tagged `adaptive` in the HTML report with an in-report caveat; frame data is unaffected.
- **Multi-hit moves false-positive parity.** A 2-hit starter (e.g. Wibou g2 Double Slash) re-launches the
  victim with hit 2 in the pipeline run, inflating pipeline apex vs the single-hit direct row → `DIVERGE`.
  KV/stun still matching is the signal it's an artifact, not a real drift.
- **Per-character combo routes:** only FightGuy has authored routes in `RoutesFor` (`DefaultRoutes`). Add a
  char's designed links there to get its combo matrix.
- Hitstop is reported (`ComputeHitstopTicks`) but not simulated — flight starts at launch.

## When to run

- User asks for attack duration, active frames, frame advantage, knockback shape/range, or a move-data table.
- After a balance/timing change to a character's normals — regenerate and read the diff as the changelog.
- If a report disagrees with in-game feel, run `--parity` and compare with the launch-contract
  sentinel using the Unity CLI workflow in `docs/contributing/unity-cli.md`.

## Character normal audit — all eight normals

Use this workflow when the request is to assess a character's ground and air normals as a
kit: whether the animated contact matches its hitbox/hurtbox timing, and whether damage,
knockback, timing, and role form a coherent Melee-inspired normal tier.

The deliverable is an **evidence-backed role proposal**, not a report dump and not an
animation-only opinion. Complete the evidence pass before proposing data changes; in the
interactive main session, explain the proposal and wait for approval before editing.

### Applicability and source of truth

- Audit the full normal tier: `ground.1`–`ground.4` and `air.1`–`air.4`. Treat each
  `SpawnHitbox` operation as an independently authored contact, not merely each input label
  as one move.
- Server simulation is authoritative. Read the package's `character.json` and cooked
  definition. Use `HitboxGeometry.ResolvePositions` /
  `ServerSimulation.BuildEntitiesFromState` geometry and simulation outcomes. Never infer
  gameplay behavior solely from a client animation.
- Ability Lab is the visual truth surface: it scrubs the same sim-tick pose, green hurtboxes,
  and orange hitboxes that the server resolves. See `docs/systems/ability-lab.md`.
- The report requires usable normal data. Wibou's Adaptive moves are representative-angle
  trajectories. Missing authored hits are a content prerequisite, not a balance verdict.

### Evidence pass

1. **Inventory authored data.** For every ground/air normal, record name, animation, trigger,
   active range, duration, IASA, landing lag/auto-cancel, shape, radius, bone/end bone,
   damage, angle, base KB, and growth.
2. **Build and record the real sim.** Run markdown and HTML separately so both committed
   renderings are refreshed:

   ```bash
   dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true
   scripts/move-data.sh <char> --pcts 0,30,60,90,120,150 --truecombos --di --reach \
     --out docs/generated/<char>-move-data.md
   scripts/move-data.sh <char> --pcts 0,30,60,90,120,150 --truecombos --di --reach \
     --html docs/generated/<char>-move-data.html
   ```

   Read frame data, trajectory rows, pipeline parity, true-combo reachability, DI
   escape-space, and the reach ladder. A `DIVERGE` parity row blocks conclusions until
   resolved.
3. **Scrub contact geometry in Ability Lab.** Select each ground and air slot, jump to every
   hitbox trigger, then scrub through `trigger + duration - 1`. Check the animated limb/capsule
   against the green attacker hurtboxes, orange hitbox, and an optional red dummy at intended
   spacing. Repeat for early/late events and all multi-hit contacts.
4. **Audit overlap semantics.** Adjacent sweetspot/sourspot or body-covering events that are
   meant to produce one hit must share a nonzero `HitGroup`; verify a stationary target does
   not take both hits. Intentional separate contacts must retain distinct hit identities.
5. **Interpret the real outcome.** Do not treat authored `StunTicks` as actual hitstun:
   under ADR-0019 it is a zero/nonzero gate, while launch speed determines actual stun.
   A move is a true combo starter only when the real-sim graph reports **T**; **F** and `-`
   are not combo claims.

### Role decision

Give each normal one primary job. Common roles are quick low poke, forward spacing/check,
cross-up/side check, vertical anti-air, body-covering linger, aerial two-hit check, grounded
kill read, and forward-air kill read. Roles may share a secondary use, but two moves must not
silently become the same tool.

Judge a proposed role from all four evidence sources:

| Evidence | Decision it supports |
|---|---|
| Active tick + IASA / landing lag | commitment, whiff cost, and whether the move is fast or deliberate |
| Baked pose + hitbox/hurtbox geometry | whether the contact is visually honest and covers its intended space |
| Real launch, stun, DI, and blast clearance | poke, anti-air, launcher, or kill-read reward |
| True-combo graph + reach ladder | whether a claimed route exists and which spacing/height gap the move fills |

### Audit record template

Write one compact card per normal in the review or character documentation:

```text
<slot> <move>
Role: <one primary role>
Animation contact: <limb/capsule and active ticks>
Hit model: <shape, anchors, radius, HitGroup behavior>
Reward: <damage, angle, KB; real 0% and high-% outcome>
Cost: <startup, total, IASA, landing lag>
Evidence: <reach / DI / parity / combo verdict>
Decision: <keep or exact change and reason>
```

### FightGuy worked example — 2026-08-19

The completed FightGuy pass is the reference shape:

| Normal | Final primary role |
|---|---|
| g1 Low Kick | quick low neutral poke |
| g2 Straight Punch | fast mid-range forward check |
| g3 Sweeping Kick | lateral/cross-up check that lifts upward |
| g4 Double Kick | slow grounded horizontal kill read |
| a1 Double Punch | two-contact airborne poke |
| a2 Floating Kick | body-covering sex kick; strong early, weak late, one hit total |
| a3 High Kick | high-angle aerial anti-air |
| a4 Air Smash | late committed horizontal aerial kill read |

The evidence artifact is `docs/generated/fightguy-move-data.md`; the pose check used Ability
Lab; `tests/Shared.Tests/FightGuyNormalTuningTests.cs` pins the revised timing, geometry,
damage, and launch contracts. Its report found no true normal links at 0–150%, so the final
roles deliberately do not promise automatic combos.

### Preserve and verify an approved retune

1. Update the ability specs and role names/descriptions together; migrate factory comments,
   character documentation, and tests so old role names cannot mislead later tuning.
2. Add focused `KitScenario`/golden coverage at an active contact tick. Pin hit confirmation,
   damage, single-hit grouping, and a representative launch; use the
   `sloparena-kit-regression-testing` workflow for scoped golden generation and review.
3. Rebuild Shared, regenerate both report renderings, and run affected regression tests.
4. Request a Unity recompilation check. For Unity-facing behavior, add a short manual
   Training/Ability-Lab checklist to `TESTING-UNITY.md`: contact alignment, coverage,
   one-hit semantics, reward, commitment, and target-lock/manual-aim behavior.

## Verify / flow

1. After a Shared change, rebuild first: `dotnet build src/Shared/ --nologo -p:SkipUnityPluginCopy=true` (offline verification; refresh Unity separately under your own bounded lease).
2. `dotnet build tools/MoveDataReport/ --nologo -p:SkipUnityPluginCopy=true`.
3. Run, read the markdown, sanity-check numbers against `docs/characters/<char>.md` / the ability spec.
4. Include regenerated `docs/generated/*.md` with the behavior change when requested as an artifact. Commit only with explicit user authorization.
