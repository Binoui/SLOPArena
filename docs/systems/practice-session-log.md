# Practice Session Log

A local, opt-in recording of one Training or Solo CPU match, plus an offline report.
It measures how a human actually misses: by distance, height or aim. It also records
whether the opponent stayed on screen and how hard the player had to drive the camera
on keyboard and mouse or a controller. CPU Opponents see positions exactly, so their
numbers say nothing about perception; this log comes from the person playing.

It is diagnostics, not a [Replay](../../CONTEXT.md#replays--spectating). It records
results and presentation context, not a re-simulatable input stream, and it never
feeds the simulation. Nothing is uploaded.

## Recording

Turn on **Settings → Gameplay → Record practice session log**. The setting applies to
the next Training or Solo match that starts; PvP and Arcade are not recorded. Automation
can use the typed settings command:

```bash
editor sloparena.settings.apply --patch '{"recordSessionLog":true}' --dry-run false --confirm true
```

Each match writes one gzip JSON Lines file to `SessionLogs/` under the game's
persistent data folder:

| Platform | Folder |
|---|---|
| Linux (Editor and player) | `~/.config/unity3d/SLOPArena/SLOPArena/SessionLogs` |
| Windows player | `%USERPROFILE%\AppData\LocalLow\SLOPArena\SLOPArena\SessionLogs` |

Names look like `20261016-192841-training-fightguy-vs-manki.slopsession.jsonl.gz`.
An hour of play is a few megabytes. The file is flushed every five seconds and
finished when the match ends or the scene unloads; a crash leaves a readable file
without its end record.

## Contents

`src/Shared/Telemetry/SessionLog.cs` owns the schema, writer and reader. Every line is
`{"type":…,"data":…}`:

- `session` — mode, arena, fighters (package identity, capsule size, CPU difficulty and
  behavior), camera settings (field of view, orbit radius, pitch range, stick yaw rate at
  the current sensitivity, lock assistance) and input settings.
- `frame` — one per simulation tick: rendered camera pose, field of view and aspect, last
  used device, mouse and right-stick camera rotation since the previous tick, peak stick
  deflection, camera mode, and every fighter's position, velocity, facing, grounded and
  action state, lock target, damage and Hitstun.
- `swing` — the bot telemetry `SwingRecord` from `MatchRecorder`, for every fighter.
- `hit` — accepted contacts this tick, including shield blocks.
- `event` — practice-control changes: position or damage resets, damage set, CPU added,
  removed, behavior or difficulty changed, cooldowns toggled.
- `end` — clean end, tick count and reason.

### Closest approach

For each accepted activation, `MatchRecorder` measures the smallest surface gap between
its hitboxes and its reference target. The reference target is the selected target at the
press, otherwise the nearest other fighter within 12 m. It uses
`SpellResolver.LastTickCheckedHitboxes`, the hitboxes exactly as that tick tested them
(including ones that connected or expired), against that fighter's
`ServerSimulation.LastTickAttackEntities`, which is the shield sphere while it shields. The
vector from the hitbox surface to the target surface is stored in the attacker's facing
frame (forward, side, up) and in world space. Self-play bots get the same fields.

## Report

```bash
dotnet run --project tools/SessionLogReport -- [files-or-folders] [--last N] \
  [--out artifacts/session-report/index.html] [--json summary.json]
```

Without paths it reads this user's session folder. The page covers the player's swings:

- **Contact rate** — hits plus blocks over measured swings (hits, blocks and whiffs).
  Accepted swings without any tested hitbox (interrupted or cancelled) and swings with no
  target are counted separately.
- **Whiff gap and near misses** — closest surface gap; a near miss is within 0.5 m.
- **Whiff direction** — the dominant attacker-frame axis: `short` (target beyond the
  hitbox), `past` (nearer or behind), `under` (target above), `over` (target below),
  `left`/`right`. A second table splits the world gap along the camera at the press:
  screen depth, lateral and vertical.
- **Target motion** — grounded, rising, near apex or falling at the closest approach.
- **Camera angle to the target** — the angle between the camera's horizontal view and the
  attacker→target line at the press, binned 0–15°, 15–30°, 30–60° and 60–180°.
- **Camera handling** — per device: share of ticks the watched opponent (the player's
  target, otherwise the nearest fighter) was off-screen or within 10% of the screen edge,
  mouse and stick camera degrees per second, ticks with the stick at 95% or more and how
  many of those still had the opponent off-screen or at the edge, and damaging hits taken
  whose attacker was off-screen when it pressed.
- **Where whiffs missed** — top and side scatter of whiff gaps by device and air/ground.

## Limits

- Camera pose comes from the last rendered frame when the simulation tick runs.
- Training position and damage resets are recorded as events, not undone in the analysis.
- The gap is measured against the reference target only. With several CPUs, a swing that
  hits a different fighter still counts as a hit. Facing-frame vectors use the attacker's
  facing at the closest tick, after any startup aim correction.
- The report supports a hypothesis; feel and readability still need the player.
