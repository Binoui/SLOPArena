---
key: license_test
display_name: Fighter's License
purpose: tutorial
variant: static
intent: playful
---

# Fighter's License production contract

Follow the locked [design brief](license_test.design.md). The maintainer approved the
gray-box shell and asked for the art pass (2026-10-18); the art awaits their in-game
review. The [tutorial record](../tutorial.md) owns the lessons and their verification.

Collision source: `Assets/Stages/license_test/license_test.unity`. Runtime artifact:
`data/arenas/license_test.arena`. Cosmetic prefab: `Assets/Resources/Stages/license_test.prefab`
with the generated art in `Assets/Stages/license_test/Presentation/Roof/`. The gameplay
scene is `Assets/Scenes/Tutorial.unity`, entered from Home → TUTORIAL.

This is a tutorial-only stage, not offered in Stage Select or admitted for PvP. It keeps
the normal four grounded spawns: `Spawn_01` is the player start, `Spawn_02` the dummy's
place in the test ring; the others are unused. `AuthoringAids/KillPlane` sits at the
building block's base (−11), so a fall past the facade is a knockout without a long drop.
The jump-lesson heights and the knockout spot live in `TutorialCurriculum.LicenseTest`
and must move with the geometry.

## Generation and baking

The stage has no Unity code of its own. [`tools/blender/license_test`](../../../tools/blender/license_test/README.md)
holds the shell as a `SPEC` in `layout.py` and the procedural art. A shell change:
`python3 tools/blender/license_test/layout.py --unity-shell > /tmp/license_test-shell.json`,
then `sloparena.stage.shell-apply --stage license_test --from /tmp/license_test-shell.json`,
`sloparena.stage.bake` and `sloparena.stage.inspect`, then `python3 layout.py` (strict check)
and the arena tests. The art: `build.py`, then `sloparena.stage.art-apply --stage license_test
--from /tmp/license-test-generated` (see the generator README). The generated FBX, PNG and
`.mat` files and the prefab are local until the maintainer accepts the art; then they are
listed in `.gitignore` after `tools/check_asset_closure.py` and committed, so a release
build shows the tutorial's map. Until then, on a fresh checkout run `build.py` and
`art-apply`, which creates the missing prefab; without it the Tutorial runs on bare collision.

## Verification

`LicenseTestArenaTests` checks the spawns, lesson surfaces, kill plane, blocking walls,
the open east edge, wall tops out of reach and a knockout from the knockout spot for all
four fighters. The tutorial record lists the native runs.

Art pass 2026-10-18 (agent evidence): the shell was rebuilt from `layout.py` with
identical surfaces (84 triangles, same spawns and blast planes; arena tests 11/11). The
generator reports 0 clearance problems and about 80k triangles; `sloparena.stage.inspect`
finds 0 colliders, 7 materials on the two stage shaders, 2 lights and matching hashes.
Gameplay-camera orbit (`tmp/license_test-views/pass-05`) clips 2.8–8.5% (the brightest is
the white START paint) with means 101–136; Editor frame time 2.65 ms mean, p95 3.7 ms at
Ultra 1920×1080 (the roster stages measure 2.2–2.7 ms). The tutorial's lessons replayed on
the art with zero console errors (`tmp/tutorial/art/`, `art2/`).

Brief conformance: decisions 1–6 and 10–11 PASS. Decisions 7–9 PASS as revised on
2026-10-18 (broken guardrail, recessed booth and cone cage, painted wall sign); the
revision itself awaits the maintainer. Human review: maintainer, pending — the in-game
camera pass and the art, then a full tutorial playthrough.
