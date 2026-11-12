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
compares exact output bytes, and compares the complete ordered severity/code list. Paths and messages may improve.
The captured input copies make the regression independent of future roster tuning or external proof directories.
Do not regenerate this fixture with the replacement compiler to accept a regression.
