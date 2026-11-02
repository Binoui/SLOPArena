# ComboFinder

Headless beam search for pressure strings against the Hard CPU. Uses the admitted
cooked roster and Shared simulation. Approved fighters are `bonk`, `fightguy`,
`manki`, and `wibou`. Requires .NET 8 and repository content.

```sh
dotnet build tools/ComboFinder -c Release -p:SkipUnityPluginCopy=true
dotnet tools/ComboFinder/bin/Release/net8.0/ComboFinder.dll search \
  --attacker wibou --victim fightguy --stage splash_deck --damage 60 --out /tmp/combos
dotnet tools/ComboFinder/bin/Release/net8.0/ComboFinder.dll sweep \
  --attackers bonk,fightguy,manki,wibou --victims bonk,fightguy,manki,wibou \
  --stages splash_deck --damages 40,60 --beam 6 --max 420 --patience 3 --out /tmp/combos
dotnet tools/ComboFinder/bin/Release/net8.0/ComboFinder.dll verify /tmp/combos/<id>.json
dotnet tools/ComboFinder/bin/Release/net8.0/ComboFinder.dll cameras \
  /tmp/combos/<id>.json --orientation portrait
```

`search` defaults to seed 1, beam 30, maximum 660 ticks, patience 25 segments,
and segment length 6 ticks. `--a x,z` and `--v x,z` override the first two spawn
points with floor. `--route A,a1,g2` restricts the action vocabulary and scores
ordered progress. Route tokens are `g1` through `g3`, `a1` through `a3`,
`1`, `2`, `3`, `A`, `E`, `R`, `F`, `beeline`, and `juggle`.

Search re-simulates each branch from tick zero because CPU memory cannot be
cloned. Scores reward normals, variety, chasing, beaten swings, and a KO after
the pressure string. Gaps over 70 ticks break a free-search string. Repeated
hits from one activation count as one landed move. Export requires no hit taken,
a majority of normals in the string, specials in the approved positions, and a
KO within 120 ticks of its last hit. A special can open or finish the string,
or appear once mid-string followed by two normals.

Candidate ordering is descending score then ordinal plan string. Equivalent
outcomes retain the plan with the fewest non-idle actions, then the ordinally
first plan. Output JSON contains no timestamps or machine paths.

A failed search prints the best surviving partial and its landed moves, writes
no combo, and exits 1. Sweep skips mirrors, writes all accepted combos, and ranks
them by score. `--top N` limits the printed table only. `--seeds 1,2` adds seeds.
Other search controls also work on sweep. Sweep exits 1 when no combo qualifies.
Invalid arguments and stale combos exit 2.

`verify` replays the stored tapes rather than asking the CPU to regenerate its
inputs. It checks protocol, arena SHA-256, cooked package hashes, ticks, KO,
landed moves, pressure-string bounds, victim swings, and hits taken. Its first
mismatch includes the JSON field path. A tape change that preserves all these
observations is equivalent for this contract and does not fail verification.

`cameras` verifies the combo before printing the version 1 camera plan as JSON.
Diagnostics go to stderr. It samples heightmap sight lines to choose the clearer
side and emits `side`, `far-side`, `behind`, `wide`, and `kill`. Portrait uses
58 degrees vertically for the main views and 65.2 for the kill view. These are
the prototype's framing distances with wider portrait lenses. Visual framing
still depends on fighter size and motion and should be reviewed in the Recorder.

`Model.cs` defines the interchange records and run trace. `Content.cs` loads
content and spawn positions. `Run.cs` owns inputs and simulation. `Scoring.cs`
contains pure trace scoring. `Search.cs` owns beam search and export eligibility.
`Timeline.cs` owns tape encoding, replay verification, and timeline projection.
`Cameras.cs` plans the views. `Program.cs` handles command arguments.
