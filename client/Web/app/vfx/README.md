# Unity effects in the Web demo

Run `node client/Web/scripts/vfx-reference.mjs --write` then `node client/Web/scripts/export-vfx.mjs` from the repository root. The exporter reads Unity YAML without opening the Editor. It uses the existing PyYAML and Pillow installations. PNGs copy without recompression unless an image exceeds 512 pixels. JPEG/TGA inputs convert to PNG. `export-coverage.json` names resized images, skipped fields, approximations and unsupported behaviours by prefab.

`effects.json` owns `{version, presentations, projectiles, materials, meshes}`. Nodes have parent indices, active flags, Unity-space `local:{p,q,s}`, behaviours and optional particle systems, meshes or trails. Parents precede children. Local component, GameObject and Transform references resolve to node indices recursively, including Nyx paper layers. Curves preserve Hermite keys; gradients preserve independent colour and alpha keys. The player bakes 65 samples per curve or gradient. A particle keeps its random interpolation factor throughout its life.

All 130 presentation IDs and 14 projectile bindings enter `shuriken.js`. Salamander currently has no presentation or projectile bindings. Unbound projectiles use the Unity engine default sphere and trail through the same player, without its point light. There are no unexported IDs requiring the generic production fallback, so that path was removed. `legacy.js` and `presets.js` are the frozen generic baseline, loaded only by the development `?legacyvfx` switch used for the requested comparisons and paired benchmark.

The player has a shared 1024-particle pool and at most 64 effect instances. Batches share texture, blend, geometry, shader/depth settings and sorting order. Particle positions remain in Unity space under the world's Z reflection. Node behaviour table entries handle Nyx paper attack-clock layers, charge-tier activation and variants, crow flight/perch wings, prop spin, visibility, dissolve emission and partner placement. Nyx's two paper materials reproduce vertex colours, palette UVs, alpha clipping, saturation, depth write and configured depth test.

Projectiles use the same graphs. The bridge exports stable activation/operation/emission identity, attached-carrier state and charged radius scaling. Removal stops the graph; `LingerAfterProjectile` children fade in place. `projectile-meshes.json` is the read-only Unity snapshot input for imported pot-lid, ring and kunai meshes. To refresh it, run `client/Web/tools/vfx-mesh-snapshot.cs` with the gateway's `eval_file --file <absolute-path> --timeout 60000` in your own bounded lease, then copy `tmp/web-vfx/projectile-mesh-snapshot.json` to this input file and rerun the exporter. It reads prefab assets and writes only the snapshot. It does not edit or refresh Unity assets.

`graphic-hit.js` translates `GraphicHitEffect` mesh construction, directional camera projection, hitstop hold and cubic expansion/fade. The procedural palette stays in the Unity project's Gamma colour space. Its classifier uses impact force ≥12, then damage ≥11 or ≥6. Appended hit-row fields carry force, direction, angle and the actual contact position. The original `stunTicks` and `ownHitPresentation` indices are preserved. Only the victim flashes white. Authored hit presentations suppress the generic graphic. Blocked hit rows skip it; one `BlockContact` semantic event places the blue ripple on the shield surface.

`knockout.js` translates `KnockoutBurst` and its shader. One bounded reusable mesh per live burst builds the star, offset ink/yellow copies, seat-coloured halftone trail and analytic paper scraps. The vertex shader reflects origins behind the camera, clamps depth, pins the burst inside the viewport and keeps its radius between 8% and 13% of screen height.

Remaining differences are explicit in the coverage file. Particle trails use bounded 32-point ribbons. Noise uses coherent value noise instead of Unity Perlin noise. Manki's charge prop has growth, shake, heat and rising marks, but lacks motion smear, tier kick and the moving cue tip. Nyx's placed hand reads its partner; the separate Unity tether rope/retract presenter is not ported. Bonk's armour/footfall/pennant and Bashie's shell/whistle rig deformation remain unsupported. Imported prop materials use the texture interpreter. Exact Unity randomness, integration, transparent particle sorting, weighted tangents, screen-size clamps and camera-velocity stretch remain outside the player.

Verification commands:

```sh
node client/Web/scripts/vfx-coverage.mjs
node client/Web/scripts/vfx-verify.mjs kit tmp/web-vfx/rollout-kit
node client/Web/scripts/vfx-verify.mjs brawl tmp/web-vfx/rollout-brawl
VFX_PORT=9351 VFX_BRAWL_FIGHTERS=Nyx,Salamander VFX_BRAWL_TICKS=1200 node client/Web/scripts/vfx-verify.mjs brawl tmp/web-vfx/rollout-nyx-salamander
node client/Web/scripts/vfx-compare.mjs before
node client/Web/scripts/vfx-compare.mjs after
python3 client/Web/scripts/vfx-board.py
node client/Web/scripts/vfx-bench.mjs paired --paired
```

Run the repo-root dev server on port 8642 first. Review cases live in `review-cases.json`; an optional `VFX_FIGHTERS` filter selects packages. Before and after use separate Chrome ports. Captures are promoted only after every expected image and `BROWSER_ERRORS 0`; failures preserve prior evidence. The board has one short page per case, with Unity, generic browser and exported browser at two matching ticks. `?vfxreview` drives deterministic C# duels on Splash Deck. It supports distance, victim damage, aim hold and victim shield options.

`?vfxbench` records page-loop CPU time, requestAnimationFrame intervals, draw counts and cumulative pool drops. The Brawl fixtures use seed 884 at 960×540. Chrome's requested headless driver uses SwiftShader. CPU timings exclude asynchronous GPU completion. Paired trials alternate the frozen generic kit and current player, check simulation progress and count browser errors. A hardware frame-budget claim needs a hardware run.
