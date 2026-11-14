# Body pose clock diagnostic

This probe compares drawn joints with Shared's resolved hurtbox pose in an isolated Win64 player. It is diagnostic tooling. It changes no gameplay code or cook input.

`Unit6ModesCapture` deliberately reuses the startup hook in the retained unit 6 diagnostic player. Do not copy these scripts into a shipping build. `BodyClockScene` starts Training with both seats using the same package and starting form.

The CSV data shape is one joint sample per fighter, phase, and displayed frame. The probe covers idle, running, a synthetic grounded hard reaction with 90 hitstun ticks, and one ground normal 1. It covers the seven roster packages and both Nyx forms. The normal uses `AbilitySlots.Normal1`, wire value 3.

Compile against a retained isolated import. The import must contain the licensed dependencies and Bee response files. The compiler reads that import and this checkout. It does not open an Editor or copy the Shared plugin into a Unity project.

```bash
python3 tools/verification/body-pose-clock/compile.py \
  --worktree "$PWD" \
  --import-project <retained-isolated-project> \
  --unity-data <Unity-Editor-Data-directory> \
  --output <storage-output-directory>
```

Add `--editor` to compile the Runtime and Editor assemblies with Editor definitions. Without this flag, compile only the player Runtime assembly. An Editor assembly cannot be validated against a player Runtime assembly because Editor-only types are absent.

Copy an existing diagnostic Win64 player to a task-owned directory on storage. Replace only that copy's `SLOPArena_Data/Managed/Assembly-CSharp.dll` with the probe assembly and `SlopArena.Shared.dll` with this checkout's offline Shared build. Launch it under Proton with `-unit6ModesCapture <Windows-output-path>`. Use a seeded guest profile or complete the guest-name screen. The probe quits after writing `done.txt`. `failure.txt` means failure. An output directory ending in `30` selects a FightGuy-only 30 fps run. Other names select the full roster at 60 fps.

```bash
python3 tools/verification/body-pose-clock/summarize.py <output>/measurements.csv
```

`currentMm` measures current P1 joints against the resolved Shared frame. `proposedMm` measures the other fighter after forcing its clip to that frame at full weight. This is a renderer-to-Shared prototype, not a fixed Shared implementation. The P1 clock remains untouched. FightGuy run and hitstun PNG sequences show current motion on the left and forced Shared timing on the right.

The summary includes only samples in the named ActionState with current clip weight at least 0.999. Run includes only matching run clips, excluding the fighter running off the small Training floor. Attack includes one ground normal rather than the entire kit. The Shared resolver stores the next non-attack frame after hurtbox construction, so the probe subtracts that increment. Transition ticks and actual hit resolution can change state after hurtbox construction. This diagnostic therefore does not replace a collision-time pose receipt. `renderMinusSharedFrames` includes loop wrap discontinuities. It must not be interpreted as a global latency percentile.

`trace.csv` records the attack state after each simulation application and after each displayed frame. Multiple `apply` rows with the same render frame reveal sampling order when several FixedUpdates precede one animation evaluation.

The local test does not cover tumble, air locomotion, re-hits, hitstop, rollback correction, online packet cadence, or model swaps. Those remain required for an implementation that claims Match-wide agreement.
