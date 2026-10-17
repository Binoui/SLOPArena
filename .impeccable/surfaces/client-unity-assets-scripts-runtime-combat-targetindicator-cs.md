---
version: 1
slug: "client-unity-assets-scripts-runtime-combat-targetindicator-cs"
primary_target: "client/Unity/Assets/Scripts/Runtime/Combat/TargetIndicator.cs"
related_targets: ["client/Unity/Assets/Resources/Shaders/FighterGroundMarker.shader", "client/Unity/Assets/Resources/Shaders/FighterDropLine.shader", "client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs", "client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs"]
---

# Fighter ground marker

Mode: Operate. Scope: fighter floor-position and airborne depth cues in native Unity gameplay. This extends the incumbent visual world.

## Approved direction — 2026-10-13

The user approved replacing the filled fighter discs with small broken rings and soft contact shadows, while preserving airborne depth perception. The direction is `.impeccable/review/ground-marker-20261013/direction.md`; its `concept.html` is an approved geometric schematic, not native gameplay evidence.

THESIS: Keep a fighter's floor position readable during jumps with a quiet, small, hollow marker.
OWN-WORLD: Paper arcs, dark edging, one short Action Yellow local notch, neutral opponent marks, and a functional soft contact shadow.
STORY: Read the current floor projection beneath the airborne fighter, then let the outline quiet on landing.
FIRST VIEWPORT: Actual gameplay-camera views at 1920×1080 and 1366×768; stage boundaries and fighter silhouettes remain readable.
FORM: Four stable broken arcs with an open centre; no expanding, rotating, pulsing, or glowing outline.

## Shipped artifact

The shader draws a 0.8904 m outer outline on a flat 1.2 m quad, replacing the prior 1.2 m filled disc. Each marker sits 0.025 m above the floor sampled from the authoritative baked ArenaHeightmap at the renderer's current X/Z. It is not a predicted landing point. Invalid or missing floor hides the marker; feet more than 0.1 m below the sampled platform also hide it.

Grounded outline alpha is 0.12. Airborne visibility uses a smooth capsule-foot-height transition from 0.05 to 0.75 m and reaches outline alpha 0.95. The shadow softens with that same transition: normalized radius 0.75–0.92, peak alpha 0.28–0.16. The outline's radius and orientation stay fixed. Soft depth serves spatial reading here, without changing the flat poster-panel language.

The local fighter receives the short yellow notch. Other fighters use neutral Paper. Training/Solo now supplies the local renderer both at setup and roster rebuild; PvP already supplied its renderers. Target-lock and aimed-destination feedback retain their own cues. Existing CharacterState and ArenaHeightmap drive presentation; no gameplay, package, collision, or Shared authority changes are introduced. A single material is shared by an indicator's quads and destroyed on teardown; the quads have no colliders. Native shader geometry requires no new raster assets or provenance.

## Final evidence and limits

`.impeccable/review/ground-marker-20261013/verification.json` records current source hashes and nine actual Unity Training captures in `native-final/`, covering FightGuy, Manki, Wibou, and Bonk, grounded/airborne/early-jump/landing/ledge views, and both native resolutions. The final verification records correct floor placement, missing-floor hiding, neutral NPC accent, material cleanup, no colliders, and zero shader/C#/runtime errors. The final finish-review disposition is ship with no material fixes. The Editor was restored to a clean stopped Frontend, with a free settled lease.

Connected PvP, packaged-player rendering, physical controls, and NPC roster rebuild are not acceptance evidence. The manual Unity follow-up checklist is in the gitignored root `TESTING-UNITY.md`.

## Drop line — 2026-10-17

Jump height and drift were still hard to read during play. Perspective-correct mockups on real Training captures compared a solid drop line, a height ruler, a growing ring, a predicted landing marker and line plus landing (`.impeccable/review/drop-line-20261017/mockups/options.html`). The user chose the drop line, dotted, and rejected the landing marker: the prediction sounded unreliable and awkward against knocked-back fighters. From four in-game variants (`variants/grid.html`: Fine, Bold, Sparse, Orange) they picked Orange.

THESIS: Tie an airborne fighter to its floor ring so height reads at a glance.
FORM: One vertical dotted line from the capsule feet to the ring centre; no prediction, arc, label or animation.

Each airborne fighter, local or not, gets a camera-facing `LineRenderer` with the `SLOPArena/Fighter Drop Line` shader. Dots are 0.05 m `#FFB03B` discs with the ring's dark rim (light core radius 0.3 of the width), one every 0.10 m counted up from the floor. Dots therefore stay put while the fighter rises, and a higher jump shows more of them. The width never drops below 4 px on screen, which widens dots on far fighters and low resolutions (0.0635 m at 1366×768 for the full-jump case). Visibility reuses the ring's airborne transition, 0.05–0.75 m of foot height. It is disabled when grounded, below 0.05 m, or wherever the ring hides for missing floor. The match passes its render camera to `Init`; `Camera.main` is only a fallback (it was null in the Training capture scene). The width, minimum pixels, spacing, core and colour are serialized fields, so they can be tuned live in the inspector. The line material is destroyed with the indicator.

`#FFB03B` is the mockup orange the user picked from the grid, not the palette Orange `#F05B35`. Over the bright After Hours poster it is weaker than the Paper or Bold variants; it reads clearly against floors.

Evidence: `.impeccable/review/drop-line-20261017/verification.json` records source hashes, five actual Unity Training captures with default settings (1920×1080 and 1366×768) and live probes. These cover the line on a frozen full jump, faint at 0.3 m, hidden past the stage edge and after landing, and on a real local jump driven through `sloparena.training.run`, with zero runtime errors and the Editor restored to Frontend. Connected PvP, packaged-player rendering, other stages and physical controls remain unverified; see `TESTING-UNITY.md`.
