# Four-CPU filming preview

`sloparena.showcase.*` provides an Editor-only, in-memory four-fighter preview for
filming. FightGuy, Manki, Wibou and Bonk use their own local development catalog
entries, poses, rigs, bot policies, memory and seeded RNG. The existing Shared
simulation resolves movement, hits, damage and respawns; the normal match
presentation handles animation, projectiles, impacts and sword trails.

This is a capture tool, not a new game mode or a test of online four-player play.
It does not cook packages, save scenes or change Training's NPC controls.

## Run

Use the canonical Editor and your own gateway lease under the
[Editor coordination protocol](../contributing/unity-editor-coordination.md).
Start in **stopped Edit Mode with saved scenes and a Game View open**. Recompile
through the gateway first if the C# source has changed.

Save this batch outside `Assets/`, changing the output paths for each take:

```json
{
  "commands": [
    ["sloparena.showcase.prepare", "--stage", "after_hours", "--seed", "7", "--difficulty", "Hard", "--spawn-scale", "0.4"],
    ["editor_play"],
    ["sloparena.ui.viewport", "--width", "1280", "--height", "720", "--gizmos", "off"],
    ["sloparena.showcase.run", "--ticks", "90", "--timeout", "90"],
    ["sloparena.capture.game-view", "--source", "screen", "--width", "1280", "--height", "720", "--output", "tmp/cpu-take-01/start.png"],
    ["sloparena.showcase.record", "--output", "tmp/cpu-take-01/fight.mp4", "--seconds", "18", "--timeout", "180"],
    ["get_console_logs", "--severity", "error", "--limit", "20"]
  ],
  "restore": [
    ["sloparena.showcase.stop"],
    ["sloparena.showcase.restore"]
  ]
}
```

Run from the selected game checkout:

```bash
bun scripts/unity-editor-gateway.ts --project-path client/Unity --batch tmp/cpu-take-01.json
```

Prepare uses the existing stage-view save/restore lifecycle, disables the normal
Training controller and human input in memory, and installs the temporary filming
component. Stop/restore belong in the **same bounded batch** as the preview. The
batch restores scenes, selection, Play Mode options, viewport and timing, and ends
in stopped Edit Mode. It never restarts the original Frontend automatically.

## Controls and output

- `seed` reproduces the bot RNG streams for the same content and tick sequence.
  Each fighter owns a separate policy because its cached move data belongs to one kit.
- `difficulty` uses the existing Easy, Normal or Hard profile.
- `spawn-scale` defaults to `1`, the authored arena spawns. Values `0.25`–`1`
  gather the cast towards the flat After Hours center for filming. Compact starts
  are supported only on After Hours and also determine the preview's respawn points.
- Targets remain stable during attacks/charges and for at least 120 ticks.
  Afterwards a substantially nearer opponent can be selected. Target/life changes
  clear old opponent observations and follow-up plans. Hits on a third party do not
  become a combo observation about the selected target.
- `run` advances 1–3600 normal 60 Hz simulation ticks, then freezes the fighters.
  Its result reports package identities, states, hits, target changes and input
  press counts indexed by the existing wire slot minus one.
- The camera gently follows and frames the group. Falling fighters below the
  roof are excluded from camera bounds so one fall cannot pull the shot away.
- Optional `prepare --reveal true` opens the recording on the high-resolution
  After Hours fight-night billboard. It holds for 1.4 seconds, then cranes down
  and widens into the group shot by four seconds. The poster
  ships with the stage art; `sloparena.stage.art-apply --stage after_hours` reinstalls
  it.
- Optional `prepare --captions true` adds the approved white text as an in-Editor
  preview using Archivo Black. The first seven recorded seconds have no overlay:
  four seconds for the reveal, then three seconds of clear fighting. The beats are
  F2P at 7–9 seconds, ONLINE at 9–11, OPENSOURCE at 11–13, and JANKY AS HELL at
  13–15. These options default off; character spotlights remain separate work.
- `record` requires an **already installed `ffmpeg`**. It records the actual Game
  View into a fresh MP4 under `tmp/`, at 30 fps with two 60 Hz simulation ticks per
  frame. It restores frame pacing afterwards. The MP4 is **silent** and contains no
  text overlays unless `--captions true` was selected. Camera and scene presentation
  are not a bit-identical render promise.
- Recording accepts 1–30 seconds and even Game View dimensions. Keep the Game View
  open; changing its size or pausing during a take fails the recording. Neither
  screenshots nor movies overwrite existing files.

The current nearest-opponent policy can settle into two duels. A compact start
provides a more crowded opening; capturing CPU play does not guarantee a knockout,
combo or four-way exchange at a particular moment.

## Initial verification

On 2026-10-16, the normal-spawn seed-7 run captured 14 seconds at 1280×720/30 fps
(420 frames). At tick 1020 all four packages had landed hits, with 17 accepted hits
overall. A compact `0.4` start captured 18 seconds (540 frames), reaching tick 1170
with 32 accepted hits. Both runtime checks had no Unity console errors; all four
fighters took damage. No knockout occurred in these takes, so the new runner's
respawn/retarget behavior is not established by them.

The reference footage and receipts are local artifacts under
`tmp/sloparena-intro-20261016/`. The compact take's opening is the stronger filming
candidate. The Editor was restored to stopped Frontend after the compact take.

The subsequent billboard opening is 15 seconds at 1920×1080/30 fps (450 frames),
with 25 accepted hits and no console errors. Inspection of recorded frame 207
(6.9 seconds) confirms clear gameplay; frame 210 (7.0 seconds) shows the first F2P
overlay. The high-resolution poster is legible in the initial camera hold. The
authoritative stage scene and baked arena retain their prior SHA-256 hashes.

The revised `billboard-opening-v2.mp4` fixes the poster's unintended shader
motion/pulsing: its animation/travel UV channels contain explicit zeroes and its
material clock is disabled. At held-camera frames 0, 15, 30 and 40, the poster's
mean RGB value stays between 117.1370 and 117.1412 (0–255 scale), compared with
84.5619–117.2007 before the fix. The revised take retains the 15-second duration,
450 frames, first caption at seven seconds, 25 accepted hits and zero console
errors. Source stage, arena and StageViewCommands hashes are unchanged; the
bounded capture restored stopped Frontend and released its settled lease.
