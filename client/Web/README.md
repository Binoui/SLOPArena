# SLOPArena web demo

A browser build for getting people into the game. The real Shared simulation,
compiled to WebAssembly, runs every match. three.js draws limbless capsule
fighters posed from each package's cooked `poses.bin`, so new moves show up
after a cook with no art export.

Modes:

- **Brawl** is a stock fight on a PvP stage, with P1 against one to three CPUs.
- **Slopball** is the solo Arcade challenge. You return the Launcher's serves
  over the net, chasing a combo and a best streak.

## Develop

```sh
client/Web/scripts/build.sh                    # publish the trimmed WASM sim into publish/
python3 -m http.server 8642 --bind 127.0.0.1   # from the repo root
# open http://127.0.0.1:8642/client/Web/app/index.html
```

The dev page reads `content-cooked/` and `data/arenas/` from the working tree.
A fighter whose cooked package no longer matches the roster manifest is skipped
with a console warning.

Query flags: `?stocks=N`, `?autoplay` (a CPU drives P1 in Brawl), and
`?fw=build` (loads the debug-enabled build output, about 4x slower per tick).

## Publish

```sh
client/Web/scripts/build.sh
node client/Web/scripts/package.mjs            # writes client/Web/dist/
```

`dist/` is a static site containing the page, the trimmed runtime, the roster's
cooked packages and the stages listed in `STAGES`. It is about 5 MB gzipped.
three.js loads from jsDelivr.

## Layout

- `sim/` compiles `src/Shared/**/*.cs` directly into a `Microsoft.NET.Sdk.WebAssembly`
  app, so it never touches `artifacts/Shared` or the Unity plugin copy.
  `Program.cs` is the JS bridge. `Modes.cs` holds one `MatchMode` per format.
- `app/` is the page. `main.js` keeps a `MODES` table that mirrors the C# modes
  (menu rows, HUD, per-tick reactions, camera focus).
- `scripts/smoke.mjs` runs a headless match in Node through the published WASM,
  for example `node smoke.mjs slopball slopball fightguy`.
- `scripts/drive.mjs` drives the page in headless Chrome over CDP and saves screenshots.

## Adding a mode

Add a `MatchMode` subclass in `sim/Modes.cs` and register it in `Demo.Start`.
Then add the matching `MODES` entry in `app/main.js`.
