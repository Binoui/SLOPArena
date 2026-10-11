# Knockback visual comparison

Watch before/after knockback paths from the real Shared simulation, with synchronized playback, a time scrubber, and selectors for all four characters, individual normal hits, and damage percents. Orange is before; teal is after. Ordinary views use equal horizontal and vertical distance scale. The weak-pop close-up magnifies height five times and labels that magnification.

## Open the saved viewer

From the repository root:

```bash
python3 tools/KnockbackVisuals/serve.py
```

Open **http://localhost:8765/**. Stop the server with Ctrl+C; use `--port 8766` if the default port is busy. The launcher resolves the viewer location from its own file, so it also works from another working directory.

You can also open [the standalone HTML](../../docs/generated/knockback-visuals/index.html) directly in a browser. It embeds every trajectory and requires no server, internet, Unity, or JavaScript packages. The [overview](../../docs/generated/knockback-visuals/overview.png), [animation](../../docs/generated/knockback-visuals/before-after.mp4), and [measurement report](../../docs/generated/knockback-flight-comparison.md) accompany the interactive viewer.

## Saved comparison

`comparison.json` preserves both sides of the **2026-10-11 flight-physics comparison**, including the baseline simulation hash, recording conditions, and trajectory point columns. It is a historical snapshot; editing the simulation does not silently change either side. It contains all four characters at 0%, 60%, 120%, and 200% damage before the hit, including isolated entries for multihit normals. Positions are rounded to five decimal places for visualization.

Both datasets use the same cooked move definitions. Victims are passive, with no DI, SDI, player movement, or follow-up hits; hitstop is excluded. Downward sends start above the floor. Ground contact can occur while hitstun continues, and positions stop at the end of the original report rather than extrapolating later ground carry. See the measurement report for the full method and verification.

The viewer, snapshot, template, generator, PNG, and MP4 live in normal repository paths, outside ignored artifact directories.

## Regenerate

Rebuild the standalone HTML from the saved snapshot using Python's standard library:

```bash
python3 tools/KnockbackVisuals/generate.py
```

To regenerate the PNG and MP4 too, install Matplotlib in a local virtual environment and have `ffmpeg` on your PATH:

```bash
python3 -m venv /tmp/sloparena-knockback-viz
/tmp/sloparena-knockback-viz/bin/pip install matplotlib
/tmp/sloparena-knockback-viz/bin/python tools/KnockbackVisuals/generate.py --export
```

Only the optional media export requires those dependencies. Opening or rebuilding the HTML never depends on temporary simulation reports or Python packages.

## Compare a future physics change

Capture MoveDataReport JSON for each of `fightguy`, `manki`, `wibou`, and `bonk` before and after the change. Keep the move definitions and recording conditions identical on both sides:

```bash
dotnet run --project tools/MoveDataReport -p:SkipUnityPluginCopy=true -- fightguy \
  --pcts 0,60,120,200 --out /tmp/fightguy-move-data.md \
  --json /tmp/knockback-before/fightguy.json
```

Repeat for the other characters, then capture the changed simulation into `/tmp/knockback-after/`. Import the pair into a new snapshot and viewer directory, preserving this original comparison:

```bash
python3 tools/KnockbackVisuals/generate.py \
  --before /tmp/knockback-before \
  --after-pattern '/tmp/knockback-after/{character}.json' \
  --label 'My next flight experiment' \
  --save-comparison docs/generated/next-knockback/comparison.json \
  --output docs/generated/next-knockback
```

Rebuild that viewer later with `--comparison docs/generated/next-knockback/comparison.json --output docs/generated/next-knockback`. The generator requires matching moves and percent samples on both sides. Optional PNG/MP4 export currently uses the three FightGuy examples and explanatory captions from the original change; the interactive viewer supports all captured trajectories.
