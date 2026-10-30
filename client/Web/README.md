# SLOPArena web demo

A browser build for getting people into the game. The real Shared simulation,
compiled to WebAssembly, runs every match. three.js draws the fighters' real
Unity models, each showing the exact animation frame the sim reports. A fighter
without an exported model falls back to a limbless capsule puppet posed from
its cooked `poses.bin`.

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
A fresh cook whose roster pin hasn't caught up yet loads by the package's own
identity with a console warning. `scripts/package.mjs` refuses to publish
such a set.

Query flags: `?stocks=N`, `?autoplay` (a CPU drives P1 in Brawl), and
`?fw=build` (loads the debug-enabled build output, about 4x slower per tick).

Automated browsers (`navigator.webdriver`: Playwright, headless Chrome) start with
sound output silenced; the audio logic still runs, so `SLOPA_AUDIO` stats stay
testable. Add `?sound=1` to hear it. Agents driving the page should also launch
Chrome with `--mute-audio`, as `scripts/drive.mjs` does.

## Fighter models

`tools/fighter_glb.py` turns a read-only Unity snapshot of each package's catalog
prefab into `assets/fighters/<package>.glb`. The snapshot is GenRA's
`snapshot_rigpack` run through the gateway's `eval_file` with every bound clip,
played at 60 Hz the way `PlayerRenderer` plays it. The glb carries the skinned
meshes, base-colour textures, weapons as hidden nodes under their bone
(`weapon:<slot>:<prefab>`), the two-form part lists, and one animation per
semantic id. Every build re-skins its own data at Unity's baked frames and fails
above 3 mm of error.

```sh
PY=artifacts/genra-20261014/.venv/bin/python
$PY client/Web/tools/fighter_glb.py all fightguy manki   # snapshot (own gateway lease) + build
$PY client/Web/tools/fighter_glb.py build nyx            # rebuild from the latest snapshot
```

Re-export a fighter after its model, rig or clips change. `app/viewer.html`
lines every exported fighter up at one clip frame for look checks, for example
`viewer.html?anim=idle&frame=30`. `?puppets` on the game page forces puppets.

## Publish

```sh
client/Web/scripts/build.sh
node client/Web/scripts/package.mjs            # writes client/Web/dist/
```

`dist/` is a static site containing the page, the trimmed runtime, the roster's
cooked packages, the stages listed in `STAGES`, and everything in `assets/`
(fighter models, stage art, audio). A match downloads only the fighters it uses.
`package.mjs` vendors three.js and the Draco decoder, so `dist/` needs no CDN.

The `web demo` workflow builds, smoke-tests and packages on every change to the
demo, the Shared sim, cooked content or arenas. On `main` it deploys `dist/` to
the Cloudflare Pages project `sloparena-play`, which playsloparena.com/demo
serves. `assets/` is not tracked: CI downloads the bundle pinned by
`client/Web/assets.lock` from the `web-assets` release. After re-exporting
anything in `assets/`, run `client/Web/scripts/assets.sh upload` and commit the
new `assets.lock`.

## Layout

- `sim/` compiles `src/Shared/**/*.cs` directly into a `Microsoft.NET.Sdk.WebAssembly`
  app, so it never touches `artifacts/Shared` or the Unity plugin copy.
  `Program.cs` is the JS bridge. `Modes.cs` holds one `MatchMode` per format.
- `app/` is the page. `main.js` boots the sim and runs the loop and menu. It keeps a
  `MODES` table that mirrors the C# modes (menu rows, HUD, per-tick reactions,
  camera). `fighter-model.js` and `fighters.js` draw fighters, `camera.js` holds
  Unity's Steady camera, and `vfx.js`, `stage-art.js` and `audio.js` own effects,
  stage art and sound.
- `scripts/smoke.mjs` runs a headless match in Node through the published WASM,
  for example `node smoke.mjs slopball slopball fightguy`.
- `scripts/drive.mjs` drives the page in headless Chrome over CDP and saves screenshots.

## Adding a mode

Add a `MatchMode` subclass in `sim/Modes.cs` and register it in `Demo.Start`.
Then add the matching `MODES` entry in `app/main.js`.
