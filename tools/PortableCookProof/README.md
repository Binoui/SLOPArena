# Portable cook comparison in the actual player

Run from a clean, committed feature checkout with the installed licensed Unity, dotnet, and Wine.
No tools are installed. The canonical Editor stays open and is not operated.

```sh
python3 tools/PortableCookProof/run-player-proof.py \
  --version 0.2.0-playtest.9469 \
  --local-unity-project /path/to/read-only/licensed/client/Unity
```

Choose a fresh version for each attempt. `--build-cache /path/to/release-owned-cache` optionally
reuses installed imports through the existing builder cache. Every native cook check still runs. The script generates a synthetic Tumbleweed item,
runs the .NET compiler/assembler, clones the committed feature into a throwaway build-only branch,
applies the committed `origin-pin.patch` containing only commit 9e5fb420's origin/main pin skip, injects a temporary proof hook,
and runs the isolated Win64 builder with all cook checks enabled. It then runs that player under
Wine with its actual Mono runtime. No product caller or launch option is added to the feature.
The source checkout and ignored licensed dependencies are read-only inputs to the builder.
The script captures the feature SHA at start and checks out that exact SHA in its clone.
No local-only branch or Git object is needed; the pin patch travels in this directory.

The item includes the near-midpoint decimal `1.00000005960464477539062500000001` and negative zero.
Both runtimes must produce bits `3f800001` and `00000000`. Both must assemble the same runtime JSON
and complete canonical manifest byte-for-byte from identical source, poses and schema-3 bindings.
The runner also exercises the folder adapter's Linux or Windows regular-file path and requires
the deliberate unit-4c pose-bake gate. This does not claim pose sampling or rendering is implemented.

Evidence lives in `build/portable-proof/<version>/`. `proof.json` records feature/build pins,
matching hashes and build receipts. The build's seven-package cook report remains in its evidence
folder. `mono-player.log` records actual Unity/Mono initialization, and `mono-result.txt` must be PASS.
A failed build or comparison raises an error and keeps the evidence for diagnosis. The Editor's
standalone Mono launcher is not a substitute for the player runtime and managed assembly setup.
