# Replay plan: GameHost recording with an Exact Track and a View Track

**Status:** Accepted by the owner, 2026-11-01. Step 0 is done; step 1 is next.
**Decision:** [ADR-0060](../adr/0060-gamehost-replays-world-frames-and-view-track.md). It replaces
the recording and playback parts of the [Replay v1 design](2026-10-13-replay-v1-design.md).

The first user is the owner, reviewing playtest Matches for bugs and patterns. Players requesting
their own Replays come later and reuse the same file and viewer. Each step below ends in a check,
and no step starts before the previous one passes.

## Step 0. Measure the file and its costs

Run `dotnet run -c Release --project tools/ReplayBudget -p:SkipUnityPluginCopy=true`. It plays
bot Matches on the real `ServerSimulation` for two fighters (slop_pit) and four (last_stop) and
reports:

- Exact Track size per minute: as sent today, and with uncompressed frames in compressed chunks.
- View Track size per minute, from a throwaway projection in the tool.
- Per-tick View Track projection cost, chunk compression and decode cost, and restore and seek cost.

**Check.** Record the numbers in this plan, then set the per-Match byte limit, the VPS volume size
and the retention defaults (30 days or 5 GB until measured) from them.

### Step 0 results, 2026-11-01

Five 18,000-tick runs (seed 31) gave identical byte counts, and a 3,600-tick run with seed 7
landed within 10 % of them. The tool checks that every frame, chunk and seek restores to the
recorded snapshot. The runs used Normal CPU bots in stock mode, so Slopball, course and Mayhem
objects are not measured.

| Per minute of Match | 2 fighters (slop_pit) | 4 fighters (last_stop) |
|---|---|---|
| World Frames as sent today | 1.70 MB | 3.31 MB |
| Exact Track, 60-tick chunks | 0.39 MB | 0.85 MB |
| View Track, float32, 60-tick chunks | 0.17 MB | 0.36 MB |
| View Track, 16-bit quantized | 0.12 MB | 0.24 MB |

- Snapshots are 90 to 92 % of the uncompressed Exact Track. Inputs are about 5 %.
- Compressing a second of uncompressed frames together stores 22 to 26 % of today's wire bytes.
  300-tick chunks save only 3 to 4 % more and make a seek about 6 times slower.
- On the GameHost, the View Track projection costs a median 0.9 µs per tick (2 fighters) and 1.8 µs
  (4 fighters). Chunk compression costs 0.35 and 0.73 ms per second of Match. Both are small
  against the 16.7 ms tick.
- A seek (inflate one 60-tick chunk and restore one frame) takes a median 32 µs and 108 µs.

### Limits set from step 0

- **Chunks.** 60 ticks.
- **View Track numbers.** float32 in the first version. Quantizing saves about a third of the View
  Track but only about 10 % of a file, and it can arrive later as a new View Track version.
- **Per Match.** 64 MB, about 50 minutes with four fighters. A recording that reaches it ends as a
  Partial Replay.
- **VPS retention.** 30 days or 5 GB. 5 GB holds about 4,000 four-fighter Match minutes.

Rerun `tools/ReplayBudget` with `--ticks` and `--seed` to check these after a large content change.

## Step 1. Build the Replay file and player in Shared

- Define the View Track state with string move IDs, a version, and defaults for every field.
- Write the file writer (header, appended chunks with a tick index, trailer) and the reader with
  bounded decoding.
- Write the Replay player: same-build Exact Track restore, View Track decoding, seek by chunk, and
  one copy of each tick's events and hits.

**Check.** Shared tests cover these cases:

- Every restored Exact Track tick equals the recorded snapshot bytes.
- A file cut mid-chunk still opens to its last complete chunk.
- A wrong version or content hash gives a reason instead of playing.
- The first golden Replay opens and plays.

## Step 2. Record on the GameHost

- Tap `BuildWorldFrame` in `MatchInstance` and pass the bytes to a background writer.
- Finish the file on Ended, `Stop`, cancellation and limits.
- Add a durable recordings volume to the VPS deployment with the step 0 limits. This is a
  deployment change and needs the owner's go.

**Check.** A local Server test runs a real `MatchInstance` Match and confirms the file's trailer
matches the Match result. A slow writer ends the recording as a Partial Replay without delaying a tick.

## Step 3. Pull Replays and report patterns

- Write a pull tool that copies new recordings from the VPS to the owner's machine. The access path
  needs the owner's approval, because `skills/sloparena-ops` permits only two read-only VPS commands.
- Write a report tool that reads View Tracks across builds. Start with move usage and landed hits,
  KO positions and causes, and how Matches end.

**Check.** After a real playtest, the pull tool fetches that day's Replays and the report runs on them.

## Step 4. Watch Replays in Unity

- Build a View Track adapter for `PlayerRenderer`, projectiles, trails, events and hits.
- Add the Replay match: play, pause, speeds, one-tick step, a scrubbable timeline, and a fighter-follow
  camera with orbit and zoom.
- Seek restores the target, resets presentation, then replays the previous half second silently so
  effects in flight are present.
- Add an **Open Replay** list of pulled files. A matching build uses the Exact Track.

**Check.** File a Slop Factory check covering three scenarios:

- A pulled playtest Replay plays in the Editor and in a player build.
- Seeking back and forth leaves no stale effects or sounds.
- An older-build Replay plays through its View Track.

## Open questions

- **Display names.** `MatchPlayer` has no name. Resolve names from the Steam ID in the tools and
  viewer, or have Master send them to the GameHost.
- **VPS access for the pull tool.** The access path is undecided: SSH over Tailscale or a Master
  download endpoint.

## Deferred

These items are deferred:

- Master storage and players' own Replay Library.
- **Watch Replay** on Results.
- Replay Share Links.
- A browser viewer on the web demo.
- Free camera and video export.
- Recording offline Matches.
- Live Spectators.

The delivery sections of the [Replay v1 design](2026-10-13-replay-v1-design.md) remain the
starting point for the player-facing steps.
