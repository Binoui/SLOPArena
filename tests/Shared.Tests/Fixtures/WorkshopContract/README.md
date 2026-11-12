# Original compiler contract corpus

`baseline.json` captures the compiler at commit `8fd127e65f97ef84a0efd2aeb5e75908dbad9023`.
It contains each complete input, cook profile, original canonical output bytes as base64 when cooking succeeded,
and the full original diagnostic list, including severity, code, path, and message.

The initial capture preceded compiler edits. The final corpus adds a warning case from an offline source archive
of that same original commit. It contains 43 cases, 11 successful outputs, and 34 distinct diagnostic codes.
Ten successful cases are the seven roster sources, the original worked example, the proof kit, and the fresh-agent knight.
The additional successful case declares an unused presentation ID and emits the original warning.
All other cases mutate the original worked example.

`ContractCorpus.Record` in the checker records this format. `WorkshopContractCorpusTests` recompiles every input,
compares exact output bytes, and compares the complete ordered severity/code list except `presentation.unused-id`. That existing warning was fixed to count nested capability cues. Paths and messages may improve.
The captured input copies make the regression independent of future roster tuning or external proof directories.
Do not regenerate this fixture with the replacement compiler to accept a regression.

## Broader contract checks

`mutation-inputs.json` freezes the seven built-ins, both original examples, and a coverage kit.
The coverage kit adds dependencies, completeTimeline, recovery hop, and charged stream.
`mutation-edges.json` freezes deterministic numeric boundaries and neighbors.
`MutationContract` visits one representative of each structural property path, distinguished by operation and capability kind.
It removes properties, inserts unknown fields, replaces JSON types, and tests numeric edges under both profiles.
Successful sources are reserialized and recompiled. The original and new compilers must agree on both initial and round-trip results. Aggregates are SHA-256 digests per fighter, profile, and mutation family.
No mutation result rows are stored.

The final matrix has 57,524 cases, 14,409 successful outputs and 14,409 round-trip comparisons.
`mutation-baseline.json` comes from origin/main at `b9276e5806a3e1c8896b558b7d2079ebb2e81f48`.
`mutation-head-baseline.json` comes from the pre-fix head `a765ef36f589702478f368fcafd10f4ac3aeed28`.
Ten malformed Manki maxCharges type cases already differ between those revisions.
Main throws InvalidOperationException; the pre-fix branch reports value.out-of-range. Two type-family digests record this known difference.
The test preserves the pre-fix branch outcomes and checks that every other contract digest matches main.
The only newly permitted change is removal of false presentation.unused-id warnings. Other diagnostic codes and order remain in the hash.

`FieldContract` walks every instance in those sources and two variants with optional phase clips omitted.
It reaches all 60 schema objects, checks 17,846 removals against main's exact outcomes and bytes,
and checks 6,077 default insertions for byte identity. Every writable documented default has a valid insertion context.
Optional fields may have coupled requirements in a particular built-in; the original compiler's exact result is compared in those contexts.
The two worked examples retain their direct required/optional removal tests.

## Reproduce a baseline in a scratch folder

Never replace the expected digests with the new compiler to accept a regression.
The helper links metadata for traversal when the original compiler predates that API. The original compiler does not read that metadata.
No Unity types, Editor, publishing cook, or new package is involved.

```sh
contract_baseline_root="$(mktemp -d)"
git archive b9276e5806a3e1c8896b558b7d2079ebb2e81f48 src/Shared | tar -x -C "$contract_baseline_root"
dotnet run --project tools/CharacterContractBaseline -p:SkipUnityPluginCopy=true -p:SharedCompilerRoot="$contract_baseline_root/src/Shared" -c Release -- mutations tests/Shared.Tests/Fixtures/WorkshopContract /tmp/original-mutations.json
dotnet run --project tools/CharacterContractBaseline -p:SkipUnityPluginCopy=true -p:SharedCompilerRoot="$contract_baseline_root/src/Shared" -c Release -- fields tests/Shared.Tests/Fixtures/WorkshopContract /tmp/original-fields.json
dotnet run --project tools/CharacterContractBaseline -p:SkipUnityPluginCopy=true -c Release -- mutations tests/Shared.Tests/Fixtures/WorkshopContract /tmp/current-mutations.json
```

The round-trip comparison preserves existing null/omission behavior. A Bonk mutation with null riseAnimationId produces different initial and reserialized bytes on both main and this branch. The test compares each result to the original compiler; it does not impose a new source self-round-trip guarantee.
